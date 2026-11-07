// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-167 -- the pure rules of 0.48.5's native half (docs/WO-167-findings.md). No engine here: native/tests/wo167_tests.cpp pins them.
//
//   C3 "shown" means rendered: a host swing queued on a copy counts as shown only when its action was installed. The action's status is
//      the Mannequin IAction field read at +0x28 (docs/WO-42-findings.md 2.4: 0 None, 1 Pending, 2 Installed, 3 Exiting, 4 Finished);
//      the field: 309 of 309 swings read Pending right after the queue call, and nothing ever looked again.
//   C1 the WO-155 walk pulse (a fresh bind stands in a T-pose until something moves it) for a fresh spawn as well as an avatar: a
//      stand-in body is spawned and bound at once, exactly WO-155's case.
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo167rules {

enum class Seen : uint8_t {
    None = 0,            // no swing of ours on this body yet
    Pending = 1,         // queued, not resolved yet
    Installed = 2,       // the action was installed (the copy's animation started): shown
    NeverInstalled = 3,  // finished or timed out without ever being installed: not shown (the lunge plays instead)
    Off = 4,             // the watch is off (the switch, or a fault this session): nothing known
};

constexpr uint32_t kStatusNone = 0, kStatusPending = 1, kStatusInstalled = 2, kStatusExiting = 3, kStatusFinished = 4;

// An attack is installed the frame after it is queued when the body animates (Mannequin's queue runs every frame); one that is not
// installed in this long never was.
constexpr double kWatchS = 0.6;
constexpr size_t kMaxWatches = 64;        // a full list resolves its oldest first
constexpr double kKeepResultS = 30.0;     // a resolved result is kept this long for the agent's question

// One frame's look at a watched action. seenInstalled: an earlier frame saw it installed.
inline Seen judge(uint32_t status, bool seenInstalled, double ageS) {
    if (seenInstalled || status == kStatusInstalled || status == kStatusExiting) return Seen::Installed;
    if (status == kStatusFinished) return Seen::NeverInstalled;   // dropped from the queue without playing
    if (status > kStatusFinished) return Seen::NeverInstalled;    // not an action status: never counted as shown
    if (ageS >= kWatchS) return Seen::NeverInstalled;
    return Seen::Pending;
}

inline const char* seen_name(Seen s) {
    switch (s) {
        case Seen::None: return "none";
        case Seen::Pending: return "pending";
        case Seen::Installed: return "installed";
        case Seen::NeverInstalled: return "no-anim";
        case Seen::Off: return "off";
    }
    return "?";
}

// C1: which bodies get the walk pulse -- an avatar, or a fresh spawn (a stand-in); once per bind.
inline bool wants_pulse(bool avatar, bool fresh, bool nudged) { return (avatar || fresh) && !nudged; }

// C1: a fresh-spawn mark that arrived before the body was bound waits this long for it.
constexpr double kFreshPendingS = 10.0;

} // namespace kcdmp::wo167rules
