// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT QoL (IgnisComeDeliverance): the packet loss the in-game connection line shows, from Steam's delivery qualities.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

public class IcdConnTests
{
    [Theory]
    [InlineData(1.0f, 1.0f, 0)]      // everything delivered both ways
    [InlineData(0.97f, 1.0f, 3)]     // ours loses 3%
    [InlineData(1.0f, 0.88f, 12)]    // the partner's side is worse: it counts
    [InlineData(-1f, 0.95f, 5)]      // ours unknown: the partner's
    [InlineData(0.9f, -1f, 10)]      // the partner's unknown: ours
    [InlineData(-1f, -1f, -1)]       // neither known (Steam still measuring): not shown
    [InlineData(1.2f, 1.0f, 0)]      // out of range is clamped, never negative loss
    public void Loss_is_the_worse_side_in_percent(float qLocal, float qRemote, int expected) =>
        Assert.Equal(expected, GameBridge.LinkLossPct(qLocal, qRemote));
}
