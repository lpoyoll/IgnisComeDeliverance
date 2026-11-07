// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-176: engine-free checks of the arranged-fight guard (wo176_rules.h), the real-hit fallback that routes a blow nowhere (wo173_rules.h)
// and B2's blocking referral (wo165_rules.h refer_blocking). Linked into KCDMP_NativeTests; wo176_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo165_rules.h"
#include "wo173_rules.h"
#include "wo174_rules.h"
#include "wo176_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo176_tests(int* passed) {
    using wo176rules::Why;
    // ---- A1: the context decides, the name list is the second net
    WCHECK(wo176rules::arranged(1, 0, false) == Why::Context, "combat_arrangedFight set -> an arranged fight");
    WCHECK(wo176rules::arranged(0, 1, false) == Why::Context, "combat_forceUnarmedWhileInUnarmedSkirmish set -> an arranged fight");
    WCHECK(wo176rules::arranged(1, 1, true) == Why::Context, "both contexts and a listed name -> the context is the reason");
    WCHECK(wo176rules::arranged(0, 0, true) == Why::Name, "no context but a listed name (the neighbours' brawl) -> the second net");
    WCHECK(wo176rules::arranged(-1, -1, true) == Why::Name, "contexts unreadable, a listed name -> the second net still holds");
    WCHECK(wo176rules::arranged(0, 0, false) == Why::None && wo176rules::arranged(-1, -1, false) == Why::None && wo176rules::arranged(-1, 0, false) == Why::None,
           "no context, no listed name, or unreadable -> not arranged (never a guess)");
    WCHECK(std::strcmp(wo176rules::context_said(1, 0), "combat_arrangedFight") == 0 && std::strcmp(wo176rules::context_said(0, 1), "combat_forceUnarmedWhileInUnarmedSkirmish") == 0 &&
           std::strcmp(wo176rules::context_said(0, 0), "-") == 0, "the log names the context that said it");
    WCHECK(std::strcmp(wo176rules::why_name(Why::Context), "context") == 0 && std::strcmp(wo176rules::why_name(Why::Name), "by-actor-name") == 0, "why names");
    WCHECK(wo176rules::kArrangedContextCount == 2 && std::strcmp(wo176rules::kArrangedContexts[0], "combat_arrangedFight") == 0,
           "the two contexts the quest graph sets (souboj_na_poli.xml)");
    // the meadow fight's fighters are NOT on WO-174's name list: the context is what catches them
    WCHECK(!wo174rules::scene_owned("ttac_man_3") && !wo174rules::scene_owned("tzel_man_2") && !wo174rules::scene_owned("ttac_procek"),
           "the meadow fight's actors (ttac_/tzel_) are caught by context only");
    // ---- A1: a figure already in it
    WCHECK(wo176rules::figure_in_fight(true, false, false), "the actor's opponent is the figure -> it is in the fight");
    WCHECK(wo176rules::figure_in_fight(false, true, true), "the engine's relation test says hostile in one skirmish -> in the fight");
    WCHECK(!wo176rules::figure_in_fight(false, true, false) && !wo176rules::figure_in_fight(false, false, true) && !wo176rules::figure_in_fight(false, false, false),
           "an unanswered relation test alone never says yes");
    WCHECK(wo176rules::should_remove(true, false, false) && !wo176rules::should_remove(false, true, true),
           "removed only when the fight is turned on the figure; a relation-only (passive) membership is logged, not removed (no re-add churn)");
    // ---- A1: cache and rate limit
    WCHECK(wo176rules::cache_fresh(10.0, 10.2) && !wo176rules::cache_fresh(10.0, 10.6) && !wo176rules::cache_fresh(0.0, 0.1) && !wo176rules::cache_fresh(10.0, 9.0),
           "a context read is reused for 0.5 s only");
    WCHECK(wo176rules::should_log(0.0, 5.0) && !wo176rules::should_log(5.0, 9.0) && wo176rules::should_log(5.0, 15.0), "WO176-ARRANGED once per actor per 10 s");
    WCHECK(wo176rules::kTickS <= 0.5 && wo176rules::kScanRadiusM >= 30.0f, "the 'already in it' check runs twice a second over 30 m or more");
    WCHECK(wo176rules::tick_interval(0.0, 100.0) == wo176rules::kIdleTickS && wo176rules::tick_interval(90.0, 100.0) == wo176rules::kTickS &&
           wo176rules::tick_interval(60.0, 100.0) == wo176rules::kIdleTickS && wo176rules::tick_interval(110.0, 100.0) == wo176rules::kIdleTickS,
           "the world walk: every 4 s with no story actor seen, 2 Hz for 30 s after one is (a clock that went back -> idle)");
    WCHECK(wo176rules::kIdleTickS >= 2.0 && wo176rules::kIdleTickS <= 5.0, "idle cadence: a few seconds (the Lua's 1 Hz brain pause and every keep-out site still act at once)");
    // ---- A1: the aftermath (the live run: quest fighters finished off after the controller went Done) and the figure's shield
    WCHECK(wo176rules::arranged_now(0, 0, false, 100.0, 130.0) == Why::Aftermath, "contexts lifted 30 s ago -> still the story's");
    WCHECK(wo176rules::arranged_now(0, 0, false, 100.0, 100.0 + wo176rules::kAftermathS) == Why::None, "after the aftermath -> an ordinary NPC again");
    WCHECK(wo176rules::arranged_now(1, 0, false, 0.0, 5.0) == Why::Context && wo176rules::arranged_now(0, 0, true, 0.0, 5.0) == Why::Name,
           "a set context or a listed name decide first");
    WCHECK(wo176rules::arranged_now(0, 0, false, 0.0, 5.0) == Why::None && wo176rules::arranged_now(0, 0, false, 100.0, 90.0) == Why::None,
           "never seen arranged, or a clock that went back -> not the story's");
    WCHECK(std::strcmp(wo176rules::why_name(Why::Aftermath), "context-aftermath") == 0, "the aftermath is named in the log");
    WCHECK(wo176rules::kAftermathS >= 30.0 && wo176rules::kAftermathS <= 120.0, "the aftermath is a short window");
    WCHECK(wo176rules::shield_wanted(true, 0.0, 10.0), "a story actor near -> the figure is shielded");
    WCHECK(wo176rules::shield_wanted(false, 10.0, 12.0) && !wo176rules::shield_wanted(false, 10.0, 10.0 + wo176rules::kShieldOffS),
           "it stays for kShieldOffS after the last story actor was near, then goes");
    WCHECK(!wo176rules::shield_wanted(false, 0.0, 10.0), "never near -> never shielded");
    WCHECK(wo176rules::kShieldContextCount == 2 && std::strcmp(wo176rules::kShieldContexts[0], "combat_invisible") == 0 &&
           std::strcmp(wo176rules::kShieldContexts[1], "combat_alwaysWithdrawSkirmish") == 0, "the game's own contexts (Libs/Tables/ai/ScriptContext.xml)");
    // ---- A1: WO-173's real hit -- the arranged fight comes first, whatever the switches, and routes the blow nowhere
    {
        wo173rules::Pre p{};
        p.arranged = true;
        WCHECK(wo173rules::precondition(p) == wo173rules::Fallback::Arranged, "arranged -> Fallback::Arranged");
        p.on = false;
        WCHECK(wo173rules::precondition(p) == wo173rules::Fallback::Arranged, "arranged beats mp_real_hits off (the raw path never runs either)");
        p.faulted = true; p.armed = false; p.rowKnown = false; p.victimDead = true;
        WCHECK(wo173rules::precondition(p) == wo173rules::Fallback::Arranged, "arranged beats every other precondition");
        WCHECK(wo173rules::routes_nowhere(wo173rules::Fallback::Arranged), "Arranged routes the blow nowhere");
        bool any = false;
        for (int f = 0; f <= 10; ++f) any = any || wo173rules::routes_nowhere(static_cast<wo173rules::Fallback>(f));
        WCHECK(!any, "every other fallback still takes the raw path (no blow dropped silently)");
        WCHECK(std::strcmp(wo173rules::fallback_name(wo173rules::Fallback::Arranged), "arranged-fight") == 0, "fallback=arranged-fight");
        wo173rules::Pre q{};
        WCHECK(wo173rules::precondition(q) == wo173rules::Fallback::None, "not arranged, all set -> the engine path as before");
    }
    // ---- B2: the blocking referral
    {
        using wo165::refer_blocking;
        WCHECK(refer_blocking(true, false, true, 0x8, true), "the old flag alone: a blocking player is referred even against a striking copy");
        WCHECK(!refer_blocking(true, true, true, 0x8, true), "0x80: a striking copy -> the engine judges the block (no referral)");
        WCHECK(!refer_blocking(true, true, true, 0x8 | 0x400, true), "striking with another bit set -> still striking");
        WCHECK(refer_blocking(true, true, true, 0x2, true), "0x80: a copy in Guard (outside its window) -> referred as before");
        WCHECK(refer_blocking(true, true, true, 0x8, false), "0x80: the attacker's State unreadable -> referred (never a guess)");
        WCHECK(!refer_blocking(true, true, false, 0x2, true) && !refer_blocking(false, true, true, 0x2, true), "no block held, or no referral asked -> replayed");
        WCHECK(wo165::kStateStriking == 0x8, "Striking is the model's 0x8 (WO-163 ModelRead state bits)");
        WCHECK(refer_blocking(true, true, true, 0x8, true, -1, true), "B2 live: a striking copy but the player's block zone unset (-1, no Opponent) -> referred");
        WCHECK(refer_blocking(true, true, true, 0x8, true, 2, false), "the block zone unreadable -> referred");
        WCHECK(!refer_blocking(true, true, true, 0x8, true, 5, true), "a striking copy and a set block zone -> the engine judges");
    }
    if (passed) *passed = g_pass;
    return g_fail;
}
