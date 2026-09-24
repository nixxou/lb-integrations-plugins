// What a plugin wants LaunchBox's emulator metadata to say about it.
//
// These rows are never written anywhere. LbipRowInjection keeps them in a private in-memory SQLite
// database and appends the matching ones to the results LaunchBox reads - see that file for why.

using System;
using System.Collections.Generic;
using System.Linq;
using LbIntegrations.Catalog;

namespace LbIntegrations.Lbip
{
    // THE ROW TYPES LIVE IN THE CONTRACT ASSEMBLY, not here. They were declared in this file until
    // a host learned to ask for them: a type a host and a plugin both have to name cannot be
    // compiled into each of them separately, because type identity in .NET is per-assembly and five
    // copies of one class are five unrelated types. See src\Catalog\LbCatalog.cs.

    internal static class LbipRows
    {
        /// <summary>Where every one of our plugins puts its rows, as a List&lt;string[]&gt; hung off
        /// the AppDomain.
        ///
        /// A LIST OF STRING ARRAYS AND NOTHING RICHER, on purpose - and it stayed that way after the
        /// row types moved into a shared assembly, where objects COULD now cross. This list is the
        /// one place two INDEPENDENTLY BUILT copies of this pack meet: an old plugin folder left
        /// beside a new one, a plugin somebody built from a fork. A flat string array is the only
        /// shape that cannot break when they disagree, and Decode skips a line it does not
        /// understand rather than taking the rest down with it.</summary>
        public const string SharedKey = "lb-integrations-plugins.rows";

        /// <summary>Rows, flattened for the shared list. One line per emulator, then one per
        /// platform - the platform lines name their emulator so order never has to be trusted.</summary>
        public static IEnumerable<string[]> Encode(LbCatalogEmulator row)
        {
            yield return new[]
            {
                "E", row.Name, row.CommandLine, row.ApplicableFileExtensions, row.Url,
                row.BinaryFileName, row.AutoExtract ? "1" : "0",
            };

            foreach (var p in row.Platforms ?? Enumerable.Empty<LbCatalogPlatform>())
            {
                if (string.IsNullOrWhiteSpace(p?.Platform)) continue;
                yield return new[]
                {
                    "P", row.Name, p.Platform, p.ApplicableFileExtensions,
                    p.Recommended ? "1" : "0", p.RequiredBiosFile,
                };
            }
        }

        /// <summary>The other direction. A line of a shape we do not recognise is skipped rather than
        /// fatal: a future plugin may write more than we know how to read, and it must not take the
        /// rows we DO understand down with it.</summary>
        public static List<LbCatalogEmulator> Decode(IEnumerable<string[]> lines)
        {
            var byName = new Dictionary<string, LbCatalogEmulator>(StringComparer.OrdinalIgnoreCase);
            var order = new List<LbCatalogEmulator>();

            foreach (var line in lines ?? Enumerable.Empty<string[]>())
            {
                if (line == null || line.Length < 2 || string.IsNullOrWhiteSpace(line[1])) continue;

                if (line[0] == "E" && line.Length >= 7 && !byName.ContainsKey(line[1]))
                {
                    var row = new LbCatalogEmulator
                    {
                        Name = line[1],
                        CommandLine = line[2],
                        ApplicableFileExtensions = line[3],
                        Url = line[4],
                        BinaryFileName = line[5],
                        AutoExtract = line[6] == "1",
                    };
                    byName[row.Name] = row;
                    order.Add(row);
                }
                else if (line[0] == "P" && line.Length >= 6 && byName.TryGetValue(line[1], out var owner))
                {
                    owner.Platforms.Add(new LbCatalogPlatform
                    {
                        Platform = line[2],
                        ApplicableFileExtensions = line[3],
                        Recommended = line[4] == "1",
                        RequiredBiosFile = line[5],
                    });
                }
            }
            return order;
        }

        /// <summary>The only two tables we extend. Order matters: the mirror is created from the
        /// host's definitions in this order, and Emulators must exist before its platforms.</summary>
        public static readonly string[] TableNames = { "Emulators", "EmulatorPlatforms" };

        /// <summary>The INSERTs that fill a mirror shaped like LaunchBox's own tables.
        ///
        /// Only the columns we have something to say about are named; every other column the host
        /// schema declares keeps its default, which is what a row LaunchBox itself never wrote
        /// should look like.</summary>
        public static IEnumerable<string> BuildInserts(IEnumerable<LbCatalogEmulator> rows)
        {
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row?.Name)) continue;

                yield return @"INSERT INTO ""Emulators""
                                 (""Name"", ""CommandLine"", ""ApplicableFileExtensions"", ""URL"",
                                  ""BinaryFileName"", ""NoQuotes"", ""NoSpace"", ""HideConsole"",
                                  ""FileNameOnly"", ""AutoExtract"")
                               VALUES (" + Q(row.Name) + ", " + Q(row.CommandLine) + ", "
                                         + Q(row.ApplicableFileExtensions) + ", " + Q(row.Url) + ", "
                                         + Q(row.BinaryFileName) + ", 0, 0, 0, 0, "
                                         + (row.AutoExtract ? "1" : "0") + ")";

                foreach (var p in row.Platforms ?? Enumerable.Empty<LbCatalogPlatform>())
                {
                    if (string.IsNullOrWhiteSpace(p?.Platform)) continue;
                    yield return @"INSERT INTO ""EmulatorPlatforms""
                                     (""Emulator"", ""Platform"", ""CommandLine"",
                                      ""ApplicableFileExtensions"", ""Recommended"", ""RequiredBiosFile"")
                                   VALUES (" + Q(row.Name) + ", " + Q(p.Platform) + ", NULL, "
                                             + Q(p.ApplicableFileExtensions) + ", "
                                             + (p.Recommended ? "1" : "0") + ", "
                                             + Q(p.RequiredBiosFile) + ")";
                }
            }
        }

        /// <summary>A SQL string literal, or NULL. These values are ours - plugin constants, never
        /// user input - but they are escaped rather than trusted: a quote in a future emulator name
        /// would otherwise turn a literal into syntax.</summary>
        private static string Q(string value)
            => value == null ? "NULL" : "'" + value.Replace("'", "''") + "'";
    }
}
