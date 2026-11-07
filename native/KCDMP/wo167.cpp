// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-167: 0.48.5's native half (wo167.h).
#include "wo167.h"
#include "wo163.h"
#include "wo167_rules.h"

#include "combat_swing.h"
#include "log.h"

#include <cstdio>
#include <cstring>

namespace kcdmp::wo167 {

int status_text(char* out, int n) { return rttr::swing_watch_status(out, n); }

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpSwingSeen: {
            if (len != 1 + 4 || cap < 9) return wo163::kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            uint32_t ageMs = 0, waitMs = 0;
            const wo167rules::Seen s = rttr::swing_watch_last(eid, &ageMs, &waitMs);
            out[0] = static_cast<uint8_t>(s);
            std::memcpy(out + 1, &ageMs, 4);
            std::memcpy(out + 5, &waitMs, 4);
            *outLen = 9;
            return wo163::kROk;
        }
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
            return wo163::kROk;
        }
        case kOpConfig: {
            if (len != 1 + 1 || cap < 2) return wo163::kRBadRequest;
            if (body[1] != 255) rttr::swing_watch_set(body[1] != 0);
            out[0] = rttr::swing_watch_on() ? 1 : 0;
            out[1] = rttr::swing_watch_faulted() ? 1 : 0;
            *outLen = 2;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

} // namespace kcdmp::wo167
