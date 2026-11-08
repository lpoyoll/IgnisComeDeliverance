// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

// KCT fix (icd14) D: the host's explicit untrack. The host's mod says `npc_untrack <npc>` when its scan drops an NPC from the tracked set
// (KCD2MP_NpcUntrackDiff, mp_npc_lease on); this agent (the host) sends it to every partner on the WO-137 QuestHost lane (kind 9); a
// joiner's agent hands it to the mod, which releases (freezes, shown) its copy at once instead of waiting the lease. The mod's own lease
// (3 x the heartbeat + 0.5 s) stays the fallback -- an older peer, quest sync off (no WO-137 host role), a lost frame.
//   MP-ICD14-UNTRACK out|in npc=<name> n=<count>      (the first 20 each way, then every 100th)
public partial class GameBridge
{
    private long _icd14UntrackOut, _icd14UntrackIn;

    private static bool Icd14LogNth(long n) => n <= 20 || n % 100 == 0;

    /// <summary>The mod's npc_untrack line (host): one name, sent to every partner.</summary>
    private void Icd14OnUntrackLine(string? arg)
    {
        string npc = (arg ?? "").Trim();
        if (!W137Host || !Wo137Text.IsNpc(npc)) return;
        long n = Interlocked.Increment(ref _icd14UntrackOut);
        if (Icd14LogNth(n)) Console.WriteLine($"MP-ICD14-UNTRACK out npc={npc} n={n}");
        foreach (byte g in Wo134Peers()) _ = Wo137SendAsync(Protocol.QuestHostUp, g, Protocol.QuestHostUntrack, 0, npc);
    }

    /// <summary>A joiner: the host stopped tracking <paramref name="npc"/>.</summary>
    private void Icd14OnHostUntrack(string npc)
    {
        if (!W137Joiner || !Wo137Text.IsNpc(npc)) return;
        long n = Interlocked.Increment(ref _icd14UntrackIn);
        if (Icd14LogNth(n)) Console.WriteLine($"MP-ICD14-UNTRACK in npc={npc} n={n}");
        _ = ExecLuaAsync($"if KCD2MP_NpcHostUntracked then KCD2MP_NpcHostUntracked(\"{npc}\") end");
    }
}
