// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Threading.Channels;

namespace KcdMp.Steam;

/// <summary>
/// KCT fix (icd14): a Steam connection's inbox, bounded for state only. Every message waits here in arrival order until the reader
/// takes it; past <see cref="StateCap"/> waiting STATE messages (the unreliable lane's: each one whole frame, a body's latest sample --
/// a newer one follows) the oldest waiting state message is dropped and counted. Events (the reliable lane: they may be pieces of a
/// larger frame) are never dropped. StateCap 0 = unbounded (the switch off: steam_inbox_bound). Thread-safe: one writer (the pump), one
/// reader.
/// </summary>
public sealed class SteamInbox
{
    private readonly object _lock = new();
    private readonly LinkedList<(byte[] Data, bool State)> _q = new();
    private readonly SemaphoreSlim _signal = new(0);
    private int _states;
    private bool _completed;

    /// <summary>The default for new connections (the agent sets it from its switch); 0 = unbounded.</summary>
    public static int DefaultStateCap = 1024;

    public int StateCap { get; set; } = DefaultStateCap;
    public long StateDropped;
    public int Count { get { lock (_lock) return _q.Count; } }
    public int MaxDepth { get; private set; }

    /// <summary>One message in; false once completed.</summary>
    public bool TryWrite(byte[] data, bool state)
    {
        lock (_lock)
        {
            if (_completed) return false;
            _q.AddLast((data, state));
            if (state) _states++;
            if (StateCap > 0)
                for (var n = _q.First; _states > StateCap && n is not null;)
                {
                    var next = n.Next;
                    if (n.Value.State) { _q.Remove(n); _states--; Interlocked.Increment(ref StateDropped); }
                    n = next;
                }
            if (_q.Count > MaxDepth) MaxDepth = _q.Count;
        }
        _signal.Release();
        return true;
    }

    /// <summary>The oldest message; throws <see cref="ChannelClosedException"/> once completed and empty (as a completed channel).</summary>
    public async ValueTask<byte[]> ReadAsync(CancellationToken ct)
    {
        while (true)
        {
            lock (_lock)
            {
                if (_q.First is { } n)
                {
                    _q.RemoveFirst();
                    if (n.Value.State) _states--;
                    return n.Value.Data;
                }
                if (_completed) throw new ChannelClosedException();
            }
            await _signal.WaitAsync(ct);
        }
    }

    public bool TryRead(out byte[] data)
    {
        lock (_lock)
        {
            if (_q.First is { } n) { _q.RemoveFirst(); if (n.Value.State) _states--; data = n.Value.Data; return true; }
        }
        data = [];
        return false;
    }

    public void Complete()
    {
        lock (_lock) _completed = true;
        _signal.Release();
    }
}
