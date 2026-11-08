// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;
using KcdMp.Steam;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>KCT fix (icd14) item 3: the link's lanes -- coalescable events, event ages, the Steam inbox bound, per-body order.</summary>
public class Icd14LinkLaneTests
{
    private static byte[] F(int type, byte[] p)
    {
        var f = new byte[3 + p.Length];
        f[0] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(1), (ushort)p.Length);
        p.CopyTo(f, 3);
        return f;
    }

    /// <summary>An activity frame (down: [src][target][joinId:4][kind][count] rows) of bodies (name, stance).</summary>
    private static byte[] Activity(params (string Name, byte Stance)[] rows)
    {
        var p = new List<byte> { 2, 1, 0, 0, 0, 7, Protocol.ActivityKindNpc, (byte)rows.Length };
        foreach (var (name, stance) in rows)
        {
            p.Add(0xFF); p.Add((byte)name.Length); p.AddRange(Encoding.ASCII.GetBytes(name));
            var a = new byte[Protocol.ActivityBytes]; a[0] = stance; p.AddRange(a);
        }
        return F(Protocol.ActivityHostDown, [.. p]);
    }

    private static byte[] Conv(byte kind, string text) =>
        F(Protocol.ConvDown, [2, 1, 0, 0, 0, 7, kind, 0, 0, 0, 1, .. Encoding.ASCII.GetBytes(text)]);

    private static byte[] Story(int i) => F(Protocol.StoryBeatDown, [(byte)i, 0, 0, 0]);

    private static List<byte[]> Drain(LinkScheduler s)
    {
        var got = new List<byte[]>();
        while (s.TryTake(int.MaxValue, -1, out var f, out _)) got.Add(f);
        return got;
    }

    // ---------------------------------------------------------------- the rules

    [Fact]
    public void An_activity_frame_splits_by_body_and_rebuilds_without_one()
    {
        Assert.True(LinkEventRules.TrySplit(Activity(("a", 1), ("b", 2)), out var p));
        Assert.Equal(2, p!.Items.Count);
        Assert.Equal("activity", p.Kind);
        p.Items.RemoveAt(0);
        Assert.Equal(Activity(("b", 2)), p.Build());
        Assert.False(LinkEventRules.TrySplit(Activity(("a", 1), ("a", 2)), out _));   // one body twice: as it is
        Assert.False(LinkEventRules.TrySplit(Story(1), out _));
    }

    [Fact]
    public void A_camera_sample_is_keyed_by_its_conversation_and_a_cut_is_protected()
    {
        Assert.True(LinkEventRules.TrySplit(Conv(Protocol.ConvCam, "v1|42|1~2~3~4~5~0"), out var p));
        Assert.Equal("conv-cam", p!.Kind);
        Assert.False(p.Protected);
        Assert.True(LinkEventRules.TrySplit(Conv(Protocol.ConvCam, "v1|42|1~2~3~4~5~0;1~2~3~4~5~1"), out var cut));
        Assert.True(cut!.Protected);
        Assert.Equal(p.Items[0].Key, cut.Items[0].Key);
        Assert.False(LinkEventRules.TrySplit(Conv(Protocol.ConvLine, "v1|42|hello"), out _));   // a line is never merged
    }

    [Fact]
    public void Only_worthless_when_old_kinds_have_an_age()
    {
        Assert.Equal(2000, LinkEventRules.MaxAgeMs(Conv(Protocol.ConvCam, "v1|1|x")));
        Assert.Equal(0, LinkEventRules.MaxAgeMs(Conv(Protocol.ConvLine, "v1|1|x")));
        Assert.Equal(0, LinkEventRules.MaxAgeMs(Conv(Protocol.ConvVote, "v1|1|x")));
        Assert.Equal(1000, LinkEventRules.MaxAgeMs(F(Protocol.VoiceDown, [1, 2, 3])));
        Assert.Equal(0, LinkEventRules.MaxAgeMs(Story(1)));
        Assert.Equal(0, LinkEventRules.MaxAgeMs(Activity(("a", 1))));   // merged, never dropped
    }

    // ---------------------------------------------------------------- the scheduler

    [Fact]
    public void A_waiting_activity_row_is_replaced_by_its_body_s_newer_one_and_the_rest_keep_their_order()
    {
        var s = new LinkScheduler();
        s.Enqueue(Activity(("a", 1), ("b", 1)));
        s.Enqueue(Story(1));
        s.Enqueue(Activity(("a", 3)));
        var got = Drain(s);
        Assert.Equal(3, got.Count);
        Assert.Equal(Activity(("b", 1)), got[0]);    // a's old row taken out of the first frame
        Assert.Equal(Story(1), got[1]);
        Assert.Equal(Activity(("a", 3)), got[2]);
        Assert.Equal(1, s.EventsCoalesced);
        Assert.Contains("state_coalesced=1 ", s.HealthText());

        s.Enqueue(Activity(("a", 1)));
        s.Enqueue(Activity(("a", 2)));               // the whole first frame goes
        Assert.Equal([Activity(("a", 2))], Drain(s));
    }

    [Fact]
    public void Off_every_activity_frame_goes_as_it_came()
    {
        var s = new LinkScheduler(new LinkSchedOptions { CoalesceEvents = false });
        s.Enqueue(Activity(("a", 1))); s.Enqueue(Activity(("a", 2)));
        Assert.Equal(2, Drain(s).Count);
    }

    [Fact]
    public void A_camera_cut_is_never_replaced_but_a_plain_sample_is()
    {
        var s = new LinkScheduler();
        s.Enqueue(Conv(Protocol.ConvCam, "v1|42|1~2~3~4~5~1"));
        s.Enqueue(Conv(Protocol.ConvCam, "v1|42|1~2~3~4~5~0"));
        s.Enqueue(Conv(Protocol.ConvCam, "v1|42|1~2~3~4~6~0"));
        var got = Drain(s);
        Assert.Equal(2, got.Count);                  // the cut and the newest sample
        Assert.Equal(Conv(Protocol.ConvCam, "v1|42|1~2~3~4~5~1"), got[0]);
    }

    [Fact]
    public void An_old_camera_sample_is_dropped_at_send_time_a_line_never()
    {
        var s = new LinkScheduler(new LinkSchedOptions { CoalesceEvents = false });
        s.Enqueue(Conv(Protocol.ConvCam, "v1|42|1~2~3~4~5~0"));
        s.Enqueue(Conv(Protocol.ConvLine, "v1|42|hello"));
        Thread.Sleep(2100);
        var got = Drain(s);
        Assert.Equal([Conv(Protocol.ConvLine, "v1|42|hello")], got);
        Assert.Equal(1, s.EventsExpired);
        Assert.Contains("events_expired=1 ", s.HealthText());
    }

    // ---------------------------------------------------------------- the Steam inbox

    [Fact]
    public async Task The_inbox_drops_the_oldest_state_never_an_event()
    {
        var box = new SteamInbox { StateCap = 2 };
        box.TryWrite([1], state: false);
        box.TryWrite([2], state: true);
        box.TryWrite([3], state: true);
        box.TryWrite([4], state: false);
        box.TryWrite([5], state: true);
        Assert.Equal(1, box.StateDropped);
        var got = new List<byte>();
        while (box.TryRead(out var d)) got.Add(d[0]);
        Assert.Equal(new byte[] { 1, 3, 4, 5 }, got);
        Assert.Equal(4, box.MaxDepth);

        var unbounded = new SteamInbox { StateCap = 0 };
        for (int i = 0; i < 50; i++) unbounded.TryWrite([(byte)i], true);
        Assert.Equal(50, unbounded.Count);
        unbounded.Complete();
        Assert.Equal(0, (await unbounded.ReadAsync(CancellationToken.None))[0]);
    }

    // ---------------------------------------------------------------- per-body order at the reader

    private static byte[] NpcPayload(string name, ushort seq, byte flags = 0)
    {
        var p = new byte[2 + name.Length + Protocol.NpcStateFixedTail];
        p[0] = 2; p[1] = (byte)name.Length;
        Encoding.ASCII.GetBytes(name).CopyTo(p, 2);
        int o = 2 + name.Length;
        p[o + Protocol.NpcStateFlagsOffset] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + Protocol.NpcStateSeqOffset), seq);
        return p;
    }

    private static byte[] GhostPayload(byte id, uint senderMs)
    {
        var p = new byte[Protocol.GhostPayloadLen + Protocol.SenderMsLen];
        p[0] = id; p[17] = Protocol.PositionFlagSenderMs;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(Protocol.GhostPayloadLen), senderMs);
        return p;
    }

    [Fact]
    public void An_older_sample_of_a_body_after_a_newer_one_is_dropped()
    {
        var g = new StateOrderGuard();
        Assert.False(g.Drop(Protocol.NpcStateDown, NpcPayload("a", 10), 0));
        Assert.True(g.Drop(Protocol.NpcStateDown, NpcPayload("a", 9), 1));       // the other lane overtook it
        Assert.False(g.Drop(Protocol.NpcStateDown, NpcPayload("a", 10), 2));     // a repeat goes on
        Assert.False(g.Drop(Protocol.NpcStateDown, NpcPayload("b", 3), 3));      // another body
        Assert.False(g.Drop(Protocol.NpcStateDown, NpcPayload("a", 9, LinkStateCodec.TransientNpcFlags), 4));   // said once: never dropped
        Assert.False(g.Drop(Protocol.NpcStateDown, NpcPayload("a", 2000), 5));   // far off: a restarted sender
        Assert.False(g.Drop(Protocol.NpcStateDown, NpcPayload("a", 1990), 6_000)); // silent 5 s: starts over
        Assert.False(g.Drop(Protocol.NpcStateDown, NpcPayload("a", 0), 6_001));   // far behind: a restarted sender, taken
        Assert.Equal(1, g.OlderDropped);

        Assert.False(g.Drop(Protocol.Ghost, GhostPayload(1, 5000), 0));
        Assert.True(g.Drop(Protocol.Ghost, GhostPayload(1, 4900), 1));
        Assert.False(g.Drop(Protocol.Ghost, GhostPayload(1, 5100), 2));
        Assert.False(g.Drop(Protocol.Ghost, new byte[Protocol.GhostPayloadLen], 3));   // no sender stamp: not judged
        Assert.Equal(2, g.OlderDropped);
    }
}
