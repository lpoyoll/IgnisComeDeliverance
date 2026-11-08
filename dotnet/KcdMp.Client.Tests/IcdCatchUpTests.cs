// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT QoL (IgnisComeDeliverance): the host pauses while the joiner catches up.
using KcdMp.Client;
using Xunit;
using A = KcdMp.Client.IcdCatchUpRules.Act;

namespace KcdMp.Client.Tests;

public class IcdCatchUpTests
{
    private static A D(bool holding, long lag, int thr = 5, long now = 100_000, long since = 0, long lastReport = 100_000, long cooldown = 0, bool busy = false) =>
        IcdCatchUpRules.Decide(holding, lag, thr, now, since, lastReport, cooldown, busy).Act;

    [Fact]
    public void The_setting_is_off_or_clamped_to_2_to_60_seconds()
    {
        Assert.Equal(0, IcdCatchUpRules.Threshold(false, 5));
        Assert.Equal(5, IcdCatchUpRules.Threshold(true, 5));
        Assert.Equal(2, IcdCatchUpRules.Threshold(true, 0));
        Assert.Equal(60, IcdCatchUpRules.Threshold(true, 600));
    }

    [Fact]
    public void The_report_round_trips_and_rejects_junk()
    {
        Assert.True(IcdCatchUpRules.TryParse(IcdCatchUpRules.Text(29444), out long ms) && ms == 29444);
        Assert.Equal("v1|0", IcdCatchUpRules.Text(-5));
        Assert.False(IcdCatchUpRules.TryParse("v2|10", out _));
        Assert.False(IcdCatchUpRules.TryParse("v1|-3", out _));
        Assert.False(IcdCatchUpRules.TryParse("v1|99999999999", out _));
        Assert.False(IcdCatchUpRules.TryParse(null, out _));
    }

    [Fact]
    public void The_world_holds_past_the_threshold_and_not_before()
    {
        Assert.Equal(A.Hold, D(false, 5000));
        Assert.Equal(A.Hold, D(false, 29444));       // the field's 30 s
        Assert.Equal(A.None, D(false, 4999));
        Assert.Equal(A.None, D(false, 29444, thr: 0));   // off
        Assert.Equal(A.None, D(false, 29444, busy: true));   // a join or a link hold runs
        Assert.Equal(A.None, D(false, 29444, cooldown: 100_001));   // just released
        Assert.Equal(A.None, D(false, -1));          // the host's own tick: nothing reported
    }

    [Fact]
    public void A_hold_ends_when_caught_up_at_its_limit_or_when_the_joiner_goes_quiet()
    {
        Assert.Equal(A.None, D(true, 3000, since: 90_000));            // still catching up
        Assert.Equal(A.Release, D(true, 1000, since: 90_000));         // under 1 s: caught up
        Assert.Equal(A.Release, D(true, 20_000, since: 40_000));       // 60 s held
        Assert.Equal(A.Release, D(true, -1, since: 95_000, lastReport: 90_000));   // 10 s without a report
        Assert.Equal(A.None, D(true, -1, since: 95_000, lastReport: 95_000));      // the tick between reports
        Assert.Equal(A.Release, D(true, 20_000, thr: 0, since: 95_000)); // turned off while holding
    }
}

// KCT fix (icd13): stale state frames in their own lanes are dropped, nothing else.
public class IcdLaneRulesTests
{
    [Theory]
    [InlineData(KcdMp.Wire.Protocol.NpcAnimDown, 2_999, false)]
    [InlineData(KcdMp.Wire.Protocol.NpcAnimDown, 3_001, true)]
    [InlineData(KcdMp.Wire.Protocol.ActivityHostDown, 14_000, false)]
    [InlineData(KcdMp.Wire.Protocol.ActivityHostDown, 330_000, true)]
    [InlineData(KcdMp.Wire.Protocol.ActivityExtraDown, 16_000, true)]
    [InlineData(KcdMp.Wire.Protocol.NpcStateDown, 600_000, false)]
    [InlineData(KcdMp.Wire.Protocol.ConvDown, 600_000, false)]
    public void Only_old_state_frames_are_dropped(byte type, long waitMs, bool drop) =>
        Assert.Equal(drop, IcdLaneRules.TooOld(type, waitMs));
}
