// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// KCT fix (icd14): the pure rules of the agent's half of the DLL link (CombatPipe), unit-tested.
/// <list type="bullet">
/// <item>Size: a payload over what the DLL takes for its type is never written (the DLL used to drop the whole connection for it, and
/// with it every body it writes: the field's "payload of 1485 bytes is out of range").</item>
/// <item>Polls: a background read (no relay frame behind it, not the priority path) that waited at the gate past its own period
/// (1 s by default) is dropped before it is sent -- the next one asks again; its reply deadline is 1 s, a frame's call keeps 5 s.</item>
/// <item>Sticky config: the set-and-forget switches and configs the agent pushes once per session or on a change; the DLL forgets
/// them when the pipe closes, so the agent sends the last of each again on every reconnect.</item>
/// </list>
/// </summary>
public static class PipeRules
{
    // the request types (CombatPipe's private constants, mirrored here so the rules stand alone)
    public const byte Ping = 0x03, ReadBodyState = 0x09, ReadLocalState = 0x0A, ScanNpcs = 0x0B, SetSession = 0x0C, SetRespawn = 0x0D,
                      ListGraves = 0x0F, NpcSamples = 0x10, NpcConfig = 0x13, NpcStatus = 0x14, MotionConfig = 0x16, HitsConfig = 0x18,
                      Wo121Status = 0x1B, JoinGuard = 0x1E, LeashSample = 0x1F, SetPartner = 0x20, Wo132 = 0x22, Wo137 = 0x23, Wo138 = 0x24,
                      Wo139 = 0x25, Wo140 = 0x26, Wo141 = 0x27, Wo143 = 0x28, Wo147 = 0x29, Wo151 = 0x2A, Wo163 = 0x2B;

    /// <summary>The DLL's limit for every request but the NPC sample batch (native icd14_pipe_rules.h kMaxPayload).</summary>
    public const int MaxPayloadDefault = 1024;
    /// <summary>The NPC sample batch's own limit (native kNpcSamplesMaxLen; NativeNpcCodec.MaxSamplesPayload).</summary>
    public const int MaxPayloadSamples = 4096;

    public static int MaxPayload(byte type) => type == NpcSamples ? MaxPayloadSamples : MaxPayloadDefault;
    public static bool IsOversize(byte type, int payloadLength) => payloadLength > MaxPayload(type);

    /// <summary>A background poll's age limit at the gate when the caller named no period of its own.</summary>
    public const int DefaultPollAgeMs = 1000;
    public static readonly TimeSpan FrameReplyDeadline = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan PollReplyDeadline = TimeSpan.FromSeconds(1);

    /// <summary>A poll that waited this long at the gate is history: dropped before it is sent.</summary>
    public static bool PollTooOld(long waitedMs, int periodMs) => waitedMs > (periodMs > 0 ? periodMs : DefaultPollAgeMs);

    // Read-only requests: answered from the DLL's state, no side effect a later ask would miss.
    private static readonly HashSet<byte> ReadTypes = [Ping, ReadBodyState, ReadLocalState, ScanNpcs, ListGraves, NpcStatus, Wo121Status, JoinGuard, LeashSample];
    // WO-163's multiplexed reads: every feature's status line, and the cursor polls (each asks "newer than N": a dropped one loses nothing)
    private static readonly HashSet<int> Wo163ReadOps =
    [
        10, 13, 19,                                    // WO-166 / WO-167 / WO-173 status
        Protocol.Wo178OpStatus, Protocol.Wo179OpStatus, Protocol.Wo187OpStatus, Protocol.Wo189OpStatus, Protocol.Wo190OpStatus,
        Protocol.Wo195OpStatus, Protocol.Wo194OpStatus, Protocol.Wo197OpStatus, Protocol.Wo200OpStatus, Protocol.Wo201OpStatus,
        Protocol.Wo202OpStatus, Protocol.Wo204OpStatus, Protocol.Wo208OpStatus, Protocol.Wo209OpStatus, Protocol.Wo210OpStatus,
        Protocol.Wo211OpStatus, Protocol.Wo213OpStatus, Protocol.Wo214OpStatus,
        21,                                            // WO-173 C2 down poll
        Protocol.Wo179OpPoll, Protocol.Wo194OpPoll, Protocol.Wo200OpPoll, Protocol.Wo201OpSpeechPoll, Protocol.Wo201OpVotePoll,
        Protocol.Wo201OpRangedPoll, Protocol.Wo202OpFailPoll,
    ];

    /// <summary>A read the agent's own loops ask again and again (op = the first payload byte, -1 none).</summary>
    public static bool IsPoll(byte type, int op) => ReadTypes.Contains(type)
        || (type == Wo163 && Wo163ReadOps.Contains(op))
        || (type == Wo132 && op == 6)                                   // one combat-state read
        || (type is Wo139 or Wo140 or Wo147 or Wo151 && op == 2)       // their status lines
        || (type == Wo137 && op == 5) || (type == Wo138 && op is 4 or 8);

    // ---- sticky config: (type, op) of every set-and-forget push; op -1 = the whole type
    private static readonly HashSet<byte> StickyTypes = [SetSession, SetRespawn, NpcConfig, MotionConfig, HitsConfig, SetPartner];
    private static readonly Dictionary<byte, HashSet<int>> StickyOps = new()
    {
        [Wo132] = [15, 16],                          // mp_skirmish_keep, mp_plan_in_fight (GameBridge.Wo207PushNativeSwitchAsync)
        [Wo137] = [1],
        [Wo138] = [Wo138Codec.OpConfig, Wo138Codec.OpLevers],
        [Wo139] = [1],
        [Wo140] = [1],                               // the sleep gate
        [Wo141] = [1],
        [Wo143] = [1],
        [Wo151] = [1],
        [Wo163] =
        [
            11, 14, 17, 20,                          // WO-166 / WO-167 / WO-172 / WO-173 configs
            Protocol.Wo178OpConfig, Protocol.Wo179OpConfig, Protocol.Wo187OpConfig, Protocol.Wo189OpConfig, Protocol.Wo190OpConfig,
            Protocol.Wo195OpConfig, Protocol.Wo194OpConfig, Protocol.Wo197OpConfig, Protocol.Wo200OpConfig, Protocol.Wo201OpConfig,
            Protocol.Wo202OpConfig, Protocol.Wo204OpConfig, Protocol.Wo208OpConfig, Protocol.Wo209OpConfig, Protocol.Wo210OpConfig,
            Protocol.Wo211OpConfig, Protocol.Wo213OpConfig, Protocol.Wo214OpConfig,
        ],
    };

    /// <summary>The sticky key of this request, or null when it is not a set-and-forget config.</summary>
    public static (byte Type, int Op)? StickyKey(byte type, ReadOnlySpan<byte> payload)
    {
        if (StickyTypes.Contains(type)) return (type, -1);
        if (payload.Length >= 1 && StickyOps.TryGetValue(type, out var ops) && ops.Contains(payload[0])) return (type, payload[0]);
        return null;
    }

    /// <summary>The reply frame type a request of this type is answered with (0x81 Result for the plain ones).</summary>
    public static byte ReplyTypeFor(byte type) => type switch
    {
        Ping => 0x83,
        Wo132 => 0x99, Wo137 => 0x9D, Wo138 => 0x9F, Wo139 => 0xA2, Wo140 => 0xA4, Wo141 => 0xA6, Wo143 => 0xA8, Wo147 => 0xAA,
        Wo151 => 0xAB, Wo163 => 0xAC,
        _ => 0x81,
    };
}

/// <summary>KCT fix (icd14): splitting a list request so each part fits the DLL's request size.</summary>
public static class PipeBatches
{
    /// <summary>
    /// The items in order, cut into batches whose encoded size (1 count byte + per item 1 length byte and its bytes) stays within
    /// <paramref name="maxBytes"/> and whose count stays within <paramref name="maxCount"/>. An item too big for any batch goes alone.
    /// </summary>
    public static List<List<byte[]>> ByLength(IReadOnlyList<byte[]> items, int maxBytes, int maxCount = 255)
    {
        var batches = new List<List<byte[]>>();
        var cur = new List<byte[]>();
        int size = 1;
        foreach (var it in items)
        {
            int need = 1 + it.Length;
            if (cur.Count > 0 && (size + need > maxBytes || cur.Count >= maxCount)) { batches.Add(cur); cur = []; size = 1; }
            cur.Add(it); size += need;
        }
        if (cur.Count > 0) batches.Add(cur);
        return batches;
    }
}

/// <summary>KCT fix (icd14): the last payload of each set-and-forget config, in the order each was first sent (the replay's order).</summary>
public sealed class PipeStickyConfig
{
    private readonly object _lock = new();
    private readonly List<(byte Type, int Op)> _order = [];
    private readonly Dictionary<(byte Type, int Op), byte[]> _last = [];

    /// <summary>Remembers the payload when it is a sticky config; true when it was.</summary>
    public bool Record(byte type, ReadOnlySpan<byte> payload)
    {
        if (PipeRules.StickyKey(type, payload) is not { } key) return false;
        lock (_lock)
        {
            if (!_last.ContainsKey(key)) _order.Add(key);
            _last[key] = payload.ToArray();
        }
        return true;
    }

    public int Count { get { lock (_lock) return _order.Count; } }

    /// <summary>Every remembered config, first-sent first, each with its newest payload.</summary>
    public List<(byte Type, byte[] Payload)> Snapshot()
    {
        lock (_lock) return _order.Select(k => (k.Type, _last[k])).ToList();
    }
}

/// <summary>KCT fix (icd14): a window of millisecond samples -- p50 / p95 / max for the health line; bounded (the newest kept).</summary>
public sealed class MsWindow
{
    private readonly object _lock = new();
    private readonly long[] _ring;
    private int _n, _at;
    private long _max;

    public MsWindow(int capacity = 4096) => _ring = new long[Math.Max(1, capacity)];

    public void Add(long ms)
    {
        lock (_lock)
        {
            _ring[_at] = ms;
            _at = (_at + 1) % _ring.Length;
            if (_n < _ring.Length) _n++;
            if (ms > _max) _max = ms;
        }
    }

    /// <summary>(p50, p95, max, count) of the window, which starts again.</summary>
    public (long P50, long P95, long Max, int Count) Take()
    {
        long[] v;
        long max;
        lock (_lock)
        {
            v = new long[_n];
            Array.Copy(_ring, v, _n);
            max = _max;
            _n = 0; _at = 0; _max = 0;
        }
        if (v.Length == 0) return (0, 0, 0, 0);
        Array.Sort(v);
        return (v[Pct(v.Length, 0.50)], v[Pct(v.Length, 0.95)], max, v.Length);
    }

    private static int Pct(int n, double p) => Math.Clamp((int)Math.Ceiling(p * n) - 1, 0, n - 1);
}

/// <summary>
/// KCT fix (icd14): the pipe's counters for the 10 s <c>MP-HEALTH area=pipe</c> line. Every count is the window's (it starts again with
/// each line); <c>gen</c> is the connection generation so far.
/// </summary>
public sealed class PipeHealth
{
    private long _sent, _replies, _stale, _oversize, _timeouts, _reconnects, _replayed, _cbMax, _oversizeDll;
    public MsWindow GateWait { get; } = new();

    public void NoteSent() => Interlocked.Increment(ref _sent);
    public void NoteReply() => Interlocked.Increment(ref _replies);
    public void NoteStale() => Interlocked.Increment(ref _stale);
    public void NoteOversizeRefused() => Interlocked.Increment(ref _oversize);
    public void NoteOversizeByDll() => Interlocked.Increment(ref _oversizeDll);
    public void NoteTimeout() => Interlocked.Increment(ref _timeouts);
    public void NoteReconnect() => Interlocked.Increment(ref _reconnects);
    public void NoteReplayed(int n) => Interlocked.Add(ref _replayed, n);
    public void NoteCallbackDepth(long depth)
    {
        long cur;
        while (depth > (cur = Interlocked.Read(ref _cbMax)) && Interlocked.CompareExchange(ref _cbMax, depth, cur) != cur) { }
    }

    public long StaleDropped => Interlocked.Read(ref _stale);
    public long OversizeRefused => Interlocked.Read(ref _oversize);

    /// <summary>The line, and the window starts again.</summary>
    public string TakeLine(bool connected, int generation)
    {
        var (p50, p95, max, _) = GateWait.Take();
        return string.Create(CultureInfo.InvariantCulture,
            $"MP-HEALTH area=pipe sent={Interlocked.Exchange(ref _sent, 0)} replies={Interlocked.Exchange(ref _replies, 0)} stale_dropped={Interlocked.Exchange(ref _stale, 0)} oversize_refused={Interlocked.Exchange(ref _oversize, 0)} oversize_dll={Interlocked.Exchange(ref _oversizeDll, 0)} gate_wait_p50_ms={p50} gate_wait_p95_ms={p95} gate_wait_max_ms={max} reply_timeouts={Interlocked.Exchange(ref _timeouts, 0)} reconnects={Interlocked.Exchange(ref _reconnects, 0)} replayed_configs={Interlocked.Exchange(ref _replayed, 0)} reader_cb_queue_max={Interlocked.Exchange(ref _cbMax, 0)} connected={(connected ? 1 : 0)} gen={generation}");
    }
}
