// Mesen's settings.json, edited IN PLACE.
//
// THE FILE (measured 18/10 on 2.1.1 and CE 2.2.1, docs\mesen.md §4): UTF-8 WITH a BOM, CRLF, indented by two spaces, enums as
// their names, written by System.Text.Json's source generator (WriteIndented, the default encoder) and File.WriteAllText.
// Mesen rewrites it WHOLE - when what it would write differs from what it read: after a game is loaded (its recent files),
// when a settings window closes, when it quits. So it is edited BEFORE a launch and read back AFTER, never while Mesen runs.
// Mesen read a file written with LF and no BOM, and wrote it back with both.
//
// THE EDIT: the document is parsed as a JsonNode tree, which keeps every member in its order - unknown ones too (a newer
// Mesen's) - and every value as its original text; only the values set here change. Written back only when something
// changed, as Mesen writes it (BOM, CRLF, two spaces), through a temporary file then a replace. Property names are matched
// without regard to case, as Mesen's own switches do (ConfigManager.ProcessSwitch), and set under the name already there.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LbIntegrations.Mesen
{
    internal sealed class MesenJson
    {
        public const string FileName = "settings.json";

        public string Path { get; }
        public JsonObject Root { get; private set; }
        public bool Changed { get; private set; }
        /// <summary>False when the file was there but could not be parsed: nothing is ever written over it.</summary>
        public bool Readable { get; private set; } = true;
        public bool Exists { get; private set; }

        private MesenJson(string path) { Path = path; }

        public static MesenJson Load(string path)
        {
            var j = new MesenJson(path);
            try
            {
                if (File.Exists(path))
                {
                    j.Exists = true;
                    var text = File.ReadAllText(path, Encoding.UTF8);   // the BOM, when there, is taken off
                    j.Root = JsonNode.Parse(text, new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                                            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject;
                    if (j.Root == null) { j.Readable = false; Log.Warn("settings.json is not a JSON object - left as it is: " + path); }
                }
            }
            catch (Exception ex) { j.Readable = false; Log.Warn("settings.json could not be read - left as it is: " + path, ex); }
            j.Root ??= new JsonObject();
            return j;
        }

        // ── reading ──

        /// <summary>The node at "Preferences/SaveDataFolder" (each name matched without regard to case), or null.</summary>
        public JsonNode Get(string path)
        {
            JsonNode cur = Root;
            foreach (var part in Split(path))
            {
                if (!(cur is JsonObject o)) return null;
                var name = NameIn(o, part);
                if (name == null) return null;
                cur = o[name];
            }
            return cur;
        }

        public string GetString(string path)
        {
            var n = Get(path);
            try { return n == null ? null : n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : n.ToJsonString(); }
            catch { return null; }
        }

        public bool? GetBool(string path)
        {
            var n = Get(path);
            try { return n?.GetValueKind() == JsonValueKind.True ? true : n?.GetValueKind() == JsonValueKind.False ? false : (bool?)null; }
            catch { return null; }
        }

        /// <summary>The value's text as a switch would give it: a string's content, true/false, a number's digits; null when absent.</summary>
        public static string TextOf(JsonNode n)
        {
            if (n == null) return null;
            try { return n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : n.ToJsonString(); }
            catch { return n.ToJsonString(); }
        }

        // ── writing ──

        /// <summary>Set the node at <paramref name="path"/> (objects made on the way), only when it differs.</summary>
        public void Set(string path, JsonNode value)
        {
            if (!Readable) return;
            var parts = Split(path);
            JsonObject cur = Root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var name = NameIn(cur, parts[i]);
                if (name == null || !(cur[name] is JsonObject next))
                {
                    next = new JsonObject();
                    cur[name ?? parts[i]] = next;
                    Changed = true;
                }
                cur = next;
            }
            var last = NameIn(cur, parts[^1]) ?? parts[^1];
            var old = cur.ContainsKey(last) ? cur[last] : null;
            if (old != null && value != null && JsonNode.DeepEquals(old, value)) return;
            if (old == null && value == null && !cur.ContainsKey(last)) return;
            cur[last] = value;
            Changed = true;
        }

        public void Set(string path, string value) => Set(path, value == null ? null : JsonValue.Create(value));
        public void Set(string path, bool value) => Set(path, JsonValue.Create(value));

        /// <summary>Take the member out (the session restore of a key that was not in the file before).</summary>
        public void Remove(string path)
        {
            if (!Readable) return;
            var parts = Split(path);
            JsonNode cur = Root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!(cur is JsonObject o)) return;
                var name = NameIn(o, parts[i]);
                if (name == null) return;
                cur = o[name];
            }
            if (cur is JsonObject parent && NameIn(parent, parts[^1]) is string n) { parent.Remove(n); Changed = true; }
        }

        /// <summary>Marks the document changed by an edit made directly on its nodes (the shortcut list).</summary>
        public void Touch() => Changed = true;

        /// <summary>Mesen's writer: indented by two spaces, CRLF, the default encoder (UI\Utilities\JsonHelper.cs, MesenSerializerContext:
        /// WriteIndented, no Encoder given).</summary>
        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions { Indented = true, IndentSize = 2, NewLine = "\r\n" };

        /// <summary>The text as Mesen writes it: two spaces, CRLF (the BOM is the writer's). A value read from the file is written
        /// back AS IT WAS - its own text: Mesen writes a date ("BsxCustomDate": "1995-01-01T00:00:00+00:00") without the escaping a
        /// string gets ("+" stays "+"), and re-encoding it would change a line nobody touched (measured 18/10 on its own file:
        /// re-serialized, the first difference was that "+"). Only a value set here is encoded, by the same default encoder.
        /// Checked by the probe: Mesen's own 238 KB file comes out byte for byte.</summary>
        public string Text()
        {
            using var buffer = new MemoryStream();
            using (var w = new Utf8JsonWriter(buffer, WriterOptions)) { WriteNode(w, Root); }
            return new UTF8Encoding(false).GetString(buffer.ToArray());
        }

        private static void WriteNode(Utf8JsonWriter w, JsonNode n)
        {
            switch (n)
            {
                case null: w.WriteNullValue(); break;
                case JsonObject o:
                    w.WriteStartObject();
                    foreach (var kv in o) { w.WritePropertyName(kv.Key); WriteNode(w, kv.Value); }
                    w.WriteEndObject();
                    break;
                case JsonArray a:
                    w.WriteStartArray();
                    foreach (var item in a)
                    {
                        // A raw value in an array is written with no line break nor indent (Utf8JsonWriter does not indent
                        // raw values there): given here, as Mesen's writer puts each element on its own line.
                        if (item is JsonValue iv && iv.TryGetValue<JsonElement>(out var ie))
                            w.WriteRawValue("\r\n" + new string(' ', w.CurrentDepth * 2) + ie.GetRawText(), skipInputValidation: true);
                        else WriteNode(w, item);
                    }
                    w.WriteEndArray();
                    break;
                case JsonValue v:
                    if (v.TryGetValue<JsonElement>(out var el)) w.WriteRawValue(el.GetRawText(), skipInputValidation: true);
                    else v.WriteTo(w);
                    break;
            }
        }

        /// <summary>Written when something changed: UTF-8 with a BOM, CRLF, through a temporary file. False when nothing was.</summary>
        public bool SaveIfChanged()
        {
            if (!Changed || !Readable) return false;
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            Directory.CreateDirectory(dir);
            var tmp = Path + ".lbip-tmp";
            File.WriteAllText(tmp, Text(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            if (File.Exists(Path)) File.Replace(tmp, Path, null);
            else File.Move(tmp, Path);
            Changed = false;
            Exists = true;
            return true;
        }

        private static string[] Split(string path) => (path ?? "").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>The member's name as the object has it, matched without regard to case; null when absent.</summary>
        internal static string NameIn(JsonObject o, string name)
        {
            if (o == null) return null;
            if (o.ContainsKey(name)) return name;
            return o.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
