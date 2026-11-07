// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Threading;

namespace KcdMp.Client;

// WO-174 (0.49.1) -- the agent's half (docs/WO-174-findings.md; the pure rules are Wo174.cs, the Lua half is kdcmp.lua's WO-174 block).
//   * the game's own save request (a loadable kind) tells the Lua to clear partner-figure fight records (mp_combat_autoclean): the mod's
//     own world save asks the same from KCD2MP_HostWorldSave itself;
//   * a friendly-fire knockdown (w174_endfig): the figure leaves the skirmish the punch started -- the engine's own RemoveSoulFromSkirmish
//     through WO-132's op, so the punch cannot outlive itself;
//   * a partner leaving: its figure leaves every skirmish before its body is removed (the host): never a vanished party in a fight.
//   MP-W174 ... lines; MP-W174-STATS counts.
public partial class GameBridge
{
    private long _w174Saves, _w174FfEnds, _w174Leaves, _w174LeaveFail, _w174SceneKept;

    private void Wo174OnSaveInitiated(string line)
    {
        Interlocked.Increment(ref _w174Saves);
        _ = ExecLuaAsync("if KCD2MP_W174Clean then KCD2MP_W174Clean(\"save\") end");
    }

    private void Wo174OnEvent(string? arg)
    {
        if (!Wo174Rules.TryParseGhost(arg, out byte ghost)) { Console.WriteLine($"MP-W174 malformed w174_endfig '{arg}'"); return; }
        if (!_isDamageAuthority) return;   // the host's world holds the fights
        Interlocked.Increment(ref _w174FfEnds);
        _ = Wo132AvatarLeaveFightAsync(ghost, Wo174Rules.FriendlyFireWhy);
    }

    private async Task Wo174LeaveBeforeRemoveAsync(byte ghost)
    {
        Interlocked.Increment(ref _w174Leaves);
        if (!_ghostEntityIds.ContainsKey(ghost.ToString(System.Globalization.CultureInfo.InvariantCulture))) { Interlocked.Increment(ref _w174LeaveFail); return; }
        await Wo132AvatarLeaveFightAsync(ghost, Wo174Rules.PartnerLeftWhy);
    }

    internal string Wo174StatsText() =>
        $"saves_seen={Interlocked.Read(ref _w174Saves)} friendly_fire_ends={Interlocked.Read(ref _w174FfEnds)} leave_fights={Interlocked.Read(ref _w174Leaves)} no_body={Interlocked.Read(ref _w174LeaveFail)} scene_kept={Interlocked.Read(ref _w174SceneKept)}";
}
