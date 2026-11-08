// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT diag (icd14): the agent's own health line every 10 s, the native DLL's health lines collected, and every area's newest line
// sent to the partner every 30 s (W203 "health"), so one machine's agent.log holds both machines' health (MP-HEALTH-PEER).
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

public partial class GameBridge
{
    private HealthLogFollower? _healthNative;
    private long _healthTicks, _healthLinesAt, _healthPeerIn, _healthPeerOut;

    /// <summary>Once a second from the heartbeat loop (GameBridge.Wo204.cs).</summary>
    private void IcdHealthTick()
    {
        long n = ++_healthTicks;
        if (n % 10 != 0) return;
        _healthNative ??= new HealthLogFollower(Path.Combine(AppContext.BaseDirectory, "kcdmp-native.log"), "native");
        _healthNative.Poll();

        long lines = Interlocked.Read(ref TeeTextWriter.LinesWritten);
        long linesPerS = (lines - _healthLinesAt) / 10;
        _healthLinesAt = lines;
        string role = W137Host ? "host" : W137JoinerSession ? "joiner" : "solo";
        Console.WriteLine(FormattableString.Invariant(
            $"MP-HEALTH area=agent role={role} build={ReleaseVersionInfo.BuildIdValue} partners={LivePartners().Count} queue_lag_ms={Wo202QueueLagMs()} log_lines_per_s={linesPerS} mem_mb={GC.GetTotalMemory(false) >> 20} threads={System.Threading.ThreadPool.ThreadCount} peer_in={Interlocked.Read(ref _healthPeerIn)} peer_out={Interlocked.Read(ref _healthPeerOut)}"));

        if (n % 30 != 0) return;
        var to = W137Host ? LivePartners() : (W137JoinerSession && _joinedWorld ? new List<byte> { Protocol.JoinTargetHost } : new List<byte>());
        if (to.Count == 0) return;
        long now = HealthHub.Clock();
        foreach (var e in HealthHub.Fresh(now))
        {
            string text = HealthHub.PeerText(e, now);
            foreach (byte t in to) { _ = Wo203SendSideAsync(t, Protocol.W203Health, text); Interlocked.Increment(ref _healthPeerOut); }
        }
    }

    /// <summary>The partner's health: one line per area, beside this machine's own.</summary>
    private void IcdHealthOnPeer(byte src, LootMsg m)
    {
        if (!HealthHub.TryParsePeer(m.Text, out string source, out string area, out long ageS, out string body)) return;
        Interlocked.Increment(ref _healthPeerIn);
        string role = W137Host ? "joiner" : "host";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"MP-HEALTH-PEER from={role}:{src} src={source} area={area} age_s={ageS} {body}"));
    }
}
