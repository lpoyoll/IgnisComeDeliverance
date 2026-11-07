// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-176 (A1) -- 0.50.0: a partner's figure stays out of a quest's arranged fight, by the fight's own context (wo176_rules.h).
//
//   kept_out(eid, site): the single question every keep-out site asks (hits::scene_owned_eid -- WO-121's skirmish add and raw blow, WO-132's
//     engage, WO-136's retarget and threat, WO-163's pair, WO-165's lock-on; WO-173's real hit asks it before the engine path): the actor's
//     soul carries combat_arrangedFight or combat_forceUnarmedWhileInUnarmedSkirmish (the engine's own context store, read through the WO-143
//     reader -- main thread only; another thread gets the cached answer), else WO-174's name list. Logs, once per actor per 10 s:
//       WO176-ARRANGED skirmish=npc:<actor> quest=<context|by-actor-name> context=<name> npc=<actor> site=<caller> figure_kept_out=yes
//   tick() (the host's world only -- op 23's walk): every actor within 40 m of a figure is read (a level-wide entity walk: every 4 s, twice a
//     second for 30 s after any story actor was seen); while a story actor is near, the figure carries the shield contexts; a figure a
//     fighter is turned on (its opponent is the figure) leaves that skirmish (RemoveSoulFromSkirmish) and the mod's forced targets and threats
//     on that actor are dropped (a relation-only, passive membership is counted, not removed: the engine re-adds it at once):
//       WO176-ARRANGED skirmish=npc:<actor> quest=context ... figure=kcd2mp_<n> figure_kept_out=removed skirmish_leave=done|FAILED
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   22 Status -> text
//   23 Config [on:1][walk:1]? -> [on:1][walk:1]   (255 = unchanged; on = mp_arranged_guard; walk = this machine is the host -- the figure
//      check's level-wide walk runs only there: the agent sends it on every role change)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo176 {

constexpr uint8_t kOpStatus = 22, kOpConfig = 23;

// Any thread (the engine is read only on the main thread). True = keep every partner's figure out of this actor's fight.
bool kept_out(uint32_t eid, const char* site);
// The context alone (no name list, no log): 1 set, 0 not, -1 unknown.
int arranged_context(uint32_t eid);

void tick();   // main thread, every frame (it runs its check at 2 Hz)
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);
bool on();

} // namespace kcdmp::wo176
