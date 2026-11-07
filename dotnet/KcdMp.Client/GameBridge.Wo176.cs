// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-176 (docs/WO-176-findings.md): the bridge. The rules are Wo176Rules (Wo176.cs); the DLL half is native/KCDMP/wo176.*.
//
//   A1 mp_arranged_guard (host, default ON): the DLL keeps every partner's figure out of a quest's arranged fight by the fight's own context;
//      a joiner's blow on such an actor is routed nowhere (no engine, no raw blow, no engage, no brain message, no fatal), the joiner is told
//      once (W164 kind 9 -> KCD2MP_W176Say):
//        MP-W176 kept npc=<n> by_ghost=<g> path=real-hit|wo121 told=yes|no
//   B3a mp_copy_blocks (joiner, default ON): a blow the host's NPC blocked / parried / had its block broken by, with no reaction row of the
//      host's coming (react != queued), plays the NPC's own block row on its copy:
//        WO176-COPYBLOCK npc=<n> verdict=<v> row="<spec>"|none why=<w>
public partial class GameBridge
{
    private volatile bool _w176ArrangedGuard = Wo176Rules.DefaultArrangedGuard, _w176CopyBlocks = Wo176Rules.DefaultCopyBlocks;
    private readonly ConcurrentDictionary<string, long> _w176KeptAt = new(StringComparer.Ordinal);   // host: actor -> last kept-out blow
    private readonly ConcurrentDictionary<byte, long> _w176ToldAt = new();                           // host: joiner -> last kept-out blow of his
    private long _w176Kept, _w176Told, _w176FatalKept, _w176StoryIn, _w176BlockRows, _w176BlockNone;
    private long _w176SaidAt;

    private void Wo176OnEvent(string name, string? arg)
    {
        if (name != "w176_cfg") return;
        foreach (var (k, on) in Wo176Rules.ParseCfg(arg))
        {
            if (k == "arranged_guard") { _w176ArrangedGuard = on; _ = Wo176PushConfigAsync(); }
            else if (k == "copy_blocks") _w176CopyBlocks = on;
        }
        Console.WriteLine($"MP-W176 cfg arranged_guard={(_w176ArrangedGuard ? "on" : "off")} copy_blocks={(_w176CopyBlocks ? "on" : "off")}");
    }

    private async Task Wo176PushConfigAsync()
    {
        try
        {
            // walk: the figure check's level-wide walk is the host's only (arranged fights run in the host's world)
            var r = await _combat.Wo163Async(23, [(byte)(_w176ArrangedGuard ? 1 : 0), (byte)(_isDamageAuthority ? 1 : 0)]);
            Console.WriteLine($"MP-W176 mp_arranged_guard {(_w176ArrangedGuard ? "on" : "off")} figure_check={(_isDamageAuthority ? "host" : "off")} -> DLL {(r is { Ok: true } x && x.Payload.Length > 0 ? (x.Payload[0] != 0 ? "on" : "off") : "no answer (an older DLL: WO-174's name list only)")}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W176 mp_arranged_guard not pushed: {ex.GetType().Name}"); }
    }

    /// <summary>A1, host: the DLL kept a joiner's blow out of an arranged fight. Remember the actor (a fatal packet for it is not applied) and tell
    /// the joiner once per story fight.</summary>
    private async Task Wo176KeptAsync(byte source, string npc, string path)
    {
        long now = Environment.TickCount64;
        Interlocked.Increment(ref _w176Kept);
        _w176KeptAt[npc] = now;
        long last = _w176ToldAt.TryGetValue(source, out long t) ? t : 0;
        _w176ToldAt[source] = now;
        bool tell = Wo176Rules.ShouldTell(last, now) && W164Text.IsNpcName(npc);
        long n = Interlocked.Read(ref _w176Kept);
        if (tell || n <= 50 || n % 50 == 0)
            Console.WriteLine($"MP-W176 kept npc={npc} by_ghost={source} path={path} told={(tell ? "yes" : "no")} -- a quest's arranged fight: the blow is not routed into it");
        if (!tell) return;
        Interlocked.Increment(ref _w176Told);
        await W164SendAsync(source, Protocol.W164StoryFight, npc);
    }

    /// <summary>A1, host: an actor the DLL kept a figure's blow from within the last minute (its fatal packet is the story's, not ours).</summary>
    private bool Wo176IsArranged(string npc) =>
        _w176KeptAt.TryGetValue(npc, out long at) && Wo176Rules.StillArranged(at, Environment.TickCount64);

    private void Wo176NoteFatalKept(string npc, byte source)
    {
        long n = Interlocked.Increment(ref _w176FatalKept);
        if (n <= 20) Console.WriteLine($"MP-W176 fatal npc={npc} by_ghost={source} not applied -- the actor is in a quest's arranged fight (the story decides)");
    }

    /// <summary>A1, joiner: the host says this fight is the story's. Said once (the host already sends it once per fight).</summary>
    private async Task Wo176OnStoryFightInAsync(string text)
    {
        if (!W164Text.IsNpcName(text)) { Interlocked.Increment(ref _w164Malformed); return; }
        Interlocked.Increment(ref _w176StoryIn);
        long now = Environment.TickCount64, last = Interlocked.Read(ref _w176SaidAt);
        Console.WriteLine($"WO176-STORYFIGHT npc={text} -- the host's game kept my blow out of a quest's arranged fight{(last > 0 && now - last < 60_000 ? " (already said)" : "")}");
        if (last > 0 && now - last < 60_000) return;
        Interlocked.Exchange(ref _w176SaidAt, now);
        await ExecLuaAsync($"if KCD2MP_W176Say then KCD2MP_W176Say(\"{EscapeLua(Wo176Rules.StoryFightText)}\") end");
    }

    /// <summary>B3a, joiner: the host's NPC blocked / parried / had its block broken by my blow and no reaction row of the host's comes: its
    /// copy plays its own block row (the weapon from the last swing row it played).</summary>
    private async Task Wo176CopyBlockAsync(string npc, HitVerdict verdict, byte react)
    {
        int action = Wo176Rules.BlockActionFor(verdict, react);
        if (!_w176CopyBlocks || action < 0) return;
        ActionRowCatalog? cat = _rowCatalog.IsCompletedSuccessfully ? _rowCatalog.Result : null;
        string why;
        ActionRowCatalog.Row? br = null;
        if (cat is null) why = "no-catalog";
        else if (!_w165LastRow.TryGetValue(npc, out var g) || !cat.TryGet(g, out var swing)) why = "no-weapon-known(no swing row of this copy yet)";
        else if ((br = cat.BlockRow(swing, action, -1)) is null) why = "no-row-for-this-weapon";
        else if (!_npcEntityIds.TryGetValue(npc, out uint neid)) { why = "not-a-copy-here"; br = null; }
        else
        {
            var r = await _combat.GhostSwingForResultAsync(neid, br.Value.Spec);
            why = r.ReasonTag;
            if (!r.Ok) br = null;
        }
        if (br is null) Interlocked.Increment(ref _w176BlockNone); else Interlocked.Increment(ref _w176BlockRows);
        Console.WriteLine($"WO176-COPYBLOCK npc={npc} verdict={HitVerdictMsg.VerdictName(verdict)} row={(br is { } x ? $"\"{x.Spec}\"" : "none")} why={why}");
    }

    private string Wo176StatusLine() =>
        $"MP-W176-STATS arranged_guard={(_w176ArrangedGuard ? "on" : "off")} kept={Interlocked.Read(ref _w176Kept)} told={Interlocked.Read(ref _w176Told)} fatal_kept={Interlocked.Read(ref _w176FatalKept)} story_in={Interlocked.Read(ref _w176StoryIn)} copy_blocks={(_w176CopyBlocks ? "on" : "off")} block_rows={Interlocked.Read(ref _w176BlockRows)} block_none={Interlocked.Read(ref _w176BlockNone)}";
}
