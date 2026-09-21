// The interop assertion: does our TryBackupSave lay a save out in the shape Argosy hashes?
//
// This is the test that locks the storage decision. A save unit is extracted into a temp folder,
// and the result is hashed with RomM's formula over the entry names a zip of that folder would
// carry. If the shape is right the value equals what Argosy computes for the same save, and what
// LiteBox's SaveHash.OfDirectory computes for the vault copy.
//
// The formula, from RomM's assets_handler.compute_content_hash (which sigil documents and Argosy
// implements in SaveArchiver.calculateZipHashFromStream):
//
//     lines  = "<entry name>:<md5 lowercase hex>" for every FILE entry
//     sorted by entry name, ordinal
//     joined with "\n", no trailing newline
//     result = md5 of the UTF-8 bytes
//
// Golden vector, from argosy-launcher/sigil/scripts/romm_hash_vectors.py:
//     ULUS10064DATA00/PARAM.SFO = "sfo", ULUS10064DATA00/DATA.BIN = "data"
//         -> 40382d4f86536c9a4fbdbc2d9eb99e91
//
// Re-implemented here rather than called from LiteBox on purpose: an assertion that shares its
// implementation with the thing it checks proves nothing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class SaveUnitCheck
    {
        public static bool Run(EmulatorPlugin plugin, string saveDataDir, string discId, string expected, string emuPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- save unit / Argosy hash " + new string('-', 38));

            if (!Directory.Exists(saveDataDir))
            {
                Console.WriteLine("  no such SAVEDATA directory: " + saveDataDir);
                return false;
            }
            if (string.IsNullOrWhiteSpace(discId))
            {
                Console.WriteLine("  needs --disc-id <ULUS10064>");
                return false;
            }
            discId = discId.ToUpperInvariant();

            var live = Directory.EnumerateDirectories(saveDataDir)
                .Where(d => (Path.GetFileName(d) ?? "").StartsWith(discId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => (Path.GetFileName(d) ?? "").Length)
                .ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (live.Count == 0)
            {
                Console.WriteLine("  nothing under " + saveDataDir + " starts with " + discId);
                return false;
            }
            Console.WriteLine("  on disk : " + string.Join(", ", live.Select(Path.GetFileName)));

            // What LiteBox would hand the plugin, minus the parts it does not read.
            var save = new GameSaveGame
            {
                FileLocation = live[0],
                SaveGroupId = "ppsspp:" + discId,
                OriginalFileName = Path.GetFileName(live[0]),
            };

            if (!plugin.IsSaveContainer(save))
            {
                Console.WriteLine("  IsSaveContainer said false - LiteBox would take the plain directory arm");
                return false;
            }

            string temp = Path.Combine(Path.GetTempPath(), "lbip-unit-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Null emulator path on purpose: TryBackupSave must cope by deriving SAVEDATA from the
                // save's own location, which is what a probe without a real install can offer.
                if (!plugin.TryBackupSave(save, null, temp, out var error))
                {
                    Console.WriteLine("  TryBackupSave failed: " + (error ?? "no reason given"));
                    return false;
                }

                var entries = Entries(temp);
                Console.WriteLine("  extracted: " + entries.Count + " file(s)");
                foreach (var e in entries) Console.WriteLine("    " + e.Name);

                // Every entry must sit under a folder, never at the root: a flat layout is exactly what
                // Argosy's unzipToFolder would scatter loose into SAVEDATA.
                var loose = entries.Where(e => !e.Name.Contains('/')).ToList();
                if (loose.Count > 0)
                {
                    Console.WriteLine("  FAIL - " + loose.Count + " entry(ies) at the root, with no folder: "
                                      + string.Join(", ", loose.Select(e => e.Name)));
                    return false;
                }

                var folders = entries.Select(e => e.Name.Split('/')[0])
                                     .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var dropped = live.Select(Path.GetFileName)
                                  .Where(n => !folders.Contains(n, StringComparer.OrdinalIgnoreCase))
                                  .ToList();
                // Reported, not judged. A prefix match the plugin left out is usually CORRECT - an
                // installed game-data folder is excluded on purpose, exactly as Argosy excludes it -
                // so counting folders would fail a passing run. The hash is what decides.
                Console.WriteLine("  kept     : " + string.Join(", ", folders));
                if (dropped.Count > 0)
                    Console.WriteLine("  dropped  : " + string.Join(", ", dropped) + "   (expected for installed game data)");

                string hash = RommHash(entries);
                Console.WriteLine("  RomM hash: " + hash);

                bool ok = true;
                if (!string.IsNullOrWhiteSpace(expected))
                {
                    bool same = string.Equals(hash, expected.Trim(), StringComparison.OrdinalIgnoreCase);
                    Console.WriteLine("  expected : " + expected.Trim() + (same ? "   MATCH" : "   MISMATCH"));
                    ok = same;
                }
                else
                {
                    Console.WriteLine("  (no --expect-hash given: the layout was checked, the value was not)");
                }
                Console.WriteLine("  " + (ok ? "OK - the layout is the one Argosy hashes" : "NOT OK - see above"));
                return ok && RoundTrip(plugin, saveDataDir, discId, temp, hash, emuPath);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>Put the extracted copy back and check nothing was lost: restore, re-extract, and
        /// compare the hash to the one we started from. Equal means the save survived a full
        /// backup/restore cycle byte for byte.
        ///
        /// This WRITES into the live SAVEDATA - the folders of this disc id are deleted and rewritten,
        /// exactly as Argosy's extractDownload does. Point it at a fixture.</summary>
        private static bool RoundTrip(EmulatorPlugin plugin, string saveDataDir, string discId,
                                      string extracted, string beforeHash, string emuPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- restore round-trip  [WRITES to " + saveDataDir + "] " + new string('-', 12));
            if (string.IsNullOrWhiteSpace(emuPath))
            {
                Console.WriteLine("  skipped (needs --emu, to resolve the memory stick)");
                return true;
            }

            // AddSaveFile carries no emulator and looks the library up instead; give it one.
            PluginHelper.DataManager = new StubDataManager(
                new StubEmulator { Title = "PPSSPP", ApplicationPath = emuPath });

            var before = Directory.EnumerateDirectories(saveDataDir).Select(Path.GetFileName)
                                  .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            var response = plugin.AddSaveFile(new AddSaveArgs
            {
                SaveToAdd = new GameSaveGame
                {
                    FileLocation = extracted,
                    SaveGroupId = "ppsspp:" + discId,
                },
                ShouldOverwriteFunc = () => true,
            });

            if (response is not { WasSuccess: true })
            {
                Console.WriteLine("  AddSaveFile failed: " + (response?.Message ?? "no reason given"));
                return false;
            }
            Console.WriteLine("  restored to " + Path.GetFileName(response.SaveAdded?.FileLocation ?? "?"));

            var after = Directory.EnumerateDirectories(saveDataDir).Select(Path.GetFileName)
                                 .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            var lost = before.Except(after, StringComparer.OrdinalIgnoreCase).ToList();
            if (lost.Count > 0)
            {
                // A restore must never take a bystander with it - not another game, and not the
                // installed game-data folder that shares the prefix.
                Console.WriteLine("  FAIL - the restore removed " + string.Join(", ", lost));
                return false;
            }
            var bystanders = after.Where(n => !n.StartsWith(discId, StringComparison.OrdinalIgnoreCase)).ToList();
            var sameIdKept = after.Where(n => n.StartsWith(discId, StringComparison.OrdinalIgnoreCase)).ToList();
            Console.WriteLine("  this disc id: " + string.Join(", ", sameIdKept));
            Console.WriteLine("  other games : " + (bystanders.Count == 0 ? "(none)" : string.Join(", ", bystanders)) + "   untouched");

            string temp2 = Path.Combine(Path.GetTempPath(), "lbip-rt-" + Guid.NewGuid().ToString("N"));
            try
            {
                var save = new GameSaveGame
                {
                    FileLocation = response.SaveAdded?.FileLocation,
                    SaveGroupId = "ppsspp:" + discId,
                };
                if (!plugin.TryBackupSave(save, emuPath, temp2, out var error))
                {
                    Console.WriteLine("  re-extract failed: " + (error ?? "no reason given"));
                    return false;
                }
                string afterHash = RommHash(Entries(temp2));
                bool same = string.Equals(beforeHash, afterHash, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine("  hash after : " + afterHash);
                Console.WriteLine("  " + (same
                    ? "OK - the save survived backup and restore unchanged"
                    : "NOT OK - the round trip changed the save"));
                return same;
            }
            finally
            {
                try { if (Directory.Exists(temp2)) Directory.Delete(temp2, recursive: true); } catch { }
            }
        }

        private readonly struct Entry
        {
            public Entry(string name, string path) { Name = name; Path = path; }
            public string Name { get; }
            public string Path { get; }
        }

        /// <summary>Files under <paramref name="root"/>, named the way a zip of it would name them.</summary>
        private static List<Entry> Entries(string root)
            => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                        .Select(f => new Entry(Path.GetRelativePath(root, f).Replace('\\', '/'), f))
                        .OrderBy(e => e.Name, StringComparer.Ordinal)
                        .ToList();

        private static string RommHash(List<Entry> entries)
        {
            var lines = entries
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .Select(e => e.Name + ":" + Md5(File.ReadAllBytes(e.Path)));
            return Md5(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        }

        private static string Md5(byte[] bytes)
            => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    }
}
