// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KcdMp.Client;

/// <summary>
/// KCT fix (icd14): which file the tail has open. On Windows the volume and the file index (the same file whatever its name, a new
/// file a new index -- creation time alone is not enough: Windows "tunnels" a deleted file's creation time to a new one of the same
/// name made within 15 s); elsewhere the creation time. Default = unknown.
/// </summary>
public readonly record struct LogFileId(ulong Volume, ulong Index, long CreatedTicks)
{
    public bool Known => Index != 0 || CreatedTicks != 0;

    /// <summary>The identity of an open handle (unknown when it cannot be read).</summary>
    public static LogFileId Of(SafeFileHandle h, string path)
    {
        try
        {
            if (OperatingSystem.IsWindows() && GetFileInformationByHandle(h, out var i))
                return new LogFileId(i.VolumeSerialNumber, (ulong)i.FileIndexHigh << 32 | i.FileIndexLow, (long)((ulong)i.CreationHigh << 32 | i.CreationLow));
            return new LogFileId(0, 0, File.GetCreationTimeUtc(path).Ticks);
        }
        catch { return default; }
    }

    /// <summary>The identity of the file at <paramref name="path"/> now (opened shared, closed at once); null when it is not there.</summary>
    public static LogFileId? AtPath(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Of(fs.SafeFileHandle, path);
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh, FileSizeLow, NumberOfLinks;
        public uint FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation info);
}

/// <summary>KCT fix (icd14): where an open of kcd.log starts reading.</summary>
public enum TailStart { End, Resume, Beginning }

/// <summary>KCT fix (icd14): the log tail's pure rules (LogTailRulesTests).</summary>
public static class LogTailRules
{
    /// <summary>
    /// Where to start in a file just opened: the very first open (or the switch off) at its end, as always -- only new lines matter;
    /// the same file again (a read error, a handle lost) where the tail stopped; a new file (another identity, or the same one shorter
    /// than where the tail stopped: truncated and written again) from its beginning, so no line after the restart is lost.
    /// </summary>
    public static (TailStart How, long Offset) OpenAt(bool resumeOn, bool firstOpen, LogFileId? saved, long savedOffset, LogFileId now, long length)
    {
        if (!resumeOn || firstOpen || saved is null) return (TailStart.End, length);
        if (saved.Value == now && length >= savedOffset) return (TailStart.Resume, savedOffset);
        return (TailStart.Beginning, 0);
    }

    /// <summary>Is the file now at the path another one than the open handle's (renamed away and a new one made, or replaced)?</summary>
    public static bool Replaced(LogFileId open, LogFileId? atPath) => open.Known && atPath is { Known: true } p && p != open;
}

/// <summary>
/// KCT fix (icd14): the tail's cheapest skip. The game's log fills with engine spam (the field: 171,306 "[Warning] Validator: Can't remove
/// anything" blocks in 32 minutes, 149 MB) that no handler reads. A line starting with a spam prefix is skipped on its BYTES -- before any
/// decoding or allocation -- unless it holds one of the markers a handler searches for anywhere in a line (<see cref="Keep"/>: every
/// IndexOf / Contains literal of LogTailGameTransport's line handlers, the mod's own tag).
/// </summary>
public static class LogLineFilter
{
    private static readonly byte[][] SkipPrefixes = [.. new[] { "[Warning] Validator:" }.Select(Encoding.UTF8.GetBytes)];

    private static readonly byte[][] Keep = [.. new[]
        {
            "[KCD2-MP", "CutscenePlayer::", "TeleportPlayer Player 'Dude'", " Loading level ", "'sqc_ptag_menu' will be", "PlayAudio: Apse",
            "Readiness observer '", "FastTravel: unable to start fast travel", "[ScenePositioningManager]", "Cancelling FF for all NPCs",
            "InteractiveSceneManager::", "AutoSave is disabled under a script lock", "[SAVE GAME]", "InitiateSaveGame()", "questNameOverride",
        }.Concat(LogTailGameTransport.Wo144Contains).Distinct().Select(Encoding.UTF8.GetBytes)];

    /// <summary>True: the line (bytes, no terminator needed) is spam no handler reads.</summary>
    public static bool Skip(ReadOnlySpan<byte> line)
    {
        bool spam = false;
        foreach (var p in SkipPrefixes) if (line.StartsWith(p)) { spam = true; break; }
        if (!spam) return false;
        foreach (var k in Keep) if (line.IndexOf(k) >= 0) return false;
        return true;
    }

    /// <summary>A line of the mod's own (its tags all start "[KCD2-MP").</summary>
    public static bool IsModLine(ReadOnlySpan<byte> line) => line.StartsWith("[KCD2-MP"u8);

    /// <summary>A plain "[KCD2-MP] " line of the mod's (mp_log; not a DATA or EVT line).</summary>
    public static bool IsModPlainLine(ReadOnlySpan<byte> line) => line.StartsWith("[KCD2-MP] "u8);
}
