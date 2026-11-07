// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-176 (docs/WO-176-findings.md): 0.50.0's agent rules -- A1's "kept out of an arranged fight" answers and the once-per-fight notice,
/// B2's replay flag, B3a's block row. The native half is native/tests/wo176_tests.cpp; the Lua half tools/Test-WO176Synthetic.lua.
/// </summary>
public class Wo176Tests
{
    [Fact]
    public void New_pieces_ship_on_with_their_switches()
    {
        Assert.True(Wo176Rules.DefaultArrangedGuard);
        Assert.True(Wo176Rules.DefaultCopyBlocks);
    }

    [Fact]
    public void Kept_out_answers_are_the_real_hit_fallback_11_and_wo121_reason_9_only()
    {
        Assert.True(Wo176Rules.KeptOut(11));
        Assert.True(Wo176Rules.KeptOut(9));
        for (byte b = 0; b < 11; b++)
            if (b != 9) Assert.False(Wo176Rules.KeptOut(b));
        Assert.Equal("arranged-fight", Wo173Rules.FallbackName(11));
        Assert.Equal("engine-core-not-run", Wo173Rules.FallbackName(10));   // the old names unchanged
    }

    [Fact]
    public void The_joiner_is_told_once_per_story_fight()
    {
        Assert.True(Wo176Rules.ShouldTell(0, 1_000));                                   // the first kept-out blow
        Assert.False(Wo176Rules.ShouldTell(1_000, 2_000));                              // the same fight a second later
        Assert.False(Wo176Rules.ShouldTell(1_000, 1_000 + Wo176Rules.TellGapMs - 1));
        Assert.True(Wo176Rules.ShouldTell(1_000, 1_000 + Wo176Rules.TellGapMs));       // a new fight after ten quiet minutes
        Assert.Contains("belongs to the story", Wo176Rules.StoryFightText);
        Assert.Contains("your partner fights it", Wo176Rules.StoryFightText);
    }

    [Fact]
    public void A_fatal_packet_for_a_kept_out_actor_is_the_storys_for_a_minute()
    {
        Assert.True(Wo176Rules.StillArranged(10_000, 10_500));
        Assert.True(Wo176Rules.StillArranged(10_000, 10_000 + Wo176Rules.ArrangedMemoryMs - 1));
        Assert.False(Wo176Rules.StillArranged(10_000, 10_000 + Wo176Rules.ArrangedMemoryMs));
        Assert.False(Wo176Rules.StillArranged(0, 5));
        Assert.False(Wo176Rules.StillArranged(10_000, 9_000));   // a clock that went back never says yes
    }

    [Fact]
    public void Story_fight_is_w164_kind_9_append_only()
    {
        Assert.Equal(9, Protocol.W164StoryFight);
        Assert.Equal("story-fight", Protocol.W164KindName(Protocol.W164StoryFight));
        Assert.Equal(8, Protocol.W164NpcDown);   // the older kinds keep their numbers
        Assert.Equal(7, Protocol.W164RealHit);
    }

    [Fact]
    public void Cfg_parses_both_switches_and_ignores_the_rest()
    {
        var d = Wo176Rules.ParseCfg("arranged_guard=off copy_blocks=on bogus=on copy_blocks=maybe x").ToList();
        Assert.Equal(new[] { ("arranged_guard", false), ("copy_blocks", true) }, d);
        Assert.Empty(Wo176Rules.ParseCfg(null));
    }

    [Fact]
    public void B2_replay_flags_refer_a_blocking_player_only_when_the_copy_is_not_striking()
    {
        Assert.Equal(0x01, Wo165Rules.ReplayFlags & 0x01);   // the repeat filter skipped
        Assert.Equal(0x10, Wo165Rules.ReplayFlags & 0x10);   // a blocking player referred ...
        Assert.Equal(0x80, Wo165Rules.ReplayFlags & 0x80);   // ... unless the copy is in its striking window (the engine judges the block)
        Assert.Equal(0, Wo165Rules.ReplayFlags & (0x20 | 0x40));   // never the reaction tail / intended mark on the player
    }

    [Fact]
    public void B3a_block_action_follows_the_verdict_and_never_doubles_a_host_reaction()
    {
        Assert.Equal(ActionRowCatalog.ActionBlock, Wo176Rules.BlockActionFor(HitVerdict.Blocked, 0));
        Assert.Equal(ActionRowCatalog.ActionBlock, Wo176Rules.BlockActionFor(HitVerdict.Parried, 3));
        Assert.Equal(ActionRowCatalog.ActionPerfectBlock, Wo176Rules.BlockActionFor(HitVerdict.PerfectBlock, 0));
        Assert.Equal(ActionRowCatalog.ActionBlockBroken, Wo176Rules.BlockActionFor(HitVerdict.Broken, 4));
        Assert.Equal(-1, Wo176Rules.BlockActionFor(HitVerdict.Blocked, 1));   // the host's own reaction row comes: nothing added
        Assert.Equal(-1, Wo176Rules.BlockActionFor(HitVerdict.Hit, 0));
        Assert.Equal(-1, Wo176Rules.BlockActionFor(HitVerdict.None, 0));
    }

    private static ActionRowCatalog.Row R(string table, string tags, int action) => new(table, "F", tags, -1, -1, -1, action);

    [Fact]
    public void B3a_block_row_matches_the_copys_weapon_exactly_prefers_the_blows_zone_and_never_a_player_row()
    {
        var swing = R("combat_action_attack", "l_noShield+r_shortSwords+aZ2+oppMale", 26);
        Guid gPlayer = new("00000000-0000-0000-0000-000000000001"), gA = new("00000000-0000-0000-0000-000000000002"),
             gB = new("00000000-0000-0000-0000-000000000003"), gShield = new("00000000-0000-0000-0000-000000000004"),
             gPb = new("00000000-0000-0000-0000-000000000005"), gBroken = new("00000000-0000-0000-0000-000000000006");
        var cat = ActionRowCatalog.FromRows(new[]
        {
            (gPlayer, R("combat_action_block", "l_noShield+r_shortSwords+player+sZ1+eZ1+dZ1+oppMale", 6)),
            (gA, R("combat_action_block", "l_noShield+r_shortSwords+sZ1+eZ1+dZ1+oppMale", 6)),
            (gB, R("combat_action_block", "l_noShield+r_shortSwords+sZ2+eZ2+dZ2+oppMale", 6)),
            (gShield, R("combat_action_block", "l_shield+r_shortSwords+sZ1+eZ1+dZ1+oppMale", 6)),
            (gPb, R("combat_action_perfect_block", "l_noShield+r_shortSwords+rightGuard+eZ1+dZ1+oppMale", 14)),
            (gBroken, R("combat_action_block", "l_noShield+r_shortSwords+sZ1+dZ1+oppMale", 8)),
        });
        var any = cat.BlockRow(swing, ActionRowCatalog.ActionBlock, -1);
        Assert.NotNull(any);
        Assert.DoesNotContain("player", any!.Value.Tags);
        Assert.DoesNotContain("l_shield", any.Value.Tags);
        Assert.Equal("l_noShield+r_shortSwords+sZ1+eZ1+dZ1+oppMale", any.Value.Tags);   // no zone asked: the lowest tags
        Assert.Equal("l_noShield+r_shortSwords+sZ2+eZ2+dZ2+oppMale", cat.BlockRow(swing, ActionRowCatalog.ActionBlock, 2)!.Value.Tags);
        Assert.Equal(14, cat.BlockRow(swing, ActionRowCatalog.ActionPerfectBlock, -1)!.Value.ActionType);
        Assert.Equal(8, cat.BlockRow(swing, ActionRowCatalog.ActionBlockBroken, -1)!.Value.ActionType);
        // a weapon the table has no block of, and a swing row without weapon tags: none (the caller says why)
        Assert.Null(cat.BlockRow(R("combat_action_attack", "l_halberd+r_halberd+aZ1", 26), ActionRowCatalog.ActionBlock, -1));
        Assert.Null(cat.BlockRow(R("combat_action_attack", "aZ1+oppMale", 26), ActionRowCatalog.ActionBlock, -1));
    }
}
