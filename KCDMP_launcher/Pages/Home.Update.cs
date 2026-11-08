// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT QoL (IgnisComeDeliverance): the updater -- a check at start, a banner, and "Update now": download the newer Setup from this
// fork's GitHub releases, check its SHA-256, close the launcher and run the Setup (Models/UpdateRules.cs has the rules).
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KCDMP_launcher.Components.Shared;
using KCDMP_launcher.Models;
using Serilog;

namespace KCDMP_launcher.Pages
{
    public partial class Home
    {
        private UpdateRules.Offer? updateOffer;
        private string updateStatus = "";      // "" | "Downloading 42%" | "Checking the download" | an error in plain words
        private bool updateBusy;
        private bool updateChecked;

        private static HttpClient NewUpdateClient(TimeSpan timeout)
        {
            var c = new HttpClient { Timeout = timeout };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("IgnisComeDeliverance-launcher");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>Once per launcher start (CheckForUpdates on): is there a newer release? Quiet on any failure (offline, rate limit).</summary>
        private async Task CheckForUpdateAsync()
        {
            if (updateChecked || !settings.CheckForUpdates) return;
            updateChecked = true;
            int? mine = UpdateRules.Icd(Globals.BuildId);
            if (mine is null) { Log.Information("MP-UPDATE not checked: this build ({Build}) has no release number", Globals.BuildId); return; }
            try
            {
                using var http = NewUpdateClient(TimeSpan.FromSeconds(15));
                string json = await http.GetStringAsync(UpdateRules.ReleasesApi);
                updateOffer = UpdateRules.Newer(json, mine);
                Log.Information("MP-UPDATE this build icd{Mine}; {Offer}", mine,
                    updateOffer is { } o ? $"newer: {o.Tag} ({o.SetupName}, {o.Size} B, sha256 {o.Sha256[..12]}...)" : "up to date");
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                Log.Information("MP-UPDATE check failed ({Kind}): {Why}", ex.GetType().Name, ex.Message);
            }
        }

        private void OpenUpdatePage()
        {
            if (updateOffer is { } o) UrlLauncher.Open(o.PageUrl);
        }

        /// <summary>"Update now": refuse while playing, download, check, then hand over to the Setup and close.</summary>
        private async Task UpdateNowAsync()
        {
            if (updateOffer is not { } o || updateBusy) return;
            if (GameAlreadyRunning() is not null || (agentProcess != null && !agentProcess.HasExited)
                || (launchStage != LaunchStage.Idle && launchStage != LaunchStage.Failed))
            {
                updateStatus = "Close the game (and finish your session) first, then press Update now.";
                return;
            }
            updateBusy = true;
            string dir = Path.Combine(Path.GetTempPath(), "IgnisComeDeliverance-update");
            string path = Path.Combine(dir, o.SetupName);
            try
            {
                Directory.CreateDirectory(dir);
                updateStatus = "Downloading 0%";
                await InvokeAsync(StateHasChanged);
                using (var http = NewUpdateClient(TimeSpan.FromMinutes(30)))
                using (var resp = await http.GetAsync(o.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? o.Size;
                    await using var src = await resp.Content.ReadAsStreamAsync();
                    await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    var buf = new byte[1 << 16];
                    long got = 0; int lastPct = -1, n;
                    while ((n = await src.ReadAsync(buf)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n));
                        got += n;
                        int pct = total > 0 ? (int)(got * 100 / total) : 0;
                        if (pct != lastPct) { lastPct = pct; updateStatus = $"Downloading {pct}%"; await InvokeAsync(StateHasChanged); }
                    }
                }
                updateStatus = "Checking the download";
                await InvokeAsync(StateHasChanged);
                string sha;
                long size;
                await using (var f = File.OpenRead(path))
                {
                    size = f.Length;
                    sha = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
                }
                if (!UpdateRules.Matches(o, sha, size))
                {
                    try { File.Delete(path); } catch { }
                    Log.Warning("MP-UPDATE download REFUSED: sha256 {Got} size {Size}, the release says {Want} size {WantSize}", sha, size, o.Sha256, o.Size);
                    updateStatus = "The download did not match the release's checksum, so it was deleted and nothing was installed. Try again later.";
                    return;
                }
                Log.Information("MP-UPDATE {Tag} downloaded and verified (sha256 {Sha}); the launcher closes and its Setup runs", o.Tag, sha);
                using (var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = UpdateRules.RunAfterExitCommand(Environment.ProcessId, path),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })) { }
                ConfirmExit();   // the Setup starts once this launcher has gone (it replaces this launcher's files)
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException
                                          or System.ComponentModel.Win32Exception)
            {
                Log.Warning("MP-UPDATE failed ({Kind}): {Why}", ex.GetType().Name, ex.Message);
                updateStatus = "The update could not be downloaded (" + ex.Message + "). You can get it from the release page instead.";
            }
            finally
            {
                updateBusy = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }
}
