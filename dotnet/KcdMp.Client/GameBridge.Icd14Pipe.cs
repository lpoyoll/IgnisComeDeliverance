// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;
using KcdMp.Wire;

namespace KcdMp.Client;

// KCT fix (icd14): the frame processor never waits for the DLL, and the DLL link holds.
//   side lanes (mp_proc_side_queues)   the processor's calls into the DLL pipe -- damage and deaths (0x13 / 0x31 / 0x15), mirror graves
//                                      (0x41 / 0x43 and a gone peer's clear), combat actions (0x3C's dispatch and the NPC resync burst),
//                                      combat cues (0x2D, the WO-214 pre-draw before its draw), and the join channel (WO-123's frames) --
//                                      go to a lane per kind, each in order on its own task. The processor goes on to the next frame.
//   stale drop (mp_proc_drop_stale)    a droppable event (IcdFrameRules) older than Protocol.EventStaleMs at dequeue is dropped, counted
//   newest per NPC (mp_lane_newest_per_npc)  the anim and activity lanes keep only each NPC's newest waiting state (IcdCoalescingLane)
//   the pipe's switches                mp_pipe_oversize_guard, mp_pipe_poll_age, mp_pipe_cb_lane, mp_pipe_config_replay (CombatPipe)
//   MP-HEALTH area=pipe / area=proc    every 10 s in the agent's log
public partial class GameBridge
{
    private readonly ProcHealth _procHealth = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, LatestQueue<(int Type, Func<Task> Work)>> _icdSide = new(StringComparer.Ordinal);
    private static readonly string[] IcdSideLaneNames = ["action", "combat_ev", "damage", "grave", "join"];
    private CancellationToken _icdSideCt;

    /// <summary>The side lanes' consumers, one per lane, for this relay connection.</summary>
    private void IcdStartSideLanes(CancellationToken ct)
    {
        _icdSideCt = ct;
        _icdSide.Clear();
        foreach (var name in IcdSideLaneNames)
        {
            var q = new LatestQueue<(int Type, Func<Task> Work)>();
            _icdSide[name] = q;
            _ = IcdSideLaneAsync(name, q, ct);
        }
    }

    /// <summary>
    /// The processor's DLL-bound work for this frame: on its kind's side lane (in order; the processor goes on), or awaited here when
    /// mp_proc_side_queues is off (the twin) or no lane runs. The lane runs it as this frame's call (Wo207Trace.Kind: frames first at
    /// the pipe's gate, the 5 s deadline, never a stale poll).
    /// </summary>
    private Task IcdSideAsync(string lane, int type, Func<Task> work)
    {
        if (!W207("proc_side_queues") || !_icdSide.TryGetValue(lane, out var q) || _icdSideCt.IsCancellationRequested) return work();
        q.Post(null, (type, work), Stopwatch.GetTimestamp());
        return Task.CompletedTask;
    }

    private async Task IcdSideLaneAsync(string name, LatestQueue<(int Type, Func<Task> Work)> q, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await q.WaitAsync(ct);
                while (q.TryTake(out var it, out long posted, out _))
                {
                    long wait = (Stopwatch.GetTimestamp() - posted) * 1000 / Stopwatch.Frequency;
                    _procHealth.NoteLane(name, q.Count + 1, wait);
                    Wo207Trace.Kind.Value = it.Type;
                    try { await it.Work(); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex) { Console.WriteLine($"MP-ICD14 side lane {name}: 0x{it.Type:X2} failed: {ex.GetType().Name}: {ex.Message}"); }
                    finally { Wo207Trace.Kind.Value = 0; }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>mp_proc_drop_stale: a droppable event (IcdFrameRules) that waited longer than Protocol.EventStaleMs is dropped at dequeue.</summary>
    private bool IcdDropStaleAtDequeue(int type, long arrival)
    {
        if (arrival <= 0) return false;
        long wait = (Stopwatch.GetTimestamp() - arrival) * 1000 / Stopwatch.Frequency;
        _procHealth.NoteProcessorWait(wait);
        if (!W207("proc_drop_stale") || !IcdFrameRules.DropAtDequeue(type, wait)) return false;
        _procHealth.NoteDroppedStale(type);
        Wo207Trace.Dropped(type);
        long n = Interlocked.Increment(ref _icdStaleDropped);
        if (n <= 20 || n % 200 == 0)
            Console.WriteLine($"MP-ICD14 stale dropped at dequeue: 0x{type:X2} waited {wait} ms (> {Protocol.EventStaleMs} ms) [n={n}]");
        return true;
    }
    private long _icdStaleDropped;

    // ---------------------------------------------------------------- the anim and activity lanes: newest per NPC

    private readonly IcdCoalescingLane _icdAnimCo = new(), _icdActCo = new();
    private int _icdCoStarted;

    /// <summary>A frame for the anim or activity lane: into its coalescing lane (mp_lane_newest_per_npc), else false (the ordered channel).</summary>
    private bool IcdCoalescePost(bool anim, int type, byte[] payload, long arrival, uint stamp, CancellationToken ct)
    {
        if (!W207("lane_newest_per_npc")) return false;
        if (Interlocked.CompareExchange(ref _icdCoStarted, 1, 0) == 0)
        {
            _ = IcdCoalescedLaneAsync("anim", _icdAnimCo, true, ct);
            _ = IcdCoalescedLaneAsync("activity", _icdActCo, false, ct);
        }
        (anim ? _icdAnimCo : _icdActCo).Post(new LaneFrame(type, payload, arrival, stamp));
        return true;
    }

    private async Task IcdCoalescedLaneAsync(string name, IcdCoalescingLane lane, bool anim, CancellationToken ct)
    {
        static long WaitMs(long arrival, long now) => Math.Max(0, (now - arrival) * 1000 / Stopwatch.Frequency);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await lane.WaitAsync(ct);
                while (lane.TryTake(out var lf, Stopwatch.GetTimestamp(), IcdLaneRules.TooOld, WaitMs, out int droppedOld))
                {
                    for (int i = 0; i < droppedOld; i++) IcdLaneDropped(lf.Type, lf.Type == Protocol.NpcAnimDown ? IcdLaneRules.AnimMaxAgeMs : IcdLaneRules.ActivityMaxAgeMs);
                    var f = new InFrame(lf.Type, lf.Payload, lf.Arrival, lf.Stamp);
                    if (Wo140DropSeparate(f.Type)) continue;   // WO-140: a joiner in its own world
                    if (anim && Wo206DropStale(f)) continue;   // a world write from an older world epoch
                    long wait = WaitMs(f.Arrival, Stopwatch.GetTimestamp());
                    _procHealth.NoteLane(name, lane.Count + 1, wait);
                    if (anim)
                    {
                        Interlocked.Increment(ref _icdAnimLaneFrames);
                        if (wait > Interlocked.Read(ref _icdAnimLaneMaxWaitMs)) Interlocked.Exchange(ref _icdAnimLaneMaxWaitMs, wait);
                    }
                    else
                    {
                        Interlocked.Increment(ref _w207ActLaneFrames);
                        if (wait > Interlocked.Read(ref _w207ActLaneMaxWaitMs)) Interlocked.Exchange(ref _w207ActLaneMaxWaitMs, wait);
                    }
                    if (IcdLaneRules.TooOld(f.Type, wait)) { IcdLaneDropped(f.Type, wait); continue; }   // an ordered frame (0x6F) this old
                    Wo207Trace.Kind.Value = f.Type;
                    long t0 = Stopwatch.GetTimestamp();
                    try { await Wo123OnFrameAsync(f.Type, f.Payload, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex) { Console.WriteLine($"MP-ICD14 {name} lane: a frame failed: {ex.GetType().Name}: {ex.Message}"); }
                    Wo207Trace.Frame(f.Type, wait, (Stopwatch.GetTimestamp() - t0) * 1000 / Stopwatch.Frequency);
                    Wo207Trace.Kind.Value = 0;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { Interlocked.Exchange(ref _icdCoStarted, 0); }
    }

    // ---------------------------------------------------------------- the switches

    /// <summary>The pipe's switches from their W207 names (at the start, and on every mp_* change).</summary>
    private void IcdApplyPipeSwitches()
    {
        _combat.OversizeGuard = W207("pipe_oversize_guard");
        _combat.PollAgeLimit = W207("pipe_poll_age");
        _combat.CallbackLane = W207("pipe_cb_lane");
        _combat.ReplayConfigOnReconnect = W207("pipe_config_replay");
    }

    // ---------------------------------------------------------------- MP-HEALTH every 10 s (the 4 Hz session tick)

    private void IcdHealthTick(long n)
    {
        if (n % 40 != 7) return;
        Console.WriteLine(_combat.Health.TakeLine(_combat.IsConnected, _combat.ConnectGeneration)
                          + $" cb_queue_now={_combat.CallbackQueueDepth} sticky_configs={_combat.Sticky.Count}");
        _procHealth.NoteSuperseded("anim", _icdAnimCo.TakeSuperseded());
        _procHealth.NoteSuperseded("activity", _icdActCo.TakeSuperseded());
        _procHealth.NoteLane("anim", _icdAnimCo.TakeDepthMax(), 0, ran: false);
        _procHealth.NoteLane("activity", _icdActCo.TakeDepthMax(), 0, ran: false);
        foreach (var (name, q) in _icdSide) { _procHealth.NoteLane(name, q.TakeDepthMax(), 0, ran: false); }
        Console.WriteLine(_procHealth.TakeLine(IcdSideLaneNames.Append("anim").Append("activity"), Wo202QueueLagMs()));
    }
}
