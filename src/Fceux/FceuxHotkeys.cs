// FCEUX's hotkeys, and the pack's bound where FCEUX lets them be (Mehdi, 07/10: F2 save state, F4 load state, F6 / F7 the
// slot down / up, Esc quit).
//
// THE TABLE (drivers\win\input.cpp:1839 VAC(FCEUD_CommandMapping, 2), read at the fceux-2.7.0 tag; decoded from a first-run
// fceux.cfg, docs\fceux.md §6): "FCEUD_CommandMapping_V2 base64:..." holds one ButtConfig per EMUCMD_* command, IN THE
// ENUM'S ORDER (src\input.h:110-264 - 129 commands, the index is the enum value). A record is 92 bytes
// (drivers\win\input.h:44, MAXBUTTCONFIG 4):
//
//     uint8 ButtType[4]     0 = keyboard           uint8 DeviceNum[4]
//     int32 ButtonNum[4]    keyboard: the DirectInput scan code | the modifier bits (LSHIFT 1<<16, RSHIFT 1<<17, LCTRL
//                           1<<18, RCTRL 1<<19, LALT 1<<20, RALT 1<<21 - input.h:6-15)
//     uint32 NumC           how many of the 4 are used        GUID DeviceInstance[4]
//
// The pads (GamePadConfig_V2) and the auto-hold keys (autoHoldKeys, autoHoldClearKeys) are ButtConfigs of the same layout.
// 2.6.5, 2.6.6 and 2.7.0 have the same 129 commands in the same order (their src\input.h compared 18/10); 2.6.4's list
// differs - a table of another length is left alone.
//
// FCEUX FIRES EVERY COMMAND ON A KEY (src\input.cpp:1017 FCEUI_HandleEmuCommands tests the whole table; a command matches
// only with no extra modifier held), and its defaults (drivers\win\mapinput.cpp:41-99, DefaultCommandMapping) already
// use the pack's keys: F2 / F4 / F6 / F7 load slots 2 / 4 / 6 / 7, Esc hides the menu. So, as RASnes9x does with Snes9x's:
//
//     F2   added to SAVE_STATE (26), whose default is I     LOAD_STATE_SLOT_2 (42) given up while still its default
//     F4   added to LOAD_STATE (38), whose default is P     LOAD_STATE_SLOT_4 (44)
//     F6   SAVE_SLOT_PREV (25), none by default             LOAD_STATE_SLOT_6 (46)
//     F7   SAVE_SLOT_NEXT (24), none by default             LOAD_STATE_SLOT_7 (47)
//     Esc  EXIT (6), none by default                        HIDE_MENU_TOGGLE (5); TASEDITOR_CANCEL_SEEKING (113) keeps
//                                                           Esc: it acts in the TAS editor only
//
// A command is bound only while it is empty or still exactly FCEUX's default (I, P), and only when no other command, pad
// button or auto-hold key of the user's holds the key with no modifier. A key the user put anywhere is never changed.
// F11 is not bound: full screen is Alt+Enter (MISC_TOGGLEFULLSCREEN, 100) and that command is not empty.
//
// MEASURED (docs\fceux.md §6, 18/10): with SAVE_STATE = {I, F2}, LOAD_STATE = {P, F4}, slots 2 and 4 emptied, EXIT = Esc,
// AutoHotkey's F2 saved the current slot's state, Esc quit FCEUX (the .sav written), and the table was still there after
// FCEUX's own exit save. .NET SendKeys reaches nothing: FCEUX reads the keyboard through DirectInput.

using System;
using System.Collections.Generic;
using System.Linq;

namespace LbIntegrations.Fceux
{
    internal static class FceuxHotkeys
    {
        public const int RecordSize = 92;
        public const int Commands = 129;

        // The commands (src\input.h, enum EMUCMD).
        public const int HideMenuToggle = 5, Exit = 6, SaveSlotNext = 24, SaveSlotPrev = 25, SaveState = 26, LoadState = 38;
        public const int LoadStateSlot2 = 42, LoadStateSlot4 = 44, LoadStateSlot6 = 46, LoadStateSlot7 = 47;
        public const int ToggleFullscreen = 100, TasEditorCancelSeeking = 113;

        // DirectInput scan codes.
        public const int Esc = 0x01, F2 = 0x3C, F4 = 0x3E, F6 = 0x40, F7 = 0x41, KeyI = 0x17, KeyP = 0x19;

        /// <summary>One pack key: the command it goes to, FCEUX's own default of that command (kept beside it), FCEUX's
        /// default on the key that would fire too (given up while still so), and the defaults that may share it.</summary>
        internal sealed class Binding
        {
            public string Name; public int Command; public int Key; public int[] Default = Array.Empty<int>();
            public int? GivesUp; public int[] Shares = Array.Empty<int>();
        }

        internal static readonly Binding[] Pack =
        {
            new Binding { Name = "save state (F2)", Command = SaveState, Key = F2, Default = new[] { KeyI }, GivesUp = LoadStateSlot2 },
            new Binding { Name = "load state (F4)", Command = LoadState, Key = F4, Default = new[] { KeyP }, GivesUp = LoadStateSlot4 },
            new Binding { Name = "previous slot (F6)", Command = SaveSlotPrev, Key = F6, GivesUp = LoadStateSlot6 },
            new Binding { Name = "next slot (F7)", Command = SaveSlotNext, Key = F7, GivesUp = LoadStateSlot7 },
            new Binding { Name = "quit (Esc)", Command = Exit, Key = Esc, GivesUp = HideMenuToggle, Shares = new[] { TasEditorCancelSeeking } },
        };

        /// <summary>FCEUX 2.7.0's default table (drivers\win\mapinput.cpp DefaultCommandMapping), as (command, key) - every
        /// record one keyboard key, nothing else set: rebuilt from this, it is byte for byte the table of a first-run
        /// fceux.cfg (probe --fceux-hotkeys). Used when FCEUX has not written its file yet.</summary>
        internal static readonly (int Command, int Key)[] Defaults =
        {
            (1, 0xC0013), (2, 0xC5), (3, 0x2B), (4, 0x58), (5, 0x01), (8, 0x0C), (10, 0x0D), (12, 0x0F),
            (14, 0x0B), (15, 0x02), (16, 0x03), (17, 0x04), (18, 0x05), (19, 0x06), (20, 0x07), (21, 0x08), (22, 0x09), (23, 0x0A),
            (26, 0x17), (28, 0x30044), (29, 0x3003B), (30, 0x3003C), (31, 0x3003D), (32, 0x3003E), (33, 0x3003F), (34, 0x30040),
            (35, 0x30041), (36, 0x30042), (37, 0x30043), (38, 0x19), (40, 0x44), (41, 0x3B), (42, 0x3C), (43, 0x3D), (44, 0x3E),
            (45, 0x3F), (46, 0x40), (47, 0x41), (48, 0x42), (49, 0x43), (52, 0x30013), (54, 0x10), (55, 0x34), (56, 0x33),
            (58, 0x30026), (85, 0x35), (95, 0xC0018), (96, 0xC0011), (97, 0xC003B), (99, 0xC002C), (100, 0x30001C),
            (110, 0x0E), (111, 0x32), (112, 0x39), (113, 0x01), (114, 0xC0039), (115, 0x11),
        };

        public static byte[] DefaultTable()
        {
            var table = new byte[Commands * RecordSize];
            foreach (var (cmd, key) in Defaults) WriteKeys(table, cmd, new[] { key });
            return table;
        }

        /// <summary>The keyboard keys of a record (scan code with its modifier bits), in its order; a pad entry is -1 - it
        /// still counts as a binding of the user's.</summary>
        public static List<int> KeysOf(byte[] table, int index)
        {
            var list = new List<int>();
            int at = index * RecordSize;
            if (table == null || at + RecordSize > table.Length) return list;
            uint numc = BitConverter.ToUInt32(table, at + 24);
            for (int j = 0; j < Math.Min(numc, 4u); j++)
                list.Add(table[at + j] == 0 ? BitConverter.ToInt32(table, at + 8 + 4 * j) : -1);
            return list;
        }

        /// <summary>A record set to these keyboard keys, everything else zero - as FCEUX's own default records are. Only
        /// ever done to a record that is empty or still FCEUX's default (keyboard keys alone), so nothing of the user's is lost.</summary>
        private static void WriteKeys(byte[] table, int index, int[] keys)
        {
            int at = index * RecordSize;
            Array.Clear(table, at, RecordSize);
            for (int j = 0; j < keys.Length && j < 4; j++)
            {
                table[at + j] = 0;          // BUTTC_KEYBOARD
                table[at + 4 + j] = 0;      // DeviceNum
                BitConverter.GetBytes(keys[j]).CopyTo(table, at + 8 + 4 * j);
            }
            BitConverter.GetBytes((uint)Math.Min(keys.Length, 4)).CopyTo(table, at + 24);
        }

        private static bool Same(List<int> keys, int[] wanted) => keys.Count == wanted.Length && keys.SequenceEqual(wanted);

        /// <summary>Is <paramref name="key"/> (no modifier) held by a record of <paramref name="table"/> other than those of
        /// <paramref name="except"/>? Returns its index, or -1.</summary>
        private static int HeldBy(byte[] table, int key, ISet<int> except)
        {
            if (table == null) return -1;
            for (int i = 0; i < table.Length / RecordSize; i++)
                if (!except.Contains(i) && KeysOf(table, i).Contains(key)) return i;
            return -1;
        }

        /// <summary>The pack's keys bound into the file's table, by the rules of the header. What was done, one line each
        /// (for the log); the file is changed in memory only - the caller saves it.</summary>
        public static List<string> Bind(FceuxCfg cfg)
        {
            var said = new List<string>();
            var table = cfg.GetBlob(FceuxCfg.CommandMapping);
            if (table == null)
            {
                if (cfg.Get(FceuxCfg.CommandMapping) != null) { said.Add("hotkeys: the table in fceux.cfg cannot be read - left as it is"); return said; }
                table = DefaultTable();     // FCEUX never wrote it: its defaults, which is what it would start with
            }
            if (table.Length != Commands * RecordSize)
            {
                said.Add("hotkeys: the table holds " + (table.Length / (double)RecordSize) + " commands, not FCEUX 2.7.0's " + Commands + " - another version's, left as it is");
                return said;
            }
            var pads = new[] { FceuxCfg.GamePads, FceuxCfg.AutoHold, FceuxCfg.AutoHoldClear }.Select(k => (Key: k, Table: cfg.GetBlob(k))).ToList();
            var before = (byte[])table.Clone();

            foreach (var b in Pack)
            {
                var now = KeysOf(table, b.Command);
                if (now.Contains(b.Key)) continue;                                     // already there
                bool asShipped = now.Count == 0 || Same(now, b.Default);
                if (!asShipped) { said.Add("hotkeys: " + b.Name + " not bound - the command was given other keys in FCEUX"); continue; }

                bool givesUp = b.GivesUp.HasValue && Same(KeysOf(table, b.GivesUp.Value), new[] { b.Key });
                var except = new HashSet<int>(b.Shares.Concat(new[] { b.Command }));
                if (givesUp) except.Add(b.GivesUp.Value);
                int holder = HeldBy(table, b.Key, except);
                if (holder >= 0) { said.Add("hotkeys: " + b.Name + " not bound - the key is command " + holder + "'s in FCEUX"); continue; }
                var pad = pads.FirstOrDefault(p => HeldBy(p.Table, b.Key, new HashSet<int>()) >= 0);
                if (pad.Key != null) { said.Add("hotkeys: " + b.Name + " not bound - the key is in " + pad.Key); continue; }

                WriteKeys(table, b.Command, now.Concat(new[] { b.Key }).ToArray());
                if (givesUp) WriteKeys(table, b.GivesUp.Value, Array.Empty<int>());
                said.Add("hotkeys: " + b.Name + " bound" + (givesUp ? ", FCEUX's default on the key (command " + b.GivesUp + ") given up" : ""));
            }
            if (!table.SequenceEqual(before) || cfg.Get(FceuxCfg.CommandMapping) == null) cfg.SetBlob(FceuxCfg.CommandMapping, table);
            return said;
        }
    }
}
