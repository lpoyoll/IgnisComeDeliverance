// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;

namespace KcdMp.Client;

/// <summary>
/// KCT: a UTC <see cref="DateTime"/> that never steps. Anchored to the wall clock once at start, then advanced by the monotonic
/// QPC counter, so a Windows time sync or a user changing the clock mid-session cannot stretch or cut a hold, a freshness test or a
/// grace (the agent's ~200 timing uses of DateTime.UtcNow: a backward step held old state for the size of the step, a forward one
/// expired holds early). Use the wall clock (DateTime.UtcNow) only where a time is compared with a file's timestamp (saves).
/// </summary>
public static class MonoClock
{
    private static readonly DateTime s_anchorUtc = DateTime.UtcNow;
    private static readonly long s_anchorTs = Stopwatch.GetTimestamp();

    public static DateTime UtcNow => s_anchorUtc.AddTicks((long)((Stopwatch.GetTimestamp() - s_anchorTs) * (10_000_000.0 / Stopwatch.Frequency)));
}
