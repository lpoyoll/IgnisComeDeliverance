// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;

namespace KcdMp.Client.Tests;

/// <summary>WO-171 M: the partner map marker's rules -- colours, the eight facings, the throttles, the name, the pictures' manifest.</summary>
public class Wo171Tests
{
    // ------------------------------------------------------------------ the slot table

    [Fact]
    public void The_host_is_slot_1_and_the_others_take_2_to_4_in_the_order_they_appear()
    {
        var t = new Wo171Rules.SlotTable();
        Assert.Equal(1, t.Slot("Host", isHost: true));
        Assert.Equal(2, t.Slot("Ann", false));
        Assert.Equal(3, t.Slot("Bob", false));
        Assert.Equal(4, t.Slot("Cy", false));
        Assert.Equal(0, t.Slot("Di", false));         // a fifth has no colour of his own: the companion icon
        Assert.Equal(2, t.Slot("Ann", false));        // asking again changes nothing
    }

    [Fact]
    public void A_rejoin_with_a_new_ghost_id_keeps_its_colour_because_the_name_is_the_key()
    {
        var t = new Wo171Rules.SlotTable();
        t.Slot("Host", true);
        Assert.Equal(2, t.Slot("Ann", false));
        Assert.Equal(3, t.Slot("Bob", false));
        // Ann crashes and comes back: same name -> still purple (2), Bob still blue (3), and a newcomer takes the free slot 4
        Assert.Equal(2, t.Slot("Ann", false));
        Assert.Equal(3, t.Slot("Bob", false));
        Assert.Equal(4, t.Slot("Cy", false));
    }

    [Fact]
    public void On_the_hosts_own_machine_the_joiners_never_take_slot_1()
    {
        var t = new Wo171Rules.SlotTable();     // this machine is the host: it never asks for itself
        Assert.Equal(2, t.Slot("Ann", false));
        Assert.Equal(3, t.Slot("Bob", false));
        Assert.Equal(4, t.Slot("Cy", false));
        Assert.Equal(0, t.Slot("Di", false));   // red stays the host's
    }

    [Fact]
    public void Four_synthetic_peers_get_four_colours_and_a_reset_forgets_them()
    {
        var t = new Wo171Rules.SlotTable();
        int[] got = { t.Slot("H", true), t.Slot("P2", false), t.Slot("P3", false), t.Slot("P4", false) };
        Assert.Equal(new[] { 1, 2, 3, 4 }, got);
        Assert.Equal(new[] { "red", "purple", "blue", "green" }, got.Select(s => Wo171Rules.ColourNames[s - 1]));
        t.Reset();
        Assert.Equal(0, t.Count);
        Assert.Equal(2, t.Slot("P3", false));
    }

    // ------------------------------------------------------------------ the facing

    private static double Rad(double compassHeadingDeg) => -compassHeadingDeg * Math.PI / 180.0;   // the engine's yaw for a compass heading

    [Theory]
    [InlineData(0, 0)] [InlineData(45, 1)] [InlineData(90, 2)] [InlineData(135, 3)]
    [InlineData(180, 4)] [InlineData(225, 5)] [InlineData(270, 6)] [InlineData(315, 7)] [InlineData(359, 0)]
    public void All_eight_headings_pick_their_octant(double heading, int octant) =>
        Assert.Equal(octant, Wo171Rules.Octant(Rad(heading)));

    [Theory]
    [InlineData(22.4, 0)] [InlineData(22.6, 1)] [InlineData(67.4, 1)] [InlineData(67.6, 2)]
    [InlineData(337.4, 7)] [InlineData(337.6, 0)] [InlineData(202.4, 4)] [InlineData(202.6, 5)]
    public void The_boundaries_lie_22_and_a_half_degrees_from_each_centre(double heading, int octant) =>
        Assert.Equal(octant, Wo171Rules.Octant(Rad(heading)));

    [Fact]
    public void The_engines_yaw_turns_counter_clockwise_so_a_positive_yaw_is_a_left_turn_on_the_compass()
    {
        Assert.Equal(7, Wo171Rules.Octant(Math.PI / 4));      // +45 deg yaw = facing north-west
        Assert.Equal(2, Wo171Rules.Octant(-Math.PI / 2));     // -90 deg yaw = facing east
        Assert.Equal(4, Wo171Rules.Octant(Math.PI));          // behind
        Assert.Equal(0, Wo171Rules.Octant(double.NaN));       // garbage = north, never an exception
    }

    [Fact]
    public void Offset_rotates_by_whole_steps_and_flip_mirrors_the_turning()
    {
        for (int o = 0; o < 8; o++)
        {
            double y = Rad(o * 45);
            Assert.Equal((o + 3) % 8, Wo171Rules.Octant(y, offset: 3));
            Assert.Equal((8 - o) % 8, Wo171Rules.Octant(y, 0, flip: true));
            Assert.Equal(((8 - o) % 8 + 2) % 8, Wo171Rules.Octant(y, 2, flip: true));   // flip first, then the offset
            Assert.Equal(o, Wo171Rules.Octant(y, 0, false));
        }
        Assert.Equal(1, Wo171Rules.ApplyOffsetFlip(0, 1, false));
        Assert.Equal(7, Wo171Rules.ApplyOffsetFlip(0, 7, false));
    }

    [Fact]
    public void A_circle_walked_clockwise_turns_the_picture_through_all_eight_frames_once()
    {
        var frames = new List<int>();
        for (int h = 0; h < 360; h += 45) frames.Add(Wo171Rules.FrameOfOctant(Wo171Rules.Octant(Rad(h))));
        Assert.Equal(8, frames.Distinct().Count());
        Assert.Equal(4, frames[0]);   // facing north shows the back (frame 4); facing south the front (frame 0)
        Assert.Equal(0, Wo171Rules.FrameOfOctant(Wo171Rules.Octant(Rad(180))));
    }

    [Fact]
    public void A_heading_on_a_boundary_does_not_flicker()
    {
        int raw = Wo171Rules.RawOctant(20);                       // 0 (north)
        var seq = new List<int>();
        foreach (double h in new[] { 22.0, 23.0, 22.0, 24.0, 21.0, 26.0, 28.0, 30.0 })
            seq.Add(raw = Wo171Rules.PickOctant(raw, h));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 1, 1 }, seq);      // it holds 5 degrees past the edge, then turns once
        Assert.Equal(0, Wo171Rules.PickOctant(-1, 10));
        Assert.Equal(7, Wo171Rules.PickOctant(0, 330));           // far enough past the edge: it turns, across the wrap too
        Assert.Equal(7, Wo171Rules.PickOctant(7, 338));           // 338 is 0.5 degree into north: the picture holds across the wrap
    }

    // ------------------------------------------------------------------ the pictures

    [Fact]
    public void The_codes_and_names_cover_128_pictures_and_match_the_native_table()
    {
        var names = Enumerable.Range(1, 128).Select(Wo171Rules.PictureName).ToList();
        Assert.Equal(128, names.Distinct().Count());
        Assert.Equal("kct_r0", names[0]); Assert.Equal("kct_r7", names[7]);
        Assert.Equal("kct_p0", names[8]); Assert.Equal("kct_b3", names[8 * 2 + 3]); Assert.Equal("kct_g7", names[31]);
        Assert.Equal("kct_r0b", names[32]); Assert.Equal("kct_p3c", names[64 + 8 + 3]); Assert.Equal("kct_g7d", names[127]);
        Assert.Equal("-", Wo171Rules.PictureName(0)); Assert.Equal("-", Wo171Rules.PictureName(129));
        Assert.Equal(1, Wo171Rules.PictureCode(1, 0)); Assert.Equal(32, Wo171Rules.PictureCode(4, 7)); Assert.Equal(0, Wo171Rules.PictureCode(0, 3)); Assert.Equal(0, Wo171Rules.PictureCode(5, 3));
        Assert.Equal(33, Wo171Rules.PictureCode(1, 0, 1)); Assert.Equal(128, Wo171Rules.PictureCode(4, 7, 3)); Assert.Equal(0, Wo171Rules.PictureCode(1, 0, 4));
    }

    [Fact]
    public void Styles_pick_the_picture()
    {
        Assert.Equal(Wo171Rules.PictureCode(2, Wo171Rules.FrameOfOctant(3)), Wo171Rules.CodeFor(Wo171Rules.Style.Rotating, 2, 3, 0));
        Assert.Equal(Wo171Rules.PictureCode(2, Wo171Rules.FrameOfOctant(3), 2), Wo171Rules.CodeFor(Wo171Rules.Style.Rotating, 2, 3, 0, 2));   // the third revision of the same picture
        Assert.Equal(Wo171Rules.PictureCode(2, Wo171Rules.FrameOfOctant(0)), Wo171Rules.CodeFor(Wo171Rules.Style.Flat, 2, 3, 0));   // flat = the north-facing one
        Assert.Equal(0, Wo171Rules.CodeFor(Wo171Rules.Style.Companion, 2, 3, 0));
        Assert.Equal(0, Wo171Rules.CodeFor(Wo171Rules.Style.Rotating, 0, 3, 0));   // no slot = the game's own icon
        Assert.True(Wo171Rules.TryParseStyle("Flat", out var st) && st == Wo171Rules.Style.Flat);
        Assert.False(Wo171Rules.TryParseStyle("big", out _));
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "VERSION"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void The_pak_source_holds_all_128_pictures_with_their_variants_and_the_manifest_matches_the_names()
    {
        string dir = Path.Combine(RepoRoot(), "docs", "branding", "pak-source", "Libs", "UI", "Textures", "Icons", "Map");
        var want = Wo171Rules.ManifestFiles().ToList();
        Assert.Equal(256, want.Count);                      // 32 pictures (4 colours x 8 directions) x 4 revisions x 2 sizes (compass, map)
        Assert.Equal(256, want.Distinct().Count());
        foreach (var f in want)
        {
            Assert.True(File.Exists(Path.Combine(dir, f)), f + " is in the pak source");
            Assert.True(File.Exists(Path.Combine(dir, f.Replace("_icon.dds", "_undiscovered_icon.dds"))), f + " has its undiscovered variant");
            Assert.True(File.Exists(Path.Combine(dir, f.Replace("_icon.dds", "_sh_icon.dds"))), f + " has its shadow");
        }
        // only our files are in the pak's icon folder, and all begin kct_ (none can shadow a game file)
        Assert.All(Directory.GetFiles(dir), f => Assert.StartsWith("kct_", Path.GetFileName(f)));
        Assert.Equal(256 * 3, Directory.GetFiles(dir).Length);
        // every picture file is DXT5, 64 x 64 for the compass and 128 x 128 for the map (the M names): the DDS header says so
        foreach (var f in want)
        {
            int px = f.EndsWith("M_icon.dds", StringComparison.Ordinal) ? 128 : 64;
            var h = File.ReadAllBytes(Path.Combine(dir, f)).AsSpan(0, 128);
            Assert.Equal((byte)'D', h[0]);
            Assert.Equal(px, BitConverter.ToInt32(h[12..16]));    // height
            Assert.Equal(px, BitConverter.ToInt32(h[16..20]));    // width
            Assert.Equal("DXT5", System.Text.Encoding.ASCII.GetString(h[84..88]));
        }
        // a revision is the same art under another name
        Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "kct_p3_icon.dds")), File.ReadAllBytes(Path.Combine(dir, "kct_p3c_icon.dds")));
    }

    // ------------------------------------------------------------------ the throttles

    [Fact]
    public void A_marker_moves_at_most_once_a_second_after_more_than_two_metres()
    {
        var g = new Wo171Rules.MoveGate();
        Assert.True(g.Due(0, 0, 0));                 // the first set
        g.Sent(0, 0, 0, counted: false);
        Assert.False(g.Due(5, 0, 500), "too soon (a second apart)");
        Assert.False(g.Due(1.9f, 0, 1500), "not far enough");
        Assert.False(g.Due(2.0f, 0, 1500), "exactly two metres is not more than two");
        Assert.True(g.Due(2.1f, 0, 1500));
    }

    [Fact]
    public void No_more_than_sixty_moves_go_out_in_a_minute()
    {
        var g = new Wo171Rules.MoveGate();
        g.Sent(0, 0, 0, false);
        long t = 0; float x = 0; int sent = 0;
        for (int sec = 1; sec <= 120; sec++)         // a partner that moves 5 m every second for two minutes
        {
            t = sec * 1000L; x += 5;
            if (g.Due(x, 0, t)) { g.Sent(x, 0, t, true); sent++; }
        }
        Assert.InRange(sent, 60, 120);               // never more than 60 in any minute: 60 in the first, then the window slides
        Assert.True(g.InLastMinute(t) <= 60);
        // and the same partner in the very first minute alone
        var g2 = new Wo171Rules.MoveGate();
        g2.Sent(0, 0, 0, false);
        int firstMinute = 0; float x2 = 0;
        for (int sec = 1; sec <= 59; sec++) { x2 += 5; if (g2.Due(x2, 0, sec * 1000L)) { g2.Sent(x2, 0, sec * 1000L, true); firstMinute++; } }
        Assert.True(firstMinute <= 60);
        Assert.Equal(59, firstMinute);
    }

    [Fact]
    public void A_picture_changes_only_when_the_octant_did_and_at_most_four_times_a_second()
    {
        Assert.False(Wo171Rules.TurnDue(5, 5, 0, 10_000), "same picture: nothing");
        Assert.False(Wo171Rules.TurnDue(5, 6, 1000, 1200), "changed, but 200 ms after the last change");
        Assert.True(Wo171Rules.TurnDue(5, 6, 1000, 1250));
        Assert.True(Wo171Rules.TurnDue(-1, 6, long.MinValue / 2, 0), "the first picture");
    }

    // ------------------------------------------------------------------ the name

    [Fact]
    public void A_label_is_printable_and_at_most_24_characters()
    {
        Assert.Equal("player", Wo171Rules.SanitizeLabel(null));
        Assert.Equal("player", Wo171Rules.SanitizeLabel("   "));
        Assert.Equal("Ann", Wo171Rules.SanitizeLabel("  Ann  "));
        Assert.Equal("AB", Wo171Rules.SanitizeLabel("A\u0007\nB‎"));                       // control and format characters are dropped
        Assert.Equal("Ann Bob", Wo171Rules.SanitizeLabel("Ann \"\\Bob"));                      // quotes and backslashes never reach a log line
        string longName = new('x', 60);
        Assert.Equal(24, Wo171Rules.SanitizeLabel(longName).Length);
        Assert.Equal("Dragan Žižek", Wo171Rules.SanitizeLabel("Dragan Žižek"));                 // letters of any script stay
        Assert.True(Wo171Rules.SanitizeLabel(string.Concat(Enumerable.Repeat("\U0001F600", 30))).Length <= 24);       // astral characters are never cut in half
    }

    // ------------------------------------------------------------------ the hover names

    [Fact]
    public void A_names_file_gives_the_eight_pictures_of_one_colour_and_revision_the_players_name_as_the_tooltip_key()
    {
        var rows = Wo171Rules.NamesXml(2, 0, "LongPlayerNam").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("<Table>", rows[0]);
        Assert.Equal("</Table>", rows[^1]);
        Assert.Equal(8, rows.Length - 2);
        for (int f = 0; f < 8; f++)
            Assert.Contains($"<Row><Cell>ui_maplegend_kct_p{f}M</Cell><Cell>LongPlayerNam</Cell><Cell>LongPlayerNam</Cell></Row>", rows);
        var rev2 = Wo171Rules.NamesXml(3, 2, "Ann").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("<Row><Cell>ui_maplegend_kct_b5cM</Cell><Cell>Ann</Cell><Cell>Ann</Cell></Row>", rev2);   // blue, frame 5, revision c
    }

    [Fact]
    public void A_name_with_xml_characters_is_escaped()
    {
        Assert.Contains("A&lt;B&gt;&amp;C", Wo171Rules.NamesXml(3, 0, "A<B>&C"));
    }

    [Fact]
    public void Two_files_never_share_a_key_so_the_engine_never_sees_a_duplicate()
    {
        var keys = new HashSet<string>();
        for (int slot = 1; slot <= 4; slot++)
            for (int rev = 0; rev < Wo171Rules.Revisions; rev++)
                foreach (var row in Wo171Rules.NamesXml(slot, rev, "x").Split('\n').Where(r => r.StartsWith("<Row>")))
                    Assert.True(keys.Add(row.Split("</Cell>")[0]), "duplicate key " + row);
        Assert.Equal(128, keys.Count);
        Assert.All(keys, k => Assert.EndsWith("M", k));   // the map's names
        Assert.Equal("kct_n_20261010-200621_p1.xml", Wo171Rules.NamesFileName("20261010-200621", 2, 1));
    }

    [Fact]
    public void The_game_runs_start_comes_from_the_log_header()
    {
        Assert.Equal("20261010-200621", Wo171Rules.RunStamp(new[] { "Connecting messages...", "", "Log Started at 2026-10-10  20:06:21", "Build info:" }));
        Assert.Null(Wo171Rules.RunStamp(new[] { "nothing here" }));
    }

    [Fact]
    public void A_new_name_takes_the_next_revision_the_same_name_keeps_its_and_a_fifth_keeps_the_last_ones_text()
    {
        var taken = new List<string>();
        Assert.Equal(0, Wo171Rules.RevFor(taken, "Ann", out bool isNew)); Assert.True(isNew); taken.Add("Ann");
        Assert.Equal(0, Wo171Rules.RevFor(taken, "Ann", out isNew)); Assert.False(isNew);            // a rejoin: no new key
        Assert.Equal(1, Wo171Rules.RevFor(taken, "Bob", out isNew)); Assert.True(isNew); taken.Add("Bob");
        Assert.Equal(0, Wo171Rules.RevFor(taken, "Ann", out isNew)); Assert.False(isNew);            // Ann is back: her revision again
        taken.Add("Cy"); taken.Add("Di");
        Assert.Equal(3, Wo171Rules.RevFor(taken, "Ed", out isNew)); Assert.False(isNew);             // four names carried: nothing more can be loaded
        Assert.Equal(2, Wo171Rules.RevFor(taken, "Cy", out isNew)); Assert.False(isNew);
    }
}
