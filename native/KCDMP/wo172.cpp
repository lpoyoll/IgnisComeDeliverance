// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-172: 0.48.8's native half (wo172.h).
#include "wo172.h"
#include "wo163.h"
#include "wo172_rules.h"

#include "engine.h"
#include "hits.h"
#include "log.h"
#include "rttr_abi.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <unordered_set>

namespace kcdmp::wo172 {

namespace {

using namespace kcdmp::wo172rules;

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

std::atomic<bool> g_gate{true};                         // mp_dead_wins (the agent's switch)
std::unordered_map<uint32_t, DeadState> g_state;        // main thread only
std::unordered_set<std::string> g_said;                 // "eid|what" said once
std::atomic<uint32_t> c_marks{0}, c_reads{0}, c_readFails{0}, c_skipActivity{0}, c_skipHands{0}, c_skipRetarget{0}, c_skipStrike{0},
    c_skipOther{0}, c_revived{0};

bool name_ok(const char* s, size_t n) {
    if (n == 0 || n > 63) return false;
    for (size_t i = 0; i < n; ++i) {
        const char c = s[i];
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')) return false;
    }
    return true;
}

void read_soul(uint32_t eid, DeadState& s, double now) {
    if (read_fresh(s, now)) return;
    s.readAt = now;
    c_reads.fetch_add(1, std::memory_order_relaxed);
    void* soul = engine::entity_by_id(eid) ? hits::soul_of_eid(eid) : nullptr;
    bool dead = false;
    if (!soul || !rttr::soul_bool(soul, "IsDead", &dead)) { c_readFails.fetch_add(1, std::memory_order_relaxed); s.haveRead = false; return; }
    // a body the mod marked dead whose soul reads alive once the mark's trust ran out: revived (a reload reuses entity ids)
    if (s.haveRead && s.soulDead && !dead) { c_revived.fetch_add(1, std::memory_order_relaxed); s.markedAt = -1e9; }
    s.haveRead = true;
    s.soulDead = dead;
}

std::atomic<uint32_t>& counter_for(const char* what) {
    if (!std::strcmp(what, "activity")) return c_skipActivity;
    if (!std::strcmp(what, "hands")) return c_skipHands;
    if (!std::strcmp(what, "retarget")) return c_skipRetarget;
    if (!std::strcmp(what, "strike")) return c_skipStrike;
    return c_skipOther;
}

} // namespace

bool copy_dead(uint32_t eid) {
    if (!eid) return false;
    const double now = now_s();
    if (g_state.size() > 4096) g_state.clear();
    DeadState& s = g_state[eid];
    read_soul(eid, s, now);
    return is_dead(s, now);
}

bool skip_dead(uint32_t eid, const char* what) {
    if (!g_gate.load(std::memory_order_relaxed) || !copy_dead(eid)) return false;
    counter_for(what).fetch_add(1, std::memory_order_relaxed);
    char key[48];
    std::snprintf(key, sizeof key, "%X|%s", eid, what);
    if (g_said.size() > 4096) g_said.clear();
    if (g_said.insert(key).second) {
        const char* nm = nullptr;
        void* e = engine::entity_by_id(eid);
        if (e) nm = engine::entity_name(e);
        logf("WO172-DEAD npc=%s eid=0x%X skipped=%s -- a dead body is never worked again (the host's death wins; said once per body and kind)",
             nm ? nm : "?", eid, what);
    }
    return true;
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    auto text = [&](const char* fmt, auto... a) {
        const int n = std::snprintf(reinterpret_cast<char*>(out), cap, fmt, a...);
        *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
    };
    switch (op) {
        case kOpDead: {
            if (len < 2 || cap < 48) return wo163::kRBadRequest;
            const size_t n = body[1];
            if (len != 2 + n || !name_ok(reinterpret_cast<const char*>(body + 2), n)) return wo163::kRBadRequest;
            const std::string name(reinterpret_cast<const char*>(body + 2), n);
            const uint32_t eid = hits::eid_of_name(name.c_str());
            if (!eid) { text("refused=no-entity"); return wo163::kRNoActor; }
            DeadState& s = g_state[eid];
            s.markedAt = now_s();
            s.readAt = -1e9;   // read the soul again at the next ask
            c_marks.fetch_add(1);
            text("marked eid=0x%X", eid);
            return wo163::kROk;
        }
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = n <= 0 || cap == 0 ? 0 : (static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap - 1);
            return wo163::kROk;
        }
        case kOpConfig: {
            if (len != 2 || cap < 1) return wo163::kRBadRequest;
            if (body[1] != 255) {
                const bool was = g_gate.exchange(body[1] != 0);
                if (was != g_gate.load()) logf("WO172-CFG mp_dead_wins %s", g_gate.load() ? "on" : "off");
            }
            out[0] = g_gate.load() ? 1 : 0;
            *outLen = 1;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

int status_text(char* out, int n) {
    return std::snprintf(out, n,
        "wo172 dead_wins=%s marks=%u reads=%u read_fails=%u revived=%u skipped activity=%u hands=%u retarget=%u strike=%u other=%u tracked=%zu",
        g_gate.load() ? "on" : "off", c_marks.load(), c_reads.load(), c_readFails.load(), c_revived.load(), c_skipActivity.load(), c_skipHands.load(),
        c_skipRetarget.load(), c_skipStrike.load(), c_skipOther.load(), g_state.size());
}

} // namespace kcdmp::wo172
