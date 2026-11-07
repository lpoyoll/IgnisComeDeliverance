// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-171 M: the pure rules of the partner marker's picture (unit-tested in native/tests/wo171_tests.cpp). No engine access here.
//
// How a partner's marker gets its picture and its hover name (docs/WO-171-findings.md, M0). The map draws its markers from groups keyed
// by the NAME of the mark's type (GUIModule's type -> name function; the picture is <name>_icon.dds), and a group's hover title is the
// name string its first mark stored when it was made. So one reserved, otherwise unused type id per player slot (0x3C..0x3F: the game's
// own table gives them no name) is pointed, in that function's jump table, at a stub that returns the DLL's own per-slot string. The
// string holds the player's name while the mark is made and the picture's name (kct_r3 ...) from then on.

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo171rules {

constexpr int kColours = 4;     // red, purple, blue, green (players 1-4)
constexpr int kFrames = 8;      // the art's rotation frames
constexpr int kPictures = kColours * kFrames;   // 32 pictures per revision
constexpr int kRevisions = 4;                  // a name can only be loaded once per game run: a colour taken over by another name uses the next revision's keys
constexpr size_t kNameMax = 16;   // "kct_r3" is 6

// A picture code on the pipe: 0 = none (the game's own icon), else rev * 32 + colour * 8 + frame + 1 (1..128).
inline bool picture_code_ok(unsigned code) { return code <= static_cast<unsigned>(kPictures * kRevisions); }

// "kct_r3", "kct_r3b", "kct_r3c", "kct_r3d": colour letter (r p b g), frame digit 0-7, revision suffix (none, b, c, d). False for code 0 or
// out of range.
inline bool picture_name(unsigned code, char* out, size_t cap) {
    if (code == 0 || !picture_code_ok(code) || cap < 8) return false;
    const unsigned i = (code - 1) % kPictures, rev = (code - 1) / kPictures;
    const unsigned c = i / kFrames, f = i % kFrames;
    static const char kLetter[kColours] = {'r', 'p', 'b', 'g'};
    out[0] = 'k'; out[1] = 'c'; out[2] = 't'; out[3] = '_'; out[4] = kLetter[c]; out[5] = static_cast<char>('0' + f);
    if (rev == 0) { out[6] = 0; } else { out[6] = static_cast<char>('a' + rev); out[7] = 0; }
    return true;
}

// The jump table of GUIModule's type-name function. Found in the function's own bytes, never by address:
//   cmp ebx,N-1 ; ... ; lea rdx,[rip+d] (rdx = the module base) ; movsxd rax,ebx ; mov ecx,[rdx+rax*4+TABLE] ; add rcx,rdx ; jmp rcx
// ebx = type + 1. Returns false -- nothing assumed -- unless every piece is found in the first `n` bytes and rdx works out to be
// `moduleBase`. `fnAddr` is where `fn` lives in the process (the rip-relative lea is resolved against it).
struct TypeTable { uint32_t tableRva = 0; uint32_t entries = 0; };
inline bool parse_type_table(const uint8_t* fn, size_t n, uintptr_t fnAddr, uintptr_t moduleBase, TypeTable* out) {
    uint32_t entries = 0; size_t lea = 0, mov = 0; bool haveCmp = false, haveLea = false, haveMov = false;
    for (size_t i = 0; i + 7 <= n; ++i) {
        if (!haveCmp && fn[i] == 0x83 && fn[i + 1] == 0xFB && fn[i + 2] >= 0x10 && fn[i + 2] < 0x80) { entries = fn[i + 2] + 1u; haveCmp = true; }
        else if (haveCmp && !haveLea && fn[i] == 0x48 && fn[i + 1] == 0x8D && fn[i + 2] == 0x15) { lea = i; haveLea = true; }
        else if (haveLea && !haveMov && fn[i] == 0x8B && fn[i + 1] == 0x8C && fn[i + 2] == 0x82) { mov = i; haveMov = true; }
    }
    if (!haveCmp || !haveLea || !haveMov || mov < lea) return false;
    int32_t rel = 0, disp = 0;
    std::memcpy(&rel, fn + lea + 3, 4);
    std::memcpy(&disp, fn + mov + 3, 4);
    if (disp <= 0) return false;
    if (fnAddr + lea + 7 + static_cast<intptr_t>(rel) != moduleBase) return false;   // rdx is the module base, or this is not the function
    out->tableRva = static_cast<uint32_t>(disp);
    out->entries = entries;
    return true;
}

// A type's stub as the game's table has it: lea rax,[rip+rel32] ; add rsp,30h ; pop rbx ; ret  (13 bytes).
constexpr size_t kStubLen = 13;
inline bool is_name_stub(const uint8_t* b) {
    static const uint8_t tail[6] = {0x48, 0x83, 0xC4, 0x30, 0x5B, 0xC3};
    return b[0] == 0x48 && b[1] == 0x8D && b[2] == 0x05 && std::memcmp(b + 7, tail, 6) == 0;
}
// The same stub returning `target` instead (target and stub both in process memory, within 2 GB).
inline bool build_stub(uint8_t* out13, uintptr_t stubAddr, uintptr_t target) {
    const intptr_t rel = static_cast<intptr_t>(target) - static_cast<intptr_t>(stubAddr + 7);
    if (rel > INT32_MAX || rel < INT32_MIN) return false;
    const int32_t r = static_cast<int32_t>(rel);
    out13[0] = 0x48; out13[1] = 0x8D; out13[2] = 0x05;
    std::memcpy(out13 + 3, &r, 4);
    out13[7] = 0x48; out13[8] = 0x83; out13[9] = 0xC4; out13[10] = 0x30; out13[11] = 0x5B; out13[12] = 0xC3;
    return true;
}
// The target a stub returns, read back (verification).
inline uintptr_t stub_target(const uint8_t* stub, uintptr_t stubAddr) {
    int32_t rel = 0; std::memcpy(&rel, stub + 3, 4);
    return stubAddr + 7 + static_cast<intptr_t>(rel);
}

// The reserved types: one per player slot (1..4). Unused in the game's table (all four return the shared "?" stub).
constexpr int kPicTypes[kColours] = {0x3C, 0x3D, 0x3E, 0x3F};
constexpr size_t kSlotBuf = 32;   // a slot's string: a player's name (<= 31 bytes) or a picture name

// A pipe picture code -> slot 0..3 (-1 for none).
inline int slot_of_code(unsigned code) { return (code == 0 || !picture_code_ok(code)) ? -1 : static_cast<int>(((code - 1) % kPictures) / kFrames); }

// A label as the pin keeps it: printable ASCII and UTF-8 bytes of at most 31 bytes, cut on a character boundary, never empty.
inline size_t sanitize_label(const uint8_t* in, size_t n, char* out32) {
    size_t w = 0;
    for (size_t i = 0; i < n && w < kSlotBuf - 1;) {
        const uint8_t c = in[i];
        size_t len = c < 0x80 ? 1 : (c >> 5) == 6 ? 2 : (c >> 4) == 14 ? 3 : (c >> 3) == 30 ? 4 : 0;
        if (len == 0 || i + len > n) break;
        if (len == 1 && (c < 0x20 || c == 0x7F || c == '"' || c == '\\')) { ++i; continue; }
        if (w + len > kSlotBuf - 1) break;
        for (size_t k = 0; k < len; ++k) out32[w++] = static_cast<char>(in[i + k]);
        i += len;
    }
    while (w > 0 && out32[w - 1] == ' ') --w;
    if (w == 0) { std::memcpy(out32, "Player", 7); return 6; }
    out32[w] = 0;
    return w;
}

} // namespace kcdmp::wo171rules
