// The owner no$gba's DS games show (Mehdi, 03/10) - and its DSi cartridges, which no$gba runs as DS games (NoGbaDsi.SetMode):
// the name, birthday, colour and language of <install>\FIRMWARE.BIN. That file is ALREADY A COPY: no$gba has no path setting,
// so NoGbaBios.Sync copies the user's dump there (dsfirmware.bin from RetroArch\system). So the owner is written into it, as
// into melonDS's copy - and never when there is no dump to copy from: a FIRMWARE.BIN put there by hand is the user's own file.
//
// no$gba has no override and no firmware settings of its own: the owner wanted is the pack's identity ("Your console").
//   the dump blank          the identity, without asking
//   the identity's own      the identity (the same person)
//   somebody else's         asked once at the first DS launch: the identity, or the dump's owner
// The answer is kept in <install>\lbip-firmware.tsv beside the dump's hash; the no$gba tab changes it. With "the identity",
// a change of identity reaches the copy at the next launch. The dump replaced by another: copied again, asked again.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LbIntegrations.Dsi;
using LbIntegrations.Identity;

namespace LbIntegrations.NoGba
{
    internal static class NoGbaFirmware
    {
        private const string IndexName = "lbip-firmware.tsv";

        private static BiosFile File_ => NoGbaBios.Files.First(f => f.TheirName.Equals("FIRMWARE.BIN", StringComparison.OrdinalIgnoreCase));

        /// <summary>FIRMWARE.BIN and the dump it is a copy of - null when it is not a copy of ours.</summary>
        private static (string Copy, string Source)? Ours(NoGbaLayout layout)
        {
            if (layout?.InstallDir == null) return null;
            var source = NoGbaBios.Find(layout, File_);
            var copy = Path.Combine(layout.InstallDir, File_.TheirName);
            if (source == null || !File.Exists(copy) || string.Equals(Path.GetFullPath(source), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase)) return null;
            return (copy, source);
        }

        private static DsOwner Wanted() => DsOwner.Of(PackIdentity.Load() ?? PackIdentity.FromWindows());

        /// <summary>The owner settled - see the header. Called on every DS launch, after NoGbaBios.Sync.</summary>
        public static void Settle(NoGbaLayout layout)
        {
            try
            {
                var ours = Ours(layout);
                if (ours == null) return;
                var (copy, source) = ours.Value;
                var sha = Sha256(source);
                var row = ReadIndex(layout);
                if (row == null || row.Sha != sha)
                {
                    // A dump never seen, or replaced: the copy made again from it, its owner asked about again.
                    if (row != null) File.Copy(source, copy, overwrite: true);
                    row = new Row { Source = source, Sha = sha, Answer = "" };
                }
                var owner = DsFirmwareOwner.Read(File.ReadAllBytes(copy));
                var wanted = Wanted();
                if (row.Answer.Length == 0)
                {
                    var dumpOwner = DsFirmwareOwner.Read(File.ReadAllBytes(source));
                    if (dumpOwner.Blank) row.Answer = "identity";
                    else if (dumpOwner.Same(wanted)) row.Answer = "same";
                    else
                    {
                        if (!DsiDialog.Available) { Log.Info("firmware: " + Path.GetFileName(source) + " belongs to " + dumpOwner.Describe() + "; windows are off, nothing asked"); return; }
                        var pick = DsiDialog.Ask("no$gba - whose DS is it?",
                            "Your DS firmware dump, " + Path.GetFileName(source) + ", belongs to " + dumpOwner.Describe() + "." + Environment.NewLine
                            + Environment.NewLine
                            + "DS games show the console's owner: the name, the language a game starts in, the birthday." + Environment.NewLine
                            + "Use your console (\"Your console\" in the Nixx window) instead: " + wanted.Describe() + "?" + Environment.NewLine
                            + Environment.NewLine
                            + "Asked once for this dump. You can change it later in the no$gba tab of the Nixx window." + Environment.NewLine
                            + "Your dump itself is never written either way: your console goes into no$gba's copy of it, FIRMWARE.BIN.",
                            new[] { "Use mine", "Keep the dump's" });
                        row.Answer = pick == 0 ? "identity" : "dump";
                    }
                    Log.Info("firmware: " + Path.GetFileName(source) + " belongs to " + dumpOwner.Describe() + " - "
                             + (row.Answer == "dump" ? "its owner is kept" : "your console is its owner in FIRMWARE.BIN"));
                }
                if (row.Answer != "dump" && !owner.Same(wanted)) Write(copy, wanted);
                WriteIndex(layout, row);
            }
            catch (Exception ex) { Log.Warn("firmware: could not settle the DS firmware's owner", ex); }
        }

        private static void Write(string copy, DsOwner wanted)
        {
            var why = DsFirmwareOwner.WriteOwner(copy, wanted);
            if (why != null) Log.Warn("firmware: your console could not be written into FIRMWARE.BIN - " + why);
            else Log.Info("firmware: your console written into FIRMWARE.BIN - " + DsFirmwareOwner.Read(File.ReadAllBytes(copy)).Describe());
        }

        /// <summary>FIRMWARE.BIN's owner now, whether it is set to follow the identity, and the dump's own - null when
        /// FIRMWARE.BIN is not a copy of ours.</summary>
        public static (string Dump, DsOwner Owner, bool Identity, DsOwner DumpOwner)? Active(NoGbaLayout layout)
        {
            try
            {
                var ours = Ours(layout);
                if (ours == null) return null;
                var row = ReadIndex(layout);
                bool identity = row == null ? DsFirmwareOwner.Read(File.ReadAllBytes(ours.Value.Source)).Blank : row.Answer != "dump";
                return (Path.GetFileName(ours.Value.Source), DsFirmwareOwner.Read(File.ReadAllBytes(ours.Value.Copy)), identity,
                        DsFirmwareOwner.Read(File.ReadAllBytes(ours.Value.Source)));
            }
            catch { return null; }
        }

        /// <summary>The tab's choice, or "Apply to my emulators": the identity written into FIRMWARE.BIN, or the dump's owner
        /// back (the copy made again from the dump). Null, or why not.</summary>
        public static string SetOwner(NoGbaLayout layout, bool identity)
        {
            if (DsiNand.EmulatorRunning()) return "no$gba is running - close it first";
            var ours = Ours(layout);
            if (ours == null) return "FIRMWARE.BIN is not a copy of a dump of yours - it is left as it is";
            var (copy, source) = ours.Value;
            if (identity) Write(copy, Wanted());
            else File.Copy(source, copy, overwrite: true);
            WriteIndex(layout, new Row { Source = source, Sha = Sha256(source), Answer = identity ? "identity" : "dump" });
            return null;
        }

        // ── lbip-firmware.tsv: the dump, its hash, the answer ────────────────

        private sealed class Row { public string Source, Sha, Answer; }

        private static Row ReadIndex(NoGbaLayout layout)
        {
            try
            {
                var p = Path.Combine(layout.InstallDir, IndexName);
                if (!File.Exists(p)) return null;
                var c = File.ReadAllLines(p).FirstOrDefault()?.Split('\t');
                return c == null || c.Length < 2 ? null : new Row { Source = c[0], Sha = c[1], Answer = c.Length > 2 ? c[2] : "" };
            }
            catch { return null; }
        }

        private static void WriteIndex(NoGbaLayout layout, Row row)
            => File.WriteAllText(Path.Combine(layout.InstallDir, IndexName), string.Join("\t", row.Source, row.Sha, row.Answer) + "\r\n", new UTF8Encoding(false));

        private static string Sha256(string path)
        {
            using var s = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
        }
    }
}
