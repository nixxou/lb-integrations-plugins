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
}
