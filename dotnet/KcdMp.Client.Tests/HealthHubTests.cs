// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT diag (icd14): the health hub and the partner's health message.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

[Collection("HealthHub")]
public class HealthHubTests
{
    [Fact]
    public void Lua_lines_parse_and_drop_the_clock()
    {
        Assert.True(HealthHub.TryParse("MP-HEALTH area=npc copies=12 holds=copy:12 t=1234.500", out var a, out var b));
        Assert.Equal("npc", a);
        Assert.Equal("copies=12 holds=copy:12", b);
    }

    [Theory]
    [InlineData("MP-HEALTH area= x=1")]
    [InlineData("MP-HEALTH area=bad|area x=1")]
    [InlineData("MP-HEALTHY area=x")]
    public void Malformed_lines_are_refused(string line) => Assert.False(HealthHub.TryParse(line, out _, out _));

    [Fact]
    public void Observe_keeps_the_newest_line_per_source_and_area_and_only_fresh_ones_are_sent()
    {
        HealthHub.Clear();
        long now = 1_000_000;
        HealthHub.Clock = () => now;
        HealthHub.Observe("lua", "[KCD2-MP] MP-HEALTH area=npc copies=1 t=5.0");
        HealthHub.Observe("lua", "[KCD2-MP] MP-HEALTH area=npc copies=2 t=15.0");
        HealthHub.Observe("agent", "MP-HEALTH area=pipe sent=9");
        HealthHub.Observe("agent", "nothing to see");
        var f = HealthHub.Fresh(now);
        Assert.Equal(2, f.Count);
        Assert.Contains(f, e => e.Source == "lua" && e.Body == "copies=2");
        now += HealthHub.PeerFreshMs + 1;
        Assert.Empty(HealthHub.Fresh(now));
        HealthHub.Clock = () => Environment.TickCount64;
        HealthHub.Clear();
    }

    [Fact]
    public void Peer_text_round_trips_and_stays_inside_the_side_channel()
    {
        var e = new HealthHub.Entry("native", "native-pipe", "a=1|b=2 \u00e9", 1000);
        string t = HealthHub.PeerText(e, 31_000);
        Assert.Equal("v1|native|native-pipe|30|a=1_b=2 _", t);
        Assert.True(HealthHub.TryParsePeer(t, out var s, out var a, out var age, out var body));
        Assert.Equal(("native", "native-pipe", 30L, "a=1_b=2 _"), (s, a, age, body));
        var big = new HealthHub.Entry("agent", "x", new string('k', 5000), 0);
        Assert.Equal(HealthHub.PeerTextMax, HealthHub.PeerText(big, 0).Length);
        Assert.False(HealthHub.TryParsePeer("v2|a|b|1|c", out _, out _, out _, out _));
    }

    [Fact]
    public void The_native_log_follower_reads_only_new_lines_and_restarts_on_a_new_file()
    {
        HealthHub.Clear();
        string p = Path.Combine(Path.GetTempPath(), $"hh-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(p, "old MP-HEALTH area=native-pipe a=0\n");
            var f = new HealthLogFollower(p, "native");
            Assert.Equal(0, f.Poll());   // the first poll starts at the end
            File.AppendAllText(p, "[1] MP-HEALTH area=native-pipe a=1\npartial MP-HEALTH area=native-npc b=");
            Assert.Equal(1, f.Poll());
            File.AppendAllText(p, "2\n");
            Assert.Equal(1, f.Poll());
            File.WriteAllText(p, "MP-HEALTH area=native-npc b=3\n");   // shorter: a new log
            Assert.Equal(1, f.Poll());
            Assert.Contains(HealthHub.Fresh(Environment.TickCount64), e => e.Area == "native-npc" && e.Body == "b=3");
        }
        finally { File.Delete(p); HealthHub.Clear(); }
    }
}
