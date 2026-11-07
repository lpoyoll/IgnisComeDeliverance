// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-174 (0.49.1; docs/WO-174-findings.md): the pure rules of the agent's half. The bridge is GameBridge.Wo174.cs; the Lua half
/// (mp_repair_combat, mp_combat_autoclean) is in kdcmp.lua, the native half (the scripted-brawl guard) in wo174_rules.h.
/// </summary>
public static class Wo174Rules
{
    /// <summary>
    /// The game's own save request line ("InitiateSaveGame() type: AutoSave, ..."), anchored at the line start like the story-beat parse
    /// (StoryBeat.InitiateMarker): a line that only quotes it is not one. A mod line never starts with it (all carry their own tag).
    /// </summary>
    public static bool IsSaveInitiation(ReadOnlySpan<char> line) => line.StartsWith("InitiateSaveGame()", StringComparison.Ordinal);

    /// <summary>The save kinds that write a file a player can load later: a quick save is one too; a level-switch save is not.</summary>
    public static bool IsLoadableSaveKind(ReadOnlySpan<char> line)
    {
        if (!IsSaveInitiation(line)) return false;
        return !line.Contains("type: LevelSwitchSave", StringComparison.Ordinal);
    }

    /// <summary>The ghost id of a w174_endfig event ("&lt;id&gt;"): a relay id, 0-255.</summary>
    public static bool TryParseGhost(string? arg, out byte ghost)
    {
        ghost = 0;
        if (string.IsNullOrWhiteSpace(arg)) return false;
        var t = arg.Trim();
        int sp = t.IndexOf(' ');
        if (sp > 0) t = t[..sp];
        return byte.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out ghost);
    }

    /// <summary>
    /// A name that belongs to a scripted brawl or scene (the neighbours' brawl's actors are rvacka_neighbour_1 / _2; quest fist fights, duels,
    /// arena and tournament fights are the others): the mod keeps a partner's figure out of it. The same words as wo174_rules.h and kdcmp.lua's
    /// W174.SCENE_WORDS.
    /// </summary>
    public static readonly string[] SceneWords = { "rvacka", "fistfight", "brawl", "duel", "arena", "tournament" };

    public static bool SceneOwned(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var w in SceneWords) if (name.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The reason text a friendly-fire or a leaving figure gives the leave-fight log line.</summary>
    public const string FriendlyFireWhy = "friendly-fire knockdown: the punch's fight ends";
    public const string PartnerLeftWhy = "partner left: its figure leaves every skirmish before its body goes";
}
