// A game's options window, saved (Mehdi, 04/10): OK CHANGES ONLY WHAT THE USER CHANGED.
//
// Before, each window saved what it SHOWED: a value it did not show - a field hidden for the renderer (Vita3K's V-Sync
// under Vulkan), a key no longer offered, a choice no longer in the list, a number clamped for its slider - was dropped
// or replaced at the first OK, untouched. Now a window notes what it shows once it is filled (AtOpen) and, at OK, puts
// only the DIFFERENCES between that and what it shows then onto the game's stored values:
//
//   shown    key -> value; null = shown, on its default (not set); a key ABSENT = not shown (left as it is stored)
//   result   the stored values, each key the user changed set (or removed, back on its default) - nothing else touched
//
// For a window over several games, each one keeps its own values but what the user changed.

#nullable disable

using System;
using System.Collections.Generic;

namespace LbIntegrations.Lbip
{
    internal static class LbipGameEdit
    {
        /// <summary>The game's stored values with the user's changes on them - see the header.</summary>
        public static Dictionary<string, string> Merge(IDictionary<string, string> stored, IDictionary<string, string> atOpen,
                                                       IDictionary<string, string> now, StringComparer keys = null)
        {
            keys ??= StringComparer.OrdinalIgnoreCase;
            var result = stored == null ? new Dictionary<string, string>(keys) : new Dictionary<string, string>(stored, keys);
            foreach (var kv in now ?? new Dictionary<string, string>())
            {
                string before = null;
                atOpen?.TryGetValue(kv.Key, out before);
                if (string.Equals(before, kv.Value, StringComparison.Ordinal)) continue;     // not changed: as stored
                if (kv.Value == null) result.Remove(kv.Key); else result[kv.Key] = kv.Value;
            }
            return result;
        }

        /// <summary>Did the user change anything at all.</summary>
        public static bool Changed(IDictionary<string, string> atOpen, IDictionary<string, string> now)
        {
            foreach (var kv in now ?? new Dictionary<string, string>())
            {
                string before = null;
                atOpen?.TryGetValue(kv.Key, out before);
                if (!string.Equals(before, kv.Value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>The same set of values, in any order.</summary>
        public static bool Same(IDictionary<string, string> a, IDictionary<string, string> b)
        {
            a ??= new Dictionary<string, string>();
            b ??= new Dictionary<string, string>();
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out var v) || !string.Equals(v, kv.Value, StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
