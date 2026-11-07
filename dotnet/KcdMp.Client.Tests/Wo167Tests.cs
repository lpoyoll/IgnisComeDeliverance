// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-167 (docs/WO-167-findings.md): 0.48.5's agent rules -- T2 (the host's collision bark), T3 (the engine's behaviour-tree paths),
/// T4 (a conversation's interactive scene that never ends). The Lua halves are tools/Test-WO167Synthetic.lua.
/// </summary>
public class Wo167Tests
{
    // ---------------------------------------------------------------- T4: the scene

    [Fact]
    public void A_held_intermission_scene_is_released_at_20_seconds()
    {
        // the jugglers: two scenes queued, one ended, the trialog ended at t=1000 and nothing since
        Assert.False(Wo167Rules.SceneStuck(2, 1, 1000, 0, 0, 1000 + 19_999));
        Assert.True(Wo167Rules.SceneStuck(2, 1, 1000, 0, 0, 1000 + 20_000));
        Assert.True(Wo167Rules.SceneStuck(2, 1, 1000, 0, 0, 1000 + 176_000));
    }

    [Fact]
    public void A_scene_that_ends_with_its_dialogue_is_never_touched()
    {
        Assert.False(Wo167Rules.SceneStuck(1, 1, 1000, 1000, 0, 1000 + 60_000));   // ended: nothing runs
        Assert.False(Wo167Rules.SceneStuck(3, 3, 1000, 0, 0, 1000 + 60_000));
        Assert.False(Wo167Rules.SceneStuck(2, 1, 0, 0, 0, 1000 + 60_000));         // no dialogue ended (a cutscene's own scene)
    }

    [Fact]
    public void Progress_restarts_the_20_seconds_and_a_scene_is_released_once()
    {
        Assert.False(Wo167Rules.SceneStuck(2, 1, 1000, 15_000, 0, 30_000));   // a fader finished at 15 s: 15 s of quiet only
        Assert.True(Wo167Rules.SceneStuck(2, 1, 1000, 15_000, 0, 35_000));
        Assert.False(Wo167Rules.SceneStuck(2, 1, 1000, 0, 1000, 60_000));      // released already for this dialogue end
    }

    [Fact]
    public void The_scene_manager_lines_are_read()
    {
        Assert.Equal("IsmEnqueue", Wo167Rules.IsmStage("InteractiveSceneManager::EnqueueScene called"));
        Assert.Equal("IsmEnd", Wo167Rules.IsmStage("InteractiveSceneManager::EndScene called for "));
        Assert.Equal("IsmCleanup", Wo167Rules.IsmStage("InteractiveSceneManager::CleanupScene called for Fader cutscene 'dice_preMinigameFader' with holder 'x'"));
        Assert.Equal("IsmInterrupt", Wo167Rules.IsmStage("InteractiveSceneManager::Interrupt called for x"));
        Assert.Null(Wo167Rules.IsmStage("InteractiveSceneManager is running scene"));
        Assert.Null(Wo167Rules.IsmStage("Readiness observer 'InteractiveScenePlayer' with category bitmask '16273' is ready"));
        Assert.Equal(102.99, Wo167Rules.FaderTook("Last fader took 102.99s")!.Value, 3);
        Assert.Equal(0.41, Wo167Rules.FaderTook("Last fader took 0.41s")!.Value, 3);
        Assert.Null(Wo167Rules.FaderTook("Duration profile was not saved because fader took less than 2.00s"));
    }

    // ---------------------------------------------------------------- T2: the collision bark

    [Fact]
    public void The_collision_bark_names_its_npc()
    {
        const string line = "New dialogue 'Barbora.open_world.crime_reaction_barks.kolize.npc_je_s_hracem_pratelske_a_hrac_do_nej_vrazil__zena (ingame monolog - Id: 1845) (AI::DoMonologue)' is starting. Params: souls = 'Ex: tzel_woman_10 - meta override: PRATELSKE_NPC_DOSTALO_ZASAH_(KOLIZE)' forced = true, forced decision = none";
        Assert.Equal("tzel_woman_10", Wo167Rules.KolizeSoul(line));
        Assert.Null(Wo167Rules.KolizeSoul("New dialogue 'Barbora.open_world.shop.nakupovani_z_dialogu_zena (normal - Id: 166) (Actor::RequestDialog)' is starting. Params: souls = 'Ex: Dude; Ex: tzel_woman_10' forced = false"));
        Assert.Null(Wo167Rules.KolizeSoul("Attempting to start new dialogue (runtime id '1845') with souls 'Ex: tzel_woman_10 - meta override: PRATELSKE_NPC_DOSTALO_ZASAH_(KOLIZE)'"));
    }

    // ---------------------------------------------------------------- T3: the behaviour tree

    [Fact]
    public void A_behaviour_tree_path_is_read_from_the_engines_lines()
    {
        var a = Wo167Rules.BtPath("/ai//(NPC)tzel_rowdy_3/(Base-SUBB)npc_basic_scheduler/(BT)dice_interrupt_playDiceMinigame/(BT tmpl)world:so_dicetable_new.xml:dice_interrupt_playDiceMinigame/(N)Root:1 [Node]:Node status inconsistency. Can't update");
        Assert.NotNull(a);
        Assert.Equal("tzel_rowdy_3", a!.Value.Npc);
        Assert.Equal("npc_basic_scheduler/dice_interrupt_playDiceMinigame", a.Value.Path);
        var b = Wo167Rules.BtPath("/ai//(NPC)tzel_rowdy_3/NPC Context [NPCContext]:Execution of action NonAlignedAnimAction has failed! Action for request '/(NPC)tzel_rowdy_3/(Base-SUBB)npc_basic_scheduler/(BT)dice_playEndReaction/dice_interrupt_pla");
        Assert.Equal("npc_basic_scheduler/dice_playEndReaction", b!.Value.Path);
        Assert.Null(Wo167Rules.BtPath("[Error] /ai//(NPC)tzel_adam/NPC Context [NPCContext]:Couldn't find actions to get NPC into game loaded state"));
        Assert.Null(Wo167Rules.BtPath("no npc here (BT)x"));
    }

    // ---------------------------------------------------------------- C2: the weapon a copy holds

    [Fact]
    public void A_row_says_armed_or_unarmed_by_its_right_hand()
    {
        Assert.False(Wo167Rules.RowArmed("r_noweapon+leftGuard+eZ1+aZ1+hook+attack_heavy+l_noShield+oppMale"));
        Assert.True(Wo167Rules.RowArmed("leftGuard+eZ1+aZ1+slash+attack_heavy+r_bluntWeapon+l_noShield+oppMale+oppFemale"));
        Assert.True(Wo167Rules.RowArmed("clinch1+eZ1+endClinch1+aZ2+slash+attack_heavy+r_stickWeapon+l_noShield+step0+oppMale"));
        Assert.Null(Wo167Rules.RowArmed("leftGuard+eZ1+aZ1"));
        Assert.True(Wo167Rules.IsWeaponKind("MeleeWeapon") && Wo167Rules.IsWeaponKind("MissileWeapon"));
        Assert.False(Wo167Rules.IsWeaponKind("Tool") || Wo167Rules.IsWeaponKind("Armor") || Wo167Rules.IsWeaponKind(null));
    }

    // ---------------------------------------------------------------- C3: shown means rendered

    [Fact]
    public void A_row_that_never_animated_gets_the_lunge()
    {
        // received 400 ms before the verdict, refused as no-anim (the watch took it out of the played rows)
        var (shown, reason) = Wo161Rules.Judge(true, false, false, null, 400, Wo167Rules.ReasonNoAnim);
        Assert.False(shown);
        Assert.Equal("no-anim", reason);
        Assert.True(Wo161Rules.WantsGenericSwing(shown, reason));
        // a row that did animate is shown as before
        var (s2, r2) = Wo161Rules.Judge(true, false, false, 400, 400, null);
        Assert.True(s2);
        Assert.Equal("-", r2);
        // another refusal keeps WO-163's rule (no lunge for a row the DLL refused outright)
        var (s3, r3) = Wo161Rules.Judge(true, false, false, null, 400, "target-missing");
        Assert.Equal("row-refused-target-missing", r3);
        Assert.False(Wo161Rules.WantsGenericSwing(s3, r3));
    }

    [Fact]
    public void The_watch_takes_a_row_back_out_of_the_played_log()
    {
        var log = new Wo161PlayedLog();
        log.Note(1000, 600);
        log.Note(2000, 600);
        Assert.True(log.Retract(2010));          // the row noted 10 ms away from the play call's time
        Assert.False(log.Retract(2010));         // once
        var best = log.Best(2500);
        Assert.NotNull(best);
        Assert.Equal(1500, best!.Value.Ago);     // only the first row is left
        Assert.Equal("installed", Wo167Rules.SeenName(Wo167Rules.SeenInstalled));
        Assert.Equal("no-anim", Wo167Rules.SeenName(Wo167Rules.SeenNoAnim));
    }

    // ---------------------------------------------------------------- C4: paired attacks on stand-ins

    private static readonly Guid SyncG = Guid.Parse("72c05fa9-6921-36ee-bbd1-8647478dfc21");
    private static ActionRowCatalog.Row SyncRow => new("combat_action_sync_attack", "CombatAttackSyncGen",
        "clinch1+eZ1+endClinch1+aZ2+slash+attack_heavy+r_stickWeapon+l_noShield+step0+oppMale", 2, 0, 1);

    [Fact]
    public void A_sync_row_falls_back_to_the_plain_attack_of_the_same_weapon()
    {
        Assert.True(Wo167Rules.IsSyncFragment("CombatAttackSyncGen"));
        Assert.False(Wo167Rules.IsSyncFragment("CombatAttackGen"));
        var plainStick = Guid.NewGuid(); var plainStickLight = Guid.NewGuid(); var plainSword = Guid.NewGuid(); var lying = Guid.NewGuid(); var kick = Guid.NewGuid();
        var cat = ActionRowCatalog.FromRows(new[]
        {
            (SyncG, SyncRow),
            (plainStick, new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "leftGuard+eZ1+aZ2+slash+attack_heavy+r_stickWeapon+l_noShield+oppMale", 2, 0, 1)),
            (plainStickLight, new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "leftGuard+eZ1+aZ3+stab+attack_light+r_stickWeapon+l_noShield+oppMale", 3, 0, 2)),
            (plainSword, new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "leftGuard+eZ1+aZ2+slash+attack_heavy+r_sword+l_noShield+oppMale", 2, 0, 1)),
            (lying, new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "eZ1+aZ2+slash+attack_heavy+r_stickWeapon+l_noShield+oppLying", 2, 0, 1)),
            (kick, new ActionRowCatalog.Row("combat_action_attack", "CombatAttackSpecialGen", "eZ2+aZ5+kick+attack_heavy+r_stickWeapon+l_noShield", 5, 0, 4)),
        });
        var p = cat.PlainForSync(SyncRow);
        Assert.NotNull(p);
        Assert.Equal(plainStick, p!.Value.Guid);   // same weapon, type, strength, zone, left hand -- never the sword, the lying row or the kick
        Assert.Equal(-1, Wo167Rules.PlainScore(SyncRow.Tags, 1, "aZ2+slash+attack_heavy+r_sword", 1));
        var none = ActionRowCatalog.FromRows(new[] { (SyncG, SyncRow), (plainSword, cat.TryGet(plainSword, out var sw) ? sw : default) });
        Assert.Null(none.PlainForSync(SyncRow));
    }

    // ---------------------------------------------------------------- FT1-FT4: one shared fast travel

    [Fact]
    public void Fast_travel_is_on_by_default()
    {
        Assert.True(Wo154MenuRules.FastTravelDefault);
    }

    [Fact]
    public void The_travel_texts_round_trip()
    {
        Assert.True(W164Text.TryParseTravel(W164Text.TravelRequest(7, "@fastTravel_trosecko"), out char v, out uint seq, out string arg, out _));
        Assert.Equal(('r', 7u, "@fastTravel_trosecko"), (v, seq, arg));
        Assert.True(W164Text.TryParseTravel(W164Text.TravelStart(8, "1551.5,2277.4,17.6"), out v, out seq, out arg, out _));
        Assert.Equal(('s', 8u, "1551.5,2277.4,17.6"), (v, seq, arg));
        Assert.True(W164Text.TryParseTravel(W164Text.TravelStart(9, "-"), out v, out _, out arg, out _));
        Assert.Equal(('s', "-"), (v, arg));
        Assert.True(W164Text.TryParseTravel(W164Text.TravelEnd(9, 1580.04f, 2327.33f, 19.38f, true), out v, out _, out arg, out bool interrupted));
        Assert.Equal(('e', "1580.0,2327.3,19.4"), (v, arg));
        Assert.True(interrupted);
        Assert.True(W164Text.TryParseTravel(W164Text.TravelRefused(10, "combat"), out v, out _, out arg, out _));
        Assert.Equal(('n', "combat"), (v, arg));
        foreach (var t in W164TextLengths()) Assert.True(t.Length <= Protocol.W164TextMax, t);
    }

    private static IEnumerable<string> W164TextLengths() => new[]
    {
        W164Text.TravelRequest(uint.MaxValue, "@" + new string('a', 48)),
        W164Text.TravelStart(uint.MaxValue, "-99999.9,-99999.9,-99999.9"),
        W164Text.TravelEnd(uint.MaxValue, -99999.9f, -99999.9f, -99999.9f, true),
        W164Text.TravelRefused(uint.MaxValue, "unknown-destination"),
    };

    [Fact]
    public void A_bad_travel_text_is_refused()
    {
        Assert.False(W164Text.TryParseTravel("r 1 @bad name", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseTravel("r 1 @" + new string('a', 49), out _, out _, out _, out _));
        Assert.False(W164Text.TryParseTravel("s 1 1,2", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseTravel("s 1 1,2,NaN", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseTravel("s 1 999999,2,3", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseTravel("e 1 1,2,3 2", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseTravel("n 1 Combat!", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseTravel("x 1 -", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseTravel("r -1 -", out _, out _, out _, out _));
        Assert.Equal("combat", Wo167Rules.RefusalWord("combat"));
        Assert.Equal("not-now", Wo167Rules.RefusalWord("timeout because"));
        Assert.Equal("not-now", Wo167Rules.RefusalWord(""));
    }

    [Fact]
    public void The_pull_after_a_shared_travel_waits_for_the_joiners_own_run()
    {
        var l = new LeashLogic { Config = LeashLogic.Settings.Default };
        long t = 0;
        l.NoteHostFastTravel();
        var held = new List<LeashLogic.Action>();
        for (int i = 0; i < 160; i++) { t += 250; held.AddRange(l.Tick(t, 2400, LeashLogic.JoinerHold(Protocol.LeashFlagInWorld | Protocol.LeashFlagTravelling))); }
        Assert.DoesNotContain(held, a => a.Kind == LeashLogic.Act.Pull);   // 40 s of the joiner's own run: never pulled mid-travel (no cap)
        Assert.True(l.FastTravelPending);
        var after = new List<LeashLogic.Action>();
        for (int i = 0; i < 2; i++) { t += 250; after.AddRange(l.Tick(t, 2400, LeashLogic.JoinerHold(Protocol.LeashFlagInWorld))); }
        var p = Assert.Single(after, a => a.Kind == LeashLogic.Act.Pull);
        Assert.Equal(Protocol.LeashReasonFastTravel, Protocol.LeashReasonBase(p.Reason));   // its run over: beside the host (wherever it stopped)
        Assert.Equal("joiner-travelling", LeashLogic.HoldText(LeashLogic.Hold.JoinerTravelling));
        Assert.Contains("travelling", Protocol.LeashFlagsText(Protocol.LeashFlagTravelling));
    }

    [Fact]
    public void The_engines_sync_guard_line_names_its_npc()
    {
        Assert.Equal("taboryUCesty_bandit_10", Wo167Rules.SyncGuardNpc("Cannot change sync guard on taboryUCesty_bandit_10 because data not found for sync movement for slave."));
        Assert.Null(Wo167Rules.SyncGuardNpc("Perfect block Slot started with planned window length: 0.295 s"));
    }

    [Fact]
    public void A_verdict_waits_for_the_swing_watch_but_at_most_a_second()
    {
        // live (WO-167's soak): verdicts came 220 ms after their swing, before the watch's ~600 ms answer -- the wait must cover its last look
        Assert.True(Wo167Rules.WatchVerdictCapMs >= Wo167Rules.ShownSecondLookMs + 250 + 250);   // the last look, its answer, the scheduling
        Assert.True(Wo167Rules.WatchVerdictCapMs <= 1000);
    }

    [Fact]
    public void A_fault_names_the_feature_it_switched_off()
    {
        string[] features = ["shared-travel", "scene-release", "swing-watch", "stand-in-pulse", "talk-clear", "talk-readiness"];
        var texts = features.Select(Wo167Rules.FaultText).ToList();
        Assert.Equal(features.Length, texts.Distinct().Count());
        Assert.All(texts, t => Assert.EndsWith(" off this session (a game error).", t));
        Assert.Equal("A co-op feature is off this session (a game error).", Wo167Rules.FaultText("something-else"));
        Assert.Equal("fault", W164Text.TryParseTravel("n 3 fault", out _, out _, out string why, out _) ? why : null);   // the refusal word
    }

    [Fact]
    public void A_travel_stopped_by_a_road_event_counts_as_interrupted()
    {
        // live (WO-167): an ambush on the route printed the event line and no "Interrupting FastTravel"
        Assert.True(Wo167Rules.IsTravelInterruptLine("FastTravel: random event variant 'ambush_npc_man' with option set 'AmbushNPC_Event' answered with 'HelpingFight' <'Success'> stopping FastTravel: 'Y'"));
        Assert.True(Wo167Rules.IsTravelInterruptLine("FastTravel: random event variant 'a' with option set 'b' answered with 'c' <'Success'> stopping FastTravel: 'Y'  "));
        Assert.True(Wo167Rules.IsTravelInterruptLine("Interrupting FastTravel..."));
        Assert.False(Wo167Rules.IsTravelInterruptLine("FastTravel: random event variant 'a' with option set 'b' answered with 'c' <'Fail'> stopping FastTravel: 'N'"));
        Assert.False(Wo167Rules.IsTravelInterruptLine("FastTravel: ended..."));
        Assert.False(Wo167Rules.IsTravelInterruptLine("Setting active random event for fast travel, event 'prepadeniNaCeste' with variant 'ambush_npc_man'"));
        Assert.False(Wo167Rules.IsTravelInterruptLine(null));
    }
}
