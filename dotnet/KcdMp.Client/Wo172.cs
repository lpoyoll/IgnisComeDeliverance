// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>WO-172 (docs/WO-172-findings.md): the pure rules of 0.48.8's agent half. The bridge is GameBridge.Wo172.cs.</summary>
public static class Wo172Rules
{
    /// <summary>C1 mp_dead_wins, R1 mp_ride_repair: new mechanisms ship on, with their switch (the maintainer's standing rule).</summary>
    public const bool DefaultDeadWins = true, DefaultRideRepair = true;

    /// <summary>R1: the mod's own mount answer (player.human:IsMounted, sent on a change and every 2 s while mounted) is trusted this long.</summary>
    public const long MountFreshMs = 3_000;

    /// <summary>
    /// R1: the riding bit this player sends. The native read is the player's "horse" animation tag; the engine clears that tag while
    /// the player stays in the saddle (the field: a refused quick save, ForceIdleState "clears 'horse' tags" -- nine minutes of
    /// riding=False at saddle height and horse speed: "running fast in the air" on the partner's screen). The game's own
    /// IsMounted, fresh, keeps it set.
    /// </summary>
    public static bool RidingFlag(bool nativeTag, bool repairOn, bool luaMounted, long luaAtMs, long nowMs) =>
        nativeTag || (repairOn && luaMounted && luaAtMs > 0 && nowMs - luaAtMs <= MountFreshMs);

    /// <summary>True when the riding bit is set only by the mod's mount answer (the native tag lost it): a repair to count and say.</summary>
    public static bool RepairedOnly(bool nativeTag, bool sent) => sent && !nativeTag;

    /// <summary>H1: the engine's own UI sound of the inventory/trade screen -- 1 opened, 0 closed, null any other line.</summary>
    public static int? ApseScreen(string line)
    {
        if (!line.StartsWith("PlayAudio: Apse", StringComparison.Ordinal)) return null;
        if (line.StartsWith("PlayAudio: ApseOpen", StringComparison.Ordinal)) return 1;
        if (line.StartsWith("PlayAudio: ApseClose", StringComparison.Ordinal)) return 0;
        return null;
    }

    /// <summary>"w172_mount 1|0" -> mounted; null for anything else.</summary>
    public static bool? ParseMount(string? arg) => arg?.Trim() switch { "1" => true, "0" => false, _ => null };
}
