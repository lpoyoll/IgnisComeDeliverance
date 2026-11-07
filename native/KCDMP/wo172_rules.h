// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-172: the pure rules of wo172.cpp (unit-tested in native/tests/wo172_rules_tests.cpp). No engine access here.

namespace kcdmp::wo172rules {

// A soul read is cached this long per body (the callers ask every tick).
constexpr double kReadCacheS = 0.25;
// The mod's own "dead" mark wins over a soul that still reads alive for this long (the owner-death apply flips the soul a
// frame or two later); after that the soul decides (a reload revives bodies under the same entity id).
constexpr double kMarkTrustS = 3.0;

struct DeadState {
    bool haveRead = false;   // a soul read was made
    bool soulDead = false;   // ... and it said IsDead
    double readAt = -1e9;    // when
    double markedAt = -1e9;  // the mod's mark (owner-death apply), -1e9 = never
};

inline bool read_fresh(const DeadState& s, double now) { return s.haveRead && now - s.readAt < kReadCacheS; }

// Dead = the soul says so, or the mod's mark is recent and the soul has not (yet) said otherwise after the mark.
inline bool is_dead(const DeadState& s, double now) {
    if (s.haveRead && s.soulDead) return true;
    if (now - s.markedAt < kMarkTrustS) return true;
    return false;
}

} // namespace kcdmp::wo172rules
