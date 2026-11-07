// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-172 -- 0.48.8's native half (docs/WO-172-findings.md). The pure rules are wo172_rules.h.
//
//   C1 a dead copy is never worked again (mp_dead_wins, default ON). The field (2026-10-10, the joiner): the host's death reached the
//      copy in 0.1 s and the copy read IsDead, then the mod kept working the corpse -- WO-141 activity applies every 1.5 s (a held
//      sword asked back into its hand: the engine re-created the body and it stood), WO-143 temporary tools, a WO-136 retarget that
//      put the dead bandit back into the joiner's skirmish ("keeps fighting"). Every one of those asks here first: a body whose soul
//      reads IsDead, or that the mod marked dead (the owner-death apply), is skipped -- counted and said once per body and kind
//      (WO172-DEAD). A strike window (WO-166 C3) open on a body that dies ends without its prior state going back.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   15 Dead [nameLen:1][name] -> text "marked eid=0x.." | "refused=<why>"   (the mod's owner-death apply, the agent relays it)
//   16 Status -> text
//   17 Config [deadGate:1] -> [deadGate:1]   (255 = unchanged)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo172 {

constexpr uint8_t kOpDead = 15, kOpStatus = 16, kOpConfig = 17;

// Pipe thread -> main thread (run_sync_bounded). Returns a WO-163 reason code (kROk ...).
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

// Main thread. True = this body is dead (its soul reads IsDead, or the mod marked it dead within the last few seconds).
bool copy_dead(uint32_t eid);

// Main thread. True = the gate is on and this body is dead: the caller must not touch it. `what` names the caller
// ("activity", "hands", "retarget", "strike"); the first skip per body and kind is logged, every skip is counted.
bool skip_dead(uint32_t eid, const char* what);

int status_text(char* out, int n);

} // namespace kcdmp::wo172
