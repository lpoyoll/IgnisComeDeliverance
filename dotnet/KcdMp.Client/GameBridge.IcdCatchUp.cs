// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT QoL (IgnisComeDeliverance): the host pauses while the joiner catches up (IcdCatchUpRules has the rules). Both halves run in the
// heartbeat loop (GameBridge.Wo204.cs), which waits on nothing but the clock.
using KcdMp.Wire;

namespace KcdMp.Client;

public partial class GameBridge
{
    private long _cuLastSentMs;
    private volatile bool _cuHolding;
    private long _cuHoldSinceMs, _cuLastReportMs, _cuLastLagMs = -1, _cuCooldownUntilMs, _cuHolds, _cuReportsIn;
    private bool _cuEngineHeld;
    private int _cuBusy;   // one hold or release in flight at a time

    /// <summary>The joiner: every 2 s in the host's world, how far behind this game takes it (the agent's queue).</summary>
    private void IcdCatchUpReportTick()
    {
        if (W137Host || !W137JoinerSession || !_joinedWorld || _joinReceivedId != 0 || _where != GameWhere.World) return;
        long now = Environment.TickCount64;
        if (now - _cuLastSentMs < IcdCatchUpRules.ReportEveryMs) return;
        _cuLastSentMs = now;
        _ = Wo203SendSideAsync(Protocol.JoinTargetHost, Protocol.W203Behind, IcdCatchUpRules.Text(Wo202QueueLagMs()));
    }

    /// <summary>The host: a joiner's report.</summary>
    private void Wo203CatchUpOnFrame(byte src, LootMsg m)
    {
        if (!W137Host || !IcdCatchUpRules.TryParse(m.Text, out long lag)) return;
        Interlocked.Increment(ref _cuReportsIn);
        _cuLastReportMs = Environment.TickCount64;
        _cuLastLagMs = lag;
        IcdCatchUpDecide(lag);
    }

    /// <summary>The host's own 1 Hz look (the heartbeat loop): a hold ends on its own limits even when no report comes.</summary>
    private void IcdCatchUpHostTick()
    {
        if (_cuHolding) IcdCatchUpDecide(-1);
    }

    private void IcdCatchUpDecide(long lag)
    {
        long now = Environment.TickCount64;
        bool busy = Wo123HostJoinActive || _w211Holding;
        var (act, why) = IcdCatchUpRules.Decide(_cuHolding, lag, config.CatchUpPauseS, now, _cuHoldSinceMs, _cuLastReportMs, _cuCooldownUntilMs, busy);
        if (act == IcdCatchUpRules.Act.None || Interlocked.CompareExchange(ref _cuBusy, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                if (act == IcdCatchUpRules.Act.Hold && !_cuHolding)
                {
                    _cuHolding = true;
                    _cuHoldSinceMs = now;
                    Interlocked.Increment(ref _cuHolds);
                    bool? held = null;
                    try { held = await _combat.Wo151JoinHoldAsync(true, (ushort)(IcdCatchUpRules.MaxHoldS + 15)); } catch { }
                    _cuEngineHeld = held == true;
                    Console.WriteLine($"MP-CATCHUP host: world held ({why}; threshold {config.CatchUpPauseS} s; engine hold {(_cuEngineHeld ? "on" : "NOT taken")}) [n={_cuHolds}]");
                    _ = ExecLuaAsync($"if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"{EscapeLua($"Paused: your partner is {_cuLastLagMs / 1000.0:F0} s behind. The game continues when they catch up.")}\") end");
                }
                else if (act == IcdCatchUpRules.Act.Release && _cuHolding)
                {
                    _cuHolding = false;
                    if (_cuEngineHeld) { try { await _combat.Wo151JoinHoldAsync(false, 0); } catch { } _cuEngineHeld = false; }
                    bool max = why.StartsWith("held", StringComparison.Ordinal);
                    _cuCooldownUntilMs = Environment.TickCount64 + (max ? IcdCatchUpRules.CooldownAfterMaxMs : IcdCatchUpRules.CooldownMs);
                    Console.WriteLine($"MP-CATCHUP host: world runs again after {(Environment.TickCount64 - _cuHoldSinceMs) / 1000.0:F1} s ({why})");
                    _ = ExecLuaAsync($"if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"{EscapeLua(max ? "Your partner is still behind; the game continues." : "Your partner has caught up.")}\") end");
                }
            }
            finally { Volatile.Write(ref _cuBusy, 0); }
        });
    }

    /// <summary>The session ends: a hold never outlives it.</summary>
    private void IcdCatchUpReset()
    {
        if (_cuHolding && _cuEngineHeld) { try { _ = _combat.Wo151JoinHoldAsync(false, 0); } catch { } }
        _cuHolding = false; _cuEngineHeld = false; _cuLastLagMs = -1;
    }
}
