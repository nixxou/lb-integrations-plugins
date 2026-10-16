using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Xml;
using LbIntegrations.EmuLabels;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.EmuLabelsBench
{
    internal static class Program
    {
        private static string _core;
        private static int _failed;

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            _core = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(a => a.Key == "LbCore").Value;
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                var path = Path.Combine(_core, name.Name + ".dll");
                return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
            };
            try { Run(); }
            catch (Exception ex) { Fail("unexpected: " + ex); }
            Console.WriteLine();
            Console.WriteLine(_failed == 0 ? "ALL CHECKS PASSED" : _failed + " CHECK(S) FAILED");
            return _failed == 0 ? 0 : 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run()
        {
            const string original = "RetroArch (bench)";
            var work = Path.Combine(Path.GetTempPath(), "emu-labels-bench-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            Console.WriteLine("work folder: " + work);

            // --- 1. a REAL LaunchBox emulator ---------------------------------------------------
            var real = new Unbroken.LaunchBox.Windows.Data.Emulator();
            real.Title = original;
            real.ApplicationPath = @"Emulators\RetroArch\retroarch.exe";
            IEmulator emu = real;
            Check("a real Emulator is built and its Title read back", emu.Title == original, emu.Title);
            var id = emu.Id;
            Console.WriteLine("   emulator id: " + id);

            var changedBefore = EmulatorsChanged();
            Console.WriteLine("   DataManager.EmulatorsChanged before: " + (changedBefore ?? "unreadable"));

            // --- 2. the field behind Title --------------------------------------------------------
            var field = TitleField.Resolve(emu);
            Check("the field behind Title is found", field != null, field == null ? "none" : field.DeclaringType + "." + field.Name);
            Check("probing left the title as it was", emu.Title == original, emu.Title);

            // --- 3. label on, through the table ---------------------------------------------------
            var table = new CompatTable(Path.Combine(work, "unused.json"));
            table.Load("// a comment\n{ \"format\": \"  [{label}]\", " +
                       "\"games\": { \"g-1\": { \"" + original + "\": \"Playable\" } }, " +
                       "\"platforms\": { \"Nintendo 64\": { \"" + id + "\": \"Untested\" } }, }");
            var game = FakeGame.Of(("Id", "g-1"), ("Title", "Super Mario 64"), ("Platform", "Nintendo 64"));
            var other = FakeGame.Of(("Id", "g-2"), ("Title", "Wave Race 64"), ("Platform", "Nintendo 64"));
            var none = FakeGame.Of(("Id", "g-3"), ("Title", "Sonic"), ("Platform", "Sega Genesis"));

            Labeler.Apply(new[] { emu }, game, table);
            var shown = emu.Title;
            Console.WriteLine("   shown: \"" + Visible(shown) + "\"   raw: " + Escape(shown));
            Check("by game id, the label is shown", Visible(shown) == original + "  [Playable]", Visible(shown));
            Check("the label is between U+2063 and U+2064", shown == original + "⁣  [Playable]⁤", Escape(shown));

            Labeler.Apply(new[] { emu }, other, table);
            Check("by platform and emulator id, another game gets its own label",
                  Visible(emu.Title) == original + "  [Untested]", Visible(emu.Title));

            Labeler.Apply(new[] { emu }, none, table);
            Check("a game with no entry shows the plain title", emu.Title == original, Escape(emu.Title));

            Labeler.Apply(new[] { emu }, game, table);
            Labeler.Clear(new[] { emu });
            Check("Clear gives back the exact original", emu.Title == original, Escape(emu.Title));

            var changedAfter = EmulatorsChanged();
            Check("the setter was not used (EmulatorsChanged unchanged)",
                  changedBefore == null || changedAfter == changedBefore,
                  changedBefore == changedAfter ? "same" : changedBefore + " -> " + changedAfter);

            // Control, for information: does LaunchBox's own setter flip one of those flags? If it
            // does not, the check above proves less than its name says (the log line tells).
            var control = new Unbroken.LaunchBox.Windows.Data.Emulator();
            control.Title = "control";
            control.Title = "control, set again";
            var afterSetter = EmulatorsChanged();
            Console.WriteLine("   control - the real setter " + (afterSetter == changedAfter
                ? "flips NO DataManager flag outside LaunchBox: the check above is weaker than it looks"
                : "flips a DataManager flag: " + changedAfter + " -> " + afterSetter));

            // --- 4. the save path, WITHOUT the guard: the leak must be real -----------------------
            var lbXml = Path.Combine(Path.GetDirectoryName(_core), "Data", "Emulators.xml");
            Labeler.Apply(new[] { emu }, game, table);
            var leaked = SaveThroughDataSet(real, lbXml, Path.Combine(work, "without-guard.xml"));
            Check("without the guard, the label DOES reach Emulators.xml (the bench sees the leak)",
                  leaked == true, leaked?.ToString() ?? "save path failed");

            // --- 5. WITH the guard ----------------------------------------------------------------
            Check("the save guard installs", SaveGuard.Install(), string.Join(", ", SaveGuard.Patched));
            Console.WriteLine("   patched: " + SaveGuard.Patched.Count + " method(s)");
            leaked = SaveThroughDataSet(real, lbXml, Path.Combine(work, "with-guard.xml"));
            Check("with the guard, the label does NOT reach Emulators.xml", leaked == false, leaked?.ToString() ?? "save path failed");
            Check("and the emulator still shows it in memory", Visible(emu.Title) == original + "  [Playable]", Visible(emu.Title));

            var labelled = emu.Title;
            Check("XmlWriter.Create + WriteElementString is guarded", !Label.Has(ViaXmlWriter(labelled)));
            Check("XmlTextWriter.WriteString is guarded", !Label.Has(ViaXmlTextWriter(labelled)));
            Check("DataRow[\"x\"] = ... is guarded", !Label.Has(ViaDataRow(labelled)));
            Check("Newtonsoft JsonConvert is guarded", !Label.Has(Newtonsoft.Json.JsonConvert.SerializeObject(new { t = labelled })));
            Check("System.Text.Json is guarded", !Label.Has(System.Text.Json.JsonSerializer.Serialize(new { t = labelled })));
            Check("a title without a label is written untouched", ViaXmlWriter(original) == original);

            Labeler.Clear(new[] { emu });
            Directory.Delete(work, true);
        }

        /// <summary>The way LaunchBox saves: the emulator fills a row of a DataSet read from the real
        /// Emulators.xml, and the DataSet is written to a file. True if a label char is in the file.</summary>
        private static bool? SaveThroughDataSet(Unbroken.LaunchBox.Windows.Data.Emulator emu, string lbXml, string target)
        {
            try
            {
                var ds = new DataSet();
                ds.ReadXml(lbXml);
                var t = ds.Tables["Emulator"];
                int before = t.Rows.Count;
                emu.AddToTable(t);
                Console.WriteLine("   AddToTable: " + before + " -> " + t.Rows.Count + " row(s) in Emulator");
                ds.WriteXml(target);
                var text = File.ReadAllText(target);
                return text.IndexOf(Label.Open) >= 0 || text.IndexOf(Label.Close) >= 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("   save path: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static string ViaXmlWriter(string s)
        {
            var sb = new StringBuilder();
            using (var w = XmlWriter.Create(sb)) { w.WriteElementString("Title", s); }
            var doc = new XmlDocument(); doc.LoadXml(sb.ToString());
            return doc.DocumentElement.InnerText;
        }

        private static string ViaXmlTextWriter(string s)
        {
            var sw = new StringWriter();
            using (var w = new XmlTextWriter(sw)) { w.WriteStartElement("Title"); w.WriteString(s); w.WriteEndElement(); }
            var doc = new XmlDocument(); doc.LoadXml(sw.ToString());
            return doc.DocumentElement.InnerText;
        }

        private static string ViaDataRow(string s)
        {
            var t = new DataTable("T"); t.Columns.Add("x", typeof(string));
            var r = t.NewRow(); r["x"] = s; t.Rows.Add(r);
            return (string)t.Rows[0]["x"];
        }

        /// <summary>DataManager.EmulatorsChanged has no getter: its flag is one of DataManager's static
        /// bool fields, under an obfuscated name. All of them are read, so "unchanged" covers it.</summary>
        private static string EmulatorsChanged()
        {
            try
            {
                var t = typeof(Unbroken.LaunchBox.Windows.Data.DataManager);
                RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                return string.Join(",", t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => f.FieldType == typeof(bool))
                    .OrderBy(f => f.Name)
                    .Select(f => f.Name + "=" + f.GetValue(null)));
            }
            catch (Exception ex) { Console.WriteLine("   DataManager statics: " + ex.GetType().Name); return null; }
        }

        private static string Visible(string s) => s?.Replace("⁣", "").Replace("⁤", "");

        private static string Escape(string s)
            => s == null ? "null" : string.Concat(s.Select(c => c < 0x20 || c > 0x7e ? "\\u" + ((int)c).ToString("X4") : c.ToString()));

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _failed++;
            Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + what + (detail == null ? "" : "   [" + detail + "]"));
        }

        private static void Fail(string what) { _failed++; Console.WriteLine("  FAIL  " + what); }
    }

    /// <summary>An IGame answering only the properties it is given.</summary>
    public class FakeGame : DispatchProxy
    {
        private Dictionary<string, object> _p;

        public static IGame Of(params (string name, object value)[] props)
        {
            var g = Create<IGame, FakeGame>();
            ((FakeGame)(object)g)._p = props.ToDictionary(p => p.name, p => p.value);
            return g;
        }

        protected override object Invoke(MethodInfo m, object[] args)
        {
            if (m.Name.StartsWith("get_") && _p.TryGetValue(m.Name.Substring(4), out var v)) return v;
            return m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
        }
    }
}
