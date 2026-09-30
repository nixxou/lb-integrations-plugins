// How this pack tells a host about the emulators it integrates - by being asked, rather than by
// patching the host's own database reads.
//
// THE PROBLEM THIS EXISTS TO SOLVE. LaunchBox builds its Add Emulator list from
// LaunchBox.Metadata.db, a file we do not own and must not write to. There is no API for adding a
// row, so the only way in is LbipRowInjection: two Harmony postfixes on
// SqliteCommand.ExecuteDbDataReader that chain our rows onto the reader LaunchBox is about to get.
// It works, it is measured, and it is a hack on somebody else's closed source. It is the price of
// integrating with a host that offers no other door.
//
// A HOST THAT OFFERS A DOOR SHOULD NOT BE PATCHED. This assembly is that door. A host references it,
// finds the plugins that implement ILbCatalogSource, asks them what emulators they bring, and merges
// the answer into its own list. No Harmony, no SQLite, no reaching into anything - a method call
// with a typed answer, which is what the arrangement should have been all along.
//
// WHY A SEPARATE ASSEMBLY AND NOT SHARED SOURCE. Type identity in .NET is per-assembly. Source
// compiled into five plugins is five unrelated types with the same name, and `is ILbCatalogSource`
// answers no to every one of them. One assembly, loaded once, is the only way a host and a plugin
// can mean the same interface. It ships BESIDE each plugin rather than being provided by the host,
// because under LaunchBox nothing would provide it and a plugin whose interface will not resolve is
// a plugin that does not load at all.
//
// It has no dependencies, nothing to configure and no behaviour. That is deliberate: every version
// of it must be interchangeable with every other, since one copy wins for the whole process.

using System.Collections.Generic;

namespace LbIntegrations.Catalog
{
    /// <summary>A plugin that can describe the emulators it integrates.</summary>
    public interface ILbCatalogSource
    {
        /// <summary>The emulators this plugin brings, as rows for the host's own catalogue. Called
        /// on the host's schedule and possibly more than once, so it must be cheap and must not
        /// depend on anything having happened first.</summary>
        IEnumerable<LbCatalogEmulator> EmulatorRows();
    }

    /// <summary>One emulator, in the shape both LaunchBox's metadata and LiteBox's preset list
    /// happen to want. The field names are the column names of LaunchBox's "Emulators" table, not
    /// out of deference but because that is the vocabulary every host in this family already
    /// speaks.</summary>
    public sealed class LbCatalogEmulator
    {
        /// <summary>What the host shows, and what identifies the row. It is a primary key in
        /// LaunchBox's own table, so a name can only ever mean one emulator - which is why this
        /// pack publishes under its own prefix rather than over a name the host already uses.</summary>
        public string Name;

        /// <summary>The default command line for a new entry.</summary>
        public string CommandLine;

        /// <summary>Semicolon-separated, each with its dot: ".iso; .chd".</summary>
        public string ApplicableFileExtensions;

        /// <summary>Where the emulator comes from, shown beside the name.</summary>
        public string Url;

        /// <summary>The executable to look for, wildcards allowed: "PPSSPP*.exe".</summary>
        public string BinaryFileName;

        /// <summary>Should the host unpack an archive before launching?</summary>
        public bool AutoExtract;

        public List<LbCatalogPlatform> Platforms = new List<LbCatalogPlatform>();
    }

    /// <summary>One platform this emulator covers.</summary>
    public sealed class LbCatalogPlatform
    {
        public string Platform;
        public string ApplicableFileExtensions;

        /// <summary>Ticked by default in the host's platform list.</summary>
        public bool Recommended;

        /// <summary>A BIOS the platform needs, named so the host can say so: "naomi.zip".</summary>
        public string RequiredBiosFile;
    }

    /// <summary>Whether the host has said it will ask.
    ///
    /// A HOST SETS THIS BEFORE IT CONSTRUCTS ANY PLUGIN, and a plugin reads it to decide whether the
    /// Harmony injection is needed. Unset - which is what LaunchBox leaves it as, having never heard
    /// of this assembly - means the plugin falls back to patching, so the default is the behaviour
    /// that works everywhere.
    ///
    /// A static crossing two codebases only works because one copy of this assembly is loaded for
    /// the whole process: the plugin folder's copy and the host's copy have the same identity, so
    /// the default load context hands both sides the same one. That is also why this assembly must
    /// never grow a dependency or a version that matters.</summary>
    public static class LbCatalog
    {
        /// <summary>Set to true by a host that reads ILbCatalogSource itself. Setting it after
        /// plugins have been constructed does nothing useful - they have already decided.</summary>
        public static bool HostWillAsk;
    }

    // ── AN EMULATOR OPENED WITHOUT A GAME ───────────────────────────────────────────────────────
    //
    // Added 29/09, never to be changed (see the header). When the host opens an emulator on its own -
    // LaunchBox's "Open emulator" menu, measured: Process.Start of the emulator's executable, no
    // arguments, from OpenEmulatorMenuAction.OnSelect - the plugins that care are told before it
    // starts and after it has quit. What for: a game's settings still in the emulator's files after a
    // session that never ended, a RAM disk left behind - put right before somebody edits the
    // emulator's own settings over them.
    //
    // ONE TELLER, ANY NUMBER OF LISTENERS. A plugin registers its listener here. Under LaunchBox the
    // first plugin to start installs ONE Harmony patch on Process.Start and says so (Patched); the
    // others see it and only register - the same shape as the catalogue rows. A host that opens
    // emulators itself (LiteBox) calls Opening/Exited directly and needs no patch at all.

    /// <summary>A plugin that wants to know when its emulator is opened without a game.</summary>
    public interface ILbEmulatorOpened
    {
        /// <summary>Before the emulator at <paramref name="exePath"/> (full path) starts. On the host's
        /// thread: quick, and never throwing - a failure here must not stop the emulator.</summary>
        void BeforeOpen(string exePath);

        /// <summary>After it has quit. On a background thread.</summary>
        void AfterExit(string exePath);
    }

    /// <summary>The listeners, and the one patch that feeds them.</summary>
    public static class LbEmulatorOpened
    {
        private static readonly object Gate = new object();
        private static readonly List<ILbEmulatorOpened> Listeners = new List<ILbEmulatorOpened>();

        /// <summary>Set by the plugin that installed the Process.Start patch. The others read it and do
        /// not patch again.</summary>
        public static bool Patched;

        /// <summary>Register a listener - once per type: the host builds a plugin more than once.</summary>
        public static void Register(ILbEmulatorOpened listener)
        {
            if (listener == null) return;
            lock (Gate)
            {
                if (Listeners.Exists(l => l.GetType().FullName == listener.GetType().FullName)) return;
                Listeners.Add(listener);
            }
        }

        /// <summary>The emulator at <paramref name="exePath"/> is about to be opened without a game.</summary>
        public static void Opening(string exePath)
        {
            foreach (var l in Snapshot())
                try { l.BeforeOpen(exePath); } catch { }
        }

        /// <summary>It has quit.</summary>
        public static void Exited(string exePath)
        {
            foreach (var l in Snapshot())
                try { l.AfterExit(exePath); } catch { }
        }

        private static List<ILbEmulatorOpened> Snapshot()
        {
            lock (Gate) return new List<ILbEmulatorOpened>(Listeners);
        }
    }

    // ── AFTER AN IMPORT: THE GAMES ARE IN ───────────────────────────────────────────────────────
    //
    // Added 30/09, never to be changed (see the header). Once a host has put the games of an import
    // into its library, the plugins that care are told what was asked for and what really went in.
    // What for, first: LaunchBox drops some files without a word (measured 30/09 - four Naomi 2 sets
    // whose MAME title differs from the game of its database they fall on); the plugin of the emulator
    // can put them back, through the host's own data API.
    //
    // "IN" MEANS IN THE LIBRARY, NOT FINISHED. A host goes on filling metadata and media afterwards;
    // this is told before that, as soon as the games exist. Same shape as the emulator opened above:
    // under LaunchBox the first plugin to start watches the import wizard and says so (Watching); the
    // others only register. A host with an import of its own calls Finished itself.

    /// <summary>A plugin that wants to know what an import has put in the library.</summary>
    public interface ILbImportFinished
    {
        /// <summary>After the games of <paramref name="done"/> are in the host's library. On the host's
        /// thread, so the data API can be used; never throwing.</summary>
        void AfterImport(LbImportDone done);
    }

    /// <summary>One import, as asked and as it went in. Every path is full.</summary>
    public sealed class LbImportDone
    {
        /// <summary>The platform the games went to, and the one they were scraped as.</summary>
        public string Platform, ScrapeAs;

        /// <summary>The executable of the emulator chosen for them, or null when none was.</summary>
        public string EmulatorPath;

        /// <summary>The files the import was asked for - its list when it was confirmed.</summary>
        public List<string> Wanted = new List<string>();

        /// <summary>The title each of them had in that list.</summary>
        public Dictionary<string, string> TitleOf = new Dictionary<string, string>();

        /// <summary>Those the library has, as a game or as a version of one.</summary>
        public List<string> Imported = new List<string>();

        /// <summary>Those it does not.</summary>
        public List<string> Missing = new List<string>();

        /// <summary>False when the host gave up waiting: what is in may still grow.</summary>
        public bool Complete;

        /// <summary>The choices the import was made with, as "page.option" -> value ("RomImportMameOptionsViewModel.SkipQuiz"
        /// -> "True" under LaunchBox): what a plugin puts back must be what those choices would have let in.</summary>
        public Dictionary<string, string> Options = new Dictionary<string, string>();
    }

    /// <summary>The listeners, and whoever watches the import to tell them.</summary>
    public static class LbImportFinished
    {
        private static readonly object Gate = new object();
        private static readonly List<ILbImportFinished> Listeners = new List<ILbImportFinished>();

        /// <summary>Set by the plugin that watches the host's import. The others read it and do not
        /// watch again.</summary>
        public static bool Watching;

        /// <summary>Register a listener - once per type: the host builds a plugin more than once.</summary>
        public static void Register(ILbImportFinished listener)
        {
            if (listener == null) return;
            lock (Gate)
            {
                if (Listeners.Exists(l => l.GetType().FullName == listener.GetType().FullName)) return;
                Listeners.Add(listener);
            }
        }

        /// <summary>The games of <paramref name="done"/> are in.</summary>
        public static void Finished(LbImportDone done)
        {
            List<ILbImportFinished> all;
            lock (Gate) all = new List<ILbImportFinished>(Listeners);
            foreach (var l in all)
                try { l.AfterImport(done); } catch { }
        }
    }

    // ── TO COME: A HOST'S OWN IMPORT, ASKING THE EMULATOR'S PLUGIN ─────────────────────────────
    //
    // Not written yet - noted here because this is where it will go (Mehdi, 28/09). LiteBox has no
    // import wizard today; when it has one, at the END of it, over the listing it is about to import,
    // it calls the plugin of the emulator chosen. Same door as the catalogue: a host asks, a plugin
    // answers, nothing is patched or watched.
    //
    // What LaunchBox's side does today by reflection on its wizard (Vita3kLbImport, then
    // Vita3kImportCleanup) is exactly what that call will return in a typed answer, per file:
    //   - keep it, under this title (the Vita plugin reads it from the param.sfo);
    //   - or leave it out, and why (an update or a DLC - installed with its game, not imported as one;
    //     a .pkg without its licence; not content this emulator runs).
    // The plugin records what it learns on the way (the Vita plugin: its extras index). A plugin that
    // has nothing to say about a file keeps it as the host has it. The shape to settle when it is
    // written: an interface next to ILbCatalogSource (say ILbImportReview), one method over the
    // listing - paths, the platform and the emulator chosen - on the host's thread, allowed to take
    // time (it reads every file). ADDED, NEVER CHANGED: this assembly is one copy for the whole process,
    // and every version of it must stay interchangeable (see the header).
}
