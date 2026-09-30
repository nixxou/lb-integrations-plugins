// A game's OWN settings for Vita3K - the Options window's "System" tab (language, date, time, enter
// button) and "Graphics" tab (renderer, image quality, FPS hack) - applied FOR ONE SESSION (Mehdi, 29/09).
//
// WHERE VITA3K READS A GAME'S SETTINGS: <config path>\config\config_<TITLE_ID>.xml (config/src/settings.cpp,
// get_custom_config_path), the config path being portable\ - the file Vita3K's own "Custom Config" writes,
// and the one "Game settings in Vita3K..." opens (Vita3kSettingsSession). That file stays the USER'S: what
// this plugin keeps for a game is in its own store, <install>\lbip-settings.tsv, one line per LaunchBox
// game id, "section/attribute=value;...", and ONLY what the user set - a setting left on "default" is not
// there at all. It goes into the xml for the length of a session only:
//
//   AT LAUNCH, once the title id is known and only for a game that has settings of its own:
//     1. config_<TITLE_ID>.bak is written FIRST - a copy of the xml, or an EMPTY file when there was none;
//     2. the xml is rebuilt: every section the game's settings touch starts from the .bak's (so an
//        attribute this plugin does not know - a newer Vita3K's - is kept as it is), then each known
//        attribute is, in order, the game's setting here, the .bak's, config.yml's (the global one),
//        Vita3K's default. Every other section of the .bak is kept as it is.
//   WHEN VITA3K HAS QUIT: the .bak goes back - over the xml, or, empty, the xml is deleted - and the .bak
//     with it. A change made in Vita3K's Custom Config DURING such a session is therefore not kept.
//   A SESSION'S NOTE (config_<ID>.lbip-session, 30/09) names it - a launch's watcher puts back only its own, not a
//     later launch's - and says what config.yml held of each of this plugin's settings: Vita3K's global settings
//     window, used mid-session, copies the running game's values - ours - into config.yml; they are taken back
//     when the session is put back, a crash's included.
//   ONE AT A TIME: the watcher and OnGameExited both end a session, near together. A lock, never waited on for
//     long (TryEnter): a launch that cannot get it in 5 s runs without the game's settings, an end that cannot
//     get it in 2 s leaves it to the one holding it - LaunchBox is never kept waiting.
//   A .BAK STILL THERE (the host or the machine went mid-session) is put back before anything else: at
//     every launch, at the plugin's start-up check, before "Game settings in Vita3K...", and when Vita3K
//     is opened without a game (LbEmulatorOpened). Never while Vita3K runs: the session is its own.
//
// WHY EVERY KNOWN ATTRIBUTE OF A SECTION IS WRITTEN. Read in the official Vita3K's source
// (load_custom_config, 29/09): a section that is there sets EVERY one of its attributes, and one it does
// not name takes pugixml's own default - false, 0, "" - not config.yml's. So a <gpu> naming only the
// resolution would switch V-Sync off and the renderer to "" (Vulkan). Our fork reads a section attribute
// by attribute (branch custom-config-overrides, PR Vita3K#4177); written whole, the file means the same
// to both.
//
// SET BY HAND (Mehdi, 29/09): the Options window's "Advanced" tab shows what the game overrides as a
// partial xml - only the attributes set, the way our fork reads a custom config - and, "Edit by hand"
// ticked, takes the user's own text instead: any section, any attribute, a newer Vita3K's included. Kept
// in the store as advanced/xml, live when advanced/on is true; the System and Graphics values are then
// set aside (kept, not used). At launch the text's sections go in as the tabs' do - each from the
// .bak's, the text's attributes and child elements over it, then for gpu and system the attributes it
// still lacks from config.yml and Vita3K's defaults. A section this plugin does not know gets only what
// the text and the .bak name: on the official Vita3K the rest takes pugixml's default (CheckHand says so).
//
// Vita3K ignores the .bak: it only ever looks for config_<id>.xml (has_custom_config), and its "delete
// all custom configs" only deletes config_*.xml.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kGameConfig
    {
        private const string StoreName = "lbip-settings.tsv";
        private const string BakExtension = ".bak";

        public const string SystemSection = "system", GpuSection = "gpu", CpuSection = "cpu", AudioSection = "audio", EmulatorSection = "emulator";

        /// <summary>One attribute of a section Vita3K reads whole: the same name in config.yml (true of
        /// every attribute of these two sections - compared in the source, 29/09), and Vita3K's default
        /// (config.h) for when neither the game nor config.yml says.</summary>
        internal sealed class Field
        {
            public string Section, Name, Default;
            public string Yml;      // its name in config.yml - the same, but for three (compared in the source, 29/09)
        }

        private static Field F(string section, string name, string def, string yml = null)
            => new Field { Section = section, Name = name, Default = def, Yml = yml ?? name };

        /// <summary>The attributes of the sections this plugin writes, in Vita3K's order (save_custom_config).
        /// ime-langs, a list, is a child of &lt;system&gt; and handled apart; gpu's custom-driver-name is
        /// Android's only and never written here.</summary>
        internal static readonly Field[] Fields =
        {
            F(SystemSection, "pstv-mode", "false"),
            F(SystemSection, Vita3kConfig.EnterKey, "1"),
            F(SystemSection, Vita3kConfig.LanguageKey, "1"),
            F(SystemSection, Vita3kConfig.DateKey, "2"),
            F(SystemSection, Vita3kConfig.TimeKey, "0"),

            F(GpuSection, "backend-renderer", "Vulkan"),
            F(GpuSection, "gpu-idx", "0"),
            F(GpuSection, "high-accuracy", "false"),
            F(GpuSection, "resolution-multiplier", "1"),
            F(GpuSection, "disable-surface-sync", "true"),
            F(GpuSection, "screen-filter", "Bilinear"),
            F(GpuSection, "memory-mapping", "double-buffer"),
            F(GpuSection, "v-sync", "true"),
            F(GpuSection, "anisotropic-filtering", "1"),
            F(GpuSection, "async-pipeline-compilation", "true"),
            F(GpuSection, "import-textures", "false"),
            F(GpuSection, "export-textures", "false"),
            F(GpuSection, "export-as-png", "true"),
            F(GpuSection, "fps-hack", "false"),
            F(GpuSection, "shader-cache", "true"),
            F(GpuSection, "spirv-shader", "false"),
            F(GpuSection, "texture-cache", "true"),

            // The Compatibility tab's sections, each with every attribute Vita3K reads in it (load_custom_config).
            F(CpuSection, "cpu-opt", "true"),
            F(AudioSection, "audio-backend", "SDL"),
            F(AudioSection, "audio-volume", "100"),
            F(AudioSection, "enable-ngs", "true", yml: "ngs-enable"),
            F(EmulatorSection, "file-loading-delay", "0"),
            F(EmulatorSection, "stretch-the-display-area", "false", yml: "stretch_the_display_area"),
            F(EmulatorSection, "fullscreen-hd-res-pixel-perfect", "false", yml: "fullscreen_hd_res_pixel_perfect"),
        };

        internal static string ConfigDir(Vita3kLayout layout)
        {
            var portable = Vita3kPaths.PortableDirOf(layout?.InstallDir);
            return string.IsNullOrEmpty(portable) ? null : Path.Combine(portable, "config");
        }

        internal static string PathOf(Vita3kLayout layout, string titleId)
        {
            var dir = ConfigDir(layout);
            return dir == null || string.IsNullOrWhiteSpace(titleId) ? null : Path.Combine(dir, "config_" + titleId + ".xml");
        }

        private static string BakOf(string xml) => Path.ChangeExtension(xml, BakExtension);
        private static string NoteOf(string xml) => Path.ChangeExtension(xml, ".lbip-session");

        /// <summary>One Apply or Restore at a time. Held only around file work of this install: nothing inside it waits on
        /// anything else, calls the host, or shows a window.</summary>
        private static readonly object Gate = new object();

        /// <summary>The id of the session the last Apply started (null: it wrote nothing) - for its watcher.</summary>
        internal static string LastSession;

        private static string StorePath(Vita3kLayout layout)
            => layout?.InstallDir == null ? null : Path.Combine(layout.InstallDir, StoreName);

        // ── a game's own, in our store ───────────────────────────────────────

        /// <summary>The game's own values of one section, attribute -> value; null when it has none.</summary>
        public static Dictionary<string, string> LoadSection(Vita3kLayout layout, string gameId, string section)
        {
            var all = LoadValues(layout, gameId);
            if (all == null) return null;
            var mine = all.Where(kv => kv.Key.StartsWith(section + "/", StringComparison.Ordinal))
                          .ToDictionary(kv => kv.Key.Substring(section.Length + 1), kv => kv.Value, StringComparer.Ordinal);
            return mine.Count > 0 ? mine : null;
        }

        /// <summary>Give the game its own values of one section - or take them away, with null or empty.
        /// The other sections it has are kept.</summary>
        public static void SaveSection(Vita3kLayout layout, string gameId, string section, Dictionary<string, string> values)
        {
            try
            {
                var path = StorePath(layout);
                if (path == null || string.IsNullOrWhiteSpace(gameId)) return;
                var all = LoadValues(layout, gameId) ?? new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in all.Keys.Where(k => k.StartsWith(section + "/", StringComparison.Ordinal)).ToList()) all.Remove(key);
                if (values != null) foreach (var kv in values) all[section + "/" + kv.Key] = kv.Value ?? "";

                var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                lines.RemoveAll(l => l.Split('\t')[0].Equals(gameId, StringComparison.OrdinalIgnoreCase));
                if (all.Count > 0)
                    lines.Add(gameId + "\t" + string.Join(";", all.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                                                  .Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value))));
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, string.Join("\r\n", lines) + (lines.Count > 0 ? "\r\n" : ""), new UTF8Encoding(false));
                File.Move(tmp, path, overwrite: true);
                Log.Info("game settings of " + gameId + ", " + section + ": "
                         + (values == null || values.Count == 0 ? "Vita3K's own" : string.Join(", ", values.Select(kv => kv.Key + "=" + kv.Value))));
            }
            catch (Exception ex) { Log.Warn("could not save a game's own settings", ex); }
        }

        /// <summary>This plugin's settings of a game PAUSED (Mehdi, 01/10): kept, not used at launch - the game runs on its
        /// own custom config, and what is changed there during the game is kept. A section of the store of its own.</summary>
        public const string PausedSection = "lbip-paused";

        public static bool IsPaused(Vita3kLayout layout, string gameId) => LoadSection(layout, gameId, PausedSection) != null;

        public static void SetPaused(Vita3kLayout layout, string gameId, bool paused)
            => SaveSection(layout, gameId, PausedSection, paused ? new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = "true" } : null);

        /// <summary>section/attribute -> value, or null.</summary>
        private static Dictionary<string, string> LoadValues(Vita3kLayout layout, string gameId)
        {
            try
            {
                var path = StorePath(layout);
                if (path == null || string.IsNullOrWhiteSpace(gameId) || !File.Exists(path)) return null;
                foreach (var line in File.ReadAllLines(path))
                {
                    var f = line.Split('\t');
                    if (f.Length < 2 || !f[0].Equals(gameId, StringComparison.OrdinalIgnoreCase)) continue;
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var part in f[1].Split(';'))
                    {
                        int eq = part.IndexOf('=');
                        if (eq <= 0) continue;
                        try { values[part.Substring(0, eq).Trim()] = Uri.UnescapeDataString(part.Substring(eq + 1).Trim()); } catch { }
                    }
                    return values.Count > 0 ? values : null;
                }
            }
            catch (Exception ex) { Log.Warn("could not read the games' own settings", ex); }
            return null;
        }

        /// <summary>The game's own system settings, or null. The System tab sets all four together.</summary>
        public static VitaSystemSettings Load(Vita3kLayout layout, string gameId)
            => ToSystem(LoadSection(layout, gameId, SystemSection));

        public static void Save(Vita3kLayout layout, string gameId, VitaSystemSettings s)
            => SaveSection(layout, gameId, SystemSection, s == null ? null : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Vita3kConfig.LanguageKey] = s.Language.ToString(CultureInfo.InvariantCulture),
                [Vita3kConfig.DateKey] = s.DateFormat.ToString(CultureInfo.InvariantCulture),
                [Vita3kConfig.TimeKey] = s.TimeFormat.ToString(CultureInfo.InvariantCulture),
                [Vita3kConfig.EnterKey] = s.EnterButton.ToString(CultureInfo.InvariantCulture),
                [Vita3kConfig.PstvKey] = s.Pstv == true ? "true" : "false",
            });

        public const string AdvancedSection = "advanced";

        /// <summary>The game's text set by hand, and whether it is the one in use; null when it has none.</summary>
        public static string LoadAdvanced(Vita3kLayout layout, string gameId, out bool on)
        {
            var v = LoadSection(layout, gameId, AdvancedSection);
            on = v != null && v.TryGetValue("on", out var o) && o == "true";
            return v != null && v.TryGetValue("xml", out var x) && !string.IsNullOrWhiteSpace(x) ? x : null;
        }

        public static void SaveAdvanced(Vita3kLayout layout, string gameId, string xml, bool on)
            => SaveSection(layout, gameId, AdvancedSection, string.IsNullOrWhiteSpace(xml) ? null
                : new Dictionary<string, string>(StringComparer.Ordinal) { ["on"] = on ? "true" : "false", ["xml"] = xml.Trim() });

        /// <summary>The partial xml of a set of section -> attributes - what the Advanced tab shows.</summary>
        public static string Partial(Dictionary<string, Dictionary<string, string>> sections)
        {
            var root = new XElement("config");
            foreach (var kv in sections.Where(s => s.Value != null && s.Value.Count > 0))
            {
                var e = new XElement(kv.Key);
                var order = Fields.Where(f => f.Section == kv.Key).Select(f => f.Name).ToList();
                foreach (var a in kv.Value.OrderBy(a => order.IndexOf(a.Key) < 0 ? int.MaxValue : order.IndexOf(a.Key)).ThenBy(a => a.Key, StringComparer.Ordinal))
                    e.SetAttributeValue(a.Key, a.Value);
                root.Add(e);
            }
            return root.ToString();
        }

        /// <summary>Is this text usable? Null error when it is; the warnings say what it may not do - an
        /// attribute config.yml does not know, a section the official Vita3K would read with defaults.</summary>
        public static List<string> CheckHand(Vita3kLayout layout, string titleId, string text, out string error)
        {
            error = null;
            var warnings = new List<string>();
            XElement root;
            try { root = XElement.Parse(text ?? "", LoadOptions.SetLineInfo); }
            catch (System.Xml.XmlException ex) { error = "line " + ex.LineNumber + ": " + ex.Message; return warnings; }
            if (root.Name != "config") { error = "the root element must be <config>, not <" + root.Name + ">"; return warnings; }
            if (!root.Elements().Any()) warnings.Add("nothing is set: the game runs on its custom config, else Vita3K's settings");

            var yml = Vita3kConfig.ReadKeys(layout);
            XElement own = null;
            try
            {
                var xml = PathOf(layout, titleId);
                var bak = xml == null ? null : BakOf(xml);
                var source = bak != null && File.Exists(bak) ? (new FileInfo(bak).Length == 0 ? null : bak) : (xml != null && File.Exists(xml) ? xml : null);
                own = source == null ? null : XDocument.Load(source).Root;
            }
            catch { }
            foreach (var section in root.Elements())
            {
                var name = section.Name.LocalName;
                bool known = Fields.Any(f => f.Section == name);
                foreach (var a in section.Attributes())
                    if (!yml.ContainsKey(a.Name.LocalName) && !Fields.Any(f => f.Section == name && f.Name == a.Name.LocalName))
                        warnings.Add("<" + name + "> " + a.Name.LocalName + ": not a key of config.yml - check its name (a few per-game names differ, like enable-ngs)");
                if (!known && own?.Element(name) == null)
                    warnings.Add("<" + name + ">: with the official Vita3K, every attribute of this section it does not name falls to false / 0 / empty "
                                 + "(the game's custom config has no <" + name + "> to fill it from) - name them all, or save the game's Custom Config in Vita3K once");
            }
            return warnings;
        }

        private static VitaSystemSettings ToSystem(Dictionary<string, string> v)
        {
            if (v == null) return null;
            int Of(string name, int d) => v.TryGetValue(name, out var x) && int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : d;
            return new VitaSystemSettings
            {
                Language = Of(Vita3kConfig.LanguageKey, Vita3kConfig.DefaultLanguage),
                DateFormat = Of(Vita3kConfig.DateKey, Vita3kConfig.DefaultDate),
                TimeFormat = Of(Vita3kConfig.TimeKey, Vita3kConfig.DefaultTime),
                EnterButton = Of(Vita3kConfig.EnterKey, Vita3kConfig.DefaultEnter),
                Pstv = v.TryGetValue(Vita3kConfig.PstvKey, out var p) ? p.Equals("true", StringComparison.OrdinalIgnoreCase) : (bool?)null,
            };
        }

        // ── what the game runs on without ours ───────────────────────────────

        /// <summary>The values of one section a game runs on WITHOUT settings of its own here - "default" in
        /// the Options window: its custom config's (the .bak mid-session), else config.yml's, else Vita3K's
        /// default. Every known attribute of the section.</summary>
        public static Dictionary<string, string> DefaultsOf(Vita3kLayout layout, string titleId, string section)
        {
            var yml = Vita3kConfig.ReadKeys(layout);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            XElement own = null;
            try
            {
                var xml = PathOf(layout, titleId);
                if (xml != null)
                {
                    var bak = BakOf(xml);
                    var source = File.Exists(bak) ? (new FileInfo(bak).Length == 0 ? null : bak) : (File.Exists(xml) ? xml : null);
                    own = source == null ? null : XDocument.Load(source).Root?.Element(section);
                }
            }
            catch (Exception ex) { Log.Warn("could not read the custom config of " + titleId, ex); }
            foreach (var f in Fields.Where(x => x.Section == section))
                values[f.Name] = (string)own?.Attribute(f.Name) ?? (yml.TryGetValue(f.Yml, out var y) ? y : null) ?? f.Default;
            return values;
        }

        /// <summary>The attributes of one section the game's OWN custom config sets (its set-aside copy
        /// mid-session) - for the options window's amber bars (OptionMarks). Empty when it has none.</summary>
        public static HashSet<string> SetByGame(Vita3kLayout layout, string titleId, string section)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var xml = PathOf(layout, titleId);
                if (xml == null) return set;
                var bak = BakOf(xml);
                var source = File.Exists(bak) ? (new FileInfo(bak).Length == 0 ? null : bak) : (File.Exists(xml) ? xml : null);
                var own = source == null ? null : XDocument.Load(source).Root?.Element(section);
                foreach (var a in own?.Attributes() ?? Enumerable.Empty<XAttribute>()) set.Add(a.Name.LocalName);
            }
            catch (Exception ex) { Log.Warn("could not read the custom config of " + titleId, ex); }
            return set;
        }

        /// <summary>The same for the System tab.</summary>
        public static VitaSystemSettings WithoutOurs(Vita3kLayout layout, string titleId)
            => ToSystem(DefaultsOf(layout, titleId, SystemSection));

        // ── the session ──────────────────────────────────────────────────────

        /// <summary>Put the game's own settings into config_&lt;TITLE_ID&gt;.xml for this session, the
        /// user's file kept aside first. True when there were any. Never throws.</summary>
        public static bool Apply(Vita3kLayout layout, string titleId, string gameId)
        {
            if (!System.Threading.Monitor.TryEnter(Gate, TimeSpan.FromSeconds(5)))
            {
                Log.Warn(titleId + ": another session's custom config is still being put back - this game runs without its own settings this time");
                return false;
            }
            try { return ApplyHeld(layout, titleId, gameId); }
            finally { System.Threading.Monitor.Exit(Gate); }
        }

        private static bool ApplyHeld(Vita3kLayout layout, string titleId, string gameId)
        {
            LastSession = null;
            try
            {
                var all = LoadValues(layout, gameId);
                var xml = PathOf(layout, titleId);
                if (all == null || xml == null) return false;
                if (all.ContainsKey(PausedSection + "/on"))
                {
                    Log.Info(titleId + ": this plugin's settings are paused for this game - it runs on its own custom config");
                    return false;
                }

                // Section -> the element to put over the user's: from the tabs' values, or the text set by hand.
                var over = new List<XElement>();
                bool hand = all.TryGetValue(AdvancedSection + "/on", out var on) && on == "true";
                if (hand)
                {
                    try { over.AddRange(XElement.Parse(all.TryGetValue(AdvancedSection + "/xml", out var t) ? t : "").Elements()); }
                    catch (Exception ex) { Log.Warn(titleId + ": the settings set by hand cannot be read (" + ex.Message + ") - this game runs without its own settings this time"); return false; }
                }
                else
                    foreach (var g in all.Where(kv => !kv.Key.StartsWith(AdvancedSection + "/", StringComparison.Ordinal) && !kv.Key.StartsWith(PausedSection + "/", StringComparison.Ordinal))
                                         .GroupBy(kv => kv.Key.Split('/')[0], StringComparer.Ordinal))
                        over.Add(new XElement(g.Key, g.Select(kv => new XAttribute(kv.Key.Substring(g.Key.Length + 1), kv.Value))));
                if (over.Count == 0) return false;
                var ours = string.Join(" ", over.Select(e => e.ToString(SaveOptions.DisableFormatting)));
                var bak = BakOf(xml);
                if (File.Exists(bak))
                {
                    // Restore runs before every launch: one still here could not be put back.
                    Log.Warn(titleId + ": the custom config of a previous session could not be put back - this game runs without its own settings this time");
                    return false;
                }

                XDocument original = null;
                if (File.Exists(xml))
                {
                    try { original = XDocument.Load(xml); }
                    catch (Exception ex) { Log.Warn(titleId + ": its custom config cannot be read (" + ex.Message + ") - left alone, this game runs without its own settings this time"); return false; }
                }

                // 1. THE .BAK FIRST: before a byte of the xml changes.
                Directory.CreateDirectory(Path.GetDirectoryName(xml));
                if (original != null) File.Copy(xml, bak, overwrite: false);
                else File.WriteAllBytes(bak, new byte[0]);

                // 2. The file for the session - and its note: its id, and what config.yml held of each of ours.
                var doc = Build(layout, original, over);
                var ymlNow = Vita3kConfig.ReadKeys(layout);
                var session = Guid.NewGuid().ToString("N");
                var note = new StringBuilder("session\t" + session + "\r\n");
                foreach (var e in over)
                    foreach (var a in e.Attributes())
                    {
                        var yml = Fields.FirstOrDefault(f => f.Section == e.Name.LocalName && f.Name == a.Name.LocalName)?.Yml;
                        if (yml == null) continue;
                        bool present = ymlNow.TryGetValue(yml, out var had);
                        note.Append("yml\t").Append(yml).Append('\t').Append(present ? "1" : "0").Append('\t').Append(Uri.EscapeDataString(had ?? ""))
                            .Append('\t').Append(Uri.EscapeDataString(a.Value)).Append("\r\n");
                    }
                File.WriteAllText(NoteOf(xml), note.ToString());
                LastSession = session;

                var tmp = xml + ".tmp";
                doc.Save(tmp);
                File.Move(tmp, xml, overwrite: true);
                Log.Info(titleId + ": this game's own settings for its session" + (hand ? ", set by hand" : "") + " - " + ours
                         + (original != null ? " (its custom config is kept aside, and comes back when Vita3K quits)" : ""));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(titleId + ": could not apply this game's own settings", ex);
                Restore(layout, titleId, "the write failed");
                return false;
            }
        }

        /// <summary>The file for a session: the user's own (or an empty one), and over it every section of
        /// <paramref name="over"/> - its attributes and lists over the user's, then, for a section this
        /// plugin knows, what neither names from config.yml and Vita3K's defaults. Writes nothing.</summary>
        private static XDocument Build(Vita3kLayout layout, XDocument original, List<XElement> over)
        {
                var doc = original != null && original.Root?.Name == "config"
                    ? new XDocument(original)
                    : new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("config"));
                var yml = Vita3kConfig.ReadKeys(layout);
                foreach (var mine in over)
                {
                    var section = mine.Name.LocalName;
                    var old = doc.Root.Element(section);
                    // From the user's own: an attribute we do not know is kept.
                    var built = old != null ? new XElement(old) : new XElement(section);
                    foreach (var a in mine.Attributes()) built.SetAttributeValue(a.Name, a.Value);
                    // A list (ime-langs, lle-modules) set by hand replaces the user's.
                    foreach (var child in mine.Elements())
                    {
                        built.Elements(child.Name).Remove();
                        built.Add(new XElement(child));
                    }
                    // Whole, for the official Vita3K: what neither ours nor the user's names.
                    foreach (var f in Fields.Where(x => x.Section == section))
                        if (built.Attribute(f.Name) == null)
                            built.SetAttributeValue(f.Name, (yml.TryGetValue(f.Yml, out var y) ? y : null) ?? f.Default);
                    if (section == SystemSection && !(built.Element("ime-langs")?.Elements("lang").Any() ?? false))
                    {
                        built.Element("ime-langs")?.Remove();
                        var (_, ime) = Vita3kConfig.PstvAndInputLanguages(layout);
                        built.Add(new XElement("ime-langs", (ime.Count > 0 ? ime : new List<string> { "4" }).Select(l => new XElement("lang", l))));
                    }
                    if (old != null) old.ReplaceWith(built); else doc.Root.Add(built);
                }
                return doc;
        }

        /// <summary>The file a launch would write from this partial xml - and, in <paramref name="before"/>,
        /// the user's own as it is now (the .bak's mid-session). Null, with the error, when the text is not
        /// usable. Writes nothing.</summary>
        public static string Preview(Vita3kLayout layout, string titleId, string partialXml, out string before, out string error)
        {
            before = ""; error = null;
            try
            {
                var root = XElement.Parse(string.IsNullOrWhiteSpace(partialXml) ? "<config />" : partialXml);
                if (root.Name != "config") { error = "the root element must be <config>"; return null; }
                var xml = PathOf(layout, titleId);
                var bak = xml == null ? null : BakOf(xml);
                var source = bak != null && File.Exists(bak) ? (new FileInfo(bak).Length == 0 ? null : bak) : (xml != null && File.Exists(xml) ? xml : null);
                XDocument original = null;
                if (source != null) { before = File.ReadAllText(source); original = XDocument.Parse(before); }
                var over = root.Elements().ToList();
                if (over.Count == 0) { error = "nothing is set: the game runs on its custom config as it is" + (source == null ? " (it has none)" : ""); return null; }
                var doc = Build(layout, original, over);
                var decl = doc.Declaration ?? new XDeclaration("1.0", "utf-8", null);
                return decl + "\n" + doc.ToString();
            }
            catch (System.Xml.XmlException ex) { error = "line " + ex.LineNumber + ": " + ex.Message; return null; }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        /// <summary>Put back every .bak of this install - or only <paramref name="titleId"/>'s; <paramref name="session"/>: only
        /// that session's (its watcher). Nothing while Vita3K runs. Never throws.</summary>
        public static void Restore(Vita3kLayout layout, string titleId, string why, string session = null)
        {
            // Busy: the other end of the same session holds it, and does the same work - nothing is lost by leaving.
            if (!System.Threading.Monitor.TryEnter(Gate, TimeSpan.FromSeconds(2)))
            {
                Log.Info("game settings: the custom configs are being put back already (" + why + ")");
                return;
            }
            try { RestoreHeld(layout, titleId, why, session); }
            finally { System.Threading.Monitor.Exit(Gate); }
        }

        private static void RestoreHeld(Vita3kLayout layout, string titleId, string why, string session)
        {
            try
            {
                var dir = ConfigDir(layout);
                if (dir == null || !Directory.Exists(dir)) return;
                var baks = titleId == null
                    ? Directory.GetFiles(dir, "config_*" + BakExtension)
                    : new[] { BakOf(PathOf(layout, titleId)) }.Where(File.Exists).ToArray();
                if (baks.Length == 0) return;
                if (Vita3kPaths.EmulatorRunning()) { Log.Info("game settings: Vita3K is running - its custom configs go back once it has quit"); return; }
                foreach (var bak in baks)
                {
                    var xml = Path.ChangeExtension(bak, ".xml");
                    try
                    {
                        // A watcher puts back only its own session (30/09).
                        if (session != null && (File.Exists(NoteOf(xml)) ? File.ReadLines(NoteOf(xml)).FirstOrDefault() : null) != "session\t" + session)
                        { Log.Info(Path.GetFileName(xml) + ": the session on is a later launch's - left to it (" + why + ")"); continue; }
                        // config.yml's copies of ours first, the note kept until that is done.
                        if (!UnLeak(layout, xml, out var unleaked)) { Log.Warn(Path.GetFileName(xml) + ": config.yml could not be put right - left to try again"); continue; }
                        if (unleaked.Count > 0) Log.Info(Path.GetFileName(xml) + ": config.yml had taken this plugin's " + string.Join(", ", unleaked) + " - put back");
                        try { if (File.Exists(NoteOf(xml))) File.Delete(NoteOf(xml)); } catch { }
                        if (new FileInfo(bak).Length == 0)
                        {
                            if (File.Exists(xml)) File.Delete(xml);
                            File.Delete(bak);
                            Log.Info(Path.GetFileName(xml) + ": the session's is removed - the game had no custom config (" + why + ")");
                        }
                        else
                        {
                            File.Move(bak, xml, overwrite: true);
                            Log.Info(Path.GetFileName(xml) + ": the user's custom config is back (" + why + ")");
                        }
                    }
                    catch (Exception ex) { Log.Warn("could not put " + Path.GetFileName(xml) + " back", ex); }
                }
            }
            catch (Exception ex) { Log.Warn("game settings: could not put the custom configs back", ex); }
        }

        /// <summary>What config.yml says of this plugin's settings that is ours, not what it held at launch - Vita3K's global
        /// window copied it there mid-session: back to what it held, or to Vita3K's default when it held nothing. False when
        /// config.yml could not be written.</summary>
        private static bool UnLeak(Vita3kLayout layout, string xml, out List<string> done)
        {
            var back = new Dictionary<string, string>(StringComparer.Ordinal);
            done = new List<string>();
            if (!File.Exists(NoteOf(xml))) return true;
            var now = Vita3kConfig.ReadKeys(layout);
            foreach (var line in File.ReadAllLines(NoteOf(xml)))
            {
                var f = line.Split('\t');
                if (f.Length != 5 || f[0] != "yml") continue;
                var key = f[1];
                bool present = f[2] == "1";
                var had = Uri.UnescapeDataString(f[3]);
                var mine = Uri.UnescapeDataString(f[4]);
                if (!now.TryGetValue(key, out var v) || !Same(v, mine)) continue;
                var was = present ? had : Fields.FirstOrDefault(x => x.Yml == key)?.Default;
                if (was != null && !Same(v, was)) back[key] = was;
            }
            done = back.Keys.ToList();
            return back.Count == 0 || Vita3kConfig.WriteKeys(layout, back);
        }

        /// <summary>The same meaning, as Vita3K reads a value: equal text, the same number, or the same truth (pugixml and
        /// yaml-cpp read "1"/"0", "yes"/"no" as truths).</summary>
        private static bool Same(string a, string b)
        {
            if (a == null || b == null) return a == b;
            var ta = a.Trim(); var tb = b.Trim();
            if (ta == tb) return true;
            if (Truth(ta, out var ba) && Truth(tb, out var bb)) return ba == bb;
            if (double.TryParse(ta, NumberStyles.Float, CultureInfo.InvariantCulture, out var da)
                && double.TryParse(tb, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
                return da == db || Math.Abs(da - db) <= 1e-6 * Math.Max(1, Math.Abs(da));
            return false;
        }

        private static bool Truth(string s, out bool v)
        {
            v = s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s == "1";
            return v || s.Equals("false", StringComparison.OrdinalIgnoreCase) || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s == "0";
        }
    }
}
