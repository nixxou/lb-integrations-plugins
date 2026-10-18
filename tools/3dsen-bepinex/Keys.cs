// The pack's keys, read here every frame (Unity's legacy Input, which 3dSen itself uses for F3) while 3dSen's window has the
// focus. 3dSen's own actions (InControl, input.cfg) are bound by the LaunchBox plugin so that none of them is on these keys.
//
// --nixx-slots: the pack's state keys over 3dSen's 37 slots (Mehdi 19/10) - F6 / F7 pick the slot (Quick, 1..36, around),
// F2 saves into it, F4 loads it, each said on screen (Toast: a white box, readable over a black screen). A save is 3dSen's own
// Emulator.SaveStateProfile (as its menu does: sav.cfg item with the game's profile, the time, a picture), the picture taken
// as 3dSen takes it when it pauses (ScreenRecorder.Capture) - its hotkey quick save stores none. A load is
// Emulator.LoadStateProfile, refused on an empty slot or one holding another game's state (it would start that game).
//
// --nixx-esc: Esc quits (Application.Quit: 3dSen writes its files as at any quit). With --nixx-quit-confirm (the default),
// the first press only says "Press Esc again to quit" for 2.5 s.

using System;
using UnityEngine;

namespace LbIntegrations.ThreeDSen.Mod
{
    internal sealed class Keys
    {
        private static readonly string[] SlotIds = BuildIds();
        private readonly Args _args;
        private readonly NixxLog _log;
        private readonly MonoBehaviour _host;
        private int _slot;               // index in SlotIds: 0 = quick
        private float _escAt = -10f;
        private bool _busy;

        public Keys(MonoBehaviour host, Args args, NixxLog log) { _host = host; _args = args; _log = log; }

        private static string[] BuildIds()
        {
            var ids = new string[37];
            ids[0] = "quick";
            for (int i = 1; i <= 36; i++) ids[i] = i.ToString();
            return ids;
        }

        private static string Name(string id) => id == "quick" ? "Quick slot" : "Slot " + id;

        public void Tick()
        {
            if (!Application.isFocused) return;
            if (_args.Esc && Input.GetKeyDown(KeyCode.Escape)) Escape();
            if (!_args.Slots) return;
            if (Input.GetKeyDown(KeyCode.F6)) Step(-1);
            else if (Input.GetKeyDown(KeyCode.F7)) Step(1);
            else if (Input.GetKeyDown(KeyCode.F2)) Save();
            else if (Input.GetKeyDown(KeyCode.F4)) Load();
        }

        private void Escape()
        {
            if (!_args.QuitConfirm || Time.unscaledTime - _escAt < 2.5f)
            {
                _log.Info("Esc: quitting");
                Application.Quit();
                return;
            }
            _escAt = Time.unscaledTime;
            Game.Notify("Press Esc again to quit", warning: true, seconds: 2.5f);
        }

        private void Step(int by)
        {
            _slot = (_slot + by + SlotIds.Length) % SlotIds.Length;
            var id = SlotIds[_slot];
            var slot = Game.Slot(id);
            string what = "";
            if (slot != null && !Game.SlotEmpty(slot))
                what = Game.SlotGame(slot) == Game.IdOf(Game.CurrentGame()) ? " - a state of this game" : " - another game's";
            else if (slot != null) what = " - empty";
            Game.Notify(Name(id) + what, seconds: 2f);
        }

        private void Save()
        {
            if (_busy) return;
            var game = Game.CurrentGame();
            if (game == null) { Game.Notify("No game running", true); return; }
            var id = SlotIds[_slot];
            var slot = Game.Slot(id);
            if (slot == null) { Game.Notify(Name(id) + ": 3dSen has no such slot", true); return; }
            _busy = true;
            _host.StartCoroutine(Game.Capture(tex =>
            {
                try
                {
                    Game.Call(Game.Ins(Game.Emulator), "SaveStateProfile", slot, tex);
                    Game.Notify("Saved - " + Name(id), seconds: 2.5f);
                    _log.Info("saved into " + id);
                }
                catch (Exception ex) { _log.Error("save into " + id + ": " + (ex.InnerException ?? ex).Message); Game.Notify("The state could not be saved", true); }
                finally { _busy = false; }
            }));
        }

        private void Load()
        {
            var id = SlotIds[_slot];
            var slot = Game.Slot(id);
            if (slot == null || Game.SlotEmpty(slot)) { Game.Notify(Name(id) + " is empty", true); return; }
            var current = Game.IdOf(Game.CurrentGame());
            if (current != null && Game.SlotGame(slot) != current) { Game.Notify(Name(id) + " holds another game's state", true); return; }
            try
            {
                Game.Call(Game.Ins(Game.Emulator), "LoadStateProfile", slot);
                Game.Notify("Loaded - " + Name(id), seconds: 2.5f);
                _log.Info("loaded " + id);
            }
            catch (Exception ex) { _log.Error("load of " + id + ": " + (ex.InnerException ?? ex).Message); Game.Notify("The state could not be loaded", true); }
        }
    }
}
