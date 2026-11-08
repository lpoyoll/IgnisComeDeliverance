// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// KCT fix (icd14, W213 state_order): per-body order on the receiver. State travels on Steam's unreliable lane 1 and the change-carrying
/// samples (a swing cue, a resync, a figure's state block) as events on reliable lane 0 -- the two lanes have no order between them, so an
/// older sample of a body can arrive after a newer one. The joiner's path did not drop it: an NPC state's flags were bookkept in order
/// (GameBridge's nInOrder) but its position still went to the DLL, and a figure's sample got its native seq in ARRIVAL order -- the copy
/// snapped back a step. This drops, at the reader, a plain sample older than the newest read for its body: an NPC state by its u16 seq
/// (within 64 behind; farther is a restarted sender), a figure's position by its sender ms (within 10 s behind). A sample that carries
/// something said once (TransientNpcFlags, a v8 figure state block) is never dropped -- it only raises the newest. A body silent for 5 s
/// starts over. Not thread-safe: the one reader calls it.
/// </summary>
public sealed class StateOrderGuard
{
    private readonly Dictionary<string, (uint Order, long AtMs)> _last = new(StringComparer.Ordinal);
    public long OlderDropped { get; private set; }
    public const int SeqWindow = 64, MsWindow = 10_000, ForgetMs = 5_000;

    /// <summary>True: drop this frame (an older plain sample of its body).</summary>
    public bool Drop(int type, byte[] p, long nowMs)
    {
        string key; uint order; bool wide, plain;
        if (type == Protocol.NpcStateDown)
        {
            if (p.Length < 3 || p.Length != 2 + p[1] + Protocol.NpcStateFixedTail) return false;
            int o = 2 + p[1];
            order = BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(o + Protocol.NpcStateSeqOffset));
            plain = (p[o + Protocol.NpcStateFlagsOffset] & LinkStateCodec.TransientNpcFlags) == 0;
            key = "n|" + p[0] + "|" + Encoding.UTF8.GetString(p, 2, p[1]);
            wide = false;
        }
        else if (type == Protocol.Ghost)
        {
            if (p.Length < Protocol.GhostPayloadLen || !Protocol.IsGhostPayloadLen(p.Length)) return false;
            if ((p[17] & Protocol.PositionFlagSenderMs) == 0 || p.Length is not (Protocol.GhostPayloadLen + Protocol.SenderMsLen or Protocol.GhostPayloadLenV8Max))
                return false;   // no sender stamp: no order to judge by
            order = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(p.Length - Protocol.SenderMsLen));
            plain = p.Length == Protocol.GhostPayloadLen + Protocol.SenderMsLen;
            key = "g|" + p[0];
            wide = true;
        }
        else return false;

        if (_last.TryGetValue(key, out var l) && nowMs - l.AtMs <= ForgetMs)
        {
            bool older = wide ? unchecked((int)(l.Order - order)) is > 0 and < MsWindow
                              : order != l.Order && !Protocol.SeqIsNewer((ushort)order, (ushort)l.Order) && (ushort)(l.Order - order) <= SeqWindow;
            if (older)
            {
                if (!plain) return false;   // said once: it goes on, and the newest stays the newest
                OlderDropped++;
                return true;
            }
        }
        _last[key] = (order, nowMs);
        if (_last.Count > 8192) _last.Clear();   // bodies come and go; a cleared table only lets one late sample through
        return false;
    }
}
