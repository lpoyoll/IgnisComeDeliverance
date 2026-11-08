// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KcdMp.Wire;

/// <summary>KCT fix (icd14): one body's piece of a coalescable event frame (its key, its bytes).</summary>
public readonly record struct EventPiece(string Key, byte[] Item);

/// <summary>KCT fix (icd14): a coalescable event frame split by body (see <see cref="LinkEventRules.TrySplit"/>).</summary>
public sealed class EventPieces
{
    public required string Kind { get; init; }
    public required int Type { get; init; }
    public required byte[] Prefix { get; init; }
    public required bool Batched { get; init; }
    /// <summary>Never removed by a newer frame of its key (a camera cut: the listener's view must jump there, not glide).</summary>
    public bool Protected { get; init; }
    public List<EventPiece> Items { get; } = new();

    /// <summary>The frame with the pieces still here (header included).</summary>
    public byte[] Build() => LinkStateCodec.Build(Type, Prefix, Items.Select(i => i.Item).ToList(), Batched);
}

/// <summary>
/// KCT fix (icd14): the link's events that are really a body's latest state -- they stay reliable and in order (events), but while one
/// still waits in the scheduler's queue a newer one of the same body replaces it (the host's activity rows and extras and a joined
/// conversation's camera samples backed up for minutes). And the events that are worthless once old (a voice frame, a camera sample, the
/// cursor's row, the "ms behind" word): dropped at send time past their age. Every other kind -- joins, loads, deaths, quests, loot,
/// inventory, epochs, conversation lines and votes, the leash -- is never merged and never dropped. Pure (LinkEventRulesTests).
/// </summary>
public static class LinkEventRules
{
    /// <summary>The pieces of a coalescable event frame (header included), or false (then it is an ordinary event).</summary>
    public static bool TrySplit(byte[] frame, out EventPieces? pieces)
    {
        pieces = null;
        if (frame.Length < 3) return false;
        int type = frame[0];
        var p = frame.AsSpan(3);
        bool down = Protocol.JoinWireFor(type) is null;
        int head = (down ? 1 : 0) + Protocol.JoinHeaderLen;   // [src (down)][target][joinId:4]
        switch (type)
        {
            case Protocol.ActivityHostUp or Protocol.ActivityHostDown or Protocol.ActivityPeerUp or Protocol.ActivityPeerDown:
            {
                // [kind][count]{[peer][nameLen][name][activity:30]}
                if (p.Length < head + 2) return false;
                int pre = head + 1, count = p[head + 1];
                if (p[head] is not (Protocol.ActivityKindNpc or Protocol.ActivityKindPlayer) || count < 1) return false;
                var prefix = p[..pre].ToArray();
                string group = type.ToString("X2", CultureInfo.InvariantCulture) + "|" + Convert.ToHexString(prefix);
                var r = new EventPieces { Kind = "activity", Type = type, Prefix = prefix, Batched = true };
                int o = pre + 1;
                for (int i = 0; i < count; i++)
                {
                    if (o + 2 > p.Length) return false;
                    int nl = p[o + 1], len = 2 + nl + Protocol.ActivityBytes;
                    if (o + len > p.Length) return false;
                    string key = group + "|" + p[o].ToString(CultureInfo.InvariantCulture) + "|" + Encoding.ASCII.GetString(p.Slice(o + 2, nl));
                    if (r.Items.Any(x => x.Key == key)) return false;   // one body twice in a frame: not split (it is sent as it is)
                    r.Items.Add(new EventPiece(key, p.Slice(o, len).ToArray()));
                    o += len;
                }
                if (o != p.Length) return false;
                pieces = r;
                return true;
            }
            case Protocol.ActivityExtraUp or Protocol.ActivityExtraDown:
            {
                // [extra kind][count] items -- the same split the state scheduler knows (a one-shot is an event of its own)
                if (p.Length < head + 2) return false;
                byte ek = p[head];
                if (ek is not (Protocol.ExtraKindHands or Protocol.ExtraKindGaits or Protocol.ExtraKindLooks or Protocol.ExtraKindVitals or Protocol.ExtraKindMount))
                    return false;
                int pre = head + 1, count = p[head + 1];
                if (count < 1) return false;
                var prefix = p[..pre].ToArray();
                string group = type.ToString("X2", CultureInfo.InvariantCulture) + "|" + Convert.ToHexString(prefix);
                var r = new EventPieces { Kind = "activity-extra", Type = type, Prefix = prefix, Batched = true };
                int o = pre + 1;
                for (int i = 0; i < count; i++)
                {
                    if (o >= p.Length) return false;
                    int nl = p[o];
                    if (nl == 0 || o + 1 + nl > p.Length) return false;
                    int len = LinkStateCodec.ItemLen(type, ek, p, o, nl);
                    if (len <= 0 || o + len > p.Length) return false;
                    string key = group + "|" + Encoding.UTF8.GetString(p.Slice(o + 1, nl));
                    if (r.Items.Any(x => x.Key == key)) return false;
                    r.Items.Add(new EventPiece(key, p.Slice(o, len).ToArray()));
                    o += len;
                }
                if (o != p.Length) return false;
                pieces = r;
                return true;
            }
            case Protocol.ConvUp or Protocol.ConvDown:
            {
                // [kind][tok:4][text]: the camera's samples ("v1|cid|s;s;s", a sample's 6th '~' field the cut) and the cursor's row
                if (p.Length < head + Protocol.LootFixedLen + 1) return false;
                byte k = p[head];
                if (k is not (Protocol.ConvCam or Protocol.ConvHover)) return false;
                var text = p[(head + Protocol.LootFixedLen)..];
                if (!TryCid(text, out string cid, out var rest)) return false;
                var prefix = p[..head].ToArray();
                string key = type.ToString("X2", CultureInfo.InvariantCulture) + "|" + Convert.ToHexString(prefix) + "|" + k.ToString(CultureInfo.InvariantCulture) + "|" + cid;
                var r = new EventPieces { Kind = k == Protocol.ConvCam ? "conv-cam" : "conv-hover", Type = type, Prefix = [], Batched = false,
                                          Protected = k == Protocol.ConvCam && HasCut(rest) };
                r.Items.Add(new EventPiece(key, p.ToArray()));
                pieces = r;
                return true;
            }
        }
        return false;
    }

    private static bool TryCid(ReadOnlySpan<byte> text, out string cid, out ReadOnlySpan<byte> rest)
    {
        cid = ""; rest = default;
        if (!text.StartsWith("v1|"u8)) return false;
        var t = text[3..];
        int bar = t.IndexOf((byte)'|');
        int end = bar < 0 ? t.Length : bar;
        if (end == 0 || end > 12) return false;
        foreach (byte b in t[..end]) if (b is < (byte)'0' or > (byte)'9') return false;
        cid = Encoding.ASCII.GetString(t[..end]);
        rest = bar < 0 ? default : t[(bar + 1)..];
        return true;
    }

    /// <summary>Does a camera text's sample list carry a cut (a sample's 6th '~' field "1")?</summary>
    public static bool HasCut(ReadOnlySpan<byte> samples)
    {
        int field = 0, start = 0;
        for (int i = 0; i <= samples.Length; i++)
        {
            bool endSample = i == samples.Length || samples[i] == (byte)';';
            if (!endSample && samples[i] != (byte)'~') continue;
            if (field == 5 && i - start == 1 && samples[start] == (byte)'1') return true;
            field = endSample ? 0 : field + 1;
            start = i + 1;
        }
        return false;
    }

    /// <summary>The age past which a waiting event of this frame is dropped at send time (0 = never).</summary>
    public static int MaxAgeMs(byte[] frame)
    {
        if (frame.Length < 3) return 0;
        int type = frame[0];
        var p = frame.AsSpan(3);
        switch (type)
        {
            case Protocol.VoiceUp or Protocol.VoiceDown: return 1000;   // a second-old voice frame only plays as a stutter
            case Protocol.ConvUp or Protocol.ConvDown or Protocol.W203Up or Protocol.W203Down:
            {
                int head = (Protocol.JoinWireFor(type) is null ? 1 : 0) + Protocol.JoinHeaderLen;
                if (p.Length <= head) return 0;
                byte k = p[head];
                if (type is Protocol.ConvUp or Protocol.ConvDown)
                    return k is Protocol.ConvCam or Protocol.ConvHover ? 2000 : 0;   // lines, votes, options, timers, ends: never
                return k == Protocol.W203Behind ? 5000 : 0;
            }
        }
        return 0;
    }

    /// <summary>The kind's label in an MP-HEALTH list (no ':' -- "conv:5" becomes "conv.5").</summary>
    public static string Label(byte[] frame) =>
        frame.Length < 3 ? "?" : LinkKinds.Of(frame[0], frame.AsSpan(3)).Replace(':', '.');

    /// <summary>"k:n,k:n" (or "-"), by count.</summary>
    public static string Counts(IReadOnlyDictionary<string, long> d) =>
        d.Count == 0 ? "-" : string.Join(',', d.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + ":" + kv.Value.ToString(CultureInfo.InvariantCulture)));
}
