// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>What a batched Lua statement is, for the day its batch does not get an answer (WO-153 3).</summary>
public enum BatchKind
{
    /// <summary>An absolute state or a periodic push (a tick, a session setter, one NPC's or ghost's latest sample): a newer statement with the same key replaces it, and a stale one is never replayed.</summary>
    Level,
    /// <summary>Anything else: it runs once, in order, even if the first attempt only ran late (the mod's `KCD2MP_Once` guard).</summary>
    Edge,
    /// <summary>A relay that is stale after about a second (the talk lines): never replayed late.</summary>
    Stale,
}

/// <summary>
/// WO-153 3 (docs/WO-153-findings.md): the field's `MP-BATCH-DROP ... HttpClient.Timeout of 0.8 seconds` was a batch the
/// game did not answer while it loaded or hung. A timeout does not cancel the command: the game queued it and ran about
/// a third of them late. So a timed-out batch is neither dropped nor blindly re-sent: its statements go back in front of
/// the queue and are classified here.
/// </summary>
public static class BatchPolicy
{
    // Absolute states and periodic pushes the mod's own loops send again anyway. The rule for adding a name: calling it
    // twice with the same first argument leaves the same state as calling it once (the newer call wins).
    private static readonly HashSet<string> LevelFns = new(StringComparer.Ordinal)
    {
        "KCD2MP_UpdateGhost", "KCD2MP_SetGhostHealth", "KCD2MP_SetGhostHorse", "KCD2MP_SetGhostMenuState", "KCD2MP_SetGhostDead",
        "KCD2MP_ApplyNpcState", "KCD2MP_StartInterp", "KCD2MP_NpcSilenceAgent", "KCD2MP_NpcNativeAlive", "KCD2MP_HostOnlyLock",
        "KCD2MP_W131Tick", "KCD2MP_W134Tick", "KCD2MP_W140Session", "KCD2MP_W139Session", "KCD2MP_W148Session", "KCD2MP_W137Session",
        "KCD2MP_W138Hold", "KCD2MP_Wo133Gate", "KCD2MP_ShowPing", "KCD2MP_Wo114Busy", "KCD2MP_W143Sync", "KCD2MP_Wo121Alive",
        "KCD2MP_FriendlyFireSession", "KCD2MP_W141AvatarStance",
        "KCD2MP_W154MenuSession",   // WO-154 7b: the menu's once-a-second session push
        "KCD2MP_W207KeepAwake",     // WO-207 C.2: the session's keep-awake word, every 5 s
    };

    private static readonly Regex Head = new(@"^(?:if (KCD2MP_\w+) then )?(KCD2MP_\w+)\((.*)\)(?: end)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>(kind, key). The key is the function and its first argument; null for an Edge or a Stale statement.</summary>
    public static (BatchKind Kind, string? Key) Classify(string lua)
    {
        var m = Head.Match(lua.Trim());
        if (!m.Success) return (BatchKind.Edge, null);
        string fn = m.Groups[2].Value;
        string args = m.Groups[3].Value;
        if (m.Groups[1].Success && m.Groups[1].Value != fn) return (BatchKind.Edge, null);
        // the talk lines are stale after about a second; a native scan is replaced by the next one two seconds later
        if (fn.StartsWith("KCD2MP_W137Talk", StringComparison.Ordinal) || fn == "KCD2MP_ApplyNativeScan") return (BatchKind.Stale, null);
        // one call only: a statement that carries a second call cannot be replaced by another statement's first
        if (args.Contains("KCD2MP_", StringComparison.Ordinal)) return (BatchKind.Edge, null);
        if (!LevelFns.Contains(fn)) return (BatchKind.Edge, null);
        return (BatchKind.Level, fn + "|" + FirstArg(args));
    }

    private static string FirstArg(string args)
    {
        args = args.TrimStart();
        if (args.Length > 0 && args[0] == '"')
        {
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == '\\') { i++; continue; }
                if (args[i] == '"') return args[..(i + 1)];
            }
            return args;
        }
        int c = args.IndexOf(',');
        return (c < 0 ? args : args[..c]).Trim();
    }
}

/// <summary>
/// WO-153 3: the ordered queue behind <see cref="HttpGameTransport"/>'s batches. One statement per entry, in the order
/// the callers made them. A Level statement replaces an older one with the same key; an Edge statement is kept (and
/// kept in order) until it is delivered or expires; the whole queue is bounded. Not thread-safe: the transport guards it.
/// </summary>
public sealed class BatchQueue
{
    public sealed class Entry
    {
        public string Lua = "";          // the statement as the caller wrote it
        public string Text = "";         // what goes into the batch (wrapped; an Edge carries its once-guard)
        public BatchKind Kind;
        public string? Key;
        public string? OnceId;
        public int Encoded;              // encoded length of Text
        public long EnqueuedMs;
        public int Sends;                // how many batches carried it (a retry is a second send)
        public int Kind207;              // WO-207 B.1: the inbound kind whose handler queued it (Wo207Trace.Kind; 0 = none)
        public EdgeClass Class = EdgeClass.Normal;   // KCT fix (icd14): an Edge's time to live by what it is (LuaEdgeRules)
    }

    // KCT fix (icd14): the queue's switches (W207 lua_edge_ttl / lua_evict / lua_replay); off = the 0.63 behaviour
    /// <summary>An Edge statement expires by its class (feedback 5 s, world never, the rest 120 s) instead of 120 s for all.</summary>
    public bool EdgeTtlByClass { get; set; } = true;
    /// <summary>Over the bound: an expired or expendable Edge goes first, then an old one, and only then a Level state.</summary>
    public bool EvictExpendableFirst { get; set; } = true;
    /// <summary>A timed-out batch's Level statements go BEHIND the newer work (a newer one of the same key drops them); its Edges still go back in front.</summary>
    public bool ReplayLevelBehind { get; set; } = true;

    public int MaxEntries { get; init; } = 256;
    public long LevelTtlMs { get; init; } = 15_000;
    public long EdgeTtlMs { get; init; } = 120_000;

    private readonly LinkedList<Entry> _q = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _byKey = new(StringComparer.Ordinal);
    private int _queuedEncoded;

    public int Count => _q.Count;
    /// <summary>WO-207 B.5: the enqueue time of the oldest waiting entry a frame's handler queued and of the oldest the agent's own loops
    /// queued (-1 none) -- the console's share of "how far behind" (the caller holds the queue's lock).</summary>
    public (long Frame, long Background) OldestEnqueuedMs()
    {
        long f = -1, b = -1;
        foreach (var e in _q)
        {
            if (e.Kind207 != 0) { if (f < 0 || e.EnqueuedMs < f) f = e.EnqueuedMs; }
            else if (b < 0 || e.EnqueuedMs < b) b = e.EnqueuedMs;
        }
        return (f, b);
    }
    public int QueuedEncoded => _queuedEncoded;
    /// <summary>Level statements replaced by a newer one (queued or put back).</summary>
    public long Superseded { get; private set; }
    /// <summary>Statements that outlived their time to live undelivered.</summary>
    public long Expired { get; private set; }
    /// <summary>Statements pushed out because the queue was full (a Level one first).</summary>
    public long Overflowed { get; private set; }
    /// <summary>Stale statements dropped instead of replayed late.</summary>
    public long StaleDropped { get; private set; }
    /// <summary>Statements that went back in front of the queue after a batch got no answer.</summary>
    public long Requeued { get; private set; }
    /// <summary>KCT fix (icd14): a timed-out batch's Level statements dropped because a newer one with the same key was queued.</summary>
    public long ReplaysDroppedSuperseded { get; private set; }
    /// <summary>KCT fix (icd14): Edge statements expired, by class (index = (int)EdgeClass).</summary>
    public long[] EdgesExpiredByClass { get; } = new long[3];

    public void Add(Entry e)
    {
        if (e.Kind == BatchKind.Level && e.Key is { } k && _byKey.TryGetValue(k, out var old))
        {
            Remove(old);
            Superseded++;
        }
        var node = _q.AddLast(e);
        _queuedEncoded += e.Encoded;
        if (e.Kind == BatchKind.Level && e.Key is { } key) _byKey[key] = node;
        while (_q.Count > MaxEntries) DropOne(e.EnqueuedMs, node);
    }

    /// <summary>The oldest statements that fit one command's budget, removed from the queue (expired ones are discarded first).</summary>
    public List<Entry> TakeBatch(int budgetEncoded, long nowMs)
    {
        Purge(nowMs);
        var batch = new List<Entry>();
        int used = 0;
        while (_q.First is { } n)
        {
            var e = n.Value;
            if (batch.Count > 0 && used + e.Encoded > budgetEncoded) break;
            Remove(n);
            batch.Add(e);
            used += e.Encoded;
        }
        foreach (var e in batch) e.Sends++;
        return batch;
    }

    /// <summary>
    /// A batch got no answer: its statements go back to the FRONT in their order, so nothing newer overtakes them. A Level
    /// statement a newer one has replaced meanwhile is skipped; a Stale one is dropped.
    /// </summary>
    public void Requeue(IReadOnlyList<Entry> failed)
    {
        // KCT fix (icd14): the game usually ran a timed-out batch late. Its Level statements (a newer state replaces them anyway) go
        // behind the newer work instead of in front of it, in their order; a newer one of the same key already queued drops them.
        // Its Edges (each once-guarded) still go back in front, in order.
        var behind = ReplayLevelBehind ? new List<Entry>() : null;
        for (int i = failed.Count - 1; i >= 0; i--)
        {
            var e = failed[i];
            if (e.Kind == BatchKind.Stale) { StaleDropped++; continue; }
            if (e.Kind == BatchKind.Level && e.Key is { } k && _byKey.ContainsKey(k)) { Superseded++; ReplaysDroppedSuperseded++; continue; }
            if (behind is not null && e.Kind == BatchKind.Level) { behind.Add(e); continue; }
            var node = _q.AddFirst(e);
            _queuedEncoded += e.Encoded;
            if (e.Kind == BatchKind.Level && e.Key is { } key) _byKey[key] = node;
            Requeued++;
        }
        if (behind is not null)
            for (int i = behind.Count - 1; i >= 0; i--)
            {
                var e = behind[i];
                var node = _q.AddLast(e);
                _queuedEncoded += e.Encoded;
                if (e.Key is { } key) _byKey[key] = node;
                Requeued++;
            }
        long now = 0;
        foreach (var f in failed) now = Math.Max(now, f.EnqueuedMs);
        while (_q.Count > MaxEntries) DropOne(now, null);
    }

    /// <summary>The time to live of an entry (KCT fix (icd14): an Edge's by its class when <see cref="EdgeTtlByClass"/>).</summary>
    public long TtlOf(Entry e) =>
        e.Kind == BatchKind.Level ? LevelTtlMs : e.Kind == BatchKind.Edge && EdgeTtlByClass ? LuaEdgeRules.TtlMs(e.Class, EdgeTtlMs) : EdgeTtlMs;

    private bool IsExpired(Entry e, long nowMs) { long ttl = TtlOf(e); return ttl != long.MaxValue && nowMs - e.EnqueuedMs > ttl; }

    private void CountExpired(Entry e)
    {
        Expired++;
        if (e.Kind == BatchKind.Edge) EdgesExpiredByClass[(int)e.Class]++;
    }

    public void Clear() { _q.Clear(); _byKey.Clear(); _queuedEncoded = 0; }

    private void Remove(LinkedListNode<Entry> n)
    {
        var e = n.Value;
        _q.Remove(n);
        _queuedEncoded -= e.Encoded;
        if (e.Kind == BatchKind.Level && e.Key is { } k && _byKey.TryGetValue(k, out var cur) && ReferenceEquals(cur, n)) _byKey.Remove(k);
    }

    private void Purge(long nowMs)
    {
        for (var n = _q.First; n is not null;)
        {
            var next = n.Next;
            if (IsExpired(n.Value, nowMs)) { Remove(n); CountExpired(n.Value); }
            n = next;
        }
    }

    /// <summary>
    /// Over the bound. Off (0.63): the oldest Level statement goes (the mod's loop sends it again); only without one, the oldest Edge --
    /// so a burst of Edges pushed every position out while two-minute-old messages still waited. KCT fix (icd14), on: the first of
    /// (1) an entry past its time to live, (2) the oldest feedback Edge or Stale relay, (3) the oldest ordinary Edge older than a Level's
    /// time to live, (4) the oldest Level state that is not <paramref name="keep"/> (the newest, just added), (5) the oldest ordinary
    /// Edge, (6) the oldest of the rest.
    /// </summary>
    private void DropOne(long nowMs, LinkedListNode<Entry>? keep)
    {
        if (!EvictExpendableFirst)
        {
            for (var n = _q.First; n is not null; n = n.Next)
                if (n.Value.Kind == BatchKind.Level) { Remove(n); Overflowed++; return; }
            if (_q.First is { } f) { Remove(f); Overflowed++; }
            return;
        }
        LinkedListNode<Entry>? Find(Func<Entry, bool> pred)
        {
            for (var n = _q.First; n is not null; n = n.Next)
                if (!ReferenceEquals(n, keep) && pred(n.Value)) return n;
            return null;
        }
        var expired = Find(e => IsExpired(e, nowMs));
        if (expired is not null) { Remove(expired); CountExpired(expired.Value); return; }
        var victim = Find(e => e.Kind == BatchKind.Stale || e.Kind == BatchKind.Edge && e.Class == EdgeClass.Feedback)
                  ?? Find(e => e.Kind == BatchKind.Edge && e.Class == EdgeClass.Normal && nowMs - e.EnqueuedMs > LevelTtlMs)
                  ?? Find(e => e.Kind == BatchKind.Level)
                  ?? Find(e => e.Kind == BatchKind.Edge && e.Class == EdgeClass.Normal)
                  ?? Find(_ => true)
                  ?? _q.First;
        if (victim is not null) { Remove(victim); Overflowed++; }
    }
}
