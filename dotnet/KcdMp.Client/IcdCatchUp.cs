// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT QoL (IgnisComeDeliverance): the host pauses while the joiner catches up -- the pure rules (GameBridge.IcdCatchUp.cs acts on them).
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// KCT fix (icd13): how old a state frame in its own lane may be before it is history. The field's joiner replayed NPC animation
/// states 30-70 s late and activity rows 330 s late; a newer frame carries the state (WO-210's ask repairs a body that missed one).
/// </summary>
public static class IcdLaneRules
{
    public const long AnimMaxAgeMs = 3_000;       // WO-208 A.2: the host's NPC animation states
    public const long ActivityMaxAgeMs = 15_000;  // WO-141 / WO-143: the host's NPC activity rows

    public static bool TooOld(int type, long waitMs) => type switch
    {
        Protocol.NpcAnimDown => waitMs > AnimMaxAgeMs,
        Protocol.ActivityHostDown or Protocol.ActivityExtraDown => waitMs > ActivityMaxAgeMs,
        _ => false,
    };
}

/// <summary>
/// A joiner whose game runs behind (its agent's queue: the game takes the host's world late -- in the background, busy, or a slow
/// PC) falls further behind while the host's world runs on, its copies of the host's NPCs go quiet, are handed back to its own AI and
/// fought over again later (the field's 30 s, a 265 MB log of over-full action queues). So the host's world is held -- the engine's own
/// whole-world pause, the one a join uses -- while the joiner is more than <c>thresholdS</c> behind, and runs again once it is under 1 s.
/// Never more than <see cref="MaxHoldS"/> at a time, never while a join or another hold runs, never when the joiner stops reporting.
/// </summary>
public static class IcdCatchUpRules
{
    public const long ReportEveryMs = 2000;   // the joiner's report
    public const long ResumeAtMs = 1000;      // caught up: under 1 s behind
    public const int MaxHoldS = 60;           // the longest the host's world waits at a time
    public const long SilentMs = 10_000;      // no report for 10 s: the joiner is gone or stuck -- the world runs again
    public const long CooldownAfterMaxMs = 30_000, CooldownMs = 5_000;
    public const int MinThresholdS = 2, MaxThresholdS = 60;

    public enum Act { None, Hold, Release }

    /// <summary>The launcher's setting as the agent's word: 0 = off, else clamped to 2..60 s.</summary>
    public static int Threshold(bool on, int seconds) => !on ? 0 : seconds < MinThresholdS ? MinThresholdS : seconds > MaxThresholdS ? MaxThresholdS : seconds;

    /// <summary>The joiner's report: "v1|&lt;ms behind&gt;".</summary>
    public static string Text(long lagMs) => "v1|" + (lagMs < 0 ? 0 : lagMs).ToString(CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out long lagMs)
    {
        lagMs = 0;
        var p = (text ?? "").Split('|');
        return p.Length == 2 && p[0] == "v1" && long.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out lagMs) && lagMs < 3_600_000;
    }

    /// <summary>One decision, on a report (or the host's own tick: lagMs = -1, nothing reported now).</summary>
    public static (Act Act, string Why) Decide(bool holding, long lagMs, int thresholdS, long nowMs, long holdSinceMs, long lastReportMs,
                                               long cooldownUntilMs, bool busy)
    {
        if (holding)
        {
            if (thresholdS <= 0) return (Act.Release, "the setting was turned off");
            if (nowMs - holdSinceMs >= MaxHoldS * 1000L) return (Act.Release, $"held {MaxHoldS} s, the longest it waits");
            if (nowMs - lastReportMs >= SilentMs) return (Act.Release, "no report from the joiner for 10 s");
            if (lagMs >= 0 && lagMs <= ResumeAtMs) return (Act.Release, "the joiner caught up");
            return (Act.None, "");
        }
        if (thresholdS <= 0 || lagMs < 0 || busy || nowMs < cooldownUntilMs) return (Act.None, "");
        return lagMs >= thresholdS * 1000L ? (Act.Hold, $"the joiner is {lagMs / 1000.0:F0} s behind") : (Act.None, "");
    }
}
