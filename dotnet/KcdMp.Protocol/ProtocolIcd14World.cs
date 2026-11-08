// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// KCT fix (icd14): the host's world state, pushed (dotnet/KcdMp.Client/Icd14World.cs, GameBridge.Icd14World.cs). Five W203 side-channel
// kinds (LootMsg text, W203TextMax), 17-21 (14 is W203Health on the diagnostics branch, 16 agent B's). The relay passes them as every W203 row.
//   17 QuestPush    host -> joiner, every 30 s: "v1|<world>|<join>|<seq>|<n>|<128 x 8 hex>" -- the digest quest map's 128-bucket sums
//   18 QuestWant    joiner -> host: "v1|<world>|<seq>|<b,b,...>" -- the buckets that differ here (at most 12 a round)
//   19 QuestEntries host -> joiner: "v1|<world>|<seq>|<bucket>|<part>|<nparts>|<key=value\n...>" -- the host's entries of one bucket
//   20 SnapAsk      joiner -> host: "v1|<sid>|<x>|<y>|<z>|<r>|<why>|<corpse,corpse,...>" -- an area snapshot around the joiner
//   21 SnapPart     host -> joiner: "v1|<sid>|<part>|<nparts>|<items>" -- doors (D), the host's corpses (C), the asked corpses alive there (A)
// An older peer never sees these (a mixed release is refused at the relay); a receiver that meets an unknown kind ignores it (counted).
// ---------------------------------------------------------------------------
public static partial class Protocol
{
    public const byte W203IcdQuestPush = 17, W203IcdQuestWant = 18, W203IcdQuestEntries = 19, W203IcdSnapAsk = 20, W203IcdSnapPart = 21;
}
