// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Security.Cryptography;
using KCDMP_launcher.Models;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-175 S2: the rolling saves backups (KCDMP_launcher/Models/Wo175Backup.cs). Temp folders only.</summary>
public sealed class Wo175BackupTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "wo175-" + Guid.NewGuid().ToString("N"));
    private string Saves => Path.Combine(_tmp, "saves");
    private string Root => Path.Combine(_tmp, "backups");
    private static readonly DateTime T0 = new(2026, 10, 11, 14, 30, 0);

    public Wo175BackupTests()
    {
        Directory.CreateDirectory(_tmp);
        Write("playline0/quicksave.whs", 70_000, 1);
        Write("playline0/autosave001.whs", 50_000, 2);
        Write("playline1/autosave002.whs", 1_000, 3);
        Write("profile.dat", 300, 4);
        Write("playline1/deep/nested/thing.bin", 17, 5);
        Directory.CreateDirectory(Path.Combine(Saves, "emptyfolder"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private void Write(string rel, int size, int seed)
    {
        string p = Path.Combine(Saves, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var b = new byte[size];
        new Random(seed).NextBytes(b);
        File.WriteAllBytes(p, b);
    }

    /// <summary>Relative path -> sha256 of every file under <paramref name="dir"/> (the manifest left out).</summary>
    private static Dictionary<string, string> Snapshot(string dir, bool skipManifest = false) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => (Rel: Path.GetRelativePath(dir, f).Replace('\\', '/'), Full: f))
            .Where(f => !(skipManifest && f.Rel == Wo175Backup.ManifestName))
            .ToDictionary(f => f.Rel, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Full))), StringComparer.Ordinal);

    private static void AssertSame(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        Assert.Equal(a.Keys.OrderBy(k => k, StringComparer.Ordinal), b.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var k in a.Keys) Assert.Equal(a[k], b[k]);
    }

    [Fact]
    public void A_backup_copies_every_file_and_folder_and_its_manifest_verifies()
    {
        var before = Snapshot(Saves);
        var r = Wo175Backup.Backup(Saves, Root, T0);

        Assert.True(r.Verified, r.Why);
        Assert.Equal(Path.Combine(Path.GetFullPath(Root), "20261011-143000"), r.Path);
        Assert.Equal(5, r.Files);
        Assert.Equal(70_000 + 50_000 + 1_000 + 300 + 17, r.Bytes);
        AssertSame(before, Snapshot(r.Path!, skipManifest: true));
        Assert.True(Directory.Exists(Path.Combine(r.Path!, "emptyfolder")));
        Assert.Empty(Directory.GetDirectories(Root, "*.part"));

        var lines = File.ReadAllLines(Path.Combine(r.Path!, Wo175Backup.ManifestName));
        Assert.Equal(5, lines.Length);
        Assert.Equal(lines.OrderBy(l => l[66..], StringComparer.Ordinal), lines);
        Assert.Contains(lines, l => l.EndsWith("  playline0/quicksave.whs", StringComparison.Ordinal));
        Assert.All(lines, l => Assert.Matches("^[0-9a-f]{64}  [^\\\\]+$", l));
        Assert.Equal(before["profile.dat"].ToLowerInvariant(), lines.Single(l => l.EndsWith("  profile.dat")).Substring(0, 64));

        var v = Wo175Backup.Verify(r.Path!);
        Assert.True(v.Ok, v.Why);
        Assert.Equal(5, v.Files);
        Assert.StartsWith("WO175-BACKUP made=", r.LogLine);
        Assert.Contains(" files=5 ", r.LogLine);
        Assert.Contains(" verified=yes kept=1 removed=0", r.LogLine);
        AssertSame(before, Snapshot(Saves));   // the source is untouched
    }

    [Fact]
    public void Tampering_with_a_backup_is_detected()
    {
        string b = Wo175Backup.Backup(Saves, Root, T0).Path!;
        string f = Path.Combine(b, "playline0", "quicksave.whs");

        var bytes = File.ReadAllBytes(f);
        bytes[1234] ^= 0x40;
        File.WriteAllBytes(f, bytes);
        Assert.False(Wo175Backup.Verify(b).Ok);
        bytes[1234] ^= 0x40;
        File.WriteAllBytes(f, bytes);
        Assert.True(Wo175Backup.Verify(b).Ok);

        File.WriteAllText(Path.Combine(b, "playline1", "stray.whs"), "x");
        Assert.Contains("not in the manifest", Wo175Backup.Verify(b).Why);
        File.Delete(Path.Combine(b, "playline1", "stray.whs"));

        File.Move(f, f + ".gone");
        Assert.Contains("missing", Wo175Backup.Verify(b).Why);
        File.Move(f + ".gone", f);
        Assert.True(Wo175Backup.Verify(b).Ok);

        string m = Path.Combine(b, Wo175Backup.ManifestName);
        string manifest = File.ReadAllText(m);
        File.WriteAllText(m, manifest.Replace("  profile.dat", "  ../profile.dat"));
        Assert.False(Wo175Backup.Verify(b).Ok);
        File.Delete(m);
        Assert.False(Wo175Backup.Verify(b).Ok);
    }

    [Fact]
    public void Retention_keeps_ten_and_never_the_newest_three_fewer()
    {
        var made = new List<string>();
        for (int i = 0; i < 13; i++)
            made.Add(Wo175Backup.Backup(Saves, Root, T0.AddMinutes(i)).Path!);

        var left = Wo175Backup.List(Root).Select(x => x.Path).ToList();
        Assert.Equal(10, left.Count);
        Assert.Equal(made.Skip(3).Reverse(), left);   // newest first, the three oldest gone

        var r = Wo175Backup.Prune(Root, T0.AddHours(1), keep: 1);
        Assert.Equal(3, r.Kept);
        Assert.Equal(7, r.Removed);
        Assert.Equal(made.Skip(10).Reverse(), Wo175Backup.List(Root).Select(x => x.Path));

        r = Wo175Backup.Prune(Root, T0.AddHours(1), keep: 0);
        Assert.Equal(3, r.Kept);
        Assert.Equal(0, r.Removed);
    }

    [Fact]
    public void Retention_touches_only_stamp_folders_with_a_manifest_and_old_part_leftovers()
    {
        Directory.CreateDirectory(Root);
        string notes = Path.Combine(Root, "my notes");
        string noManifest = Path.Combine(Root, "20200101-000000");      // a stamp name, but no manifest: not ours
        string looksClose = Path.Combine(Root, "20200101-000000-old");
        string oldPart = Path.Combine(Root, "20200101-000000.part");    // a leftover from long ago
        string freshPart = Path.Combine(Root, T0.AddMinutes(-5).ToString("yyyyMMdd-HHmmss") + ".part");
        foreach (var d in new[] { notes, noManifest, looksClose, oldPart, freshPart })
        {
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "keep.txt"), "x");
        }
        File.WriteAllText(Path.Combine(Root, "readme.txt"), "x");
        File.WriteAllText(Path.Combine(looksClose, Wo175Backup.ManifestName), "");

        for (int i = 0; i < 12; i++) Wo175Backup.Backup(Saves, Root, T0.AddMinutes(i));

        Assert.Equal(10, Wo175Backup.List(Root).Count);
        Assert.True(Directory.Exists(notes));
        Assert.True(Directory.Exists(noManifest));
        Assert.True(Directory.Exists(looksClose));
        Assert.True(Directory.Exists(freshPart));
        Assert.True(File.Exists(Path.Combine(Root, "readme.txt")));
        Assert.False(Directory.Exists(oldPart));
    }

    [Fact]
    public void Two_backups_in_the_same_second_get_their_own_folders()
    {
        var a = Wo175Backup.Backup(Saves, Root, T0);
        var b = Wo175Backup.Backup(Saves, Root, T0);
        Assert.True(a.Verified && b.Verified);
        Assert.NotEqual(a.Path, b.Path);
        Assert.EndsWith("20261011-143001", b.Path);
    }

    [Fact]
    public void An_empty_or_missing_saves_folder_is_skipped_not_backed_up()
    {
        string empty = Path.Combine(_tmp, "empty");
        Directory.CreateDirectory(empty);
        var r = Wo175Backup.Backup(empty, Root, T0);
        Assert.True(r.Skipped);
        Assert.False(r.Verified);
        Assert.Null(r.Path);
        r = Wo175Backup.Backup(Path.Combine(_tmp, "nowhere"), Root, T0);
        Assert.True(r.Skipped);
        Assert.False(Directory.Exists(Path.Combine(Root, "20261011-143000")));
    }

    [Fact]
    public void A_backup_folder_inside_the_saves_folder_is_refused()
    {
        var r = Wo175Backup.Backup(Saves, Path.Combine(Saves, "backups"), T0);
        Assert.False(r.Verified);
        Assert.False(r.Skipped);
        Assert.False(Directory.Exists(Path.Combine(Saves, "backups")));
    }

    [Fact]
    public void Restore_makes_the_saves_folder_byte_equal_after_a_safety_backup()
    {
        var original = Snapshot(Saves);
        string b = Wo175Backup.Backup(Saves, Root, T0).Path!;

        // the player plays on: a save changes, one is deleted, new ones appear
        Write("playline0/quicksave.whs", 71_000, 99);
        File.Delete(Path.Combine(Saves, "profile.dat"));
        Write("playline2/autosave003.whs", 2_000, 7);
        Write("playline0/extra.whs", 10, 8);
        File.SetAttributes(Path.Combine(Saves, "playline0", "extra.whs"), FileAttributes.ReadOnly);
        var played = Snapshot(Saves);

        var r = Wo175Backup.Restore(b, Saves, Root, T0.AddHours(2));

        Assert.True(r.Verified, r.Why);
        Assert.Equal(5, r.Files);
        Assert.NotNull(r.SafetyBackup);
        AssertSame(original, Snapshot(Saves));
        Assert.False(Directory.Exists(Path.Combine(Saves, "playline2")));
        Assert.True(Directory.Exists(Path.Combine(Saves, "emptyfolder")));
        // the safety backup holds what the player had, so the restore can be undone
        Assert.True(Wo175Backup.Verify(r.SafetyBackup!).Ok);
        AssertSame(played, Snapshot(r.SafetyBackup!, skipManifest: true));
        Assert.Equal(r.SafetyBackup, Wo175Backup.List(Root)[0].Path);
        Assert.StartsWith($"WO175-RESTORE from={b} safety_backup={r.SafetyBackup} files=5 verified=yes", r.LogLine);

        // and undoing it works the same way
        var undo = Wo175Backup.Restore(r.SafetyBackup!, Saves, Root, T0.AddHours(3));
        Assert.True(undo.Verified, undo.Why);
        AssertSame(played, Snapshot(Saves));
    }

    [Fact]
    public void Restore_refuses_a_corrupt_backup_and_changes_nothing()
    {
        string b = Wo175Backup.Backup(Saves, Root, T0).Path!;
        Write("playline0/quicksave.whs", 71_000, 99);
        var now = Snapshot(Saves);
        File.AppendAllText(Path.Combine(b, "profile.dat"), "rot");

        var r = Wo175Backup.Restore(b, Saves, Root, T0.AddHours(2));

        Assert.False(r.Verified);
        Assert.Null(r.SafetyBackup);
        Assert.Contains("damaged", r.Why);
        AssertSame(now, Snapshot(Saves));
        Assert.Single(Wo175Backup.List(Root));   // no safety backup was made either
    }

    [Fact]
    public void Restore_does_not_prune_the_backup_it_restores_from()
    {
        var made = new List<string>();
        for (int i = 0; i < 10; i++) made.Add(Wo175Backup.Backup(Saves, Root, T0.AddMinutes(i)).Path!);
        Write("profile.dat", 300, 42);
        var r = Wo175Backup.Restore(made[0], Saves, Root, T0.AddHours(1));
        Assert.True(r.Verified, r.Why);
        Assert.True(Directory.Exists(made[0]));
        Assert.Equal(11, Wo175Backup.List(Root).Count);
    }

    [Fact]
    public void The_default_root_honours_the_override_and_otherwise_sits_in_local_app_data()
    {
        string? old = Environment.GetEnvironmentVariable(Wo175Backup.RootEnv);
        try
        {
            Environment.SetEnvironmentVariable(Wo175Backup.RootEnv, Root);
            Assert.Equal(Root, Wo175Backup.DefaultRoot());
            Environment.SetEnvironmentVariable(Wo175Backup.RootEnv, null);
            Assert.EndsWith(Path.Combine("KingdomComeTogether", "save-backups"), Wo175Backup.DefaultRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable(Wo175Backup.RootEnv, old);
        }
    }

    [Fact]
    public void The_setting_is_on_by_default()
    {
        Assert.True(new AppSettings().BackupSavesBeforeSession);
    }
}
