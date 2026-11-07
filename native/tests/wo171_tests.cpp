// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-171 M: engine-free checks of the marker picture's name table, the jump table found in the type-name function's bytes, the stub that
// returns the DLL's own string, and the label text. Linked into KCDMP_NativeTests; wo171_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo171_rules.h"

using namespace kcdmp::wo171rules;

namespace {
int g_fail = 0, g_pass = 0;
#define DCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

// The first 0x3D bytes of GUIModule's type -> name function in the Modding Tools build 1.5.5 (RVA 0x24200) and the stubs of three types
// as the game's table has them.
const uint8_t kFn[] = {
    0x40, 0x53, 0x48, 0x83, 0xEC, 0x30, 0x8D, 0x59, 0x01, 0x48, 0x8D, 0x0D, 0xEB, 0x66, 0x5F, 0x00, 0xE8, 0xFB, 0x87, 0x3D, 0x00,
    0x83, 0xFB, 0x61, 0x0F, 0x87, 0x82, 0x04, 0x00, 0x00, 0x48, 0x8D, 0x15, 0xDB, 0xBD, 0xFD, 0xFF, 0x48, 0x63, 0xC3, 0x8B, 0x8C,
    0x82, 0xB0, 0x46, 0x02, 0x00, 0x48, 0x03, 0xCA, 0xFF, 0xE1, 0x48, 0x8D, 0x05, 0x5D, 0x5A, 0x3F, 0x00, 0x48, 0x83, 0xC4, 0x30, 0x5B,
};
const uint8_t kStubGeneralPoi[13] = {0x48, 0x8D, 0x05, 0x09, 0x5A, 0x3F, 0x00, 0x48, 0x83, 0xC4, 0x30, 0x5B, 0xC3};
const uint8_t kStubUnused[13]     = {0x48, 0x8D, 0x05, 0x0D, 0x0E, 0x40, 0x00, 0x48, 0x83, 0xC4, 0x30, 0x5B, 0xC3};
} // namespace

int wo171_tests(int* passed) {
    g_fail = g_pass = 0;
    // the names: 4 revisions x 4 colours x 8 frames, colour letter + frame digit + revision suffix, all unique
    {
        static char seen[kPictures * kRevisions][9];
        const char* want = "rpbg";
        const char* suffix = "\0bcd";
        for (unsigned code = 1; code <= kPictures * kRevisions; ++code) {
            char n[9]{};
            DCHECK(picture_name(code, n, sizeof n), "code %u has a name", code);
            const unsigned i = (code - 1) % kPictures, rev = (code - 1) / kPictures, c = i / 8, f = i % 8;
            char exp[9]{}; std::snprintf(exp, sizeof exp, "kct_%c%u%c", want[c], f, suffix[rev]);
            DCHECK(std::strcmp(n, exp) == 0, "code %u is %s (got %s)", code, exp, n);
            DCHECK(slot_of_code(code) == static_cast<int>(c), "code %u is slot %u", code, c);
            std::memcpy(seen[code - 1], n, 9);
            for (unsigned k = 1; k < code; ++k) DCHECK(std::strcmp(seen[k - 1], n) != 0, "%s is unique", n);
        }
        char n[9];
        DCHECK(!picture_name(0, n, sizeof n), "code 0 = no picture");
        DCHECK(!picture_name(129, n, sizeof n), "code 129 is out of range");
        DCHECK(!picture_name(1, n, 7), "a buffer too small is refused");
        DCHECK(picture_code_ok(0) && picture_code_ok(128) && !picture_code_ok(129), "the pipe's code range");
        DCHECK(slot_of_code(0) == -1 && slot_of_code(129) == -1, "no slot for none / out of range");
        DCHECK(picture_name(1, n, sizeof n) && std::strcmp(n, "kct_r0") == 0 && picture_name(33, n, sizeof n) && std::strcmp(n, "kct_r0b") == 0, "revision 1 of red frame 0 is kct_r0b");
        DCHECK(slot_of_code(33) == 0 && slot_of_code(65 + 9) == 1, "a revision keeps its slot");
    }
    // the jump table, found in the function's own bytes: rdx must work out to the module base
    {
        const uintptr_t base = 0x7FF6A0000000ull, fnAddr = base + 0x24200;
        TypeTable t;
        DCHECK(parse_type_table(kFn, sizeof kFn, fnAddr, base, &t), "the table is found in the real function's bytes");
        DCHECK(t.tableRva == 0x246B0 && t.entries == 0x62, "table at RVA 0x%X with %u entries (a type + 1 indexes it)", t.tableRva, t.entries);
        TypeTable u;
        DCHECK(!parse_type_table(kFn, sizeof kFn, fnAddr, base + 0x1000, &u), "a base that does not match what rdx works out to refuses");
        DCHECK(!parse_type_table(kFn, 20, fnAddr, base, &u), "too few bytes: refused");
        uint8_t bad[sizeof kFn]; std::memcpy(bad, kFn, sizeof kFn); bad[40] = 0x8C;   // the table load is not a table load
        DCHECK(!parse_type_table(bad, sizeof bad, fnAddr, base, &u), "bytes that are not the function refuse");
    }
    // the stubs
    {
        DCHECK(is_name_stub(kStubGeneralPoi) && is_name_stub(kStubUnused), "the game's own stubs are recognised");
        uint8_t odd[13]; std::memcpy(odd, kStubUnused, 13); odd[9] = 0x20;
        DCHECK(!is_name_stub(odd), "a stub with other bytes is not");
        const uintptr_t stubAt = 0x7FF6B0001000ull, buf = 0x7FF6B0001200ull;
        uint8_t mine[kStubLen];
        DCHECK(build_stub(mine, stubAt, buf), "our stub builds");
        DCHECK(is_name_stub(mine) && stub_target(mine, stubAt) == buf, "it has the game's shape and returns our buffer");
        DCHECK(stub_target(kStubGeneralPoi, 0x7FF6A0024470ull) == 0x7FF6A0024470ull + 7 + 0x3F5A09, "a game stub's target reads back");
        DCHECK(!build_stub(mine, 0x7FF6B0001000ull, 0x100000000ull), "a target beyond 2 GB is refused, nothing assumed");
    }
    // the reserved types are the four unused ones
    {
        DCHECK(kPicTypes[0] == 0x3C && kPicTypes[1] == 0x3D && kPicTypes[2] == 0x3E && kPicTypes[3] == 0x3F, "types 0x3C..0x3F, one per slot");
    }
    // the label text
    {
        char o[kSlotBuf];
        auto lab = [&](const char* s) { return sanitize_label(reinterpret_cast<const uint8_t*>(s), std::strlen(s), o); };
        DCHECK(lab("LongPlayerNam") == 13 && std::strcmp(o, "LongPlayerNam") == 0, "a name stays as it is");
        DCHECK(lab("") == 6 && std::strcmp(o, "Player") == 0, "empty = Player");
        DCHECK(lab("A\tB\nC\"D\\E") == 5 && std::strcmp(o, "ABCDE") == 0, "controls, quotes and backslashes are dropped");
        DCHECK(lab("  Ann  ") == 5 && std::strcmp(o, "  Ann") == 0, "trailing spaces trimmed");
        char big[80]; std::memset(big, 'x', 79); big[79] = 0;
        DCHECK(lab(big) == 31 && std::strlen(o) == 31, "at most 31 bytes");
        DCHECK(lab("Dr\xC5\xBE" "an \xC5\xBD" "i\xC5\xBE" "ek") == std::strlen(o) && std::strcmp(o, "Dr\xC5\xBE" "an \xC5\xBD" "i\xC5\xBE" "ek") == 0, "UTF-8 letters stay whole");
        // 20 two-byte letters = 40 bytes: cut at a character boundary (15 letters = 30 bytes)
        char two[41]; for (int i = 0; i < 20; ++i) { two[i * 2] = static_cast<char>(0xC5); two[i * 2 + 1] = static_cast<char>(0xBE); } two[40] = 0;
        DCHECK(lab(two) == 30, "a 40-byte name is cut on a character boundary at 30 (got %zu)", lab(two));
    }
    if (passed) *passed = g_pass;
    return g_fail;
}
