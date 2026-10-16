// The lines of LaunchBox's import list a plugin's sort takes out (Codex review 17/10) - apart from LbipListFill, which is
// WPF, so the probe can test them. BY REFERENCE, not by their place in the snapshot the sort judged: another pass may have
// shifted the list since (ZiNc EX reads the romsets off the window's thread while LaunchBox can still add lines).

using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LbIntegrations.Lbip
{
    internal static class LbipListRows
    {
        /// <summary><paramref name="records"/> taken out of <paramref name="list"/> wherever they now are - by reference, not by
        /// their place in an earlier snapshot. A record no longer in the list is skipped. Returns how many went.</summary>
        public static int Remove(IList list, IEnumerable<object> records)
        {
            var gone = new HashSet<object>(records.Where(r => r != null), ReferenceEqualityComparer.Instance);
            int n = 0;
            for (int i = list.Count - 1; i >= 0 && gone.Count > 0; i--)
                if (gone.Remove(list[i])) { list.RemoveAt(i); n++; }
            return n;
        }

        /// <summary>How many of <paramref name="records"/> are still in <paramref name="list"/> (by reference).</summary>
        public static int CountIn(IList list, IEnumerable<object> records)
        {
            var present = new HashSet<object>(list.Cast<object>(), ReferenceEqualityComparer.Instance);
            return records.Count(r => r != null && present.Contains(r));
        }

    }
}
