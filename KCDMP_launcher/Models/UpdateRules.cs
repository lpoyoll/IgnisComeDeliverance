// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT QoL (IgnisComeDeliverance): the launcher's updater -- the pure rules (Pages/Home.Update.cs does the network and the files).
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KCDMP_launcher.Models
{
    /// <summary>
    /// Which release is newer than this build, and where its Setup is. Releases come only from this fork's GitHub repository;
    /// a Setup is offered only with a SHA-256 to check it against (GitHub's own digest of the uploaded file, or the release
    /// notes' "Setup: `hash`" line), and only from that repository's own download address.
    /// </summary>
    public static class UpdateRules
    {
        public const string Repo = "lpoyoll/IgnisComeDeliverance";
        public const string ReleasesApi = "https://api.github.com/repos/" + Repo + "/releases?per_page=20";
        public const string DownloadPrefix = "https://github.com/" + Repo + "/releases/download/";
        public const string PagePrefix = "https://github.com/" + Repo + "/releases/";

        public sealed record Offer(int Icd, string Tag, string Name, string PageUrl, string SetupName, string SetupUrl, string Sha256, long Size);

        private static readonly Regex IcdRx = new(@"(?:^|[^0-9a-z])icd(\d{1,4})(?![0-9A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SetupRx = new(@"^IgnisComeDeliverance-Setup-[0-9A-Za-z.\-]+\.exe$", RegexOptions.CultureInvariant);
        private static readonly Regex DigestRx = new(@"^sha256:([0-9a-f]{64})$", RegexOptions.CultureInvariant);
        private static readonly Regex NotesHashRx = new(@"Setup:\s*`([0-9a-f]{64})`", RegexOptions.CultureInvariant);

        /// <summary>The icd number in a build id ("icd11-1a2b3c4") or a tag ("v0.64.1-icd11"); null when there is none (a dev build).</summary>
        public static int? Icd(string? s)
        {
            var m = IcdRx.Match(s ?? "");
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
        }

        /// <summary>
        /// The newest release with a higher icd than <paramref name="currentIcd"/>, from the GitHub API's release list; null when
        /// there is none, this build has no icd number (a dev build is never "updated"), or the newest has no checkable Setup.
        /// </summary>
        public static Offer? Newer(string? releasesJson, int? currentIcd)
        {
            if (currentIcd is not int mine || string.IsNullOrWhiteSpace(releasesJson)) return null;
            JsonArray? list;
            try { list = JsonNode.Parse(releasesJson) as JsonArray; } catch (JsonException) { return null; }
            if (list is null) return null;
            Offer? best = null;
            foreach (var n in list)
            {
                if (n is not JsonObject r) continue;
                if (Bool(r, "draft")) continue;
                string tag = Str(r, "tag_name");
                if (Icd(tag) is not int icd || icd <= mine || (best is not null && icd <= best.Icd)) continue;
                string page = Str(r, "html_url");
                if (!page.StartsWith(PagePrefix, StringComparison.Ordinal)) continue;
                if (r["assets"] is not JsonArray assets) continue;
                string body = Str(r, "body");
                foreach (var an in assets)
                {
                    if (an is not JsonObject a) continue;
                    string name = Str(a, "name"), url = Str(a, "browser_download_url");
                    if (!SetupRx.IsMatch(name) || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) continue;
                    string? sha = DigestRx.Match(Str(a, "digest")) is { Success: true } dm ? dm.Groups[1].Value
                                : NotesHashRx.Match(body) is { Success: true } nm ? nm.Groups[1].Value : null;
                    if (sha is null) continue;   // never offered without a hash to check it against
                    long size = a["size"] is JsonValue sv && sv.TryGetValue(out long sz) ? sz : 0;
                    best = new Offer(icd, tag, Str(r, "name"), page, name, url, sha, size);
                    break;
                }
            }
            return best;
        }

        /// <summary>A downloaded file matches the offer: its SHA-256 (lowercase hex) and, when the release said, its size.</summary>
        public static bool Matches(Offer o, string sha256Hex, long size) =>
            string.Equals(o.Sha256, sha256Hex, StringComparison.OrdinalIgnoreCase) && (o.Size <= 0 || o.Size == size);

        /// <summary>The installer's command line once the launcher has gone: waits for this launcher, then runs Setup (its own wizard).</summary>
        public static string RunAfterExitCommand(int launcherPid, string setupPath) =>
            "-NoProfile -WindowStyle Hidden -Command \"Wait-Process -Id " + launcherPid.ToString(CultureInfo.InvariantCulture) +
            " -ErrorAction SilentlyContinue; Start-Process -FilePath '" + setupPath.Replace("'", "''") + "' -ArgumentList '/SP-'\"";

        private static string Str(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
        private static bool Bool(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue(out bool b) && b;
    }
}
