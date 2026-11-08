// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

/// <summary>
/// KCT fix (icd14): the pure rules of the host-authoritative world pushes (GameBridge.Icd14.cs). The switches, the quest push's buckets,
/// texts and its allowlist, the clock follower's decision and the area snapshot's retry rule.
/// </summary>
public static class Icd14Rules
{
    // ---------------------------------------------------------------- switches
    /// <summary>The icd14 switches (Lua mp_icd_*; the agent's names). Every one ships on except corpse_revive (see DefaultOn).</summary>
    public static readonly string[] SwitchNames = ["quest_push", "clock_follow", "crime_ledger", "area_snapshot", "corpse_revive"];

    /// <summary>corpse_revive is OFF by default: the engine's actor:Revive on a world NPC's corpse is only observed for a knockout
    /// (WO-135), never for a death; a revived NPC's brain may come back wrong. Everything else ships on.</summary>
    public static bool DefaultOn(string name) => name != "corpse_revive";

    public const string HealthHead = "MP-HEALTH area=world";

    // ---------------------------------------------------------------- 1. the quest push
    public const int QuestPushEveryS = 30;
    public const int QuestBuckets = 128;
    public const int MaxWantBuckets = 12;
    public const int MaxCorrectionsPerRound = 16;
    /// <summary>A difference is written only when the same (host, joiner) pair was already seen this long before (an earlier round):
    /// a change the event mirror is still carrying settles by itself.</summary>
    public const double StableAfterS = 20.0;
    /// <summary>A want / entries round older than this is dropped (the host's answer came too late to describe "now").</summary>
    public const double RoundMaxAgeS = 15.0;
    public const int EntriesTextMax = 1300;

    public static int QBucketOf(string key) => (int)(Wo206Digest.Fnv1a64(key) % QuestBuckets);

    /// <summary>The 128 bucket sums (the low 32 bits of the wrapping sum of the entries' mixes, as the WO-206 digest's buckets).</summary>
    public static uint[] QBuckets(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var s = new ulong[QuestBuckets];
        foreach (var kv in entries) s[QBucketOf(kv.Key)] = unchecked(s[QBucketOf(kv.Key)] + Wo206Digest.Mix(kv.Key, kv.Value));
        var r = new uint[QuestBuckets];
        for (int i = 0; i < QuestBuckets; i++) r[i] = (uint)(s[i] & 0xFFFFFFFFUL);
        return r;
    }

    public static List<int> DiffBuckets(uint[] host, uint[] mine)
    {
        var d = new List<int>();
        for (int i = 0; i < QuestBuckets && i < host.Length && i < mine.Length; i++) if (host[i] != mine[i]) d.Add(i);
        return d;
    }

    public static string PushText(uint world, uint join, int seq, int n, uint[] b)
    {
        var sb = new StringBuilder(64 + QuestBuckets * 8);
        sb.Append(CultureInfo.InvariantCulture, $"v1|{world}|{join}|{seq}|{n}|");
        foreach (uint v in b) sb.Append(v.ToString("x8", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public static bool TryParsePush(string text, out uint world, out uint join, out int seq, out int n, out uint[] b)
    {
        world = join = 0; seq = n = 0; b = [];
        var p = (text ?? "").Split('|');
        if (p.Length != 6 || p[0] != "v1" || p[5].Length != QuestBuckets * 8) return false;
        if (!uint.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out world) || !uint.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out join)
            || !int.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out seq) || !int.TryParse(p[4], NumberStyles.None, CultureInfo.InvariantCulture, out n)) return false;
        b = new uint[QuestBuckets];
        for (int i = 0; i < QuestBuckets; i++)
            if (!uint.TryParse(p[5].AsSpan(i * 8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b[i])) return false;
        return seq > 0;
    }

    public static string WantText(uint world, int seq, IEnumerable<int> buckets) =>
        FormattableString.Invariant($"v1|{world}|{seq}|{string.Join(",", buckets)}");

    public static bool TryParseWant(string text, out uint world, out int seq, out List<int> buckets)
    {
        world = 0; seq = 0; buckets = [];
        var p = (text ?? "").Split('|');
        if (p.Length != 4 || p[0] != "v1" || !uint.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out world)
            || !int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out seq)) return false;
        foreach (var s in p[3].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int bb) || bb < 0 || bb >= QuestBuckets) return false;
            if (!buckets.Contains(bb)) buckets.Add(bb);
            if (buckets.Count > MaxWantBuckets) return false;
        }
        return buckets.Count > 0;
    }

    /// <summary>One bucket's entries as one or more texts (each under EntriesTextMax); an empty bucket is one part with no entries.</summary>
    public static List<string> EntriesTexts(uint world, int seq, int bucket, IEnumerable<KeyValuePair<string, string>> entries)
    {
        var chunks = new List<List<string>> { new() };
        int size = 0;
        foreach (var kv in entries.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (kv.Key.Contains('\n') || kv.Key.Contains('|') || kv.Value.Contains('\n') || kv.Value.Contains('=')) continue;   // never on the wire
            string e = kv.Key + "=" + kv.Value;
            if (e.Length > EntriesTextMax - 64) continue;
            if (size + e.Length + 1 > EntriesTextMax - 64 && chunks[^1].Count > 0) { chunks.Add(new()); size = 0; }
            chunks[^1].Add(e);
            size += e.Length + 1;
        }
        var r = new List<string>(chunks.Count);
        for (int i = 0; i < chunks.Count; i++)
            r.Add(FormattableString.Invariant($"v1|{world}|{seq}|{bucket}|{i + 1}|{chunks.Count}|") + string.Join("\n", chunks[i]));
        return r;
    }

    public static bool TryParseEntries(string text, out uint world, out int seq, out int bucket, out int part, out int nparts, out Dictionary<string, string> map)
    {
        world = 0; seq = bucket = part = nparts = 0; map = new Dictionary<string, string>(StringComparer.Ordinal);
        var p = (text ?? "").Split('|', 7);
        if (p.Length != 7 || p[0] != "v1" || !uint.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out world)
            || !int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out seq) || !int.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out bucket)
            || !int.TryParse(p[4], NumberStyles.None, CultureInfo.InvariantCulture, out part) || !int.TryParse(p[5], NumberStyles.None, CultureInfo.InvariantCulture, out nparts)) return false;
        if (bucket < 0 || bucket >= QuestBuckets || part < 1 || nparts < 1 || part > nparts || nparts > 64) return false;
        foreach (var line in p[6].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = line.LastIndexOf('=');
            if (eq <= 0 || eq == line.Length - 1) return false;
            map[line[..eq]] = line[(eq + 1)..];
        }
        return true;
    }

    /// <summary>One key's verdict: Write (the host's int to write), or a refusal reason; Equal when nothing differs.</summary>
    public readonly record struct QuestVerdict(bool Equal, string? Refusal, int Value)
    {
        public bool Write => !Equal && Refusal is null;
    }

    /// <summary>
    /// The allowlist: only a quest State (the digest's "sv:"), only an int / bool / uint by its quest-file type (WO-164's QuestFixTypeSafe:
    /// stored inline, no enum range to break), never a negative uint, never a contested, DLC or per-machine State, readable on both
    /// machines, and only a difference already seen StableAfterS before (an earlier round). Clocks (C_Timer / C_TimeOfDayWatch running
    /// states, "clk:") are never written: no write-by-path exists for a running timer, and the joiner's own timer fires are already
    /// dropped in a joined world (WO-200's quiet clocks) -- what a host timer does arrives as the State changes it makes.
    /// </summary>
    public static QuestVerdict Judge(string key, string? host, string? local, string? typeDef, bool contested, bool dlcOrPerMachine, bool stable)
    {
        if (host == local) return new(true, null, 0);
        bool hostClk = host?.StartsWith("clk:", StringComparison.Ordinal) == true, localClk = local?.StartsWith("clk:", StringComparison.Ordinal) == true;
        if (hostClk || localClk) return new(false, "clock-kind", 0);
        if (host is null) return new(false, "host-not-loaded", 0);
        if (local is null) return new(false, "not-loaded-here", 0);
        if (!host.StartsWith("sv:", StringComparison.Ordinal) || !local.StartsWith("sv:", StringComparison.Ordinal)) return new(false, "unknown-kind", 0);
        if (host == "sv:?" || local == "sv:?" || !int.TryParse(host.AsSpan(3), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v))
            return new(false, "unreadable", 0);
        if (!key.StartsWith("Barbora.", StringComparison.Ordinal)) return new(false, "not-a-quest", 0);
        if (dlcOrPerMachine) return new(false, "per-machine", 0);
        if (contested) return new(false, "contested", 0);
        if (!Wo164Rules.QuestFixTypeSafe(typeDef)) return new(false, "type-not-safe", 0);
        if (typeDef == "uint" && v < 0) return new(false, "negative-uint", 0);
        if (typeDef == "bool" && v is not (0 or 1)) return new(false, "type-not-safe", 0);
        if (!stable) return new(false, "not-stable-yet", 0);
        return new(false, null, v);
    }

    /// <summary>The stability memory: the same (host, local) pair first seen at <paramref name="firstAt"/>, judged at <paramref name="now"/>.</summary>
    public static bool Stable(double firstAt, double now) => now - firstAt >= StableAfterS;

    // ---------------------------------------------------------------- 2. the clock
    public enum ClockAct { None, Slew, SetForward, PullBack }

    /// <summary>Over this many game seconds apart the joiner's clock is SET to the host's (10 real s at the game's 15x); under it, a slew.</summary>
    public const double HardSetOverS = 150.0;
    /// <summary>After a hard set the next one waits this long (real seconds): the reading that follows must show the set first.</summary>
    public const double HardSetCooldownS = 20.0;

    /// <summary>
    /// The follower's decision for <paramref name="errS"/> (game seconds this clock is AHEAD of the host's; negative = behind). Nothing while
    /// held (a skip, a sleep or wait arbitration, a cutscene, a load, the host's pause); a forward set when far behind (Calendar.SetWorldTime:
    /// forward only, live-verified), the WO-140 native pull back when far ahead; otherwise the WO-200/201 slew.
    /// </summary>
    public static ClockAct Decide(double errS, bool held, double sinceLastSetS)
    {
        if (held || !double.IsFinite(errS)) return ClockAct.None;
        if (Math.Abs(errS) > HardSetOverS)
        {
            if (sinceLastSetS < HardSetCooldownS) return ClockAct.None;
            return errS < 0 ? ClockAct.SetForward : ClockAct.PullBack;
        }
        return ClockAct.Slew;
    }

    // ---------------------------------------------------------------- 4. the area snapshot
    public const double SnapRadiusM = 60.0;
    public const int SnapEveryS = 30;
    public const double SnapMoveM = 80.0;
    public const int SnapMaxTries = 5;

    /// <summary>A snapshot item not found here (its entity may stream in later): tried again until <see cref="SnapMaxTries"/>.</summary>
    public static bool RetryNotFound(int triesSoFar) => triesSoFar < SnapMaxTries;

    public static string SnapAskText(int sid, double x, double y, double z, double r, string why, IEnumerable<string> corpses) =>
        FormattableString.Invariant($"v1|{sid}|{x:F1}|{y:F1}|{z:F1}|{r:F0}|{why}|{string.Join(",", corpses)}");

    public static bool TryParseSnapAsk(string text, out int sid, out double x, out double y, out double z, out double r, out string why, out List<string> corpses)
    {
        sid = 0; x = y = z = r = 0; why = ""; corpses = [];
        var p = (text ?? "").Split('|');
        var inv = CultureInfo.InvariantCulture;
        if (p.Length != 8 || p[0] != "v1" || !int.TryParse(p[1], NumberStyles.None, inv, out sid)
            || !double.TryParse(p[2], NumberStyles.Float, inv, out x) || !double.TryParse(p[3], NumberStyles.Float, inv, out y)
            || !double.TryParse(p[4], NumberStyles.Float, inv, out z) || !double.TryParse(p[5], NumberStyles.Float, inv, out r)) return false;
        if (r <= 0 || r > 150 || !SafeWord(p[6])) return false;
        why = p[6];
        foreach (var c in p[7].Split(',', StringSplitOptions.RemoveEmptyEntries)) { if (!SafeWord(c)) return false; corpses.Add(c); }
        return corpses.Count <= 40;
    }

    /// <summary>An NPC or reason word that may go into a Lua string unescaped: letters, digits, underscore.</summary>
    public static bool SafeWord(string s) => s.Length is > 0 and <= 96 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-');

    /// <summary>A Lua snapshot part ("&lt;peer&gt; &lt;sid&gt; &lt;part&gt; &lt;nparts&gt; &lt;items&gt;"): the items are tokens of [A-Za-z0-9_.:-].</summary>
    public static bool SafeItems(string s) => s.Length <= 1200 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ':' or '-' or ' ');

    public static string SnapPartText(int sid, int part, int nparts, string items) => FormattableString.Invariant($"v1|{sid}|{part}|{nparts}|{items}");

    public static bool TryParseSnapPart(string text, out int sid, out int part, out int nparts, out string items)
    {
        sid = part = nparts = 0; items = "";
        var p = (text ?? "").Split('|', 5);
        var inv = CultureInfo.InvariantCulture;
        if (p.Length != 5 || p[0] != "v1" || !int.TryParse(p[1], NumberStyles.None, inv, out sid) || !int.TryParse(p[2], NumberStyles.None, inv, out part)
            || !int.TryParse(p[3], NumberStyles.None, inv, out nparts) || part < 1 || part > nparts || nparts > 64 || !SafeItems(p[4])) return false;
        items = p[4];
        return true;
    }
}
