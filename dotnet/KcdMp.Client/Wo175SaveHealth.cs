// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-175 Part 1.4 (S4): the save health check and repair for the "stuck quest fight" weapon lock.
///
/// The quest graphs (ConceptModule) keep their node state as XML text inside the save, in the TLV node
/// <see cref="ConceptPath"/>: <c>ConceptState\0</c>, 8 opaque bytes, a NUL, then <c>&lt;Roots&gt;...&lt;/Roots&gt;</c>.
/// A quest fight module applies the game's EnableAllowedWeaponsInQAM (unarmed only) while its
/// <c>_skirmishResult</c> is <c>Started</c>. A fight that ended without a verdict leaves
/// <c>_skirmishControler value="Done"</c> beside <c>_skirmishResult value="Started"</c> under the same
/// <c>&lt;Nodes&gt;</c>: the player never draws a weapon again (proven live on the field save, WO-175).
///
/// The repair (proven live): that one <c>_skirmishResult</c> becomes <c>value="Fled"</c>, padded with spaces
/// before <c>/&gt;</c> to the SAME byte length, so no TLV length changes and every other byte of the stream stays;
/// the stream is re-deflated with the original description and footer tail and re-signed
/// (<see cref="WhsSave.Deflate"/>). The game loads it, draws, and re-saves the node as <c>value="Fled"</c>. An
/// <c>Active</c> controller (a fight in progress) is never touched. The result is always a NEW file next to the
/// original (<see cref="RepairedName"/>); the original is never written.
/// </summary>
public static class Wo175SaveHealth
{
    public const string CliTag = "W175";

    /// <summary>ConceptModule's state blob: 01F4/01F6/730A/01F2/036B/20A9.</summary>
    public static readonly ushort[] ConceptPath = [0x01F4, 0x01F6, 0x730A, 0x01F2, 0x036B, 0x20A9];

    private static ReadOnlySpan<byte> Header => "ConceptState\0"u8;
    private const int OpaqueBytes = 8;   // after the header; left untouched
    private const string Controller = "_skirmishControler";   // the game's spelling
    private const string Result = "_skirmishResult";

    /// <summary>A stuck fight: the module path (leading underscores stripped, the Roots and Nodes wrappers
    /// skipped) and the stream offset (inflated bytes) of its <c>&lt;_skirmishResult</c> element.</summary>
    public sealed record StuckFight(string QuestPath, int Offset);

    /// <summary>Readable = the file inflates and holds a parseable concept state; Why says what failed (or "ok").</summary>
    public sealed record HealthReport(bool Readable, string Why, List<StuckFight> Stuck);

    private readonly record struct Hit(StuckFight Fight, int Len);

    private sealed class Frame(string name)
    {
        public readonly string Name = name;
        public string? Ctrl, Res;
        public int ResOff, ResLen;
    }

    private static readonly Regex ValueAttr = new(@"\svalue\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex StartedForm = new(@"^<_skirmishResult\s+value\s*=\s*""Started""\s*/>$", RegexOptions.CultureInvariant);

    // ------------------------------------------------------------------ check

    /// <summary>The health report of a file image. Pure, no IO; never throws.</summary>
    public static HealthReport Check(byte[] file)
    {
        WhsSave.Container c;
        try { c = WhsSave.Inflate(file); }
        catch (Exception ex) { return new(false, "not a readable save: " + ex.Message, []); }
        var (ok, why, hits) = Scan(c.Raw);
        if (!ok) return new(false, why, []);
        if (!WhsSave.VerifyFooter(file).Ok) why = "ok (the footer MD5 does not verify: repair refuses this file)";
        return new(true, why, hits.Select(h => h.Fight).ToList());
    }

    /// <summary>The stuck fights of a file image (empty for a healthy save and for a non-save: see <see cref="Check"/>).</summary>
    public static List<StuckFight> FindStuckFights(byte[] file) => Check(file).Stuck;

    /// <summary>The scan of an inflated stream. Never throws.</summary>
    private static (bool Ok, string Why, List<Hit> Hits) Scan(byte[] raw)
    {
        try
        {
            if (WhsSave.PathGet(raw, ConceptPath) is not WhsSave.Node n) return (false, "no ConceptModule state node (01f4/01f6/730a/01f2/036b/20a9)", []);
            var p = raw.AsSpan(n.PayloadOff, n.Len);
            if (!p.StartsWith(Header)) return (false, "the concept node does not start with ConceptState", []);
            int x = Header.Length + OpaqueBytes;
            while (x < p.Length && x < Header.Length + OpaqueBytes + 8 && p[x] != (byte)'<') x++;
            if (x >= p.Length || p[x] != (byte)'<') return (false, "the concept node holds no XML after its header", []);
            return ScanXml(raw, n.PayloadOff + x, n.End);
        }
        catch (Exception ex) { return (false, "the stream does not parse: " + ex.Message, []); }
    }

    /// <summary>A small tag walker over [start,end) of buf: element stack, quotes honoured, comments and
    /// declarations skipped. Only self-closing children of a <c>Nodes</c> element are read for the pair.</summary>
    private static (bool Ok, string Why, List<Hit> Hits) ScanXml(byte[] buf, int start, int end)
    {
        var hits = new List<Hit>();
        var stack = new List<Frame>();
        var s = buf.AsSpan(start, end - start);
        int i = 0;
        (bool, string, List<Hit>) Bad(string why) => (false, $"the concept XML is malformed at stream offset {start + i}: {why}", []);
        while (i < s.Length)
        {
            int lt = s[i..].IndexOf((byte)'<');
            if (lt < 0) break;
            i += lt;
            if (s[i..].StartsWith("<!--"u8))
            {
                int e = s[(i + 4)..].IndexOf("-->"u8);
                if (e < 0) return Bad("unterminated comment");
                i += 4 + e + 3;
                continue;
            }
            int j = i + 1;
            byte q = 0;
            for (; j < s.Length; j++)
            {
                byte b = s[j];
                if (q != 0) { if (b == q) q = 0; }
                else if (b == (byte)'"' || b == (byte)'\'') q = b;
                else if (b == (byte)'>') break;
            }
            if (j >= s.Length) return Bad("unterminated tag");
            var tag = s[(i + 1)..j];
            if (tag.Length == 0) return Bad("empty tag");
            if (tag[0] == (byte)'?' || tag[0] == (byte)'!') { i = j + 1; continue; }
            if (tag[0] == (byte)'/')
            {
                string name = Encoding.Latin1.GetString(tag[1..]).Trim();
                if (stack.Count == 0 || stack[^1].Name != name) return Bad($"</{name}> closes {(stack.Count == 0 ? "nothing" : "<" + stack[^1].Name + ">")}");
                var f = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (f.Name == "Nodes" && f.Ctrl == "Done" && f.Res == "Started")
                    hits.Add(new Hit(new StuckFight(PathOf(stack), start + f.ResOff), f.ResLen));
            }
            else
            {
                bool self = tag[^1] == (byte)'/';
                var body = self ? tag[..^1] : tag;
                int sp = 0;
                while (sp < body.Length && body[sp] > (byte)' ') sp++;
                string name = Encoding.Latin1.GetString(body[..sp]);
                if (name.Length == 0) return Bad("nameless element");
                if (!self) stack.Add(new Frame(name));
                else if (stack.Count > 0 && stack[^1].Name == "Nodes" && (name == Controller || name == Result))
                {
                    var m = ValueAttr.Match(Encoding.Latin1.GetString(body));
                    var top = stack[^1];
                    if (m.Success && name == Controller) top.Ctrl ??= m.Groups[1].Value;
                    else if (m.Success && top.Res is null) { top.Res = m.Groups[1].Value; top.ResOff = i; top.ResLen = j - i + 1; }
                }
            }
            i = j + 1;
        }
        if (stack.Count > 0) return Bad($"<{stack[^1].Name}> is never closed");
        return (true, "ok", hits);
    }

    /// <summary>The enclosing elements, outermost first: the document root and the Nodes wrappers skipped, leading underscores stripped.</summary>
    private static string PathOf(List<Frame> stack) =>
        string.Join("/", stack.Skip(1).Where(f => f.Name != "Nodes").Select(f => f.Name.TrimStart('_')));

    // ------------------------------------------------------------------ repair

    /// <summary>The replacement for a stuck <c>_skirmishResult</c> element: <c>value="Fled"</c>, the same byte length.</summary>
    public static byte[] FledElement(int len)
    {
        const string head = "<" + Result + " value=\"Fled\"";
        if (len < head.Length + 2) throw new InvalidDataException($"a {len}-byte element cannot hold the Fled form");
        return Encoding.ASCII.GetBytes(head + new string(' ', len - head.Length - 2) + "/>");
    }

    /// <summary>
    /// A new file image with every stuck fight set to Fled (same-length edit, re-deflated, re-signed). A healthy
    /// save comes back as an unchanged copy with an empty <paramref name="fixedFights"/>. Throws
    /// <see cref="InvalidDataException"/> on an unreadable save, a footer that does not verify, an element of an
    /// unexpected form, or a result that does not re-check clean.
    /// </summary>
    public static byte[] Repair(byte[] file, out List<StuckFight> fixedFights)
    {
        if (!WhsSave.VerifyFooter(file).Ok) throw new InvalidDataException("the save's footer MD5 does not verify; refusing");
        var c = WhsSave.Inflate(file);
        var (ok, why, hits) = Scan(c.Raw);
        if (!ok) throw new InvalidDataException(why);
        fixedFights = hits.Select(h => h.Fight).ToList();
        if (hits.Count == 0) return (byte[])file.Clone();
        var raw = (byte[])c.Raw.Clone();
        foreach (var h in hits)
        {
            string text = Encoding.Latin1.GetString(raw, h.Fight.Offset, h.Len);
            if (!StartedForm.IsMatch(text)) throw new InvalidDataException($"unexpected element form at {h.Fight.Offset}: {text}");
            FledElement(h.Len).CopyTo(raw, h.Fight.Offset);
        }
        var o = WhsSave.Deflate(c.DescBytes, raw, c.FooterTail);

        // the gate: re-signed, inflates to exactly the edited stream, no stuck fight left, same description and tail
        if (!WhsSave.VerifyFooter(o).Ok) throw new InvalidDataException("the repaired file's footer does not verify");
        var oc = WhsSave.Inflate(o);
        if (!oc.Raw.AsSpan().SequenceEqual(raw)) throw new InvalidDataException("the repaired file does not inflate to the edited stream");
        if (!oc.DescBytes.AsSpan().SequenceEqual(c.DescBytes) || !oc.FooterTail.AsSpan().SequenceEqual(c.FooterTail))
            throw new InvalidDataException("the repaired file's description or footer tail changed");
        var re = Check(o);
        if (!re.Readable || re.Stuck.Count != 0) throw new InvalidDataException($"the repaired file still checks stuck ({re.Why}, {re.Stuck.Count} left)");
        return o;
    }

    /// <summary>
    /// The repaired file's name in the original's folder: <c>&lt;base&gt;_fixed.whs</c>, then <c>_fixed2</c>, <c>_fixed3</c>...
    /// while a name is taken (compared case-insensitively, as Windows does). The game lists any .whs in a playline folder.
    /// </summary>
    public static string RepairedName(string originalBaseName, IEnumerable<string> existingNames)
    {
        string b = Path.GetFileName(originalBaseName);
        if (b.EndsWith(".whs", StringComparison.OrdinalIgnoreCase)) b = b[..^4];
        var taken = new HashSet<string>(existingNames.Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        for (int k = 1; ; k++)
        {
            string name = b + "_fixed" + (k == 1 ? "" : k.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ".whs";
            if (!taken.Contains(name)) return name;
        }
    }

    // ------------------------------------------------------------------ CLI

    /// <summary>
    /// <c>--w175 check &lt;save.whs&gt;</c> (exit 0 healthy, 1 stuck, 2 unreadable) ·
    /// <c>--w175 repair &lt;save.whs&gt;</c> (writes &lt;base&gt;_fixed.whs NEXT TO it, never in place; exit 0 written,
    /// 1 nothing to repair or refused, 2 unreadable). One line: <c>W175 {json}</c>.
    /// </summary>
    public static int RunCli(string[] args, TextWriter w)
    {
        int at = Array.IndexOf(args, "--w175");
        string cmd = at >= 0 && at + 1 < args.Length ? args[at + 1] : "";
        string file = at >= 0 && at + 2 < args.Length ? args[at + 2] : "";
        void Out(object o) => w.WriteLine(CliTag + " " + JsonSerializer.Serialize(o));
        object Fights(IEnumerable<StuckFight> l) => l.Select(f => new { quest = f.QuestPath, offset = f.Offset }).ToList();
        if ((cmd != "check" && cmd != "repair") || file == "") { Out(new { ok = false, why = "usage: --w175 check|repair <save.whs>" }); return 2; }
        byte[] data;
        try { data = WhsSave.ReadShared(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Out(new { ok = false, file = Path.GetFileName(file), why = "unreadable: " + ex.Message }); return 2; }
        var rep = Check(data);
        if (cmd == "check")
        {
            Out(new { ok = rep.Readable, file = Path.GetFileName(file), readable = rep.Readable, why = rep.Why, stuck = Fights(rep.Stuck) });
            return !rep.Readable ? 2 : rep.Stuck.Count == 0 ? 0 : 1;
        }
        if (!rep.Readable) { Out(new { ok = false, file = Path.GetFileName(file), why = rep.Why }); return 2; }
        if (rep.Stuck.Count == 0) { Out(new { ok = false, file = Path.GetFileName(file), why = "nothing to repair", stuck = Fights(rep.Stuck) }); return 1; }
        string dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
        string name = RepairedName(Path.GetFileName(file), Directory.EnumerateFiles(dir).Select(Path.GetFileName).OfType<string>());
        string target = Path.Combine(dir, name);
        string part = target + ".part";
        try
        {
            var o = Repair(data, out var fixedFights);
            if (File.Exists(target)) { Out(new { ok = false, file = Path.GetFileName(file), why = $"{name} exists; refusing to overwrite" }); return 1; }
            using (var fs = new FileStream(part, FileMode.CreateNew, FileAccess.Write)) fs.Write(o);
            File.Move(part, target, overwrite: false);
            if (!WhsSave.ReadShared(target).AsSpan().SequenceEqual(o)) throw new IOException("the written file does not read back");
            Out(new { ok = true, file = Path.GetFileName(file), name, path = target, @fixed = Fights(fixedFights) });
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { }
            Out(new { ok = false, file = Path.GetFileName(file), why = ex.Message });
            return 1;
        }
    }
}
