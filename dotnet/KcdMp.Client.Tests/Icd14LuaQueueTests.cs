// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>KCT fix (icd14) item 1: the Lua command queue -- Edge time to live by kind, eviction order, replays, health.</summary>
public class Icd14LuaQueueTests
{
    private static string Ghost(string id, int n) => $"KCD2MP_UpdateGhost(\"{id}\",{n},0,0,0,false)";
    private static string Toast(string s) => $"if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"{s}\") end";
    private static string Death(string s) => $"if KCD2MP_W210DeathApply then KCD2MP_W210DeathApply(\"{s}\") end";
    private static string Door(int n) => $"if KCD2MP_W151DoorApply then KCD2MP_W151DoorApply({n},2,3) end";

    private static BatchQueue.Entry E(string lua, long at = 0, int kind207 = 0)
    {
        var (kind, key) = BatchPolicy.Classify(lua);
        var cls = kind == BatchKind.Edge ? LuaEdgeRules.Classify(LuaEdgeRules.FunctionOf(lua), kind207) : EdgeClass.Normal;
        return new BatchQueue.Entry { Lua = lua, Text = lua, Kind = kind, Key = key, Encoded = 100, EnqueuedMs = at, Class = cls,
                                      OnceId = kind == BatchKind.Edge ? "e-" + lua.GetHashCode() : null };
    }

    // ---------------------------------------------------------------- the classes

    [Theory]
    [InlineData("KCD2MP_ShowNativeToast", 0, EdgeClass.Feedback)]
    [InlineData("KCD2MP_W165Say", 0, EdgeClass.Feedback)]
    [InlineData("KCD2MP_Wo124Msg", 0, EdgeClass.Feedback)]
    [InlineData("KCD2MP_W210DeathApply", 0, EdgeClass.World)]
    [InlineData("KCD2MP_W160QuestStep", 0, EdgeClass.World)]
    [InlineData("KCD2MP_LootTake", 0, EdgeClass.World)]
    [InlineData("KCD2MP_W123JoinDone", 0, EdgeClass.World)]
    [InlineData("KCD2MP_W151DoorApply", 0, EdgeClass.Normal)]
    [InlineData(null, 0, EdgeClass.Normal)]
    // the kind whose handler queued it: a world kind wins over a feedback name; a feedback kind only names the unnamed
    [InlineData("KCD2MP_ShowNativeToast", Protocol.DeathDown, EdgeClass.World)]
    [InlineData("KCD2MP_W151DoorApply", Protocol.CombatEventDown, EdgeClass.Feedback)]
    [InlineData(null, Protocol.ItemDropDown, EdgeClass.World)]
    public void An_edge_is_classed_by_its_function_and_the_kind_that_queued_it(string? fn, int kind207, EdgeClass want) =>
        Assert.Equal(want, LuaEdgeRules.Classify(fn, kind207));

    [Fact]
    public void The_function_is_the_first_mod_call_and_a_class_has_its_time_to_live()
    {
        Assert.Equal("KCD2MP_ShowNativeToast", LuaEdgeRules.FunctionOf(Toast("x")));
        Assert.Null(LuaEdgeRules.FunctionOf("System.LogAlways(\"x\")"));
        Assert.Equal(LuaEdgeRules.FeedbackTtlMs, LuaEdgeRules.TtlMs(EdgeClass.Feedback));
        Assert.Equal(120_000, LuaEdgeRules.TtlMs(EdgeClass.Normal));
        Assert.Equal(long.MaxValue, LuaEdgeRules.TtlMs(EdgeClass.World));
    }

    // ---------------------------------------------------------------- expiry

    [Fact]
    public void A_toast_expires_in_seconds_a_death_never_and_off_is_120_s_for_all()
    {
        var q = new BatchQueue();
        q.Add(E(Toast("hit"), at: 0)); q.Add(E(Death("a"), at: 0)); q.Add(E(Door(1), at: 0));
        Assert.Equal(new[] { Death("a"), Door(1) }, q.TakeBatch(10_000, 6_000).Select(e => e.Lua));
        Assert.Equal(1, q.EdgesExpiredByClass[(int)EdgeClass.Feedback]);

        q = new BatchQueue();
        q.Add(E(Death("a"), at: 0)); q.Add(E(Door(1), at: 0));
        Assert.Equal(new[] { Death("a") }, q.TakeBatch(10_000, 10 * 60_000).Select(e => e.Lua));   // ten minutes on, the death still goes
        Assert.Equal(1, q.EdgesExpiredByClass[(int)EdgeClass.Normal]);

        q = new BatchQueue { EdgeTtlByClass = false };
        q.Add(E(Toast("hit"), at: 0)); q.Add(E(Death("a"), at: 0));
        Assert.Equal(2, q.TakeBatch(10_000, 6_000).Count);
        q.Add(E(Death("b"), at: 0));
        Assert.Empty(q.TakeBatch(10_000, 121_000));
    }

    // ---------------------------------------------------------------- the bound

    [Fact]
    public void Over_the_bound_an_expired_or_feedback_edge_goes_before_any_state_and_the_newest_state_stays()
    {
        var q = new BatchQueue { MaxEntries = 3 };
        q.Add(E(Ghost("a", 1), at: 0)); q.Add(E(Toast("t"), at: 0)); q.Add(E(Death("d"), at: 0));
        q.Add(E(Ghost("b", 1), at: 100));
        Assert.Equal(new[] { Ghost("a", 1), Death("d"), Ghost("b", 1) }, q.TakeBatch(10_000, 100).Select(e => e.Lua));   // the toast went

        q = new BatchQueue { MaxEntries = 3 };
        q.Add(E(Door(1), at: 0)); q.Add(E(Death("d"), at: 0)); q.Add(E(Ghost("a", 1), at: 0));
        q.Add(E(Ghost("b", 1), at: 1_000));
        // no feedback, no old ordinary edge: the oldest state goes, never the one just added
        Assert.Equal(new[] { Door(1), Death("d"), Ghost("b", 1) }, q.TakeBatch(10_000, 1_000).Select(e => e.Lua));

        q = new BatchQueue { MaxEntries = 2 };
        q.Add(E(Death("d"), at: 0)); q.Add(E(Death("e"), at: 0));
        q.Add(E(Ghost("b", 1), at: 1_000));   // only world edges and the newest state: the oldest of the rest goes
        Assert.Equal(new[] { Death("e"), Ghost("b", 1) }, q.TakeBatch(10_000, 1_000).Select(e => e.Lua));
        Assert.Equal(1, q.Overflowed);
    }

    [Fact]
    public void Over_the_bound_an_ordinary_edge_older_than_a_state_lifetime_goes_before_a_state()
    {
        var q = new BatchQueue { MaxEntries = 2 };
        q.Add(E(Door(1), at: 0)); q.Add(E(Ghost("a", 1), at: 20_000));
        q.Add(E(Death("d"), at: 20_000));
        Assert.Equal(new[] { Ghost("a", 1), Death("d") }, q.TakeBatch(10_000, 20_000).Select(e => e.Lua));
    }

    [Fact]
    public void Off_the_bound_drops_the_oldest_state_first_as_before()
    {
        var q = new BatchQueue { MaxEntries = 2, EvictExpendableFirst = false };
        q.Add(E(Ghost("a", 1))); q.Add(E(Toast("t")));
        q.Add(E(Ghost("b", 1)));
        Assert.Equal(new[] { Toast("t"), Ghost("b", 1) }, q.TakeBatch(10_000, 0).Select(e => e.Lua));
    }

    // ---------------------------------------------------------------- a timed-out batch

    [Fact]
    public void A_timed_out_batch_puts_its_edges_in_front_and_its_states_behind_the_newer_work()
    {
        var q = new BatchQueue();
        q.Add(E(Toast("one"))); q.Add(E(Ghost("0", 1))); q.Add(E(Ghost("1", 1)));
        var sent = q.TakeBatch(10_000, 0);
        q.Add(E(Ghost("0", 9))); q.Add(E(Door(3)));
        q.Requeue(sent);
        Assert.Equal(new[] { Toast("one"), Ghost("0", 9), Door(3), Ghost("1", 1) }, q.TakeBatch(10_000, 0).Select(e => e.Lua));
        Assert.Equal(1, q.ReplaysDroppedSuperseded);   // ghost 0's old sample: a newer one was already queued
    }

    [Fact]
    public void A_replayed_state_is_dropped_when_a_newer_one_of_its_key_comes_after_the_timeout()
    {
        var q = new BatchQueue();
        q.Add(E(Ghost("0", 1)));
        var sent = q.TakeBatch(10_000, 0);
        q.Requeue(sent);
        q.Add(E(Ghost("0", 2)));
        Assert.Equal(new[] { Ghost("0", 2) }, q.TakeBatch(10_000, 0).Select(e => e.Lua));
        Assert.Equal(1, q.ReplaysDroppedSuperseded);
        q.Add(E(Ghost("0", 3)));   // a plain replacement (never sent) is not a replay
        Assert.Equal(1, q.ReplaysDroppedSuperseded);
    }

    // ---------------------------------------------------------------- health

    [Fact]
    public void The_health_line_has_every_key()
    {
        var h = new LuaHealth();
        h.Sent(10); h.Sent(30); h.Sent(20);
        string line = LuaHealth.Line(5, h.SentBatches, 1, 2, [3, 4, 0], 6, 7, h.AnswerMs.Take());
        foreach (var k in new[] { "MP-HEALTH area=lua ", "queued=5", "sent_batches=3", "batch_timeouts=1", "replays_dropped_superseded=2",
                                  "edges_expired=7", "edges_expired_by_kind=feedback:3,normal:4,world:0", "evicted=6", "recovery_drains=7",
                                  "answer_ms_p50=20", "answer_ms_p95=30" })
            Assert.Contains(k, line);
        Assert.Equal((0d, 0d), h.AnswerMs.Take());   // the window starts over
    }
}
