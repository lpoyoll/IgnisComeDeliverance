// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-167 -- 0.48.5's native half (docs/WO-167-findings.md). The pure rules are wo167_rules.h; the swing watch itself lives beside the
// swing it watches (combat_construct.cpp), the walk pulse beside the gait it writes (motion.cpp).
//
//   C3 mp_swing_watch (joiner, default ON): a host swing queued on a copy counts as shown only when its action was installed -- the
//      agent asks for the body's last result before the hit's verdict; a swing that never played gets the generic lunge instead.
//   C1 a fresh spawn (a stand-in for a host spawn) gets the WO-155 walk pulse an avatar gets (the agent's avatar event kind 3).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   12 SwingSeen [eid:4] -> [seen:1][ageMs:4][waitMs:4]   (seen: wo167rules::Seen -- 0 none, 1 pending, 2 installed, 3 no-anim, 4 off)
//   13 Status -> text
//   14 Config [swingWatch:1] -> [swingWatch:1][faulted:1]   (255 = unchanged)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo167 {

constexpr uint8_t kOpSwingSeen = 12, kOpStatus = 13, kOpConfig = 14;

// Pipe thread -> main thread (run_sync_bounded). Returns a WO-163 reason code (kROk ...).
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

int status_text(char* out, int n);

} // namespace kcdmp::wo167
