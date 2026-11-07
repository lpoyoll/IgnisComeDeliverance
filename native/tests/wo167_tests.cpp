// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-167: engine-free checks of the 0.48.5 native rules -- C3 (a watched swing's action status: installed or not), C1 (which bodies
// get the walk pulse). Linked into KCDMP_NativeTests; wo167_tests() returns the number of failures.
#include <cstdio>
#include <string_view>

#include "wo167_rules.h"

using namespace kcdmp::wo167rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo167_tests(int* passed) {
    // ---- C3: one frame's look at a watched action (IAction::EStatus at +0x28, docs/WO-42-findings.md 2.4)
    WCHECK(judge(kStatusPending, false, 0.0) == Seen::Pending, "right after the queue call (the field's 309 of 309): still pending");
    WCHECK(judge(kStatusInstalled, false, 0.02) == Seen::Installed, "installed the next frame: the copy's animation started -- shown");
    WCHECK(judge(kStatusExiting, false, 0.9) == Seen::Installed, "exiting: it was installed (a short swing seen late)");
    WCHECK(judge(kStatusFinished, false, 0.2) == Seen::NeverInstalled, "finished without ever being installed: dropped -- not shown");
    WCHECK(judge(kStatusFinished, true, 0.7) == Seen::Installed, "finished after an earlier frame saw it installed: shown");
    WCHECK(judge(kStatusPending, false, kWatchS - 0.01) == Seen::Pending, "still waiting just under the limit");
    WCHECK(judge(kStatusPending, false, kWatchS) == Seen::NeverInstalled, "never installed in 0.6 s: never will be (a T-posed body)");
    WCHECK(judge(kStatusNone, false, 0.1) == Seen::Pending, "status None for a moment: looked at again");
    WCHECK(judge(kStatusNone, false, 1.0) == Seen::NeverInstalled, "None past the limit: not shown");
    WCHECK(judge(77, false, 0.0) == Seen::NeverInstalled, "a value that is no action status is never counted as shown");
    WCHECK(judge(kStatusInstalled, false, 99.0) == Seen::Installed, "installed is installed, however late the frame");
    // the names the agent's line uses
    WCHECK(std::string_view(seen_name(Seen::Installed)) == "installed" && std::string_view(seen_name(Seen::NeverInstalled)) == "no-anim"
           && std::string_view(seen_name(Seen::Off)) == "off" && std::string_view(seen_name(Seen::Pending)) == "pending", "the result names");
    WCHECK(static_cast<int>(Seen::None) == 0 && static_cast<int>(Seen::Pending) == 1 && static_cast<int>(Seen::Installed) == 2
           && static_cast<int>(Seen::NeverInstalled) == 3 && static_cast<int>(Seen::Off) == 4, "the wire values (the agent reads them)");
    WCHECK(kMaxWatches >= 16 && kKeepResultS >= 5.0, "room for a fight's swings, results kept past a verdict's lag");

    // ---- C1: the walk pulse (WO-155's T-pose fix) for an avatar and a fresh spawn, once per bind
    WCHECK(wants_pulse(true, false, false), "an avatar, not yet started: pulsed (WO-155)");
    WCHECK(wants_pulse(false, true, false), "a stand-in (a fresh spawn), not yet started: pulsed (the field's T-posed bandit)");
    WCHECK(!wants_pulse(false, false, false), "an ordinary copy (alive in this world before its bind): never pulsed");
    WCHECK(!wants_pulse(true, false, true) && !wants_pulse(false, true, true), "once started: never again for that bind");
    WCHECK(kFreshPendingS >= 2.0, "a mark that came before the bind waits for it");

    *passed = g_pass;
    return g_fail;
}
