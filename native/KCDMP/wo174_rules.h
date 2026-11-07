// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-174: the pure rules of the scripted-brawl guard (unit-tested in native/tests/wo174_tests.cpp). No engine access here.
//
// A scripted brawl or scene (the neighbours' brawl of the petty-feud quest, a quest fist fight, a duel, an arena or tournament fight) is the
// game's own: the mod never puts a partner's figure into its skirmish, never turns its actors on an avatar, never starts a fist-fight state
// in it. The brawl is known by its actors' names (the neighbours' brawl's are rvacka_neighbour_1 / _2, its random event `rvacka`; docs/
// WO-174-findings.md 0.4); the Lua half (kdcmp.lua W174.SCENE_WORDS) holds the same words.

#include <cstddef>
#include <cstring>

namespace kcdmp::wo174rules {

inline constexpr const char* kSceneWords[] = { "rvacka", "fistfight", "brawl", "duel", "arena", "tournament" };

inline char lower(char c) { return (c >= 'A' && c <= 'Z') ? static_cast<char>(c - 'A' + 'a') : c; }

// Case-insensitive "name contains word".
inline bool contains_ci(const char* name, const char* word) {
    if (!name || !word || !*word) return false;
    const size_t wn = std::strlen(word);
    for (const char* p = name; *p; ++p) {
        size_t i = 0;
        while (i < wn && p[i] && lower(p[i]) == lower(word[i])) ++i;
        if (i == wn) return true;
    }
    return false;
}

// Does this actor's name say it belongs to a scripted brawl or scene?
inline bool scene_owned(const char* name) {
    if (!name || !*name) return false;
    for (const char* w : kSceneWords) if (contains_ci(name, w)) return true;
    return false;
}

// A partner's figure: kcd2mp_<digits>.
inline bool is_figure(const char* name) {
    if (!name || std::strncmp(name, "kcd2mp_", 7) != 0) return false;
    const char* d = name + 7;
    if (!*d) return false;
    for (; *d; ++d) if (*d < '0' || *d > '9') return false;
    return true;
}

// The log line for a kept-out figure is said once per actor per this many seconds.
inline constexpr double kBrawlLogEveryS = 10.0;

} // namespace kcdmp::wo174rules
