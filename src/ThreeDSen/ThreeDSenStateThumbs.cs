// A thumbnail beside each save state (Mehdi, 07/10) - LbipStateThumbs's file: sav\5.sav -> sav\5.sav.png, in 3dSen's own
// state folder (3dSen reads only the files sav.cfg names - SaveConfig.path - so a PNG beside them is never seen by it).
//
// THE PICTURE (ThreeDSenSavCfg's header): 3dSen's OWN picture first - the frame its index keeps for a state saved from its
// menu (sav.cfg field 3, 256 x 192). A state saved by the QuickSave key (F2, the pack's key, ThreeDSenInput) carries a blank
// one ([run] 0xCD), so the quick slot is pictured by a CAPTURE of 3dSen's window at the moment its file is written
// (LbipStateThumbs.Watch) - watched for quick.sav only: a numbered slot is saved from 3dSen's menu, which is on screen then
// (the capture would be the menu), and it has its own picture anyway.
//
// WHEN: made again at listing (before LaunchBox backs a state up), after a restore, and once 3dSen has quit after a launch
// (AfterSession): from 3dSen's picture when its state is newer than the PNG; without one, a PNG newer than its state (the
// capture) is kept and an older one dropped - it would show another moment. A PNG whose state is gone (3dSen frees a
// slot when its profile disappears, or the user deletes it in 3dSen's menu) is removed.

using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenStateThumbs
    {
        /// <summary>The thumbnail of one state, with its slot of sav.cfg (null: read it). Never throws.</summary>
        public static void Refresh(string state, ThreeDSenSlot slot)
        {
            try
            {
                if (string.IsNullOrEmpty(state)) return;
                var png = LbipStateThumbs.PathOf(state);
                if (!File.Exists(state)) { LbipStateThumbs.Remove(state); return; }
                bool fresh = File.Exists(png) && File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(state);
                if (fresh) return;
                using (var bmp = ThreeDSenSavCfg.Picture(slot))
                {
                    if (bmp == null)
                    {
                        // No picture of 3dSen's and no newer capture: the old one would show another moment.
                        if (File.Exists(png)) { LbipStateThumbs.Remove(state); Log.Info("thumbnail dropped (older than its state): " + Path.GetFileName(png)); }
                        return;
                    }
                    var tmp = png + ".part";
                    bmp.Save(tmp, ImageFormat.Png);
                    File.Move(tmp, png, overwrite: true);
                    LbIntegrations.Saves.SaveScan.Touched(png);
                }
                Log.Info("thumbnail from 3dSen's own picture: " + Path.GetFileName(png));
            }
            catch (Exception ex) { Log.Warn("thumbnail of " + Path.GetFileName(state) + " not made", ex); }
        }

        /// <summary>Every state of the folder made again, and the thumbnails of states that are gone removed.</summary>
        public static void RefreshAll(string data)
        {
            try
            {
                var slots = ThreeDSenSavCfg.Read(ThreeDSenSavCfg.PathIn(data)) ?? new List<ThreeDSenSlot>();
                foreach (var s in slots.Where(s => !s.Empty && s.Number >= 0)) Refresh(ThreeDSenSavCfg.StatePath(data, s.SlotId), s);
                Orphans(data, slots);
            }
            catch (Exception ex) { Log.Warn("thumbnails of the states", ex); }
        }

        /// <summary>Our PNGs whose state is gone, or whose slot is free in sav.cfg (3dSen's Delete removes the file and
        /// clears the slot's game).</summary>
        public static void Orphans(string data, List<ThreeDSenSlot> slots)
        {
            try
            {
                var dir = ThreeDSenSavCfg.StateDir(data);
                if (!Directory.Exists(dir)) return;
                foreach (var png in Directory.GetFiles(dir, "*.sav" + LbipStateThumbs.Suffix))
                {
                    var state = png.Substring(0, png.Length - LbipStateThumbs.Suffix.Length);
                    var n = ThreeDSenSavCfg.NumberOfFile(state);
                    var slot = slots?.FirstOrDefault(s => s.Number == n);
                    if (File.Exists(state) && (slot == null || !slot.Empty)) continue;
                    try { File.Delete(png); Log.Info("thumbnail removed, its state is gone: " + Path.GetFileName(png)); } catch { }
                }
            }
            catch { }
        }

        /// <summary>A launch's session: the quick slot captured when 3dSen writes it, and every thumbnail made again once
        /// 3dSen has come (a minute to appear) and gone.</summary>
        public static void Session(string exe, string data)
        {
            if (string.IsNullOrEmpty(exe) || string.IsNullOrEmpty(data)) return;
            try
            {
                LbipStateThumbs.Watch(exe, new[] { ThreeDSenSavCfg.StateDir(data) },
                    p => string.Equals(Path.GetFileName(p), ThreeDSenSavCfg.QuickId + ".sav", StringComparison.OrdinalIgnoreCase), m => Log.Info(m));
            }
            catch (Exception ex) { Log.Warn("thumbnail watch", ex); }
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 120 && !ThreeDSenLaunch.AnyRunning(); i++) await Task.Delay(500).ConfigureAwait(false);
                    while (ThreeDSenLaunch.AnyRunning()) await Task.Delay(1000).ConfigureAwait(false);
                    await Task.Delay(4000).ConfigureAwait(false);   // the watch's last capture (it waits for the file to settle) first
                    RefreshAll(data);
                    Log.Info("session over: thumbnails of the states made again");
                }
                catch (Exception ex) { Log.Warn("thumbnails after the session", ex); }
            });
        }
    }
}
