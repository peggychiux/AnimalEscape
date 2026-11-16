using UnityEngine;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 原型介面（IMGUI）共用的樣式與縮放。只能在 OnGUI 裡呼叫。
    /// 注意：不要指定系統字型，Unity 6.3 載入失敗時整個介面的文字會錯亂；預設字型就能顯示中文。
    /// </summary>
    public static class UiKit
    {
        public const float ReferenceHeight = 720f;

        public static GUIStyle Title { get; private set; }
        public static GUIStyle Header { get; private set; }
        public static GUIStyle Text { get; private set; }
        public static GUIStyle Hint { get; private set; }
        public static GUIStyle Warning { get; private set; }
        public static GUIStyle Button { get; private set; }
        public static GUIStyle Field { get; private set; }
        public static GUIStyle Tag { get; private set; }

        /// <summary>置中的一般文字（畫面中央的提示用）。</summary>
        public static GUIStyle Center { get; private set; }

        static Texture2D s_Backdrop;
        static bool s_Ready;

        /// <summary>
        /// 每次 OnGUI 開頭呼叫：以 720p 為基準等比例縮放（高解析度螢幕上字才不會太小），
        /// 回傳縮放倍率，並輸出縮放後的畫面寬高。
        /// </summary>
        public static float Begin(out float width, out float height)
        {
            EnsureStyles();
            float scale = Mathf.Max(0.5f, Screen.height / ReferenceHeight);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            width = Screen.width / scale;
            height = Screen.height / scale;
            return scale;
        }

        /// <summary>畫一塊半透明的深色底板。</summary>
        public static void Panel(Rect rect)
        {
            GUI.DrawTexture(rect, s_Backdrop, ScaleMode.StretchToFill);
        }

        static void EnsureStyles()
        {
            if (s_Ready && s_Backdrop != null)
            {
                return;
            }

            s_Ready = true;

            s_Backdrop = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            s_Backdrop.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.11f, 0.88f));
            s_Backdrop.Apply();

            Title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            Title.normal.textColor = Color.white;

            Header = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            Header.normal.textColor = Color.white;

            Text = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft
            };
            Text.normal.textColor = new Color(0.92f, 0.93f, 0.95f);

            Hint = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true
            };
            Hint.normal.textColor = new Color(0.72f, 0.75f, 0.80f);

            Warning = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true
            };
            Warning.normal.textColor = new Color(1f, 0.82f, 0.35f);

            Button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15
            };

            Field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft
            };

            Tag = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            Tag.normal.textColor = Color.white;

            Center = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            Center.normal.textColor = new Color(0.92f, 0.93f, 0.95f);
        }

        /// <summary>畫一條進度條（0～1）。</summary>
        public static void Bar(Rect rect, float value, Color fill)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * Mathf.Clamp01(value), rect.height - 4f), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Ready = false;
            s_Backdrop = null;
        }
    }
}
