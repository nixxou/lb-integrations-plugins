// LaunchBox's own game database (Metadata\LaunchBox.Metadata.db), READ ONLY - which of its games a title
// names on a platform, the answer LaunchBox itself found when it imported the game.
//
// Measured 30/09: a title and a platform name one game. "Virtua Fighter 4" is three games of the
// database (Sony Playstation 2, Arcade, Sega Naomi 2) and one on "Sega Naomi 2", 152524; LaunchBox
// compares on CompareName, the title in capitals without its punctuation ("VIRTUA FIGHTER 4
// EVOLUTION"), and falls back on the alternate titles - "Virtua Fighter 4 Version C" is one of 152524's,
// which is how the set vf4 got there.
//
// Opened with the Microsoft.Data.Sqlite LaunchBox has loaded (this pack references none), in its own
// read-only connection: nothing of LaunchBox's is held or shared, and nothing is ever written.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Lbip
{
    internal static class LbipMetadataDb
    {
        /// <summary>A title as the database compares it: capitals, letters and digits, one space between words.</summary>
        public static string CompareValue(string title)
        {
            var sb = new StringBuilder();
            foreach (var c in (title ?? "").ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (char.IsWhiteSpace(c) && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        /// <summary>The one game of the database <paramref name="title"/> names on <paramref name="platform"/>,
        /// by its name, else by an alternate title - or null, and why.</summary>
        public static int? DatabaseIdOf(string title, string platform, out string why)
        {
            why = null;
            var value = CompareValue(title);
            if (value.Length == 0 || string.IsNullOrEmpty(platform)) { why = "no title or no platform"; return null; }
            try
            {
                using var db = Open(out why);
                if (db == null) return null;
                var byName = Ids(db, "SELECT DISTINCT DatabaseID FROM Games WHERE CompareName = @v AND Platform = @p", value, platform);
                if (byName.Count == 1) { why = "by its name"; return byName[0]; }
                if (byName.Count > 1) { why = byName.Count + " games of that name on " + platform; return null; }
                var byAlt = Ids(db, "SELECT DISTINCT g.DatabaseID FROM GameAlternateTitles a JOIN Games g ON g.DatabaseID = a.DatabaseID "
                                  + "WHERE a.AltNameCompareValue = @v AND g.Platform = @p", value, platform);
                if (byAlt.Count == 1) { why = "by an alternate title"; return byAlt[0]; }
                why = byAlt.Count == 0 ? "no game of that title on " + platform : byAlt.Count + " games of that alternate title on " + platform;
                return null;
            }
            catch (Exception ex) { why = "the database could not be read (" + ex.GetType().Name + ": " + ex.Message + ")"; return null; }
        }

        private static List<int> Ids(DbConnection db, string sql, string value, string platform)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            Param(cmd, "@v", value);
            Param(cmd, "@p", platform);
            var ids = new List<int>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) if (!reader.IsDBNull(0)) ids.Add(Convert.ToInt32(reader.GetValue(0)));
            return ids.Distinct().ToList();
        }

        private static void Param(DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        /// <summary>A read-only connection to the database, open - or null, and why.</summary>
        private static DbConnection Open(out string why)
        {
            why = null;
            var file = Path.Combine(LbipImportWatch.LaunchBoxRoot(), "Metadata", "LaunchBox.Metadata.db");
            if (!File.Exists(file)) { why = "no " + file; return null; }
            var type = Type.GetType("Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite", false)
                       ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Microsoft.Data.Sqlite.SqliteConnection", false)).FirstOrDefault(t => t != null);
            if (type == null) { why = "Microsoft.Data.Sqlite is not in this process"; return null; }
            var db = (DbConnection)Activator.CreateInstance(type, "Data Source=" + file + ";Mode=ReadOnly");
            db.Open();
            return db;
        }
    }
}
