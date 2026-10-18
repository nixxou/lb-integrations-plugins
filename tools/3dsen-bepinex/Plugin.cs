// The pack's plugin inside 3dSen (BepInEx 5, Mono x86) - the model of tools\superzsnes-bepinex (Mehdi 19/10). Its whole
// interface is the command line the LaunchBox plugin builds (Args): the data folder and the states' (Folders), the pack's
// keys - slots on F6 / F7, F2 / F4 into them, Esc to quit with a confirmation (Keys) - the cartridge's battery RAM on disk
// (Battery), 3dSen's settings for one session (Overrides) and a dump of them for measuring (Dump). Without "--nixx-" arguments it changes nothing: 3dSen started by
// hand from its folder behaves as it always has.

using System;
using BepInEx;
using HarmonyLib;

namespace LbIntegrations.ThreeDSen.Mod
{
    [BepInPlugin(Guid, "Nixx 3dSen integration", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nixx.lbip.3dsen", Version = "1.0.0";

        private NixxLog _log;
        private Args _args;
        private Keys _keys;

        private void Awake()
        {
            _log = new NixxLog(Logger);
            try
            {
                _args = Args.Parse(Environment.GetCommandLineArgs());
                var folder = _args.Data ?? System.IO.Path.Combine(Paths.GameRootPath, "BepInEx");
                _log.To(folder, _args.Log);
                _log.Info("Nixx 3dSen integration " + Version + " - " + string.Join(" ", Environment.GetCommandLineArgs()));
                foreach (var u in _args.Unknown) _log.Warn("argument not understood: " + u);
                Game.Resolve(_log);
                var h = new Harmony(Guid);
                Try("data folders", () => Folders.Install(h, _args, _log));
                Try("settings", () => Overrides.Install(h, _args, _log));
                Try("battery", () => Battery.Install(h, _args, _log));
                _keys = new Keys(this, _args, _log);
                if (_args.Dump) StartCoroutine(Dump.Run(folder, _log));
            }
            catch (Exception ex) { _log.Error("start: " + ex); }
        }

        private void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { _log.Error(what + ": " + (ex.InnerException ?? ex).Message); }
        }

        private float _batteryAt;

        private void Update()
        {
            try { _keys?.Tick(); }
            catch (Exception ex) { _log.Error("keys: " + ex.Message); _keys = null; }
            if (UnityEngine.Time.unscaledTime - _batteryAt >= 2f) { _batteryAt = UnityEngine.Time.unscaledTime; Battery.Flush("changed"); }
        }

        /// <summary>The window closed or Application.Quit (the pack's Esc): 3dSen's CloseRom is not called on that way out.</summary>
        private void OnApplicationQuit() => Battery.Flush("quit");
    }
}
