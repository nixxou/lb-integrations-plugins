// Controls found wherever they are in a window - its own children and theirs. 05/10: a game's window dressed by NixxShell
// keeps its tabs under the page bar it adds, no longer as the form's own child.

using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Probe
{
    internal static class ProbeUi
    {
        public static IEnumerable<Control> Deep(Control c) => new[] { c }.Concat(c.Controls.Cast<Control>().SelectMany(Deep));
    }
}