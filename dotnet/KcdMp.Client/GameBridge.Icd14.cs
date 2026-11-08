// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// KCT fix (icd14): the host's world is the one that counts -- pushed, compared and corrected on the joiner, not only checked.
//   1 quest_push    (host -> joiner, every 30 s) the WO-206 digest quest map's 128 bucket sums; the joiner asks for the buckets that differ
//                   and writes the host's value of a differing quest State by path (the DLL's WO-164 op 9: int / bool / uint States only)
//                   when the same difference stood 20 s (Icd14Rules.Judge is the allowlist). Never during a join, a load, a skip, a
//                   cutscene, a conversation or a world-epoch change; at most 16 writes a round. Every write: one ICD14-QUEST line.
//   2 clock_follow  (joiner) the host's clock every 2 s (WO-200 II.7) is followed continuously: a slew for a small error (WO-200/201), a
//                   hard set over Icd14Rules.HardSetOverS (forward: Calendar.SetWorldTime; back: the WO-140 native pull), never in a skip,
//                   its arbitration, a cutscene, a load or the host's pause.
//   3/4             the crime ledger and the area snapshot live in kdcmp.lua (KCD2MP.icd14); this side carries the snapshot's messages.
// MP-HEALTH area=world lines: part=clock (every 10 s), part=quest (every 30 s); the Lua prints part=crime / part=snapshot.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _icd14On = new(StringComparer.Ordinal);
    private bool Icd14(string name) => _icd14On.TryGetValue(name, out bool v) ? v : Icd14Rules.DefaultOn(name);

    // ---- 1: the quest push ----
    private int _icdQSeq;
    private long _icdQNextPushMs;
    private long _icdQPushes, _icdQKeysCompared, _icdQDiffs, _icdQCorrections, _icdQRefusedTotal;
    private readonly ConcurrentDictionary<string, long> _icdQRefused = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string? Host, string? Local, double FirstAt)> _icdQSeen = new(StringComparer.Ordinal);
    private readonly object _icdQLock = new();
    private (int Seq, uint World, uint Join, long AtMs, HashSet<int> Buckets)? _icdQRound;
    private readonly Dictionary<int, Dictionary<int, Dictionary<string, string>>> _icdQParts = new();   // bucket -> part -> entries
    private readonly Dictionary<int, int> _icdQNParts = new();
    private int _icdQWritesThisRound;

    // ---- 2: the clock ----
    private double _icdClockErrMs;
    private long _icdClockSlews, _icdClockSets, _icdClockSetFails, _icdClockHeld;
    private long _icdClockLastSetMs = -1_000_000;

    // ---- 4: the snapshot's messages ----
    private int _icdSnapSid;
    private long _icdSnapAsks, _icdSnapSent, _icdSnapPartsIn, _icdSnapRefused, _icdSnapKills;

    private long _icdHealthClockMs, _icdHealthQuestMs;

    private static double Icd14Now() => Environment.TickCount64 / 1000.0;

    /// <summary>The 4 Hz session tick.</summary>
    private void Icd14Tick(long n)
    {
        long now = Environment.TickCount64;
        if (W137Host && now >= _icdQNextPushMs)
        {
            _icdQNextPushMs = now + Icd14Rules.QuestPushEveryS * 1000L;
            Icd14HostPush();
        }
        if (now - _icdHealthClockMs >= 10_000)
        {
            _icdHealthClockMs = now;
            if (W137Host || W137Joiner) Console.WriteLine(Icd14HealthClock());
        }
        if (now - _icdHealthQuestMs >= Icd14Rules.QuestPushEveryS * 1000L)
        {
            _icdHealthQuestMs = now;
            if (W137Host || W137Joiner) Console.WriteLine(Icd14HealthQuest());
        }
    }

    private string Icd14HealthClock() => FormattableString.Invariant(
        $"{Icd14Rules.HealthHead} part=clock role={(W137Host ? "host" : "joiner")} clock_follow={(Icd14("clock_follow") ? "on" : "off")} clock_err_ms={_icdClockErrMs:F0} clock_slews={Interlocked.Read(ref _icdClockSlews)} clock_sets={Interlocked.Read(ref _icdClockSets)} clock_set_fails={Interlocked.Read(ref _icdClockSetFails)} clock_held={Interlocked.Read(ref _icdClockHeld)} snapshot_asks={Interlocked.Read(ref _icdSnapAsks)} snapshot_sent={Interlocked.Read(ref _icdSnapSent)} snapshot_parts_in={Interlocked.Read(ref _icdSnapPartsIn)} snapshot_refused={Interlocked.Read(ref _icdSnapRefused)} snapshot_kills={Interlocked.Read(ref _icdSnapKills)}");

    private string Icd14HealthQuest()
    {
        string by = string.Join(",", _icdQRefused.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + ":" + k.Value.ToString(CultureInfo.InvariantCulture)));
        return FormattableString.Invariant(
            $"{Icd14Rules.HealthHead} part=quest role={(W137Host ? "host" : "joiner")} quest_push={(Icd14("quest_push") ? "on" : "off")} quest_pushes={Interlocked.Read(ref _icdQPushes)} quest_keys_compared={Interlocked.Read(ref _icdQKeysCompared)} quest_diffs={Interlocked.Read(ref _icdQDiffs)} quest_corrections={Interlocked.Read(ref _icdQCorrections)} quest_correct_refused={Interlocked.Read(ref _icdQRefusedTotal)} quest_correct_refused_by={(by.Length > 0 ? by : "-")} quest_unsettled={_icdQSeen.Count}");
    }

    private void Icd14Refuse(string reason, string? key = null, string? host = null, string? local = null)
    {
        long n = _icdQRefused.AddOrUpdate(reason, 1, (_, v) => v + 1);
        Interlocked.Increment(ref _icdQRefusedTotal);
        if (key is not null && (n <= 10 || n % 100 == 0))
            Console.WriteLine($"ICD14-QUEST refused key={key} host={host ?? "-"} joiner={local ?? "-"} why={reason} [n={n}]");
    }

    // ================================================================ 1. the quest push

    /// <summary>Host: the bucket sums of its digest quest map to every partner (only a whole map at a quiet point).</summary>
    private void Icd14HostPush()
    {
        if (!Icd14("quest_push") || !W206DigestOn || !Wo206Quiet() || !_w206dSynced) return;
        var peers = LivePartners();
        if (peers.Count == 0) return;
        var map = _w206dQuest.ToArray();
        int seq = Interlocked.Increment(ref _icdQSeq);
        string text = Icd14Rules.PushText(Wo206Applied.WorldEpoch, Wo206Applied.JoinEpoch, seq, map.Length, Icd14Rules.QBuckets(map));
        foreach (byte p in peers) _ = Wo203SendSideAsync(p, Protocol.W203IcdQuestPush, text);
        Interlocked.Increment(ref _icdQPushes);
    }

    /// <summary>The joiner's gate for any correction (null = may correct).</summary>
    private string? Icd14QuestGate(uint world, uint join)
    {
        if (!Icd14("quest_push")) return "switch-off";
        if (!W206DigestOn) return "digest-off";
        if (!W137Joiner) return "not-a-joiner";
        if (!_combat.IsConnected) return "no-dll";
        if (!Wo206Quiet()) return "join-load-or-skip";
        if (!_w206dSynced) return "digest-not-synced";
        if (_localCutsceneActive) return "cutscene";
        if (world != Wo206Applied.WorldEpoch || join != Wo206Applied.JoinEpoch) return "epoch-changed";
        if (_w137Queue.Count > 0 || !_w137Asked.IsEmpty) return "mirror-busy";
        if (Wo147InOwnConversation()) return "conversation";
        return null;
    }

    /// <summary>Joiner: the host's push -- the buckets that differ are asked for.</summary>
    private void Icd14OnQuestPush(LootMsg m)
    {
        if (!W137Joiner || !Icd14Rules.TryParsePush(m.Text, out uint world, out uint join, out int seq, out int n, out uint[] hostB)) return;
        if (Icd14QuestGate(world, join) is string why) { Icd14Refuse("round:" + why); return; }
        Interlocked.Add(ref _icdQKeysCompared, n);
        uint[] mine = Icd14Rules.QBuckets(_w206dQuest.ToArray());
        var diff = Icd14Rules.DiffBuckets(hostB, mine);
        if (diff.Count == 0) { _icdQSeen.Clear(); return; }
        // a fair share: start at a different bucket each round so a long list is walked through
        int start = diff.Count > Icd14Rules.MaxWantBuckets ? seq * Icd14Rules.MaxWantBuckets % diff.Count : 0;
        var want = Enumerable.Range(0, Math.Min(diff.Count, Icd14Rules.MaxWantBuckets)).Select(i => diff[(start + i) % diff.Count]).ToList();
        lock (_icdQLock)
        {
            _icdQRound = (seq, world, join, Environment.TickCount64, want.ToHashSet());
            _icdQParts.Clear(); _icdQNParts.Clear();
            _icdQWritesThisRound = 0;
        }
        _ = Wo203SendSideAsync(Protocol.JoinTargetHost, Protocol.W203IcdQuestWant, Icd14Rules.WantText(world, seq, want));
    }

    /// <summary>Host: a joiner's buckets -- this world's entries of each, as they stand now.</summary>
    private void Icd14OnQuestWant(byte src, LootMsg m)
    {
        if (!W137Host || !Icd14Rules.TryParseWant(m.Text, out uint world, out int seq, out var buckets)) return;
        if (world != Wo206Applied.WorldEpoch || !Icd14("quest_push")) return;   // asked of a world this host has since left
        var map = _w206dQuest.ToArray();
        foreach (int b in buckets)
            foreach (var t in Icd14Rules.EntriesTexts(world, seq, b, map.Where(kv => Icd14Rules.QBucketOf(kv.Key) == b)))
                _ = Wo203SendSideAsync(src, Protocol.W203IcdQuestEntries, t);
    }

    /// <summary>Joiner: one part of the host's entries; a bucket whose parts are all in is judged.</summary>
    private void Icd14OnQuestEntries(LootMsg m)
    {
        if (!W137Joiner || !Icd14Rules.TryParseEntries(m.Text, out uint world, out int seq, out int bucket, out int part, out int nparts, out var entries)) return;
        Dictionary<string, string>? whole = null;
        uint join;
        lock (_icdQLock)
        {
            if (_icdQRound is not { } r || r.Seq != seq || r.World != world || !r.Buckets.Contains(bucket)) return;
            if (Environment.TickCount64 - r.AtMs > Icd14Rules.RoundMaxAgeS * 1000) { Icd14Refuse("round:late-answer"); _icdQRound = null; return; }
            join = r.Join;
            if (!_icdQParts.TryGetValue(bucket, out var parts)) _icdQParts[bucket] = parts = new();
            parts[part] = entries;
            _icdQNParts[bucket] = nparts;
            if (parts.Count == nparts)
            {
                whole = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in parts.Values) foreach (var kv in p) whole[kv.Key] = kv.Value;
                _icdQParts.Remove(bucket);
                r.Buckets.Remove(bucket);
            }
        }
        if (whole is not null) _ = Icd14JudgeBucketAsync(world, join, bucket, whole);
    }

    private async Task Icd14JudgeBucketAsync(uint world, uint join, int bucket, Dictionary<string, string> host)
    {
        try
        {
            QuestValueIndex? idx = null;
            try { idx = await Wo147QuestIndexAsync(); } catch { }
            var local = _w206dQuest.Where(kv => Icd14Rules.QBucketOf(kv.Key) == bucket).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            double now = Icd14Now();
            foreach (string key in host.Keys.Concat(local.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
            {
                host.TryGetValue(key, out var hv);
                local.TryGetValue(key, out var lv);
                if (hv == lv) { _icdQSeen.TryRemove(key, out _); continue; }
                Interlocked.Increment(ref _icdQDiffs);
                var seen = _icdQSeen.AddOrUpdate(key, _ => (hv, lv, now), (_, o) => o.Host == hv && o.Local == lv ? o : (hv, lv, now));
                string? typeDef = idx?.StateType(key);
                bool perMachine = Wo137Rules.IsDlc(key) || Wo137Rules.PerMachine(new QuestChange(0, 0, 0, 0, "", typeDef ?? "", key, 0));
                var v = Icd14Rules.Judge(key, hv, lv, typeDef, _w154Contest.IsContested(key), perMachine, Icd14Rules.Stable(seen.FirstAt, now));
                if (v.Equal) continue;
                if (v.Refusal is string why) { Icd14Refuse(why, key, hv, lv); continue; }
                if (Icd14QuestGate(world, join) is string gate) { Icd14Refuse(gate, key, hv, lv); continue; }
                if (Interlocked.Increment(ref _icdQWritesThisRound) > Icd14Rules.MaxCorrectionsPerRound) { Icd14Refuse("rate-limit", key, hv, lv); continue; }
                var res = await _combat.Wo137SetValueAsync(Interlocked.Increment(ref _w137Tok), key, v.Value);
                bool ok = res is { } rr && Wo137Rules.WriteTook(rr.Result);
                string result = res is null ? "no-answer" : Wo137Rules.AppliedName(res.Value.Result);
                if (ok)
                {
                    Interlocked.Increment(ref _icdQCorrections);
                    _icdQSeen.TryRemove(key, out _);
                    Wo206NoteApplied("quest", key, "icd14-push", lv, hv);
                }
                else Icd14Refuse("write:" + result);
                Console.WriteLine($"ICD14-QUEST correct key={key} old={lv ?? "-"} new={hv ?? "-"} type={typeDef ?? "?"} result={result} applied={(ok ? "yes" : "no")} age_s={now - seen.FirstAt:F0}");
            }
            // a memory of keys no longer differing in any asked bucket is dropped after a minute without a sighting
            foreach (var (k, s) in _icdQSeen.ToArray()) if (now - s.FirstAt > 600) _icdQSeen.TryRemove(k, out _);
        }
        catch (Exception ex) { Console.WriteLine($"ICD14-QUEST bucket {bucket} not judged: {ex.GetType().Name}: {ex.Message}"); }
    }

    // ================================================================ 2. the clock

    /// <summary>
    /// Joiner, from Wo200OnHostClockAsync after its own holds (a skip, a load, the host's pause): true = handled here (a hard set ran or
    /// a hold applies); false = the WO-200/201 slew goes on.
    /// </summary>
    private async Task<bool> Icd14ClockAsync(uint host, double hostRatio, double err, uint mine)
    {
        _icdClockErrMs = err * 1000.0;
        if (!Icd14("clock_follow")) return false;
        bool hostSkipping = _w140HostSkipSinceUtc != DateTime.MinValue && (MonoClock.UtcNow - _w140HostSkipSinceUtc).TotalMinutes < 10;
        bool held = W140SharedSkip || hostSkipping || _w140LocalSkipping || _localSkipActive || _localCutsceneActive || Wo136Holding || _w144Following || W140JoinBusy;
        long nowMs = Environment.TickCount64;
        var act = Icd14Rules.Decide(err, held, (nowMs - _icdClockLastSetMs) / 1000.0);
        switch (act)
        {
            case Icd14Rules.ClockAct.None:
                if (held) Interlocked.Increment(ref _icdClockHeld);
                return held || Math.Abs(err) > Icd14Rules.HardSetOverS;   // a cooldown: no slew over a set that is still landing
            case Icd14Rules.ClockAct.Slew:
                if (Math.Abs(err) >= 4) Interlocked.Increment(ref _icdClockSlews);
                return false;
        }
        _icdClockLastSetMs = nowMs;
        uint target = (uint)Math.Round(host + Wo161Rules.OneWayMs(_clockRttMedianMs) / 1000.0 * hostRatio);
        if (act == Icd14Rules.ClockAct.SetForward)
        {
            try
            {
                await ExecLuaAsync(FormattableString.Invariant(
                    $"pcall(function() local c = Calendar.GetWorldTime(); if c and c < {target} then Calendar.SetWorldTime({target}) end end); if KCD2MP_ReportWorldTime then KCD2MP_ReportWorldTime() end"));
                _lastPolledWorldTime = target;
                _lastPollUtc = MonoClock.UtcNow;
                _suppressJumpUntilUtc = MonoClock.UtcNow.AddSeconds(30);   // the set is the host's time, never this player's own skip
                Interlocked.Increment(ref _icdClockSets);
                Console.WriteLine(FormattableString.Invariant($"ICD14-CLOCK set forward {mine} -> {target} ({-err:F0} game s behind the host) -- one clock: the host's"));
            }
            catch (Exception ex) { Interlocked.Increment(ref _icdClockSetFails); Console.WriteLine($"ICD14-CLOCK set forward failed: {ex.Message}"); }
        }
        else
        {
            var r = await _combat.Wo140PullAsync(mine, target);
            if (r is { Result: 1 } ok)
            {
                _lastPolledWorldTime = ok.After;
                _lastPollUtc = MonoClock.UtcNow;
                _suppressJumpUntilUtc = MonoClock.UtcNow.AddSeconds(30);
                Interlocked.Increment(ref _icdClockSets);
                Console.WriteLine(FormattableString.Invariant($"ICD14-CLOCK pulled back {ok.Before} -> {ok.After} ({err:F0} game s ahead of the host) -- one clock: the host's"));
            }
            else
            {
                Interlocked.Increment(ref _icdClockSetFails);
                Console.WriteLine($"ICD14-CLOCK pull back to {target} not done ({(r is null ? "no answer" : "result " + r.Value.Result.ToString(CultureInfo.InvariantCulture))}) -- the slew goes on");
                return false;
            }
        }
        await Wo200RatioAsync(hostRatio, "icd14: the host's pace after a set");
        return true;
    }

    // ================================================================ 4. the area snapshot (the Lua builds and applies it)

    private void Icd14OnSnapAsk(byte src, LootMsg m)
    {
        if (!W137Host || !Icd14("area_snapshot")) { Interlocked.Increment(ref _icdSnapRefused); return; }
        if (!Icd14Rules.TryParseSnapAsk(m.Text, out int sid, out double x, out double y, out double z, out double r, out string why, out var corpses)) return;
        _ = ExecLuaAsync(FormattableString.Invariant(
            $"if KCD2MP_Icd14SnapBuild then KCD2MP_Icd14SnapBuild({src}, {sid}, {x:F1}, {y:F1}, {z:F1}, {r:F0}, \"{why}\", \"{string.Join(",", corpses)}\") end"));
    }

    private void Icd14OnSnapPart(LootMsg m)
    {
        if (!W137Joiner || !Icd14Rules.TryParseSnapPart(m.Text, out int sid, out int part, out int nparts, out string items)) return;
        // never during a join, a load, a skip or a cutscene, nor into a world the host has since left
        if (!Icd14("area_snapshot") || !Wo206Quiet() || _localCutsceneActive) { Interlocked.Increment(ref _icdSnapRefused); return; }
        Interlocked.Increment(ref _icdSnapPartsIn);
        _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_Icd14SnapApply then KCD2MP_Icd14SnapApply({sid}, {part}, {nparts}, \"{items}\") end"));
    }

    /// <summary>The Lua's icd14_* events.</summary>
    private void Icd14OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "icd14_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !Icd14Rules.SwitchNames.Contains(arg[..eq])) { Console.WriteLine($"ICD14 cfg refused: '{arg}'"); return; }
                bool on = arg[(eq + 1)..] == "on";
                if (!_icd14On.TryGetValue(arg[..eq], out bool was) || was != on) Console.WriteLine($"ICD14 cfg {arg}");
                _icd14On[arg[..eq]] = on;
                return;
            }
            case "icd14_status":
                Console.WriteLine($"ICD14-STATUS {string.Join(' ', Icd14Rules.SwitchNames.Select(n => $"{n}={(Icd14(n) ? "on" : "off")}"))}");
                Console.WriteLine(Icd14HealthClock());
                Console.WriteLine(Icd14HealthQuest());
                return;
            case "icd14_snap_ask":   // joiner: "<x> <y> <z> <r> <why> <corpse,...|->" -> the host
            {
                if (!W137Joiner || !Icd14("area_snapshot") || !Wo206Quiet() || _localCutsceneActive) return;
                var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var inv = CultureInfo.InvariantCulture;
                if (f.Length != 6 || !double.TryParse(f[0], NumberStyles.Float, inv, out double x) || !double.TryParse(f[1], NumberStyles.Float, inv, out double y)
                    || !double.TryParse(f[2], NumberStyles.Float, inv, out double z) || !double.TryParse(f[3], NumberStyles.Float, inv, out double r)) return;
                var corpses = f[5] == "-" ? new List<string>() : f[5].Split(',', StringSplitOptions.RemoveEmptyEntries).Where(Icd14Rules.SafeWord).Take(40).ToList();
                if (!Icd14Rules.SafeWord(f[4])) return;
                int sid = Interlocked.Increment(ref _icdSnapSid);
                Interlocked.Increment(ref _icdSnapAsks);
                _ = Wo203SendSideAsync(Protocol.JoinTargetHost, Protocol.W203IcdSnapAsk, Icd14Rules.SnapAskText(sid, x, y, z, r, f[4], corpses));
                return;
            }
            case "icd14_snap_part":   // host: "<peer> <sid> <part> <nparts> <items...>" -> that joiner
            {
                if (!W137Host || arg is null) return;
                var f = arg.Split(' ', 5);
                if (f.Length < 4 || !byte.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte peer)
                    || !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out int sid) || !int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out int part)
                    || !int.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out int nparts)) return;
                string items = f.Length == 5 ? f[4] : "";
                if (!Icd14Rules.SafeItems(items) || part < 1 || part > nparts) return;
                if (part == nparts) Interlocked.Increment(ref _icdSnapSent);
                _ = Wo203SendSideAsync(peer, Protocol.W203IcdSnapPart, Icd14Rules.SnapPartText(sid, part, nparts, items));
                return;
            }
            case "icd14_kill":   // joiner: a body alive here that the host's world has dead (the snapshot) -- the host's death, applied
            {
                string n = (arg ?? "").Trim();
                if (!W137Joiner || !Icd14Rules.SafeWord(n)) return;
                Interlocked.Increment(ref _icdSnapKills);
                _ = ApplyRemoteNpcDeathAsync(n, null, 0, "icd14-snapshot", CancellationToken.None);
                return;
            }
        }
    }
}
