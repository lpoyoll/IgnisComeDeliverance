// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

/// <summary>
/// WO-171 M: the partner map markers' pure rules -- which colour a player is (the slot table), which of the artist's eight pictures
/// a heading wants, when a marker may be moved or turned, and the name text. docs/WO-171-findings.md has the conventions.
/// </summary>
public static class Wo171Rules
{
    public const int Colours = 4;
    public const int Frames = 8;

    /// <summary>Player 1 (the host) .. 4, in the artist's order.</summary>
    public static readonly string[] ColourNames = { "red", "purple", "blue", "green" };
    private static readonly char[] ColourLetters = { 'r', 'p', 'b', 'g' };
    public static readonly string[] Compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public enum Style { Rotating, Flat, Companion }

    public static bool TryParseStyle(string? s, out Style style)
    {
        style = Style.Rotating;
        switch ((s ?? "").Trim().ToLowerInvariant())
        {
            case "rotating": style = Style.Rotating; return true;
            case "flat": style = Style.Flat; return true;
            case "companion": style = Style.Companion; return true;
            default: return false;
        }
    }

    public static string StyleWord(Style s) => s switch { Style.Flat => "flat", Style.Companion => "companion", _ => "rotating" };

    // ------------------------------------------------------------------ the facing

    /// <summary>
    /// The compass octant (0 = N, 1 = NE, ... 7 = NW, clockwise) a streamed yaw means. The yaw is the engine's: radians, 0 faces +Y (north on
    /// the map), positive turns counter-clockwise, so the compass heading is its negative. <paramref name="flip"/> mirrors the turning (a
    /// map that turns the wrong way); <paramref name="offset"/> then rotates the result by whole steps (a convention off by a step).
    /// </summary>
    public static int Octant(double rotZRad, int offset = 0, bool flip = false)
    {
        double h = HeadingDeg(rotZRad);
        return Finish(RawOctant(h), offset, flip);
    }

    public static double HeadingDeg(double rotZRad)
    {
        if (double.IsNaN(rotZRad) || double.IsInfinity(rotZRad)) return 0;
        double h = -rotZRad * 180.0 / Math.PI;
        h %= 360.0;
        if (h < 0) h += 360.0;
        return h;
    }

    public static int RawOctant(double headingDeg) => ((int)Math.Floor((headingDeg + 22.5) / 45.0)) & 7;

    private static int Finish(int oct, int offset, bool flip)
    {
        if (flip) oct = (8 - oct) & 7;
        return ((oct + offset) % 8 + 8) % 8;
    }

    /// <summary>
    /// The octant with a little hysteresis: a heading right on a boundary does not flicker between two pictures. The previous octant is
    /// kept while the heading is within <paramref name="holdDeg"/> of its edge. -1 as <paramref name="previousRaw"/> = none.
    /// </summary>
    public static int PickOctant(int previousRaw, double headingDeg, double holdDeg = 5.0)
    {
        int raw = RawOctant(headingDeg);
        if (previousRaw < 0 || previousRaw == raw) return raw;
        double centre = previousRaw * 45.0;
        double d = Math.Abs(headingDeg - centre);
        if (d > 180) d = 360 - d;
        return d <= 22.5 + holdDeg ? previousRaw : raw;
    }

    public static int ApplyOffsetFlip(int rawOctant, int offset, bool flip) => Finish(rawOctant, offset, flip);

    /// <summary>
    /// The artist's frame n (0..7) for a compass octant: this build's reading of the art -- frame 0 is the front view (the figure faces the
    /// viewer: south) and the frames turn clockwise seen from above, one octant each, so frame n faces octant (n + 4) mod 8 and a figure facing
    /// north shows its back (frame 4). A convention off by a step is <c>mp_marker_dir_offset</c>; turning the wrong way is
    /// <c>mp_marker_dir_flip</c>.
    /// </summary>
    public static int FrameOfOctant(int octant) => (octant + 4) & 7;

    // ------------------------------------------------------------------ the pictures

    /// <summary>A name can be loaded into the game's localization only once per game run, so a colour taken over by another name uses the
    /// next revision of its pictures (the same art under another key): 4 revisions, then the last one's name stays.</summary>
    public const int Revisions = 4;
    private static readonly string[] RevSuffix = { "", "b", "c", "d" };

    /// <summary>The pipe's picture code: 0 = none (the game's own icon), else revision * 32 + slot 0..3 * 8 + frame + 1 (1..128).</summary>
    public static int PictureCode(int slot, int frame, int rev = 0) =>
        slot is < 1 or > Colours || rev is < 0 or >= Revisions ? 0 : rev * Colours * Frames + (slot - 1) * Frames + (frame & 7) + 1;

    /// <summary>The picture's name in the pak and in the game: <c>kct_r3</c>, <c>kct_r3b</c> (colour letter, frame digit, revision suffix).</summary>
    public static string PictureName(int code)
    {
        if (code is < 1 or > Colours * Frames * Revisions) return "-";
        int i = (code - 1) % (Colours * Frames), rev = (code - 1) / (Colours * Frames);
        return $"kct_{ColourLetters[i / Frames]}{i % Frames}{RevSuffix[rev]}";
    }

    /// <summary>
    /// The pictures the pak must hold, each as <c>kct_&lt;c&gt;&lt;n&gt;&lt;rev&gt;_icon.dds</c> (64 px: the compass draws it 1:1) and the same with an
    /// M after the name (128 px: the map draws its icons scaled down): 128 + 128.
    /// </summary>
    public static IEnumerable<string> ManifestFiles()
    {
        foreach (string big in new[] { "", "M" })
            for (int r = 0; r < Revisions; r++)
                for (int c = 0; c < Colours; c++)
                    for (int n = 0; n < Frames; n++)
                        yield return $"kct_{ColourLetters[c]}{n}{RevSuffix[r]}{big}_icon.dds";
    }

    /// <summary>The map's (large) name of a picture: its name and an M.</summary>
    public static string MapName(int code) => PictureName(code) + "M";

    /// <summary>The code a style asks for: rotating = the picture of the heading's octant, flat = the north-facing one, companion = none.</summary>
    public static int CodeFor(Style style, int slot, int octant, int offset, int rev = 0)
    {
        if (style == Style.Companion || slot is < 1 or > Colours) return 0;
        int o = style == Style.Flat ? ((0 + offset) % 8 + 8) % 8 : octant;
        return PictureCode(slot, FrameOfOctant(o), rev);
    }

    // ------------------------------------------------------------------ the slots

    /// <summary>
    /// Who is which colour. Slot 1 is the host; the others take 2, 3, 4 in the order they first appear. A player is known by his name (a
    /// crash-rejoin comes back with a new ghost id and the same name), so a rejoin keeps its colour. A fifth player has no slot (0): the
    /// companion icon.
    /// </summary>
    public sealed class SlotTable
    {
        private readonly string?[] _who = new string?[Colours];
        private readonly object _lock = new();

        public int Slot(string identity, bool isHost)
        {
            lock (_lock)
            {
                for (int i = 0; i < Colours; i++) if (_who[i] == identity) return i + 1;
                if (isHost)
                {
                    if (_who[0] is null) { _who[0] = identity; return 1; }
                    // the host's slot is taken by another name (a different host this session): it takes the first free one
                }
                for (int i = 1; i < Colours; i++) if (_who[i] is null) { _who[i] = identity; return i + 1; }
                return 0;
            }
        }

        public void Reset() { lock (_lock) Array.Clear(_who); }

        public int Count { get { lock (_lock) return _who.Count(w => w is not null); } }
    }

    // ------------------------------------------------------------------ the throttles

    public const double MoveMinM = 2.0;
    public const long MoveMinMs = 1000;
    public const int MoveCapPerMinute = 60;
    public const long TurnMinMs = 250;   // at most 4 picture changes a second

    /// <summary>
    /// Whether a marker may be moved now: it moved more than 2 m since it was last set, the last move was at least a second ago, and fewer
    /// than 60 moves went out in the last minute (the field's 0.47.6 rate was 81 a minute).
    /// </summary>
    public sealed class MoveGate
    {
        private readonly Queue<long> _moves = new();
        private long _lastMs = long.MinValue / 2;
        private float _x, _y;
        private bool _have;

        public bool Due(float x, float y, long nowMs)
        {
            if (!_have) return true;
            if (nowMs - _lastMs < MoveMinMs) return false;
            double dx = x - _x, dy = y - _y;
            if (dx * dx + dy * dy <= MoveMinM * MoveMinM) return false;
            while (_moves.Count > 0 && nowMs - _moves.Peek() > 60_000) _moves.Dequeue();
            return _moves.Count < MoveCapPerMinute;
        }

        public void Sent(float x, float y, long nowMs, bool counted)
        {
            _have = true; _x = x; _y = y; _lastMs = nowMs;
            if (counted) _moves.Enqueue(nowMs);
        }

        public int InLastMinute(long nowMs) { while (_moves.Count > 0 && nowMs - _moves.Peek() > 60_000) _moves.Dequeue(); return _moves.Count; }
    }

    /// <summary>A picture change is due when the octant changed and the last change was at least 250 ms ago.</summary>
    public static bool TurnDue(int lastCode, int newCode, long lastChangeMs, long nowMs) => newCode != lastCode && nowMs - lastChangeMs >= TurnMinMs;

    // ------------------------------------------------------------------ the hover name

    /// <summary>
    /// The map's tooltip for a marker is the localization key "@ui_maplegend_" + the marker group's name (the map's picture name, kct_p3M). The
    /// agent writes one small file per (colour, revision) -- eight keys, the player's name as the text -- and the mod loads it
    /// (System.LoadLocalizationXml). The engine refuses a key it already has (a modal error window), so a (colour, revision) is written and
    /// loaded once per game run and a different name in the same colour takes the next revision. The file name carries the game run's start.
    /// </summary>
    public const string NamesFolder = "Mods/kdcmp/Localization";

    public static string NamesFileName(string runStamp, int slot, int rev) =>
        $"kct_n_{runStamp}_{ColourLetters[slot - 1]}{rev}.xml";

    public static string NamesXml(int slot, int rev, string name)
    {
        string text = System.Security.SecurityElement.Escape(SanitizeLabel(name)) ?? "player";
        var sb = new StringBuilder("<Table>\n");
        for (int f = 0; f < Frames; f++)
            sb.Append("<Row><Cell>ui_maplegend_").Append(MapName(PictureCode(slot, f, rev))).Append("</Cell><Cell>").Append(text).Append("</Cell><Cell>").Append(text).Append("</Cell></Row>\n");
        return sb.Append("</Table>\n").ToString();
    }

    /// <summary>"Log Started at 2026-10-10  20:06:21" -> "20261010-200621": the game run a loaded key belongs to (null when not found).</summary>
    public static string? RunStamp(IEnumerable<string> firstLines)
    {
        foreach (var l in firstLines)
        {
            var m = System.Text.RegularExpressions.Regex.Match(l, @"^Log Started at (\d{4})-(\d\d)-(\d\d)\s+(\d\d):(\d\d):(\d\d)");
            if (m.Success) return $"{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[3].Value}-{m.Groups[4].Value}{m.Groups[5].Value}{m.Groups[6].Value}";
        }
        return null;
    }

    /// <summary>
    /// Which revision a colour's name uses in this game run: the revision that already carries this name, else the next free one; the last
    /// revision's name stays for any further name (<paramref name="isNew"/> false then: nothing more can be loaded, the old text shows).
    /// <paramref name="taken"/> = the names the colour's revisions already carry, in revision order.
    /// </summary>
    public static int RevFor(IReadOnlyList<string> taken, string name, out bool isNew)
    {
        for (int r = 0; r < taken.Count; r++) if (taken[r] == name) { isNew = false; return r; }
        if (taken.Count < Revisions) { isNew = true; return taken.Count; }
        isNew = false;
        return Revisions - 1;
    }

    // ------------------------------------------------------------------ the name

    public const int LabelMax = 24;

    /// <summary>A player's name as it is logged on a marker line: printable characters only, at most 24, never empty.</summary>
    public static string SanitizeLabel(string? name)
    {
        var sb = new StringBuilder();
        foreach (var r in (name ?? "").EnumerateRunes())
        {
            if (sb.Length >= LabelMax) break;
            var cat = Rune.GetUnicodeCategory(r);
            if (r.Value < 0x20 || r.Value == 0x7F || cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) continue;
            if (r.Value == '"' || r.Value == '\\') continue;
            if (sb.Length + r.Utf16SequenceLength > LabelMax) break;
            sb.Append(r.ToString());
        }
        string t = sb.ToString().Trim();
        return t.Length == 0 ? "player" : t;
    }
}
