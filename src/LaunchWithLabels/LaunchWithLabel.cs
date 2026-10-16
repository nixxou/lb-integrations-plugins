// "Launch With", in whatever language LaunchBox runs: the localized labels whose ENGLISH value is
// "Launch With", read from LaunchBox's resx (Unbroken.LaunchBox.Properties.Strings), the way
// litebox-parental's AdminGuard classifies its menus. English and French are kept as a fallback when the
// resx cannot be read.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;

namespace LbIntegrations.LaunchWithLabels
{
    internal static class LaunchWithLabel
    {
        private static readonly HashSet<string> Labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "launch with", "lancer avec" };
        private static bool _built;

        public static bool Matches(string header)
        {
            if (string.IsNullOrEmpty(header)) return false;
            Build();
            return Labels.Contains(Norm(header));
        }

        private static void Build()
        {
            if (_built) return;
            _built = true;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Unbroken.LaunchBox");
                var t = asm?.GetType("Unbroken.LaunchBox.Properties.Strings");
                var rm = t?.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as ResourceManager;
                if (rm == null) { Log.Info("resx unavailable: \"Launch With\" matched in English and French only"); return; }

                CultureInfo cur = null;
                try { cur = t.GetProperty("Culture", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as CultureInfo; } catch { }
                cur ??= CultureInfo.CurrentUICulture;

                var neutral = rm.GetResourceSet(CultureInfo.InvariantCulture, true, true);
                int n = 0;
                foreach (DictionaryEntry de in neutral)
                {
                    if (!(de.Value is string en) || Norm(en) != "launch with") continue;
                    var key = de.Key?.ToString();
                    string loc; try { loc = rm.GetString(key, cur) ?? en; } catch { loc = en; }
                    if (Labels.Add(Norm(loc))) n++;
                    Log.Info("\"Launch With\" label: resx key " + key + " = \"" + loc + "\" (" + cur.Name + ")");
                }
                if (n == 0) Log.Info("resx read, no new \"Launch With\" label for " + cur.Name);
            }
            catch (Exception ex) { Log.Warn("reading LaunchBox's resx", ex); }
        }

        /// <summary>Lowercase, no accelerator after a tab, no access-key '_', no trailing dots or ellipsis.</summary>
        private static string Norm(string s)
        {
            var h = s.ToLowerInvariant();
            int tab = h.IndexOf('\t'); if (tab >= 0) h = h.Substring(0, tab);
            return h.Replace("_", "").Trim().TrimEnd('.', '…', ' ');
        }
    }
}
