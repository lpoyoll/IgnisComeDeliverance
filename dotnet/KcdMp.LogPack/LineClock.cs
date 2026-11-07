// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-171 L: a line's time of day, in seconds, wherever the log has one. The agent's and the native logs carry a wall clock; the
/// game's own kcd.log has none, but every Lua line carries <c>t=&lt;seconds since the game started&gt;</c> and the file's header
/// says when it started (the same one-constant-offset-per-launch map the WO-172 findings used). Lines with no time inherit the
/// last one seen above them.
/// </summary>
public sealed class LineClock
{
    private static readonly Regex Hms = new(@"^\[?(\d\d):(\d\d):(\d\d)(?:\.(\d+))?\]?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DateHms = new(@"^\d{4}-\d\d-\d\d (\d\d):(\d\d):(\d\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GameT = new(@"\bt=(\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Started = new(@"^Log Started at \d{4}-\d\d-\d\d\s+(\d\d):(\d\d):(\d\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private double _startSec = double.NaN;   // kcd.log: time of day the game started
    private double _last = double.NaN;

    /// <summary>The line's time of day (seconds since midnight), or the last known one; NaN before any.</summary>
    public double Advance(string line)
    {
        var m = Hms.Match(line);
        if (m.Success) return _last = Secs(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
        m = DateHms.Match(line);
        if (m.Success) return _last = Secs(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, "");
        m = Started.Match(line);
        if (m.Success) { _startSec = Secs(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, ""); return _last = _startSec; }
        if (!double.IsNaN(_startSec))
        {
            var g = GameT.Match(line);
            if (g.Success && double.TryParse(g.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                return _last = (_startSec + t) % 86400.0;
        }
        return _last;
    }

    private static double Secs(string h, string m, string s, string frac)
    {
        double f = frac.Length == 0 ? 0 : double.Parse("0." + frac, CultureInfo.InvariantCulture);
        return int.Parse(h, CultureInfo.InvariantCulture) * 3600 + int.Parse(m, CultureInfo.InvariantCulture) * 60 + int.Parse(s, CultureInfo.InvariantCulture) + f;
    }

    /// <summary>The time text a collapse summary shows for a line: its clock, else its game time, else "-".</summary>
    public static string Stamp(string line)
    {
        var m = Hms.Match(line);
        if (m.Success) return m.Value.Trim('[', ']');
        var g = GameT.Match(line);
        return g.Success ? "t=" + g.Groups[1].Value : "-";
    }
}

/// <summary>The ±5 minute windows round every <c>mark_odd</c> ("Something's wrong here"): every line inside is kept in full.</summary>
public sealed class OddWindows
{
    public const double HalfWidthSeconds = 300.0;
    private static readonly Regex Odd = new(@"MP-MARK odd\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly List<double> _centres = new();

    public int Count => _centres.Count;

    /// <summary>Reads one text (a whole log) and notes the time of every <c>MP-MARK odd</c> line.</summary>
    public void Scan(IEnumerable<string> lines)
    {
        var clock = new LineClock();
        foreach (var l in lines)
        {
            double t = clock.Advance(l);
            if (!double.IsNaN(t) && Odd.IsMatch(l)) _centres.Add(t);
        }
    }

    /// <summary>Adds a mark at a known time of day (tests; the packer when a mark line has no clock of its own).</summary>
    public void Add(double secOfDay) => _centres.Add(secOfDay);

    /// <summary>True when this time of day is within five minutes of a mark (the day wraps).</summary>
    public bool Inside(double secOfDay)
    {
        if (double.IsNaN(secOfDay)) return false;
        foreach (var c in _centres)
        {
            double d = Math.Abs(secOfDay - c);
            if (d > 43200) d = 86400 - d;
            if (d <= HalfWidthSeconds) return true;
        }
        return false;
    }
}
