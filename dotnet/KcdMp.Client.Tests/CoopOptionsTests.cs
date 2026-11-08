// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT QoL (IgnisComeDeliverance): the launcher's co-op options window and the auto-join rule.
using System.Text.Json.Nodes;
using KCDMP_launcher.Models;
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

public class CoopOptionsTests
{
    [Fact]
    public void Every_remembered_menu_choice_is_on_the_page_and_nothing_else()
    {
        var agent = Wo154MenuRules.Settings.Select(s => s.Key).OrderBy(k => k, StringComparer.Ordinal);
        var page = CoopOptions.All.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(agent, page);
        foreach (var o in CoopOptions.All)
            Assert.Equal(Wo154MenuRules.Find(o.Key)!.HostOwned, o.HostOnly);   // the host's levers are the page's "World rules"
        Assert.All(CoopOptions.All, o => Assert.Contains(o.Group, CoopOptions.Groups));
    }

    [Fact]
    public void A_missing_or_broken_file_reads_as_the_mods_defaults()
    {
        var d = CoopOptions.Read(null);
        Assert.True(d["FriendlyFire"] && d["FastTravel"] && d["BadgeIcon"] && d["QuietConsole"] && d["CrimeMode"]);
        Assert.False(d["CleanScreen"]);
        Assert.Equal(CoopOptions.Read(null), CoopOptions.Read("{ not json"));
    }

    [Fact]
    public void The_file_is_read_and_written_in_the_agents_own_form_keeping_other_keys()
    {
        const string file = "{ \"FriendlyFire\": false, \"CrimeMode\": \"individual\", \"MenuKey\": \"np_add\", \"Something\": 3 }";
        var d = CoopOptions.Read(file);
        Assert.False(d["FriendlyFire"]);
        Assert.False(d["CrimeMode"]);
        d["FriendlyFire"] = true;
        d["CrimeMode"] = true;
        d["CleanScreen"] = true;
        var o = JsonNode.Parse(CoopOptions.Write(file, d))!.AsObject();
        Assert.True(o["FriendlyFire"]!.GetValue<bool>());
        Assert.Equal("joint", o["CrimeMode"]!.GetValue<string>());
        Assert.True(o["CleanScreen"]!.GetValue<bool>());
        Assert.Equal("np_add", o["MenuKey"]!.GetValue<string>());   // the menu key and anything else are left alone
        Assert.Equal(3, o["Something"]!.GetValue<int>());
        Assert.Equal(d, CoopOptions.Read(o.ToJsonString()));
    }

    [Fact]
    public void Saving_writes_the_file_and_loading_reads_it_back()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kct-coop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string p = Path.Combine(dir, CoopOptions.FileName);
            var d = CoopOptions.Load(p);
            d["Whistle"] = false;
            Assert.True(CoopOptions.Save(p, d, out var err), err);
            Assert.False(CoopOptions.Load(p)["Whistle"]);
            Assert.False(File.Exists(p + ".tmp"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(true, false, true, "idle", true)]       // the launcher's Join, a character to bring: waiting for the host at once
    [InlineData(false, false, true, "idle", false)]     // AutoConnect off: the game's Join Game, as before
    [InlineData(true, true, true, "idle", false)]       // the host never joins
    [InlineData(true, false, false, "idle", false)]     // nothing to bring: the player picks a new character in the game
    [InlineData(true, false, true, "waiting", false)]   // already waiting: nothing again
    public void Join_waits_for_the_host_at_once_only_with_a_character_to_bring(bool auto, bool hosting, bool bring, string state, bool expected) =>
        Assert.Equal(expected, MenuTakeoverRule.JoinerPreArm(auto, hosting, bring, state));
}

public class HostingLaunchTests
{
    [Theory]
    [InlineData(true, null, "127.0.0.1", 7777, 7777, true)]       // the Host button: this PC's relay, its own address and port
    [InlineData(true, "ABCD-1234", "", 0, 7777, false)]            // a Steam join to a friend while this PC's relay still runs: NOT hosting
    [InlineData(true, null, "203.0.113.5", 7777, 7777, false)]     // a direct join to a friend's address while the relay runs
    [InlineData(true, null, "127.0.0.1", 7790, 7777, false)]       // another relay port on this PC (a second install's)
    [InlineData(false, null, "127.0.0.1", 7777, 7777, false)]      // the relay is not running: nothing to host on
    [InlineData(true, null, "localhost", 7777, 7777, true)]
    public void Only_a_launch_to_this_PCs_own_running_relay_is_the_hosts(bool relay, string? steam, string ip, int port, int hostPort, bool expected) =>
        Assert.Equal(expected, ConnectGateRule.IsHostingLaunch(relay, steam, ip, port, hostPort));
}
