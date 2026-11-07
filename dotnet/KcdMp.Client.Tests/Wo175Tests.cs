// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-175 S4: the stuck-quest-fight check and repair against SYNTHETIC saves built here (a real save carries the
/// writing machine's account name and is never a fixture). The concept node sits at its real path
/// (01f4/01f6/730a/01f2/036b/20a9) behind a stored (incompressible) block, so the edit lands in a later block.
/// </summary>
public class Wo175Tests
{
    // ---------------------------------------------------------------- fixture builder

    private static byte[] Tlv(ushort tag, byte[] payload)
    {
        var b = new byte[6 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(b, tag);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(2), (uint)payload.Length);
        payload.CopyTo(b, 6);
        return b;
    }
    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }

    private static readonly byte[] Opaque = [0x14, 0x70, 0xF4, 0xAC, 0xDC, 0x62, 0x9E, 0x06];
    private static readonly byte[] Desc = Encoding.UTF8.GetBytes("<C_SaveGameDescription SaveId=\"7\" LevelName=\"trosecko\" />\n\0");
    private static readonly byte[] Tail = Enumerable.Range(0, 44).Select(i => (byte)(i * 3)).ToArray();

    private const string Started = "<_skirmishResult value=\"Started\" />";
    private const string Fled = "<_skirmishResult value=\"Fled\"    />";

    /// <summary>A quest fight module as the game writes it (tabs, one element per line).</summary>
    private static string Fight(string name, string ctrl, string res, bool resultFirst = false)
    {
        string c = $"\t\t\t\t\t\t<_skirmishControler value=\"{ctrl}\" />\n";
        string r = $"\t\t\t\t\t\t<_skirmishResult value=\"{res}\" />\n";
        return $"\t\t\t\t<_{name} Version=\"ver_01_05_05\">\n\t\t\t\t\t<Nodes>\n" +
               "\t\t\t\t\t\t<_fightconfiguration_indifference Version=\"ver_01_05_05\">\n\t\t\t\t\t\t\t<Nodes>\n" +
               "\t\t\t\t\t\t\t\t<_timer12 value=\"Finished\" />\n\t\t\t\t\t\t\t</Nodes>\n\t\t\t\t\t\t</_fightconfiguration_indifference>\n" +
               (resultFirst ? r + c : c + r) +
               $"\t\t\t\t\t</Nodes>\n\t\t\t\t</_{name}>\n";
    }

    private static string Xml(params string[] modules) =>
        "<Roots>\n\t<_konecZabomysichValek Version=\"ver_01_05_05\">\n\t\t<Nodes>\n\t\t\t<_hibernovana_cast>\n\t\t\t\t<!-- a comment > with a bracket -->\n" +
        "\t\t\t\t<_objectiveVisual10>\n\t\t\t\t\t<Logs>\n\t\t\t\t\t\t<Started UpdateTime=\"54508044\" note=\"a > b\" />\n\t\t\t\t\t</Logs>\n\t\t\t\t</_objectiveVisual10>\n" +
        string.Concat(modules) +
        "\t\t\t</_hibernovana_cast>\n\t\t</Nodes>\n\t</_konecZabomysichValek>\n</Roots>\n";

    private static byte[] Payload(string xml) => Cat("ConceptState\0"u8.ToArray(), Opaque, [0], Encoding.UTF8.GetBytes(xml));

    private static byte[] Filler(int n, int seed) { var b = new byte[n]; new Random(seed).NextBytes(b); return b; }

    private static byte[] Stream(byte[]? concept) => Cat(U32(23), Tlv(0x01F4, Cat(
        Tlv(0x01F5, Filler(40000, 1)),   // incompressible: a stored block before the concept node
        concept is null ? Tlv(0x01F7, Filler(10, 2)) : Tlv(0x01F6, Tlv(0x730A, Tlv(0x01F2, Tlv(0x036B, Tlv(0x20A9, concept))))),
        Tlv(0x01F8, Filler(5000, 3)))));

    private static byte[] Save(string xml) => WhsSave.Deflate(Desc, Stream(Payload(xml)), Tail);

    private const string Path3 = "konecZabomysichValek/hibernovana_cast/souboj_na_poli";

    // ---------------------------------------------------------------- the check

    [Fact]
    public void The_stuck_pair_is_found_with_its_quest_path_and_offset()
    {
        var f = Save(Xml(Fight("souboj_na_poli", "Done", "Started")));
        var r = Wo175SaveHealth.Check(f);
        Assert.True(r.Readable, r.Why);
        var s = Assert.Single(r.Stuck);
        Assert.Equal(Path3, s.QuestPath);
        var raw = WhsSave.Inflate(f).Raw;
        Assert.Equal(Started, Encoding.ASCII.GetString(raw, s.Offset, Started.Length));
        Assert.Equal(r.Stuck, Wo175SaveHealth.FindStuckFights(f));
    }

    [Fact]
    public void An_active_controller_is_a_fight_in_progress_and_never_stuck()
    {
        var f = Save(Xml(Fight("souboj_na_poli", "Active", "Started")));
        var r = Wo175SaveHealth.Check(f);
        Assert.True(r.Readable, r.Why);
        Assert.Empty(r.Stuck);
        Assert.Equal(f, Wo175SaveHealth.Repair(f, out var none));   // Repair leaves it alone
        Assert.Empty(none);
    }

    [Theory]
    [InlineData("Won")]
    [InlineData("Lost")]
    [InlineData("Fled")]
    public void A_done_fight_with_a_verdict_is_healthy(string verdict)
    {
        var r = Wo175SaveHealth.Check(Save(Xml(Fight("souboj_na_poli", "Done", verdict))));
        Assert.True(r.Readable, r.Why);
        Assert.Empty(r.Stuck);
    }

    [Fact]
    public void The_pair_is_found_in_either_sibling_order()
    {
        var s = Assert.Single(Wo175SaveHealth.FindStuckFights(Save(Xml(Fight("souboj_na_poli", "Done", "Started", resultFirst: true)))));
        Assert.Equal(Path3, s.QuestPath);
    }

    [Fact]
    public void A_done_controller_and_a_started_result_under_different_nodes_are_not_a_pair()
    {
        // the controller's module is Done, but the Started result belongs to a nested module of its own (Active there)
        string xml = Xml("\t\t\t\t<_outer>\n\t\t\t\t\t<Nodes>\n\t\t\t\t\t\t<_skirmishControler value=\"Done\" />\n" +
                         Fight("inner", "Active", "Started") + "\t\t\t\t\t</Nodes>\n\t\t\t\t</_outer>\n");
        var r = Wo175SaveHealth.Check(Save(xml));
        Assert.True(r.Readable, r.Why);
        Assert.Empty(r.Stuck);
    }

    // ---------------------------------------------------------------- the repair

    [Fact]
    public void The_repair_is_a_same_length_edit_and_every_other_stream_byte_stays()
    {
        var f = Save(Xml(Fight("souboj_na_poli", "Done", "Started")));
        var before = WhsSave.Inflate(f).Raw;
        var o = Wo175SaveHealth.Repair(f, out var fixedFights);
        var s = Assert.Single(fixedFights);
        var after = WhsSave.Inflate(o).Raw;
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
            if (i < s.Offset || i >= s.Offset + Started.Length) Assert.True(before[i] == after[i], $"byte {i} changed");
        Assert.Equal(Fled, Encoding.ASCII.GetString(after, s.Offset, Fled.Length));
        Assert.Equal(Started.Length, Fled.Length);
        // the concept header (ConceptState, the 8 opaque bytes, the NUL) is untouched
        var n = WhsSave.PathGet(after, Wo175SaveHealth.ConceptPath)!.Value;
        Assert.Equal(Payload("").AsSpan(0, 22).ToArray(), after.AsSpan(n.PayloadOff, 22).ToArray());
    }

    [Fact]
    public void The_repaired_file_verifies_keeps_its_description_and_tail_and_checks_clean()
    {
        var o = Wo175SaveHealth.Repair(Save(Xml(Fight("souboj_na_poli", "Done", "Started"))), out _);
        Assert.True(WhsSave.VerifyFooter(o).Ok);
        var c = WhsSave.Inflate(o);
        Assert.Equal(Desc, c.DescBytes);
        Assert.Equal(Tail, c.FooterTail);
        var r = Wo175SaveHealth.Check(o);
        Assert.True(r.Readable, r.Why);
        Assert.Empty(r.Stuck);
        Assert.Contains("value=\"Fled\"", Encoding.ASCII.GetString(c.Raw));
    }

    [Fact]
    public void Every_stuck_fight_is_repaired_and_an_active_one_beside_them_is_kept()
    {
        var f = Save(Xml(Fight("souboj_na_poli", "Done", "Started"), Fight("rvacka_u_mlyna", "Active", "Started"), Fight("duel_v_lese", "Done", "Started")));
        var found = Wo175SaveHealth.FindStuckFights(f);
        Assert.Equal(new[] { Path3, "konecZabomysichValek/hibernovana_cast/duel_v_lese" }, found.Select(x => x.QuestPath));
        var o = Wo175SaveHealth.Repair(f, out var fixedFights);
        Assert.Equal(found, fixedFights);
        var text = Encoding.ASCII.GetString(WhsSave.Inflate(o).Raw);
        Assert.Equal(2, CountOf(text, Fled));
        Assert.Equal(1, CountOf(text, Started));   // the Active fight's
        Assert.Empty(Wo175SaveHealth.FindStuckFights(o));
    }

    private static int CountOf(string s, string t) { int n = 0, i = 0; while ((i = s.IndexOf(t, i, StringComparison.Ordinal)) >= 0) { n++; i += t.Length; } return n; }

    [Fact]
    public void A_healthy_save_comes_back_unchanged()
    {
        var f = Save(Xml(Fight("souboj_na_poli", "Done", "Won")));
        var o = Wo175SaveHealth.Repair(f, out var fixedFights);
        Assert.Empty(fixedFights);
        Assert.Equal(f, o);
        Assert.NotSame(f, o);
    }

    [Fact]
    public void A_save_whose_footer_does_not_verify_is_refused()
    {
        var f = Save(Xml(Fight("souboj_na_poli", "Done", "Started")));
        f[f.Length - 60] ^= 1;   // one MD5 byte
        Assert.Throws<InvalidDataException>(() => Wo175SaveHealth.Repair(f, out _));
        var r = Wo175SaveHealth.Check(f);   // still readable, the stuck fight still reported
        Assert.True(r.Readable);
        Assert.Contains("footer", r.Why);
        Assert.Single(r.Stuck);
    }

    [Fact]
    public void The_fled_element_is_padded_to_the_original_length()
    {
        Assert.Equal(Fled, Encoding.ASCII.GetString(Wo175SaveHealth.FledElement(Started.Length)));
        Assert.Equal("<_skirmishResult value=\"Fled\"/>", Encoding.ASCII.GetString(Wo175SaveHealth.FledElement(31)));
        Assert.Throws<InvalidDataException>(() => Wo175SaveHealth.FledElement(30));
    }

    // ---------------------------------------------------------------- not a save

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_non_save_is_not_readable_and_never_throws(int kind)
    {
        byte[] b = kind switch { 0 => [], 1 => Filler(5000, 9), _ => Encoding.UTF8.GetBytes("<Roots><_x><Nodes><_skirmishControler value=\"Done\" /></Nodes></_x></Roots>") };
        var r = Wo175SaveHealth.Check(b);
        Assert.False(r.Readable);
        Assert.Empty(r.Stuck);
        Assert.Empty(Wo175SaveHealth.FindStuckFights(b));
        Assert.Throws<InvalidDataException>(() => Wo175SaveHealth.Repair(b, out _));
    }

    [Fact]
    public void A_save_without_the_concept_node_is_not_readable()
    {
        var r = Wo175SaveHealth.Check(WhsSave.Deflate(Desc, Stream(null), Tail));
        Assert.False(r.Readable);
        Assert.Contains("ConceptModule", r.Why);
    }

    [Fact]
    public void A_concept_node_without_its_header_or_with_broken_xml_is_not_readable()
    {
        var noHeader = WhsSave.Deflate(Desc, Stream(Encoding.UTF8.GetBytes(Xml(Fight("souboj_na_poli", "Done", "Started")))), Tail);
        Assert.False(Wo175SaveHealth.Check(noHeader).Readable);
        string full = Xml(Fight("souboj_na_poli", "Done", "Started"));
        foreach (var broken in new[] { full[..(full.Length / 2)], full.Replace("</_hibernovana_cast>", "</_other>"), full + "<_x unterminated" })
        {
            var r = Wo175SaveHealth.Check(Save(broken));
            Assert.False(r.Readable);
            Assert.Contains("malformed", r.Why);
            Assert.Empty(r.Stuck);
        }
    }

    // ---------------------------------------------------------------- the name

    [Fact]
    public void The_repaired_name_is_new_and_avoids_every_taken_name()
    {
        Assert.Equal("autosave335_fixed.whs", Wo175SaveHealth.RepairedName("autosave335", ["autosave335.whs", "autosave336.whs"]));
        Assert.Equal("autosave335_fixed.whs", Wo175SaveHealth.RepairedName("autosave335.whs", []));
        Assert.Equal("autosave335_fixed2.whs", Wo175SaveHealth.RepairedName("autosave335.WHS", ["AUTOSAVE335_FIXED.whs"]));
        Assert.Equal("autosave335_fixed4.whs", Wo175SaveHealth.RepairedName("autosave335",
            ["autosave335_fixed.whs", "autosave335_fixed2.whs", "autosave335_fixed3.whs", "autosave335_fixed5.whs"]));
        Assert.Equal("quicksave2_fixed2.whs", Wo175SaveHealth.RepairedName("quicksave2", [Path.Combine("C:", "x", "quicksave2_fixed.whs")]));
    }

    // ---------------------------------------------------------------- the CLI

    [Fact]
    public void The_cli_writes_the_repair_next_to_the_original_and_never_in_place()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kcdmp-wo175-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string src = Path.Combine(dir, "autosave335.whs");
            var f = Save(Xml(Fight("souboj_na_poli", "Done", "Started")));
            File.WriteAllBytes(src, f);
            File.WriteAllBytes(Path.Combine(dir, "autosave335_fixed.whs"), [1, 2, 3]);   // taken: the next name is used

            var w = new StringWriter();
            Assert.Equal(1, Wo175SaveHealth.RunCli(["--w175", "check", src], w));
            Assert.StartsWith("W175 {", w.ToString());
            Assert.Contains("souboj_na_poli", w.ToString());

            w = new StringWriter();
            Assert.Equal(0, Wo175SaveHealth.RunCli(["--w175", "repair", src], w));
            Assert.Contains("\"name\":\"autosave335_fixed2.whs\"", w.ToString());
            Assert.Equal(f, File.ReadAllBytes(src));                                   // the original is untouched
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(dir, "autosave335_fixed.whs")));
            string outp = Path.Combine(dir, "autosave335_fixed2.whs");
            Assert.Empty(Wo175SaveHealth.FindStuckFights(File.ReadAllBytes(outp)));
            Assert.False(File.Exists(outp + ".part"));

            w = new StringWriter();
            Assert.Equal(0, Wo175SaveHealth.RunCli(["--w175", "check", outp], w));
            w = new StringWriter();
            Assert.Equal(1, Wo175SaveHealth.RunCli(["--w175", "repair", outp], w));   // nothing to repair: nothing written
            Assert.Contains("nothing to repair", w.ToString());
            Assert.Equal(3, Directory.GetFiles(dir).Length);                            // the source, the taken name, the repair
            Assert.Equal(2, Wo175SaveHealth.RunCli(["--w175", "check", Path.Combine(dir, "missing.whs")], new StringWriter()));
            Assert.Equal(2, Wo175SaveHealth.RunCli(["--w175", "frobnicate"], new StringWriter()));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---------------------------------------------------------------- the agent's file handling (GameBridge.Wo175)

    [Fact]
    public void Agent_StuckSave_RepairedCopyBeside_OriginalByteIdentical()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wo175agent-" + Guid.NewGuid().ToString("N"), "playline4");
        Directory.CreateDirectory(dir);
        try
        {
            string orig = Path.Combine(dir, "autosave335.whs");
            var bytes = Save(Xml(Fight("souboj_na_poli", "Done", "Started")));
            File.WriteAllBytes(orig, bytes);
            var r = GameBridge.Wo175RepairInto(orig);
            Assert.Equal("repaired", r.Kind);
            Assert.Equal(Path.Combine(dir, "autosave335_fixed.whs"), r.NewPath);
            Assert.Equal(bytes, File.ReadAllBytes(orig));
            Assert.Empty(Wo175SaveHealth.FindStuckFights(File.ReadAllBytes(r.NewPath!)));
            Assert.False(File.Exists(r.NewPath + ".part"));
            var again = GameBridge.Wo175RepairInto(orig);   // a second check never overwrites the first copy
            Assert.Equal(Path.Combine(dir, "autosave335_fixed2.whs"), again.NewPath);
            Assert.Equal("ok", GameBridge.Wo175RepairInto(r.NewPath!).Kind);
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true); }
    }

    [Fact]
    public void Agent_HealthySave_NothingWritten()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wo175agent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string f = Path.Combine(dir, "autosave322.whs");
            File.WriteAllBytes(f, Save(Xml(Fight("souboj_na_poli", "Active", "Started"))));
            Assert.Equal("ok", GameBridge.Wo175RepairInto(f).Kind);
            Assert.Single(Directory.EnumerateFiles(dir));
            File.WriteAllBytes(f, [1, 2, 3]);
            Assert.Equal("failed", GameBridge.Wo175RepairInto(f).Kind);
            Assert.Equal("failed", GameBridge.Wo175RepairInto(Path.Combine(dir, "missing.whs")).Kind);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
