// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Text;

namespace KcdMp.Client.Tests;

/// <summary>KCT fix (icd14) item 2: the log tail -- where an open starts, the spam skip, the event seq, the events' own task.</summary>
public class Icd14LogTailTests
{
    private static readonly LogFileId A = new(1, 10, 100), B = new(1, 11, 100);

    [Fact]
    public void The_first_open_starts_at_the_end_the_same_file_resumes_another_starts_at_its_beginning()
    {
        Assert.Equal((TailStart.End, 500L), LogTailRules.OpenAt(true, firstOpen: true, null, 0, A, 500));
        Assert.Equal((TailStart.Resume, 300L), LogTailRules.OpenAt(true, false, A, 300, A, 500));
        Assert.Equal((TailStart.Beginning, 0L), LogTailRules.OpenAt(true, false, A, 300, B, 500));          // replaced
        Assert.Equal((TailStart.Beginning, 0L), LogTailRules.OpenAt(true, false, A, 300, A, 200));          // shorter: written again
        Assert.Equal((TailStart.Beginning, 0L), LogTailRules.OpenAt(true, false, A, long.MaxValue, A, 500)); // known to be new
        Assert.Equal((TailStart.End, 500L), LogTailRules.OpenAt(false, false, A, 300, A, 500));             // off: the end, always
    }

    [Fact]
    public void A_replacement_is_another_known_identity_at_the_path()
    {
        Assert.True(LogTailRules.Replaced(A, B));
        Assert.False(LogTailRules.Replaced(A, A));
        Assert.False(LogTailRules.Replaced(A, null));        // not there right now: not judged
        Assert.False(LogTailRules.Replaced(default, B));     // unknown identity: never judged
    }

    [Theory]
    [InlineData("[Warning] Validator: Can't remove anything", true)]
    [InlineData("[Warning] Validator: Can't remove anything [KCD2-MP-EVT] v1 1 x y", false)]   // a marker inside: kept
    [InlineData("[Warning] something else", false)]
    [InlineData("[KCD2-MP] hello", false)]
    [InlineData("CutscenePlayer::Play x", false)]
    public void Only_engine_spam_without_a_marker_is_skipped(string line, bool skip) =>
        Assert.Equal(skip, LogLineFilter.Skip(Encoding.UTF8.GetBytes(line)));

    [Fact]
    public void Mod_lines_are_told_by_their_prefix()
    {
        Assert.True(LogLineFilter.IsModLine("[KCD2-MP-DATA] v2"u8));
        Assert.True(LogLineFilter.IsModPlainLine("[KCD2-MP] MOD INIT"u8));
        Assert.False(LogLineFilter.IsModPlainLine("[KCD2-MP-EVT] v1 1 a b"u8));
        Assert.False(LogLineFilter.IsModLine("x [KCD2-MP] y"u8));
    }

    [Fact]
    public void A_seq_jump_is_a_gap_a_step_back_is_a_restart_and_a_new_file_starts_the_count_over()
    {
        var t = new EventSeqTracker();
        Assert.Null(t.Next(5));
        Assert.Null(t.Next(6));
        Assert.Equal((7L, 9L), t.Next(10));
        Assert.Null(t.Next(1));       // the mod's Lua reloaded
        Assert.Null(t.Next(2));
        t.Reset();
        Assert.Null(t.Next(40));      // a new file: no gap against the old one's
        Assert.Equal(1, t.Gaps);
        Assert.Equal(3, t.Missing);
        Assert.Equal(1, t.Restarts);
    }

    [Fact]
    public void The_health_line_has_every_key()
    {
        var h = new EvtHealth();
        h.Scanned(1000); h.LinesRead = 3; h.ModLines = 1;
        EvtHealth.Max(ref h.HandlerQueueMax, 4); EvtHealth.Max(ref h.HandlerQueueMax, 2);
        string line = h.TakeLine(new EventSeqTracker());
        foreach (var k in new[] { "MP-HEALTH area=evt ", "lines_read=3", "mod_lines=1", "bytes_per_s=", "reopen_resumes=0", "new_file_starts=0",
                                  "seq_gaps=0", "events_missing=0", "handler_queue_max=4", "handler_ms_max=0" })
            Assert.Contains(k, line);
        Assert.Contains("handler_queue_max=0", h.TakeLine(new EventSeqTracker()));   // the maxima start over
    }

    // ---------------------------------------------------------------- the loop itself, on a file of the test's own

    private static void Append(string path, string text)
    {
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var b = Encoding.UTF8.GetBytes(text);
        fs.Write(b);
    }

    private static async Task Until(Func<bool> cond, int ms = 5000)
    {
        var until = Environment.TickCount64 + ms;
        while (!cond() && Environment.TickCount64 < until) await Task.Delay(10);
        Assert.True(cond());
    }

    [Fact]
    public async Task The_tail_reads_events_in_order_skips_spam_hands_mod_lines_to_the_hook_and_reads_a_replacement_from_its_start()
    {
        string dir = Path.Combine(Path.GetTempPath(), "icd14-tail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string log = Path.Combine(dir, "kcd.log");
        File.WriteAllText(log, "[KCD2-MP-EVT] v1 1 before_start x\r\n");   // the first open starts at the end: never read
        await using var tail = new LogTailGameTransport(new HttpGameTransport("http://127.0.0.1:1"), log);
        var events = new ConcurrentQueue<string>();
        var plain = new ConcurrentQueue<string>();
        tail.GameEvent += (n, a) => { Thread.Sleep(5); events.Enqueue(n + ":" + a); };   // a slow handler: the tail never waits for it
        tail.ModLogLine += plain.Enqueue;
        tail.StartTail();
        await Task.Delay(300);

        Append(log, "[Warning] Validator: Can't remove anything\r\n[KCD2-MP] hello there\r\n[KCD2-MP-EVT] v1 2 one a\r\n" +
                    "[Warning] Validator: Can't remove anything\r\n[KCD2-MP-EVT] v1 5 two b\r\n");
        await Until(() => events.Count == 2);
        Assert.Equal(new[] { "one:a", "two:b" }, events);
        Assert.Equal(new[] { "[KCD2-MP] hello there" }, plain);
        Assert.Equal(2, tail.Health.LinesSkipped);
        Assert.Equal(1, tail.EventSeq.Gaps);
        Assert.Equal(2, tail.EventSeq.Missing);   // 3 and 4

        // the game restarts: the old log renamed away, a new one written (never shorter than where the tail was)
        File.Move(log, Path.Combine(dir, "kcd-old.log"));
        File.WriteAllText(log, new string('#', 400) + "\r\n[KCD2-MP-EVT] v1 1 fresh c\r\n");
        await Until(() => events.Count == 3);
        Assert.Equal("fresh:c", events.Last());
        Assert.True(tail.Health.Replaced + tail.Health.NewFileStarts > 0);
        Assert.Equal(1, tail.EventSeq.Gaps);      // seq 1 in a new file: no gap
        try { Directory.Delete(dir, true); } catch { }
    }
}
