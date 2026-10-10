// LaunchBox's integrations' save ids in our form, and back (lot 4, docs\saves-lot4.md; plan: saves-lb14-routing.md D3).
//
// RetroArch, Dolphin and PCSX2 give their rows ids of their own - pcsx2:<serial>, saturn-<base>, or nothing at all
// (RetroArch's ordinary saves and states, measured 07/10 and 10/10) - and LaunchBox makes a GUID for a row given none.
// Translated, every row says who owns it, the way our plugins' rows do:
//
//   a non-empty native id  → {"emu":"lb.<x>","kind":"save"|"state","native":"<the id, exactly>"}
//   no id                  → {"emu":"lb.<x>","kind":...,"by":"name","id":"<file name>"[,"slot":n],"dir":"<its folder>"}
//                            (the folder relative to LaunchBox's root, lower case: one name in two cores' folders is two
//                            saves; a card persisted before "dir" keeps its id - a listed row takes its persisted card)
//
// The way back is exact: `native` holds the whole original string (System.Text.Json reads it back as written), a
// by-name id goes back to no id - what the integration gave. Their own code always sees their own form: measured 10/10,
// RetroArch matches its Saturn cards only on a saturn- id and PCSX2 decides by the pcsx2: prefix.
//
// Pure: no Harmony, no LaunchBox. StockDesk calls it on the way in and out; lot 7 calls Reverse at uninstall.

#nullable enable

using System;

namespace LbIntegrations.Saves
{
    internal static class StockIds
    {
        /// <summary>Our id for a row an integration (<paramref name="selfEmu"/>, "lb.retroarch"...) gave with
        /// <paramref name="native"/>. Left as it is: an id that is already ours (any JSON) or LiteBox's (entry:/unit:),
        /// and a row with neither an id nor a file name.</summary>
        public static string? Encode(string selfEmu, string? native, bool state, string? fileName, int? slot, string? dir = null)
        {
            if (!string.IsNullOrEmpty(native))
            {
                if (native![0] == '{' || native.StartsWith("entry:", StringComparison.Ordinal) || native.StartsWith("unit:", StringComparison.Ordinal))
                    return native;
                return SaveId.Write(new SaveIdData { Emu = selfEmu, Kind = state ? "state" : "save", Native = native });
            }
            if (string.IsNullOrEmpty(fileName)) return native;
            return SaveId.Write(new SaveIdData { Emu = selfEmu, Kind = state ? "state" : "save", By = "name", Id = fileName, Slot = state ? slot : null,
                                                 Dir = string.IsNullOrEmpty(dir) ? null : dir });
        }

        /// <summary>The folder of <paramref name="fileLocation"/> as a by-name id holds it: relative to <paramref name="root"/>
        /// (LaunchBox's), else whole; lower case, so a path written in another case is the same card. Null for none.</summary>
        public static string? DirOf(string? fileLocation, string root)
        {
            if (string.IsNullOrWhiteSpace(fileLocation)) return null;
            try
            {
                var full = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(fileLocation) ? fileLocation : System.IO.Path.Combine(root, fileLocation));
                var folder = System.IO.Path.GetDirectoryName(full);
                if (string.IsNullOrEmpty(folder)) return null;
                var rel = System.IO.Path.GetRelativePath(root, folder);
                if (rel.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(rel)) rel = folder;
                return rel.TrimEnd('\\', '/').ToLowerInvariant();
            }
            catch { return null; }
        }

        /// <summary>True when <paramref name="id"/> is our id of <paramref name="selfEmu"/>: <paramref name="native"/> is
        /// then what the integration gave - its native id, or "" for a by-name id. A decoration another program added
        /// after the JSON ("#c2") stays on the native id.</summary>
        public static bool TryDecode(string? id, string selfEmu, out string native)
        {
            native = "";
            if (!SaveId.TryRead(id, out var d) || d!.Emu != selfEmu) return false;
            int cut = id!.IndexOf('#');
            native = (d.Native ?? "") + (cut >= 0 && d.Native != null ? id.Substring(cut) : "");
            return true;
        }

        /// <summary>The id of a row an integration's AddSaveFile gave back, handed a row whose id was
        /// <paramref name="inboundJson"/> (ours, null when it came in native) decoded to <paramref name="sentNative"/>.
        /// 1. it gave back what it was sent: the very id that came in; 2. another id of its own: that one, ours;
        /// 3. none: the id that came in when it named the save (a native one), else by name from the restored file.</summary>
        public static string? Result(string selfEmu, string? sentNative, string? inboundJson, string? resultId, bool state, string? fileName, int? slot, string? dir = null)
        {
            if (inboundJson != null && string.Equals(resultId ?? "", sentNative ?? "", StringComparison.Ordinal)
                && !(string.IsNullOrEmpty(resultId) && IsByName(inboundJson)))
                return inboundJson;
            if (!string.IsNullOrEmpty(resultId)) return Encode(selfEmu, resultId, state, fileName, slot, dir);
            if (inboundJson != null && SaveId.TryRead(inboundJson, out var d) && d!.Native != null) return inboundJson;
            return Encode(selfEmu, "", state, fileName, slot, dir);
        }

        /// <summary>A by-name id: the row came with no id of its own.</summary>
        private static bool IsByName(string json) => SaveId.TryRead(json, out var d) && d!.Native == null && d.By == "name";

        /// <summary>The way back (lot 7, and our ids turned off): a LaunchBox integration's id to its native form - `native`
        /// as it was; <paramref name="freshGuid"/> for one that had none (a by-name id: LaunchBox makes a GUID for a row
        /// without an id). Anything else is returned as it is.</summary>
        public static string? Reverse(string? id, Func<string> freshGuid)
        {
            if (!SaveId.TryRead(id, out var d) || d!.Emu == null || !d.Emu.StartsWith(SaveOwner.LaunchBoxPrefix, StringComparison.Ordinal)) return id;
            return d.Native ?? freshGuid();
        }

        /// <summary>A row's MatchLineageId once its SaveGroupId goes from <paramref name="oldId"/> to <paramref name="newId"/>:
        /// it follows when it was the same (or empty), else it keeps its own.</summary>
        public static string? Lineage(string? lineage, string? oldId, string? newId)
            => string.IsNullOrEmpty(lineage) || string.Equals(lineage, oldId, StringComparison.Ordinal) ? newId : lineage;
    }
}
