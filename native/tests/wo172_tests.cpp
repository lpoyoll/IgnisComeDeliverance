// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-172: engine-free checks of the 0.48.8 native rules -- C1's dead gate (the soul's IsDead, the mod's mark, a reload's revival).
// Linked into KCDMP_NativeTests; wo172_tests() returns the number of failures.
#include <cstdio>

#include "wo172_rules.h"

using namespace kcdmp::wo172rules;

namespace {
int g_fail = 0, g_pass = 0;
#define DCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo172_tests(int* passed) {
    g_fail = g_pass = 0;
    {
        DeadState s;
        DCHECK(!is_dead(s, 10.0), "a body never read and never marked is alive");
        s.haveRead = true; s.soulDead = false; s.readAt = 10.0;
        DCHECK(!is_dead(s, 10.1), "its soul reads alive: alive");
        DCHECK(read_fresh(s, 10.1) && !read_fresh(s, 10.0 + kReadCacheS + 0.01), "a read is reused for %.2f s, then read again", kReadCacheS);
        s.soulDead = true;
        DCHECK(is_dead(s, 10.2), "its soul reads IsDead: dead");
    }
    // the owner-death apply marks it before the soul flips: dead at once (the field: rows and applies within the same 0.1 s)
    {
        DeadState s;
        s.haveRead = true; s.soulDead = false; s.readAt = 20.0;
        s.markedAt = 20.05;
        DCHECK(is_dead(s, 20.06), "marked by the mod: dead in the same frame, the soul not flipped yet");
        DCHECK(is_dead(s, 20.05 + kMarkTrustS - 0.01), "the mark holds for %.0f s", kMarkTrustS);
        DCHECK(!is_dead(s, 20.05 + kMarkTrustS + 0.01), "after the mark's trust the soul decides (a reload revived it under the same id)");
        s.soulDead = true;
        DCHECK(is_dead(s, 30.0), "once the soul says IsDead the mark is not needed");
    }
    if (passed) *passed = g_pass;
    return g_fail;
}
