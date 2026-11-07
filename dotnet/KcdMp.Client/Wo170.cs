// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>WO-170 (docs/WO-170-findings.md): the pure rules of 0.48.6's agent half. The bridge is GameBridge.Wo170.cs.</summary>
public static class Wo170Rules
{
    /// <summary>F1 mp_figure_guard, F4 mp_scene_hold: a new mechanism ships on, with its switch (the maintainer's standing rule).</summary>
    public const bool DefaultFigureGuard = true, DefaultSceneHold = true;

    /// <summary>"kcd2mp_2" -> 2: a partner's figure's entity name (the ghost id), or null.</summary>
    public static int? FigureGhost(string soul)
    {
        const string p = "kcd2mp_";
        if (!soul.StartsWith(p, StringComparison.Ordinal) || soul.Length == p.Length || soul.Length > p.Length + 3) return null;
        for (int i = p.Length; i < soul.Length; i++) if (!char.IsAsciiDigit(soul[i])) return null;
        return int.Parse(soul.AsSpan(p.Length), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>One "New dialogue 'name (...)' is starting. Params: souls = 'Ex: a - meta override: X; Nx: b - meta override: Y ...'" line.</summary>
    public readonly record struct Bark(string Dialogue, string Kind, string Speaker, string SpeakerMeta, string? Other, string? OtherMeta);

    /// <summary>
    /// The engine's own "New dialogue" line -> its dialogue's short name, its kind ("CombatShout", "AI::DoMonologue" ...), the speaking soul
    /// (the first "Ex:") with its meta override and the first "Nx:" soul with its meta override; null for any other line.
    /// </summary>
    public static Bark? ParseBark(string line)
    {
        const string head = "New dialogue '";
        if (!line.StartsWith(head, StringComparison.Ordinal)) return null;
        int q = line.IndexOf('\'', head.Length);
        if (q < 0) return null;
        string full = line[head.Length..q];
        int sp = full.IndexOf(' ');
        string name = sp > 0 ? full[..sp] : full;
        int dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];
        string kind = "";
        int kp = full.LastIndexOf('(');
        if (kp >= 0 && full.EndsWith(')')) kind = full[(kp + 1)..^1];
        const string souls = "souls = '";
        int s = line.IndexOf(souls, q, StringComparison.Ordinal);
        if (s < 0) return null;
        string rest = line[(s + souls.Length)..];
        var ex = Participant(rest, "Ex: ");
        if (ex is null) return null;
        var nx = Participant(rest, "Nx: ");
        return new Bark(name, kind, ex.Value.Soul, ex.Value.Meta, nx?.Soul, nx?.Meta);
    }

    private static (string Soul, string Meta)? Participant(string souls, string tag)
    {
        int a = souls.IndexOf(tag, StringComparison.Ordinal);
        if (a < 0) return null;
        int b = a + tag.Length, e = b;
        while (e < souls.Length && (char.IsAsciiLetterOrDigit(souls[e]) || souls[e] == '_')) e++;
        if (e == b) return null;
        string soul = souls[b..e];
        string meta = "";
        const string mo = "meta override: ";
        int m = souls.IndexOf(mo, e, StringComparison.Ordinal);
        int next = souls.IndexOf("; ", e, StringComparison.Ordinal);
        if (m >= 0 && (next < 0 || m < next))
        {
            int me = m + mo.Length, mEnd = me;
            while (mEnd < souls.Length && (char.IsAsciiLetterOrDigit(souls[mEnd]) || souls[mEnd] == '_')) mEnd++;
            meta = souls[me..mEnd];
        }
        return (soul, meta);
    }

    /// <summary>
    /// A combat shout (the engine's barks as a fight starts and goes on) whose opponent is a partner's figure: the NPC that shouts and the
    /// figure's ghost id -- the moment an NPC takes the figure as its foe. Null for any other line.
    /// </summary>
    public static (string Npc, int Ghost, string NpcMeta, string FigureMeta, string Dialogue)? AggroAgainstFigure(string line)
    {
        if (ParseBark(line) is not { } b || b.Kind != "CombatShout" || b.Other is null) return null;
        if (FigureGhost(b.Speaker) is not null) return null;      // the figure shouting is not an NPC's aggression
        if (FigureGhost(b.Other) is not { } g) return null;
        return (b.Speaker, g, b.SpeakerMeta, b.OtherMeta ?? "", b.Dialogue);
    }

    /// <summary>The reason an NPC took the figure on, in the engine's own words: its shout's meta override, else "unknown".</summary>
    public static string AggroReason(string npcMeta, string figureMeta) =>
        npcMeta.Length > 0 ? npcMeta : figureMeta.Length > 0 ? figureMeta : "unknown";

    /// <summary>Interactive scenes the engine runs: EnqueueScene minus EndScene/Interrupt, never below zero (T4's counters).</summary>
    public static long ScenesRunning(long enqueues, long ends) => Math.Max(0, enqueues - ends);
}
