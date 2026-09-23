// Confronting LbipRowInjection with a real Microsoft.Data.Sqlite.
//
// Every other check in this probe exercises the plugin's own logic, where being wrong costs us a
// wrong answer. This one exercises the only piece that runs INSIDE someone else's query, where being
// wrong costs LaunchBox its emulator list. So it is confronted rather than asserted about: a real
// provider, a real database, real readers.
//
// Two things are being asked of it, and the second matters more than the first: does our row show up
// where it should, and does everything else come back exactly as it was.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Unbroken.LaunchBox.Plugins;

namespace LbIntegrations.Probe
{
    internal static class RowInjectionCheck
    {
        /// <summary>A type by its SIMPLE name, because every plugin merges its own copy of the
        /// injection classes under its own namespace - LbIntegrations.Flycast.LbipRowInjection and
        /// LbIntegrations.MelonDs.LbipRowInjection are different types with the same job.</summary>
        private static Type ByName(Assembly assembly, string simpleName)
            => assembly.GetTypes().FirstOrDefault(t => t.Name == simpleName);

        private static int _fail;

        private static void Check(string what, bool good)
        {
            if (!good) _fail++;
            Console.WriteLine("  " + what.PadRight(54) + (good ? "OK" : "FAIL"));
        }

        public static bool Run(EmulatorPlugin plugin)
        {
            _fail = 0;
            Console.WriteLine();
            Console.WriteLine("[row injection] against a real Microsoft.Data.Sqlite");

            var assembly = plugin.GetType().Assembly;
            var injection = ByName(assembly, "LbipRowInjection");
            var pluginType = assembly.GetTypes()
                                     .FirstOrDefault(t => !t.IsAbstract
                                                          && typeof(EmulatorPlugin).IsAssignableFrom(t));
            if (injection == null || pluginType == null)
            {
                Console.WriteLine("  FAIL - LbipRowInjection is not in this assembly");
                return false;
            }

            // A stand-in for LaunchBox.Metadata.db. The column types matter: "Id" is an INTEGER,
            // which SQLite hands back as Int64 - the case a DataTableReader would have thrown on.
            var main = Path.Combine(Path.GetTempPath(), "lbip-probe-metadata.db");
            if (File.Exists(main)) File.Delete(main);
            using (var seed = new SqliteConnection("Data Source=" + main))
            {
                seed.Open();
                Exec(seed, @"CREATE TABLE ""Emulators"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT, ""CommandLine"" TEXT, ""ApplicableFileExtensions"" TEXT, ""URL"" TEXT,
                    ""BinaryFileName"" TEXT, ""NoQuotes"" INTEGER, ""NoSpace"" INTEGER,
                    ""HideConsole"" INTEGER, ""FileNameOnly"" INTEGER, ""AutoExtract"" INTEGER)");
                Exec(seed, @"CREATE TABLE ""EmulatorPlatforms"" (""Emulator"" TEXT, ""Platform"" TEXT,
                    ""CommandLine"" TEXT, ""ApplicableFileExtensions"" TEXT, ""Recommended"" INTEGER,
                    ""RequiredBiosFile"" TEXT)");
                Exec(seed, @"CREATE TABLE ""Games"" (""Name"" TEXT)");
                Exec(seed, @"INSERT INTO ""Emulators"" (""Name"") VALUES ('RetroArch'), ('Demul')");
                Exec(seed, @"INSERT INTO ""EmulatorPlatforms"" (""Emulator"", ""Platform"")
                             VALUES ('RetroArch', 'Sega Dreamcast')");
            }

            var rows = pluginType.GetMethod("MetadataRows", BindingFlags.NonPublic | BindingFlags.Static)
                                 .Invoke(null, null);
            Check("the plugin declares metadata rows", ((IEnumerable)rows).Cast<object>().Any());

            // WHAT THIS PLUGIN CLAIMS, read off the rows it just declared rather than written down
            // here. Three plugins now inject, and an assertion naming one of them would fail on the
            // other two while proving nothing about either.
            var first = ((IEnumerable)rows).Cast<object>().First();
            string ourName = (string)first.GetType().GetField("Name").GetValue(first);
            string ourBinary = (string)first.GetType().GetField("BinaryFileName").GetValue(first);
            var ourPlatforms = ((IEnumerable)first.GetType().GetField("Platforms").GetValue(first))
                .Cast<object>()
                .Select(pl => (string)pl.GetType().GetField("Platform").GetValue(pl))
                .ToList();
            Console.WriteLine("  under test          : " + ourName + " -> "
                              + string.Join(", ", ourPlatforms));

            injection.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)
                     .Invoke(null, new object[] { "com.nixxou.lbip.probe", rows });

            // The kill switch turns this whole feature off. Saying so beats twenty failures that all
            // mean "you asked for it to be off".
            var marker = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "lb-integrations-plugins", "no-metadata");
            if (File.Exists(marker))
            {
                Console.WriteLine("  skipped - the injection is switched off by " + marker);
                return true;
            }
            // CHAINING. A second plugin's rows, registered through the same entry point a second
            // integration plugin would use. Harmony chains postfixes, so both sets must come back -
            // and this is what an earlier design got wrong, electing one installer and silently
            // dropping everybody else's rows.
            var second = MakeRow(assembly, "Redream", "redream.exe", "Sega Dreamcast");
            var clash = MakeRow(assembly, "Demul", "demul.exe", "Sega Dreamcast");
            injection.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)
                     .Invoke(null, new object[] { "com.nixxou.lbip.probe.clash", clash });
            injection.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)
                     .Invoke(null, new object[] { "com.nixxou.lbip.probe.second", second });

            // A SECOND PLUGIN, reproduced faithfully: another copy of this very assembly, loaded
            // from bytes into the SAME load context - which is how LaunchBox loads plugins. Not
            // another AssemblyLoadContext, a situation that never arises here.
            //
            // Two things must hold, and they pull in opposite directions. The second copy must NOT
            // patch - measured, a second merged Harmony throws on the attempt - and its rows must
            // appear all the same, through the shared list.
            Type stranger = null;
            try
            {
                var copy = Assembly.Load(File.ReadAllBytes(assembly.Location));
                stranger = ByName(copy, "LbipRowInjection");
                stranger.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)
                        .Invoke(null, new object[] { "com.nixxou.lbip.probe.stranger",
                                                     MakeRow(copy, "Xenia", "xenia_canary.exe",
                                                             "Microsoft Xbox 360") });
            }
            catch (Exception ex)
            {
                Console.WriteLine("  (second copy could not be loaded: " + ex.GetType().Name
                                  + ": " + ex.Message + ")");
            }
            Check("a second, independent copy of the plugin loaded",
                  stranger != null && !ReferenceEquals(stranger, injection));
            Check("it stood aside rather than patching twice",
                  stranger != null && (bool)stranger
                      .GetField("_installed", BindingFlags.NonPublic | BindingFlags.Static)
                      .GetValue(null)
                  && Equals(AppDomain.CurrentDomain.GetData("lb-integrations-plugins.row-injection.owner"),
                            "com.nixxou.lbip.probe"));

            Check("the patch installed", (bool)injection
                  .GetField("_installed", BindingFlags.NonPublic | BindingFlags.Static)
                  .GetValue(null));

            using (var c = new SqliteConnection("Data Source=" + main))
            {
                c.Open();

                var names = Query(c, @"SELECT ""Name"" FROM ""Emulators""");
                Console.WriteLine("  emulators read back : " + string.Join(", ", names));
                Check("the host's own rows still come back",
                      names.Contains("RetroArch") && names.Contains("Demul"));
                Check("ours comes back too", names.Contains(ourName));
                Check("a second plugin's row comes back as well", names.Contains("Redream"));

                // The one that proves Harmony chains ACROSS assemblies: this row was registered by a
                // copy of the class that shares nothing with the one the rest of this file talks to.
                Check("a row from the OTHER copy comes back too - the list is shared",
                      names.Contains("Xenia"));

                // THE HOST ALWAYS WINS. "Demul" is in the stand-in metadata already; registering it
                // must add nothing, or the user would see it twice the day LaunchBox ships a row we
                // also carry.
                Check("an emulator the host already has is not doubled",
                      names.Count(n => n == "Demul") == 1);

                var platforms = Query(c, @"SELECT ""Platform"" FROM ""EmulatorPlatforms""
                                           WHERE ""Emulator"" = '" + ourName + "'");
                Console.WriteLine("  platforms read back : " + string.Join(", ", platforms));
                Check("every platform it declares comes back, and no other",
                      platforms.Count == ourPlatforms.Count
                      && ourPlatforms.All(platforms.Contains));
                Check("another emulator's platform row survived",
                      Query(c, @"SELECT ""Platform"" FROM ""EmulatorPlatforms""
                                 WHERE ""Emulator"" = 'RetroArch'").Contains("Sega Dreamcast"));

                // The whole reason the query is re-run rather than the rows appended blindly: the
                // WHERE clause has to apply to OUR rows too.
                Check("a WHERE that excludes us excludes us",
                      !Query(c, @"SELECT ""Name"" FROM ""Emulators"" WHERE ""Name"" = 'RetroArch'")
                          .Contains(ourName));
                Check("a WHERE that selects us finds us",
                      Query(c, @"SELECT ""Name"" FROM ""Emulators"" WHERE ""Name"" = '" + ourName + "'")
                          .SequenceEqual(new[] { ourName }));

                // A parameter, which is how EF Core actually writes it.
                using (DbCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText = @"SELECT ""Name"" FROM ""Emulators"" WHERE ""Name"" = @p0";
                    var p0 = cmd.CreateParameter();
                    p0.ParameterName = "@p0";
                    p0.Value = ourName;
                    cmd.Parameters.Add(p0);
                    using var r = cmd.ExecuteReader();
                    var got = new List<string>();
                    while (r.Read()) got.Add(r.GetString(0));
                    Check("a parameterised query reaches our rows", got.SequenceEqual(new[] { ourName }));
                }

                // An arbitrary projection: the mirror answers with the same columns in the same order.
                using (DbCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText = @"SELECT ""BinaryFileName"", ""AutoExtract"", ""Id"" FROM ""Emulators""
                                        WHERE ""Name"" = '" + ourName + "'";
                    using var r = cmd.ExecuteReader();
                    var found = r.Read();
                    Check("a three-column projection sees our row", found && r.GetString(0) == ourBinary);
                    if (found)
                    {
                        // Both directions of the trap a DataTableReader would have set: it narrows
                        // neither, and pre-narrowing would have broken the other caller.
                        Check("GetInt32 works on our INTEGER columns", Survives(() => r.GetInt32(1)));
                        Check("GetInt64 works on the same column", Survives(() => r.GetInt64(1)));
                    }
                }

                // THE ASYNC PATH, which is the one LaunchBox actually takes: it reads its metadata
                // asynchronously, and the provider's async methods never pass through the sync ones.
                // A patch on the sync path alone produced no error and no effect at all - two
                // deployments' worth of nothing - so it is asserted here from now on.
                using (DbCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText = @"SELECT ""Name"" FROM ""Emulators""";
                    using var r = cmd.ExecuteReaderAsync().GetAwaiter().GetResult();
                    var got = new List<string>();
                    while (r.ReadAsync().GetAwaiter().GetResult()) got.Add(r.GetString(0));
                    Console.WriteLine("  read asynchronously : " + string.Join(", ", got));
                    Check("an ASYNC read is extended too", got.Contains(ourName));
                }

                // Queries we must leave exactly as they were.
                Check("an aggregate is left alone",
                      Query(c, @"SELECT COUNT(*) FROM ""Emulators""").SequenceEqual(new[] { "2" }));
                Check("a table we do not mirror is left alone",
                      Query(c, @"SELECT ""Name"" FROM ""Games""").Count == 0);
                Check("a join onto a table we lack degrades quietly",
                      Query(c, @"SELECT e.""Name"" FROM ""Emulators"" e
                                 JOIN ""Games"" g ON g.""Name"" = e.""Name""").Count == 0);

                // THE DECLARED LIMIT, asserted rather than merely written down. A caller holding the
                // concrete SqliteCommand does not pass through ExecuteDbDataReader and is not
                // extended. The day that changes, this line says so.
                using (var raw = c.CreateCommand())
                {
                    raw.CommandText = @"SELECT ""Name"" FROM ""Emulators""";
                    using var r = raw.ExecuteReader();
                    var got = new List<string>();
                    while (r.Read()) got.Add(r.GetString(0));
                    Check("a raw SqliteCommand caller is NOT extended (known limit)",
                          !got.Contains(ourName));
                }

                Exec(c, @"INSERT INTO ""Emulators"" (""Name"") VALUES ('Written')");
                Check("a write still lands in the host database",
                      Query(c, @"SELECT ""Name"" FROM ""Emulators""").Count(n => n == "Written") == 1);
            }

            // A second connection: pooled by the provider, and it must behave identically.
            using (var c = new SqliteConnection("Data Source=" + main))
            {
                c.Open();
                Check("a pooled connection still sees our rows",
                      Query(c, @"SELECT ""Name"" FROM ""Emulators""").Contains(ourName));
            }

            // A database with no Emulators table at all: nothing of ours may appear, nothing may throw.
            var other = Path.Combine(Path.GetTempPath(), "lbip-probe-other.db");
            if (File.Exists(other)) File.Delete(other);
            using (var c = new SqliteConnection("Data Source=" + other))
            {
                c.Open();
                Exec(c, @"CREATE TABLE ""Platforms"" (""Name"" TEXT)");
                Check("an unrelated database is untouched",
                      Query(c, @"SELECT ""Name"" FROM ""Platforms""").Count == 0);
            }

            Console.WriteLine();
            Console.WriteLine(_fail == 0
                ? "  OK - the injection holds against a real provider"
                : "  " + _fail + " FAILURE(S)");
            return _fail == 0;
        }

        /// <summary>One row, built through the plugin's own internal types - the same shape a second
        /// integration plugin would register.</summary>
        private static object MakeRow(Assembly assembly, string name, string binary, string platform)
        {
            var rowType = ByName(assembly, "LbipEmulatorRow");
            var platformType = ByName(assembly, "LbipPlatformRow");
            var row = Activator.CreateInstance(rowType);
            rowType.GetField("Name").SetValue(row, name);
            rowType.GetField("BinaryFileName").SetValue(row, binary);

            var one = Activator.CreateInstance(platformType);
            platformType.GetField("Platform").SetValue(one, platform);
            ((IList)rowType.GetField("Platforms").GetValue(row)).Add(one);

            var list = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(rowType));
            list.Add(row);
            return list;
        }

        private static bool Survives(Action action)
        {
            try { action(); return true; } catch { return false; }
        }

        private static void Exec(SqliteConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        /// <summary>Read through DbCommand on purpose: that is the path EF Core takes, and the only
        /// one the patch sits on.</summary>
        private static List<string> Query(SqliteConnection c, string sql)
        {
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            var list = new List<string>();
            while (r.Read()) list.Add(r.IsDBNull(0) ? null : Convert.ToString(r.GetValue(0)));
            return list;
        }
    }
}
