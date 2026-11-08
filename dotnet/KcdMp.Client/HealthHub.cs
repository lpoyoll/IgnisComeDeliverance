// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT diag (icd14): one place that knows the newest "MP-HEALTH area=<x> ..." line of every part of the mod -- the agent's own
// (seen as it prints), the mod's Lua (kcd.log, seen by the log tail) and the native DLL (kcdmp-native.log, read every 10 s) --
// so each agent can send its partner a summary and both machines' health ends up in one log (MP-HEALTH-PEER lines).
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

public static class HealthHub
{
    public const string Marker = "MP-HEALTH area=";
    public const int PeerTextMax = 1400;          // KcdMp.Wire.Protocol.W203TextMax
    public const long PeerFreshMs = 60_000;       // an area silent this long is not sent

    public sealed record Entry(string Source, string Area, string Body, long AtMs);

    private static readonly ConcurrentDictionary<(string Source, string Area), Entry> Latest = new();
    public static Func<long> Clock = () => Environment.TickCount64;

    /// <summary>Any log line; cheap when it is not a health line (one IndexOf).</summary>
    public static void Observe(string source, string? line)
    {
        if (line is null) return;
        int i = line.IndexOf(Marker, StringComparison.Ordinal);
        if (i < 0) return;
        if (!TryParse(line.AsSpan(i), out string area, out string body)) return;
        Latest[(source, area)] = new Entry(source, area, body, Clock());
    }

    /// <summary>"MP-HEALTH area=npc a=1 b=2 t=12.3" -> ("npc", "a=1 b=2"); the Lua line's trailing " t=&lt;clock&gt;" is dropped.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out string area, out string body)
    {
        area = body = "";
        if (!s.StartsWith(Marker, StringComparison.Ordinal)) return false;
        s = s[Marker.Length..];
        int sp = s.IndexOf(' ');
        var a = sp < 0 ? s : s[..sp];
        if (a.Length == 0 || a.Length > 32) return false;
        foreach (char c in a) if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;
        area = a.ToString();
        string rest = sp < 0 ? "" : s[(sp + 1)..].ToString().Trim();
        int t = rest.LastIndexOf(" t=", StringComparison.Ordinal);
        if (t >= 0 && double.TryParse(rest.AsSpan(t + 3), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) rest = rest[..t];
        body = rest;
        return true;
    }

    public static IReadOnlyList<Entry> Fresh(long nowMs, long freshMs = PeerFreshMs) =>
        Latest.Values.Where(e => nowMs - e.AtMs <= freshMs).OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Area, StringComparer.Ordinal).ToList();

    public static void Clear() => Latest.Clear();

    /// <summary>The partner's message for one area: "v1|src|area|age_s|body", printable ASCII, at most <see cref="PeerTextMax"/>.</summary>
    public static string PeerText(Entry e, long nowMs)
    {
        long age = Math.Max(0, (nowMs - e.AtMs) / 1000);
        string head = $"v1|{e.Source}|{e.Area}|{age.ToString(CultureInfo.InvariantCulture)}|";
        var sb = new StringBuilder(head.Length + e.Body.Length);
        sb.Append(head);
        foreach (char c in e.Body) sb.Append(c is >= ' ' and <= '~' && c != '|' ? c : '_');
        return sb.Length <= PeerTextMax ? sb.ToString() : sb.ToString(0, PeerTextMax);
    }

    public static bool TryParsePeer(string? text, out string source, out string area, out long ageS, out string body)
    {
        source = area = body = ""; ageS = 0;
        var p = (text ?? "").Split('|', 5);
        if (p.Length != 5 || p[0] != "v1" || p[1].Length is 0 or > 16 || p[2].Length is 0 or > 32) return false;
        if (!long.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out ageS)) return false;
        source = p[1]; area = p[2]; body = p[4];
        return true;
    }
}

/// <summary>KCT diag (icd14): reads the native DLL's log from where it left off and hands its health lines to <see cref="HealthHub"/>.</summary>
public sealed class HealthLogFollower(string path, string source)
{
    private long _offset = -1;
    private readonly StringBuilder _partial = new();

    /// <summary>Reads what was added since the last call (the first call starts at the end: only new lines count).</summary>
    public int Poll(int maxBytes = 4 << 20)
    {
        int seen = 0;
        try
        {
            if (!File.Exists(path)) return 0;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            if (_offset < 0) { _offset = len; return 0; }
            if (len < _offset) { _offset = 0; _partial.Clear(); }   // the DLL started a new log
            if (len - _offset > maxBytes) _offset = len - maxBytes;   // far behind: only the newest part matters
            fs.Seek(_offset, SeekOrigin.Begin);
            var buf = new byte[len - _offset];
            int n = 0;
            while (n < buf.Length) { int r = fs.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
            _offset += n;
            string text = Encoding.UTF8.GetString(buf, 0, n);
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                _partial.Append(text, start, i - start);
                string line = _partial.ToString().TrimEnd('\r');
                _partial.Clear();
                start = i + 1;
                if (line.Contains(HealthHub.Marker, StringComparison.Ordinal)) { HealthHub.Observe(source, line); seen++; }
            }
            _partial.Append(text, start, text.Length - start);
            if (_partial.Length > 64 * 1024) _partial.Clear();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return seen;
    }
}
