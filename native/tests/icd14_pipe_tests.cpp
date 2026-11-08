// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT fix (icd14): engine-free checks of icd14_pipe_rules.h (oversize requests kept on the connection, the main thread's pump budget).
#include <cstdio>
#include <cstring>

#include "icd14_pipe_rules.h"
#include "wo213_rules.h"

using namespace kcdmp::icd14pipe;

namespace {
int g_fail = 0, g_pass = 0;
#define ICHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
constexpr uint8_t kSamples = 0x10;
constexpr uint32_t kSamplesMax = 4096;
} // namespace

int icd14_pipe_tests(int* passed) {
    // ---- the switches ship on, and never collide with WO-213's own bits
    ICHECK((kBitsDefault & kBitOversizeSkip) && (kBitsDefault & kBitPumpBudget), "both pipe switches ship on");
    ICHECK((kBitsDefault & kcdmp::wo213rules::kBitsDefault) == 0, "the pipe bits are apart from WO-213's");

    // ---- oversize requests
    ICHECK(classify(0x2B, 1024, kSamples, kSamplesMax, true) == Size::Ok, "1024 bytes is in range");
    ICHECK(classify(0x2B, 1025, kSamples, kSamplesMax, true) == Size::Discard, "1025 bytes: skipped, the connection kept");
    ICHECK(classify(0x2B, 1485, kSamples, kSamplesMax, true) == Size::Discard, "the field's 1485 bytes: skipped, the connection kept");
    ICHECK(classify(0x2B, 1485, kSamples, kSamplesMax, false) == Size::Disconnect, "switched off: the old rule (disconnect)");
    ICHECK(classify(kSamples, 4096, kSamples, kSamplesMax, true) == Size::Ok, "the sample batch may run to its own maximum");
    ICHECK(classify(kSamples, 4097, kSamples, kSamplesMax, true) == Size::Discard, "past it the batch is skipped too");
    ICHECK(classify(0x2B, 65535, kSamples, kSamplesMax, true) == Size::Discard, "every 16-bit length is under the hard cap");
    ICHECK(classify(0x2B, kHardCap + 1, kSamples, kSamplesMax, true) == Size::Disconnect, "past the hard cap: garbage, disconnect");
    ICHECK(discard_chunks(1485, kDiscardChunk) == 2 && discard_chunks(1024, kDiscardChunk) == 1 && discard_chunks(65535, kDiscardChunk) == 64,
           "a discard reads through the stack buffer in chunks");
    ICHECK(kDiscardChunk <= kSamplesMax, "a chunk fits the serve loop's body buffer");
    ICHECK(kReasonOversize == 20, "the reason code the agent knows (PipeReason.Oversize)");

    // ---- the pump's budget
    ICHECK(!may_linger(0, 0), "a frame with no queued work never waits");
    ICHECK(may_linger(1, 100) && may_linger(7, 1400), "under the cap and the budget: it waits for the next request");
    ICHECK(!may_linger(kPumpMaxItems, 100), "at 8 items it stops");
    ICHECK(!may_linger(3, kPumpBudgetUs), "at 1.5 ms it stops");
    ICHECK(linger_us(0) == kLingerUs && linger_us(1200) == 300 && linger_us(1600) == 0, "one wait never runs past the budget");
    ICHECK(budget_hit(8, 10) && budget_hit(2, 1500) && !budget_hit(2, 100), "a frame that stopped at the cap or the budget is counted");

    LingerGate g;
    ICHECK(gate_open(g), "the gate starts open");
    gate_note(g, false); gate_note(g, false);
    ICHECK(gate_open(g), "two misses: still waiting");
    gate_note(g, true); gate_note(g, false); gate_note(g, false);
    ICHECK(gate_open(g), "a hit resets the misses");
    gate_note(g, false);
    int closed = 0;
    for (uint32_t i = 0; i < kRestFrames; ++i) if (!gate_open(g)) ++closed;
    ICHECK(closed == static_cast<int>(kRestFrames) && gate_open(g), "three misses in a row: the pump rests 8 frames, then waits again");

    // ---- the MP-HEALTH line
    Health h;
    restart(h, 100.0);
    note_frame(h, 0, false);
    note_frame(h, 1, false);
    note_frame(h, 3, false);
    note_frame(h, 8, true);
    note_linger(h, true); note_linger(h, false);
    ICHECK(h.frames == 4 && h.busyFrames == 3 && h.items == 12 && h.maxItems == 8 && h.budgetHits == 1, "frames, busy frames, items, max, budget hits");
    ICHECK(mean_per_frame(h) == 4.0, "the mean is over frames that ran work");
    ICHECK(!due(h, 109.9) && due(h, 110.0), "every 10 s");
    char buf[400];
    format(h, 2, true, buf, sizeof buf);
    ICHECK(std::strstr(buf, "MP-HEALTH area=native-pipe requests_per_frame_mean=4.00 requests_per_frame_max=8 oversize_skipped=2 budget_hits=1") == buf, "%s", buf);
    ICHECK(std::strstr(buf, "lingers=2 linger_hits=1 pump_budget=1") != nullptr, "%s", buf);
    restart(h, 110.0);
    ICHECK(h.frames == 0 && h.startS == 110.0, "a new window");

    *passed = g_pass;
    return g_fail;
}
