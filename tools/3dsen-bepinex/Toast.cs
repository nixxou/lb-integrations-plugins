// The plugin's own on-screen messages ("Press Esc again to quit", the slot picked, saved / loaded): a white box with a dark
// border, black bold text (dark red for a warning), at the top centre of the screen, over everything. 3dSen's own
// notifications (NotificationManager) are white text with no background - unreadable over a black screen (Mehdi 19/10).
// Drawn in OnGUI (IMGUI, Unity's immediate mode): nothing is added to 3dSen's scene.

using UnityEngine;

namespace LbIntegrations.ThreeDSen.Mod
{
    internal static class Toast
    {
        private static string _text;
        private static bool _warning;
        private static float _until;
        private static GUIStyle _style;
        private static Texture2D _white, _border;

        /// <summary>From Unity's main thread (the plugin's Update); shown until <paramref name="seconds"/> have passed.</summary>
        public static void Show(string text, bool warning, float seconds)
        {
            _text = text;
            _warning = warning;
            _until = Time.unscaledTime + seconds;
        }

        /// <summary>From the plugin's OnGUI.</summary>
        public static void Draw()
        {
            if (_text == null || Time.unscaledTime > _until) return;
            if (Event.current.type != EventType.Repaint) return;
            if (_white == null) { _white = Solid(Color.white); _border = Solid(new Color(0.1f, 0.1f, 0.1f)); }
            int size = Mathf.Clamp(Screen.height / 26, 14, 64);
            if (_style == null || _style.fontSize != size)
                _style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = false };
            _style.normal.textColor = _warning ? new Color(0.65f, 0f, 0f) : Color.black;

            var content = new GUIContent(_text);
            var text = _style.CalcSize(content);
            float padX = size * 0.9f, padY = size * 0.45f, edge = Mathf.Max(2f, size / 10f);
            float w = Mathf.Min(text.x + 2 * padX, Screen.width - 20), h = text.y + 2 * padY;
            var box = new Rect((Screen.width - w) / 2f, Screen.height * 0.08f, w, h);
            GUI.depth = -1000;
            GUI.DrawTexture(new Rect(box.x - edge, box.y - edge, box.width + 2 * edge, box.height + 2 * edge), _border);
            GUI.DrawTexture(box, _white);
            GUI.Label(box, content, _style);
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
