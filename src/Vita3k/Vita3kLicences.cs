// Where a .pkg's licence comes from. The plugin SHIPS NONE: it reads the ones the user gives it.
//
// A .pkg is Sony's store package, and a game in it cannot run without its licence (a RIF, 512 bytes;
// a zRIF is the same in text form - VitaZrif). A NoNpDRM zip carries its own as work.bin; a .pkg never
// does. So, in order:
//
//   1. beside the .pkg, the same name: <name>.rif, <name>.bin or <name>.work.bin (the licence itself -
//      what Vita3K's own install offers to pick, "*.bin *.rif"), or <name>.zrif (a text file holding
//      the zRIF). Failing those, any small .rif or .bin in the same folder whose CONTENT_ID is the
//      package's: a licence kept beside it under another name is found without being renamed.
//   2. <emulator folder>\zrif\*.tsv - tables of licences, one line per content: columns found BY THEIR
//      HEADER, "Content ID" and "zRIF" (a "Title ID" too, for the log); every other column ignored.
//      A my-licences.tsv template is put there when the folder has none - the header, how to fill it,
//      and nothing else.
//
// A licence is only used for the content it names: its own CONTENT_ID (at 0x10 in the RIF) has to be
// the package's. One that names another is set aside, and said.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kLicences
    {
        public const string FolderName = "zrif";
        public const string TemplateName = "my-licences.tsv";

        /// <summary>The emulator folder of the session being prepared - its zrif\ folder is read.
        /// Set by Vita3kWorkspace before anything is installed.</summary>
        internal static string InstallDir;

        public static string FolderFor(string installDir) => Path.Combine(installDir, FolderName);

        /// <summary>The licence of <paramref name="contentId"/>, for the .pkg at <paramref name="pkgPath"/> -
        /// null, with <paramref name="error"/> saying where it was looked for.</summary>
        public static byte[] Find(string pkgPath, string contentId, out string foundBy, out string error)
        {
            foundBy = null;
            error = null;
            var tried = new List<string>();

            // 1. beside the package
            var stem = Path.Combine(Path.GetDirectoryName(pkgPath) ?? "", Path.GetFileNameWithoutExtension(pkgPath));
            foreach (var ext in new[] { ".rif", ".bin", ".work.bin" })
            {
                var file = stem + ext;
                if (!File.Exists(file)) continue;
                // THE SIZE FIRST: a .bin of the same name may be anything - never read one that cannot be a licence.
                if (!LicenceSized(file)) { Log.Info("licence: " + Path.GetFileName(file) + " is not licence-sized - not read"); continue; }
                var rif = Checked(File.ReadAllBytes(file), contentId, Path.GetFileName(file));
                if (rif != null) { foundBy = Path.GetFileName(file) + " beside it"; return rif; }
            }
            if (File.Exists(stem + ".zrif"))
            {
                var rif = VitaZrif.ToRif(File.ReadAllText(stem + ".zrif"), out var why);
                if (rif == null) Log.Warn("licence: " + Path.GetFileName(stem + ".zrif") + " is not a zRIF - " + why);
                else if ((rif = Checked(rif, contentId, Path.GetFileName(stem + ".zrif"))) != null)
                { foundBy = Path.GetFileName(stem + ".zrif") + " beside it"; return rif; }
            }
            // ... or under another name, in the same folder: a licence is 512 bytes and says what it is for.
            foreach (var file in SmallLicences(Path.GetDirectoryName(pkgPath)))
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(file); } catch { continue; }
                if (!string.Equals(Vita3kContent.NamesIn(bytes).contentId, contentId, StringComparison.OrdinalIgnoreCase)) continue;
                foundBy = Path.GetFileName(file) + " in its folder";
                return bytes;
            }
            tried.Add("beside it (" + Path.GetFileName(stem) + ".rif / .bin / .work.bin / .zrif, or any licence of that content in its folder)");

            // 2. the tables in the emulator's zrif folder
            if (!string.IsNullOrEmpty(InstallDir))
            {
                var folder = FolderFor(InstallDir);
                EnsureTemplate(InstallDir);
                foreach (var table in SafeFiles(folder))
                {
                    foreach (var (zrif, line) in Lines(table, contentId))
                    {
                        var rif = VitaZrif.ToRif(zrif, out var why);
                        if (rif == null) { Log.Warn("licence: " + Path.GetFileName(table) + " line " + line + " - " + why); continue; }
                        if ((rif = Checked(rif, contentId, Path.GetFileName(table) + " line " + line)) != null)
                        { foundBy = Path.GetFileName(table) + ", line " + line; return rif; }
                    }
                }
                tried.Add("in " + folder + "\\*.tsv");
            }

            error = "no licence for " + contentId + " - looked " + string.Join(", and ", tried)
                    + ". A .pkg needs the licence of the content it holds: put its zRIF in the zrif folder's " + TemplateName + ".";
            return null;
        }

        /// <summary>The .rif and .bin files of a folder small enough to be a licence (a RIF is 512 bytes;
        /// 4 KB leaves room for a padded one). Not recursive: a ROM folder can be a whole library.</summary>
        private static IEnumerable<string> SmallLicences(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) yield break;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder).ToList(); } catch { yield break; }
            foreach (var f in files)
            {
                var ext = Path.GetExtension(f);
                if (!string.Equals(ext, ".rif", StringComparison.OrdinalIgnoreCase) && !string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase)) continue;
                if (LicenceSized(f)) yield return f;
            }
        }

        /// <summary>Small enough to be a licence, and big enough - asked BEFORE a byte is read.</summary>
        private static bool LicenceSized(string file)
        {
            try { var size = new FileInfo(file).Length; return size >= 0x40 && size <= 4096; }
            catch { return false; }
        }

        /// <summary>The licence, if it is <paramref name="contentId"/>'s.</summary>
        private static byte[] Checked(byte[] rif, string contentId, string from)
        {
            if (rif == null || rif.Length < 0x40) { Log.Warn("licence: " + from + " is too short to be one"); return null; }
            var names = Vita3kContent.NamesIn(rif).contentId;
            if (!string.Equals(names, contentId, StringComparison.OrdinalIgnoreCase))
            { Log.Warn("licence: " + from + " is " + (names ?? "no content") + "'s, not " + contentId + "'s - set aside"); return null; }
            return rif;
        }

        /// <summary>The zRIFs a table gives for <paramref name="contentId"/>, with their line numbers.
        /// Columns by header name; a cell that is not a zRIF (empty, a word) is passed over.</summary>
        private static IEnumerable<(string zrif, int line)> Lines(string table, string contentId)
        {
            string[] lines;
            try { lines = File.ReadAllLines(table); } catch { yield break; }
            int idAt = -1, zrifAt = -1, n = 0;
            foreach (var raw in lines)
            {
                n++;
                if (raw.Length == 0 || raw.StartsWith("#")) continue;
                var cells = raw.Split('\t');
                if (idAt < 0)
                {
                    idAt = Array.FindIndex(cells, c => string.Equals(c.Trim(), "Content ID", StringComparison.OrdinalIgnoreCase));
                    zrifAt = Array.FindIndex(cells, c => string.Equals(c.Trim(), "zRIF", StringComparison.OrdinalIgnoreCase));
                    if (idAt < 0 || zrifAt < 0) { Log.Info("licence: " + Path.GetFileName(table) + " has no \"Content ID\" and \"zRIF\" header - not read"); yield break; }
                    continue;
                }
                if (cells.Length <= Math.Max(idAt, zrifAt)) continue;
                if (!string.Equals(cells[idAt].Trim(), contentId, StringComparison.OrdinalIgnoreCase)) continue;
                var zrif = cells[zrifAt].Trim();
                if (zrif.Length < 20 || zrif.Contains(' ')) continue;   // "MISSING", "NOT REQUIRED", empty...
                yield return (zrif, n);
            }
        }

        private static IEnumerable<string> SafeFiles(string folder)
        {
            try { return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.tsv").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray() : new string[0]; }
            catch { return new string[0]; }
        }

        /// <summary>The zrif folder, with a template to fill when it has no table at all. Never
        /// overwrites one.</summary>
        public static void EnsureTemplate(string installDir)
        {
            try
            {
                var folder = FolderFor(installDir);
                Directory.CreateDirectory(folder);
                if (Directory.GetFiles(folder, "*.tsv").Length > 0) return;
                File.WriteAllLines(Path.Combine(folder, TemplateName), new[]
                {
                    "# The licences of the PS Vita content you own, for installing its .pkg files.",
                    "# One line per content, the columns separated by TABS. Only \"Content ID\" and \"zRIF\" are needed.",
                    "# The Content ID is the one the .pkg is for (e.g. UP0000-PCSE00000_00-XXXXXXXXXXXXXXXX); the zRIF is its licence",
                    "# in text form, as a console with NoNpDrm dumps it. Any other .tsv file in this folder with the same headers is read too.",
                    "# Lines starting with # are ignored.",
                    "Title ID\tRegion\tName\tContent ID\tzRIF",
                });
                Log.Info("licence: " + folder + " made, with " + TemplateName + " to fill");
            }
            catch (Exception ex) { Log.Warn("could not make the zrif folder", ex); }
        }
    }
}
