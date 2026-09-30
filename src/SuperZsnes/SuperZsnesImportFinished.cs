// SUPER ZSNES after an import to it: told what went in (LbImportFinished, src\Catalog\LbCatalog.cs). Nothing
// to put right for SUPER ZSNES yet - the line in the log says the watch works for this plugin too.

using System;
using LbIntegrations.Catalog;

namespace LbIntegrations.SuperZsnes
{
    internal sealed class SuperZsnesImportFinished : ILbImportFinished
    {
        public void AfterImport(LbImportDone done)
        {
            try
            {
                if (done == null || !SuperZsnesPaths.IsSuperZsnesExecutable(done.EmulatorPath)) return;
                Log.Info("[import] after the import to " + done.Platform + ": " + done.Imported.Count + "/" + done.Wanted.Count
                         + " in the library" + (done.Missing.Count == 0 ? "" : ", " + done.Missing.Count + " left out by LaunchBox")
                         + (done.Complete ? "" : " (gave up waiting)"));
            }
            catch { }
        }
    }
}
