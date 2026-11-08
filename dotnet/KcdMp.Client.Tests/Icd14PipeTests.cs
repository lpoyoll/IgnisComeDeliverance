// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT fix (icd14): the DLL link's and the frame processor's pure rules (Icd14Pipe.cs, IcdSideQueues.cs).
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

public class Icd14PipeTests
{
    // ---------------------------------------------------------------- item 1: size

    [Fact]
    public void Oversize_is_over_1024_except_the_sample_batch_up_to_its_own_limit()
    {
        Assert.False(PipeRules.IsOversize(PipeRules.Wo163, 1024));
        Assert.True(PipeRules.IsOversize(PipeRules.Wo163, 1025));
        Assert.True(PipeRules.IsOversize(PipeRules.Wo163, 1485));   // the field's
        Assert.False(PipeRules.IsOversize(PipeRules.NpcSamples, 4096));
        Assert.True(PipeRules.IsOversize(PipeRules.NpcSamples, 4097));
        Assert.Equal(1024, PipeRules.MaxPayload(PipeRules.SetSession));
    }

    [Fact]
    public void Batches_keep_order_and_fit_the_byte_and_count_limits()
    {
        var names = Enumerable.Range(0, 32).Select(i => Encoding.ASCII.GetBytes(new string((char)('a' + i % 26), 63))).ToList();
        var batches = PipeBatches.ByLength(names, CombatPipe.Wo173DownPollBatchBytes);
        Assert.True(batches.Count > 1);
        Assert.All(batches, b => Assert.True(1 + b.Sum(x => 1 + x.Length) <= CombatPipe.Wo173DownPollBatchBytes));
        Assert.Equal(names, batches.SelectMany(b => b).ToList());
        // with the op byte the request stays under the DLL's limit
        Assert.All(batches, b => Assert.False(PipeRules.IsOversize(PipeRules.Wo163, 1 + 1 + b.Sum(x => 1 + x.Length))));
        Assert.Equal(3, PipeBatches.ByLength([[1], [2], [3], [4], [5]], 1000, maxCount: 2).Count);
        Assert.Single(PipeBatches.ByLength([new byte[2000]], 1000));   // one too big goes alone (the guard refuses it)
        Assert.Empty(PipeBatches.ByLength([], 1000));
    }

    // ---------------------------------------------------------------- item 4: background polls

    [Fact]
    public void A_poll_is_too_old_past_its_period_or_the_1_s_default()
    {
        Assert.False(PipeRules.PollTooOld(1000, 0));
        Assert.True(PipeRules.PollTooOld(1001, 0));
        Assert.False(PipeRules.PollTooOld(250, 250));
        Assert.True(PipeRules.PollTooOld(251, 250));
        Assert.Equal(TimeSpan.FromSeconds(1), PipeRules.PollReplyDeadline);
        Assert.Equal(TimeSpan.FromSeconds(5), PipeRules.FrameReplyDeadline);
    }

    [Fact]
    public void Reads_and_status_ops_are_polls_writes_and_configs_are_not()
    {
        Assert.True(PipeRules.IsPoll(PipeRules.Ping, -1));
        Assert.True(PipeRules.IsPoll(PipeRules.ReadLocalState, -1));
        Assert.True(PipeRules.IsPoll(PipeRules.Wo163, Protocol.Wo213OpStatus));
        Assert.True(PipeRules.IsPoll(PipeRules.Wo163, 21));
        Assert.False(PipeRules.IsPoll(PipeRules.Wo163, Protocol.Wo213OpConfig));
        Assert.False(PipeRules.IsPoll(PipeRules.SetSession, -1));
        Assert.False(PipeRules.IsPoll(0x01, -1));   // ApplyDamage
        Assert.True(PipeRules.IsPoll(PipeRules.Wo139, 2));
        Assert.False(PipeRules.IsPoll(PipeRules.Wo139, 1));
    }

    // ---------------------------------------------------------------- item 2: the reconnect's replay

    [Fact]
    public void Sticky_keys_cover_the_session_configs_and_skip_everything_else()
    {
        Assert.Equal((PipeRules.SetSession, -1), PipeRules.StickyKey(PipeRules.SetSession, [1]));
        Assert.Equal((PipeRules.HitsConfig, -1), PipeRules.StickyKey(PipeRules.HitsConfig, [1, 1, 1]));   // friendly fire
        Assert.Equal((PipeRules.SetRespawn, -1), PipeRules.StickyKey(PipeRules.SetRespawn, [1]));        // the respawn guard
        Assert.Equal((PipeRules.Wo140, 1), PipeRules.StickyKey(PipeRules.Wo140, [1, 1]));                // the sleep gate
        Assert.Equal((PipeRules.Wo163, (int)Protocol.Wo213OpConfig), PipeRules.StickyKey(PipeRules.Wo163, [Protocol.Wo213OpConfig, 0]));
        Assert.Equal((PipeRules.Wo132, 13), PipeRules.StickyKey(PipeRules.Wo132, [13, 1]));
        Assert.Null(PipeRules.StickyKey(PipeRules.Wo163, [Protocol.Wo213OpStatus]));
        Assert.Null(PipeRules.StickyKey(0x01, new byte[30]));   // a damage apply
        Assert.Null(PipeRules.StickyKey(PipeRules.Wo132, []));
    }

    [Fact]
    public void Sticky_config_keeps_the_newest_payload_in_first_sent_order()
    {
        var s = new PipeStickyConfig();
        Assert.True(s.Record(PipeRules.NpcConfig, [1, 0, 0]));
        Assert.True(s.Record(PipeRules.SetSession, [1]));
        Assert.False(s.Record(PipeRules.Ping, []));
        Assert.True(s.Record(PipeRules.NpcConfig, [0, 1, 1]));
        var all = s.Snapshot();
        Assert.Equal(2, s.Count);
        Assert.Equal(PipeRules.NpcConfig, all[0].Type);
        Assert.Equal(new byte[] { 0, 1, 1 }, all[0].Payload);
        Assert.Equal(PipeRules.SetSession, all[1].Type);
    }

    [Fact]
    public void Each_request_type_has_its_reply_type()
    {
        Assert.Equal(0x83, PipeRules.ReplyTypeFor(PipeRules.Ping));
        Assert.Equal(0xAC, PipeRules.ReplyTypeFor(PipeRules.Wo163));
        Assert.Equal(0x81, PipeRules.ReplyTypeFor(PipeRules.SetSession));
        Assert.Equal(0xA4, PipeRules.ReplyTypeFor(PipeRules.Wo140));
    }

    // ---------------------------------------------------------------- MP-HEALTH area=pipe

    [Fact]
    public void Ms_window_gives_percentiles_and_starts_again()
    {
        var w = new MsWindow(8);
        foreach (var v in new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 100 }) w.Add(v);   // the ring keeps the newest 8
        var (p50, p95, max, n) = w.Take();
        Assert.Equal(8, n);
        Assert.Equal(100, max);
        Assert.Equal(6, p50);
        Assert.Equal(100, p95);
        Assert.Equal((0L, 0L, 0L, 0), w.Take());
    }

    [Fact]
    public void Pipe_health_line_has_every_key_and_resets()
    {
        var h = new PipeHealth();
        h.NoteSent(); h.NoteSent(); h.NoteReply(); h.NoteStale(); h.NoteOversizeRefused(); h.NoteTimeout(); h.NoteReconnect();
        h.NoteReplayed(7); h.NoteCallbackDepth(3); h.NoteCallbackDepth(2); h.GateWait.Add(4);
        string line = h.TakeLine(true, 2);
        Assert.StartsWith("MP-HEALTH area=pipe ", line);
        foreach (var k in new[] { "sent=2", "replies=1", "stale_dropped=1", "oversize_refused=1", "gate_wait_p50=4", "gate_wait_p95=4",
                                  "gate_wait_max=4", "reply_timeouts=1", "reconnects=1", "replayed_configs=7", "reader_cb_queue_max=3", "connected=1", "gen=2" })
            Assert.Contains(k, line);
        string next = h.TakeLine(false, 2);
        Assert.Contains("sent=0", next);
        Assert.Contains("reader_cb_queue_max=0", next);
    }

    // ---------------------------------------------------------------- item 6: which frames may be dropped at dequeue

    [Fact]
    public void Must_arrive_kinds_are_never_dropped_however_late()
    {
        foreach (int t in new int[] { Protocol.JoinRequestDown, Protocol.WorldChunkDown, Protocol.DeathDown, Protocol.PlayerDeathDown,
                                      Protocol.DamageDown, Protocol.NpcDamageDown, Protocol.QuestHostDown, Protocol.LeashDown, Protocol.LeashStateDown,
                                      Protocol.ConvDown, Protocol.LootHostDown, Protocol.ItemDropDown, Protocol.ItemClaimDown, Protocol.WorldSavedDown,
                                      Protocol.GraveAddDown, Protocol.SessionEventDown, Protocol.TimeSkipDown })
        {
            Assert.Equal(IcdFrameRules.Kind.NeverDrop, IcdFrameRules.Of(t));
            Assert.False(IcdFrameRules.DropAtDequeue(t, 60_000));
        }
    }

    [Fact]
    public void States_are_left_to_their_own_coalescing()
    {
        Assert.Equal(IcdFrameRules.Kind.State, IcdFrameRules.Of(Protocol.Ghost));
        Assert.Equal(IcdFrameRules.Kind.State, IcdFrameRules.Of(Protocol.NpcStateDown));
        Assert.False(IcdFrameRules.DropAtDequeue(Protocol.NpcStateDown, 60_000));
    }

    [Fact]
    public void Moments_are_dropped_past_the_event_stale_age_only()
    {
        Assert.Equal(IcdFrameRules.Kind.Droppable, IcdFrameRules.Of(Protocol.VoiceDown));
        Assert.False(IcdFrameRules.DropAtDequeue(Protocol.VoiceDown, Protocol.EventStaleMs));
        Assert.True(IcdFrameRules.DropAtDequeue(Protocol.VoiceDown, Protocol.EventStaleMs + 1));
        Assert.True(IcdFrameRules.DropAtDequeue(Protocol.ClockSyncDown, 5_000));
        // a combat cue: a swing is a moment, a draw or sheathe is the figure's weapon state
        Assert.True(IcdFrameRules.DropAtDequeue(Protocol.CombatEventDown, [1, Protocol.CombatEventSwing], 5_000));
        Assert.False(IcdFrameRules.DropAtDequeue(Protocol.CombatEventDown, [1, Protocol.CombatEventWeaponDrawn], 5_000));
        Assert.False(IcdFrameRules.DropAtDequeue(Protocol.CombatEventDown, [1, Protocol.CombatEventWeaponSheathed], 5_000));
    }

    // ---------------------------------------------------------------- item 7: newest per NPC

    private static byte[] Anim(params string[] names)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0); ms.WriteByte(1); ms.Write(new byte[4]); ms.WriteByte((byte)names.Length);
        foreach (var n in names) { var b = Encoding.UTF8.GetBytes(n); ms.WriteByte((byte)b.Length); ms.Write(b); ms.WriteByte(0); ms.WriteByte(0); }
        return ms.ToArray();
    }

    private static byte[] Activity(params (string Name, byte Val)[] rows)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0); ms.WriteByte(1); ms.Write(new byte[4]); ms.WriteByte(Protocol.ActivityKindNpc); ms.WriteByte((byte)rows.Length);
        foreach (var (n, v) in rows)
        {
            var b = Encoding.ASCII.GetBytes(n);
            ms.WriteByte(0); ms.WriteByte((byte)b.Length); ms.Write(b);
            var act = new byte[Protocol.ActivityBytes]; act[0] = v; ms.Write(act);
        }
        return ms.ToArray();
    }

    private static bool Never(int type, long wait) => false;
    private static long Zero(long a, long b) => 0;

    [Fact]
    public void Anim_frames_split_per_body_and_the_newest_replaces_the_waiting_one()
    {
        Assert.True(IcdLaneKeys.TrySplit(Protocol.NpcAnimDown, Anim("a", "b"), 7, out var pieces));
        Assert.Equal(2, pieces.Count);
        Assert.NotEqual(pieces[0].Key, pieces[1].Key);
        Assert.Equal(pieces[0].Group, pieces[1].Group);

        var lane = new IcdCoalescingLane();
        lane.Post(new LaneFrame(Protocol.NpcAnimDown, Anim("a", "b"), 10, 7));
        lane.Post(new LaneFrame(Protocol.NpcAnimDown, Anim("b", "c"), 20, 7));
        Assert.Equal(3, lane.Count);           // a, b (replaced in place), c
        Assert.Equal(1, lane.TakeSuperseded());
        Assert.True(lane.TryTake(out var f, 0, Never, Zero, out int dropped));
        Assert.Equal(0, dropped);
        Assert.Equal(Protocol.NpcAnimDown, f.Type);
        Assert.Equal(3, f.Payload[1 + Protocol.JoinHeaderLen]);   // one rebuilt frame with all three bodies
        Assert.False(lane.TryTake(out _, 0, Never, Zero, out _));
    }

    [Fact]
    public void Activity_rows_coalesce_per_npc_and_unparsed_frames_keep_their_order()
    {
        var lane = new IcdCoalescingLane();
        lane.Post(new LaneFrame(Protocol.ActivityHostDown, Activity(("a", 1), ("b", 1)), 10, 1));
        lane.Post(new LaneFrame(Protocol.ActivityExtraDown, [9, 9, 9], 11, 1));   // ordered, never merged
        lane.Post(new LaneFrame(Protocol.ActivityHostDown, Activity(("a", 2)), 12, 1));
        Assert.True(lane.TryTake(out var f1, 0, Never, Zero, out _));
        Assert.Equal(Protocol.ActivityHostDown, f1.Type);
        Assert.Equal(2, f1.Payload[1 + Protocol.JoinHeaderLen + 1]);
        int aRow = 1 + Protocol.JoinHeaderLen + 2;   // the first row: [peer][nameLen=1]['a'][activity...]
        Assert.Equal((byte)'a', f1.Payload[aRow + 2]);
        Assert.Equal(2, f1.Payload[aRow + 3]);        // a's newer row took the older one's place
        Assert.True(lane.TryTake(out var f2, 0, Never, Zero, out _));
        Assert.Equal(Protocol.ActivityExtraDown, f2.Type);
        Assert.False(lane.TryTake(out _, 0, Never, Zero, out _));
    }

    [Fact]
    public void A_too_old_body_is_dropped_alone_and_counted()
    {
        var lane = new IcdCoalescingLane();
        lane.Post(new LaneFrame(Protocol.NpcAnimDown, Anim("a", "b"), 10, 1));
        Assert.False(lane.TryTake(out _, 0, (t, w) => true, Zero, out int dropped));
        Assert.Equal(2, dropped);
    }

    [Fact]
    public void Switched_off_the_lane_is_a_plain_ordered_queue()
    {
        var lane = new IcdCoalescingLane { Keyed = false };
        lane.Post(new LaneFrame(Protocol.NpcAnimDown, Anim("a"), 10, 1));
        lane.Post(new LaneFrame(Protocol.NpcAnimDown, Anim("a"), 11, 1));
        Assert.Equal(2, lane.Count);
    }

    [Fact]
    public void Latest_queue_replaces_keyed_items_in_place_and_keeps_events_in_order()
    {
        var q = new LatestQueue<int>();
        Assert.False(q.Post("x", 1, 1));
        Assert.False(q.Post(null, 2, 2));
        Assert.True(q.Post("x", 3, 3));
        Assert.False(q.Post(null, 4, 4));
        var got = new List<int>();
        while (q.TryTake(out int v, out _, out _)) got.Add(v);
        Assert.Equal(new[] { 3, 2, 4 }, got);
        Assert.Equal(3, q.TakeDepthMax());
    }

    [Fact]
    public void Proc_health_line_has_each_lane_and_the_stale_drops_by_kind()
    {
        var h = new ProcHealth();
        h.NoteLane("damage", 3, 40);
        h.NoteLane("damage", 1, 10);
        h.NoteDroppedStale(Protocol.VoiceDown);
        h.NoteDroppedStale(Protocol.VoiceDown);
        h.NoteProcessorWait(1234);
        string line = h.TakeLine(["damage", "grave"], 55);
        Assert.StartsWith("MP-HEALTH area=proc ", line);
        Assert.Contains("q_damage_depth_max=3", line);
        Assert.Contains("q_damage_wait_max_ms=40", line);
        Assert.Contains("q_grave_depth_max=0", line);
        Assert.Contains("dropped_stale=2", line);
        Assert.Contains("dropped_stale_by=0x08:2", line);
        Assert.Contains("oldest_wait_ms=1234", line);
        Assert.Contains("dropped_stale=0", h.TakeLine(["damage"], 0));
    }
}
