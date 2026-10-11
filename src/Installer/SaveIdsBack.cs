// Lot 7: before the pack goes, the save ids of LaunchBox's own integrations get their own form back in every platform
// XML (StockIdsXml) - LaunchBox closed, which Uninstall has already checked.
//
// ALL OR NOTHING (Mehdi, 11/10): every file read and changed in memory first (one unreadable: nothing written, nothing
// uninstalled); every platform XML copied to Local\Plugins\.data\nixx-saves-backup\uninstall-<time>\ (a folder the
// uninstall leaves alone); each changed file written beside itself, read back, renamed into place; a failure on one puts
// back the files already written from that copy and stops the uninstall. Afterwards each file is read again: no id of
// ours may be left. The pack's own rows are not touched (Mehdi, 11/10).

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using LbIntegrations.Saves;

namespace NixxIntegrations;

internal static class SaveIdsBack
{
    /// <summary>The probe's failure: true for a platform file whose write must fail (after another was written).</summary>
    internal static Func<string, bool>? FailWriteForTests;

    /// <summary>(true, what was done) - or (false, why), the XML files then exactly as they were.</summary>
    public static (bool ok, string message) Run(string root)
    {
        var platforms = Path.Combine(root, "Data", "Platforms");
        if (!Directory.Exists(platforms)) return (true, "");

        // 1. Everything read and changed in memory.
        var changes = new List<(string Path, bool Bom, string Text, int Rows)>();
        foreach (var path in Directory.GetFiles(platforms, "*.xml"))
        {
            try
            {
                var (text, bom) = Read(path);
                var back = StockIdsXml.Reverse(text, () => Guid.NewGuid().ToString("N"), out var rows);
                if (rows > 0) changes.Add((path, bom, back, rows));
            }
            catch (Exception ex) { return (false, "The save ids could not be given back: " + Path.GetFileName(path) + " could not be read (" + ex.Message + ")."); }
        }
        if (changes.Count == 0) return (true, "");

        // 2. The platform XML files as they are, aside.
        string copy;
        try { copy = BackUp(root, platforms); }
        catch (Exception ex) { return (false, "The save ids could not be given back: the platform XML files could not be copied aside (" + ex.Message + ")."); }

        // 3. Written one by one, each checked before it takes the place of the old one; a failure puts back the others.
        var written = new List<string>();
        foreach (var c in changes)
        {
            var tmp = c.Path + ".lbip-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(tmp, c.Text, new UTF8Encoding(c.Bom));
                var (again, _) = Read(tmp);
                new XmlDocument().LoadXml(again);                        // still an XML LaunchBox can load
                if (StockIdsXml.Remaining(again) != 0) throw new InvalidOperationException("ids left in our form after the change");
                if (FailWriteForTests?.Invoke(Path.GetFileName(c.Path)) == true) throw new IOException("failure injected by the probe");
                File.Move(tmp, c.Path, overwrite: true);
                written.Add(c.Path);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                var undone = PutBack(written, copy);
                return (false, "The save ids could not be given back in " + Path.GetFileName(c.Path) + " (" + ex.Message + ")."
                             + (written.Count == 0 ? "" : undone ? " The files already changed were put back as they were."
                                                                 : " Some files already changed could not be put back: copy them from " + copy + "."));
            }
        }

        // 4. Read again: none of our ids left.
        foreach (var c in changes)
        {
            int left;
            try { left = StockIdsXml.Remaining(Read(c.Path).Text); }
            catch { left = -1; }
            if (left != 0)
            {
                var undone = PutBack(written, copy);
                return (false, Path.GetFileName(c.Path) + " still holds save ids in the pack's form after the change."
                             + (undone ? " The files were put back as they were." : " Copy them back from " + copy + "."));
            }
        }

        int rowsBack = changes.Sum(c => c.Rows);
        return (true, "\n\nThe saves of LaunchBox's RetroArch, Dolphin and PCSX2 got their own ids back: " + rowsBack + " row(s) in "
                      + changes.Count + " platform file(s). The files as they were: " + copy);
    }

    private static (string Text, bool Bom) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return (new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
    }

    /// <summary>Every platform XML copied into a new folder, made aside and renamed (never half kept).</summary>
    private static string BackUp(string root, string platforms)
    {
        var store = Path.Combine(root, "Local", "Plugins", ".data", "nixx-saves-backup");
        Directory.CreateDirectory(store);
        var target = Path.Combine(store, "uninstall-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        var tmp = Path.Combine(store, ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            foreach (var f in Directory.GetFiles(platforms, "*.xml")) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));
            if (Directory.Exists(target)) target += "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Directory.Move(tmp, target);
            return target;
        }
        finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
    }

    private static bool PutBack(IEnumerable<string> written, string copy)
    {
        bool all = true;
        foreach (var p in written)
            try { File.Copy(Path.Combine(copy, Path.GetFileName(p)), p, overwrite: true); } catch { all = false; }
        return all;
    }
}
