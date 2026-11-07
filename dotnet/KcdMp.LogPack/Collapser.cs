// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;

namespace KcdMp.LogPack;

/// <summary>What one collapse pass did: every input line is either written as it was or counted in <see cref="Elided"/>.</summary>
public sealed class CollapseStats
{
    public long LinesIn;        // lines read
    public long LinesKept;      // lines written exactly as they were
    public long Elided;         // lines replaced by a summary line (the middle of a run; its first and last stay)
    public long Summaries;      // summary lines written (not original lines)
    public long BytesIn, BytesOut;

    public void Add(CollapseStats o)
    {
        LinesIn += o.LinesIn; LinesKept += o.LinesKept; Elided += o.Elided; Summaries += o.Summaries; BytesIn += o.BytesIn; BytesOut += o.BytesOut;
    }

    /// <summary>The books balance: nothing was read that was neither written nor counted.</summary>
    public bool Balanced => LinesIn == LinesKept + Elided;
}

/// <summary>
/// WO-171 L: collapses runs of repeated lines. A run is either a stretch of known-noise lines (<see cref="NoiseRules"/>; several
/// classes may alternate inside it) or a stretch of identical lines. Each class in a run keeps its first and last line in full and
/// one summary line says how many lines lay between and when the run began and ended. A line that is protected (a mark, a fault, a
/// stats / summary line) or lies within five minutes of a <c>mark_odd</c> is written in full and ends the run before it.
/// Text is read and written as Latin-1, so every byte of a kept line comes out as it went in.
/// </summary>
public static class Collapser
{
    public const string SummaryPrefix = "[kct-pack] ";

    private sealed class Entry
    {
        public string Key = "";
        public string Label = "";
        public long Count;
        public string First = "", Last = "";
        public List<string> Head = new();   // the first lines of the run, kept while it is still short enough to be written out whole
    }

    /// <summary>A run of at most this many lines is written out whole: a summary line would not be shorter.</summary>
    public const int MinCollapse = 7;

    public static CollapseStats Collapse(TextReader input, TextWriter output, OddWindows? windows = null)
    {
        var st = new CollapseStats();
        var clock = new LineClock();
        var run = new List<Entry>();
        bool runIsSame = false;      // true: a run of identical ordinary lines (one entry); false: a stretch of noise classes

        void Emit(string line) { output.Write(line); output.Write('\n'); st.BytesOut += line.Length + 1; }

        void Flush()
        {
            foreach (var e in run)
            {
                if (e.Count < MinCollapse)
                {
                    foreach (var l in e.Head) Emit(l);
                    st.LinesKept += e.Head.Count;
                    continue;
                }
                long mid = e.Count - 2;
                Emit(e.First);
                st.Elided += mid;
                st.Summaries++;
                Emit($"{SummaryPrefix}{mid} more {e.Label} between {LineClock.Stamp(e.First)} and {LineClock.Stamp(e.Last)}");
                Emit(e.Last);
                st.LinesKept += 2;
            }
            run.Clear();
        }

        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            st.LinesIn++;
            st.BytesIn += line.Length + 1;
            double t = clock.Advance(line);
            string body = NoiseRules.Body(line);

            if (NoiseRules.IsProtected(line) || (windows is not null && windows.Inside(t)))
            {
                Flush();
                Emit(line); st.LinesKept++;
                continue;
            }

            var cls = NoiseRules.Classify(body);
            if (cls is not null)
            {
                if (run.Count > 0 && runIsSame) Flush();
                runIsSame = false;
                Entry? e = null;
                foreach (var x in run) if (x.Key == cls.Name) { e = x; break; }
                if (e is null) { e = new Entry { Key = cls.Name, Label = cls.Name, First = line }; run.Add(e); }
                e.Count++; e.Last = line; if (e.Count < MinCollapse) e.Head.Add(line);
                continue;
            }

            // an ordinary line: only a repeat of the line right before it joins a run
            string key = NoiseRules.RepeatKey(body);
            if (run.Count == 1 && runIsSame && run[0].Key == key)
            {
                run[0].Count++; run[0].Last = line; if (run[0].Count < MinCollapse) run[0].Head.Add(line);
                continue;
            }
            Flush();
            runIsSame = true;
            run.Add(new Entry { Key = key, Label = "identical lines", First = line, Last = line, Count = 1, Head = { line } });
        }
        Flush();
        output.Flush();
        return st;
    }

    /// <summary>Collapses a file's bytes; Latin-1 both ways so kept lines keep their bytes.</summary>
    public static (byte[] Data, CollapseStats Stats) CollapseBytes(byte[] raw, OddWindows? windows = null)
    {
        using var ms = new MemoryStream(raw.Length / 4 + 1024);
        CollapseStats st;
        using (var r = new StreamReader(new MemoryStream(raw, false), Encoding.Latin1, false, 1 << 16))
        using (var w = new StreamWriter(ms, Encoding.Latin1, 1 << 16, leaveOpen: true))
            st = Collapse(r, w, windows);
        return (ms.ToArray(), st);
    }
}
