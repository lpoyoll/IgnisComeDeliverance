// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-170: engine-free checks of the 0.48.6 native rules -- F1's figure guard (the relayed-swing ledger, the put-back, the automation kept
// off). Linked into KCDMP_NativeTests; wo170_tests() returns the number of failures.
#include <cstdio>
#include <string_view>

#include "wo170_rules.h"

using namespace kcdmp::wo170rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo170_tests(int* passed) {
    g_fail = g_pass = 0;
    constexpr uint32_t kA = 0x1BFF98, kB = 0x1BFF99;

    // ---- the ledger: a figure's hit is applied only against a swing the partner really sent
    {
        SwingLedger L;
        WCHECK(L.take(kA, 10.0) == Verdict::NotRelayed, "no swing was sent: the figure's own blow is dropped (the field's tzel_adam)");
        L.note_swing(kA, 10.0);
        WCHECK(L.open(kA, 10.1) == 1, "one swing queued, one open token");
        WCHECK(L.take(kA, 10.4) == Verdict::Relayed, "its blow, 0.4 s later: applied");
        WCHECK(L.take(kA, 10.5) == Verdict::NotRelayed, "a second blow for the same swing: dropped (no blow applied twice)");
        L.note_swing(kA, 20.0);
        WCHECK(L.take(kA, 20.0 + kSwingHitS + 0.01) == Verdict::NotRelayed, "a blow long after its swing: not that swing's");
        L.note_swing(kA, 30.0); L.note_swing(kA, 30.1); L.note_swing(kA, 30.2);
        int ok = 0;
        for (int i = 0; i < 5; ++i) if (L.take(kA, 30.5 + i * 0.1) == Verdict::Relayed) ++ok;
        WCHECK(ok == 3, "five blows against three swings: exactly three applied (%d)", ok);
    }
    // five attacks of the synthetic joiner -> five hits applied once each (WO-170 F1's check)
    {
        SwingLedger L;
        int applied = 0, dropped = 0;
        for (int i = 0; i < 5; ++i) {
            const double t = 100.0 + i * 3.0;
            L.note_swing(kA, t);
            if (L.take(kA, t + 0.6) == Verdict::Relayed) ++applied; else ++dropped;
            if (L.take(kA, t + 0.9) == Verdict::Relayed) ++applied; else ++dropped;   // a stray second blow of the same swing
        }
        WCHECK(applied == 5 && dropped == 5, "5 swings: 5 blows applied once each, the 5 strays dropped (%d/%d)", applied, dropped);
    }
    // one figure's swing never licenses another figure's blow; a forgotten figure has no tokens
    {
        SwingLedger L;
        L.note_swing(kA, 1.0);
        WCHECK(L.take(kB, 1.2) == Verdict::NotRelayed, "figure B has no swing of its own");
        WCHECK(L.take(kA, 1.3) == Verdict::Relayed, "figure A's token is still there");
        L.note_swing(kA, 2.0);
        L.forget(kA);
        WCHECK(L.open(kA, 2.1) == 0 && L.take(kA, 2.1) == Verdict::NotRelayed, "a released body's tokens are gone");
    }
    // the table is bounded: the oldest goes first
    {
        SwingLedger L;
        for (size_t i = 0; i < kMaxTokens + 8; ++i) L.note_swing(kA, 5.0 + i * 0.001);
        WCHECK(L.open(kA, 5.1) == kMaxTokens, "a full ledger keeps %zu tokens", kMaxTokens);
    }
    WCHECK(std::string_view(verdict_name(Verdict::Relayed)) == "relayed" && std::string_view(verdict_name(Verdict::NotRelayed)) == "not-relayed",
           "the names the log uses");

    // ---- the put-back: a loss only, never a body this blow killed
    WCHECK(should_put_back(90.0f, 100.0f), "a loss of 10: put back");
    WCHECK(!should_put_back(100.0f, 100.0f), "no change: nothing to put back");
    WCHECK(!should_put_back(110.0f, 100.0f), "a gain is never taken");
    WCHECK(!should_put_back(0.0f, 100.0f), "dead from the blow: a dead body is not raised (the line says so)");
    WCHECK(!should_put_back(99.995f, 100.0f), "a rounding wobble below 0.01 is not a blow");
    WCHECK(kPutBackS > 0.1 && kPutBackS < 0.6, "watched for a few frames, shorter than the 0.6 s the other watches run");

    // ---- the automation
    WCHECK(want_automation_off(true, false), "guard on: off for the whole life of the body, partner's combat bit or not");
    WCHECK(want_automation_off(true, true), "guard on, combat bit set: off");
    WCHECK(want_automation_off(false, true) && !want_automation_off(false, false), "guard off: 0.48.5 -- only while the combat bit is set");
    WCHECK(!give_automation_back(true, Release::Unbind), "an unbound figure keeps its automation off (the field: 'released -- automation back on', 16:18:49)");
    WCHECK(give_automation_back(true, Release::ToggleOff) && give_automation_back(true, Release::PipeClosed), "a closed feature or session gives it back");
    WCHECK(give_automation_back(false, Release::Unbind), "guard off: given back as in 0.48.5");

    *passed = g_pass;
    return g_fail;
}
