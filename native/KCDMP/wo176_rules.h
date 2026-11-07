// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-176 (A1): the pure rules of keeping a partner's figure out of a quest's arranged fight, by the fight's own context (unit-tested in
// native/tests/wo176_tests.cpp). No engine access here.
//
// A quest that arranges a fight marks every soul it puts into it with the game's own entity contexts while its controller runs (the field
// fight of konecZabomysichValek, souboj_na_poli.xml: SetEntityContext combat_arrangedFight and combat_forceUnarmedWhileInUnarmedSkirmish on
// both groups, IsActive <- the controller's Active; docs/WO-175-findings.md 2). An actor that carries either is in a story fight: the mod
// never adds a figure to its skirmish, never turns it on a figure, never pairs the host with it, never routes a figure's blow into it, and a
// figure already in it is taken out. WO-174's name list (wo174_rules.h) stays as the second net for scenes that set no context.

#include <cstdint>

namespace kcdmp::wo176rules {

inline constexpr const char* kArrangedContexts[] = { "combat_arrangedFight", "combat_forceUnarmedWhileInUnarmedSkirmish" };
inline constexpr int kArrangedContextCount = 2;

inline constexpr double kCacheS = 0.5;       // one actor's context read is reused this long (a blow asks at several sites in one frame)
inline constexpr double kLogEveryS = 10.0;   // WO176-ARRANGED once per actor per this many seconds
inline constexpr double kTickS = 0.5;        // the "already in it" check runs twice a second while a story fight is about
// The check walks every entity of the level to find the actors near a figure (npcscan::for_each_in_radius): live, at 2 Hz it raised the
// DLL's own cost in a 3-copy fight from ~700 to ~1,150 us a frame. With no story actor seen for kActiveHoldS it runs every kIdleTickS;
// any keep-out site or the walk itself seeing one brings the 2 Hz cadence back at once.
inline constexpr double kIdleTickS = 4.0;
inline constexpr double kActiveHoldS = 30.0;
inline double tick_interval(double storySeenAt, double now) {
    return (storySeenAt > 0.0 && now - storySeenAt >= 0.0 && now - storySeenAt < kActiveHoldS) ? kTickS : kIdleTickS;
}
inline constexpr float kScanRadiusM = 40.0f; // actors this near a figure are read by the check
inline constexpr int kMaxScan = 96;          // at most this many actors read per check
// After a story fight's contexts lift (the controller Done), its actors stay the story's for this long: the quest heals them, runs its
// own aftermath dialogue, and a knocked-out fighter must not be finished off by a partner's figure in that window (the live A1 run:
// two quest fighters killed after the fight -> a witness turned on the host).
inline constexpr double kAftermathS = 60.0;
// While a story fight is within kScanRadiusM of a figure, the figure carries the game's own "not a combat target" and "always leave a
// skirmish" contexts (only the counts this module wrote are cleared); off again once no story actor has been near it for kShieldOffS.
inline constexpr const char* kShieldContexts[] = { "combat_invisible", "combat_alwaysWithdrawSkirmish" };
inline constexpr int kShieldContextCount = 2;
inline constexpr double kShieldOffS = 5.0;

enum class Why : uint8_t { None = 0, Context = 1, Name = 2, Aftermath = 3 };

// The verdict on one actor: its two contexts (each 1 set, 0 not set, -1 unreadable) and whether its name is on WO-174's list.
// The context decides; the name list catches a scene that sets no context.
inline Why arranged(int ctxArranged, int ctxUnarmed, bool nameListed) {
    if (ctxArranged == 1 || ctxUnarmed == 1) return Why::Context;
    if (nameListed) return Why::Name;
    return Why::None;
}

// The same, with the aftermath: an actor whose contexts were last seen set lastArrangedAt seconds ago is still the story's for kAftermathS.
inline Why arranged_now(int ctxArranged, int ctxUnarmed, bool nameListed, double lastArrangedAt, double now) {
    const Why w = arranged(ctxArranged, ctxUnarmed, nameListed);
    if (w != Why::None) return w;
    if (lastArrangedAt > 0.0 && now - lastArrangedAt >= 0.0 && now - lastArrangedAt < kAftermathS) return Why::Aftermath;
    return Why::None;
}

// The shield on a figure: on while a story actor is near (or was within kShieldOffS); off after that.
inline bool shield_wanted(bool storyNear, double lastNearAt, double now) {
    if (storyNear) return true;
    return lastNearAt > 0.0 && now - lastNearAt >= 0.0 && now - lastNearAt < kShieldOffS;
}

// Which context said it (for the log): the first one set, else "-".
inline const char* context_said(int ctxArranged, int ctxUnarmed) {
    if (ctxArranged == 1) return kArrangedContexts[0];
    if (ctxUnarmed == 1) return kArrangedContexts[1];
    return "-";
}

inline const char* why_name(Why w) {
    switch (w) {
        case Why::Context: return "context";
        case Why::Name: return "by-actor-name";
        case Why::Aftermath: return "context-aftermath";
        default: return "-";
    }
}

// Is a figure inside this actor's fight? The actor's opponent is the figure, or the engine's own relation test says the two are hostile
// opponents in one skirmish. An unanswered relation test alone never says yes.
inline bool figure_in_fight(bool opponentIsFigure, bool relationAnswered, bool relationHostile) {
    return opponentIsFigure || (relationAnswered && relationHostile);
}

// Take the figure out of the skirmish only when the fight is turned on it (the actor's opponent is the figure). Live (A1 run 3): a
// figure spawned from a local villager's soul is added by the engine itself as a passive member of that village's fight; every removal
// is answered by an immediate re-add (195 adds for 79 removals), while with its brain paused no fighter ever turned on it (0). So a
// relation-only membership is logged, not removed.
inline bool should_remove(bool opponentIsFigure, bool relationAnswered, bool relationHostile) {
    (void)relationAnswered; (void)relationHostile;
    return opponentIsFigure;
}

// A cached read is fresh for kCacheS; a read from another thread than the game's main thread never touches the engine (cache only).
inline bool cache_fresh(double readAt, double now) { return readAt > 0.0 && now - readAt >= 0.0 && now - readAt < kCacheS; }

// Rate limit of the log line per actor.
inline bool should_log(double lastSaid, double now) { return lastSaid <= 0.0 || now - lastSaid >= kLogEveryS; }

} // namespace kcdmp::wo176rules
