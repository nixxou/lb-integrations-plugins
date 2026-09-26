// Making LaunchBox read our emulator rows without writing a byte anywhere.
//
// LaunchBox's Add Emulator window builds its name list and its Associated Platforms grid from
// LaunchBox.Metadata.db. Several of the emulators this repository integrates are simply not in
// it - there is no Flycast row while Demul, nullDC and Redream are all there, dead for years, and
// no standalone DS emulator of any kind. And since this pack publishes under its own names, none of
// its rows is ever in there. Without a row the grid has nothing to narrow, and the emulator looks
// like it covers every platform.
//
// HOW IT WORKS. Two Harmony postfixes, on one method and its asynchronous twin:
//
//     SqliteCommand.ExecuteDbDataReader(CommandBehavior)
//     SqliteCommand.ExecuteDbDataReaderAsync(CommandBehavior, CancellationToken)
//
// BOTH, and the second is the one that matters here: LaunchBox reads its metadata asynchronously -
// measured - and the provider's async path never passes through the sync method. Patching only the
// sync one produced no error and no effect whatever, which is the worst kind of wrong.
//
// When the command's SQL names a table we extend, we run THE SAME SQL, with the same parameters,
// against a private in-memory database holding only our rows, and chain whatever it returns onto the
// end of the reader LaunchBox is about to get (see LbipAppendingReader).
//
// Re-running the query rather than guessing is the whole point: the WHERE clause, the projection,
// the joins and the aliases are all honoured by SQLite itself. We never parse LaunchBox's SQL, so
// there is nothing to get wrong when they change it.
//
// WHY THIS METHOD AND NO OTHER. Measured, from the decompiled provider and from a Harmony run:
//   * ExecuteDbDataReader is declared to return DbDataReader, so a reader of ours can be put in its
//     place. The two public ExecuteReader overloads return SqliteDataReader - Harmony will happily
//     let you take a `ref DbDataReader __result` on them, and the first caller that actually treats
//     the result as a SqliteDataReader then pays for it.
//   * ExecuteDbDataReader(behavior) simply calls ExecuteReader(behavior), and EF Core - which is how
//     LaunchBox reads this database - always goes through it. One patch covers every entry point
//     that matters.
//
// WHY REFLECTION FOR THE TYPES. Microsoft.Data.Sqlite is NOT referenced as a package and must not
// be: this assembly merges its dependencies, so a package reference would fold a second copy in here
// while the patch has to target the one LaunchBox actually loaded. Only the two type lookups need
// reflection - everything else is System.Data.Common, which both copies would share anyway.
//
// SEVERAL PLUGINS, ONE PATCH, AND WHY IT IS NOT THE OTHER WAY ROUND.
//
// Harmony chains postfixes, so the obvious design is for every integration plugin to install its
// own. Measured, in one process and one load context, it does not work: the SECOND plugin to try
// throws
//
//     ArgumentException: GenericArguments[0], 'MonoMod.Utils.Cil.CecilILGenerator', on
//     'MonoMod.Utils.Cil.ILGeneratorProxy[TTarget]' violates the constraint of type 'TTarget'
//
// because each plugin merges its OWN Harmony, and HarmonySharedState is found BY NAME across
// assemblies: the second copy picks up the first copy's state and mixes those types with its own
// generics. Chaining postfixes works; chaining two internalized Harmonies does not.
//
// So the first plugin to get here patches, and the others do not. What they all share instead is the
// ROW LIST - a List<string[]> on the AppDomain, framework types only, because a row object of one
// plugin is an unreadable foreign type to the next. An earlier version elected an installer the same
// way but let each copy keep its rows in its own static list, so the plugin that stood aside
// published into a list nobody read and its emulator never appeared at all.
//
// ExtendDB patches SqliteCommand.ExecuteReader in production and coexists with us regardless: it is
// a different method, and its Harmony is its own file rather than merged.
//
// WE NEVER DOUBLE THE HOST. An emulator already present in LaunchBox's own metadata is left to
// them, checked by name against their database. Since this pack publishes under its own "Nixx-"
// names that check now answers no every time, and the duplicate is deliberate: the official PPSSPP
// row and ours sit side by side, one of them carrying the integration. The check is kept because it
// still holds against a database somebody has edited by hand, and because a row of ours must never
// quietly shadow one of theirs - see HostAlreadyKnows.
//
// THE LIMIT, MEASURED. Only callers holding a DbCommand are covered, because only they go through
// ExecuteDbDataReader; code calling SqliteCommand.ExecuteReader() on the concrete type reaches
// SQLite without passing us. LaunchBox reads this database through EF Core, which always takes the
// DbCommand path, so this is the right trade against the alternative - substituting a result on a
// method declared to return SqliteDataReader, which Harmony permits and which would hand the first
// caller that treats it as one an object of the wrong type. The trace below makes the gap visible
// rather than silent.
//
// WHAT IT DELIBERATELY DOES NOT DO. Aggregates are left alone: appending a row to a COUNT(*) is
// meaningless, and we would rather under-report than corrupt. Everything else degrades the same way
// - any query we cannot mirror simply comes back exactly as LaunchBox built it.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LbIntegrations.Catalog;

namespace LbIntegrations.Lbip
{
    internal static class LbipRowInjection
    {
        /// <summary>Which plugin owns the patch. An AppDomain flag rather than Harmony state on
        /// purpose - see the header: two merged Harmony copies do not share state, and the second
        /// cannot patch at all.</summary>
        private const string OwnerKey = "lb-integrations-plugins.row-injection.owner";

        private static readonly object Gate = new object();

        /// <summary>Guards the queries WE issue from re-entering our own postfix.</summary>
        [ThreadStatic] private static bool _inOurOwnWork;

        private static bool _installed;
        private static Type _connectionType, _commandType;

        /// <summary>The mirror: an in-memory database shaped like LaunchBox's, holding only our rows.
        /// Built once, from the host's own table definitions, and kept open - an in-memory SQLite
        /// database exists only as long as its connection does.</summary>
        private static DbConnection _mirror;
        private static int _mirrorLines = -1;

        /// <summary>A second mirror that also holds the HOST's rows, built only when a query needs
        /// it. See EnsureMirror.</summary>
        private static DbConnection _whole;
        private static int _wholeLines = -1;

        /// <summary>Databases we have already said are not the metadata one, so we say it once.</summary>
        private static readonly HashSet<string> _saidNotOurs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Register these rows and install the patch if nobody has yet. Never throws: a
        /// plugin whose metadata could not be published still works for an emulator the user points
        /// at by hand.</summary>
        public static void Install(string pluginId, IEnumerable<LbCatalogEmulator> rows)
        {
            try
            {
                lock (Gate)
                {
                    Publish(rows);

                    if (_installed) return;

                    // A HOST THAT OFFERS A DOOR IS NOT PATCHED. Everything below exists because
                    // LaunchBox has no way to add an emulator to its catalogue and no intention of
                    // growing one; it is a hack on a closed source, and it is the price of that
                    // host. A host that implements the ask instead - it sets this flag before it
                    // constructs anything, then reads ILbCatalogSource off the plugins it loaded -
                    // gets a method call with a typed answer, and none of this runs.
                    //
                    // LaunchBox has never heard of that assembly, so the flag is false there and
                    // the fallback stands. Unset meaning "patch" is the safe default: a host that
                    // says nothing gets the behaviour that works everywhere.
                    if (LbCatalog.HostWillAsk)
                    {
                        LbipLog.Info("the host reads ILbCatalogSource itself - nothing is patched");
                        _installed = true;
                        return;
                    }

                    // A KILL SWITCH, for telling this feature's effects apart from everything else's.
                    // Create an empty file
                    //
                    //     %LOCALAPPDATA%\lb-integrations-plugins\no-metadata
                    //
                    // and LaunchBox goes back to not knowing this pack's emulators exist: no rows are
                    // added to any query, the Add Emulator window has nothing to narrow, and every
                    // other part of the plugin behaves exactly as it does now. Deleting the file
                    // restores it. This
                    // is here because "is it the metadata injection?" is a question worth being able
                    // to answer by measurement rather than by argument.
                    if (LbipLog.Disabled("no-metadata"))
                    {
                        LbipLog.Info("metadata injection DISABLED by the no-metadata marker");
                        _installed = true;
                        return;
                    }

                    var owner = AppDomain.CurrentDomain.GetData(OwnerKey) as string;
                    if (owner != null)
                    {
                        // Another of our plugins patched first. Our rows are in the shared list it
                        // reads, so they will appear - it is only the patching we skip.
                        LbipLog.Info("already patched by " + owner + "; our rows go through the shared list");
                        _installed = true;
                        return;
                    }

                    // A failure here is not final: the provider may simply not be loaded yet, and a
                    // later call - the plugin retries on host events - can still succeed.
                    if (!Patch(pluginId)) return;

                    AppDomain.CurrentDomain.SetData(OwnerKey, pluginId);
                    _installed = true;
                    LbipLog.Info("row injection installed by " + pluginId);
                }
            }
            catch (Exception ex) { LbipLog.Warn("could not install the row injection", ex); }
        }

        /// <summary>Put our rows where every copy of this class can read them, skipping any emulator
        /// somebody has already published.
        ///
        /// By NAME, and it is not paranoia: a host may construct the same plugin class more than once
        /// - measured - and each row would then be served twice. An emulator listed twice in the Add
        /// Emulator window is worse than one listed not at all.</summary>
        private static void Publish(IEnumerable<LbCatalogEmulator> rows)
        {
            if (rows == null) return;

            var shared = AppDomain.CurrentDomain.GetData(LbipRows.SharedKey) as List<string[]>;
            if (shared == null)
            {
                shared = new List<string[]>();
                AppDomain.CurrentDomain.SetData(LbipRows.SharedKey, shared);
            }

            lock (shared)
            {
                var known = new HashSet<string>(
                    shared.Where(l => l != null && l.Length > 1 && l[0] == "E").Select(l => l[1]),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name)))
                {
                    if (!known.Add(row.Name)) continue;
                    shared.AddRange(LbipRows.Encode(row));
                    LbipLog.Info("published \"" + row.Name + "\" to the shared row list");
                }
            }
        }

        /// <summary>Everything every plugin has published, as it stands right now.</summary>
        private static List<string[]> Shared()
        {
            var shared = AppDomain.CurrentDomain.GetData(LbipRows.SharedKey) as List<string[]>;
            if (shared == null) return new List<string[]>();
            lock (shared) return shared.ToList();
        }

        private static bool Patch(string pluginId)
        {
            _connectionType = AccessTools.TypeByName("Microsoft.Data.Sqlite.SqliteConnection");
            _commandType = AccessTools.TypeByName("Microsoft.Data.Sqlite.SqliteCommand");
            if (_connectionType == null || _commandType == null)
            {
                LbipLog.Info("Microsoft.Data.Sqlite is not loaded in this process - no metadata to extend");
                return false;
            }

            // DeclaredOnly: Harmony refuses an inherited method outright ("you can only patch
            // implemented methods"), and the override is the one that runs anyway.
            var target = _commandType.GetMethod("ExecuteDbDataReader",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null, new[] { typeof(CommandBehavior) }, null);
            if (target == null)
            {
                LbipLog.Warn("SqliteCommand.ExecuteDbDataReader is not where we expect it - not patching");
                return false;
            }

            // AND ITS ASYNC TWIN, which is the one that actually runs here. Measured from the log:
            // LaunchBox reads its metadata asynchronously, and the provider's async path never
            // touches the sync ExecuteDbDataReader -
            //     ExecuteDbDataReaderAsync -> ExecuteReaderAsync(behavior, ct) -> ExecuteReader(behavior)
            // - so a patch on the sync one alone sees nothing at all. It returns Task<DbDataReader>,
            // which is as substitutable as DbDataReader was.
            var asyncTarget = _commandType.GetMethod("ExecuteDbDataReaderAsync",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null, new[] { typeof(CommandBehavior), typeof(CancellationToken) }, null);

            var harmony = new Harmony(pluginId);
            harmony.Patch(target, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(LbipRowInjection), nameof(AfterExecuteReader))));
            if (asyncTarget != null)
                harmony.Patch(asyncTarget, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(LbipRowInjection), nameof(AfterExecuteReaderAsync))));
            LbipLog.Info("patched SqliteCommand.ExecuteDbDataReader"
                     + (asyncTarget != null ? " and ExecuteDbDataReaderAsync" : " (no async twin found)"));

            InstallTrace(harmony);
            return true;
        }

        // -- the trace -------------------------------------------------------
        //
        // Off unless the user asks for it, because LaunchBox runs a great many queries and a log
        // nobody reads is just wear on the disk. Switched on by creating an empty file:
        //
        //     %LOCALAPPDATA%\lb-integrations-plugins	race
        //
        // It answers a question the injection cannot: WHEN LaunchBox reads what. A patch installed
        // when the plugin is constructed cannot affect a query that already ran, and the trace is
        // what distinguishes "we never saw that query" from "we saw it and did nothing".
        //
        // It sits on ExecuteReader(CommandBehavior) and ExecuteNonQuery rather than on the method we
        // patch above: those two are where EVERY statement passes, including the ones ExecuteScalar
        // issues, and the ones of other plugins. It only ever reads, so no return type is at stake.

        /// <summary>Whether the statement hooks are in. The per-query lines below follow the same
        /// switch: they are what found the async gap, and they are useless noise the rest of the
        /// time - LaunchBox asks these tables often enough that three lines per query would bury
        /// everything else in this log.</summary>
        private static bool _traceInstalled;

        /// <summary>So the log says ONCE, per session, that rows really are being added. That single
        /// line is what tells you the feature is alive without tracing anything.</summary>
        private static bool _saidItWorks;

        private static void InstallTrace(Harmony harmony)
        {
            try
            {
                if (!LbipLog.Tracing) return;

                var hook = new HarmonyMethod(AccessTools.Method(typeof(LbipRowInjection), nameof(TraceCommand)));
                foreach (var name in new[] { "ExecuteReader", "ExecuteNonQuery" })
                    foreach (var m in _commandType.GetMethods(BindingFlags.Public | BindingFlags.Instance
                                                              | BindingFlags.DeclaredOnly)
                                                  .Where(m => m.Name == name && !m.IsAbstract))
                        harmony.Patch(m, prefix: hook);

                _traceInstalled = true;
                LbipLog.Info("SQL trace ON (delete the \"trace\" marker to stop it)");
            }
            catch (Exception ex) { LbipLog.Warn("could not install the SQL trace", ex); }
        }

        /// <summary>Log a statement on its way through. Never touches the call.
        ///
        /// A statement naming our tables is logged even with the trace off, and that is the point:
        /// this hook sits BELOW the one we extend, so every such line without a matching "added" or
        /// "left alone" line is a query that reached our tables by a path we do not cover - a caller
        /// holding a SqliteCommand rather than a DbCommand. That is how we would find out.</summary>
        public static void TraceCommand(DbCommand __instance)
        {
            if (!_traceInstalled || _inOurOwnWork || __instance == null) return;
            try
            {
                var sql = __instance.CommandText;
                if (string.IsNullOrEmpty(sql)) return;
                var ours = TouchesOurTables(sql);

                var source = __instance.Connection?.DataSource;
                LbipLog.Info("sql" + (ours ? "*" : "") + " ["
                         + (string.IsNullOrEmpty(source) ? "?" : System.IO.Path.GetFileName(source))
                         + "] " + (ours ? Flatten(sql) : Shorten(sql)));
            }
            catch { /* a trace must never be the reason something fails */ }
        }

        /// <summary>Append our matching rows to the result LaunchBox is about to read.
        ///
        /// Everything here is best-effort: any failure leaves <paramref name="__result"/> exactly as
        /// the provider built it, and LaunchBox carries on with its own data.</summary>
        public static void AfterExecuteReader(DbCommand __instance, ref DbDataReader __result)
        {
            if (__result == null) return;
            var extra = RowsFor(__instance, out var replace);
            if (extra == null) return;
            __result = new LbipAppendingReader(__result, extra, replace);
        }

        /// <summary>The same thing for the asynchronous path, which is the one LaunchBox takes.
        ///
        /// The rows are computed now - the mirror is in memory and answers immediately - and the
        /// reader is wrapped when the host's own task completes, so a failed query stays failed and
        /// a cancelled one stays cancelled.</summary>
        public static void AfterExecuteReaderAsync(DbCommand __instance, ref Task<DbDataReader> __result)
        {
            if (__result == null) return;
            var extra = RowsFor(__instance, out var replace);
            if (extra == null) return;
            __result = WrapWhenReady(__result, extra, replace);
        }

        private static async Task<DbDataReader> WrapWhenReady(Task<DbDataReader> inner,
                                                             List<object[]> extra, bool replace)
            => new LbipAppendingReader(await inner.ConfigureAwait(false), extra, replace);

        /// <summary>Our rows for this command's query, or null when there is nothing to add.
        ///
        /// Everything here is best-effort: on any failure the caller leaves the result exactly as
        /// the provider built it, and LaunchBox carries on with its own data.</summary>
        private static List<object[]> RowsFor(DbCommand command) => RowsFor(command, out _);

        private static List<object[]> RowsFor(DbCommand command, out bool replace)
        {
            replace = false;
            if (_inOurOwnWork || command == null) return null;

            var sql = command.CommandText;
            if (string.IsNullOrEmpty(sql) || !TouchesOurTables(sql)) return null;
            if (IsAggregate(sql))
            {
                // Logged even with the trace off: it is a query about our tables that we chose not
                // to answer, and that is worth knowing when a count looks wrong.
                if (LbipLog.Tracing) LbipLog.Info("left an aggregate on our tables alone: " + Shorten(sql));
                return null;
            }

            // Logged BEFORE anything can go wrong, and unconditionally. The trace hook below sits on
            // a method our postfixes do not cover, so a "sql*" line with no "postfix" line beside it
            // is the proof that LaunchBox reached our tables by a path we never see - which is
            // exactly how the missing async patch was found. Without this line, silence is ambiguous.
            if (LbipLog.Tracing) LbipLog.Info("postfix reached, full sql: " + Flatten(sql));

            _inOurOwnWork = true;
            try
            {
                // A LIMIT means the answer is a slice of an order, so it has to be computed over
                // BOTH sets at once. The mirror then carries the host's rows as well and the whole
                // query is replayed on it - SQLite does the sorting and the cutting, and we do not
                // parse a line of SQL.
                bool whole = IsOrderedSlice(sql);
                var mirror = EnsureMirror(command.Connection, whole);
                if (mirror == null)
                {
                    if (LbipLog.Tracing) LbipLog.Info("no mirror for this connection - nothing added");
                    return null;
                }

                var extra = RunOnMirror(mirror, sql, command.Parameters);
                if (extra == null || extra.Count == 0)
                {
                    if (LbipLog.Tracing) LbipLog.Info("the mirror matched none of our rows for this query - nothing added");
                    return null;
                }

                if (LbipLog.Tracing || !_saidItWorks)
                {
                    _saidItWorks = true;
                    LbipLog.Info((whole ? "answered a limited query with " : "added ")
                                 + extra.Count + " row(s) " + (whole ? "of its own: " : "to a metadata query: ")
                                 + Shorten(sql));
                }
                replace = whole;
                return extra;
            }
            catch (Exception ex)
            {
                LbipLog.Warn("could not extend a metadata query (" + Shorten(sql) + ")", ex);
                return null;
            }
            finally { _inOurOwnWork = false; }
        }

        /// <summary>Copy every row of one table from the host into the mirror, by value. Best
        /// effort: a table that cannot be read leaves the mirror with our rows alone, which is the
        /// behaviour this had before there was a whole mirror at all.</summary>
        private static void CopyTable(DbConnection host, DbConnection mirror, string table)
        {
            try
            {
                using var read = host.CreateCommand();
                read.CommandText = "SELECT * FROM \"" + table + "\"";
                using var reader = read.ExecuteReader();

                int n = reader.FieldCount;
                var names = new string[n];
                for (int i = 0; i < n; i++) names[i] = "\"" + reader.GetName(i) + "\"";
                var holes = new string[n];
                for (int i = 0; i < n; i++) holes[i] = "@p" + i;
                var sql = "INSERT INTO \"" + table + "\" (" + string.Join(", ", names)
                          + ") VALUES (" + string.Join(", ", holes) + ")";

                int rows = 0;
                while (reader.Read())
                {
                    using var write = mirror.CreateCommand();
                    write.CommandText = sql;
                    for (int i = 0; i < n; i++)
                    {
                        var p = write.CreateParameter();
                        p.ParameterName = "@p" + i;
                        p.Value = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                        write.Parameters.Add(p);
                    }
                    write.ExecuteNonQuery();
                    rows++;
                }
                if (LbipLog.Tracing) LbipLog.Info("copied " + rows + " host row(s) of \"" + table + "\"");
            }
            catch (Exception ex) { LbipLog.Warn("could not copy \"" + table + "\" into the mirror", ex); }
        }

        /// <summary>Quoted identifiers are what makes this safe: `"Emulators"` cannot match inside
        /// `"EmulatorPlatforms"`, and EF Core's SQLite provider always quotes.</summary>
        private static bool TouchesOurTables(string sql)
            => LbipRows.TableNames.Any(t => sql.IndexOf("\"" + t + "\"", StringComparison.Ordinal) >= 0);

        /// <summary>Does this query pick a few rows out of an order?
        ///
        /// APPENDING CANNOT ANSWER ONE OF THESE, and the failure is silent. Measured on LaunchBox 14,
        /// whose ROM import wizard asks:
        ///
        ///     SELECT "e"."Emulator" FROM "EmulatorPlatforms" AS "e"
        ///     WHERE "e"."Platform" = @platform AND "e"."Recommended"
        ///     ORDER BY "e"."Emulator" LIMIT 1
        ///
        /// SQLite sorted and cut the HOST's rows, we stapled ours on after - in second place, where
        /// a caller taking the first never sees it. By their own ordering ours should have won, and
        /// the import offered no files at all because the row that did win carries no extensions.
        ///
        /// So these are answered whole instead: see EnsureMirror(withHostRows: true).</summary>
        private static bool IsOrderedSlice(string sql)
            => sql.IndexOf(" limit ", StringComparison.OrdinalIgnoreCase) >= 0
               || sql.EndsWith(" limit", StringComparison.OrdinalIgnoreCase);

        /// <summary>A query that folds its rows into one value must not gain another. We would rather
        /// leave such a query alone than answer it wrongly.</summary>
        private static bool IsAggregate(string sql)
        {
            foreach (var token in new[] { "count(", "sum(", "avg(", "min(", "max(", "group by" })
                if (sql.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>The mirror, built from the host's OWN table definitions so that `SELECT *` has
        /// the same columns in the same order as theirs. A hand-written schema would silently rot the
        /// day LaunchBox adds a column.
        ///
        /// Returns null - without remembering the failure - when this connection is not the metadata
        /// database. LaunchBox opens several SQLite files, and only one of them has these tables.</summary>
        private static DbConnection EnsureMirror(DbConnection host, bool withHostRows = false)
        {
            if (host == null || host.State != ConnectionState.Open) return null;

            // Rebuilt when a plugin publishes after we built it - they do not all load at once.
            var lines = Shared();
            if (withHostRows)
            {
                if (_whole != null && lines.Count == _wholeLines) return _whole;
                if (_whole != null) { _whole.Dispose(); _whole = null; }
                _wholeLines = lines.Count;
            }
            else
            {
                if (_mirror != null && lines.Count == _mirrorLines) return _mirror;
                if (_mirror != null) { _mirror.Dispose(); _mirror = null; }
                _mirrorLines = lines.Count;
            }

            var ddl = new List<string>();
            foreach (var table in LbipRows.TableNames)
            {
                var text = Scalar(host, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '"
                                        + table + "'") as string;
                if (string.IsNullOrEmpty(text))
                {
                    // Once per file, and only while tracing: LaunchBox opens several SQLite
                    // databases and all but one of them land here, every time they are opened.
                    if (LbipLog.Tracing && _saidNotOurs.Add(host.DataSource ?? "?"))
                        LbipLog.Info("no \"" + table + "\" table on " + (host.DataSource ?? "?")
                                 + " - not the metadata database");
                    return null;
                }
                ddl.Add(text);
            }

            var rows = LbipRows.Decode(lines);
            if (rows.Count == 0) return null;

            // NEVER ADD AN EMULATOR THE HOST ALREADY HAS. Asked of the host's own database, by
            // name, so it is their answer and not our guess. Under the "Nixx-" names this pack
            // publishes the answer is always no - our row and theirs are different emulators as far
            // as that table is concerned, which is the point. What this still prevents is a row of
            // ours landing on a name they own: the user would get two identical entries in the Add
            // Emulator window and no way to tell which is which.
            var mine = new List<LbCatalogEmulator>();
            foreach (var row in rows)
            {
                if (HostAlreadyKnows(host, row.Name))
                {
                    LbipLog.Info("\"" + row.Name + "\" is already in LaunchBox's metadata - leaving it to them");
                    continue;
                }
                mine.Add(row);
            }
            if (mine.Count == 0) return null;
            rows = mine;

            var mirror = (DbConnection)Activator.CreateInstance(_connectionType, "Data Source=:memory:");
            mirror.Open();
            foreach (var statement in ddl)
                using (var c = mirror.CreateCommand()) { c.CommandText = statement; c.ExecuteNonQuery(); }

            // THE HOST'S ROWS FIRST, when this mirror has to answer a query whole. Copied rather
            // than joined: the mirror is a separate in-memory database, and these tables hold tens
            // of rows, not thousands. It is cached like the other one, and metadata does not change
            // while a host is running.
            if (withHostRows)
                foreach (var table in LbipRows.TableNames) CopyTable(host, mirror, table);

            foreach (var statement in LbipRows.BuildInserts(rows))
                using (var c = mirror.CreateCommand()) { c.CommandText = statement; c.ExecuteNonQuery(); }

            if (withHostRows)
            {
                _whole = mirror;
                LbipLog.Info("whole mirror built (host rows included), " + rows.Count + " emulator(s) of ours");
                return _whole;
            }

            _mirror = mirror;
            LbipLog.Info("mirror built in memory from the host's schema, " + rows.Count + " emulator(s)");
            return _mirror;
        }

        /// <summary>Run LaunchBox's own query against our rows. A query naming a table the mirror does
        /// not have simply throws, and the caller then leaves the result alone.</summary>
        private static List<object[]> RunOnMirror(DbConnection mirror, string sql, DbParameterCollection parameters)
        {
            using var command = mirror.CreateCommand();
            command.CommandText = sql;
            foreach (DbParameter source in parameters)
            {
                var p = command.CreateParameter();
                p.ParameterName = source.ParameterName;
                p.Value = source.Value ?? DBNull.Value;
                command.Parameters.Add(p);
            }

            using var reader = command.ExecuteReader();
            var rows = new List<object[]>();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(values);
            }
            return rows;
        }

        /// <summary>Does LaunchBox's own metadata already carry this emulator? Case-insensitively,
        /// because their spelling of a name is not ours to predict.</summary>
        private static bool HostAlreadyKnows(DbConnection host, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            var count = Scalar(host, "SELECT count(*) FROM main.\"Emulators\" WHERE \"Name\" = '"
                                     + name.Replace("'", "''") + "' COLLATE NOCASE");
            return Convert.ToInt64(count ?? 0L) > 0L;
        }

        private static object Scalar(DbConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        private static string Shorten(string sql)
        {
            var flat = string.Join(" ", sql.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return flat.Length <= 160 ? flat : flat.Substring(0, 160) + " ...";
        }

        /// <summary>The whole statement, on one line. Used where the text is the measurement.</summary>
        private static string Flatten(string sql)
        {
            return string.Join(" ", sql.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
