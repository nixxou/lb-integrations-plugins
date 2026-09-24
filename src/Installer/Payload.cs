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
    public const string NoGba   = "Nixx-no$gba";
    public const string Ppsspp  = "Nixx-PPSSPP";
    public const string Xenia   = "Nixx-Xenia";

    public static readonly string[] Folders = { Flycast, MelonDs, NoGba, Ppsspp, Xenia };

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
    };

    /// <summary>The five assembly names. A folder is only ever removed when it holds one of these:
    /// a folder that merely carries a name we recognise is still somebody else's folder, and
    /// deleting it on the strength of its name alone is how an installer destroys something it was
    /// never told about.</summary>
    public static readonly string[] Assemblies =
        { "Flycast.dll", "MelonDs.dll", "NoGba.dll", "Ppsspp.dll", "Xenia.dll" };

    // The DSi NAND library, and the command-line tool beside it. ONE copy is carried and TWO are
    // written: the DllImport resolver in Shared.Dsi only ever looks inside the plugin's own folder,
    // so melonDS and no$gba each need their own.
    //
    // The resource path says "nogba" where the folder says "no$gba" on purpose: a resource name is
    // an MSBuild LogicalName, and a dollar in one is a property expansion waiting to happen.
    private const string NandLib  = "payload/native/melonds-nand.dll";
    private const string NandTool = "payload/native/melonds-nandtool.exe";

    // THE CATALOGUE CONTRACT, the one managed file that is not the plugin itself. It cannot be
    // merged in: a host and a plugin have to mean the SAME interface type, and type identity in
    // .NET is per-assembly - internalized into five DLLs it would become five private types no host
    // could name. So one copy goes into each plugin folder, all identical, and whichever the load
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

        new("payload/Nixx-Xenia/Xenia.dll",         Xenia,   "Xenia.dll"),
        new("payload/Nixx-Xenia/manifest.json",     Xenia,   "manifest.json"),
        new(Contract,                               Xenia,   "LbIntegrations.Catalog.dll"),
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
