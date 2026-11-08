// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCT QoL (IgnisComeDeliverance): the launcher's Co-op options page -- the in-game mod menu's own choices, set before a session.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KCDMP_launcher.Models
{
    /// <summary>
    /// The co-op options the in-game mod menu remembers (the agent's mod-settings.json beside KcdMpClient.exe, keys =
    /// Wo154MenuRules.Settings). The agent pushes them into the game at the start of every session (the host's own only
    /// when hosting), so a change here applies at the next session; a change in the game's menu is written back to the
    /// same file. Defaults are the mod's own (kdcmp.lua) for a key not in the file yet.
    /// </summary>
    public static class CoopOptions
    {
        public const string FileName = "mod-settings.json";

        public enum Kind { OnOff, Crime }

        public sealed record Option(string Key, string Label, string Help, string Group, bool HostOnly, bool Default, Kind Kind = Kind.OnOff);

        public static readonly Option[] All =
        {
            new("FriendlyFire", "Friendly fire", "Players can hurt each other.", "World rules", true, true),
            new("CrimeMode", "Shared crime", "On: a crime by either of you counts for both. Off: each answers for their own.", "World rules", true, true, Kind.Crime),
            new("FastTravel", "Fast travel", "Allows fast travel. Only the host travels; the joiner is brought along.", "World rules", true, true),
            new("Leash", "Keep players together", "Warns the joiner, then brings them back beside the host, when they wander too far apart.", "World rules", true, true),
            new("Quicksave", "Quicksave without schnapps", "The quicksave key saves for free. Off: the game's own saving (schnapps, beds).", "World rules", true, true),

            new("SleepVote", "Sleep and wait together", "Your sleep or wait asks your partner first. Off: you sleep and wait alone.", "Playing together", false, true),
            new("Whistle", "Hear each other's whistle", "Your whistle is heard at your figure on your partner's screen, and theirs on yours.", "Playing together", false, true),
            new("PartnerHerbs", "Show your partner picking herbs", "Off: their figure stands still while they pick.", "Playing together", false, true),

            new("BadgeIcon", "Partner marker instead of name", "A small marker over your partner coloured by their health, with a bar when hurt. Off: their name and numbers.", "On screen", false, true),
            new("NameBadges", "Show the partner's badge", "Off: nothing over your partner's head.", "On screen", false, true),
            new("PartnerMarker", "Partner on the map", "A pin on the map where your partner is.", "On screen", false, true),
            new("PartnerMarkerArt", "Knight map marker", "Your partner is the knight on the map, turning with them. Off: the game's plain pin.", "On screen", false, true),
            new("PingLine", "Connection line", "The connection's grade, ping, jitter and loss in the top left corner.", "On screen", false, true),
            new("QuietConsole", "Quiet console", "Keeps the mod's routine lines off the ~ console; errors still show.", "On screen", false, true),
            new("CleanScreen", "Clean screen", "Hides everything the mod draws, for screenshots and recording.", "On screen", false, false),
        };

        public static readonly string[] Groups = { "World rules", "Playing together", "On screen" };

        /// <summary>The values in a mod-settings.json (null / unreadable = none), every option filled: the file's, else its default.</summary>
        public static Dictionary<string, bool> Read(string? json)
        {
            JsonObject? o = null;
            try { if (!string.IsNullOrWhiteSpace(json)) o = JsonNode.Parse(json) as JsonObject; } catch (JsonException) { }
            var d = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var opt in All)
            {
                bool v = opt.Default;
                if (o is not null && o.TryGetPropertyValue(opt.Key, out var n) && n is JsonValue jv)
                {
                    if (opt.Kind == Kind.Crime && jv.TryGetValue(out string? s)) v = s == "joint" ? true : s == "individual" ? false : opt.Default;
                    else if (jv.TryGetValue(out bool b)) v = b;
                }
                d[opt.Key] = v;
            }
            return d;
        }

        /// <summary>The file with these values written in (in the agent's own form); every other key in it is kept as it was.</summary>
        public static string Write(string? existingJson, IReadOnlyDictionary<string, bool> values)
        {
            JsonObject o;
            try { o = (!string.IsNullOrWhiteSpace(existingJson) ? JsonNode.Parse(existingJson) as JsonObject : null) ?? new JsonObject(); }
            catch (JsonException) { o = new JsonObject(); }
            foreach (var opt in All)
            {
                if (!values.TryGetValue(opt.Key, out bool v)) continue;
                o[opt.Key] = opt.Kind == Kind.Crime ? JsonValue.Create(v ? "joint" : "individual") : JsonValue.Create(v);
            }
            return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>Reads the file (missing = defaults).</summary>
        public static Dictionary<string, bool> Load(string path)
        {
            try { return Read(File.Exists(path) ? File.ReadAllText(path) : null); }
            catch (IOException) { return Read(null); }
            catch (UnauthorizedAccessException) { return Read(null); }
        }

        /// <summary>Writes the values in (a temp file, then a replace: a crash never leaves half a file). False when it could not.</summary>
        public static bool Save(string path, IReadOnlyDictionary<string, bool> values, out string? error)
        {
            error = null;
            try
            {
                string? old = File.Exists(path) ? File.ReadAllText(path) : null;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Write(old, values));
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
