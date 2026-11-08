// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// KCT fix (icd14): an ordered queue where an item posted with a key that is still waiting replaces that waiting item in its place
/// (the newest state of a body, never two), and an item without a key is an event: always appended, kept in order. Thread-safe.
/// </summary>
public sealed class LatestQueue<T>
{
    private sealed class Entry
    {
        public string? Key;
        public T Item = default!;
        public long PostedTs;
    }

    private readonly object _lock = new();
    private readonly LinkedList<Entry> _list = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _byKey = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _signal = new(0);
    private long _superseded, _depthMax;

    public int Count { get { lock (_lock) return _list.Count; } }
    public long Superseded => Interlocked.Read(ref _superseded);

    /// <summary>The window's deepest the queue got; the window starts again.</summary>
    public long TakeDepthMax() => Interlocked.Exchange(ref _depthMax, 0);
    public long TakeSuperseded() => Interlocked.Exchange(ref _superseded, 0);

    /// <summary>Posts an item; true when it replaced a waiting item of the same key (no new entry, no new signal).</summary>
    public bool Post(string? key, T item, long postedTs)
    {
        lock (_lock)
        {
            if (key is not null && _byKey.TryGetValue(key, out var node))
            {
                node.Value.Item = item;
                node.Value.PostedTs = postedTs;
                Interlocked.Increment(ref _superseded);
                return true;
            }
            var n = _list.AddLast(new Entry { Key = key, Item = item, PostedTs = postedTs });
            if (key is not null) _byKey[key] = n;
            if (_list.Count > Interlocked.Read(ref _depthMax)) Interlocked.Exchange(ref _depthMax, _list.Count);
        }
        _signal.Release();
        return false;
    }

    /// <summary>The head of the queue, if <paramref name="accept"/> takes it (null = any).</summary>
    public bool TryTake(out T item, out long postedTs, out string? key, Func<string?, T, bool>? accept = null)
    {
        lock (_lock)
        {
            var head = _list.First;
            if (head is null || (accept is not null && !accept(head.Value.Key, head.Value.Item)))
            {
                item = default!; postedTs = 0; key = null;
                return false;
            }
            _list.RemoveFirst();
            if (head.Value.Key is { } k) _byKey.Remove(k);
            item = head.Value.Item; postedTs = head.Value.PostedTs; key = head.Value.Key;
            return true;
        }
    }

    /// <summary>Waits until something was posted since the last wait (it may already have been taken).</summary>
    public Task WaitAsync(CancellationToken ct) => _signal.WaitAsync(ct);
}

/// <summary>KCT fix (icd14): one inbound relay frame as the side lanes see it (GameBridge.InFrame's public twin).</summary>
public readonly record struct LaneFrame(int Type, byte[] Payload, long Arrival, uint Stamp);

/// <summary>
/// KCT fix (icd14): per-body keying of the batched NPC kinds in the anim and activity lanes. NpcAnimDown (0x7B) splits like the relay's
/// own scheduler (LinkStateCodec.Split: [src][target][joinId:4][count] then [nameLen][name][cm][nsc] nsc x scope); ActivityHostDown (0x6B)
/// as [src][target][joinId:4][kind][count] then [peer][nameLen][name][activity]. ActivityExtraDown (0x6F) carries one-shots beside its
/// states and stays ordered (the lane's age drop still applies to it).
/// </summary>
public static class IcdLaneKeys
{
    public readonly record struct Piece(string Key, string Group, byte[] Prefix, byte[] Item);

    private const int DownPrefix = 1 + Protocol.JoinHeaderLen;   // [src][target][joinId:4]

    public static bool Keyable(int type) => type is Protocol.NpcAnimDown or Protocol.ActivityHostDown;

    /// <summary>How many bodies one rebuilt frame of this type may carry.</summary>
    public static int MaxItems(int type) => type == Protocol.NpcAnimDown ? Protocol.NpcAnimMaxBodies : Protocol.ActivityMaxRows;

    /// <summary>The frame's bodies, each keyed by its body; false when the kind is not keyed or the payload does not parse.</summary>
    public static bool TrySplit(int type, byte[] payload, uint stamp, out List<Piece> pieces)
    {
        pieces = [];
        if (type == Protocol.NpcAnimDown)
        {
            var frame = new byte[3 + payload.Length];
            frame[0] = (byte)type;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), (ushort)payload.Length);
            payload.CopyTo(frame, 3);
            var sp = new List<StatePiece>();
            if (!LinkStateCodec.Split(frame, sp) || sp.Count == 0) return false;
            foreach (var s in sp)
            {
                string group = s.Group + "|" + stamp.ToString(CultureInfo.InvariantCulture);
                pieces.Add(new Piece(group + "|" + s.Body, group, s.Prefix, s.Item));
            }
            return true;
        }
        if (type == Protocol.ActivityHostDown)
        {
            if (payload.Length < DownPrefix + 2) return false;
            var body = payload.AsSpan(DownPrefix);
            byte kind = body[0];
            int count = body[1], o = 2;
            if (kind is not (Protocol.ActivityKindNpc or Protocol.ActivityKindPlayer) || count < 1 || count > Protocol.ActivityMaxRows) return false;
            var prefix = payload.AsSpan(0, DownPrefix + 1).ToArray();   // ... and the kind
            string group = type.ToString("X2", CultureInfo.InvariantCulture) + "|" + Convert.ToHexString(prefix) + "|" + stamp.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < count; i++)
            {
                if (o + 2 > body.Length) return false;
                byte peer = body[o]; int nl = body[o + 1];
                int len = 2 + nl + Protocol.ActivityBytes;
                if (nl > Protocol.MaxNpcNameLen || o + len > body.Length) return false;
                string name = Encoding.ASCII.GetString(body.Slice(o + 2, nl));
                pieces.Add(new Piece(group + "|" + peer.ToString(CultureInfo.InvariantCulture) + "|" + name, group, prefix, body.Slice(o, len).ToArray()));
                o += len;
            }
            return o == body.Length;
        }
        return false;
    }

    /// <summary>One frame's payload from a group's prefix and its bodies: [prefix][count][bodies].</summary>
    public static byte[] Build(byte[] prefix, IReadOnlyList<byte[]> items)
    {
        var p = new byte[prefix.Length + 1 + items.Sum(i => i.Length)];
        prefix.CopyTo(p, 0);
        p[prefix.Length] = (byte)items.Count;
        int o = prefix.Length + 1;
        foreach (var i in items) { i.CopyTo(p, o); o += i.Length; }
        return p;
    }
}

/// <summary>
/// KCT fix (icd14): a side lane that keeps only the newest pending state of each NPC (lane_newest_per_npc): a keyed kind's frame is split
/// into its bodies, each replacing that body's waiting one in place; at the head, the waiting bodies of one group are put back into one
/// frame (up to the kind's limit), each body too old dropped alone. Anything else waits in order as it came.
/// </summary>
public sealed class IcdCoalescingLane
{
    private abstract record Entry(long Arrival, uint Stamp);
    private sealed record FrameEntry(LaneFrame Frame) : Entry(Frame.Arrival, Frame.Stamp);
    private sealed record PieceEntry(int Type, IcdLaneKeys.Piece Piece, long Arrival, uint Stamp) : Entry(Arrival, Stamp);

    private readonly LatestQueue<Entry> _q = new();
    public bool Keyed { get; set; } = true;

    public int Count => _q.Count;
    public long TakeDepthMax() => _q.TakeDepthMax();
    public long TakeSuperseded() => _q.TakeSuperseded();
    public Task WaitAsync(CancellationToken ct) => _q.WaitAsync(ct);

    public void Post(LaneFrame f)
    {
        if (Keyed && IcdLaneKeys.Keyable(f.Type) && IcdLaneKeys.TrySplit(f.Type, f.Payload, f.Stamp, out var pieces))
        {
            foreach (var p in pieces) _q.Post(p.Key, new PieceEntry(f.Type, p, f.Arrival, f.Stamp), f.Arrival);
            return;
        }
        _q.Post(null, new FrameEntry(f), f.Arrival);
    }

    /// <summary>
    /// The next frame to process: an ordered frame as it came, or one group's waiting bodies rebuilt (each older than
    /// <paramref name="tooOld"/> says dropped alone and counted in <paramref name="droppedOld"/>). False when nothing is waiting.
    /// </summary>
    public bool TryTake(out LaneFrame frame, long nowTs, Func<int, long, bool> tooOld, Func<long, long, long> waitMs, out int droppedOld)
    {
        droppedOld = 0;
        while (_q.TryTake(out var e, out _, out _))
        {
            if (e is FrameEntry fe) { frame = fe.Frame; return true; }
            var first = (PieceEntry)e;
            var batch = new List<PieceEntry>();
            if (!tooOld(first.Type, waitMs(first.Arrival, nowTs))) batch.Add(first); else droppedOld++;
            int max = IcdLaneKeys.MaxItems(first.Type);
            string group = first.Piece.Group;
            while (batch.Count < max && _q.TryTake(out var next, out _, out _, (_, it) => it is PieceEntry pe && pe.Piece.Group == group))
            {
                var pe = (PieceEntry)next;
                if (tooOld(pe.Type, waitMs(pe.Arrival, nowTs))) { droppedOld++; continue; }
                batch.Add(pe);
            }
            if (batch.Count == 0) continue;
            long arrival = batch.Min(b => b.Arrival);
            frame = new LaneFrame(first.Type, IcdLaneKeys.Build(first.Piece.Prefix, batch.Select(b => b.Piece.Item).ToList()), arrival, first.Stamp);
            return true;
        }
        frame = default;
        return false;
    }
}

/// <summary>
/// KCT fix (icd14): which inbound kinds the frame processor may drop at dequeue once they waited longer than Protocol.EventStaleMs
/// (mp_proc_drop_stale). States are never dropped here (each already waits once per body: WO-202 III.1's coalescing); the kinds that
/// must always land never are: joins and every other join-channel kind (quests, world epochs, loot, leash, conversations, sleep votes,
/// verdicts...), deaths and damage, loads and saves, names, sessions, pauses, graves, respawns, the combat role, versions, disconnects.
/// What is left is a moment whose use has passed: a combat event, a voice frame, a ping's answer, a clock sample, an action.
/// </summary>
public static class IcdFrameRules
{
    public enum Kind { State, NeverDrop, Droppable }

    private static readonly HashSet<int> StateKinds =
    [
        Protocol.Ghost, Protocol.NpcStateDown, Protocol.NpcStateQDown, Protocol.PlayerStateDown, Protocol.WeatherDown,
        Protocol.HorseInfoDown, Protocol.AppearanceDown, Protocol.NpcAnimDown, Protocol.NpcOppDown,
    ];

    private static readonly HashSet<int> NeverDropCore =
    [
        Protocol.Name, Protocol.Disconnect, Protocol.SessionEventDown, Protocol.DamageDown, Protocol.DeathDown, Protocol.PauseDown,
        Protocol.PlayerHitDown, Protocol.PlayerDeathDown, Protocol.TimeSkipDown, Protocol.NpcDamageDown, Protocol.ItemDropDown,
        Protocol.ItemClaimDown, Protocol.StoryBeatDown, Protocol.PlayerRespawnedDown, Protocol.GraveAddDown, Protocol.GraveRemoveDown,
        Protocol.PlayerHitV8Down, Protocol.WorldSavedDown, Protocol.CombatRole, Protocol.ReleaseVersion, Protocol.ReleaseVersionMismatch,
    ];

    private static readonly bool[] JoinChannelDown = BuildJoinChannel();

    private static bool[] BuildJoinChannel()
    {
        var a = new bool[256];
        foreach (var r in Protocol.JoinWire) a[r.Down] = true;
        return a;
    }

    public static Kind Of(int type)
    {
        if (StateKinds.Contains(type)) return Kind.State;
        if (NeverDropCore.Contains(type) || (type is >= 0 and <= 255 && JoinChannelDown[type])) return Kind.NeverDrop;
        return Kind.Droppable;
    }

    /// <summary>
    /// The kind of this frame: as <see cref="Of(int)"/>, except a combat cue's draw or sheathe (0x2D event 0 / 1) -- the figure's
    /// weapon state, never a moment: a lost one leaves the figure armed or bare until the next.
    /// </summary>
    public static Kind Of(int type, ReadOnlySpan<byte> payload)
    {
        if (type == Protocol.CombatEventDown && payload.Length >= 2
            && payload[1] is Protocol.CombatEventWeaponDrawn or Protocol.CombatEventWeaponSheathed) return Kind.NeverDrop;
        return Of(type);
    }

    /// <summary>Dropped at dequeue: a droppable kind that waited longer than <paramref name="staleMs"/>.</summary>
    public static bool DropAtDequeue(int type, long waitedMs, int staleMs = Protocol.EventStaleMs) =>
        waitedMs > staleMs && Of(type) == Kind.Droppable;

    /// <summary>Dropped at dequeue: this frame is droppable (<see cref="Of(int, ReadOnlySpan{byte})"/>) and waited longer than <paramref name="staleMs"/>.</summary>
    public static bool DropAtDequeue(int type, ReadOnlySpan<byte> payload, long waitedMs, int staleMs = Protocol.EventStaleMs) =>
        waitedMs > staleMs && Of(type, payload) == Kind.Droppable;
}

/// <summary>KCT fix (icd14): the side lanes' and the processor's counters for the 10 s <c>MP-HEALTH area=proc</c> line.</summary>
public sealed class ProcHealth
{
    private readonly ConcurrentDictionary<string, (long DepthMax, long WaitMax, long Superseded, long Ran)> _lanes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, long> _droppedStale = new();
    private long _oldestWaitMs;

    public void NoteLane(string lane, long depth, long waitMs, bool superseded = false, bool ran = true) =>
        _lanes.AddOrUpdate(lane, _ => (depth, waitMs, superseded ? 1 : 0, ran ? 1 : 0),
            (_, v) => (Math.Max(v.DepthMax, depth), Math.Max(v.WaitMax, waitMs), v.Superseded + (superseded ? 1 : 0), v.Ran + (ran ? 1 : 0)));

    public void NoteSuperseded(string lane, long n)
    {
        if (n > 0) _lanes.AddOrUpdate(lane, _ => (0, 0, n, 0), (_, v) => (v.DepthMax, v.WaitMax, v.Superseded + n, v.Ran));
    }

    public void NoteDroppedStale(int type) => _droppedStale.AddOrUpdate(type, 1, (_, n) => n + 1);

    public void NoteProcessorWait(long waitMs)
    {
        long cur;
        while (waitMs > (cur = Interlocked.Read(ref _oldestWaitMs)) && Interlocked.CompareExchange(ref _oldestWaitMs, waitMs, cur) != cur) { }
    }

    /// <summary>The line, and the window starts again (lanes that ran nothing still say 0 when listed in <paramref name="lanes"/>).</summary>
    public string TakeLine(IEnumerable<string> lanes, long processorLagMs)
    {
        var sb = new StringBuilder("MP-HEALTH area=proc");
        foreach (var l in lanes.Concat(_lanes.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            _lanes.TryRemove(l, out var v);
            sb.Append(CultureInfo.InvariantCulture, $" q_{l}_depth_max={v.DepthMax} q_{l}_wait_max_ms={v.WaitMax} q_{l}_superseded={v.Superseded} q_{l}_ran={v.Ran}");
        }
        long total = 0;
        var by = new List<string>();
        foreach (var k in _droppedStale.Keys.OrderBy(x => x))
        {
            if (!_droppedStale.TryRemove(k, out long n) || n == 0) continue;
            total += n;
            by.Add(string.Create(CultureInfo.InvariantCulture, $"0x{k:X2}:{n}"));
        }
        sb.Append(CultureInfo.InvariantCulture, $" dropped_stale={total} dropped_stale_by={(by.Count == 0 ? "-" : string.Join(',', by))}");
        sb.Append(CultureInfo.InvariantCulture, $" oldest_wait_ms={Interlocked.Exchange(ref _oldestWaitMs, 0)} proc_lag_ms={processorLagMs}");
        return sb.ToString();
    }
}
