// input.cfg - 3dSen's keys and pads, in its user data folder. The plugin binds the pack's save-state keys there, ONLY
// when 3dSen has none: QuickSave = F2, QuickLoad = F4 (the pack's keys, Mehdi 07/10; what ThreeDSenAhk sends). 3dSen
// ships both UNBOUND (Gamepads.DefKeys: QuickSave / QuickLoad = Key.None) - the pause screens of LaunchBox and BigBox
// would have nothing to press.
//
// THE FORMAT, read in 3dSen 1.1.2 (Gamepads.SaveConfig; InControl's PlayerActionSet.Save, PlayerAction.Save,
// KeyCombo.Save) and measured on its own file (docs\3dsen.md section 7, an exact round trip of both layers):
//   - the file: protobuf-net Dictionary<string,string>, repeated field 1 = { 1: key, 2: value }; keys "Keyboard0 Binding",
//     "Keyboard1 Binding", "Gamepad0 Binding", "Gamepad1 Binding";
//   - a keyboard value: base64 of "BIND", u16 version (2), i32 action count, then per action a .NET BinaryWriter string
//     (7-bit length + UTF-8), i32 binding count, per binding i32 type (2 = KeyBindingSource) then a KeyCombo: i32 include
//     count, u64 include keys (one InControl.Key per byte), i32 exclude count, u64 exclude keys.
//   - InControl.Key: None 0x00, Escape 0x0D, F1 0x0E ... F12 0x19.
// [run] F2 (0x0F) and F4 (0x11) written this way into QuickSave / QuickLoad were used by 3dSen (a quick save on F2, its load
// on F4 in a new session) and kept by its own rewrite at quit.
//
// THE RULE: an action is bound only when every binding it has is Key.None (or it has none), and only when no action of
// either keyboard set already holds the key. Anything this reader does not know (another binding type, a version other
// than 2, a value that does not decode) leaves the whole file as it is. 3dSen writes input.cfg whole at quit
// (Gamepads.OnApplicationQuit): never written while it runs. No input.cfg (3dSen never run): nothing written - 3dSen makes
// its defaults at its first quit, the keys come at the next launch.
//
// THE PACK'S OTHER KEYS CANNOT BE HAD (docs\3dsen.md section 7): F6 / F7 rotate the camera (and there is one quick slot, no
// slot to change), Esc opens 3dSen's menu (question Q3), 3dSen has no full screen action for F11, F3 exports the scene.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenInput
    {
        public const string FileName = "input.cfg";
        public const byte KeyNone = 0x00, KeyF2 = 0x0F, KeyF4 = 0x11;

        internal static readonly (string Action, byte Key)[] Wanted = { ("QuickSave", KeyF2), ("QuickLoad", KeyF4) };
        private static readonly string[] KeyboardSets = { "Keyboard0 Binding", "Keyboard1 Binding" };

        internal sealed class Binding { public int Type, IncSize, ExcSize; public ulong Inc, Exc; }
        internal sealed class Action { public string Name; public List<Binding> Bindings = new List<Binding>(); }
        internal sealed class Set { public ushort Version; public List<Action> Actions = new List<Action>(); }

        // ── protobuf map ─────────────────────────────────────────────────────

        private static ulong Varint(byte[] b, ref int i)
        {
            ulong r = 0; int s = 0;
            while (true)
            {
                if (i >= b.Length) throw new FormatException("truncated varint");
                byte c = b[i++];
                r |= (ulong)(c & 0x7F) << s;
                if (c < 0x80) return r;
                s += 7;
                if (s > 63) throw new FormatException("varint too long");
            }
        }

        private static void WVarint(List<byte> o, ulong n)
        {
            while (true) { byte c = (byte)(n & 0x7F); n >>= 7; if (n != 0) o.Add((byte)(c | 0x80)); else { o.Add(c); return; } }
        }

        internal static List<KeyValuePair<string, string>> ParseMap(byte[] b)
        {
            var list = new List<KeyValuePair<string, string>>();
            int i = 0;
            while (i < b.Length)
            {
                if (Varint(b, ref i) != 0x0A) throw new FormatException("not a map entry");
                int len = checked((int)Varint(b, ref i));
                int end = checked(i + len);
                if (end > b.Length) throw new FormatException("truncated entry");
                string k = null, v = null;
                while (i < end)
                {
                    var tag = Varint(b, ref i);
                    int l2 = checked((int)Varint(b, ref i));
                    if (i + l2 > end) throw new FormatException("truncated field");
                    var s = Encoding.UTF8.GetString(b, i, l2);
                    i += l2;
                    if (tag == 0x0A) k = s; else if (tag == 0x12) v = s; else throw new FormatException("unknown field " + tag);
                }
                list.Add(new KeyValuePair<string, string>(k ?? "", v ?? ""));
            }
            return list;
        }

        internal static byte[] WriteMap(List<KeyValuePair<string, string>> entries)
        {
            var o = new List<byte>();
            foreach (var kv in entries)
            {
                var kb = Encoding.UTF8.GetBytes(kv.Key);
                var vb = Encoding.UTF8.GetBytes(kv.Value);
                var e = new List<byte> { 0x0A };
                WVarint(e, (ulong)kb.Length); e.AddRange(kb);
                e.Add(0x12); WVarint(e, (ulong)vb.Length); e.AddRange(vb);
                o.Add(0x0A); WVarint(o, (ulong)e.Count); o.AddRange(e);
            }
            return o.ToArray();
        }

        // ── InControl's binding data ─────────────────────────────────────────

        internal static Set ParseSet(byte[] b)
        {
            using var r = new BinaryReader(new MemoryStream(b), Encoding.UTF8);
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "BIND") throw new FormatException("not BIND data");
            var set = new Set { Version = r.ReadUInt16() };
            if (set.Version != 2) throw new FormatException("binding version " + set.Version);
            int count = r.ReadInt32();
            for (int a = 0; a < count; a++)
            {
                var action = new Action { Name = r.ReadString() };
                int n = r.ReadInt32();
                for (int k = 0; k < n; k++)
                {
                    int type = r.ReadInt32();
                    if (type != 2) throw new FormatException("binding type " + type + " (" + action.Name + ")");
                    action.Bindings.Add(new Binding { Type = type, IncSize = r.ReadInt32(), Inc = r.ReadUInt64(), ExcSize = r.ReadInt32(), Exc = r.ReadUInt64() });
                }
                set.Actions.Add(action);
            }
            if (r.BaseStream.Position != b.Length) throw new FormatException("bytes after the bindings");
            return set;
        }

        internal static byte[] WriteSet(Set set)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                w.Write(Encoding.ASCII.GetBytes("BIND"));
                w.Write(set.Version);
                w.Write(set.Actions.Count);
                foreach (var a in set.Actions)
                {
                    w.Write(a.Name);
                    w.Write(a.Bindings.Count);
                    foreach (var x in a.Bindings) { w.Write(x.Type); w.Write(x.IncSize); w.Write(x.Inc); w.Write(x.ExcSize); w.Write(x.Exc); }
                }
            }
            return ms.ToArray();
        }

        private static IEnumerable<byte> KeysOf(Binding b)
        {
            for (int i = 0; i < Math.Min(8, Math.Max(0, b.IncSize)); i++) yield return (byte)(b.Inc >> (8 * i));
        }

        private static bool Unbound(Action a) => a.Bindings.All(b => KeysOf(b).All(k => k == KeyNone));

        // ── the launch's edit ────────────────────────────────────────────────

        /// <summary>QuickSave = F2 and QuickLoad = F4 bound in input.cfg when unbound and free. Never throws; the file is
        /// written only when something changed, and left as it is when anything in it is not understood.</summary>
        public static void BindStateKeys(string dataDir)
        {
            var path = Path.Combine(dataDir ?? "", FileName);
            try
            {
                if (!File.Exists(path)) { Log.Info("input.cfg not there yet (3dSen never quit once) - F2 / F4 bound at a later launch"); return; }
                var raw = File.ReadAllBytes(path);
                var entries = ParseMap(raw);
                var sets = new Dictionary<string, Set>();
                foreach (var name in KeyboardSets)
                {
                    var e = entries.FirstOrDefault(kv => kv.Key == name);
                    if (e.Key == null || string.IsNullOrEmpty(e.Value)) continue;
                    sets[name] = ParseSet(Convert.FromBase64String(e.Value));
                }
                if (!sets.TryGetValue(KeyboardSets[0], out var main)) { Log.Info("input.cfg has no first keyboard - nothing bound"); return; }

                var done = new List<string>();
                foreach (var (actionName, key) in Wanted)
                {
                    var action = main.Actions.FirstOrDefault(a => a.Name == actionName);
                    if (action == null) { Log.Info("input.cfg: no action " + actionName); continue; }
                    if (!Unbound(action)) continue;   // the user's, or already ours
                    var holder = sets.Values.SelectMany(s => s.Actions).FirstOrDefault(a => a != action && a.Bindings.Any(b => KeysOf(b).Contains(key)));
                    if (holder != null) { Log.Info("input.cfg: " + actionName + " left unbound - its key is already " + holder.Name + "'s"); continue; }
                    action.Bindings.Clear();
                    action.Bindings.Add(new Binding { Type = 2, IncSize = 1, Inc = key, ExcSize = 0, Exc = 0 });
                    done.Add(actionName + " = F" + (key - 0x0D));
                }
                if (done.Count == 0) return;

                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].Key == KeyboardSets[0])
                        entries[i] = new KeyValuePair<string, string>(entries[i].Key, Convert.ToBase64String(WriteSet(main)));
                var temp = path + ".lbip-tmp";
                File.WriteAllBytes(temp, WriteMap(entries));
                File.Move(temp, path, true);
                Log.Info("input.cfg: " + string.Join(", ", done));
            }
            catch (Exception ex) { Log.Warn("input.cfg left as it is - the save-state keys were not bound", ex); }
        }

        /// <summary>The key of an action of the first keyboard ("F2", "None"), for the pages; null when unreadable.</summary>
        public static string KeyName(string dataDir, string actionName)
        {
            try
            {
                var path = Path.Combine(dataDir ?? "", FileName);
                if (!File.Exists(path)) return null;
                var e = ParseMap(File.ReadAllBytes(path)).FirstOrDefault(kv => kv.Key == KeyboardSets[0]);
                if (string.IsNullOrEmpty(e.Value)) return null;
                var a = ParseSet(Convert.FromBase64String(e.Value)).Actions.FirstOrDefault(x => x.Name == actionName);
                if (a == null) return null;
                var keys = a.Bindings.SelectMany(KeysOf).Where(k => k != KeyNone).ToList();
                return keys.Count == 0 ? "None" : string.Join(" + ", keys.Select(KeyText));
            }
            catch { return null; }
        }

        private static string KeyText(byte k) => k >= 0x0E && k <= 0x19 ? "F" + (k - 0x0D) : k == 0x0D ? "Esc" : "key 0x" + k.ToString("X2");
    }
}
