// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>KCT fix (icd14): NPC copies as pure puppets -- the host's explicit untrack (D) and the pure_copy switch (A).</summary>
public class Icd14NpcTests
{
    [Fact]
    public void The_untrack_is_its_own_QuestHost_kind_appended_after_the_others()
    {
        Assert.Equal(9, Protocol.QuestHostUntrack);
        Assert.Equal("untrack", Protocol.QuestHostName(Protocol.QuestHostUntrack));
        byte[] others = [Protocol.QuestHostChange, Protocol.QuestHostResult, Protocol.QuestHostCheckpoint, Protocol.QuestHostHold,
                         Protocol.QuestHostMode, Protocol.QuestHostConverse, Protocol.QuestHostReplayAck, Protocol.QuestHostItemCount];
        Assert.DoesNotContain(Protocol.QuestHostUntrack, others);
        Assert.True(others.All(k => k < Protocol.QuestHostUntrack));   // append-only: an older joiner logs it as unparsed
    }

    [Theory]
    [InlineData("tzel_olbram", true)]
    [InlineData("horse_tzel_1", true)]
    [InlineData("", false)]
    [InlineData("bad name\") end os.exit(", false)]
    public void Only_an_NPC_name_goes_into_the_untrack_and_into_the_Lua_call(string name, bool ok) =>
        Assert.Equal(ok, Wo137Text.IsNpc(name));

    [Fact]
    public void Pure_copy_is_a_WO213_switch_the_agent_knows()
    {
        Assert.Contains("pure_copy", W213Rules.SwitchNames);
        Assert.Equal(W213Rules.SwitchNames.Length, W213Rules.SwitchNames.Distinct().Count());
    }
}
