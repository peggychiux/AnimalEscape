using System.Collections.Generic;
using UnityEngine;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 主選單、測試房間、F1 開發者面板共用的樣式（IMGUI）。顏色照 claude.ai 上的設計稿「動物逃脫 主選單」。
    ///
    /// 這個專案是線性色彩空間，IMGUI 會把顏色和貼圖再轉一次而偏淡（見 CLAUDE.md），所以這裡的顏色一律先轉成線性值。
    /// 圓角用程式畫一張小貼圖再九宮格拉伸，不需要美術檔。
    /// </summary>
    public static class MenuKit
    {
        public const float CanvasWidth = 1280f;
        public const float CanvasHeight = 720f;

        // 設計稿的顏色（sRGB）
        public static readonly Color Background = Hex(0x14201B);
        public static readonly Color Card = Hex(0x1E2B25);
        public static readonly Color CardBorder = Hex(0x2E3F37);
        public static readonly Color Line = Hex(0x3A4D44);
        public static readonly Color DeepCard = Hex(0x1A2721);
        public static readonly Color SelectedCard = Hex(0x26352E);
        public static readonly Color TextColor = Hex(0xEEF2EE);
        public static readonly Color Soft = Hex(0xDCE5DF);
        public static readonly Color Muted = Hex(0xA9B8AF);
        public static readonly Color Dim = Hex(0x8FA096);
        public static readonly Color Accent = Hex(0xF2A33A);
        public static readonly Color AccentText = Hex(0x1A1206);
        public static readonly Color Blue = Hex(0x7FA7F5);
        public static readonly Color BlueText = Hex(0x0E1A2E);
        public static readonly Color BlueChip = Hex(0x22324A);
        public static readonly Color BlueChipText = Hex(0xA9C3F7);
        public static readonly Color Green = Hex(0x8FD3A8);
        public static readonly Color BotBorder = Hex(0x5A4A2A);
        public static readonly Color BotText = Hex(0xF2C27A);
        public static readonly Color Danger = Hex(0xF08A7A);

        /// <summary>按鈕的顏色組合。</summary>
        public enum Tone
        {
            /// <summary>橘色實心（主要動作，正式模式的選中）。</summary>
            Accent,

            /// <summary>藍色實心（測試模式的主要動作、選中）。</summary>
            Blue,

            /// <summary>深色底＋細框（一般按鈕）。</summary>
            Secondary,

            /// <summary>透明底＋細框（沒選中的選項）。</summary>
            Outline,

            /// <summary>透明底、沒有框（離開遊戲這種次要的）。</summary>
            Ghost,

            /// <summary>淺色實心（「加入」）。</summary>
            Light,

            /// <summary>透明底＋虛線感的淡框（「只看」）。</summary>
            Faint,

            /// <summary>透明底＋橘框（「加電腦玩家」）。</summary>
            AccentOutline
        }

        public static GUIStyle H1 { get; private set; }
        public static GUIStyle H1Small { get; private set; }
        public static GUIStyle H2 { get; private set; }
        public static GUIStyle H3 { get; private set; }
        public static GUIStyle Eyebrow { get; private set; }
        public static GUIStyle Body { get; private set; }
        public static GUIStyle BodyLarge { get; private set; }
        public static GUIStyle Label { get; private set; }
        public static GUIStyle Small { get; private set; }
        public static GUIStyle SmallStrong { get; private set; }
        public static GUIStyle Code { get; private set; }
        public static GUIStyle Field { get; private set; }
        public static GUIStyle FieldLarge { get; private set; }
        public static GUIStyle SliderTrack { get; private set; }
        public static GUIStyle SliderThumb { get; private set; }

        static GUIStyle s_CardStyle;
        static GUIStyle s_DeepCardStyle;
        static GUIStyle s_InsetStyle;
        static GUIStyle s_SelectedStyle;
        static GUIStyle s_BlueFrameStyle;
        static GUIStyle s_BotRowStyle;
        static GUIStyle s_DashedRowStyle;
        static GUIStyle s_PanelStyle;
        static GUIStyle s_ChipBlue;
        static GUIStyle s_CheckOn;
        static GUIStyle s_CheckOff;
        static GUIStyle s_CheckOnAccent;
        static GUIStyle s_RadioOn;
        static GUIStyle s_RadioOff;
        static readonly Dictionary<long, GUIStyle> s_ButtonStyles = new Dictionary<long, GUIStyle>();
        static readonly List<Texture2D> s_Textures = new List<Texture2D>();
        static bool s_Ready;

        static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
        }

        /// <summary>給 IMGUI 用的顏色（線性色彩空間時先轉成線性值，畫出來才是原本的顏色）。</summary>
        public static Color Gui(Color color)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }

        /// <summary>
        /// 每次 OnGUI 開頭呼叫：畫面至少有 1280 x 720 的空間（寬度不夠時整個縮小），回傳縮放倍率；
        /// origin 是 1280 x 720 設計稿的左上角（畫面比較寬或高時置中）。
        /// </summary>
        public static float Begin(out float width, out float height, out Vector2 origin)
        {
            Ensure();
            float scale = Mathf.Max(0.4f, Mathf.Min(Screen.height / CanvasHeight, Screen.width / CanvasWidth));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            width = Screen.width / scale;
            height = Screen.height / scale;
            origin = new Vector2(Mathf.Floor((width - CanvasWidth) * 0.5f), Mathf.Floor((height - CanvasHeight) * 0.5f));
            return scale;
        }

        /// <summary>只準備樣式（在別人的縮放下畫，例如 F1 面板）。</summary>
        public static void Ensure()
        {
            if (s_Ready && s_CardStyle != null && s_CardStyle.normal.background != null)
            {
                return;
            }

            Build();
        }

        // ------------------------------------------------------------------
        // 底板
        // ------------------------------------------------------------------

        /// <summary>整個畫面的底色。</summary>
        public static void Backdrop(float width, float height, float alpha = 1f)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color previous = GUI.color;
            Color color = Gui(Background);
            color.a = alpha;
            GUI.color = color;
            GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>圓角卡片（#1E2B25 底、#2E3F37 框）。</summary>
        public static void CardBox(Rect rect)
        {
            Box(rect, s_CardStyle);
        }

        /// <summary>主選單右邊的說明框（比卡片再深一點）。</summary>
        public static void DeepBox(Rect rect)
        {
            Box(rect, s_DeepCardStyle);
        }

        /// <summary>卡片裡面的凹槽（#14201B，房間列表、玩家列）。</summary>
        public static void InsetBox(Rect rect)
        {
            Box(rect, s_InsetStyle);
        }

        /// <summary>藍框的凹槽（線上代碼）。</summary>
        public static void BlueFrameBox(Rect rect)
        {
            Box(rect, s_BlueFrameStyle);
        }

        /// <summary>電腦玩家的列（深色底、咖啡色框）。</summary>
        public static void BotRowBox(Rect rect)
        {
            Box(rect, s_BotRowStyle);
        }

        /// <summary>空位（淡框）。</summary>
        public static void DashedBox(Rect rect)
        {
            Box(rect, s_DashedRowStyle);
        }

        /// <summary>F1 面板的底板（幾乎不透明的深綠）。</summary>
        public static void PanelBox(Rect rect)
        {
            Box(rect, s_PanelStyle);
        }

        /// <summary>藍色小標籤（「開發用」「測試模式」「重複」）。</summary>
        public static void ChipBlue(Rect rect, string text)
        {
            GUI.Label(rect, text, s_ChipBlue);
        }

        static void Box(Rect rect, GUIStyle style)
        {
            if (Event.current.type == EventType.Repaint)
            {
                style.Draw(rect, GUIContent.none, false, false, false, false);
            }
        }

        // ------------------------------------------------------------------
        // 按鈕與選項
        // ------------------------------------------------------------------

        /// <summary>一般按鈕。回傳這一格有沒有被按。</summary>
        public static bool Button(Rect rect, string text, Tone tone, int fontSize = 16, bool bold = false, TextAnchor align = TextAnchor.MiddleCenter)
        {
            return GUI.Button(rect, text, ButtonStyle(tone, fontSize, bold, align));
        }

        /// <summary>二選一、三選一這種選項：選中的是實心（accent 決定橘色或藍色），沒選中的是細框。</summary>
        public static bool Choice(Rect rect, string text, bool selected, bool blue, int fontSize = 16)
        {
            Tone tone = selected ? (blue ? Tone.Blue : Tone.Accent) : Tone.Outline;
            return GUI.Button(rect, text, ButtonStyle(tone, fontSize, selected, TextAnchor.MiddleCenter));
        }

        /// <summary>設計稿「選中時是粗框、底色略亮」的選項（地圖、比賽時間）。</summary>
        public static bool FramedChoice(Rect rect, string title, string subtitle, bool selected, int titleSize = 17)
        {
            bool clicked = GUI.Button(rect, GUIContent.none, selected ? s_SelectedStyle : ButtonStyle(Tone.Outline, 16, false, TextAnchor.MiddleCenter));
            if (string.IsNullOrEmpty(subtitle))
            {
                GUI.Label(rect, title, Styled(titleSize, selected, TextAnchor.MiddleCenter, TextColor));
            }
            else
            {
                GUI.Label(new Rect(rect.x + 14f, rect.y + 8f, rect.width - 20f, 24f), title, Styled(titleSize, true, TextAnchor.MiddleLeft, TextColor));
                GUI.Label(new Rect(rect.x + 14f, rect.y + 32f, rect.width - 20f, 20f), subtitle, Styled(13, false, TextAnchor.MiddleLeft, Muted));
            }

            return clicked;
        }

        /// <summary>勾選框＋文字。回傳新的值。</summary>
        public static bool Check(Rect rect, bool value, string text, bool enabled = true, bool blue = true, int fontSize = 15)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            var box = new Rect(rect.x, rect.y + (rect.height - 20f) * 0.5f, 20f, 20f);
            GUIStyle boxStyle = value ? (blue ? s_CheckOn : s_CheckOnAccent) : s_CheckOff;
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            Box(box, boxStyle);
            if (value && Event.current.type == EventType.Repaint)
            {
                Color mark = blue ? BlueText : AccentText;
                Segment(new Vector2(box.x + 4.5f, box.y + 10.5f), new Vector2(box.x + 8.5f, box.y + 14.5f), 2.4f, mark);
                Segment(new Vector2(box.x + 8f, box.y + 14.5f), new Vector2(box.x + 15.5f, box.y + 5.5f), 2.4f, mark);
            }

            GUI.Label(new Rect(rect.x + 30f, rect.y, rect.width - 30f, rect.height), text,
                Styled(fontSize, false, TextAnchor.MiddleLeft, GUI.enabled ? TextColor : Muted));
            GUI.enabled = previous;
            return clicked && enabled ? !value : value;
        }

        /// <summary>畫一條有粗細的線段（勾勾、叉叉用）。只在 Repaint 呼叫。</summary>
        public static void Segment(Vector2 from, Vector2 to, float thickness, Color color)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f)
            {
                return;
            }

            Vector2 middle = (from + to) * 0.5f;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            // 自己乘矩陣（GUIUtility.RotateAroundPivot 在 GUI.matrix 有縮放時轉的中心會跑掉）。
            GUI.matrix = previousMatrix
                         * Matrix4x4.TRS(new Vector3(middle.x, middle.y, 0f), Quaternion.Euler(0f, 0f, angle), Vector3.one)
                         * Matrix4x4.TRS(new Vector3(-middle.x, -middle.y, 0f), Quaternion.identity, Vector3.one);
            GUI.color = Gui(color);
            GUI.DrawTexture(new Rect(middle.x - length * 0.5f, middle.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        /// <summary>單選圓點＋文字。回傳這一格有沒有被選。</summary>
        public static bool Radio(Rect rect, bool selected, string text, bool enabled = true, int fontSize = 16)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            var dot = new Rect(rect.x, rect.y + (rect.height - 18f) * 0.5f, 18f, 18f);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            Box(dot, selected ? s_RadioOn : s_RadioOff);
            GUI.Label(new Rect(rect.x + 28f, rect.y, rect.width - 28f, rect.height), text,
                Styled(fontSize, false, TextAnchor.MiddleLeft, GUI.enabled ? TextColor : Muted));
            GUI.enabled = previous;
            return clicked && enabled;
        }

        /// <summary>橫向滑桿。回傳新的值。</summary>
        public static float Slider(Rect rect, float value, float min, float max)
        {
            return GUI.HorizontalSlider(rect, value, min, max, SliderTrack, SliderThumb);
        }

        /// <summary>依字級、粗細、對齊、顏色取一個文字樣式（會快取）。</summary>
        public static GUIStyle Styled(int fontSize, bool bold, TextAnchor align, Color color)
        {
            long key = ((long)fontSize << 40) ^ ((bold ? 1L : 0L) << 39) ^ ((long)align << 32) ^ (uint)color.GetHashCode();
            GUIStyle style;
            if (s_TextStyles.TryGetValue(key, out style) && style != null)
            {
                return style;
            }

            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = align,
                wordWrap = true,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            style.normal.textColor = Gui(color);
            s_TextStyles[key] = style;
            return style;
        }

        static readonly Dictionary<long, GUIStyle> s_TextStyles = new Dictionary<long, GUIStyle>();

        public static GUIStyle ButtonStyle(Tone tone, int fontSize, bool bold, TextAnchor align)
        {
            long key = ((long)tone << 40) ^ ((long)fontSize << 20) ^ ((bold ? 1L : 0L) << 10) ^ (long)align;
            GUIStyle style;
            if (s_ButtonStyles.TryGetValue(key, out style) && style != null && style.normal.background != null)
            {
                return style;
            }

            Color fill;
            Color border;
            Color text;
            float radius = 10f;
            switch (tone)
            {
                case Tone.Accent:
                    fill = Accent;
                    border = Accent;
                    text = AccentText;
                    break;
                case Tone.Blue:
                    fill = Blue;
                    border = Blue;
                    text = BlueText;
                    break;
                case Tone.Secondary:
                    fill = Card;
                    border = Line;
                    text = TextColor;
                    break;
                case Tone.Ghost:
                    fill = new Color(0f, 0f, 0f, 0f);
                    border = new Color(0f, 0f, 0f, 0f);
                    text = Muted;
                    break;
                case Tone.Light:
                    fill = Soft;
                    border = Soft;
                    text = Background;
                    break;
                case Tone.Faint:
                    fill = new Color(0f, 0f, 0f, 0f);
                    border = new Color(Line.r, Line.g, Line.b, 0.7f);
                    text = Muted;
                    break;
                case Tone.AccentOutline:
                    fill = new Color(0f, 0f, 0f, 0f);
                    border = Accent;
                    text = BotText;
                    break;
                default:
                    fill = new Color(0f, 0f, 0f, 0f);
                    border = Line;
                    text = Soft;
                    break;
            }

            style = new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = align,
                wordWrap = false,
                padding = new RectOffset(align == TextAnchor.MiddleLeft ? 22 : 6, 6, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
                border = BorderFor(radius)
            };

            Color hoverFill = fill.a > 0f ? Color.Lerp(fill, Color.white, 0.12f) : new Color(1f, 1f, 1f, 0.06f);
            Color activeFill = fill.a > 0f ? Color.Lerp(fill, Color.black, 0.12f) : new Color(1f, 1f, 1f, 0.12f);
            style.normal.background = Rounded(fill, border, radius);
            style.hover.background = Rounded(hoverFill, border, radius);
            style.active.background = Rounded(activeFill, border, radius);
            style.focused.background = style.normal.background;
            style.onNormal.background = style.normal.background;
            style.onHover.background = style.hover.background;
            style.onActive.background = style.active.background;
            Color textColor = Gui(text);
            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            style.active.textColor = textColor;
            style.focused.textColor = textColor;
            style.onNormal.textColor = textColor;
            style.onHover.textColor = textColor;
            style.onActive.textColor = textColor;
            s_ButtonStyles[key] = style;
            return style;
        }

        // ------------------------------------------------------------------
        // 建樣式、畫圓角貼圖
        // ------------------------------------------------------------------

        static void Build()
        {
            for (int i = 0; i < s_Textures.Count; i++)
            {
                if (s_Textures[i] != null)
                {
                    Object.Destroy(s_Textures[i]);
                }
            }

            s_Textures.Clear();
            s_ButtonStyles.Clear();
            s_TextStyles.Clear();
            s_Ready = true;

            H1 = Styled(72, true, TextAnchor.MiddleLeft, TextColor);
            H1Small = Styled(36, true, TextAnchor.MiddleLeft, TextColor);
            H2 = Styled(26, true, TextAnchor.MiddleLeft, TextColor);
            H3 = Styled(16, true, TextAnchor.MiddleLeft, TextColor);
            Eyebrow = Styled(15, false, TextAnchor.MiddleLeft, Muted);
            Body = Styled(16, false, TextAnchor.UpperLeft, Soft);
            BodyLarge = Styled(18, false, TextAnchor.UpperLeft, Soft);
            Label = Styled(15, false, TextAnchor.MiddleLeft, Muted);
            Small = Styled(13, false, TextAnchor.MiddleLeft, Muted);
            SmallStrong = Styled(13, true, TextAnchor.MiddleLeft, Muted);
            Code = Styled(36, true, TextAnchor.MiddleLeft, TextColor);

            s_CardStyle = BoxStyle(Card, CardBorder, 18f);
            s_DeepCardStyle = BoxStyle(DeepCard, CardBorder, 18f);
            s_InsetStyle = BoxStyle(Background, Background, 12f);
            s_SelectedStyle = new GUIStyle(BoxStyle(SelectedCard, Accent, 10f, 2f));
            s_SelectedStyle.hover.background = s_SelectedStyle.normal.background;
            s_SelectedStyle.active.background = s_SelectedStyle.normal.background;
            s_BlueFrameStyle = BoxStyle(Background, Blue, 14f);
            s_BotRowStyle = BoxStyle(Background, BotBorder, 12f);
            s_DashedRowStyle = BoxStyle(new Color(0f, 0f, 0f, 0f), new Color(Line.r, Line.g, Line.b, 0.8f), 12f);
            var panel = new Color(0.063f, 0.094f, 0.082f, 0.96f);
            s_PanelStyle = BoxStyle(panel, CardBorder, 16f);

            s_ChipBlue = new GUIStyle(BoxStyle(BlueChip, BlueChip, 10f))
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 0, 0)
            };
            s_ChipBlue.normal.textColor = Gui(BlueChipText);

            s_CheckOn = BoxStyle(Blue, Blue, 4f);
            s_CheckOnAccent = BoxStyle(Accent, Accent, 4f);
            s_CheckOff = BoxStyle(Background, Line, 4f, 1.5f);
            s_RadioOn = BoxStyle(Blue, Blue, 9f);
            s_RadioOff = BoxStyle(Background, Line, 9f, 1.5f);

            Field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 10, 2, 2),
                border = BorderFor(10f)
            };
            Field.normal.background = Rounded(Background, Line, 10f);
            Field.hover.background = Field.normal.background;
            Field.focused.background = Rounded(Background, Blue, 10f);
            Field.normal.textColor = Gui(TextColor);
            Field.hover.textColor = Gui(TextColor);
            Field.focused.textColor = Gui(TextColor);
            Field.onNormal.background = Field.normal.background;
            Field.onFocused.background = Field.focused.background;

            FieldLarge = new GUIStyle(Field) { fontSize = 20, fontStyle = FontStyle.Bold };

            SliderTrack = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 8f,
                border = BorderFor(3f),
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0)
            };
            SliderTrack.normal.background = Rounded(Line, Line, 3f);
            SliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 20f,
                fixedHeight = 20f,
                border = BorderFor(9f),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 6, 6)
            };
            SliderThumb.normal.background = Rounded(Accent, Accent, 9f);
            SliderThumb.hover.background = Rounded(Color.Lerp(Accent, Color.white, 0.15f), Accent, 9f);
            SliderThumb.active.background = SliderThumb.hover.background;
        }

        static GUIStyle BoxStyle(Color fill, Color border, float radius, float borderWidth = 1f)
        {
            var style = new GUIStyle
            {
                border = BorderFor(radius),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            style.normal.background = Rounded(fill, border, radius, borderWidth);
            return style;
        }

        static RectOffset BorderFor(float radius)
        {
            int pad = Mathf.CeilToInt(radius) + 2;
            return new RectOffset(pad, pad, pad, pad);
        }

        /// <summary>畫一張圓角方塊的小貼圖（含 1 像素的邊框、反鋸齒），給九宮格拉伸用。</summary>
        static Texture2D Rounded(Color fill, Color border, float radius, float borderWidth = 1f)
        {
            int pad = Mathf.CeilToInt(radius) + 2;
            int size = pad * 2 + 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            // 形狀的外框比貼圖小一圈（留 1 像素透明邊），角半徑是 radius。
            float inset = 1f;
            float minX = inset;
            float maxX = size - inset;
            float r = Mathf.Max(0.5f, radius);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float cx = Mathf.Clamp(px, minX + r, maxX - r);
                    float cy = Mathf.Clamp(py, minX + r, maxX - r);
                    float distance = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                    float edge = r - distance;
                    float coverage = Mathf.Clamp01(edge + 0.5f);
                    float borderMix = Mathf.Clamp01(borderWidth - edge + 0.5f);
                    Color color = Color.Lerp(fill, border, borderMix);
                    float alpha = Mathf.Lerp(fill.a, border.a, borderMix) * coverage;
                    Color linear = Gui(new Color(color.r, color.g, color.b, 1f));
                    pixels[y * size + x] = new Color(linear.r, linear.g, linear.b, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            s_Textures.Add(texture);
            return texture;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Ready = false;
            s_ButtonStyles.Clear();
            s_TextStyles.Clear();
            s_Textures.Clear();
            s_CardStyle = null;
        }
    }
}
