// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Steam;
using KcdMp.Wire;

namespace KcdMp.Client;

// KCT fix (icd14): the Lua command queue, the log tail and the link's lanes. Every mechanism ships on with its own switch; off is the
// 0.63/icd13 behaviour.
//   W213 switches (mp_<name> on|off in the mod -> w213_cfg; the bench twin KCDMP_TEST_W213_OFF=name), all default on:
//     the Lua queue:  lua_edge_ttl  lua_evict  lua_replay  lua_recovery_drain
//     the log tail:   evt_resume  evt_skip_spam  evt_early_fast  evt_seq_check  evt_handler_task
//     the link:       link_event_coalesce  link_event_age  steam_inbox_bound  state_order
// Every 10 s (in a session): MP-HEALTH area=lua / area=evt / area=link (Icd14Health.cs).
public partial class GameBridge
{
    private readonly StateOrderGuard _icd14Order = new();

    /// <summary>The switches onto the live objects (at the run's start, on every w213_cfg, and with every health line).</summary>
    private void Icd14ApplySwitches()
    {
        if (_httpForMenu is { } h)
        {
            h.EdgeTtlByClass = W213On("lua_edge_ttl");
            h.EvictExpendableFirst = W213On("lua_evict");
            h.ReplayLevelBehind = W213On("lua_replay");
            h.RecoveryDrain = W213On("lua_recovery_drain");
        }
        if (_transport is LogTailGameTransport t)
        {
            t.ResumeByIdentity = W213On("evt_resume");
            t.SkipSpam = W213On("evt_skip_spam");
            t.EarlyEventFast = W213On("evt_early_fast");
            t.EventSeqCheck = W213On("evt_seq_check");
            t.EventHandlerTask = W213On("evt_handler_task");
        }
        if (_w213Writer is { } w)
        {
            w.Scheduler.Options.CoalesceEvents = W213On("link_event_coalesce");
            w.Scheduler.Options.EventAgeLimits = W213On("link_event_age");
        }
        SteamInbox.DefaultStateCap = W213On("steam_inbox_bound") ? 1024 : 0;
        if (_w213Link?.Steam is { } sc) sc.InboxStateCap = SteamInbox.DefaultStateCap;
    }

    /// <summary>At the reader: an older state of a body than one already read (the other lane overtook it) goes no further.</summary>
    private bool Icd14DropOlderState(int type, byte[] payload) =>
        (type == Protocol.Ghost || type == Protocol.NpcStateDown) && W213On("state_order") && _icd14Order.Drop(type, payload, Environment.TickCount64);

    /// <summary>The three MP-HEALTH lines (every 10 s from the session tick).</summary>
    private void Icd14HealthTick()
    {
        Icd14ApplySwitches();
        if (_httpForMenu is { } h) Console.WriteLine(h.HealthLine());
        if (_transport is LogTailGameTransport t) Console.WriteLine(t.HealthLine());
        var sc = _w213Link?.Steam;
        string sched = _w213Writer is { } w ? w.Scheduler.HealthText() : "state_coalesced=- events_expired=- queue_depth=0 queue_depth_max=0 older_state_reordered=0";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"MP-HEALTH area=link {sched} inbox_dropped={sc?.InboxStateDropped ?? 0} inbox_depth_max={sc?.InboxMaxDepth ?? 0} older_state_dropped={_icd14Order.OlderDropped}"));
    }
}
