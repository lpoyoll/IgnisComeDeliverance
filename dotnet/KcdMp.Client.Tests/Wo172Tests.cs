// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-172 (docs/WO-172-findings.md): 0.48.8's agent rules -- R1 (the riding bit this player sends), H1 (the trade screen's own UI
/// sounds), the switches. The Lua halves are tools/Test-WO172Synthetic.lua; C1's native gate is native/tests/wo172_tests.cpp.
/// </summary>
public class Wo172Tests
{
    [Fact]
    public void New_mechanisms_ship_on()
    {
        Assert.True(Wo172Rules.DefaultDeadWins);
        Assert.True(Wo172Rules.DefaultRideRepair);
    }

    [Fact]
    public void The_riding_bit_survives_the_engine_clearing_its_horse_tag()
    {
        // the field (the joiner, 22:16:26-22:25:47): the horse tag gone, the game's own IsMounted true every 2 s
        Assert.True(Wo172Rules.RidingFlag(nativeTag: false, repairOn: true, luaMounted: true, luaAtMs: 10_000, nowMs: 11_500));
        Assert.True(Wo172Rules.RepairedOnly(nativeTag: false, sent: true));
        // the tag alone, as before
        Assert.True(Wo172Rules.RidingFlag(nativeTag: true, repairOn: true, luaMounted: false, luaAtMs: 0, nowMs: 11_500));
        Assert.False(Wo172Rules.RepairedOnly(nativeTag: true, sent: true));
    }

    [Fact]
    public void A_stale_or_absent_mount_answer_sets_nothing()
    {
        Assert.False(Wo172Rules.RidingFlag(false, true, true, luaAtMs: 10_000, nowMs: 10_000 + Wo172Rules.MountFreshMs + 1));   // the mod went quiet (a menu, a load)
        Assert.False(Wo172Rules.RidingFlag(false, true, true, luaAtMs: 0, nowMs: 5_000));                                    // never told
        Assert.False(Wo172Rules.RidingFlag(false, true, false, luaAtMs: 10_000, nowMs: 10_500));                              // dismounted
        Assert.False(Wo172Rules.RidingFlag(false, repairOn: false, luaMounted: true, luaAtMs: 10_000, nowMs: 10_500));         // mp_ride_repair off
    }

    [Fact]
    public void The_trade_screen_sounds_are_read()
    {
        Assert.Equal(1, Wo172Rules.ApseScreen("PlayAudio: ApseOpen"));
        Assert.Equal(0, Wo172Rules.ApseScreen("PlayAudio: ApseClose"));
        Assert.Null(Wo172Rules.ApseScreen("PlayAudio: ApseCameraMove"));
        Assert.Null(Wo172Rules.ApseScreen("PlayAudio: ModalOpen"));
        Assert.Null(Wo172Rules.ApseScreen("[KCD2-MP] PlayAudio: ApseOpen"));   // a mod line quoting it is not the engine's
    }

    [Fact]
    public void The_trade_screen_sounds_reach_the_agent()
    {
        // the log tail forwards only the engine lines it is told about (the first live H1 run: the close never arrived)
        Assert.Contains(LogTailGameTransport.Wo144Prefixes, p => "PlayAudio: ApseClose".StartsWith(p, StringComparison.Ordinal));
        Assert.Contains(LogTailGameTransport.Wo144Prefixes, p => "PlayAudio: ApseOpen".StartsWith(p, StringComparison.Ordinal));
    }

    [Fact]
    public void The_mount_event_is_parsed_strictly()
    {
        Assert.True(Wo172Rules.ParseMount("1"));
        Assert.False(Wo172Rules.ParseMount("0"));
        Assert.Null(Wo172Rules.ParseMount("yes"));
        Assert.Null(Wo172Rules.ParseMount(null));
    }
}
