// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-171 L: which lines are "known noise" (a run of them is worth one line with a count) and which are never touched.
/// Noise = the per-frame sync lines of the agent and the mod, and the engine's own repeating warnings (planner / NPC-context
/// spam, animation-queue overflow, call stacks). The list is explicit: a line that matches no class is only ever collapsed when it
/// is a byte-for-byte repeat of the line before it.
/// </summary>
public static class NoiseRules
{
    /// <summary>One noise class: a name for the summary line and the pattern its lines match (after the clock prefix is cut).</summary>
    public sealed record Class(string Name, Regex Pattern);

    private static Class C(string name, string pattern) => new(name, new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant));

    public static readonly IReadOnlyList<Class> Classes = new[]
    {
        C("position stream (KCD2-MP-DATA)",        @"^\[KCD2-MP-DATA\] v\d"),
        C("event stream (KCD2-MP-EVT)",            @"^\[KCD2-MP-EVT\] v\d"),
        C("combat pipe frames",                    @"\[combat\] pipe frame "),
        C("ghost position lines",                  @"^\[ghost \d+\] .* riding="),
        C("agent position lines",                  @"^\[pos\] .* read=\d+ms"),
        C("ping lines",                            @"^\[ping\] \d+ ms"),
        C("vitals lines",                          @"^\[vitals\] sent "),
        C("read-time stat lines",                  @"^\[stat\] avg read="),
        C("missing animation warnings",            @"AnimFramePoseModifier: animation .* not found"),
        C("invalid animation reference warnings",  @"Validator: Invalid anim ref"),
        C("item moved errors",                     @"^\[Error\] Item \S+ was moved to "),
        C("combat actor init errors",              @"Combat actor init failed for actor"),
        C("OSH preset errors",                     @"OSH: m_PresetOperatorDictionary"),
        C("animation-queue overflow errors",       @"Animation-queue overflow"),
        C("NPC state search errors",               @"NPCStateSearch\]:NPC state search failed"),
        C("first shot replanning",                 @"^First shot replanning for"),
        C("camera proxy warnings",                 @"AnimCameraNode could not get camera proxy"),
        C("cinematic holder lines",                @"^Holder: .* in layer"),
        C("call stack lines",                      @"^(Callstack:|function=0x)"),
        C("soul tracking samples",                 @"^SAMPLE: tracking "),
        C("join extras (activity / leash state)",  @"\[INF\] \[join\] (activity-extra|leash-state)"),
        C("unchanged switch lines (TOGGLE)",       @"WO\d+-TOGGLE name=\S+ state=(\w+) was=\1 "),
        C("authority scan lines",                  @"WO\d+-AUTHORITY host authority ON"),
        C("NPC scan lines",                        @"MP-NPCSCAN dir="),
        C("NPC write lines (activity)",            @"MP-NPCWRITE eid=\d+ event=activity-"),
        C("gait lines",                            @"WO\d+-GAIT body="),
        C("packet lines",                          @"^\[KCD2-MP\] pkt\d+ id="),
        C("horse sync warnings",                   @"Horse and rider synchronized animation"),
        C("skirmish probe lines",                  @"^Skirmish Probe: OnSkirmishEvent"),
        C("pause console lines",                   @"Executing console command 'wh_ai_PauseNPC"),
        C("prologue check lines",                  @"^SCTX: both prologues match"),
        C("blank lines",                           @"^\s*$"),
    };

    /// <summary>Lines that are kept in full wherever they are: the testers' marks, faults, the stat / summary lines of the work orders.</summary>
    private static readonly Regex Protected = new(@"MP-MARK|FAULT|STATS|SUMMARY|summary", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A line that is never collapsed, whatever it is next to.</summary>
    public static bool IsProtected(string line) => Protected.IsMatch(line);

    private static readonly Regex Clock = new(
        @"^(?:\[\d\d:\d\d:\d\d(?:\.\d+)?\]\s*(?:t\d+\s*)?|\d\d:\d\d:\d\d(?:\.\d+)?\s+(?:m=\d+\s+off=\S+\s+)?|\d{4}-\d\d-\d\d \d\d:\d\d:\d\d(?:\.\d+)? [+-]\d\d:\d\d \[[A-Z]{3}\]\s*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The line without its leading clock / thread prefix: what the class patterns and the repeat test look at.</summary>
    public static string Body(string line)
    {
        var m = Clock.Match(line);
        return m.Success ? line.Substring(m.Length) : line;
    }

    /// <summary>The noise class this line belongs to, or null.</summary>
    public static Class? Classify(string body)
    {
        foreach (var c in Classes) if (c.Pattern.IsMatch(body)) return c;
        return null;
    }

    private static readonly Regex Volatile = new(@"\bt=[\d.]+|\bm=\d+ off=\S+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The key of the "same line as the one before" test: the body with the game-time stamp cut, nothing else changed.</summary>
    public static string RepeatKey(string body) => Volatile.Replace(body, "").TrimEnd('\r');
}
