// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>KCT fix (icd14): how long an Edge statement may wait for the game's console before it is pointless.</summary>
public enum EdgeClass
{
    /// <summary>A UI word, a toast, a combat cue: worthless after a few seconds (a 2-minute-old "hit!" is noise).</summary>
    Feedback,
    /// <summary>Everything not named either way: today's 120 s.</summary>
    Normal,
    /// <summary>It changes the world (a death, a quest, loot, inventory, a join or a load): it never expires.</summary>
    World,
}

/// <summary>
/// KCT fix (icd14): the Lua command queue's Edge statements expire by what they are. The class comes from the Lua function the statement
/// calls and from the inbound frame kind whose handler queued it (<see cref="BatchQueue.Entry.Kind207"/>); a world-changing name or kind
/// always wins over a feedback one. Pure (LuaEdgeRulesTests).
/// </summary>
public static class LuaEdgeRules
{
    public const long FeedbackTtlMs = 5_000;
    public const long NormalTtlMs = 120_000;

    // Order matters only between the lists: World is checked first.
    private static readonly string[] WorldWords =
    [
        "Death", "Dead", "Kill", "Knockout", "Takedown", "KoDone", "Executed", "Corpse", "Ragdoll", "Collapse", "Down(",
        "Quest", "Loot", "Item", "Chest", "Inv", "Equip", "Hands", "Carry", "Mount", "Horse",
        "Join", "Load", "Save", "Saved", "World", "TimeSkip", "SkipTime", "Remove", "Reconcile", "Sweep", "Spawn",
        "Crime", "Amnesty", "Judge", "Cleared", "Release", "Separate", "Session", "Cfg", "Set",
    ];

    private static readonly string[] FeedbackWords =
    [
        "Toast", "Msg", "Say", "Notice", "Hint", "Countdown", "VoteIcon", "Hover", "Ping", "DiceTurn", "SwingCue", "SwingHold",
        "PlayEmote", "Bump", "Bark", "NoisePulses", "NoiseAtBody", "VoiceState", "LogSummary", "TraceDone", "CamEmit", "Interaction",
    ];

    private static readonly Regex FirstFn = new(@"KCD2MP_\w+", RegexOptions.CultureInvariant);

    /// <summary>The first mod function a statement calls ("if KCD2MP_X then KCD2MP_X(..) end" -> KCD2MP_X), or null.</summary>
    public static string? FunctionOf(string lua)
    {
        var m = FirstFn.Match(lua);
        return m.Success ? m.Value : null;
    }

    /// <summary>The class of an Edge statement calling <paramref name="fn"/>, queued by the handler of inbound kind <paramref name="kind207"/> (0 = none).</summary>
    public static EdgeClass Classify(string? fn, int kind207)
    {
        if (fn is not null && fn != "KCD2MP_Once")
        {
            string n = fn.StartsWith("KCD2MP_", StringComparison.Ordinal) ? fn[7..] : fn;
            // "Set..." is world only as a state setter's verb at the start (SetGhostDead, SetCutscene), never "Reset" inside a name
            foreach (var w in WorldWords)
                if (w == "Set" ? n.StartsWith("Set", StringComparison.Ordinal) : w == "Down(" ? n.EndsWith("Down", StringComparison.Ordinal) : n.Contains(w, StringComparison.Ordinal))
                    return EdgeClass.World;
        }
        if (WorldKind(kind207)) return EdgeClass.World;
        if (fn is not null)
        {
            string n = fn.StartsWith("KCD2MP_", StringComparison.Ordinal) ? fn[7..] : fn;
            foreach (var w in FeedbackWords) if (n.Contains(w, StringComparison.Ordinal)) return EdgeClass.Feedback;
        }
        if (FeedbackKind(kind207)) return EdgeClass.Feedback;
        return EdgeClass.Normal;
    }

    /// <summary>The time to live of an Edge statement of class <paramref name="c"/> (<see cref="long.MaxValue"/> = never expires).</summary>
    public static long TtlMs(EdgeClass c, long normalTtlMs = NormalTtlMs) => c switch
    {
        EdgeClass.Feedback => FeedbackTtlMs,
        EdgeClass.World => long.MaxValue,
        _ => normalTtlMs,
    };

    /// <summary>An inbound kind whose handler's statements change the world: deaths, items, saves, the join and its side channels.</summary>
    public static bool WorldKind(int k) => k is Protocol.DeathDown or Protocol.PlayerDeathDown or Protocol.ItemDropDown or Protocol.ItemClaimDown
        or Protocol.WorldSavedDown or Protocol.JoinRequestDown or Protocol.WorldOfferDown or Protocol.WorldChunkDown or Protocol.WorldAckDown
        or Protocol.WorldDoneDown or Protocol.JoinAbortDown or Protocol.JoinerReadyDown or Protocol.JoinStatusDown or Protocol.LootAskDown
        or Protocol.LootHostDown or Protocol.QuestHostDown or Protocol.QuestAskDown or Protocol.CrimeAskDown or Protocol.CrimeHostDown
        or Protocol.SleepVoteDown or Protocol.CarryDown or Protocol.PlayerHitDown or Protocol.PlayerHitV8Down;

    /// <summary>An inbound kind whose handler's statements are cosmetic feedback: a combat cue or action (B.2 already drops stale ones).</summary>
    public static bool FeedbackKind(int k) => k is Protocol.CombatEventDown or Protocol.ActionDown;
}
