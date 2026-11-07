// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "wo176.h"
#include "wo176_rules.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <iterator>
#include <unordered_map>

#include "engine.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_scan.h"
#include "script_context.h"
#include "wo136.h"
#include "wo163.h"
#include "wo174_rules.h"

namespace kcdmp::wo176 {
namespace {

using namespace kcdmp::wo176rules;

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

struct Read { double at = 0; int ctxA = -1, ctxU = -1; double lastArranged = 0; };
struct Shield { bool on = false; bool wrote[kShieldContextCount]{}; double lastNear = 0; };
struct Said { double at = 0; };

std::atomic<bool> g_on{true};
std::atomic<bool> g_walk{true};   // the figure check's world walk: the host's only (arranged fights run in the host's world); the agent says
std::atomic<DWORD> g_mainTid{0};
// Written on the main thread only; another thread reads a copy under the lock.
SRWLOCK g_lock = SRWLOCK_INIT;
std::unordered_map<uint32_t, Read> g_reads;
std::unordered_map<uint32_t, Said> g_said;
std::unordered_map<uint32_t, Shield> g_shields;   // figure eid -> what this module set on it (main thread only)
double g_next = 0;
std::atomic<double> g_storySeenAt{0.0};   // the last time any site or the walk saw a story actor (the walk's cadence follows it)
std::atomic<uint32_t> c_asked{0}, c_keptContext{0}, c_keptName{0}, c_reads{0}, c_unreadable{0}, c_offThread{0}, c_removed{0}, c_removeFail{0},
                      c_ticks{0}, c_seen{0}, c_keptAftermath{0}, c_passive{0}, c_shieldOn{0}, c_shieldOff{0}, c_shieldFail{0};

const char* name_of(uint32_t eid) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "";
}

// The actor's two contexts: read on the main thread (cached kCacheS), the cache elsewhere.
Read read_contexts(uint32_t eid) {
    const double now = now_s();
    Read r{};
    {
        AcquireSRWLockShared(&g_lock);
        auto it = g_reads.find(eid);
        if (it != g_reads.end()) r = it->second;
        ReleaseSRWLockShared(&g_lock);
    }
    if (cache_fresh(r.at, now)) return r;
    if (GetCurrentThreadId() != g_mainTid.load()) { c_offThread.fetch_add(1); return r; }   // the engine is read on the main thread only
    void* soul = hits::soul_of_eid(eid);
    r.at = now;
    r.ctxA = soul ? sctx::has_soul_context(soul, kArrangedContexts[0]) : -1;
    r.ctxU = soul ? sctx::has_soul_context(soul, kArrangedContexts[1]) : -1;
    if (r.ctxA == 1 || r.ctxU == 1) r.lastArranged = now;   // the aftermath runs from the last read that saw a context
    c_reads.fetch_add(1);
    if (r.ctxA < 0 && r.ctxU < 0) c_unreadable.fetch_add(1);
    AcquireSRWLockExclusive(&g_lock);
    if (g_reads.size() > 2048) g_reads.clear();
    g_reads[eid] = r;
    ReleaseSRWLockExclusive(&g_lock);
    return r;
}

void say(uint32_t eid, const char* nm, Why why, const Read& r, const char* site, const char* figure, const char* tail) {
    const double now = now_s();
    if (GetCurrentThreadId() == g_mainTid.load()) {
        Said& s = g_said[eid];
        if (!figure && !should_log(s.at, now)) return;
        s.at = now;
    }
    logf("WO176-ARRANGED skirmish=npc:%s quest=%s context=%s npc=%s site=%s%s%s figure_kept_out=%s -- a quest's arranged fight is the story's: "
         "no partner's figure joins it, no threat, no forced target, no lock, no blow routed into it",
         nm, why_name(why), context_said(r.ctxA, r.ctxU), nm, site ? site : "?", figure ? " figure=" : "", figure ? figure : "", tail);
}

// ---- the "already in it" check (tick) ----
}   // namespace
int arranged_context(uint32_t eid);
namespace {
struct ScanCtx { uint32_t eids[kMaxScan]; int n = 0; };

void visit(void* e, const char* /*name*/, float, float, float, bool isHorse, void* ctx) {
    auto* c = static_cast<ScanCtx*>(ctx);
    if (isHorse || c->n >= kMaxScan || !e) return;
    const uint32_t id = engine::entity_id(e);
    if (!id || motion::is_avatar_eid(id)) return;
    for (int i = 0; i < c->n; ++i) if (c->eids[i] == id) return;
    c->eids[c->n++] = id;
}

// The shield on one figure (main thread): only the counts this module wrote are ever cleared (the store is refcounted, WO-68 3).
void set_shield(uint32_t feid, Shield& sh, bool want) {
    if (want == sh.on) return;
    void* soul = hits::soul_of_eid(feid);
    if (!soul) return;
    char what[200]{};
    int len = 0;
    bool fail = false;
    for (int c = 0; c < kShieldContextCount; ++c) {
        int r = 0;
        const char* said = "";
        if (want) {
            r = sctx::set_soul_context_quiet(soul, kShieldContexts[c], true);
            sh.wrote[c] = r == 1;
            said = r == 1 ? "set" : r == 0 ? "already" : "FAILED";
        } else {
            r = sh.wrote[c] ? sctx::set_soul_context_quiet(soul, kShieldContexts[c], false) : 0;
            said = !sh.wrote[c] ? "not-ours" : r == 1 ? "cleared" : r == 0 ? "already-clear" : "FAILED";
            sh.wrote[c] = false;
        }
        fail = fail || r < 0;
        const int n = std::snprintf(what + len, sizeof what - len, "%s%s(%s)", c ? "+" : "", kShieldContexts[c], said);
        if (n > 0) len += n;
        if (len >= static_cast<int>(sizeof what)) len = static_cast<int>(sizeof what) - 1;
    }
    sh.on = want;
    (want ? c_shieldOn : c_shieldOff).fetch_add(1);
    if (fail) c_shieldFail.fetch_add(1);
    logf("WO176-SHIELD figure=%s %s contexts=%s -- %s", name_of(feid), want ? "on" : "off", what,
         want ? "a quest's arranged fight is near: the game's own combat leaves the partner's figure out of it" : "no story fight near any more");
}

void check_figures() {
    uint32_t aeids[8]{}; void* asouls[8]{};
    const int na = hits::avatar_list(aeids, asouls, 8);
    if (na <= 0) return;
    npcscan::Anchor anchors[8]{};
    int nAnch = 0;
    for (int i = 0; i < na; ++i) {
        void* e = engine::entity_by_id(aeids[i]);
        float p[3]{};
        if (e && engine::entity_world_pos(e, p)) anchors[nAnch++] = npcscan::Anchor{p[0], p[1], p[2]};
    }
    if (!nAnch) return;
    ScanCtx ctx{};
    uint32_t walked = 0; uint8_t refuse = 0;
    if (!npcscan::for_each_in_radius(anchors, nAnch, kScanRadiusM, &visit, &ctx, &walked, &refuse)) return;
    c_ticks.fetch_add(1);
    for (int k = 0; k < ctx.n; ++k) {
        const uint32_t npc = ctx.eids[k];
        const Read r = read_contexts(npc);
        if (arranged(r.ctxA, r.ctxU, false) != Why::Context) continue;   // removal only while the fight runs (the aftermath has no skirmish)
        c_seen.fetch_add(1);
        g_storySeenAt.store(now_s());
        void* nsoul = hits::soul_of_eid(npc);
        motion::NpcCombat nc{};
        const bool haveCombat = motion::read_npc_combat(npc, &nc);
        for (int i = 0; i < na; ++i) {
            void* fsoul = asouls[i] ? asouls[i] : hits::soul_of_eid(aeids[i]);
            bool answered = false, hostile = false;
            if (nsoul && fsoul) hits::skirmish_hostile(nsoul, fsoul, &answered, &hostile);
            if (!figure_in_fight(haveCombat && nc.opponentEid == aeids[i], answered, hostile)) continue;
            if (!should_remove(haveCombat && nc.opponentEid == aeids[i], answered, hostile)) { c_passive.fetch_add(1); continue; }
            uint64_t rv = 0;
            const bool ok = fsoul && hits::skirmish_remove(fsoul, &rv);
            (ok ? c_removed : c_removeFail).fetch_add(1);
            const int dropped = kcdmp::wo136::forget_npc(npc, "WO-176: a quest's arranged fight");
            char tail[96];
            std::snprintf(tail, sizeof tail, "removed skirmish_leave=%s mod_targets_dropped=%d", ok ? "done" : "FAILED", dropped);
            say(npc, name_of(npc), Why::Context, r, "wo176-tick", name_of(aeids[i]), tail);
        }
    }
    // the shield: every figure with a story actor within the radius carries the game's own "not a combat target" / "leave the skirmish"
    const double now = now_s();
    for (int i = 0; i < na; ++i) {
        void* fe = engine::entity_by_id(aeids[i]);
        float fp[3]{};
        bool storyNear = false;
        if (fe && engine::entity_world_pos(fe, fp)) {
            for (int k = 0; k < ctx.n && !storyNear; ++k) {
                if (arranged_context(ctx.eids[k]) != 1) continue;
                void* ne = engine::entity_by_id(ctx.eids[k]);
                float np[3]{};
                if (!ne || !engine::entity_world_pos(ne, np)) continue;
                const float dx = np[0] - fp[0], dy = np[1] - fp[1];
                storyNear = dx * dx + dy * dy <= kScanRadiusM * kScanRadiusM;
            }
        }
        Shield& sh = g_shields[aeids[i]];
        if (storyNear) sh.lastNear = now;
        set_shield(aeids[i], sh, shield_wanted(storyNear, sh.lastNear, now));
    }
    // figures that left: forget them (their bodies are gone; nothing to clear)
    for (auto it = g_shields.begin(); it != g_shields.end();) {
        bool live = false;
        for (int i = 0; i < na; ++i) live = live || aeids[i] == it->first;
        it = live ? std::next(it) : g_shields.erase(it);
    }
}

} // namespace

bool on() { return g_on.load(); }

int arranged_context(uint32_t eid) {
    if (!eid || !g_on.load()) return 0;
    const Read r = read_contexts(eid);
    if (arranged_now(r.ctxA, r.ctxU, false, r.lastArranged, now_s()) != Why::None) return 1;
    if (r.ctxA < 0 && r.ctxU < 0) return -1;
    return 0;
}

bool kept_out(uint32_t eid, const char* site) {
    if (!eid) return false;
    c_asked.fetch_add(1);
    const char* nm = name_of(eid);
    const bool listed = kcdmp::wo174rules::scene_owned(nm);
    Read r{};
    if (g_on.load()) r = read_contexts(eid);
    const Why w = g_on.load() ? arranged_now(r.ctxA, r.ctxU, listed, r.lastArranged, now_s()) : arranged(-1, -1, listed);
    if (w == Why::None) return false;
    if (w == Why::Context || w == Why::Aftermath) g_storySeenAt.store(now_s());
    (w == Why::Context ? c_keptContext : w == Why::Aftermath ? c_keptAftermath : c_keptName).fetch_add(1);
    if (w == Why::Context || w == Why::Aftermath) say(eid, nm, w, r, site, nullptr, "yes");
    return true;   // the name list's own WO174-BRAWL line is the caller's (hits::scene_owned_eid)
}

void tick() {
    g_mainTid.store(GetCurrentThreadId());
    if (!g_on.load() || !g_walk.load()) {
        for (auto& kv : g_shields) set_shield(kv.first, kv.second, false);   // mp_arranged_guard off: what this module set goes
        g_shields.clear();
        return;
    }
    const double now = now_s();
    if (now < g_next) return;
    g_next = now + tick_interval(g_storySeenAt.load(), now);
    check_figures();
}

int status_text(char* out, int n) {
    return std::snprintf(out, n,
        "wo176 arranged_guard=%s walk=%s asked=%u kept_context=%u kept_aftermath=%u kept_name=%u reads=%u unreadable=%u off_thread=%u checks=%u arranged_seen=%u removed=%u remove_failed=%u passive_member=%u shield_on=%u shield_off=%u shield_failed=%u",
        g_on.load() ? "on" : "off", g_walk.load() ? "host" : "off", c_asked.load(), c_keptContext.load(), c_keptAftermath.load(), c_keptName.load(), c_reads.load(), c_unreadable.load(),
        c_offThread.load(), c_ticks.load(), c_seen.load(), c_removed.load(), c_removeFail.load(), c_passive.load(), c_shieldOn.load(), c_shieldOff.load(), c_shieldFail.load());
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = n <= 0 || cap == 0 ? 0 : (static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap - 1);
            return wo163::kROk;
        }
        case kOpConfig: {   // [on:1] or [on:1][walk:1] (255 = unchanged); walk = this machine is the host (the agent's role)
            if ((len != 2 && len != 3) || cap < 2) return wo163::kRBadRequest;
            if (body[1] != 255) { const bool was = g_on.exchange(body[1] != 0); if (was != g_on.load()) logf("WO176-CFG mp_arranged_guard %s", g_on.load() ? "on" : "off"); }
            if (len == 3 && body[2] != 255) {
                const bool was = g_walk.exchange(body[2] != 0);
                if (was != g_walk.load()) logf("WO176-CFG the figure check %s -- %s", g_walk.load() ? "on" : "off", g_walk.load() ? "the host" : "a joiner (no arranged fight runs in this world)");
            }
            out[0] = g_on.load() ? 1 : 0; out[1] = g_walk.load() ? 1 : 0;
            *outLen = 2;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

} // namespace kcdmp::wo176
