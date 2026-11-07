// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-170 -- the pure rules of 0.48.6's native half (docs/WO-170-findings.md). No engine here: native/tests/wo170_tests.cpp pins them.
//
//   F1 the figure guard. A partner's figure on the host is a puppet of that partner: it never attacks on its own. Two parts:
//      * its combat automation stays off for the whole life of its body, not only while the partner's combat bit is set (the field,
//        0.48.5: the body was released at 16:18:49 -- "automation back on" -- and its own brain fought the villager it had been put in a
//        skirmish with; the blows landed on the host's world);
//      * the backstop, at the hit chokepoint: a hit whose attacker is a figure is applied only when it belongs to a swing the partner
//        really sent (the relayed attack the figure played), within that swing's own timing, once per swing; every other figure hit is
//        measured and put back (the hook never skips the engine's call: a hit on an avatar needs a cause back).
#include <cstddef>
#include <cstdint>
#include <vector>

namespace kcdmp::wo170rules {

// A relayed swing's blow can land from the moment the swing is queued on the figure until this long after (the swing's own hit time plus the
// engine's queueing; the longest attack row's hit is ~1.2 s in).
constexpr double kSwingHitS = 2.0;
constexpr size_t kMaxTokens = 32;
// The put-back watch of a dropped blow: the engine applies the damage on the next update (hits.cpp: "landed at frame 1"); watched this long.
constexpr double kPutBackS = 0.35;

enum class Verdict : uint8_t {
    Relayed = 0,        // matches a swing the partner sent: applied
    NotRelayed = 1,     // no such swing (never one, one already used, or too long ago): dropped
};

inline const char* verdict_name(Verdict v) { return v == Verdict::Relayed ? "relayed" : "not-relayed"; }

// The figure's relayed swings: one token per swing the DLL queued on that figure; a hit takes the oldest open token (once per swing).
class SwingLedger {
public:
    void note_swing(uint32_t figureEid, double now) {
        prune(now);
        if (tokens_.size() >= kMaxTokens) tokens_.erase(tokens_.begin());
        tokens_.push_back({figureEid, now});
    }
    // A hit by this figure at `now`: Relayed when an unused swing of its is still inside its window (that token is used up).
    Verdict take(uint32_t figureEid, double now) {
        prune(now);
        for (auto it = tokens_.begin(); it != tokens_.end(); ++it)
            if (it->eid == figureEid) { tokens_.erase(it); return Verdict::Relayed; }
        return Verdict::NotRelayed;
    }
    size_t open(uint32_t figureEid, double now) {
        prune(now);
        size_t n = 0;
        for (const auto& t : tokens_) if (t.eid == figureEid) ++n;
        return n;
    }
    void forget(uint32_t figureEid) {
        for (auto it = tokens_.begin(); it != tokens_.end();) it = it->eid == figureEid ? tokens_.erase(it) : it + 1;
    }
    void clear() { tokens_.clear(); }

private:
    struct Token { uint32_t eid; double at; };
    void prune(double now) {
        for (auto it = tokens_.begin(); it != tokens_.end();) it = (now - it->at > kSwingHitS) ? tokens_.erase(it) : it + 1;
    }
    std::vector<Token> tokens_;
};

// The put-back of a dropped blow: only a loss is put back (never a gain), and never from a body that this blow killed (a body at or below
// 0.01 health is left as the engine has it -- a dead body is not raised, and the line says so).
inline bool should_put_back(float healthNow, float healthBefore) { return healthNow > 0.01f && healthNow < healthBefore - 0.01f; }

// The figure's automation: kept off for the life of the body when the guard is on, whatever the partner's combat bit says; with the guard
// off, the 0.48.5 behaviour (only while the partner's combat bit is set).
inline bool want_automation_off(bool guardOn, bool combatBit) { return guardOn || combatBit; }

// A released figure body keeps its automation off (the guard on): only a switched-off combat feature, or a closed pipe, gives it back.
enum class Release : uint8_t { Unbind = 0, ToggleOff = 1, PipeClosed = 2 };
inline bool give_automation_back(bool guardOn, Release why) { return !guardOn || why != Release::Unbind; }

} // namespace kcdmp::wo170rules
