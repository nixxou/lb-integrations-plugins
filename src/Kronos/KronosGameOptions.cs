// A game's own options for Kronos - written in kronos.ini for its session only (KronosSession), put back after it. Ymir's
// model (YmirGameOptions): "As set in Kronos" (the default) writes nothing; a value Kronos wrote that this table does not list is
// kept as it is ("raw:" + the ini value), written back as it is, shown "(set in Kronos)".
//
// THE TABLE IS EMPTY in this first batch: the game's window, its storage and the session are ready for it (docs\kronos.md §3
// lists the keys - video core, resolution, upscaling, filter, mesh, banding, SH-2 cache, cartridge...).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Kronos
{
    internal sealed class KronosGameOption
    {
        public string Key, Label, Hint, Group;
        /// <summary>The ini keys it writes ("Video\VideoCore"), and whether each is a path.</summary>
        public (string Key, bool IsPath)[] Keys;
        /// <summary>The values offered (stored value, text), for the Kronos at that exe.</summary>
        public Func<string, List<(string Value, string Text)>> Choices;
        /// <summary>A value's TEXT for each key, in Keys' order; null: that key is not written.</summary>
        public Func<string, string[]> Write;
        /// <summary>The value the file says (a getter of text by key); null when nothing can be said.</summary>
        public Func<Func<string, string>, string> Read;
    }

    internal static class KronosGameOptions
    {
        public const string Raw = "raw:";
        public static readonly string[] Groups = { "Picture", "Emulation", "Sound and cartridge" };

        public static readonly KronosGameOption[] All = { };

        public static KronosGameOption Option(string key) => All.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));

        private static string IniKey(string key) => "opt." + key;

        /// <summary>The game's own value, or null: "As set in Kronos".</summary>
        public static string GameValue(string gameId, string key) => KronosSettings.GameRaw(gameId, IniKey(key));

        public static void SetGameValue(string gameId, string key, string value) => KronosSettings.SetGameRaw(gameId, IniKey(key), value);

        /// <summary>What a value is called in the lists - one this table does not list, as Kronos wrote it.</summary>
        public static string TextOf(KronosGameOption o, string value, string exe)
        {
            if (value == null) return null;
            var hit = o.Choices(exe).FirstOrDefault(c => c.Value == value);
            if (hit.Text != null) return hit.Text;
            return (value.StartsWith(Raw) ? value.Substring(Raw.Length) : value) + " (set in Kronos)";
        }

        public static string Current(KronosGameOption o, KronosIniDoc doc) => o.Read(k => doc?.Get(k));

        /// <summary>For the session's end: the value the option has in kronos.ini now.</summary>
        public static string FromIni(string key, KronosIniDoc doc) => Option(key) is KronosGameOption o ? Current(o, doc) : null;

        /// <summary>Kronos's ini keys are not versioned: every option is written (a key a newer Kronos dropped is ignored by it).</summary>
        public static bool InKronos(KronosGameOption o, KronosIniDoc doc) => true;

        /// <summary>The keys a game's options write for its launch, each with whether it is a path and its option.</summary>
        public static List<(string Key, string Value, bool IsPath, string Option)> KeysFor(string gameId, KronosIniDoc doc)
        {
            var keys = new List<(string, string, bool, string)>();
            foreach (var o in All)
            {
                var value = GameValue(gameId, o.Key);
                if (value == null) continue;
                var texts = o.Write(value);
                for (int i = 0; i < o.Keys.Length; i++)
                {
                    var (key, isPath) = o.Keys[i];
                    // A key the value leaves alone is held as it is all the same, so what Kronos writes there is put back.
                    var text = texts[i] ?? doc?.Get(key);
                    if (text != null) keys.Add((key, text, isPath, o.Key));
                }
            }
            return keys;
        }
    }
}
