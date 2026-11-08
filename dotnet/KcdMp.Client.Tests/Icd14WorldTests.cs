// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT fix (icd14) agent C: the pure rules of the host-authoritative world (Icd14World.cs).
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;
using C = KcdMp.Client.Icd14Rules.ClockAct;

namespace KcdMp.Client.Tests;

public class Icd14WorldTests
{
    // ---------------------------------------------------------------- switches
    [Fact]
    public void Every_switch_ships_on_but_corpse_revive()
    {
        foreach (var n in Icd14Rules.SwitchNames) Assert.Equal(n != "corpse_revive", Icd14Rules.DefaultOn(n));
        Assert.Contains("quest_push", Icd14Rules.SwitchNames);
        Assert.Contains("clock_follow", Icd14Rules.SwitchNames);
    }

    [Fact]
    public void The_side_kinds_are_17_to_21_and_named()
    {
        Assert.Equal("icd-quest-push", Protocol.W203KindName(17));
        Assert.Equal("icd-quest-want", Protocol.W203KindName(18));
        Assert.Equal("icd-quest-entries", Protocol.W203KindName(19));
        Assert.Equal("icd-snap-ask", Protocol.W203KindName(20));
        Assert.Equal("icd-snap-part", Protocol.W203KindName(21));
        Assert.Equal("unknown-22", Protocol.W203KindName(22));
    }

    // ---------------------------------------------------------------- 2. the clock
    [Theory]
    [InlineData(0.0, false, 999.0, C.Slew)]
    [InlineData(-149.0, false, 999.0, C.Slew)]
    [InlineData(149.0, false, 999.0, C.Slew)]
    [InlineData(-151.0, false, 999.0, C.SetForward)]     // far behind: set forward
    [InlineData(-600.0, false, 999.0, C.SetForward)]     // the field's 300-700 s after a skip
    [InlineData(400.0, false, 999.0, C.PullBack)]        // far ahead: the WO-140 pull
    [InlineData(-400.0, false, 5.0, C.None)]             // a set still landing: nothing
    [InlineData(-400.0, true, 999.0, C.None)]            // held (a skip, its arbitration, a cutscene)
    [InlineData(10.0, true, 999.0, C.None)]
    [InlineData(double.NaN, false, 999.0, C.None)]
    public void The_clock_slews_a_small_error_and_sets_a_large_one(double err, bool held, double sinceSet, C want) =>
        Assert.Equal(want, Icd14Rules.Decide(err, held, sinceSet));

    [Fact]
    public void A_set_waits_its_cooldown()
    {
        Assert.Equal(C.None, Icd14Rules.Decide(-500, false, Icd14Rules.HardSetCooldownS - 0.1));
        Assert.Equal(C.SetForward, Icd14Rules.Decide(-500, false, Icd14Rules.HardSetCooldownS));
    }

    // ---------------------------------------------------------------- 1. the quest push: buckets and texts
    private static Dictionary<string, string> Map(params (string K, string V)[] kv) => kv.ToDictionary(x => x.K, x => x.V, StringComparer.Ordinal);

    [Fact]
    public void Equal_maps_have_equal_buckets_and_one_change_marks_its_bucket_only()
    {
        var a = Map(("Barbora.q1.s1", "sv:1"), ("Barbora.q1.s2", "sv:0"), ("Barbora.q2.s1", "sv:5"));
        var b = Map(("Barbora.q1.s1", "sv:1"), ("Barbora.q1.s2", "sv:0"), ("Barbora.q2.s1", "sv:5"));
        Assert.Empty(Icd14Rules.DiffBuckets(Icd14Rules.QBuckets(a), Icd14Rules.QBuckets(b)));
        b["Barbora.q2.s1"] = "sv:6";
        var d = Icd14Rules.DiffBuckets(Icd14Rules.QBuckets(a), Icd14Rules.QBuckets(b));
        Assert.Equal([Icd14Rules.QBucketOf("Barbora.q2.s1")], d);
        b.Remove("Barbora.q2.s1");
        Assert.Contains(Icd14Rules.QBucketOf("Barbora.q2.s1"), Icd14Rules.DiffBuckets(Icd14Rules.QBuckets(a), Icd14Rules.QBuckets(b)));
    }

    [Fact]
    public void The_push_text_round_trips()
    {
        var b = Icd14Rules.QBuckets(Map(("Barbora.q.s", "sv:3")));
        string t = Icd14Rules.PushText(4, 9, 12, 1, b);
        Assert.True(t.Length < Protocol.W203TextMax);
        Assert.True(Icd14Rules.TryParsePush(t, out uint w, out uint j, out int seq, out int n, out uint[] back));
        Assert.Equal((4u, 9u, 12, 1), (w, j, seq, n));
        Assert.Equal(b, back);
        Assert.False(Icd14Rules.TryParsePush(t[..^1], out _, out _, out _, out _, out _));
        Assert.False(Icd14Rules.TryParsePush("v2" + t[2..], out _, out _, out _, out _, out _));
    }

    [Fact]
    public void The_want_text_round_trips_and_refuses_too_many_or_bad_buckets()
    {
        Assert.True(Icd14Rules.TryParseWant(Icd14Rules.WantText(3, 7, [1, 5, 127]), out uint w, out int seq, out var bs));
        Assert.Equal((3u, 7), (w, seq));
        Assert.Equal([1, 5, 127], bs);
        Assert.False(Icd14Rules.TryParseWant(Icd14Rules.WantText(3, 7, [128]), out _, out _, out _));
        Assert.False(Icd14Rules.TryParseWant(Icd14Rules.WantText(3, 7, Enumerable.Range(0, Icd14Rules.MaxWantBuckets + 1)), out _, out _, out _));
        Assert.False(Icd14Rules.TryParseWant("v1|3|7|", out _, out _, out _));
    }

    [Fact]
    public void A_big_bucket_goes_in_parts_under_the_side_text_limit_and_comes_back_whole()
    {
        var entries = Enumerable.Range(0, 200).Select(i => new KeyValuePair<string, string>($"Barbora.quest_{i:D3}.some_long_state_name", "sv:" + i)).ToList();
        var texts = Icd14Rules.EntriesTexts(2, 5, 17, entries);
        Assert.True(texts.Count > 1);
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < texts.Count; i++)
        {
            Assert.True(texts[i].Length <= Icd14Rules.EntriesTextMax);
            Assert.True(Icd14Rules.TryParseEntries(texts[i], out uint w, out int seq, out int b, out int part, out int np, out var m));
            Assert.Equal((2u, 5, 17, i + 1, texts.Count), (w, seq, b, part, np));
            foreach (var kv in m) all[kv.Key] = kv.Value;
        }
        Assert.Equal(200, all.Count);
        Assert.Equal("sv:42", all["Barbora.quest_042.some_long_state_name"]);
    }

    [Fact]
    public void An_empty_bucket_is_one_part_and_a_value_with_a_separator_never_goes_on_the_wire()
    {
        var t = Icd14Rules.EntriesTexts(1, 1, 0, []);
        Assert.Single(t);
        Assert.True(Icd14Rules.TryParseEntries(t[0], out _, out _, out _, out _, out _, out var m));
        Assert.Empty(m);
        var t2 = Icd14Rules.EntriesTexts(1, 1, 0, [new("Barbora.a", "sv:1"), new("Barbora.b", "x=y"), new("Barbora.c", "a\nb")]);
        Assert.True(Icd14Rules.TryParseEntries(t2[0], out _, out _, out _, out _, out _, out var m2));
        Assert.Equal(["Barbora.a"], m2.Keys);
    }

    // ---------------------------------------------------------------- 1. the quest push: the allowlist
    private static Icd14Rules.QuestVerdict J(string? host, string? local, string? type = "int", bool contested = false, bool perMachine = false,
        bool stable = true, string key = "Barbora.q_main.State") => Icd14Rules.Judge(key, host, local, type, contested, perMachine, stable);

    [Fact]
    public void A_stable_safe_quest_state_is_written_to_the_hosts_value()
    {
        var v = J("sv:7", "sv:3");
        Assert.True(v.Write);
        Assert.Equal(7, v.Value);
        Assert.True(J("sv:1", "sv:0", "bool").Write);
        Assert.True(J("sv:5", "sv:0", "uint").Write);
        Assert.True(J("sv:-2", "sv:0", "int").Write);
    }

    [Theory]
    [InlineData("sv:1", "sv:1", "int", false, false, true, null)]   // equal
    [InlineData("clk:5", "clk:9", "int", false, false, true, "clock-kind")]
    [InlineData("sv:1", "clk:9", "int", false, false, true, "clock-kind")]
    [InlineData(null, "sv:1", "int", false, false, true, "host-not-loaded")]
    [InlineData("sv:1", null, "int", false, false, true, "not-loaded-here")]
    [InlineData("xx:1", "sv:1", "int", false, false, true, "unknown-kind")]
    [InlineData("sv:?", "sv:1", "int", false, false, true, "unreadable")]
    [InlineData("sv:1", "sv:0", "int", false, true, true, "per-machine")]
    [InlineData("sv:1", "sv:0", "int", true, false, true, "contested")]
    [InlineData("sv:1", "sv:0", "enum", false, false, true, "type-not-safe")]
    [InlineData("sv:1", "sv:0", null, false, false, true, "type-not-safe")]
    [InlineData("sv:-1", "sv:0", "uint", false, false, true, "negative-uint")]
    [InlineData("sv:2", "sv:0", "bool", false, false, true, "type-not-safe")]
    [InlineData("sv:1", "sv:0", "int", false, false, false, "not-stable-yet")]
    public void Everything_else_is_refused_with_its_reason(string? host, string? local, string? type, bool contested, bool perMachine, bool stable, string? why)
    {
        var v = J(host, local, type, contested, perMachine, stable);
        Assert.False(v.Write);
        if (why is null) Assert.True(v.Equal);
        else Assert.Equal(why, v.Refusal);
    }

    [Fact]
    public void Only_a_quest_key_is_written()
    {
        Assert.Equal("not-a-quest", J("sv:1", "sv:0", key: "Global.something").Refusal);
    }

    [Fact]
    public void A_difference_is_stable_only_after_it_stood()
    {
        Assert.False(Icd14Rules.Stable(100, 100 + Icd14Rules.StableAfterS - 0.5));
        Assert.True(Icd14Rules.Stable(100, 100 + Icd14Rules.StableAfterS));
    }

    // ---------------------------------------------------------------- 4. the area snapshot
    [Fact]
    public void A_not_found_item_is_retried_up_to_the_limit()
    {
        for (int i = 0; i < Icd14Rules.SnapMaxTries; i++) Assert.True(Icd14Rules.RetryNotFound(i));
        Assert.False(Icd14Rules.RetryNotFound(Icd14Rules.SnapMaxTries));
        Assert.False(Icd14Rules.RetryNotFound(Icd14Rules.SnapMaxTries + 3));
    }

    [Fact]
    public void The_snapshot_ask_round_trips_and_refuses_unsafe_words()
    {
        string t = Icd14Rules.SnapAskText(3, 10.5, -2, 30, 60, "join", ["bandit_1", "villager-2"]);
        Assert.True(Icd14Rules.TryParseSnapAsk(t, out int sid, out double x, out double y, out double z, out double r, out string why, out var c));
        Assert.Equal((3, 10.5, -2.0, 30.0, 60.0, "join"), (sid, x, y, z, r, why));
        Assert.Equal(["bandit_1", "villager-2"], c);
        Assert.False(Icd14Rules.TryParseSnapAsk(Icd14Rules.SnapAskText(3, 0, 0, 0, 60, "jo\"in", []), out _, out _, out _, out _, out _, out _, out _));
        Assert.False(Icd14Rules.TryParseSnapAsk(Icd14Rules.SnapAskText(3, 0, 0, 0, 500, "join", []), out _, out _, out _, out _, out _, out _, out _));
        Assert.False(Icd14Rules.TryParseSnapAsk(Icd14Rules.SnapAskText(3, 0, 0, 0, 60, "join", ["a\")"]), out _, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void The_snapshot_part_round_trips_and_carries_only_safe_items()
    {
        string items = "D:d0a1b2c3d:1:0:10.0:-2.5:3.0 C:bandit_1:1.0:2.0:3.0 A:villager_2:4.0:5.0:6.0";
        Assert.True(Icd14Rules.TryParseSnapPart(Icd14Rules.SnapPartText(9, 1, 2, items), out int sid, out int part, out int np, out string back));
        Assert.Equal((9, 1, 2, items), (sid, part, np, back));
        Assert.False(Icd14Rules.SafeItems("C:x\");os.exit()--"));
        Assert.False(Icd14Rules.TryParseSnapPart(Icd14Rules.SnapPartText(9, 3, 2, items), out _, out _, out _, out _));
        Assert.False(Icd14Rules.SafeWord(""));
        Assert.False(Icd14Rules.SafeWord("a b"));
        Assert.True(Icd14Rules.SafeWord("bandit_1"));
    }
}
