using AnimalEscape.Core;
using AnimalEscape.Match;
using AnimalEscape.Players;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 測試模式的 F1 開發者面板（畫面右側）。由 GameHud 在遊戲中呼叫：Update 讀 F1、OnGUI 畫面板。
    /// 「比賽」「其他」只有房主能改；連進來的人只能換自己的角色、請主機放自己出來、瞬移。
    /// 按鈕做的事都在 DevActions（測試工具的 dev 指令也走那裡）。
    /// </summary>
    public static class DevPanel
    {
        const string k_MinutesField = "dev-min";
        const string k_SecondsField = "dev-sec";
        const float k_Width = 460f;
        const float k_MessageSeconds = 4f;

        /// <summary>面板開著。</summary>
        public static bool Open { get; private set; }

        static string s_Minutes = "10";
        static string s_Seconds = "00";
        static bool s_TimeEdited;
        static string s_Message = "";
        static float s_MessageTime = -100f;
        static float s_LastChaos = -1f;

        public static void SetOpen(bool open)
        {
            open = open && DevActions.Available;
            if (open == Open)
            {
                return;
            }

            Open = open;
            s_TimeEdited = false;
            if (open)
            {
                PlayerCamera.ReleaseCursorRequested = true;
            }
            else
            {
                GUIUtility.keyboardControl = 0;
                InputGate.Typing = false;
            }
        }

        /// <summary>GameHud.Update 每一格呼叫：按 F1 開關；離開測試房間就關掉。</summary>
        public static void HandleHotkey()
        {
            if (!DevActions.Available)
            {
                if (Open)
                {
                    SetOpen(false);
                }

                return;
            }

            // Mac 的 F1 預設是調亮度（要按 fn＋F1 才是 F1），所以 ` 鍵（1 左邊那顆）也可以開關；在面板的輸入框打字時不算。
            Keyboard keyboard = Keyboard.current;
            bool pressed = keyboard != null
                && (keyboard.f1Key.wasPressedThisFrame || (keyboard.backquoteKey.wasPressedThisFrame && !InputGate.Typing));
            if (Application.isFocused && pressed)
            {
                SetOpen(!Open);
            }
        }

        static void Say(string message)
        {
            s_Message = message;
            s_MessageTime = Time.unscaledTime;
        }

        /// <summary>畫面板（在 GameHud 的 OnGUI 裡、UiKit 的縮放下）。回傳面板的範圍（滑鼠在上面時不要轉鏡頭）。</summary>
        public static Rect Draw(float width, float height)
        {
            if (!Open || !DevActions.Available)
            {
                return Rect.zero;
            }

            MenuKit.Ensure();
            HandleEnterKey();

            // 放在上方比賽條（高 70）的下面，不要蓋住剩餘時間和混亂值（改了馬上看得到）。
            var panel = new Rect(width - k_Width - 16f, 88f, k_Width, Mathf.Min(height - 104f, 662f));
            MenuKit.PanelBox(panel);
            float x = panel.x + 22f;
            float inner = panel.width - 44f;
            float y = panel.y + 18f;

            GUI.Label(new Rect(x, y, 160f, 30f), "開發者面板", MenuKit.Styled(22, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
            MenuKit.ChipBlue(new Rect(x + 128f, y + 4f, 84f, 22f), "測試模式");
            if (MenuKit.Button(new Rect(panel.xMax - 22f - 92f, y + 1f, 92f, 28f), "F1 關閉", MenuKit.Tone.Ghost, 13))
            {
                SetOpen(false);
            }

            y += 42f;
            bool host = DevActions.IsHost;
            MatchFlow flow = MatchFlow.Instance;
            MapRegistry registry = MapRegistry.Instance;

            // ---------------- 比賽（房主） ----------------
            Section(x, ref y, host ? "比賽" : "比賽（只有房主能改）");
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && host && flow != null;
            DrawTimeRow(x, ref y, flow);
            DrawChaosRow(x, inner, ref y, flow);

            float third = (inner - 12f) / 3f;
            GUI.Label(new Rect(x, y, 92f, 40f), "出口", MenuKit.Styled(15, false, TextAnchor.MiddleLeft, MenuKit.TextColor));
            float bx = x + 92f;
            float bw = (inner - 92f - 12f) / 3f;
            if (MenuKit.Button(new Rect(bx, y, bw, 40f), "直接打開", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.OpenExit());
            }

            if (MenuKit.Button(new Rect(bx + bw + 6f, y, bw, 40f), "貨車剩 10 秒", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.TruckTenSeconds());
            }

            if (MenuKit.Button(new Rect(bx + (bw + 6f) * 2f, y, bw, 40f), "東西全部復原", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.RestoreBreakables());
            }

            GUI.enabled = previousEnabled;
            y += 52f;

            // ---------------- 我的角色（每個人） ----------------
            Section(x, ref y, "我的角色（原地換，不回出生點）");
            PlayerSlot local = PlayerSlot.Local;
            Role current = local != null ? local.CurrentRole.Value : Role.None;
            float fifth = (inner - 24f) / 5f;
            for (int i = 0; i < Roles.Selectable.Length; i++)
            {
                Role role = Roles.Selectable[i];
                var rect = new Rect(x + i * (fifth + 6f), y, fifth, 40f);
                if (MenuKit.Choice(rect, GameConfig.Instance.GetRole(role).displayName, current == role, true, 14))
                {
                    Say(DevActions.SwapRoleInPlace(role));
                }
            }

            y += 46f;
            if (MenuKit.Button(new Rect(x, y, third, 40f), "打開所有收容區", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.Request(DevRequest.OpenAllHabitats));
            }

            if (MenuKit.Button(new Rect(x + third + 6f, y, third, 40f), "送我進實驗室", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.Request(DevRequest.SendMeToLab));
            }

            if (MenuKit.Button(new Rect(x + (third + 6f) * 2f, y, third, 40f), "從籠子放出來", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.Request(DevRequest.ReleaseMe));
            }

            y += 52f;

            // ---------------- 瞬移 ----------------
            Section(x, ref y, "瞬移到");
            float quarter = (inner - 18f) / 4f;
            string[] spots = { "出口", "實驗室門口", "最近的籠子", "出生點" };
            for (int i = 0; i < spots.Length; i++)
            {
                if (MenuKit.Button(new Rect(x + i * (quarter + 6f), y, quarter, 40f), spots[i], MenuKit.Tone.Secondary, 14))
                {
                    Say(DevActions.Teleport((DevActions.Spot)i));
                }
            }

            // 第二排：直接丟進某個空間裡（實驗室、貨車、研究室、收容區）。
            y += 46f;
            string[] insides = { "實驗室裡", "貨車裡", "研究室", "收容區（輪流）" };
            DevActions.Spot[] insideSpots =
            {
                DevActions.Spot.LabInside, DevActions.Spot.TruckInside, DevActions.Spot.ResearchRoom, DevActions.Spot.Habitat
            };
            for (int i = 0; i < insides.Length; i++)
            {
                if (MenuKit.Button(new Rect(x + i * (quarter + 6f), y, quarter, 40f), insides[i], MenuKit.Tone.Secondary, 14))
                {
                    Say(DevActions.Teleport(insideSpots[i]));
                }
            }

            y += 52f;

            // ---------------- 其他（房主） ----------------
            Section(x, ref y, host ? "其他" : "其他（只有房主能改）");
            GUI.enabled = previousEnabled && host;
            string nextMap = registry != null && registry.Count > 1
                ? "換地圖 → " + registry.maps[registry.Wrap(registry.ActiveIndex + 1)].displayName
                : "換地圖";
            if (MenuKit.Button(new Rect(x, y, third, 40f), nextMap, MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.NextMap());
            }

            if (MenuKit.Button(new Rect(x + third + 6f, y, third, 40f), "重新開始這一局", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.RestartRound());
            }

            if (MenuKit.Button(new Rect(x + (third + 6f) * 2f, y, third, 40f), "回到測試房間", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.ReturnToSetup());
                SetOpen(false);
            }

            GUI.enabled = previousEnabled;
            y += 50f;

            // ---------------- 結果、說明 ----------------
            string message = "";
            if (local != null && Time.unscaledTime - local.DevReplyTime < k_MessageSeconds && !string.IsNullOrEmpty(local.DevReply))
            {
                message = local.DevReply;
            }
            else if (Time.unscaledTime - s_MessageTime < k_MessageSeconds)
            {
                message = s_Message;
            }

            if (local != null && Time.unscaledTime - local.DeniedTime < k_MessageSeconds && local.DeniedRole != Role.None)
            {
                message = PlayerSlot.DescribeDenial(local.DeniedRole, local.DeniedReason);
            }

            if (!string.IsNullOrEmpty(message))
            {
                GUI.Label(new Rect(x, y, inner, 22f), "→ " + message, MenuKit.Styled(14, true, TextAnchor.MiddleLeft, MenuKit.BotText));
            }

            GUI.Label(new Rect(x, panel.yMax - 34f, inner, 20f),
                host ? "點一下畫面回到遊戲（滑鼠會鎖住，按 Esc 再放開）" : "「比賽」「其他」只有房主能改｜點一下畫面回到遊戲",
                MenuKit.Styled(12, false, TextAnchor.MiddleLeft, MenuKit.Muted));

            UpdateTypingGate();
            return panel;
        }

        static void Section(float x, ref float y, string title)
        {
            GUI.Label(new Rect(x, y, 400f, 20f), title, MenuKit.Styled(15, false, TextAnchor.MiddleLeft, MenuKit.Muted));
            y += 26f;
        }

        static void DrawTimeRow(float x, ref float y, MatchFlow flow)
        {
            bool playing = flow != null && flow.IsPlaying;
            bool focused = IsTimeFieldFocused();
            if (!focused && !s_TimeEdited && flow != null)
            {
                float seconds = playing ? flow.TimeLeft : flow.MatchSeconds.Value;
                int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
                s_Minutes = (total / 60).ToString();
                s_Seconds = (total % 60).ToString("00");
            }

            GUI.Label(new Rect(x, y, 92f, 40f), playing ? "剩餘時間" : "一局長度", MenuKit.Styled(15, false, TextAnchor.MiddleLeft, MenuKit.TextColor));
            float fx = x + 92f;
            GUI.SetNextControlName(k_MinutesField);
            string minutes = GUI.TextField(new Rect(fx, y, 56f, 40f), s_Minutes, 3, MenuKit.FieldLarge);
            GUI.Label(new Rect(fx + 60f, y, 22f, 40f), "分", MenuKit.Styled(15, false, TextAnchor.MiddleLeft, MenuKit.Muted));
            GUI.SetNextControlName(k_SecondsField);
            string seconds2 = GUI.TextField(new Rect(fx + 84f, y, 56f, 40f), s_Seconds, 2, MenuKit.FieldLarge);
            GUI.Label(new Rect(fx + 144f, y, 22f, 40f), "秒", MenuKit.Styled(15, false, TextAnchor.MiddleLeft, MenuKit.Muted));
            minutes = DigitsOnly(minutes);
            seconds2 = DigitsOnly(seconds2);
            if (minutes != s_Minutes || seconds2 != s_Seconds)
            {
                s_TimeEdited = true;
                s_Minutes = minutes;
                s_Seconds = seconds2;
            }

            float bx = fx + 172f;
            if (MenuKit.Button(new Rect(bx, y, 64f, 40f), "套用", MenuKit.Tone.Blue, 14, true))
            {
                ApplyTime();
            }

            bool paused = flow != null && flow.TimerPaused;
            bool previous = GUI.enabled;
            GUI.enabled = previous && playing;
            if (MenuKit.Button(new Rect(bx + 70f, y, 64f, 40f), paused ? "繼續" : "暫停", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.TogglePause());
            }

            GUI.enabled = previous;
            y += 44f;
            GUI.Label(new Rect(fx, y, 320f, 18f), playing
                ? (paused ? "計時暫停中｜直接改分和秒，按 Enter 或「套用」" : "直接改分和秒，按 Enter 或「套用」")
                : "還沒開始比賽：改的是一局多長", MenuKit.Styled(12, false, TextAnchor.MiddleLeft, MenuKit.Muted));
            y += 24f;
        }

        static void DrawChaosRow(float x, float inner, ref float y, MatchFlow flow)
        {
            float chaos = flow != null ? flow.Chaos.Value : 0f;
            GUI.Label(new Rect(x, y, 92f, 40f), "混亂值", MenuKit.Styled(15, false, TextAnchor.MiddleLeft, MenuKit.TextColor));
            float sliderX = x + 92f;
            float sliderWidth = inner - 92f - 132f - 52f;
            float value = MenuKit.Slider(new Rect(sliderX, y + 16f, sliderWidth, 8f), chaos, 0f, 100f);
            GUI.Label(new Rect(sliderX + sliderWidth + 8f, y, 46f, 40f), Mathf.RoundToInt(chaos) + "%",
                MenuKit.Styled(14, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
            if (GUI.enabled && Mathf.Abs(value - chaos) >= 1f && Mathf.Abs(value - s_LastChaos) >= 0.5f)
            {
                s_LastChaos = value;
                DevActions.SetChaos(Mathf.Round(value));
            }

            float bx = x + inner - 126f;
            if (MenuKit.Button(new Rect(bx, y, 60f, 40f), "+10", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.AddChaos(10f));
            }

            if (MenuKit.Button(new Rect(bx + 66f, y, 60f, 40f), "滿", MenuKit.Tone.Secondary, 14))
            {
                Say(DevActions.SetChaos(100f));
            }

            y += 52f;
        }

        static void ApplyTime()
        {
            int minutes;
            int seconds;
            int.TryParse(string.IsNullOrEmpty(s_Minutes) ? "0" : s_Minutes, out minutes);
            int.TryParse(string.IsNullOrEmpty(s_Seconds) ? "0" : s_Seconds, out seconds);
            seconds = Mathf.Clamp(seconds, 0, 59);
            Say(DevActions.SetTime(minutes * 60f + seconds));
            s_TimeEdited = false;
            GUIUtility.keyboardControl = 0;
        }

        static void HandleEnterKey()
        {
            Event current = Event.current;
            if (current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
                && IsTimeFieldFocused())
            {
                ApplyTime();
                current.Use();
            }
        }

        static bool IsTimeFieldFocused()
        {
            string name = GUI.GetNameOfFocusedControl();
            return name == k_MinutesField || name == k_SecondsField;
        }

        static void UpdateTypingGate()
        {
            if (Event.current.type == EventType.Repaint)
            {
                InputGate.Typing = IsTimeFieldFocused();
            }
        }

        static string DigitsOnly(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var builder = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                {
                    builder.Append(text[i]);
                }
            }

            return builder.ToString();
        }

        /// <summary>測試工具用：直接套用剩餘時間（和按「套用」一樣）。</summary>
        public static string ApplyTimeForTest(int minutes, int seconds)
        {
            s_Minutes = minutes.ToString();
            s_Seconds = Mathf.Clamp(seconds, 0, 59).ToString("00");
            int total = minutes * 60 + Mathf.Clamp(seconds, 0, 59);
            s_TimeEdited = false;
            return DevActions.SetTime(total);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Open = false;
            s_Minutes = "10";
            s_Seconds = "00";
            s_TimeEdited = false;
            s_Message = "";
            s_MessageTime = -100f;
            s_LastChaos = -1f;
        }
    }
}
