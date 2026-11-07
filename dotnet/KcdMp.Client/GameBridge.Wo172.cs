// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;

namespace KcdMp.Client;

// WO-172 (0.48.8) -- the agent's half (docs/WO-172-findings.md; the pure rules are Wo172.cs).
//   C1  mp_dead_wins: the mod's owner-death apply ("w172_dead <npc>") is told to the DLL (op 15: the body is skipped by every
//       activity, tool, retarget and strike from now on) and no host row plays on that copy any more (dispatch=dropped-dead);
//       "w172_alive <npc>" (a reload revived it) lifts it.
//   R1  mp_ride_repair: the riding bit this player sends is the native "horse" tag OR the game's own IsMounted ("w172_mount 1|0").
//   H1  the inventory/trade screen's own UI sounds (ApseOpen / ApseClose) are told to the Lua (KCD2MP_W172TradeScreen): a joiner's
//       trade holds its trader until the screen closes.
//   WO172-DEAD ... / WO172-RIDE ... / MP-WO172-STATS ...
public partial class GameBridge
{
    private volatile bool _w172DeadWins = Wo172Rules.DefaultDeadWins, _w172RideRepair = Wo172Rules.DefaultRideRepair;
    private readonly ConcurrentDictionary<string, long> _w172Dead = new(StringComparer.Ordinal);   // copies the mod applied a death to
    private volatile bool _w172Mounted;
    private long _w172MountedAtMs, _w172DeadMarks, _w172RowsDropped, _w172RideRepaired, _w172RideEpisodes, _w172Apse;
    private bool _w172RepairOpen;

    private void Wo172OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w172_dead":   // <npc>: C1 -- the mod applied the host's death to this copy
                if (f.Length >= 1 && Wo137Text.IsNpc(f[0]))
                {
                    _w172Dead[f[0]] = Environment.TickCount64;
                    if (_w172Dead.Count > 1024) foreach (var k in _w172Dead.Where(kv => Environment.TickCount64 - kv.Value > 1_800_000).Select(kv => kv.Key).ToList()) _w172Dead.TryRemove(k, out _);
                    Interlocked.Increment(ref _w172DeadMarks);
                    if (_w172DeadWins) _ = Wo172MarkDeadAsync(f[0]);
                }
                return;
            case "w172_alive":   // <npc>: C1 -- it reads alive again (a reload): rows may play on it again
                if (f.Length >= 1 && _w172Dead.TryRemove(f[0], out _)) Console.WriteLine($"WO172-DEAD npc={f[0]} alive again (a reload) -- its rows play again");
                return;
            case "w172_mount":   // 1|0: R1 -- the game's own IsMounted for this player
                if (Wo172Rules.ParseMount(arg) is bool m) { _w172Mounted = m; Interlocked.Exchange(ref _w172MountedAtMs, Environment.TickCount64); }
                return;
            case "w172_cfg":     // dead_wins=on|off ride_repair=on|off
                foreach (var kv in f)
                {
                    if (kv is "dead_wins=on" or "dead_wins=off")
                    {
                        bool v = kv.EndsWith("=on", StringComparison.Ordinal);
                        if (v != _w172DeadWins) Console.WriteLine($"WO172-CFG mp_dead_wins {(v ? "on" : "off")}");
                        _w172DeadWins = v;
                        _ = Wo172PushConfigAsync();
                    }
                    else if (kv is "ride_repair=on" or "ride_repair=off")
                    {
                        bool v = kv.EndsWith("=on", StringComparison.Ordinal);
                        if (v != _w172RideRepair) Console.WriteLine($"WO172-CFG mp_ride_repair {(v ? "on" : "off")}");
                        _w172RideRepair = v;
                    }
                }
                return;
        }
    }

    private async Task Wo172MarkDeadAsync(string npc)
    {
        try
        {
            // the WO-132 engagement ends first (the copy no longer counts as fighting this player), then the DLL is told
            bool engaged = false;
            if (_w132Engaged.TryRemove(npc, out var eg)) { engaged = true; await _combat.Wo132EngageAsync(false, eg.Eid, default); }
            if (engaged) Console.WriteLine($"WO172-DEAD npc={npc} engagement released");
            string? r = await _combat.Wo172DeadAsync(npc);
            Console.WriteLine($"WO172-DEAD npc={npc} marked -- the host's death wins here: no activity, tool, retarget, strike or row on it ({r ?? "no answer"})");
        }
        catch (Exception ex) { Console.WriteLine($"WO172-DEAD npc={npc} the DLL was not told ({ex.GetType().Name}) -- its own IsDead read still skips the body"); }
    }

    private async Task Wo172PushConfigAsync()
    {
        try { await _combat.Wo172ConfigAsync((byte)(_w172DeadWins ? 1 : 0)); }
        catch (Exception ex) { Console.WriteLine($"WO172-CFG the DLL was not told ({ex.GetType().Name})"); }
    }

    /// <summary>C1: a host row for a copy the mod applied a death to is not played (the field: rows and lunges on a corpse).</summary>
    private bool Wo172RowOnDead(string npc)
    {
        if (!_w172DeadWins || !_w172Dead.ContainsKey(npc)) return false;
        long n = Interlocked.Increment(ref _w172RowsDropped);
        if (n <= 20 || n % 50 == 0) Console.WriteLine($"MP-ACTION section=inbound kind=NpcAttack npc={npc} dispatch=dropped-dead -- the host's death won: no row plays on a corpse [n={n}]");
        return true;
    }

    /// <summary>R1: the riding bit this player sends (the native tag, repaired by the game's own IsMounted).</summary>
    private bool Wo172Riding(bool nativeTag)
    {
        bool sent = Wo172Rules.RidingFlag(nativeTag, _w172RideRepair, _w172Mounted, Interlocked.Read(ref _w172MountedAtMs), Environment.TickCount64);
        bool repaired = Wo172Rules.RepairedOnly(nativeTag, sent);
        if (repaired) Interlocked.Increment(ref _w172RideRepaired);
        if (repaired && !_w172RepairOpen)
        {
            _w172RepairOpen = true;
            long n = Interlocked.Increment(ref _w172RideEpisodes);
            Console.WriteLine($"WO172-RIDE flag repaired: the horse tag is gone but the game says mounted -- riding stays set [episodes={n}]");
        }
        else if (!repaired && _w172RepairOpen) { _w172RepairOpen = false; Console.WriteLine($"WO172-RIDE flag repair over (native={(nativeTag ? 1 : 0)} sent={(sent ? 1 : 0)})"); }
        return sent;
    }

    /// <summary>H1: the trade/inventory screen's own UI sounds, to the Lua (never consumes the line).</summary>
    private void Wo172OnEngineLine(string line)
    {
        if (Wo172Rules.ApseScreen(line) is not int open) return;
        Interlocked.Increment(ref _w172Apse);
        _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W172TradeScreen then KCD2MP_W172TradeScreen({open}) end"));
    }

    private string Wo172StatsText() =>
        FormattableString.Invariant(
            $"dead_wins={(_w172DeadWins ? "on" : "off")} dead_marks={Interlocked.Read(ref _w172DeadMarks)} rows_dropped_dead={Interlocked.Read(ref _w172RowsDropped)} ride_repair={(_w172RideRepair ? "on" : "off")} ride_repaired_samples={Interlocked.Read(ref _w172RideRepaired)} ride_repair_episodes={Interlocked.Read(ref _w172RideEpisodes)} apse_lines={Interlocked.Read(ref _w172Apse)}");
}
