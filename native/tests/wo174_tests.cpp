// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-174: engine-free checks of the scripted-brawl guard's rules (wo174_rules.h). Linked into KCDMP_NativeTests; wo174_tests() returns the
// number of failures.
#include <cstdio>

#include "wo174_rules.h"

using namespace kcdmp::wo174rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo174_tests(int* passed) {
    // ---- the neighbours' brawl's actors (the field: rvacka_neighbour_1 / _2) and the other scripted fights
    WCHECK(scene_owned("rvacka_neighbour_1"), "the petty feud's brawl actor 1");
    WCHECK(scene_owned("rvacka_neighbour_2"), "the petty feud's brawl actor 2");
    WCHECK(scene_owned("rvacka_apprentice_3"), "a village brawl's actor (rvacka = brawl)");
    WCHECK(scene_owned("RVACKA_Neighbour_1"), "case does not matter");
    WCHECK(scene_owned("fistfightsChampion_champion"), "the fist-fight champion");
    WCHECK(scene_owned("tbuk_duel_man_1") && scene_owned("arena_fighter_2") && scene_owned("tournament_knight_4") && scene_owned("brawl_man"),
           "duels, arenas, tournaments, brawls");
    // ---- never an ordinary villager, a guard, an animal, a bandit, the player or a partner's figure
    WCHECK(!scene_owned("ttac_man_9") && !scene_owned("tvez_man_14") && !scene_owned("prepadeniNaCeste_bandit_5") && !scene_owned("Dude"),
           "ordinary NPCs and the player are not scene-owned");
    WCHECK(!scene_owned("kcd2mp_1") && !scene_owned("kcd2mp_2"), "a partner's figure is not scene-owned");
    WCHECK(!scene_owned("SpawnedAnimal_WildDog_71450B44_0") && !scene_owned("") && !scene_owned(nullptr), "an animal, an empty name, no name");
    // ---- the partner's figure
    WCHECK(is_figure("kcd2mp_0") && is_figure("kcd2mp_2") && is_figure("kcd2mp_12"), "kcd2mp_<n> is a figure");
    WCHECK(!is_figure("kcd2mp_") && !is_figure("kcd2mp_x") && !is_figure("xkcd2mp_1") && !is_figure("kcd2mp_1a") && !is_figure("Dude") && !is_figure(nullptr),
           "nothing else is");
    // ---- the helper
    WCHECK(contains_ci("abcDEF", "cde") && !contains_ci("abc", "abcd") && !contains_ci("abc", "") && !contains_ci(nullptr, "a"), "contains_ci edges");
    WCHECK(kBrawlLogEveryS >= 5.0, "the kept-out line is rate-limited");
    if (passed) *passed = g_pass;
    return g_fail;
}
