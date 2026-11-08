// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;

namespace KcdMp.Client;

// WO-201 (docs/WO-201-findings.md): the bridge's halves. The pure rules are Wo201Rules (Wo201.cs).
//
//   B5 inv_screen_guard: the engine's own lines about this machine's inventory screens (OnLoot, the loot screen's and the trade
//      screen's UI sounds) go to the Lua on both machines, in a session or not; no mod path changes an inventory that screen shows.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w201On = new(StringComparer.Ordinal);
    private bool W201(string name) => _w201On.TryGetValue(name, out bool v) ? v : Wo201Rules.DefaultOn(name);
    private long _w201ScreenLines;

    private void Wo201OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w201_cfg":
                if (!Wo201Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W201 cfg refused: '{arg}'"); return; }
                _w201On[cfg] = on;
                Console.WriteLine($"MP-W201 cfg {cfg}={(on ? "on" : "off")}");
                Wo201OnCfg(cfg, on);
                return;
            case "w201_status":
                _ = Wo201StatusAsync();
                return;
        }
        Wo201OnEventMore(name, arg);
    }

    /// <summary>The engine lines (Wo144OnEngineLine) -- never consumes.</summary>
    private void Wo201OnEngineLine(string line)
    {
        switch (Wo201Rules.ScreenLine(line, out string subject))
        {
            case Wo201Rules.ScreenSignal.In:
                Interlocked.Increment(ref _w201ScreenLines);
                _ = ExecLuaAsync("if KCD2MP_W201InvScreen then KCD2MP_W201InvScreen(\"in\", \"audio\") end");
                break;
            case Wo201Rules.ScreenSignal.Out:
                Interlocked.Increment(ref _w201ScreenLines);
                _ = ExecLuaAsync("if KCD2MP_W201InvScreen then KCD2MP_W201InvScreen(\"out\", \"audio\") end");
                break;
            case Wo201Rules.ScreenSignal.Intent:
                _ = ExecLuaAsync($"if KCD2MP_W201InvIntent then KCD2MP_W201InvIntent(\"{subject}\", \"engine\") end");
                break;
        }
        Wo201OnEngineLineMore(line);
    }

    // ---- the session loop: the DLL's config every 2 s (B1), the parts' ticks ----
    private bool _w201Connected;
    private long _w201TickN;
    private int _w201CfgBusy, _w201StatusBusy;   // KCT fix: one of each in flight at most

    private void Wo201OnConnect(CancellationToken ct)
    {
        _w201Connected = true;
        _ = Wo201LoopAsync(ct);
        _ = Wo204HeartbeatLoopAsync(ct);   // KCT fix: the join's "still loading" never waits behind the game
    }

    private void Wo201OnDisconnect()
    {
        _w201Connected = false;
        IcdCatchUpReset();   // KCT QoL: a catch-up hold never outlives the session
        Wo201ScenesReset();
        Wo201ValuesReset();
        Wo201CombatReset();
        Wo201HandsReset();
        Wo214HandsReset();   // WO-214 F: a partner's last hand goes with him
        Wo201Dude2Reset();
        Wo201NearReset();
        _ = Wo201PushConfigAsync();   // role none: the DLL's joiner rules stand down at once
    }

    private async Task Wo201LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _w201Connected)
        {
            try { await Task.Delay(250, ct); } catch { break; }
            try
            {
                long n = ++_w201TickN;
                // KCT fix: never awaited here -- a loading game does not answer the DLL pipe, and this loop stalled with it (live: the join's
                // heartbeat stopped for 60 s, the host aborted the join while the joiner loaded). One at a time, so a stall never piles them up.
                if (n % 8 == 3 && Interlocked.CompareExchange(ref _w201CfgBusy, 1, 0) == 0)
                    _ = Task.Run(async () => { try { await Wo201PushConfigAsync(); } finally { Volatile.Write(ref _w201CfgBusy, 0); } });
                Wo201ScenesTick(Environment.TickCount64);
                Wo201Tick(n);
                if (n % 240 == 130 && Interlocked.CompareExchange(ref _w201StatusBusy, 1, 0) == 0)
                    _ = Task.Run(async () => { try { await Wo201StatusAsync(); } finally { Volatile.Write(ref _w201StatusBusy, 0); } });
            }
            catch (Exception ex) { Console.WriteLine($"MP-W201 loop: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    /// <summary>The DLL's op 77: [role][joined][bits] -- bits 1 joiner_scene_noposition, 2 walk_from_host, 4 oneshot_tick_cap, 8 copy_no_idle_turn.</summary>
    private async Task Wo201PushConfigAsync()
    {
        try
        {
            byte role = _w201Connected ? Wo200Role() : Wo200Rules.RoleNone;
            byte bits = Wo201Rules.ConfigBits(W201);
            await _combat.Wo163Async(KcdMp.Wire.Protocol.Wo201OpConfig, [role, (byte)(W137Joiner ? 1 : 0), bits]);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W201 config not pushed ({ex.GetType().Name})"); }
    }

    private async Task Wo201StatusAsync()
    {
        string dll = "-";
        try
        {
            var r = await _combat.Wo163Async(KcdMp.Wire.Protocol.Wo201OpStatus, []);
            if (r is { Ok: true } rr) dll = System.Text.Encoding.ASCII.GetString(rr.Payload);
        }
        catch { }
        Console.WriteLine(Wo201StatusText() + " dll=[" + dll + "]");
    }

    private string Wo201StatusText() =>
        FormattableString.Invariant($"MP-W201-STATS screen_lines={Interlocked.Read(ref _w201ScreenLines)} ") +
        string.Join(' ', Wo201Rules.SwitchNames.Select(n => $"{n}={(W201(n) ? "on" : "off")}")) + " " + Wo201StatsMore();
}
