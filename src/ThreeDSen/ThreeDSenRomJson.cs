// rom.json - 3dSen's list of profiles and the ROM bound to each, in its user data folder. THE LAUNCH WRITES IT: the ROM
// path is never on 3dSen's command line, it is the profile's "romPath" here (docs\3dsen.md section 3).
//
// MEASURED (docs\3dsen.md section 4, 3dSen PC 1.1.2): Unity's JsonUtility pretty print through Nes3D.Utils.JsonHelper -
// UTF-8 with no BOM, LF line ends, 4-space indent, no newline at the end -
//
//     {
//         "Warning": "Don't touch id parameter,  you will regret it",
//         "Items": [
//             {
//                 "id": "73",
//                 "romPath": "G:\\...\\Zelda II.nes",
//                 "title": "Zelda II: The Adventure of Link",
//                 "lastTime": 1792338827
//             },
//
// written WHOLE when 3dSen quits (GameDB.SaveConfig in OnApplicationQuit) - so never while it runs - and read at its start
// (GameDB.Awake): a file that does not parse gives 3dSen an EMPTY list, every binding lost (JsonHelper.FromJson in a
// try/catch that starts over). Hence an editor that touches one value in place and nothing else: the bytes around it are
// kept as they are, the new value escaped as JsonUtility reads it (backslash and quote escaped; measured: a path with
// doubled backslashes is read, and kept by 3dSen's own rewrite). A profile missing from the file gets an item (3dSen makes
// the same for any profile it does not find - GameDB.AddGame - and keeps ours).
//
// NO rom.json (3dSen never run): one is made with the one item. But 3dSen 1.1.2 reads an older rom.cfg (protobuf) only when
// rom.json is absent, and DELETES rom.cfg as soon as rom.json exists (GameDB.Awake): with a rom.cfg there, making rom.json
// would drop its bindings - refused, the user asked to start 3dSen once by itself (it converts rom.cfg at its quit).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenRomJson
    {
        public const string FileName = "rom.json";
        public const string OldFileName = "rom.cfg";
        private const string Warning = "Don't touch id parameter,  you will regret it";

        internal sealed class Item
        {
            public string Id, RomPath, Title;
            public long ValueStart = -1, ValueLength;    // the romPath string's bytes, quotes included
        }

        /// <summary>The items of a rom.json, in its order; empty when it is absent or does not parse.</summary>
        public static List<Item> Items(string path)
        {
            try { return File.Exists(path) ? Read(File.ReadAllBytes(path)) : new List<Item>(); }
            catch (Exception ex) { Log.Warn("rom.json could not be read", ex); return new List<Item>(); }
        }

        /// <summary>The romPath bound to <paramref name="id"/>, or null.</summary>
        public static string RomOf(string path, string id) => Items(path).FirstOrDefault(i => i.Id == id)?.RomPath;

        internal static List<Item> Read(byte[] bytes)
        {
            var items = new List<Item>();
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            int depth = 0;
            bool inItems = false;
            Item current = null;
            string property = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        depth++;
                        if (inItems && depth == 3) current = new Item();   // root 1, Items 2, an item 3
                        break;
                    case JsonTokenType.EndObject:
                        if (inItems && depth == 3 && current != null) { items.Add(current); current = null; }
                        depth--;
                        break;
                    case JsonTokenType.StartArray:
                        if (depth == 1 && property == "Items") inItems = true;
                        depth++;
                        break;
                    case JsonTokenType.EndArray:
                        depth--;
                        if (depth == 1) inItems = false;
                        break;
                    case JsonTokenType.PropertyName:
                        property = reader.GetString();
                        break;
                    case JsonTokenType.String:
                        if (current != null && depth == 3)
                        {
                            var v = reader.GetString();
                            if (property == "id") current.Id = v;
                            else if (property == "title") current.Title = v;
                            else if (property == "romPath")
                            {
                                current.RomPath = v;
                                current.ValueStart = reader.TokenStartIndex;
                                current.ValueLength = reader.ValueSpan.Length + 2;
                            }
                        }
                        break;
                }
            }
            return items;
        }

        /// <summary>JsonUtility's string: quotes, backslash and quote escaped, control characters as \u00XX; the rest as is
        /// (UTF-8).</summary>
        internal static string Quote(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in value ?? "")
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>The romPath of <paramref name="id"/> set to <paramref name="rom"/> in the rom.json at <paramref name="path"/>
        /// - in place, written only when it changes. Null when done (or nothing to do), else why it could not be.</summary>
        public static string SetRom(string path, string id, string title, string rom)
        {
            try
            {
                if (!File.Exists(path))
                {
                    var old = Path.Combine(Path.GetDirectoryName(path) ?? "", OldFileName);
                    if (File.Exists(old))
                        return "3dSen still has its older list of games (rom.cfg) and no rom.json: start 3dSen once by itself and quit it, "
                               + "it converts the list, then launch the game again.";
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    Write(path, Encoding.UTF8.GetBytes(Fresh(id, title, rom)));
                    Log.Info("rom.json made with the profile " + id + " -> " + rom);
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                List<Item> items;
                try { items = Read(bytes); }
                catch (JsonException ex) { return "3dSen's rom.json does not read as JSON (" + ex.Message + ") - it was left as it is."; }
                var item = items.FirstOrDefault(i => i.Id == id);
                byte[] updated;
                if (item != null && item.ValueStart >= 0)
                {
                    if (item.RomPath == rom) { Log.Info("rom.json: the profile " + id + " already plays " + rom); return null; }
                    var value = Encoding.UTF8.GetBytes(Quote(rom));
                    updated = Splice(bytes, item.ValueStart, item.ValueLength, value);
                }
                else if (item == null)
                {
                    updated = Append(bytes, id, title, rom);
                    if (updated == null) return "3dSen's rom.json has no list of games where one was expected - it was left as it is.";
                }
                else return "the profile " + id + " has no romPath in rom.json - it was left as it is.";

                // What is written must read back as 3dSen will read it, the value in place.
                var check = Read(updated).FirstOrDefault(i => i.Id == id);
                if (check?.RomPath != rom) return "rom.json could not be edited safely - it was left as it is.";
                Write(path, updated);
                Log.Info("rom.json: the profile " + id + " now plays " + rom);
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("rom.json could not be written", ex);
                return "3dSen's rom.json could not be written: " + ex.Message;
            }
        }

        private static byte[] Splice(byte[] bytes, long start, long length, byte[] value)
        {
            var o = new byte[bytes.Length - length + value.Length];
            Array.Copy(bytes, 0, o, 0, start);
            Array.Copy(value, 0, o, start, value.Length);
            Array.Copy(bytes, start + length, o, start + value.Length, bytes.Length - start - length);
            return o;
        }

        private static string ItemText(string id, string title, string rom, string nl)
            => "{" + nl + "            \"id\": " + Quote(id) + "," + nl
               + "            \"romPath\": " + Quote(rom) + "," + nl
               + "            \"title\": " + Quote(title ?? "") + "," + nl
               + "            \"lastTime\": 0" + nl + "        }";

        private static string Fresh(string id, string title, string rom)
            => "{\n    \"Warning\": " + Quote(Warning) + ",\n    \"Items\": [\n        " + ItemText(id, title, rom, "\n") + "\n    ]\n}";

        /// <summary>An item added at the end of "Items": [ ... ], in the file's own line ends.</summary>
        private static byte[] Append(byte[] bytes, string id, string title, string rom)
        {
            var text = Encoding.UTF8.GetString(bytes);
            var nl = text.Contains("\r\n") ? "\r\n" : "\n";
            // The "Items" array's closing bracket: the last ']' of the file (the root object holds nothing after it).
            var close = text.LastIndexOf(']');
            var at = text.IndexOf("\"Items\"", StringComparison.Ordinal);
            if (close < 0 || at < 0 || close < at) return null;
            var open = text.IndexOf('[', at);
            if (open < 0 || open > close) return null;
            var inside = text.Substring(open + 1, close - open - 1);
            string insert = inside.Trim().Length == 0
                ? nl + "        " + ItemText(id, title, rom, nl) + nl + "    "
                : null;
            if (insert != null) return Encoding.UTF8.GetBytes(text.Substring(0, open + 1) + insert + text.Substring(close));
            // After the last item's closing brace.
            var lastBrace = text.LastIndexOf('}', close);
            if (lastBrace < open) return null;
            return Encoding.UTF8.GetBytes(text.Substring(0, lastBrace + 1) + "," + nl + "        " + ItemText(id, title, rom, nl) + text.Substring(lastBrace + 1));
        }

        /// <summary>Written beside, then moved over: a rom.json is never left half written.</summary>
        private static void Write(string path, byte[] bytes)
        {
            var temp = path + ".lbip-tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, true);
        }
    }
}
