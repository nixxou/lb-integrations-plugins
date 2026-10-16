// THE COMPATIBILITY TABLE: which label each emulator gets for a game.
//
// A JSON file beside the log (not in the plugin folder, which LaunchBox checks against the manifest):
//
//     %LOCALAPPDATA%\lb-integrations-plugins\emu-labels.json
//
//     {
//       "format": "  [{label}]",
//       "games":     { "<LaunchBox game id>":      { "<emulator id or title>": "Perfect" } },
//       "titles":    { "<game title>|<platform>":  { "<emulator id or title>": "Playable" } },
//       "platforms": { "<platform>":               { "<emulator id or title>": "Untested" } },
//       "colors":    { "Perfect": "#66BB6A" },     // optional: a label's badge colour (LaunchBox menu)
//       "badgeText": "#1A1A1A"                     // optional: the badge's text colour
//     }
//
// For one emulator, the first level that names it wins: games, then titles, then platforms. An
// emulator is named by its Id or by its title (as the user sees it, without a label), case ignored.
// Comments and trailing commas are allowed. The file is read again whenever its date changes, so it
// can be edited while LaunchBox runs; the next selection shows the change.
//
// Missing, the file is created with an empty table. Unreadable, the previous table stays in use and
// the log says why.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.LaunchWithLabels
{
    internal sealed class CompatTable
    {
        private const string DefaultFormat = "  [{label}]";

        private const string Template =
            "// Nixx-LaunchWithLabels: the label each emulator shows for a game. See src\\LaunchWithLabels\\CompatTable.cs.\r\n" +
            "{\r\n" +
            "  \"format\": \"  [{label}]\",\r\n" +
            "  \"games\": {},\r\n" +
            "  \"titles\": {},\r\n" +
            "  \"platforms\": {}\r\n" +
            "}\r\n";

        public string Format { get; private set; } = DefaultFormat;

        private Dictionary<string, Dictionary<string, string>> _games = Map();
        private Dictionary<string, Dictionary<string, string>> _titles = Map();
        private Dictionary<string, Dictionary<string, string>> _platforms = Map();
        private Dictionary<string, string> _colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The badge's text colour; dark, because the badge colours are light (see README).</summary>
        public string BadgeText { get; private set; } = "#1A1A1A";

        public string FilePath { get; }
        private DateTime _stamp = DateTime.MinValue;

        public CompatTable(string filePath) { FilePath = filePath; }

        /// <summary>Read the file again if it changed since the last read.</summary>
        public void Refresh()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, Template);
                    Log.Info("created " + FilePath);
                }
                var stamp = File.GetLastWriteTimeUtc(FilePath);
                if (stamp == _stamp) return;
                Load(File.ReadAllText(FilePath));
                _stamp = stamp;
                Log.Info("table read: " + _games.Count + " game(s), " + _titles.Count + " title(s), "
                         + _platforms.Count + " platform(s)");
            }
            catch (Exception ex) { Log.Warn("could not read " + FilePath + ", keeping the previous table", ex); }
        }

        /// <summary>Parse a table. Public for the test bench.</summary>
        public void Load(string json)
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var doc = JsonDocument.Parse(json, options);
            var root = doc.RootElement;
            Format = root.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString() : DefaultFormat;
            _games = Read(root, "games");
            _titles = Read(root, "titles");
            _platforms = Read(root, "platforms");
            _colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Object)
                foreach (var c in colors.EnumerateObject())
                    if (c.Value.ValueKind == JsonValueKind.String) _colors[c.Name] = c.Value.GetString();
            BadgeText = root.TryGetProperty("badgeText", out var bt) && bt.ValueKind == JsonValueKind.String ? bt.GetString() : "#1A1A1A";
        }

        /// <summary>The text to add to this emulator's title for this game, or null for none.</summary>
        public string LabelFor(IGame game, IEmulator emulator, string originalTitle)
        {
            var label = RawLabelFor(game, emulator, originalTitle);
            return label == null ? null : Format.Replace("{label}", label);
        }

        /// <summary>The label itself ("Perfect"), unformatted, or null for none.</summary>
        public string RawLabelFor(IGame game, IEmulator emulator, string originalTitle)
        {
            if (game == null || emulator == null) return null;
            var label = Find(_games, game.Id, emulator, originalTitle)
                        ?? Find(_titles, game.Title + "|" + game.Platform, emulator, originalTitle)
                        ?? Find(_platforms, game.Platform, emulator, originalTitle);
            return string.IsNullOrEmpty(label) ? null : label;
        }

        /// <summary>The badge colour of a label ("#RRGGBB"), or null when it has none.</summary>
        public string ColorFor(string rawLabel)
            => rawLabel != null && _colors.TryGetValue(rawLabel, out var c) ? c : null;

        private static string Find(Dictionary<string, Dictionary<string, string>> level, string key,
                                   IEmulator emulator, string originalTitle)
        {
            if (key == null || !level.TryGetValue(key, out var byEmulator)) return null;
            if (emulator.Id != null && byEmulator.TryGetValue(emulator.Id, out var byId)) return byId;
            if (originalTitle != null && byEmulator.TryGetValue(originalTitle, out var byTitle)) return byTitle;
            return null;
        }

        private static Dictionary<string, Dictionary<string, string>> Read(JsonElement root, string name)
        {
            var result = Map();
            if (!root.TryGetProperty(name, out var level) || level.ValueKind != JsonValueKind.Object) return result;
            foreach (var entry in level.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                var byEmulator = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in entry.Value.EnumerateObject())
                    if (e.Value.ValueKind == JsonValueKind.String) byEmulator[e.Name] = e.Value.GetString();
                result[entry.Name] = byEmulator;
            }
            return result;
        }

        private static Dictionary<string, Dictionary<string, string>> Map()
            => new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    }
}
