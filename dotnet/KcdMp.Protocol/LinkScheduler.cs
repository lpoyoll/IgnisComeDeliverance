// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KcdMp.Wire;

/// <summary>
/// WO-213 C.1: one body's piece of a state frame. A single-body frame (a position, an NPC state) is its own piece; a batched one (the
/// owner's NpcAnim rows, NpcOpp, WO-143's extras) is split per body and put back together at send time, so every body has its own
/// "latest wins" slot. <see cref="Prefix"/> is what the frame carries before its count byte (the source id, the join header, the
/// extras' kind); pieces of one group (type + prefix) merge into one frame.
/// </summary>
public readonly record struct StatePiece(string Slot, string Group, int Type, byte[] Prefix, byte[] Item, string Kind, string Body, bool Batched, bool Repeatable);

/// <summary>WO-213 C.1: how each state type splits into bodies (pure; LinkSchedulerTests).</summary>
public static class LinkStateCodec
{
    /// <summary>NPC-state flags said once (the swing cue 0x08, the resync): such a sample is never coalesced (KcdMp.Client.Wo147 agrees).</summary>
    public const byte TransientNpcFlags = 0x08 | Protocol.NpcStateFlagResync;

    /// <summary>KCT: is <paramref name="incoming"/> an older sample of the same body than <paramref name="stored"/>? NPC states by their
    /// seq, positions by their sender stamp; anything without an order (the batched kinds, unstamped positions) is never "older".</summary>
    public static bool IsOlder(StatePiece incoming, StatePiece stored)
    {
        if (incoming.Type != stored.Type) return false;
        if (OrderOf(incoming) is not (long a, bool wide) || OrderOf(stored) is not (long b, _)) return false;
        return wide ? unchecked((int)((uint)b - (uint)a)) > 0 && unchecked((int)((uint)b - (uint)a)) < 10_000
                    : Protocol.SeqIsNewer((ushort)b, (ushort)a);
    }

    private static (long, bool)? OrderOf(StatePiece s)
    {
        var p = s.Item;
        switch (s.Type)
        {
            case Protocol.NpcStateUp when p.Length >= 2 && p.Length == 1 + p[0] + Protocol.NpcStateFixedTail:
                return (BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(1 + p[0] + Protocol.NpcStateSeqOffset)), false);
            case Protocol.NpcStateDown when p.Length >= 3 && p.Length == 2 + p[1] + Protocol.NpcStateFixedTail:
                return (BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(2 + p[1] + Protocol.NpcStateSeqOffset)), false);
            case Protocol.Position when p.Length == Protocol.PositionPayloadLen + Protocol.SenderMsLen:
            case Protocol.Ghost when p.Length == Protocol.GhostPayloadLen + Protocol.SenderMsLen:
                return (BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(p.Length - 4)), true);
        }
        return null;
    }

    /// <summary>The pieces of a state frame (header included), or false for an event (then <paramref name="into"/> is untouched).</summary>
    public static bool Split(byte[] frame, List<StatePiece> into)
    {
        if (frame.Length < 3) return false;
        int type = frame[0];
        if (!LinkState.IsStateType(type)) return false;
        var p = frame.AsSpan(3);
        // KCT fix: a state frame that carries something said only once is an event, never "latest wins": a swing cue or a resync on an
        // NPC state, or a figure's change-gated BodyState2 block (crouch, guard, combat). Coalesced away by the next plain sample, the
        // copy's attack never started and the partner's stance waited for the 1 s heartbeat.
        switch (type)
        {
            case Protocol.NpcStateUp when p.Length >= 2 && p.Length == 1 + p[0] + Protocol.NpcStateFixedTail
                                          && (p[1 + p[0] + Protocol.NpcStateFlagsOffset] & TransientNpcFlags) != 0:
            case Protocol.NpcStateDown when p.Length >= 3 && p.Length == 2 + p[1] + Protocol.NpcStateFixedTail
                                            && (p[2 + p[1] + Protocol.NpcStateFlagsOffset] & TransientNpcFlags) != 0:
            case Protocol.Position when p.Length >= Protocol.PositionPayloadLenV8:
            case Protocol.Ghost when p.Length >= Protocol.GhostPayloadLenV8:
                return false;
        }
        switch (type)
        {
            case Protocol.Position:
                into.Add(new StatePiece("pos|self", "01", type, [], p.ToArray(), "pos", "self", false, false));
                return true;
            case Protocol.Ghost:
                if (p.Length < 1) return false;
                into.Add(new StatePiece("pos|" + p[0], "02", type, [], p.ToArray(), "pos", "kcd2mp_" + p[0], false, false));
                return true;
            case Protocol.NpcStateUp:
            {
                if (p.Length < 2 || p.Length != 1 + p[0] + Protocol.NpcStateFixedTail) return false;
                string name = Encoding.UTF8.GetString(p.Slice(1, p[0]));
                into.Add(new StatePiece("npc|" + name, "26", type, [], p.ToArray(), "npc-state", name, false, false));
                return true;
            }
            case Protocol.NpcStateDown:
            {
                if (p.Length < 3 || p.Length != 2 + p[1] + Protocol.NpcStateFixedTail) return false;
                string name = Encoding.UTF8.GetString(p.Slice(2, p[1]));
                into.Add(new StatePiece("npc|" + p[0] + "|" + name, "27", type, [], p.ToArray(), "npc-state", name, false, false));
                return true;
            }
        }
        // the batched kinds: [src (down)][target][joinId:4]([extra kind])[count] then the items
        bool down = Protocol.JoinWireFor(type) is null;
        int pre = (down ? 1 : 0) + Protocol.JoinHeaderLen;
        bool extra = type is Protocol.ActivityExtraUp or Protocol.ActivityExtraDown;
        if (extra) pre += 1;
        if (p.Length < pre + 1) return false;
        byte ek = extra ? p[pre - 1] : (byte)0;
        if (extra && ek is not (Protocol.ExtraKindHands or Protocol.ExtraKindGaits or Protocol.ExtraKindLooks or Protocol.ExtraKindVitals or Protocol.ExtraKindMount))
            return false;   // a one-shot (or a kind this does not know) is an event
        int count = p[pre];
        var prefix = p[..pre].ToArray();
        string group = type.ToString("X2", CultureInfo.InvariantCulture) + "|" + Convert.ToHexString(prefix);
        string kind = LinkKinds.Of(type, p);
        var pieces = new List<StatePiece>(count);
        int o = pre + 1;
        for (int i = 0; i < count; i++)
        {
            if (o >= p.Length) return false;
            int nl = p[o];
            if (nl == 0 || o + 1 + nl > p.Length) return false;
            string name = Encoding.UTF8.GetString(p.Slice(o + 1, nl));
            int len = ItemLen(type, ek, p, o, nl);
            if (len <= 0 || o + len > p.Length) return false;
            pieces.Add(new StatePiece(group + "|" + name, group, type, prefix, p.Slice(o, len).ToArray(), kind, name, true, true));
            o += len;
        }
        if (o != p.Length || pieces.Count == 0) return false;
        into.AddRange(pieces);
        return true;
    }

    /// <summary>One item's length (name byte included), 0 when it does not parse.</summary>
    internal static int ItemLen(int type, byte extraKind, ReadOnlySpan<byte> p, int o, int nl)
    {
        int b = o + 1 + nl;   // after the name
        switch (type)
        {
            case Protocol.NpcAnimUp: case Protocol.NpcAnimDown:          // [cm:1][nsc:1] nsc x scope
                if (b + 2 > p.Length) return 0;
                return 1 + nl + 2 + p[b + 1] * Protocol.NpcAnimScopeLen;
            case Protocol.NpcOppUp: case Protocol.NpcOppDown:            // [kind:1][ghost:1][oppLen][opp]
                if (b + 3 > p.Length) return 0;
                return 1 + nl + 3 + p[b + 2];
        }
        return extraKind switch
        {
            Protocol.ExtraKindHands => 1 + nl + 32,
            Protocol.ExtraKindGaits => 1 + nl + 2,
            Protocol.ExtraKindLooks => b + 2 <= p.Length ? 1 + nl + 2 + p[b + 1] : 0,
            Protocol.ExtraKindVitals => 1 + nl + 8,
            Protocol.ExtraKindMount => b + 1 <= p.Length ? 1 + nl + 1 + p[b] : 0,
            _ => 0,
        };
    }

    /// <summary>How many items one frame of a batched type may carry, and its payload's limit.</summary>
    public static (int Items, int Payload) Limits(int type)
    {
        bool down = Protocol.JoinWireFor(type) is null;
        int upType = down ? type - 1 : type;
        int max = Protocol.JoinWireFor(upType) is { } r ? r.Max + (down ? 1 : 0) : Protocol.FramePayloadMax;
        int items = type is Protocol.NpcAnimUp or Protocol.NpcAnimDown ? Protocol.NpcAnimMaxBodies
                  : type is Protocol.NpcOppUp or Protocol.NpcOppDown ? Protocol.NpcOppMaxBodies : Protocol.ExtraMaxRows;
        return (items, Math.Min(max, Protocol.FramePayloadMax));
    }

    /// <summary>One frame: the prefix, the count and the items (batched), or the single piece.</summary>
    public static byte[] Build(int type, byte[] prefix, IReadOnlyList<byte[]> items, bool batched)
    {
        int len = batched ? prefix.Length + 1 + items.Sum(i => i.Length) : items[0].Length;
        var f = new byte[3 + len];
        f[0] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(1), (ushort)len);
        if (!batched) { items[0].CopyTo(f, 3); return f; }
        prefix.CopyTo(f, 3);
        f[3 + prefix.Length] = (byte)items.Count;
        int o = 3 + prefix.Length + 1;
        foreach (var i in items) { i.CopyTo(f, o); o += i.Length; }
        return f;
    }
}

/// <summary>WO-213 C/D: the scheduler's settings (the switches' state; the twins turn one off).</summary>
public sealed class LinkSchedOptions
{
    /// <summary>C.1: a newer state of a body replaces its older unsent one (off: every frame in order).</summary>
    public bool LatestWins = true;
    /// <summary>C.2: state goes the path's unreliable way (set once the hello agreed and the path has one).</summary>
    public bool Unreliable;
    /// <summary>C.3: a state older than this at send time is dropped and counted (0 = no limit).</summary>
    public int AgeLimitMs = 500;
    /// <summary>C.2: an unchanged owner / pose / opponent state is stated again this often while state travels unreliably.</summary>
    public int RepeatMs = 1000;
    /// <summary>C.2: WO-143's extras (hands, gaits, looks, vitals, mounts) again this often -- each row is a native write on the joiner
    /// (WO-207 B.3), so their healing is slower than the owner's states'.</summary>
    public int RepeatExtraMs = 3000;
    /// <summary>C.2: a repeat only for a body whose NPC state came within this (it is still streamed).</summary>
    public int AliveMs = 5000;
    /// <summary>D.1: the path's measured rate minus this share is the budget (off: no budget).</summary>
    public bool Budget = true;
    public double Headroom = 0.2;
    /// <summary>D.1: state is not handed to a path that already holds this much (its own queue stays short; the backlog stays here).</summary>
    public int PendingCapBytes = 16 * 1024;
    /// <summary>B with C: the events' own lane (Steam's lane 0) is kept at most this full; the rest wait here, never dropped.</summary>
    public int EventPendingCapBytes = 64 * 1024;
    /// <summary>KCT: on a path with one lane (TCP), after this many event bytes in a row one waiting state frame goes before the next
    /// event -- a world transfer (~1.4 MB) no longer holds every body's position until it ends. 0 = events strictly first (as before).</summary>
    public int EventBurstBytes = 16 * 1024;
    /// <summary>D.2: each band's period (the urgency's yardstick): fight, 40 m, 150 m, beyond.</summary>
    public int[] BandPeriodMs = [33, 50, 100, 500];
    /// <summary>KCT fix (icd14, W213 link_event_coalesce): an event that is a body's latest state (activity rows and extras, a conversation's
    /// camera and cursor) is replaced while it waits by a newer one of the same body; it stays reliable and in order (LinkEventRules).</summary>
    public bool CoalesceEvents = true;
    /// <summary>KCT fix (icd14, W213 link_event_age): an event worthless once old (voice, camera samples, the cursor, "ms behind") is dropped
    /// at send time past its kind's age and counted; every kind that must arrive has no age.</summary>
    public bool EventAgeLimits = true;
}

/// <summary>
/// WO-213 C/D: one link's outbound order. Events (everything that is not a body's state) leave first, in order, complete -- never
/// dropped, never merged. State waits in one slot per (kind, body): a newer one replaces it (C.1); a slot whose value is older than
/// the age limit at send time is dropped and counted (C.3); among waiting slots the most overdue for its band goes first (D.2), as
/// long as the budget and the path's own queue allow (D.1) -- otherwise it waits in its slot, where the next newer value replaces it.
/// While state travels unreliably, an unchanged owner / pose / opponent state is stated again every RepeatMs (C.2).
/// Thread-safe: any thread enqueues, one writer takes.
/// </summary>
public sealed class LinkScheduler
{
    private sealed class Slot
    {
        public StatePiece P;
        public long UpdatedTs, SentTs, AliveTs;
        public bool Dirty;
        public int Band = 1;
    }

    private readonly object _lock = new();
    // KCT fix (icd14): a list (was a queue) so a waiting coalescable event can be replaced by a newer one of its body
    private sealed class Ev
    {
        public byte[] F = [];
        public long Ts, MaxAgeTicks;
        public EventPieces? C;
    }
    private readonly LinkedList<Ev> _events = new();
    private readonly Dictionary<string, LinkedListNode<Ev>> _evByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _coalescedByKind = new(StringComparer.Ordinal), _expiredByKind = new(StringComparer.Ordinal);
    private int _queueDepthMax;
    public long EventsCoalesced, EventsExpired;
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _npcSeenTs = new(StringComparer.Ordinal);   // body name -> its last NPC state (repeats' liveness)
    private readonly Dictionary<string, int> _npcBand = new(StringComparer.Ordinal);      // body name -> the band of its last NPC state
    private readonly List<StatePiece> _split = new(8);
    private long _eventBytes;
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;

    public LinkSchedOptions Options { get; }

    /// <summary>D.2: the band of a state piece (0 most urgent); null = the body's NPC state's band, else 1. Called under the lock.</summary>
    public Func<StatePiece, int?>? BandOf { get; set; }

    // counters (the MP-LINK lines)
    public long Reordered, StaleLate, StateInterleaved;
    private long _eventBytesSinceState;
    public long EventsIn, StateIn, Coalesced, StaleDropped, Repeats, StateSent, EventsSent, FramesBuilt, PendingWaits, BudgetWaits;
    private readonly List<double> _stateAgeAtSendMs = new(512), _eventAgeAtSendMs = new(512);

    public LinkScheduler(LinkSchedOptions? options = null) => Options = options ?? new LinkSchedOptions();

    public long EventBytes { get { lock (_lock) return _eventBytes; } }
    public int EventCount { get { lock (_lock) return _events.Count; } }
    public int DirtySlots { get { lock (_lock) return _slots.Values.Count(s => s.Dirty); } }

    /// <summary>One whole frame (header included), from any thread.</summary>
    public void Enqueue(byte[] frame)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            _split.Clear();
            if (!Options.LatestWins || !LinkStateCodec.Split(frame, _split))
            {
                EnqueueEvent(frame, now);
                return;
            }
            StateIn++;
            foreach (var p in _split)
            {
                if (!_slots.TryGetValue(p.Slot, out var s)) _slots[p.Slot] = s = new Slot { SentTs = now - (long)(10_000 * TicksPerMs) };
                else if (s.Dirty)
                {
                    // KCT fix: this hop can be fed from an unreliable lane, so the last to ARRIVE is not always the newest -- an older
                    // sample replacing a newer one still waiting meant the newer one was never sent (one step behind until the heartbeat)
                    if (LinkStateCodec.IsOlder(p, s.P)) { Reordered++; continue; }
                    Coalesced++;
                    Bump(_coalescedByKind, p.Kind.Replace(':', '.'));
                }
                s.P = p; s.UpdatedTs = now; s.Dirty = true; s.AliveTs = now;
                if (p.Kind == "npc-state")
                {
                    _npcSeenTs[p.Body] = now;
                    s.Band = BandOf?.Invoke(p) ?? 1;
                    _npcBand[p.Body] = s.Band;
                }
                else s.Band = BandOf?.Invoke(p) ?? (_npcBand.TryGetValue(p.Body, out int b) ? b : 1);   // the body's own NPC state's band
            }
        }
    }

    /// <summary>KCT fix (icd14): an event in; a coalescable one first takes its bodies out of an older waiting frame (under the lock).</summary>
    private void EnqueueEvent(byte[] frame, long now)
    {
        var ev = new Ev { F = frame, Ts = now };
        EventPieces? c = null;
        if (Options.CoalesceEvents || Options.EventAgeLimits) LinkEventRules.TrySplit(frame, out c);
        // a camera cut is never aged out either: the listener's view must jump there
        if (Options.EventAgeLimits && c is not { Protected: true } && LinkEventRules.MaxAgeMs(frame) is int age and > 0) ev.MaxAgeTicks = (long)(age * TicksPerMs);
        if (Options.CoalesceEvents && Options.LatestWins && c is not null)
        {
            ev.C = c;
            foreach (var it in c.Items)
                if (_evByKey.TryGetValue(it.Key, out var old) && old.Value.C is { Protected: false } && RemovePiece(old, it.Key))
                {
                    EventsCoalesced++;
                    Bump(_coalescedByKind, c.Kind);
                }
        }
        var node = _events.AddLast(ev);
        _eventBytes += frame.Length;
        EventsIn++;
        if (ev.C is { } cc) foreach (var it in cc.Items) _evByKey[it.Key] = node;
        if (_events.Count > _queueDepthMax) _queueDepthMax = _events.Count;
    }

    /// <summary>Takes one body's piece out of a waiting frame (the frame goes when it has none left).</summary>
    private bool RemovePiece(LinkedListNode<Ev> node, string key)
    {
        var ev = node.Value;
        if (ev.C is not { } c) return false;
        int i = c.Items.FindIndex(x => x.Key == key);
        if (i < 0) return false;
        c.Items.RemoveAt(i);
        if (_evByKey.TryGetValue(key, out var cur) && ReferenceEquals(cur, node)) _evByKey.Remove(key);
        if (c.Items.Count == 0)
        {
            _events.Remove(node);
            _eventBytes -= ev.F.Length;
            return true;
        }
        var nf = c.Build();
        _eventBytes += nf.Length - ev.F.Length;
        ev.F = nf;
        return true;
    }

    private Ev DequeueEvent()
    {
        var n = _events.First!;
        _events.RemoveFirst();
        var e = n.Value;
        _eventBytes -= e.F.Length;
        if (e.C is { } c) foreach (var it in c.Items) if (_evByKey.TryGetValue(it.Key, out var cur) && ReferenceEquals(cur, n)) _evByKey.Remove(it.Key);
        return e;
    }

    /// <summary>KCT fix (icd14): the waiting events at the head that are past their kind's age go (counted); the rest wait their turn.</summary>
    private void DropExpiredHead(long now)
    {
        while (_events.First is { } n && n.Value.MaxAgeTicks > 0 && now - n.Value.Ts > n.Value.MaxAgeTicks)
        {
            var e = DequeueEvent();
            EventsExpired++;
            Bump(_expiredByKind, LinkEventRules.Label(e.F));
        }
    }

    private static void Bump(Dictionary<string, long> d, string k) => d[k] = d.TryGetValue(k, out long n) ? n + 1 : 1;

    /// <summary>KCT fix (icd14): the scheduler's part of MP-HEALTH area=link (totals; queue_depth_max starts over).</summary>
    public string HealthText()
    {
        lock (_lock)
        {
            int max = _queueDepthMax;
            _queueDepthMax = _events.Count;
            return string.Create(CultureInfo.InvariantCulture,
                $"state_coalesced={_coalescedByKind.Values.Sum()} state_coalesced_by_kind={LinkEventRules.Counts(_coalescedByKind)} events_expired={EventsExpired} events_expired_by_kind={LinkEventRules.Counts(_expiredByKind)} queue_depth={_events.Count} queue_depth_max={max} older_state_reordered={Reordered}");
        }
    }

    /// <summary>
    /// The next frame to send, or false when nothing may go now. <paramref name="budgetBytes"/>: what the budget allows this moment
    /// (int.MaxValue = no budget); <paramref name="pendingBytes"/>: what the path already holds (-1 = it does not say).
    /// </summary>
    public bool TryTake(int budgetBytes, int pendingBytes, out byte[] frame, out bool isState, bool eventsHeld = false)
    {
        long now = Stopwatch.GetTimestamp();
        frame = []; isState = false;
        lock (_lock)
        {
            DropExpiredHead(now);   // KCT fix (icd14)
            bool TakeEvent(out byte[] f)
            {
                var e = DequeueEvent();
                EventsSent++;
                _eventBytesSinceState += e.F.Length;
                if (_eventAgeAtSendMs.Count < 20_000) _eventAgeAtSendMs.Add((now - e.Ts) / TicksPerMs);
                f = e.F;
                return true;
            }
            // KCT: fair interleave on a one-lane path -- after a burst of events, one waiting state frame first (falls back to the event
            // when no state can go now, so this never stalls the events)
            bool yieldToState = Options.EventBurstBytes > 0 && _eventBytesSinceState >= Options.EventBurstBytes && _slots.Values.Any(s => s.Dirty);
            if (_events.Count > 0 && !eventsHeld && !yieldToState) return TakeEvent(out frame);
            if (_slots.Count == 0) return _events.Count > 0 && !eventsHeld && TakeEvent(out frame);
            if (yieldToState) StateInterleaved++;
            // C.3 then the most overdue: (time since this body's state last went) / its band's period; a change beats a repeat
            Slot? best = null; double bestU = 0;
            long ageLimit = Options.AgeLimitMs > 0 ? (long)(Options.AgeLimitMs * TicksPerMs) : long.MaxValue;
            long repeat = (long)(Options.RepeatMs * TicksPerMs), repeatExtra = (long)(Options.RepeatExtraMs * TicksPerMs), alive = (long)(Options.AliveMs * TicksPerMs);
            bool repeats = Options.Unreliable && Options.RepeatMs > 0;
            long RepeatOf(Slot s) => s.P.Type is Protocol.ActivityExtraUp or Protocol.ActivityExtraDown ? repeatExtra : repeat;
            foreach (var s in _slots.Values)
            {
                // KCT fix: a piece that is repeated anyway may expire; one that is never repeated (a position, an NPC state) is the body's
                // only newest word -- dropped, the receiver kept an even older one (a walker's stop) until the 2 s heartbeat. It now goes
                // late, below every fresh change.
                bool late = s.Dirty && now - s.UpdatedTs > ageLimit;
                if (late && s.P.Repeatable) { s.Dirty = false; StaleDropped++; late = false; }
                bool due = s.Dirty || repeats && s.P.Repeatable && now - s.SentTs >= RepeatOf(s) && Alive(s.P.Body, now, alive);
                if (!due) continue;
                double over = (now - s.SentTs) / TicksPerMs / Period(s.Band);
                double u = s.Dirty && !late ? 1.0 + over : late ? 0.995 : Math.Min(0.99, over / 1000.0);   // a repeat never outranks a change
                if (best is null || u > bestU) { best = s; bestU = u; }
            }
            bool EventInstead(out byte[] f) { f = []; return yieldToState && _events.Count > 0 && !eventsHeld && TakeEvent(out f); }
            if (best is null) return EventInstead(out frame);
            if (pendingBytes >= 0 && pendingBytes > Options.PendingCapBytes) { PendingWaits++; return EventInstead(out frame); }
            // the group: every due piece of the same frame type and prefix, the most overdue first, up to the frame's limits
            var take = new List<Slot> { best };
            if (best.P.Batched)
            {
                var (maxItems, maxPayload) = LinkStateCodec.Limits(best.P.Type);
                int size = best.P.Prefix.Length + 1 + best.P.Item.Length;
                foreach (var s in _slots.Values
                             .Where(s => !ReferenceEquals(s, best) && s.P.Group == best.P.Group
                                         && (s.Dirty || repeats && s.P.Repeatable && now - s.SentTs >= RepeatOf(s) && Alive(s.P.Body, now, alive)))
                             .OrderByDescending(s => (now - s.SentTs) / Period(s.Band)))
                {
                    if (take.Count >= maxItems || size + s.P.Item.Length > maxPayload) break;
                    take.Add(s); size += s.P.Item.Length;
                }
            }
            var built = LinkStateCodec.Build(best.P.Type, best.P.Prefix, take.Select(s => s.P.Item).ToList(), best.P.Batched);
            if (built.Length > budgetBytes) { BudgetWaits++; return EventInstead(out frame); }
            foreach (var s in take)
            {
                if (!s.Dirty) Repeats++;
                else if (_stateAgeAtSendMs.Count < 20_000) _stateAgeAtSendMs.Add((now - s.UpdatedTs) / TicksPerMs);
                s.Dirty = false; s.SentTs = now;
            }
            StateSent++; FramesBuilt++;
            _eventBytesSinceState = 0;
            frame = built; isState = true;
            return true;
        }
    }

    private double Period(int band)
    {
        var p = Options.BandPeriodMs;
        return p[Math.Clamp(band, 0, p.Length - 1)];
    }

    private bool Alive(string body, long now, long alive) => _npcSeenTs.TryGetValue(body, out long t) && now - t <= alive;

    /// <summary>Forgets the slots of bodies whose state stopped long ago (a body that left the stream); call now and then.</summary>
    public void Prune(int idleMs = 60_000)
    {
        long now = Stopwatch.GetTimestamp(), idle = (long)(idleMs * TicksPerMs);
        lock (_lock)
        {
            foreach (var k in _slots.Where(kv => !kv.Value.Dirty && now - kv.Value.AliveTs > idle).Select(kv => kv.Key).ToList()) _slots.Remove(k);
            foreach (var k in _npcSeenTs.Where(kv => now - kv.Value > idle).Select(kv => kv.Key).ToList()) { _npcSeenTs.Remove(k); _npcBand.Remove(k); }
        }
    }

    /// <summary>The MP-LINK text of this scheduler since the last call (the windows start over).</summary>
    public string TakeText()
    {
        List<double> sa, ea;
        int ev, dirty, slots; long evb;
        lock (_lock)
        {
            sa = new List<double>(_stateAgeAtSendMs); ea = new List<double>(_eventAgeAtSendMs);
            _stateAgeAtSendMs.Clear(); _eventAgeAtSendMs.Clear();
            ev = _events.Count; evb = _eventBytes; dirty = _slots.Values.Count(s => s.Dirty); slots = _slots.Count;
        }
        double Q(List<double> v, double q) => v.Count == 0 ? 0 : ArrivalStats.Quantile(v, q);
        return string.Create(CultureInfo.InvariantCulture,
            $" sched=on unreliable={(Options.Unreliable ? 1 : 0)} events_waiting={ev} event_bytes={evb} slots={slots} dirty={dirty} event_age_p99={Q(ea, 0.99):F1} state_age_p50={Q(sa, 0.5):F1} state_age_p99={Q(sa, 0.99):F1} " +
            $"state_in={Interlocked.Read(ref StateIn)} coalesced={Interlocked.Read(ref Coalesced)} stale_dropped={Interlocked.Read(ref StaleDropped)} repeats={Interlocked.Read(ref Repeats)} state_sent={Interlocked.Read(ref StateSent)} events_sent={Interlocked.Read(ref EventsSent)} pending_waits={Interlocked.Read(ref PendingWaits)} budget_waits={Interlocked.Read(ref BudgetWaits)}");
    }
}

/// <summary>
/// WO-213 C/D: the one writer of a link: takes from the scheduler and sends on the path, events reliable, state the path's unreliable
/// way when the options say so. The budget (D.1) is a token bucket refilled from the path's own rate (Steam's send rate, the shaper's)
/// minus the headroom; a path that does not report a rate (plain TCP) has none, and its own small buffer holds the writer instead.
/// </summary>
public sealed class LinkWriter
{
    private readonly LinkScheduler _sched;
    private readonly ILinkPath _path;
    private readonly SemaphoreSlim _signal = new(0);
    private double _tokens;
    private long _lastRefill = Stopwatch.GetTimestamp();
    public Exception? Failed { get; private set; }
    public long Written, WrittenBytes;
    public int LastRate { get; private set; }

    public LinkWriter(LinkScheduler sched, ILinkPath path) { _sched = sched; _path = path; }

    /// <summary>WO-213 D.3: rewrites a frame just before it is sent (null = as it is).</summary>
    public Func<byte[], byte[]>? Transform { get; set; }

    public LinkScheduler Scheduler => _sched;
    public ILinkPath Path => _path;

    /// <summary>One frame in (any thread); the writer wakes.</summary>
    public void Enqueue(byte[] frame)
    {
        if (Failed is { } f) throw new IOException("the link's writer stopped: " + f.Message, f);
        _sched.Enqueue(frame);
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    public async Task RunAsync(CancellationToken ct, Action<byte[], bool>? onFrame = null)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _signal.WaitAsync(10, ct);
                await PumpOnceAsync(ct, onFrame);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failed = ex; throw; }
    }

    /// <summary>Sends whatever may go now (the budget and the path's queue allow); <paramref name="onFrame"/> sees each frame first.</summary>
    public async Task PumpOnceAsync(CancellationToken ct, Action<byte[], bool>? onFrame = null)
    {
        while (!ct.IsCancellationRequested)
        {
            var st = _path.Status();
            int budget = int.MaxValue;
            if (_sched.Options.Budget && st.Known && st.RateBytesPerS > 0)
            {
                LastRate = st.RateBytesPerS;
                double rate = st.RateBytesPerS * (1.0 - _sched.Options.Headroom);
                long now = Stopwatch.GetTimestamp();
                _tokens = Math.Min(Math.Max(4096, rate * 0.05), _tokens + rate * Stopwatch.GetElapsedTime(_lastRefill, now).TotalSeconds);
                _lastRefill = now;
                budget = (int)Math.Max(0, _tokens);
            }
            // B with C: a state lane of its own -- the events wait here while their lane holds enough, so the pump is never stuck in an
            // event's send and the state goes beside a burst (the world's transfer) at its lane's share instead of after it
            bool eventsHeld = _path.StateOwnLane && st.Known && st.EventPendingBytes > _sched.Options.EventPendingCapBytes;
            if (!_sched.TryTake(budget, st.Known ? st.PendingBytes : -1, out var frame, out bool isState, eventsHeld)) return;
            if (Transform is { } t) frame = t(frame);   // D.3: the link's own encoding (the relay's compact NPC states)
            if (budget != int.MaxValue && (isState || !_path.StateOwnLane)) _tokens -= frame.Length;   // a lane of its own: events spend their lane's share
            onFrame?.Invoke(frame, isState);
            await _path.SendAsync(frame, isState && _sched.Options.Unreliable, ct);
            Written++; WrittenBytes += frame.Length;
        }
    }

    public void Wake() { if (_signal.CurrentCount == 0) _signal.Release(); }
}

/// <summary>WO-213 C: a plain stream as a link path (TCP): reliable, in order, no numbers of its own.</summary>
public sealed class StreamLinkPath(Stream stream, string transport = "tcp") : ILinkPath
{
    public string Transport => transport;
    public bool CanSendUnreliable => false;
    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, bool state, CancellationToken ct) => stream.WriteAsync(frame, ct);
    public LinkPathStatus Status() => default;
}
