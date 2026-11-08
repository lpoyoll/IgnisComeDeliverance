// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT QoL (IgnisComeDeliverance): the launcher's updater rules, against the GitHub API's own release shape.
using KCDMP_launcher.Models;
using Xunit;

namespace KcdMp.Client.Tests;

public class UpdateRulesTests
{
    private const string Dl = "https://github.com/lpoyoll/IgnisComeDeliverance/releases/download/";
    private const string Page = "https://github.com/lpoyoll/IgnisComeDeliverance/releases/tag/";
    private static readonly string H10 = new('a', 64), H11 = new('b', 64), H12 = new('c', 64);

    private static string Rel(int icd, string? digest, string body = "", bool draft = false, string? url = null, string? page = null) =>
        $$"""
        { "tag_name": "v0.64.1-icd{{icd}}", "name": "IgnisComeDeliverance 0.64.1-icd{{icd}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": true,
          "html_url": "{{page ?? Page + "v0.64.1-icd" + icd}}", "body": "{{body}}",
          "assets": [
            { "name": "IgnisComeDeliverance-DirectInstall-0.64.1-icd{{icd}}.zip", "size": 5, "digest": "sha256:{{new string('d', 64)}}",
              "browser_download_url": "{{Dl}}v0.64.1-icd{{icd}}/IgnisComeDeliverance-DirectInstall-0.64.1-icd{{icd}}.zip" },
            { "name": "IgnisComeDeliverance-Setup-0.64.1-icd{{icd}}.exe", "size": 103391744{{(digest is null ? "" : ", \"digest\": \"sha256:" + digest + "\"")}},
              "browser_download_url": "{{url ?? Dl + "v0.64.1-icd" + icd + "/IgnisComeDeliverance-Setup-0.64.1-icd" + icd + ".exe"}}" } ] }
        """;

    [Theory]
    [InlineData("icd11-1a2b3c4", 11)]
    [InlineData("v0.64.1-icd10", 10)]
    [InlineData("6e21f1d-2611071805", null)]   // the builds before icd11 carried no release number
    [InlineData("dev", null)]
    [InlineData("v0.64.1-icd9x", null)]
    public void The_release_number_is_read_from_a_build_id_or_a_tag(string s, int? expected) => Assert.Equal(expected, UpdateRules.Icd(s));

    [Fact]
    public void The_newest_newer_release_is_offered_with_GitHubs_own_checksum()
    {
        string json = $"[{Rel(12, H12)},{Rel(11, H11)},{Rel(10, H10)}]";
        var o = UpdateRules.Newer(json, 11);
        Assert.NotNull(o);
        Assert.Equal(12, o!.Icd);
        Assert.Equal(H12, o.Sha256);
        Assert.Equal("IgnisComeDeliverance-Setup-0.64.1-icd12.exe", o.SetupName);
        Assert.StartsWith(Dl, o.SetupUrl);
        Assert.Equal(103391744, o.Size);
    }

    [Fact]
    public void Nothing_is_offered_when_up_to_date_or_for_a_build_without_a_number()
    {
        string json = $"[{Rel(11, H11)},{Rel(10, H10)}]";
        Assert.Null(UpdateRules.Newer(json, 11));
        Assert.Null(UpdateRules.Newer(json, 12));
        Assert.Null(UpdateRules.Newer(json, null));
        Assert.Null(UpdateRules.Newer("not json", 1));
    }

    [Fact]
    public void A_draft_a_foreign_download_or_a_release_without_a_checksum_is_never_offered()
    {
        Assert.Null(UpdateRules.Newer($"[{Rel(12, H12, draft: true)}]", 11));
        Assert.Null(UpdateRules.Newer($"[{Rel(12, H12, url: "https://evil.example/IgnisComeDeliverance-Setup-0.64.1-icd12.exe")}]", 11));
        Assert.Null(UpdateRules.Newer($"[{Rel(12, H12, page: "https://github.com/someone/else/releases/tag/x")}]", 11));
        Assert.Null(UpdateRules.Newer($"[{Rel(12, null)}]", 11));
        // the next one down is offered instead of the unusable newest
        Assert.Equal(12, UpdateRules.Newer($"[{Rel(13, null)},{Rel(12, H12)}]", 11)!.Icd);
    }

    [Fact]
    public void Without_GitHubs_digest_the_release_notes_hash_is_used()
    {
        var o = UpdateRules.Newer($"[{Rel(12, null, body: "- Setup: `" + H12 + "`")}]", 11);
        Assert.Equal(H12, o!.Sha256);
    }

    [Fact]
    public void A_download_matches_only_with_the_same_checksum_and_size()
    {
        var o = UpdateRules.Newer($"[{Rel(12, H12)}]", 11)!;
        Assert.True(UpdateRules.Matches(o, H12, 103391744));
        Assert.True(UpdateRules.Matches(o, H12.ToUpperInvariant(), 103391744));
        Assert.False(UpdateRules.Matches(o, H11, 103391744));
        Assert.False(UpdateRules.Matches(o, H12, 1));
    }

    [Fact]
    public void The_setup_waits_for_the_launcher_to_close_and_quotes_its_path()
    {
        string c = UpdateRules.RunAfterExitCommand(4242, @"C:\Users\O'Brien\AppData\Local\Temp\IgnisComeDeliverance-update\Setup.exe");
        Assert.Contains("Wait-Process -Id 4242", c);
        Assert.Contains(@"'C:\Users\O''Brien\AppData\Local\Temp\IgnisComeDeliverance-update\Setup.exe'", c);
        Assert.Contains("-ArgumentList '/SP-'", c);
    }
}
