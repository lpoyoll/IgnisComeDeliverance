// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// KCT fix (icd14): the pure rules of the DLL link's two native fixes (engine-free: native/tests check them).
//   oversize  a request longer than the pipe takes (1024 bytes, the NPC sample batch up to kNpcSamplesMaxLen) used to break the
//             connection ("PIPE: payload of 1485 bytes is out of range; dropping the connection") -- and every drop released every
//             body the DLL writes. Now its body is read and thrown away in chunks and the agent gets a failure reply (reason
//             kReasonOversize); only a length past the hard cap (garbage, not a request) still disconnects.
//   pump      the agent's calls are one request, one reply: the DLL took one request a frame (~60 a second against the agent's ~86 a
//             second of polls). After the frame's queued work the main thread waits a short while for the agent's next request and
//             runs it in the same frame -- up to kPumpMaxItems or kPumpBudgetUs of this frame, whichever first, in order. A wait that
//             finds nothing three times running stops the waiting for a few frames (an idle agent costs the frame nothing).
// Both behind WO-213 native switch bits (the agent's word, op 146): pipe_oversize_skip, pipe_pump_budget (default on).
#include <cstdint>
#include <cstdio>

namespace kcdmp::icd14pipe {

// ---- the switch bits (in the WO-213 word; on until the agent says otherwise)
constexpr uint32_t kBitOversizeSkip = 0x1000;   // pipe_oversize_skip
constexpr uint32_t kBitPumpBudget   = 0x2000;   // pipe_pump_budget
constexpr uint32_t kBitsDefault = kBitOversizeSkip | kBitPumpBudget;

// ---- oversize requests
constexpr uint32_t kMaxPayload  = 1024;         // every request but the NPC sample batch
constexpr uint32_t kHardCap     = 64 * 1024;    // past this the length is garbage: disconnect (the 16-bit length never reaches it)
constexpr uint32_t kDiscardChunk = 1024;        // the body is thrown away through the serve loop's stack buffer, this much at a time
constexpr uint8_t  kReasonOversize = 20;        // mirrors PipeReason.Oversize

enum class Size { Ok, Discard, Disconnect };

// What the serve loop does with a request of this type and length.
inline Size classify(uint8_t type, uint32_t len, uint8_t samplesType, uint32_t samplesMax, bool skipOn) {
    if (len <= kMaxPayload) return Size::Ok;
    if (type == samplesType && len <= samplesMax) return Size::Ok;
    if (!skipOn || len > kHardCap) return Size::Disconnect;
    return Size::Discard;
}

// The chunks a discard reads (each at most `buf` bytes, the last one the rest).
inline uint32_t discard_chunks(uint32_t len, uint32_t buf) { return buf == 0 ? 0 : (len + buf - 1) / buf; }

// ---- the main-thread pump's budget
constexpr uint32_t kPumpMaxItems = 8;       // one-shot items a frame before it stops waiting for more
constexpr double   kPumpBudgetUs = 1500.0;  // the frame time the queued work and the waits may take together
constexpr double   kLingerUs     = 500.0;   // one wait for the agent's next request (its round trip is a few hundred us)
constexpr uint32_t kMissesToRest = 3;       // waits that found nothing in a row before the pump rests
constexpr uint32_t kRestFrames   = 8;       // frames it then does not wait at all

// Another wait for the next request is allowed while the frame ran some work, is under the item cap and has budget left.
inline bool may_linger(uint32_t ranThisFrame, double spentUs) {
    return ranThisFrame > 0 && ranThisFrame < kPumpMaxItems && spentUs < kPumpBudgetUs;
}
// How long the next wait may last: one linger, never past the budget.
inline double linger_us(double spentUs) {
    const double left = kPumpBudgetUs - spentUs;
    return left <= 0 ? 0 : (left < kLingerUs ? left : kLingerUs);
}
// A frame that stopped at the cap or the budget (more might have come).
inline bool budget_hit(uint32_t ran, double spentUs) { return ran >= kPumpMaxItems || spentUs >= kPumpBudgetUs; }

struct LingerGate { uint32_t misses = 0; uint32_t rest = 0; };
// Whether this frame may wait at all (false while resting; each call while resting uses up one frame of the rest).
inline bool gate_open(LingerGate& g) {
    if (g.rest > 0) { --g.rest; return false; }
    return true;
}
inline void gate_note(LingerGate& g, bool found) {
    if (found) { g.misses = 0; return; }
    if (++g.misses >= kMissesToRest) { g.misses = 0; g.rest = kRestFrames; }
}

// ---- MP-HEALTH area=native-pipe, every 10 s
constexpr double kHealthEveryS = 10.0;
struct Health {
    double   startS = 0;
    uint64_t frames = 0, busyFrames = 0, items = 0;
    uint32_t maxItems = 0, budgetHits = 0, lingers = 0, lingerHits = 0;
};
inline void note_frame(Health& h, uint32_t items, bool budgetHit) {
    ++h.frames;
    if (items > 0) { ++h.busyFrames; h.items += items; }
    if (items > h.maxItems) h.maxItems = items;
    if (budgetHit) ++h.budgetHits;
}
inline void note_linger(Health& h, bool found) { ++h.lingers; if (found) ++h.lingerHits; }
inline bool due(const Health& h, double nowS) { return h.startS > 0 && nowS - h.startS >= kHealthEveryS; }
// The mean is over the frames that ran any queued work (an idle frame is not a request served).
inline double mean_per_frame(const Health& h) { return h.busyFrames ? static_cast<double>(h.items) / static_cast<double>(h.busyFrames) : 0.0; }
inline int format(const Health& h, uint32_t oversizeSkipped, bool pumpOn, char* buf, size_t n) {
    return std::snprintf(buf, n,
        "MP-HEALTH area=native-pipe requests_per_frame_mean=%.2f max=%u oversize_skipped=%u budget_hits=%u frames=%llu busy_frames=%llu "
        "requests=%llu lingers=%u linger_hits=%u pump_budget=%s",
        mean_per_frame(h), h.maxItems, oversizeSkipped, h.budgetHits, static_cast<unsigned long long>(h.frames),
        static_cast<unsigned long long>(h.busyFrames), static_cast<unsigned long long>(h.items), h.lingers, h.lingerHits, pumpOn ? "on" : "off");
}
inline void restart(Health& h, double nowS) { h = Health{}; h.startS = nowS; }

} // namespace kcdmp::icd14pipe
