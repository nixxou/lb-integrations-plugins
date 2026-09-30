// melonDS after an import to it: told what went in (LbImportFinished, src\Catalog\LbCatalog.cs). Nothing
// to put right for melonDS yet - the line in the log says the watch works for this plugin too.

using System;
using LbIntegrations.Catalog;

namespace LbIntegrations.MelonDs
{
    internal sealed class MelonDsImportFinished : ILbImportFinished
    {
        public void AfterImport(LbImportDone done)
        {
            try
            {
                if (done == null || !MelonDsPaths.IsMelonDsExecutable(done.EmulatorPath)) return;
                Log.Info("[import] after the import to " + done.Platform + ": " + done.Imported.Count + "/" + done.Wanted.Count
                         + " in the library" + (done.Missing.Count == 0 ? "" : ", " + done.Missing.Count + " left out by LaunchBox")
                         + (done.Complete ? "" : " (gave up waiting)"));
            }
            catch { }
        }
    }
}
