// PUTS THE LABELS ON, AND TAKES THEM OFF.
//
// LaunchBox and Big Box both build their emulator lists ("Launch With", Big Box's emulator choice,
// the emulator box of Edit Game) from the same Emulator objects, reading Title when the list is
// shown. So the labels are not put on a menu - which Big Box's themes would each draw their own way -
// but on the emulators themselves, for the game selected at that moment, and changed at every
// selection.
//
// The original title is never stored: it is the current title with its label stripped (Label.Strip),
// so an emulator replaced by LaunchBox (an edit makes a new object) is handled like any other.

using System;
using System.Collections.Generic;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.EmuLabels
{
    internal static class Labeler
    {
        /// <summary>Label every emulator for this game; a null game takes every label off.
        /// Returns how many titles were changed.</summary>
        public static int Apply(IEnumerable<IEmulator> emulators, IGame game, CompatTable table)
        {
            if (emulators == null) return 0;
            int changed = 0;
            foreach (var e in emulators)
            {
                if (e == null) continue;
                try
                {
                    var current = TitleField.Get(e);
                    var original = Label.Strip(current);
                    var text = game == null || table == null ? null : table.LabelFor(game, e, original);
                    var wanted = Label.Apply(original, text);
                    if (string.Equals(current, wanted, StringComparison.Ordinal)) continue;
                    TitleField.Set(e, wanted);
                    changed++;
                }
                catch (Exception ex) { Log.Warn("labelling " + e.Id, ex); }
            }
            return changed;
        }

        public static int Clear(IEnumerable<IEmulator> emulators) => Apply(emulators, null, null);
    }
}
