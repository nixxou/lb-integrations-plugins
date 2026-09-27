// The one table this installer works from, and it works from it in BOTH directions.
//
// Install writes what is here; uninstall removes what is here; "is it installed?" asks about what is
// here. Two lists that have to agree are two lists that will not - the lesson is written three times
// over in LiteBox's own Uninstaller, which derives its delete list from its deploy list for exactly
// this reason.
//
// THE FOLDER NAMES ARE THE PACK'S NAMES, not the emulators'. A plugin folder is how LaunchBox and
// LiteBox name a plugin: it is what Options > Plugins shows, and what LiteBox.ini's EnabledPlugins
// line is keyed on. Keep this table in step with deploy-dev.ps1's $Pack - they are the two places a
// folder name is decided, and a disagreement means a user runs two copies of one plugin.

namespace NixxIntegrations;

/// <summary>One file: where it is carried, which plugin folder it belongs to, and what it is called
/// once it is there.</summary>
internal sealed record PayloadFile(string Resource, string Folder, string Relative);

internal static class Payload
{
    public const string Flycast = "Nixx-Flycast";
    public const string MelonDs = "Nixx-melonDS";
    public const string NoGba   = "Nixx-nogba";
    public const string Ppsspp  = "Nixx-PPSSPP";
    public const string Vita3k  = "Nixx-Vita3K";
    public const string Xenia   = "Nixx-Xenia";

    public static readonly string[] Folders =
        { Flycast, MelonDs, NoGba, Ppsspp, Vita3k, Xenia };

    /// <summary>Every folder name this pack has been installed under before the rename. They are
    /// swept on install, because PluginLoader dedupes by FILE NAME across plugin roots: a stale
    /// "melonDS Integration" beside "Nixx-melonDS" is two copies of MelonDs.dll, and which one wins
    /// is not something a user can see or control.
    ///
    /// Two spellings per plugin where the two writers disagreed: deploy-dev.ps1 named the folder
    /// after the PROJECT ("MelonDs Integration") and the manifest after the EMULATOR ("melonDS
    /// Integration"), so an install carries whichever one put it there.</summary>
    public static readonly string[] StaleFolders =
    {
        "Flycast Integration",
        "MelonDs Integration", "melonDS Integration",
        "NoGba Integration",   "no$gba Integration",
        "Ppsspp Integration",  "PPSSPP Integration",
        "Xenia Integration",

        // And the one this pack itself published for a day: no$gba's row carried the emulator's
        // dollar until LaunchBox was measured never calling GetInstallableVersions for it.
        "Nixx-no$gba",
    };

    /// <summary>The assembly names. A folder is only ever removed when it holds one of these:
    /// a folder that merely carries a name we recognise is still somebody else's folder, and
    /// deleting it on the strength of its name alone is how an installer destroys something it was
    /// never told about.</summary>
    public static readonly string[] Assemblies =
        { "Flycast.dll", "MelonDs.dll", "NoGba.dll", "Ppsspp.dll", "Vita3k.dll", "Xenia.dll", "NixxMenus.dll" };

    /// <summary>The menu relay: the pack's right-click entries on games, shown for the plugins.
    ///
    /// ALWAYS IN THE CLASSIC Plugins\ ROOT, whatever the version - and so NOT in Folders, whose
    /// entries the install sweeps out of the root it is not using. Measured on LaunchBox 14: a
    /// plugin in Local\Plugins gets its emulator role and nothing else, and its menu is never asked
    /// a question; the same entry in a bare DLL under Plugins\ shows at once. No manifest: the
    /// classic root takes a bare DLL, and this one declares nothing the managed root would read.
    /// See src\Menus\Menus.cs.</summary>
    public const string Menus = "Nixx-Menus";

    public static readonly PayloadFile[] LegacyFiles =
    {
        new("payload/Nixx-Menus/NixxMenus.dll", Menus, "NixxMenus.dll"),
    };

    // The DSi NAND library, and the command-line tool beside it. ONE copy is carried and TWO are
    // written: the DllImport resolver in Shared.Dsi only ever looks inside the plugin's own folder,
    // so melonDS and no$gba each need their own.
    //
    private const string NandLib  = "payload/native/melonds-nand.dll";
    private const string NandTool = "payload/native/melonds-nandtool.exe";

    // Vita3K's install library - PFS decryption and firmware - loaded by the Vita3K plugin. See
    // src\Vita3k\Vita3kNative.cs and tools\vita3k-install.
    private const string V3kLib = "payload/native/vita3k-install.dll";

    // THE CATALOGUE CONTRACT, the one managed file that is not the plugin itself. It cannot be
    // merged in: a host and a plugin have to mean the SAME interface type, and type identity in
    // .NET is per-assembly - internalized into each plugin it would become as many private types as
    // there are plugins, none of which a host could name. So one copy goes into each plugin folder,
    // all identical, and whichever the load
    // context reaches first serves the whole process. See src\Catalog\LbCatalog.cs.
    private const string Contract = "payload/LbIntegrations.Catalog.dll";

    // The library lands under native\ and WITHOUT the .dll extension. That is not tidiness.
    // LaunchBox loads every .dll in a plugin folder as a .NET assembly, and a native one there
    // produces "System.BadImageFormatException: Bad IL format ... failed to load during
    // PluginLoader.LoadAssembly" plus an error dialog at every start - measured, with the dialog.
    // The plugin reaches it by path, through a DllImport resolver.
    public static readonly PayloadFile[] Files =
    {
        new("payload/Nixx-Flycast/Flycast.dll",     Flycast, "Flycast.dll"),
        new("payload/Nixx-Flycast/manifest.json",   Flycast, "manifest.json"),
        new(Contract,                               Flycast, "LbIntegrations.Catalog.dll"),

        new("payload/Nixx-melonDS/MelonDs.dll",     MelonDs, "MelonDs.dll"),
        new("payload/Nixx-melonDS/manifest.json",   MelonDs, "manifest.json"),
        new(Contract,                               MelonDs, "LbIntegrations.Catalog.dll"),
        new(NandLib,                                MelonDs, @"native\melonds-nand.native"),
        new(NandTool,                               MelonDs, @"native\melonds-nandtool.exe"),

        new("payload/Nixx-nogba/NoGba.dll",         NoGba,   "NoGba.dll"),
        new("payload/Nixx-nogba/manifest.json",     NoGba,   "manifest.json"),
        new(Contract,                               NoGba,   "LbIntegrations.Catalog.dll"),
        new(NandLib,                                NoGba,   @"native\melonds-nand.native"),
        new(NandTool,                               NoGba,   @"native\melonds-nandtool.exe"),

        new("payload/Nixx-PPSSPP/Ppsspp.dll",       Ppsspp,  "Ppsspp.dll"),
        new("payload/Nixx-PPSSPP/manifest.json",    Ppsspp,  "manifest.json"),
        new(Contract,                               Ppsspp,  "LbIntegrations.Catalog.dll"),

        new("payload/Nixx-Vita3K/Vita3k.dll",       Vita3k,  "Vita3k.dll"),
        new("payload/Nixx-Vita3K/manifest.json",    Vita3k,  "manifest.json"),
        new(Contract,                               Vita3k,  "LbIntegrations.Catalog.dll"),
        new(V3kLib,                                 Vita3k,  @"native\vita3k-install.native"),

        new("payload/Nixx-Xenia/Xenia.dll",         Xenia,   "Xenia.dll"),
        new("payload/Nixx-Xenia/manifest.json",     Xenia,   "manifest.json"),
        new(Contract,                               Xenia,   "LbIntegrations.Catalog.dll"),
    };


    /// <summary>Files that do NOT belong to a plugin folder.
    ///
    /// One thing so far: the RAM disk helper, which goes to
    /// &lt;LaunchBox&gt;\ThirdParty\RomExtractor\ramdisk\ - the folder LITEBOX uses. That is the whole
    /// point. LiteBox already ships this helper, one elevated scheduled task drives it, and the
    /// ImDisk driver behind it is installed once by the user; a second copy of all that under a name
    /// of our own would be two tasks doing one job.
    ///
    /// A SEPARATE TABLE RATHER THAN A FOURTH FIELD, because these files have a different lifetime.
    /// Files is what install writes and uninstall removes; this is written only when it is ABSENT and
    /// is never removed, because the copy sitting there may be LiteBox's and taking it away would
    /// break its RAM disk. The paths are relative to the LaunchBox root, not to a plugin folder.</summary>
    public static readonly (string Resource, string Relative)[] SharedFiles =
    {
        ("payload/ramdisk/RamDiskHelper.exe",
             @"ThirdParty\RomExtractor\ramdisk\RamDiskHelper.exe"),
        ("payload/ramdisk/RamDiskHelper.dll",
             @"ThirdParty\RomExtractor\ramdisk\RamDiskHelper.dll"),
        ("payload/ramdisk/RamDiskHelper.deps.json",
             @"ThirdParty\RomExtractor\ramdisk\RamDiskHelper.deps.json"),
        ("payload/ramdisk/RamDiskHelper.runtimeconfig.json",
             @"ThirdParty\RomExtractor\ramdisk\RamDiskHelper.runtimeconfig.json"),
    };

    /// <summary>What an older deploy put in the folder LaunchBox scans, rather than under native\.
    /// Left there, the BadImageFormatException dialog comes back at every start.</summary>
    public static readonly string[] StaleInFolder = { "melonds-nand.dll", "melonds-nandtool.exe" };

    /// <summary>Does this folder hold one of our assemblies? The question every removal asks before
    /// it removes anything.</summary>
    public static bool IsOurs(string dir)
    {
        try { return Assemblies.Any(a => File.Exists(Path.Combine(dir, a))); }
        catch { return false; }
    }
}
