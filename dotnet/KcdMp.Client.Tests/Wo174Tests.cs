// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-174: the agent's pure rules (docs/WO-174-findings.md).</summary>
public class Wo174Tests
{
    // the real field lines (2026-10-10 host log)
    private const string AutoSave = "InitiateSaveGame() type: AutoSave, overwriteSaveId: -1, questNameOverride: ''";
    private const string QuestSave = "InitiateSaveGame() type: AutoSave, overwriteSaveId: -1, questNameOverride: '@qname_koneczabomysichvalek_BRee|@jmena_obj_zacatek_questu_JJyn'";
    private const string ExitSave = "InitiateSaveGame() type: ExitSave, overwriteSaveId: -1, questNameOverride: ''";
    private const string Switch = "InitiateSaveGame() type: LevelSwitchSave, overwriteSaveId: -1, questNameOverride: ''";

    [Theory]
    [InlineData(AutoSave)]
    [InlineData(QuestSave)]
    [InlineData(ExitSave)]
    public void The_games_own_save_lines_are_loadable_save_initiations(string line)
    {
        Assert.True(Wo174Rules.IsSaveInitiation(line));
        Assert.True(Wo174Rules.IsLoadableSaveKind(line));
    }

    [Fact]
    public void A_level_switch_save_is_an_initiation_but_not_a_loadable_kind()
    {
        Assert.True(Wo174Rules.IsSaveInitiation(Switch));
        Assert.False(Wo174Rules.IsLoadableSaveKind(Switch));
    }

    [Theory]
    [InlineData("[KCD2-MP] InitiateSaveGame() type: AutoSave")]           // a mod line that quotes it
    [InlineData("Sending E_MMI_SaveGameRequestBegin for savegame type AutoSave, questNameOverride: ''")]
    [InlineData("  InitiateSaveGame() type: AutoSave")]                    // not at the line start
    [InlineData("")]
    public void A_line_that_only_quotes_or_resembles_it_is_not_one(string line)
    {
        Assert.False(Wo174Rules.IsSaveInitiation(line));
        Assert.False(Wo174Rules.IsLoadableSaveKind(line));
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData(" 17 ", 17)]
    [InlineData("255", 255)]
    [InlineData("3 extra words", 3)]
    public void A_ghost_id_parses(string arg, int expected)
    {
        Assert.True(Wo174Rules.TryParseGhost(arg, out byte g));
        Assert.Equal(expected, g);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-1")]
    [InlineData("256")]
    [InlineData("ghost")]
    public void A_malformed_ghost_id_is_refused(string? arg) => Assert.False(Wo174Rules.TryParseGhost(arg, out _));

    [Fact]
    public void The_why_texts_say_what_happens()
    {
        Assert.Contains("friendly-fire", Wo174Rules.FriendlyFireWhy);
        Assert.Contains("partner left", Wo174Rules.PartnerLeftWhy);
    }

    [Theory]
    [InlineData("rvacka_neighbour_1", true)]
    [InlineData("RVACKA_apprentice_3", true)]
    [InlineData("fistfightsChampion_champion", true)]
    [InlineData("tbuk_duel_man_1", true)]
    [InlineData("arena_fighter_2", true)]
    [InlineData("tournament_knight_4", true)]
    [InlineData("ttac_man_9", false)]
    [InlineData("prepadeniNaCeste_bandit_5", false)]
    [InlineData("kcd2mp_1", false)]
    [InlineData("Dude", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_scripted_brawls_actors_are_scene_owned_and_nobody_else(string? name, bool expected) => Assert.Equal(expected, Wo174Rules.SceneOwned(name));

    [Fact]
    public void The_scene_words_are_the_native_and_luas_words()
    {
        Assert.Equal(new[] { "rvacka", "fistfight", "brawl", "duel", "arena", "tournament" }, Wo174Rules.SceneWords);
    }
}
