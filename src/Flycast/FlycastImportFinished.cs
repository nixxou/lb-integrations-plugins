// After an import to Flycast: the arcade sets LaunchBox left out, put back as versions of their game.
//
// Measured 30/09 (Naomi 2, 45 sets asked for, 41 in): LaunchBox files a set as a version of a game only
// when their MAME titles are identical. vf4b, vf4o, vf4cart ("Virtua Fighter 4") and clubkprz ("Club Kart
// Prize") fall on the same game of its database as a game already made under another title - 152524
// "Virtua Fighter 4 Version C" (vf4), 36060 "Club Kart Prize Version B" (clubkpzbp) - so they end up
// neither a version nor a game. Put back here, THROUGH THE DATA API ONLY, and only those: a file
// LaunchBox imported is never touched.
//
// ONLY WHAT THE IMPORT'S CHOICES LET IN (Mehdi, 30/09): the wizard's MAME page says which clones and which
// kinds of game to import; a set it would have left out stays out - a clone when "Import all clones" was not
// chosen, a quiz game when "Skip quiz games" was ticked, and so on (RefusedByChoices).
//
// For each file left out: its title in the list -> the game of the database it names on the platform
// (LbipMetadataDb, LaunchBox's own answer) -> the ONE game of the library holding that id -> a version
// of it, made the way LaunchBox makes its own: "Play <version> Version...", the MAME entry's version,
// region, publisher, developer, status and year (Metadata\MAME.xml, LaunchBox's copy), the game's
// emulator, the next priority. No game, or more than one: nothing, and the log says why.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Flycast
{
    internal sealed class FlycastImportFinished : ILbImportFinished
    {
        public void AfterImport(LbImportDone done)
        {
            try { Run(done); }
            catch (Exception ex) { Log.Warn("[import] after the import: could not put the left-out sets back", ex); }
        }

        private static void Run(LbImportDone done)
        {
            if (done == null || !FlycastPaths.IsFlycastExecutable(done.EmulatorPath)) return;
            var system = FlycastPlatforms.SystemOf(done.Platform) ?? FlycastPlatforms.SystemOf(done.ScrapeAs);
            Log.Info("[import] after the import to " + done.Platform + ": " + done.Imported.Count + "/" + done.Wanted.Count
                     + " in the library" + (done.Complete ? "" : " (gave up waiting)"));
            if (system == null || system == "Dreamcast" || done.Missing.Count == 0) return;
            if (!FlycastSettings.RepairImport)
            {
                Log.Info("[import] " + done.Missing.Count + " set(s) left out by LaunchBox, left so: putting them back is turned off in the settings");
                return;
            }

            // The database names its platforms as LaunchBox's own; a custom platform scraped as one uses that.
            var dbPlatform = string.IsNullOrEmpty(done.ScrapeAs) ? done.Platform : done.ScrapeAs;
            var games = PluginHelper.DataManager?.GetPlatformByName(done.Platform)?.GetAllGames(true, true) ?? Array.Empty<IGame>();
            // Read again now: a file LaunchBox added after all, since the watch said "in", is its own and
            // left alone - an early answer must never make a duplicate.
            var inLibrary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in games)
            {
                var p = LbipImportWatch.Full(g.ApplicationPath);
                if (p != null) inLibrary.Add(p);
                foreach (var a in g.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>())
                {
                    var ap = LbipImportWatch.Full(a.ApplicationPath);
                    if (ap != null) inLibrary.Add(ap);
                }
            }
            var mame = MameEntries(done.Missing.Select(p => Path.GetFileNameWithoutExtension(p)));
            int added = 0;
            foreach (var path in done.Missing)
            {
                var file = Path.GetFileName(path);
                if (inLibrary.Contains(path)) { Log.Info("[import]   " + file + " is in the library after all - left alone"); continue; }
                done.TitleOf.TryGetValue(path, out var title);
                var id = LbipMetadataDb.DatabaseIdOf(title, dbPlatform, out var how);
                if (id == null) { Log.Info("[import]   " + file + " \"" + title + "\" left as LaunchBox left it: " + how); continue; }
                var targets = games.Where(g => g.LaunchBoxDbId == id).ToList();
                if (targets.Count != 1)
                {
                    Log.Info("[import]   " + file + " \"" + title + "\" (game " + id + ", " + how + ") left as LaunchBox left it: "
                             + (targets.Count == 0 ? "no game of the library holds that id" : targets.Count + " games of the library hold it"));
                    continue;
                }
                var game = targets[0];
                var apps = game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>();
                if (apps.Any(a => string.Equals(LbipImportWatch.Full(a.ApplicationPath), path, StringComparison.OrdinalIgnoreCase))) continue;

                mame.TryGetValue(Path.GetFileNameWithoutExtension(path), out var m);
                var refused = RefusedByChoices(Options(done), m);
                if (refused != null) { Log.Info("[import]   " + file + " \"" + title + "\" left out, as the import's choices say: " + refused); continue; }
                var version = !string.IsNullOrWhiteSpace(m?.Version) ? m.Version : "(" + Path.GetFileNameWithoutExtension(path) + ")";
                var app = game.AddNewAdditionalApplication();
                app.ApplicationPath = LbipImportWatch.AsLaunchBoxWrites(path);
                app.Name = "Play " + version + " Version...";
                app.UseEmulator = true;
                app.EmulatorId = game.EmulatorId;
                app.Version = version;
                if (m != null)
                {
                    app.Region = m.Region;
                    app.Publisher = m.Publisher;
                    app.Developer = m.Developer;
                    app.Status = m.Status;
                    if (int.TryParse(m.Year, out var year) && year > 1900) app.ReleaseDate = new DateTime(year, 1, 1);
                }
                app.Priority = apps.Length == 0 ? 1 : apps.Max(a => a.Priority) + 1;
                added++;
                Log.Info("[import]   " + file + " \"" + title + "\" put back as a version of \"" + game.Title + "\" (game " + id + ", " + how + "): " + app.Name);
            }
            if (added == 0) return;
            PluginHelper.DataManager.Save(true);
            Log.Info("[import] " + added + " set(s) LaunchBox had left out put back as versions, saved through the data API");
        }

        // ── what the import's MAME page would have let in (Mehdi, 30/09) ─────────

        /// <summary>The page's box, and the MAME entry's flag it skips.</summary>
        private static readonly (string Skip, string Flag)[] SkipFlags =
        {
            ("SkipHacks", "IsHack"), ("SkipPrototypes", "IsPrototype"), ("SkipBootleg", "IsBootleg"),
            ("SkipMechanical", "IsMechanical"), ("SkipMature", "IsMature"), ("SkipQuiz", "IsQuiz"),
            ("SkipFruit", "IsFruit"), ("SkipCasino", "IsCasino"), ("SkipRhythm", "IsRhythm"),
            ("SkipTabletop", "IsTableTop"), ("SkipPlayChoice", "IsPlayChoice"), ("SkipMahjong", "IsMahjong"),
            ("SkipNonArcade", "IsNonArcade"),
        };

        private const string MamePage = "RomImportMameOptionsViewModel.";

        /// <summary>The import's choices - none when the Catalog in the process predates them.</summary>
        private static Dictionary<string, string> Options(LbImportDone done)
        {
            try { return OptionsCore(done) ?? new Dictionary<string, string>(); }
            catch { return new Dictionary<string, string>(); }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static Dictionary<string, string> OptionsCore(LbImportDone done) => done.Options;

        /// <summary>Why the import's choices would have left this set out - or null. A clone only when "Import all
        /// clones" was chosen; nothing a ticked "Skip ..." box names. Without the page (another wizard, an older
        /// Catalog) or without the set's MAME entry, nothing is refused.</summary>
        private static string RefusedByChoices(Dictionary<string, string> options, MameEntry m)
        {
            if (m == null) return null;
            string Opt(string name) => options.TryGetValue(MamePage + name, out var v) ? v : null;
            bool On(string name) => string.Equals(Opt(name), "True", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(m.Field("CloneOf")) && Opt("ImportAllClones") != null && !On("ImportAllClones"))
                return "a clone of " + m.Field("CloneOf") + ", and clones were not all to be imported";
            foreach (var (skip, flag) in SkipFlags)
                if (On(skip) && string.Equals(m.Field(flag), "true", StringComparison.OrdinalIgnoreCase)) return skip + " was ticked";
            if (On("SkipUnplayable") && string.Equals(m.Status, "preliminary", StringComparison.OrdinalIgnoreCase))
                return "SkipUnplayable was ticked (MAME says preliminary)";
            return null;
        }

        private sealed class MameEntry
        {
            public string Version, Region, Publisher, Developer, Status, Year;
            public Dictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal);
            public string Field(string name) => All.TryGetValue(name, out var v) ? v : null;
        }

        /// <summary>The entries of LaunchBox's Metadata\MAME.xml for these sets, read as a stream. Empty when it is not there.</summary>
        private static Dictionary<string, MameEntry> MameEntries(IEnumerable<string> sets)
        {
            var found = new Dictionary<string, MameEntry>(StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(sets, StringComparer.OrdinalIgnoreCase);
            var file = Path.Combine(LbipImportWatch.LaunchBoxRoot(), "Metadata", "MAME.xml");
            if (wanted.Count == 0 || !File.Exists(file)) return found;
            try
            {
                using var reader = XmlReader.Create(file, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true, DtdProcessing = DtdProcessing.Ignore });
                while (found.Count < wanted.Count && reader.ReadToFollowing("MameFile"))
                {
                    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    using (var one = reader.ReadSubtree())
                    {
                        one.Read();                     // <MameFile>
                        one.Read();                     // its first child
                        while (!one.EOF)
                        {
                            // ReadElementContentAsString moves on by itself; anything else is stepped over.
                            if (one.NodeType == XmlNodeType.Element && one.Depth == 1)
                            {
                                var name = one.LocalName;
                                fields[name] = one.ReadElementContentAsString();
                            }
                            else one.Read();
                        }
                    }
                    if (!fields.TryGetValue("FileName", out var set) || !wanted.Contains(set)) continue;
                    fields.TryGetValue("Version", out var v); fields.TryGetValue("Region", out var r);
                    fields.TryGetValue("Publisher", out var p); fields.TryGetValue("Developer", out var d);
                    fields.TryGetValue("Status", out var s); fields.TryGetValue("Year", out var y);
                    found[set] = new MameEntry { Version = v, Region = r, Publisher = p, Developer = d, Status = s, Year = y, All = fields };
                }
            }
            catch (Exception ex) { Log.Info("[import] MAME.xml could not be read (" + ex.GetType().Name + ": " + ex.Message + ") - versions named by their set"); }
            return found;
        }
    }
}
