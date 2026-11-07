// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-173: engine-free checks of the 0.49.0 native rules -- the reaction tail's sites lifted from the collision handler's bytes, the
// one-blow-once ladder, the capped raw path, the health correction, the down / up reports.
// Linked into KCDMP_NativeTests; wo173_tests() returns the number of failures.
#include <cstdio>
#include <cstring>
#include <vector>

#include "wo173_rules.h"

using namespace kcdmp::wo173rules;

namespace {
int g_fail = 0, g_pass = 0;
#define TCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

// The three blocks as the 1.5.5 collision handler holds them (read this WO), at their offsets from the handler's entry.
const uint8_t kA[90] = {0xE8, 0x5B, 0xDD, 0xF0, 0xFF, 0x84, 0xC0, 0x0F, 0x84, 0xEC, 0x03, 0x00, 0x00, 0x49, 0x8B, 0x8E, 0xC8, 0x00, 0x00, 0x00,
    0xE8, 0x87, 0x0D, 0xF1, 0xFF, 0x44, 0x0F, 0xB6, 0xF8, 0x84, 0xC0, 0x0F, 0x85, 0x45, 0x02, 0x00, 0x00, 0x48, 0x8B, 0x4E, 0x08, 0xE8, 0x72, 0x38,
    0xF1, 0xFF, 0x8B, 0x55, 0x34, 0x44, 0x0F, 0xB6, 0xC0, 0xE8, 0xF6, 0x0B, 0x21, 0x00, 0x4D, 0x8B, 0x86, 0xC8, 0x00, 0x00, 0x00, 0x48, 0x8D, 0x4D,
    0xF0, 0x48, 0x89, 0x4C, 0x24, 0x20, 0x48, 0x8D, 0x54, 0x24, 0x60, 0x48, 0x8B, 0xCE, 0x44, 0x8B, 0xC8, 0xE8, 0x86, 0x11, 0x00, 0x00};
const uint8_t kB[67] = {0x49, 0x8B, 0x86, 0xC8, 0x00, 0x00, 0x00, 0x48, 0x89, 0x5C, 0x24, 0x60, 0x48, 0x8B, 0xB8, 0xE0, 0x02, 0x00, 0x00, 0x48,
    0x85, 0xDB, 0x74, 0x09, 0x48, 0x8B, 0x03, 0x48, 0x8B, 0xCB, 0xFF, 0x50, 0x08, 0x48, 0x8D, 0x54, 0x24, 0x60, 0x48, 0x8B, 0xCF, 0xFF, 0x15, 0x92,
    0xF5, 0x43, 0x00, 0x48, 0x8B, 0x4C, 0x24, 0x60, 0x44, 0x0F, 0xB6, 0xF8, 0x48, 0x85, 0xC9, 0x74, 0x06, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x10};
const uint8_t kC[68] = {0x0F, 0xB7, 0x87, 0x74, 0x04, 0x00, 0x00, 0x66, 0x39, 0x81, 0x74, 0x04, 0x00, 0x00, 0x0F, 0x85, 0xE8, 0x00, 0x00, 0x00,
    0x44, 0x38, 0x6D, 0x30, 0x75, 0x60, 0x44, 0x38, 0x6D, 0x31, 0x75, 0x5A, 0x48, 0x8B, 0xCF, 0xE8, 0x2A, 0xAE, 0xF0, 0xFF, 0x84, 0xC0, 0x74, 0x4E,
    0x48, 0x8B, 0x8F, 0xF0, 0x02, 0x00, 0x00, 0x48, 0x81, 0xC1, 0x40, 0x0F, 0x00, 0x00, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x08, 0xA8, 0x04, 0x74, 0x36};
const uint8_t kD[26] = {0xB2, 0x01, 0xEB, 0x02, 0x33, 0xD2, 0x49, 0x8D, 0x88, 0x00, 0x06, 0x00, 0x00, 0x48, 0x89, 0x9C, 0x24, 0xF0, 0x01, 0x00, 0x00,
    0xE8, 0xE8, 0xCF, 0xF1, 0xFF};
constexpr size_t kOffD = 0x16E;
constexpr uintptr_t kEntry = 0x180163B30;   // an image-based address of the handler (the parser only adds offsets to it)
constexpr size_t kOffA = 0x320, kOffB = 0x537, kOffC = 0x47E;

std::vector<uint8_t> window() {
    std::vector<uint8_t> w(0x1000, 0xCC);
    std::memcpy(w.data() + kOffA, kA, sizeof kA);
    std::memcpy(w.data() + kOffB, kB, sizeof kB);
    std::memcpy(w.data() + kOffC, kC, sizeof kC);
    std::memcpy(w.data() + kOffD, kD, sizeof kD);
    return w;
}
} // namespace

int wo173_tests(int* passed) {
    g_fail = g_pass = 0;
    // ---- the sites from the real bytes ----
    {
        auto w = window();
        ReactionSites s{};
        TCHECK(parse_reaction_sites(w.data(), w.size(), kEntry, &s), "the three blocks are found once each");
        const uintptr_t a = kEntry + kOffA;
        TCHECK(s.alive == a + 5 + static_cast<int32_t>(0xFFF0DD5B), "the can-react test is the first call's target");
        TCHECK(s.ownReaction == a + 25 + static_cast<int32_t>(0xFFF10D87), "the own-action test is the second call's target");
        TCHECK(s.attackerFlag == a + 46 + static_cast<int32_t>(0xFFF13872), "the attacker flag is the third call's target");
        TCHECK(s.kind == a + 58 + 0x00210BF6, "the kind rule is the fourth call's target");
        TCHECK(s.createHitAction == a + 90 + 0x1186, "CreateHitAction is the fifth call's target");
        TCHECK(s.victimOff == 0xC8, "the victim actor sits at details +0xC8 (read: 0x%X)", s.victimOff);
        TCHECK(s.kindInOff == 0x44, "the kind's input sits at result +0x44 (rbp+0x34, the result at rbp-0x10; read 0x%X)", s.kindInOff);
        TCHECK(s.directorOff == 0x2E0, "the director at combat actor +0x2E0 (read 0x%X)", s.directorOff);
        TCHECK(s.teamOff == 0x474, "the group word at +0x474 (read 0x%X)", s.teamOff);
        TCHECK(s.killedOff0 == 0x40 && s.killedOff1 == 0x41, "the killed bytes at result +0x40/+0x41 (read 0x%X/0x%X)", s.killedOff0, s.killedOff1);
        TCHECK(s.modelOff == 0x2F0 && s.flagsPropOff == 0xF40 && s.flagsGetSlot == 8 && s.flagsBit == 4, "the victim model's flags property +0xF40, getter 8, bit 4");
        TCHECK(s.interruptible == kEntry + kOffC + 40 + static_cast<int32_t>(0xFFF0AE2A), "the interrupt predicate is the call in the test");
        TCHECK(s.addrefSlot == 0x08 && s.releaseSlot == 0x10, "addref slot 8, release slot 0x10");
        TCHECK(s.iatSlot == kEntry + kOffB + 47 + 0x43F592, "the import slot is rip-relative after the call");
        TCHECK(s.intendedOff == 0x600, "the intended-target property at model +0x600 (read 0x%X)", s.intendedOff);
        TCHECK(s.intendedSetter == kEntry + kOffD + 26 + static_cast<int32_t>(0xFFF1CFE8), "its setter is the call after it");
    }
    // ---- moved: the same blocks elsewhere give the same offsets, targets moved with them ----
    {
        std::vector<uint8_t> w(0x1000, 0x90);
        std::memcpy(w.data() + 0x100, kC, sizeof kC);
        std::memcpy(w.data() + 0x600, kA, sizeof kA);
        std::memcpy(w.data() + 0x900, kB, sizeof kB);
        std::memcpy(w.data() + 0x200, kD, sizeof kD);
        ReactionSites s{};
        TCHECK(parse_reaction_sites(w.data(), w.size(), kEntry, &s), "moved blocks still parse");
        TCHECK(s.createHitAction == kEntry + 0x600 + 90 + 0x1186, "a moved call resolves from its own position");
    }
    // ---- refused: a block missing, doubled, cut, or disagreeing with itself ----
    {
        auto w = window();
        ReactionSites s{};
        std::memcpy(w.data() + 0xA00, kA, sizeof kA);
        TCHECK(!parse_reaction_sites(w.data(), w.size(), kEntry, &s), "a doubled reaction block is refused");
    }
    {
        auto w = window();
        ReactionSites s{};
        TCHECK(!parse_reaction_sites(w.data(), kOffB + 40, kEntry, &s), "a window that cuts the hand-over is refused");
    }
    {
        auto w = window();
        ReactionSites s{};
        std::memset(w.data() + kOffD, 0xCC, sizeof kD);
        TCHECK(!parse_reaction_sites(w.data(), w.size(), kEntry, &s), "no intended-target mark: refused");
    }
    {
        auto w = window();
        ReactionSites s{};
        w[kOffB + 3] = 0xD0;   // B's victim offset 0xD0 != A's 0xC8
        TCHECK(!parse_reaction_sites(w.data(), w.size(), kEntry, &s), "a hand-over reading another victim field is refused");
    }
    {
        auto w = window();
        ReactionSites s{};
        w[kOffA + 61] = 0xD0;  // A's two victim offsets disagree
        TCHECK(!parse_reaction_sites(w.data(), w.size(), kEntry, &s), "a reaction block whose two victim reads disagree is refused");
    }
    {
        auto w = window();
        ReactionSites s{};
        w[kOffC + 10] = 0x78;  // the group test compares two different fields
        TCHECK(!parse_reaction_sites(w.data(), w.size(), kEntry, &s), "a group test of two different words is refused");
    }
    {
        auto w = window();
        ReactionSites s{};
        w[kOffB + 66] = 0x08;  // release slot == addref slot
        TCHECK(!parse_reaction_sites(w.data(), w.size(), kEntry, &s), "an addref equal to the release is refused");
    }
    {
        auto w = window();
        ReactionSites s{};
        w[kOffA + 48] = 0x00; w[kOffA + 68] = 0x7F;   // the kind input outside the result struct
        TCHECK(!parse_reaction_sites(w.data(), w.size(), kEntry, &s), "a kind input outside the result struct is refused");
    }
    // ---- one blow, once: the ladder ----
    {
        Pre p{};
        TCHECK(precondition(p) == Fallback::None, "everything in place: the engine");
        p.on = false; TCHECK(precondition(p) == Fallback::Off, "switch off first");
        p = Pre{}; p.faulted = true; TCHECK(precondition(p) == Fallback::Faulted, "a fault this session");
        p = Pre{}; p.armed = false; TCHECK(precondition(p) == Fallback::NotArmed, "processor not armed");
        p = Pre{}; p.rowKnown = false; TCHECK(precondition(p) == Fallback::NoRow, "no swing row for the blow");
        p = Pre{}; p.victimFound = false; TCHECK(precondition(p) == Fallback::NoVictim, "no victim");
        p = Pre{}; p.victimDead = true; TCHECK(precondition(p) == Fallback::VictimDead, "a dead victim");
        p = Pre{}; p.striking = false; TCHECK(precondition(p) == Fallback::NoSwingWindow, "the figure not striking");
        p = Pre{}; p.on = false; p.striking = false; TCHECK(precondition(p) == Fallback::Off, "the first failing precondition names it");
        TCHECK(after_engine(1) == Fallback::None && after_engine(2) == Fallback::None && after_engine(3) == Fallback::None && after_engine(4) == Fallback::None,
               "hit / blocked / pb / broken: the engine applied it");
        TCHECK(after_engine(5) == Fallback::Filtered, "filtered: the raw path applies it (never dropped)");
        TCHECK(after_engine(0) == Fallback::CoreNotRun, "the core never ran: the raw path applies it");
        for (int i = 0; i <= 10; ++i) TCHECK(fallback_name(static_cast<Fallback>(i))[0] != '?', "every fallback has a name (%d)", i);
        for (int i = 0; i <= 9; ++i) TCHECK(react_name(static_cast<React>(i))[0] != 0, "every reaction result has a name (%d)", i);
    }
    // ---- the capped raw path ----
    {
        TCHECK(stamina_max_estimate(0, -1) == 100.0f, "no reading: the floor (100)");
        TCHECK(stamina_max_estimate(140, 90) == 140.0f, "the highest seen");
        TCHECK(stamina_max_estimate(90, 160) == 160.0f, "the current one when higher");
        TCHECK(cap_stamina(80, 100) == 35.0f, "80 on a 100 max -> 35");
        TCHECK(cap_stamina(20, 100) == 20.0f, "below the cap: unchanged");
        TCHECK(cap_stamina(-3, 100) == 0.0f && cap_stamina(NAN, 100) == 0.0f, "negative / NaN -> 0");
        TCHECK(cap_stamina(100, 200) == 70.0f, "35 %% of a 200 max");
        // one capped blow on a full-stamina fighter never empties it
        TCHECK(100.0f - cap_stamina(1000.0f, 100.0f) > 0.0f, "one capped blow never takes a full fighter to zero stamina");
    }
    // ---- the health correction ----
    {
        Correction c = health_correction(1, 21.0f, 9.0f, 70.0f);
        TCHECK(c.apply && c.raiseBy > 11.9f && c.raiseBy < 12.1f && c.extraDamage == 0, "the engine took more than the joiner measured: raised back by the difference");
        c = health_correction(1, 4.0f, 9.0f, 70.0f);
        TCHECK(c.apply && c.extraDamage > 4.9f && c.extraDamage < 5.1f, "the engine took less: the rest as damage");
        c = health_correction(1, 9.02f, 9.0f, 70.0f);
        TCHECK(!c.apply, "within 0.05: nothing");
        c = health_correction(2, 0.0f, 9.0f, 70.0f);
        TCHECK(!c.apply, "a blocked blow keeps the engine's damage");
        c = health_correction(3, 0.0f, 9.0f, 70.0f);
        TCHECK(!c.apply, "a perfect block keeps the engine's damage");
        c = health_correction(1, 30.0f, 9.0f, 0.0f);
        TCHECK(!c.apply, "a body the engine killed is never raised");
        c = health_correction(1, -1.0f, 9.0f, 50.0f);
        TCHECK(!c.apply, "unmeasured: nothing");
        c = health_correction(1, 5.0f, -1.0f, 50.0f);
        TCHECK(!c.apply, "the joiner's measure unknown: nothing");
    }
    // ---- down / up ----
    {
        DownTrack t{};
        TCHECK(down_should_send(t, 0, 1.0), "the first reading is sent");
        TCHECK(!down_should_send(t, 0, 1.2), "standing again: nothing");
        TCHECK(down_should_send(t, 1, 1.3), "down: sent at once");
        TCHECK(!down_should_send(t, 1, 1.4), "still down: not again at once");
        TCHECK(down_should_send(t, 1, 1.3 + kDownRepeatS), "still down after %.0f s: repeated (a lost message recovers)", kDownRepeatS);
        TCHECK(down_should_send(t, 0, 5.0), "up: sent at once");
        TCHECK(!down_should_send(t, 0, 5.0 + 2 * kDownRepeatS), "standing is not repeated");
        TCHECK(down_should_send(t, 2, 20.0), "dead: sent at once");
        TCHECK(!down_should_send(t, 2, 20.0 + 2 * kDownRepeatS), "a death is said once, never repeated");
    }
    if (passed) *passed = g_pass;
    return g_fail;
}
