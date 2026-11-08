// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// KCT fix (icd14) D: the host's explicit untrack. Until now a joiner learned
// that the host stopped tracking an NPC only from silence (no packet for 3 s),
// so one late heartbeat released a copy. The host now says it, on the WO-137
// QuestHost lane (same header, same routing, no new frame type):
//
//   Host kind 9 Untrack   "<npc>"    the host's scan dropped this NPC from its
//                                    tracked set: the joiner releases (freezes)
//                                    its copy now instead of waiting the lease
//
// APPEND-ONLY like the other kinds; an older joiner logs it as unparsed and
// keeps its own silence rule.
// ---------------------------------------------------------------------------
public static partial class Protocol
{
    public const byte QuestHostUntrack = 9;   // KCT fix (icd14) D
}
