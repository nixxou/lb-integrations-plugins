// Where the plugins go, and how they get there and back.
//
// WHICH ROOT depends on the LaunchBox version, and getting it wrong is silent. LaunchBox 14 reads
// three plugin roots: System\Plugins is Unbroken's own and refuses a manifest declaring
// SourceKind "Local"; Local\Plugins is the managed third-party root and is where this pack belongs;
// Plugins\ is the legacy root, which still works but is not managed. Before 14, Local\Plugins does
// not exist at all and Plugins\ is the only option. So the version decides, read off the real
// LaunchBox.exe rather than guessed from what folders happen to be there.
//
// EVERY WRITE IS VERIFIED BY HASH, never by "the copy did not throw". A copy onto a file a running
// host holds open leaves the old bytes in place and reports success - the lesson deploy-dev.ps1
// carries at the top, and an installer that ships stale bytes while saying "installed" is worse than
// one that fails.
//
// AND IT NEVER STOPS A PROCESS. If LaunchBox, BigBox or LiteBox is up, this refuses and says so.
// Killing a host somebody is using looks exactly like a crash.

using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace NixxIntegrations;

/// <summary>The one install: its root, the plugin root chosen for its version, and the two places a
/// previous install could have left something.</summary>
internal sealed record Layout(string Root, string Core, int LbMajor,
                              string PluginsRoot, string LocalRoot, string LegacyRoot,
                              string LiteBoxIni);

internal static class InstallerCore
{
    // ── Finding a LaunchBox ──────────────────────────────────────────────────

    /// <summary>A LaunchBox root holds Core\ with the real LaunchBox.exe or BigBox.exe: the root exe
    /// is a launcher and the app runs out of Core\. Accept either side, because a user pointing at
    /// "the LaunchBox executable" may well pick the one they see first.</summary>
    public static bool LooksLikeRoot(string? dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir)) return false;
            var core = Path.Combine(dir, "Core");
            if (!Directory.Exists(core)) return false;
            return File.Exists(Path.Combine(core, "LaunchBox.exe"))
                || File.Exists(Path.Combine(core, "BigBox.exe"))
                || File.Exists(Path.Combine(dir, "LaunchBox.exe"))
                || File.Exists(Path.Combine(dir, "BigBox.exe"));
        }
        catch { return false; }
    }

    /// <summary>The root a chosen executable implies. Picking Core\LaunchBox.exe is the ordinary
    /// mistake, so that case walks up one rather than refusing.</summary>
    public static string RootFromExe(string exePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? exePath;
        if (string.Equals(Path.GetFileName(dir), "Core", StringComparison.OrdinalIgnoreCase))
        {
            var up = Path.GetDirectoryName(dir);
            if (LooksLikeRoot(up)) return up!;
        }
        return dir;
    }

    /// <summary>The major version of the LaunchBox this root holds, or 0 when it cannot be read.
    /// Read off Core\LaunchBox.exe - the launcher at the root reports its own version, not the
    /// application's.</summary>
    public static int MajorVersion(string root)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(root, "Core", "LaunchBox.exe"),
                     Path.Combine(root, "Core", "BigBox.exe"),
                 })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var major = FileVersionInfo.GetVersionInfo(candidate).FileMajorPart;
                if (major > 0) return major;
            }
            catch { }
        }
        return 0;
    }

    public static Layout Resolve(string root)
    {
        var major = MajorVersion(root);
        var local = Path.Combine(root, "Local", "Plugins");
        var legacy = Path.Combine(root, "Plugins");

        // 14 and up: the managed root. Below that it does not exist. When the version cannot be read
        // at all, believe the folder rather than nothing - an install that has Local\Plugins is an
        // install that knows what it is for.
        var chosen = major >= 14 ? local
                   : major > 0 ? legacy
                   : Directory.Exists(local) ? local : legacy;

        return new Layout(root, Path.Combine(root, "Core"), major, chosen, local, legacy,
                          Path.Combine(root, "Core", "litebox", "LiteBox.ini"));
    }

    /// <summary>Is this pack installed here? True as soon as one of our folders in either root holds
    /// one of our assemblies - "either root", because an install made before a LaunchBox upgrade
    /// sits in the other one and is still very much installed.</summary>
    public static bool IsInstalled(Layout l)
    {
        foreach (var root in Roots(l))
            foreach (var folder in Payload.Folders)
                if (Payload.IsOurs(Path.Combine(root, folder))) return true;
        return false;
    }

    private static IEnumerable<string> Roots(Layout l)
    {
        yield return l.LocalRoot;
        if (!string.Equals(l.LocalRoot, l.LegacyRoot, StringComparison.OrdinalIgnoreCase))
            yield return l.LegacyRoot;
    }

    /// <summary>A host holds these files open. LiteBox is checked as well as LaunchBox and BigBox -
    /// it is a host in its own right and loads the very DLLs this writes.</summary>
    public static string? RunningHost()
    {
        try
        {
            foreach (var name in new[] { "LaunchBox", "BigBox", "LiteBox" })
                if (Process.GetProcessesByName(name).Length > 0) return name;
        }
        catch { }
        return null;
    }

    // ── Install ──────────────────────────────────────────────────────────────

    public static (bool ok, string message) Install(Layout l)
    {
        var running = RunningHost();
        if (running != null)
            return (false, "Close " + running + " first, then try again.\n\n"
                         + "It holds the plugin files open, and a copy onto a file a running host "
                         + "has open leaves the old bytes in place without failing.");

        var notes = new StringBuilder();
        try
        {
            var swept = SweepStale(l, notes);

            foreach (var file in Payload.Files)
            {
                var dir = Path.Combine(l.PluginsRoot, file.Folder);
                var dest = Path.Combine(dir, file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                WriteResource(file.Resource, dest);
            }

            // What an older deploy put in the folder LaunchBox scans. Left there, LaunchBox tries to
            // load a native library as a .NET assembly and puts a dialog up at every start.
            foreach (var folder in Payload.Folders)
                foreach (var stale in Payload.StaleInFolder)
                {
                    var old = Path.Combine(l.PluginsRoot, folder, stale);
                    if (!File.Exists(old)) continue;
                    try { File.Delete(old); notes.AppendLine("  removed " + folder + "\\" + stale); }
                    catch { }
                }

            var moved = MigrateEnabledPlugins(l);
            if (moved > 0)
                notes.AppendLine("  LiteBox.ini: " + moved + " plugin tick(s) carried over to the new names");

            var where = l.PluginsRoot.StartsWith(l.LocalRoot, StringComparison.OrdinalIgnoreCase)
                        ? "Local\\Plugins" : "Plugins";
            var head = "Installed " + Payload.Folders.Length + " plugins into " + where
                     + (l.LbMajor > 0 ? "  (LaunchBox " + l.LbMajor + ")" : "") + ".";

            return (true, head
                        + (swept > 0 ? "\n\n" + swept + " older folder(s) from a previous name were removed." : "")
                        + (notes.Length > 0 ? "\n\n" + notes.ToString().TrimEnd() : "")
                        + "\n\nRestart LaunchBox / BigBox / LiteBox for it to take effect. Under "
                        + "LiteBox, tick the plugins in Options > Plugins the first time - they are "
                        + "not enabled implicitly, deliberately.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied writing to the LaunchBox folder.\n\n"
                         + "Try running this installer as administrator.");
        }
        catch (Exception ex)
        {
            return (false, "Install failed (a file may be locked - close LaunchBox / BigBox / "
                         + "LiteBox):\n" + ex.Message);
        }
    }

    /// <summary>Remove what a previous version of this pack left under its old names, in BOTH roots,
    /// and our own folders in the root we are NOT installing into.
    ///
    /// The second half is the case a LaunchBox upgrade creates: installed into Plugins\ under 13,
    /// then 14 arrives and the managed root becomes the right answer. Leaving the old copy behind
    /// gives PluginLoader two files of the same name and no stated rule about which wins.
    ///
    /// NEVER ON THE NAME ALONE. A folder is removed only when it holds one of our assemblies.</summary>
    private static int SweepStale(Layout l, StringBuilder notes)
    {
        var removed = 0;
        foreach (var root in Roots(l))
        {
            if (!Directory.Exists(root)) continue;

            var names = Payload.StaleFolders.AsEnumerable();
            if (!string.Equals(root, l.PluginsRoot, StringComparison.OrdinalIgnoreCase))
                names = names.Concat(Payload.Folders);

            foreach (var name in names)
            {
                // Asked for through GetDirectories rather than built with Combine, so the name that
                // goes in the message is the one ON DISK. Windows matches a path without regard to
                // case, so "MelonDs Integration" finds a folder spelt "melonDS Integration" - and
                // reporting the name we asked for sends somebody looking for a folder that never
                // existed under that spelling.
                string dir;
                try { dir = Directory.GetDirectories(root, name).FirstOrDefault() ?? ""; }
                catch { continue; }
                if (dir.Length == 0 || !Payload.IsOurs(dir)) continue;
                try
                {
                    Directory.Delete(dir, recursive: true);
                    notes.AppendLine("  removed " + dir);
                    removed++;
                }
                catch (Exception ex) { notes.AppendLine("  could not remove " + dir + ": " + ex.Message); }
            }
        }
        return removed;
    }

    /// <summary>Carry a plugin's tick over to its new folder name.
    ///
    /// LiteBox.ini's EnabledPlugins is a comma-separated list of FOLDER NAMES, so renaming a folder
    /// silently unticks the plugin and leaves "! enabled plugin not found:" in the log. Only an
    /// existing line is rewritten: the key being absent means "never configured", which is what
    /// makes LiteBox enable everything it finds, and writing one would take that away.</summary>
    private static int MigrateEnabledPlugins(Layout l)
    {
        try
        {
            if (!File.Exists(l.LiteBoxIni)) return 0;

            var pairs = new (string Old, string New)[]
            {
                ("Flycast Integration", Payload.Flycast),
                ("MelonDs Integration", Payload.MelonDs), ("melonDS Integration", Payload.MelonDs),
                ("NoGba Integration",   Payload.NoGba),   ("no$gba Integration",  Payload.NoGba),
                ("Ppsspp Integration",  Payload.Ppsspp),  ("PPSSPP Integration",  Payload.Ppsspp),
                ("Xenia Integration",   Payload.Xenia),
            };

            var lines = File.ReadAllLines(l.LiteBoxIni);
            var changed = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("EnabledPlugins=", StringComparison.OrdinalIgnoreCase)) continue;

                var names = lines[i]["EnabledPlugins=".Length..]
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(n => n.Trim()).Where(n => n.Length > 0).ToList();

                for (var n = 0; n < names.Count; n++)
                    foreach (var (old, fresh) in pairs)
                        if (string.Equals(names[n], old, StringComparison.OrdinalIgnoreCase))
                        {
                            names[n] = fresh;
                            changed++;
                        }

                if (changed > 0)
                    lines[i] = "EnabledPlugins=" + string.Join(",", names.Distinct(StringComparer.OrdinalIgnoreCase));
            }

            if (changed > 0) File.WriteAllLines(l.LiteBoxIni, lines);
            return changed;
        }
        catch { return 0; }
    }

    // ── Uninstall ────────────────────────────────────────────────────────────

    /// <summary>Remove exactly what Install wrote, derived from the same table, in both roots.
    ///
    /// What it does NOT touch, and says so: Data\Emulators.xml (a user's emulator entries keep
    /// working - the plugins claim an emulator by its executable path, never by name), the plugins'
    /// data folders under .data\&lt;PluginId&gt;\, the emulators themselves, the NAND dumps and every
    /// save ever made.</summary>
    public static (bool ok, string message) Uninstall(Layout l)
    {
        var running = RunningHost();
        if (running != null) return (false, "Close " + running + " first, then try again.");

        var problems = new List<string>();
        var gone = 0;

        foreach (var root in Roots(l))
        {
            foreach (var folder in Payload.Folders.Concat(Payload.StaleFolders))
            {
                var dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir) || !Payload.IsOurs(dir)) continue;
                try { Directory.Delete(dir, recursive: true); gone++; }
                catch (Exception ex) { problems.Add(folder + ": " + ex.Message); }
            }
        }

        if (problems.Count > 0)
            return (false, "Some folders could not be removed (close LaunchBox / BigBox / LiteBox):\n"
                         + string.Join("\n", problems));

        return (true, (gone == 0 ? "Nothing of this pack was installed here." : gone + " plugin folder(s) removed.")
                    + "\n\nLeft alone on purpose: your emulator entries in Data\\Emulators.xml, the "
                    + "plugins' settings under Local\\Plugins\\.data\\, your NAND dumps and every "
                    + "save. An emulator you set up keeps launching - these plugins recognise one by "
                    + "its executable, never by its name.");
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    /// <summary>Write an embedded file out, then read it back and compare hashes. Written to a
    /// temporary name and moved into place, so a failure halfway leaves the previous file rather
    /// than half of the new one.</summary>
    private static void WriteResource(string logicalName, string destPath)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var source = asm.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException("Embedded payload missing: " + logicalName
                                               + ". This build was made without its payload.");

        var expected = Hash(source);
        source.Position = 0;

        var tmp = destPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(tmp)) source.CopyTo(file);
            File.Move(tmp, destPath, overwrite: true);
        }
        catch { try { File.Delete(tmp); } catch { } throw; }

        using var written = File.OpenRead(destPath);
        if (Hash(written) != expected)
            throw new IOException(destPath + " still holds different bytes after the copy - it is "
                                + "probably held open by a running host. Nothing further was written.");
    }

    private static string Hash(Stream s) => Convert.ToHexString(SHA256.HashData(s));
}
