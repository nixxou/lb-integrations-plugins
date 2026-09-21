// The interop assertion, plugin-agnostic.
//
// Hands a plugin the two things LiteBox would hand it - a FileLocation and a SaveGroupId - then
// backs the save up, checks the shape of what came out, and hashes it with RomM's formula. If the
// layout is right the value equals what the emulator's Android counterpart computes for the same
// save, which is the whole point: the fingerprint is taken over ZIP ENTRY NAMES, so the directory
// shape IS the wire contract.
//
//     lines  = "<entry name>:<md5 lowercase hex>" for every FILE entry
//     sorted by entry name, ordinal; joined with "\n"; md5 of the UTF-8 bytes
//
// Re-implemented here rather than called out of LiteBox on purpose: an assertion that shares its
// implementation with the thing it checks proves nothing.
//
// Metadata a plugin parks in LiteBox's reserved `.litebox-plugin` folder is excluded, exactly as
// SaveVault excludes it - so a backup can carry sidecars without moving the fingerprint.

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
    internal static class UnitCheck
    {
        /// <summary>Mirrors SaveVault.PluginMetaDirName on the host side.</summary>
        private const string MetaDirName = ".litebox-plugin";

        public static bool Run(EmulatorPlugin plugin, string unitPath, string groupId,
                               string expected, string emuPath, bool roundTrip)
        {
            Console.WriteLine();
            Console.WriteLine("-- save unit / wire hash " + new string('-', 40));
            Console.WriteLine("  FileLocation : " + unitPath);
            Console.WriteLine("  SaveGroupId  : " + groupId);

            if (!Directory.Exists(unitPath))
            {
                Console.WriteLine("  no such directory");
                return false;
            }

            var save = new GameSaveGame
            {
                FileLocation = unitPath,
                SaveGroupId = groupId,
                OriginalFileName = Path.GetFileName(unitPath.TrimEnd('\\', '/')),
            };

            if (!plugin.IsSaveContainer(save))
            {
                Console.WriteLine("  IsSaveContainer said false - LiteBox would take the plain directory arm");
                return false;
            }

            string temp = Path.Combine(Path.GetTempPath(), "lbip-unit-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (!plugin.TryBackupSave(save, emuPath, temp, out var error))
                {
                    Console.WriteLine("  TryBackupSave failed: " + (error ?? "no reason given"));
                    return false;
                }

                var all = Entries(temp, includeMeta: true);
                var hashed = all.Where(e => !IsMeta(e.Name)).ToList();
                var meta = all.Where(e => IsMeta(e.Name)).ToList();

                Console.WriteLine("  extracted    : " + hashed.Count + " file(s) that count, "
                                  + meta.Count + " sidecar file(s)");
                foreach (var e in hashed) Console.WriteLine("    " + e.Name);
                foreach (var e in meta) Console.WriteLine("    " + e.Name + "   (excluded from the hash)");

                var loose = hashed.Where(e => !e.Name.Contains('/')).ToList();
                if (loose.Count > 0)
                {
                    Console.WriteLine("  FAIL - " + loose.Count + " entry(ies) at the root with no folder: "
                                      + string.Join(", ", loose.Select(e => e.Name)));
                    return false;
                }

                var roots = hashed.Select(e => e.Name.Split('/')[0])
                                  .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                Console.WriteLine("  archive root : " + string.Join(", ", roots)
                                  + (roots.Count == 1 ? "" : "   <-- MULTI-ROOT"));

                string hash = RommHash(hashed);
                Console.WriteLine("  wire hash    : " + hash);

                bool ok = true;
                if (!string.IsNullOrWhiteSpace(expected))
                {
                    bool same = string.Equals(hash, expected.Trim(), StringComparison.OrdinalIgnoreCase);
                    Console.WriteLine("  expected     : " + expected.Trim() + (same ? "   MATCH" : "   MISMATCH"));
                    ok = same;
                }
                else Console.WriteLine("  (no --expect-hash given: the layout was checked, the value was not)");

                Console.WriteLine("  " + (ok ? "OK - the layout is the one the other end hashes" : "NOT OK - see above"));
                if (!ok || !roundTrip) return ok;

                return RestoreRoundTrip(plugin, save, temp, hash, emuPath);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>Restore the copy, re-extract, and compare. Equal means the save survived a full
        /// cycle byte for byte. This WRITES where the plugin decides to put it back.</summary>
        private static bool RestoreRoundTrip(EmulatorPlugin plugin, GameSaveGame original,
                                             string extracted, string beforeHash, string emuPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- restore round-trip  [WRITES] " + new string('-', 33));

            // AddSaveFile carries no emulator and looks the library up instead; give it one, the way a
            // host would. Without this the plugin cannot know where the emulator keeps its saves.
            PluginHelper.DataManager = new StubDataManager(
                new StubEmulator { Title = "Emulator", ApplicationPath = emuPath });

            var response = plugin.AddSaveFile(new AddSaveArgs
            {
                SaveToAdd = new GameSaveGame
                {
                    FileLocation = extracted,
                    SaveGroupId = original.SaveGroupId,
                    GameId = original.GameId,
                },
                ShouldOverwriteFunc = () => true,
            });

            if (response is not { WasSuccess: true })
            {
                Console.WriteLine("  AddSaveFile failed: " + (response?.Message ?? "no reason given"));
                return false;
            }
            var landed = response.SaveAdded?.FileLocation;
            Console.WriteLine("  restored to  : " + landed);

            string temp2 = Path.Combine(Path.GetTempPath(), "lbip-rt-" + Guid.NewGuid().ToString("N"));
            try
            {
                var again = new GameSaveGame { FileLocation = landed, SaveGroupId = original.SaveGroupId };
                if (!plugin.TryBackupSave(again, emuPath, temp2, out var error))
                {
                    Console.WriteLine("  re-extract failed: " + (error ?? "no reason given"));
                    return false;
                }
                string afterHash = RommHash(Entries(temp2, includeMeta: false));
                bool same = string.Equals(beforeHash, afterHash, StringComparison.OrdinalIgnoreCase);
                Console.WriteLine("  hash after   : " + afterHash);
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

        private static bool IsMeta(string entryName)
            => entryName.Split('/').Any(s => string.Equals(s, MetaDirName, StringComparison.OrdinalIgnoreCase));

        private static List<Entry> Entries(string root, bool includeMeta)
            => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                        .Select(f => new Entry(Path.GetRelativePath(root, f).Replace('\\', '/'), f))
                        .Where(e => includeMeta || !IsMeta(e.Name))
                        .OrderBy(e => e.Name, StringComparer.Ordinal)
                        .ToList();

        private static string RommHash(List<Entry> entries)
        {
            var lines = entries.OrderBy(e => e.Name, StringComparer.Ordinal)
                               .Select(e => e.Name + ":" + Md5(File.ReadAllBytes(e.Path)));
            return Md5(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        }

        private static string Md5(byte[] bytes)
            => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    }
}
