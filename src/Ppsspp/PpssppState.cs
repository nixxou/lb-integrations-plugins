// PPSSPP save states.
//
// A state is a plain FILE, not a container - the opposite of a PSP save, which is a set of
// directories. That difference drives everything in PpssppSaves.cs: IsSaveContainer says false for
// a state, the host copies the file itself, and TryBackupSave is never asked for one.
//
// Layout, measured on a real install rather than assumed:
//
//     <memstick>\PSP\PPSSPP_STATE\ULUS10516_1.01_0.ppst        the state
//     <memstick>\PSP\PPSSPP_STATE\ULUS10516_1.01_0.jpg         its screenshot
//     <memstick>\PSP\PPSSPP_STATE\ULUS10516_1.01_0.undo.ppst   PPSSPP's undo buffer
//     <memstick>\PSP\PPSSPP_STATE\ULUS10516_1.01_0.undo.jpg
//
// THE TRAP IS THE UNDO PAIR. A glob on "<disc id>_*.ppst" picks it up too, and it is not a slot the
// user ever chose: PPSSPP writes it when a save overwrites an existing slot, so that "undo last save
// state" can put the old one back. The name pattern below ends at "_<digits>.ppst", which rejects
// "_0.undo.ppst" with no special case - but the case is spelled out here because a laxer pattern
// would silently invent a phantom slot.
//
// THE VERSION MATTERS. Unlike Dolphin, whose state file is "<disc id>.s<slot>", PPSSPP puts the disc
// VERSION in the name and looks for that exact name when loading. A restore that guesses the version
// wrong writes a file the emulator will never see, so refusing beats guessing.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LbIntegrations.Ppsspp
{
    internal sealed class PpssppState
    {
        public string Path;
        /// <summary>The sibling screenshot, or null when PPSSPP did not write one.</summary>
        public string ThumbnailPath;
        public string DiscId;
        /// <summary>The disc version as it appears in the file name, e.g. "1.01".</summary>
        public string Version;
        public int Slot;
        public long SizeBytes;
        public DateTime LastWriteUtc;

        public string FileName => System.IO.Path.GetFileName(Path ?? "");
    }

    internal static class PpssppStates
    {
        public const string Extension = ".ppst";
        public const string ThumbnailExtension = ".jpg";

        /// <summary>PPSSPP's default number of state slots. It is the `SaveStateSlotCount` setting in
        /// ppsspp.ini - measured at 5 on a stock install - and a user can change it, but
        /// GetPotentialSaveSlots is handed no emulator, so there is nothing to resolve an install
        /// from at that point.</summary>
        public const int DefaultSlotCount = 5;

        // "<9 chars>_<version>_<slot>.ppst". Ending at the digits is what excludes the .undo pair.
        private static readonly Regex NameRx = new Regex(
            @"^(?<disc>[A-Za-z0-9]{9})_(?<ver>[0-9][0-9A-Za-z.]*)_(?<slot>\d{1,3})\.ppst$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Read a state file name. False for anything else, the undo buffer included.</summary>
        public static bool TryParseName(string fileName, out string discId, out string version, out int slot)
        {
            discId = null; version = null; slot = 0;
            if (string.IsNullOrEmpty(fileName)) return false;
            var m = NameRx.Match(fileName);
            if (!m.Success) return false;
            if (!int.TryParse(m.Groups["slot"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
                return false;
            discId = m.Groups["disc"].Value.ToUpperInvariant();
            version = m.Groups["ver"].Value;
            return true;
        }

        /// <summary>Every state of one game, ordered by slot.</summary>
        public static List<PpssppState> ForDiscId(string stateDir, string discId)
        {
            var found = new List<PpssppState>();
            if (string.IsNullOrWhiteSpace(stateDir) || string.IsNullOrWhiteSpace(discId)
                || !Directory.Exists(stateDir)) return found;

            foreach (var path in Directory.EnumerateFiles(stateDir, discId + "_*" + Extension))
            {
                if (!TryParseName(Path.GetFileName(path), out var d, out var ver, out var slot)) continue;
                if (!string.Equals(d, discId, StringComparison.OrdinalIgnoreCase)) continue;

                FileInfo info;
                try { info = new FileInfo(path); } catch { continue; }

                found.Add(new PpssppState
                {
                    Path = path,
                    ThumbnailPath = ThumbnailFor(path),
                    DiscId = d,
                    Version = ver,
                    Slot = slot,
                    SizeBytes = Safe(() => info.Length),
                    LastWriteUtc = Safe(() => info.LastWriteTimeUtc),
                });
            }
            return found.OrderBy(s => s.Slot).ToList();
        }

        /// <summary>The screenshot beside a state, or null when it is absent. PPSSPP writes it with
        /// the state, but a copy made by hand may not carry it.</summary>
        public static string ThumbnailFor(string ppstPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ppstPath)) return null;
                var jpg = Path.ChangeExtension(ppstPath, ThumbnailExtension);
                return File.Exists(jpg) ? jpg : null;
            }
            catch { return null; }
        }

        /// <summary>Is this the undo buffer PPSSPP keeps beside a slot?</summary>
        public static bool IsUndo(string fileName)
            => !string.IsNullOrEmpty(fileName)
               && fileName.EndsWith(".undo" + Extension, StringComparison.OrdinalIgnoreCase);

        /// <summary>The disc version to write into a restored file name, learned from what is already
        /// in the state folder for that game. Null when there is nothing to learn from - and null must
        /// make the caller refuse, not guess: PPSSPP loads a state by exact file name.</summary>
        public static string VersionFromFolder(string stateDir, string discId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(stateDir) || string.IsNullOrWhiteSpace(discId)
                    || !Directory.Exists(stateDir)) return null;
                foreach (var path in Directory.EnumerateFiles(stateDir, discId + "_*" + Extension))
                    if (TryParseName(Path.GetFileName(path), out _, out var ver, out _)) return ver;
            }
            catch { }
            return null;
        }

        /// <summary>The file name PPSSPP expects for one slot of one game.</summary>
        public static string NameFor(string discId, string version, int slot)
            => discId + "_" + version + "_" + slot.ToString(CultureInfo.InvariantCulture) + Extension;

        private static T Safe<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }
    }
}
