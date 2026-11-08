// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

// KCT fix (icd14): the counters behind the MP-HEALTH lines (every 10 s in the agent log; a dashboard reads them). Counters are totals
// since the agent started (a reader takes differences); the *_ms quantiles, *_max and bytes_per_s are of the last window only.

/// <summary>A small window of millisecond samples (quantiles on take); thread-safe.</summary>
public sealed class MsWindow
{
    private readonly object _lock = new();
    private readonly List<double> _v = new(256);
    private readonly int _cap;
    public MsWindow(int cap = 4096) => _cap = cap;

    public void Add(double ms) { lock (_lock) if (_v.Count < _cap) _v.Add(ms); }

    /// <summary>(p50, p95) of the window, which starts over; (0, 0) when empty.</summary>
    public (double P50, double P95) Take()
    {
        List<double> v;
        lock (_lock) { v = new List<double>(_v); _v.Clear(); }
        if (v.Count == 0) return (0, 0);
        v.Sort();
        double Q(double q) => v[Math.Min(v.Count - 1, (int)Math.Round(q * (v.Count - 1)))];
        return (Q(0.5), Q(0.95));
    }
}

/// <summary>MP-HEALTH area=lua: the Lua command queue behind HttpGameTransport.</summary>
public sealed class LuaHealth
{
    public long SentBatches, BatchTimeouts, RecoveryDrains;
    public readonly MsWindow AnswerMs = new();

    public void Sent(double answerMs) { Interlocked.Increment(ref SentBatches); AnswerMs.Add(answerMs); }

    public static string Line(int queued, long sent, long timeouts, long replaysDropped, long[] expiredByClass, long evicted, long drains, (double P50, double P95) answer) =>
        string.Create(CultureInfo.InvariantCulture,
            $"MP-HEALTH area=lua queued={queued} sent_batches={sent} batch_timeouts={timeouts} replays_dropped_superseded={replaysDropped} " +
            $"edges_expired_by_kind=feedback:{expiredByClass[(int)EdgeClass.Feedback]},normal:{expiredByClass[(int)EdgeClass.Normal]},world:{expiredByClass[(int)EdgeClass.World]} " +
            $"evicted={evicted} recovery_drains={drains} answer_ms_p50={answer.P50:F0} answer_ms_p95={answer.P95:F0}");
}

/// <summary>MP-HEALTH area=evt: the log tail (LogTailGameTransport).</summary>
public sealed class EvtHealth
{
    public long LinesRead, ModLines, LinesSkipped, BytesScanned, ReopenResumes, NewFileStarts, Replaced, HandlerQueueMax, HandlerMsMax;
    private long _windowBytes, _windowStartTicks = Environment.TickCount64;

    public void Scanned(int bytes) { Interlocked.Add(ref BytesScanned, bytes); Interlocked.Add(ref _windowBytes, bytes); }

    public static void Max(ref long field, long v)
    {
        long cur;
        while (v > (cur = Interlocked.Read(ref field)) && Interlocked.CompareExchange(ref field, v, cur) != cur) { }
    }

    /// <summary>The line; the window's bytes/s and the two maxima start over.</summary>
    public string TakeLine(EventSeqTracker seq)
    {
        long now = Environment.TickCount64;
        long start = Interlocked.Exchange(ref _windowStartTicks, now);
        long bytes = Interlocked.Exchange(ref _windowBytes, 0);
        double s = Math.Max(0.001, (now - start) / 1000.0);
        long qmax = Interlocked.Exchange(ref HandlerQueueMax, 0), hmax = Interlocked.Exchange(ref HandlerMsMax, 0);
        return string.Create(CultureInfo.InvariantCulture,
            $"MP-HEALTH area=evt lines_read={Interlocked.Read(ref LinesRead)} mod_lines={Interlocked.Read(ref ModLines)} lines_skipped={Interlocked.Read(ref LinesSkipped)} " +
            $"bytes_per_s={bytes / s:F0} reopen_resumes={Interlocked.Read(ref ReopenResumes)} new_file_starts={Interlocked.Read(ref NewFileStarts)} replaced={Interlocked.Read(ref Replaced)} " +
            $"seq_gaps={seq.Gaps} events_missing={seq.Missing} handler_queue_max={qmax} handler_ms_max={hmax}");
    }
}

/// <summary>
/// KCT fix (icd14): the mod's event sequence ([KCD2-MP-EVT] v1 &lt;seq&gt; ...; KCD2MP_EmitEvent counts from 1 at every script load).
/// A jump forward is a gap (events the tail never saw); a step back or a repeat is a restart (the mod's Lua reloaded), never a gap.
/// Pure; one caller (the tail loop).
/// </summary>
public sealed class EventSeqTracker
{
    private long _last = -1;
    public long Gaps { get; private set; }
    public long Missing { get; private set; }
    public long Restarts { get; private set; }

    /// <summary>One event's seq: (from, to) of the missing run, or null.</summary>
    public (long From, long To)? Next(long seq)
    {
        long last = _last;
        _last = seq;
        if (last < 0) return null;
        if (seq <= last) { Restarts++; return null; }
        if (seq == last + 1) return null;
        Gaps++;
        Missing += seq - last - 1;
        return (last + 1, seq - 1);
    }

    /// <summary>A new file: the next seq starts a fresh count (no gap against the old file's).</summary>
    public void Reset() => _last = -1;
}
