// input.cfg - 3dSen's keys and pads, in its user data folder. The plugin binds the pack's keys there (Table):
//   QuickSave F2, QuickLoad F4   - 3dSen ships both UNBOUND (Gamepads.DefKeys: Key.None); what ThreeDSenAhk sends
//   Menu F1                      - off Escape, its default: Esc quits, as everywhere in the pack (Mehdi 19/10), through
//                                  the running AutoHotkey script (3dSen has no quit action)
//   FastForward L                - off Period, its default: the pack's held fast forward (VRUIController reads it with
//                                  Pressed every frame and Engine re-arms 30 fast frames - it runs while held)
//
// THE FORMAT, read in 3dSen 1.1.2 (Gamepads.SaveConfig; InControl's PlayerActionSet.Save, PlayerAction.Save,
// KeyCombo.Save) and measured on its own file (docs\3dsen.md section 7, an exact round trip of both layers):
//   - the file: protobuf-net Dictionary<string,string>, repeated field 1 = { 1: key, 2: value }; keys "Keyboard0 Binding",
//     "Keyboard1 Binding", "Gamepad0 Binding", "Gamepad1 Binding";
//   - a keyboard value: base64 of "BIND", u16 version (2), i32 action count, then per action a .NET BinaryWriter string
//     (7-bit length + UTF-8), i32 binding count, per binding i32 type (2 = KeyBindingSource) then a KeyCombo: i32 include
//     count, u64 include keys (one InControl.Key per byte), i32 exclude count, u64 exclude keys.
//   - InControl.Key (enum order of InControl\Key.cs): None 0x00, Escape 0x0D, F1 0x0E ... F12 0x19, A 0x24 ... Z 0x3D
//     (L 0x2F), Period 0x4A.
// [run] F2 (0x0F) and F4 (0x11) written this way into QuickSave / QuickLoad were used by 3dSen (a quick save on F2, its load
// on F4 in a new session) and kept by its own rewrite at quit.
//
// THE RULE (the pack's, as puNES and FCEUX): an action is set only when it is unbound, on 3dSen's default, or already on the
// pack's key - one the user changed is the user's. A pack key another action of the user's holds leaves the action as it
// is; an action that keeps a key another action of this pass takes is unbound - never one key on two actions. Anything
// this reader does not know (another binding type, a version other than 2, a value that does not decode) leaves the whole
// file as it is. 3dSen writes input.cfg whole at quit (Gamepads.OnApplicationQuit): never written while it runs. No
// input.cfg (3dSen never run): nothing written - 3dSen makes its defaults at its first quit, the keys come at the next launch.
//
// THE PACK'S OTHER KEYS CANNOT BE HAD (docs\3dsen.md section 7): F6 / F7 / F8 turn the camera (and there is one quick slot,
// no slot to change), 3dSen has no full screen action for F11, F3 exports the scene.

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
        public const byte KeyNone = 0x00, KeyEscape = 0x0D, KeyF1 = 0x0E, KeyF2 = 0x0F, KeyF4 = 0x11, KeyL = 0x2F, KeyPeriod = 0x4A;

        /// <summary>The pack's key on each of 3dSen's actions, with 3dSen 1.1.2's default (Gamepads.DefKeys).</summary>
        internal static readonly (string Action, byte Key, byte Default)[] Table =
        {
            ("QuickSave", KeyF2, KeyNone), ("QuickLoad", KeyF4, KeyNone), ("Menu", KeyF1, KeyEscape), ("FastForward", KeyL, KeyPeriod),
        };
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

        private static List<byte> Bound(Action a) => a.Bindings.SelectMany(KeysOf).Where(k => k != KeyNone).Distinct().ToList();

        /// <summary>An action this plugin may set: unbound, on 3dSen's default, or on the pack's key already.</summary>
        private static bool IsOurs(Action a, byte key, byte dflt)
        {
            var keys = Bound(a);
            return keys.Count == 0 || (keys.Count == 1 && (keys[0] == key || (dflt != KeyNone && keys[0] == dflt)));
        }

        // ── the launch's edit ────────────────────────────────────────────────

        /// <summary>The pack's keys (Table) bound in input.cfg where the rule lets them be. Never throws; the file is written
        /// only when something changed, and left as it is when anything in it is not understood.</summary>
        public static void BindStateKeys(string dataDir)
        {
            var path = Path.Combine(dataDir ?? "", FileName);
            try
            {
                if (!File.Exists(path)) { Log.Info("input.cfg not there yet (3dSen never quit once) - the pack's keys bound at a later launch"); return; }
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
                var ours = Table.Select(t => (t, Action: main.Actions.FirstOrDefault(a => a.Name == t.Action)))
                                .Where(x => x.Action != null && IsOurs(x.Action, x.t.Key, x.t.Default)).ToList();
                var moving = new HashSet<Action>(ours.Select(x => x.Action));
                foreach (var (t, action) in ours)
                {
                    var now = Bound(action);
                    if (now.Count == 1 && now[0] == t.Key) continue;   // already the pack's
                    var holder = sets.Values.SelectMany(s => s.Actions).FirstOrDefault(a => a != action && !moving.Contains(a) && Bound(a).Contains(t.Key));
                    if (holder != null) { Log.Info("input.cfg: " + t.Action + " left as it is - " + KeyText(t.Key) + " is already " + holder.Name + "'s"); continue; }
                    action.Bindings.Clear();
                    action.Bindings.Add(new Binding { Type = 2, IncSize = 1, Inc = t.Key, ExcSize = 0, Exc = 0 });
                    done.Add(t.Action + " = " + KeyText(t.Key));
                }
                foreach (var t in Table.Where(t => main.Actions.All(a => a.Name != t.Action))) Log.Info("input.cfg: no action " + t.Action);
                if (done.Count == 0) return;

                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].Key == KeyboardSets[0])
                        entries[i] = new KeyValuePair<string, string>(entries[i].Key, Convert.ToBase64String(WriteSet(main)));
                var temp = path + ".lbip-tmp";
                File.WriteAllBytes(temp, WriteMap(entries));
                File.Move(temp, path, true);
                Log.Info("input.cfg: " + string.Join(", ", done));
            }
            catch (Exception ex) { Log.Warn("input.cfg left as it is - the pack's keys were not bound", ex); }
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

        private static string KeyText(byte k) => k >= 0x0E && k <= 0x19 ? "F" + (k - 0x0D) : k == KeyEscape ? "Esc" : k >= 0x24 && k <= 0x3D ? ((char)('A' + k - 0x24)).ToString()
                                                : k == KeyPeriod ? "Period" : "key 0x" + k.ToString("X2");
    }
}
