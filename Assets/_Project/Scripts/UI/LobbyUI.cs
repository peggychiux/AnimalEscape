using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Match;
using AnimalEscape.Networking;
using AnimalEscape.Players;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 主選單（照 claude.ai 上的設計稿「動物逃脫 主選單」，架構 A）：
    /// - 主畫面：開始遊戲／測試模式（開發版才有）／設定／操作與規則／離開遊戲，右邊是游標所在選項的說明。
    /// - 開始遊戲：建立房間（同一個 Wi-Fi 或線上、地圖、比賽時間）、加入房間（代碼、附近的房間、輸入 IP）。
    /// - 測試模式：一進來就開一個測試房間；房間裡的玩家（真人、電腦）各自的身分、規則開關、地圖、從哪個階段開始、「開始測試」。
    ///   連進測試房間的人也看到這個畫面（房主按「開始測試」才進遊戲）；遊戲中房主可以從 F1 面板回到這裡。
    /// - 設定：滑鼠靈敏度、方向鍵轉速、上下反轉、音量、全螢幕（UserSettings）。
    /// - 操作與規則：按鍵和規則的說明。
    /// 進了正式房間之後的介面在 GameHud。這個類別沿用舊的名字 LobbyUI（場景裡掛的就是它，不用重建場景）。
    /// 注意：IMGUI 在執行時要收得到滑鼠鍵盤，Player Settings 的 Active Input Handling 必須是 Both。
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        public enum MenuScreen
        {
            Main,
            Play,
            Test,
            Settings,
            Help
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static readonly bool s_GuiInputDisabled = true;
#else
        static readonly bool s_GuiInputDisabled = false;
#endif

        /// <summary>現在在哪個畫面（測試工具可以直接切）。</summary>
        public static MenuScreen CurrentScreen { get; set; } = MenuScreen.Main;

        /// <summary>主畫面上游標停在哪個選項（右邊顯示它的說明）。測試工具可以直接設。</summary>
        public static int HoveredItem { get; set; }

        static readonly string[] k_MapShort = { "環狀走道", "中央長走廊", "溪流和小島" };
        static readonly int[] k_MatchMinutes = { 5, 8, 10 };

        static LobbyUI s_Instance;

        string m_Code = "";
        string m_Address = "";
        bool m_ShowIp;
        LinkMode m_PlayLink = LinkMode.Lan;
        int m_MatchMinutes = 10;
        ConnectionState m_PreviousState = ConnectionState.Offline;
        bool m_Relinking;
        LinkMode m_PendingLink;
        bool m_PendingHost;
        string m_Notice = "";
        float m_NoticeTime = -100f;

        void Awake()
        {
            s_Instance = this;
        }

        void Start()
        {
            UserSettings.ApplyAll();
        }

        void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        // ------------------------------------------------------------------
        // 每一格：找附近的房間、測試房間換連法、Esc 回上一頁
        // ------------------------------------------------------------------

        void Update()
        {
            ConnectionManager connection = ConnectionManager.Instance;
            if (connection == null)
            {
                return;
            }

            bool connected = connection.State == ConnectionState.Connected;
            InputGate.MenuOpen = connected && MatchState.TestSetupOpen;

            // 只有在「開始遊戲」畫面才找附近的房間。
            bool search = connection.State == ConnectionState.Offline && CurrentScreen == MenuScreen.Play;
            if (search && !connection.Lan.IsSearching)
            {
                connection.Lan.StartSearching();
            }
            else if (!search && connection.Lan.IsSearching)
            {
                connection.Lan.StopSearching();
            }

            // 測試房間換連法：等上一個房間關乾淨了再開新的。
            if (m_PendingHost && connection.IsIdle)
            {
                m_PendingHost = false;
                HostTestRoom(m_PendingLink);
            }

            // 房間關了（自己離開、被斷線）：回到主畫面。換連法時不算。
            if (m_PreviousState != ConnectionState.Offline && connection.State == ConnectionState.Offline && !m_Relinking && !m_PendingHost)
            {
                if (CurrentScreen == MenuScreen.Test && !string.IsNullOrEmpty(connection.StatusMessage) && m_PreviousState == ConnectionState.Connecting)
                {
                    // 測試房間開不起來：留在測試畫面顯示原因。
                }
                else
                {
                    CurrentScreen = MenuScreen.Main;
                }
            }

            if (connection.State != ConnectionState.Offline)
            {
                m_Relinking = false;
            }

            m_PreviousState = connection.State;

            Keyboard keyboard = Keyboard.current;
            if (connection.State == ConnectionState.Offline && Application.isFocused && keyboard != null
                && keyboard.escapeKey.wasPressedThisFrame && CurrentScreen != MenuScreen.Main)
            {
                BackToMain();
            }
        }

        static void GoTo(MenuScreen screen)
        {
            CurrentScreen = screen;
            GUIUtility.keyboardControl = 0;
            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.ClearStatus();
            }
        }

        void Notice(string text)
        {
            m_Notice = text;
            m_NoticeTime = Time.unscaledTime;
        }

        // ------------------------------------------------------------------
        // 給按鈕和測試工具共用的動作
        // ------------------------------------------------------------------

        /// <summary>主畫面的「測試模式」：開一個測試房間（連法照上次選的），進測試房間畫面。</summary>
        public static void EnterTestMode()
        {
            CurrentScreen = MenuScreen.Test;
            if (s_Instance != null)
            {
                s_Instance.HostTestRoom(RoomOptions.Link);
            }
        }

        /// <summary>開測試房間（主選單的「測試模式」、換連法）。</summary>
        void HostTestRoom(LinkMode link)
        {
            ConnectionManager connection = ConnectionManager.Instance;
            if (connection == null)
            {
                return;
            }

            RoomOptions.Mode = RoomMode.Test;
            RoomOptions.Link = link;
            RoomOptions.MatchSeconds = 0f;
            if (connection.State != ConnectionState.Offline)
            {
                // 已經開著別的房間：先關掉，等關乾淨了 Update 再開。
                m_Relinking = true;
                m_PendingLink = link;
                m_PendingHost = true;
                connection.Leave();
                return;
            }

            if (!connection.IsIdle)
            {
                m_PendingLink = link;
                m_PendingHost = true;
                return;
            }

            connection.StartHost(link);
        }

        /// <summary>測試房間的連法換成 link（房主）。</summary>
        public static void ChangeTestLink(LinkMode link)
        {
            if (s_Instance == null)
            {
                return;
            }

            ConnectionManager connection = ConnectionManager.Instance;
            if (connection != null && connection.State != ConnectionState.Offline && connection.Link == link)
            {
                return;
            }

            s_Instance.HostTestRoom(link);
        }

        /// <summary>「開始遊戲」的「建立房間」：正式房間。</summary>
        public static void CreateOfficialRoom(LinkMode link, int minutes)
        {
            ConnectionManager connection = ConnectionManager.Instance;
            if (connection == null || connection.State != ConnectionState.Offline)
            {
                return;
            }

            RoomOptions.Mode = RoomMode.Official;
            RoomOptions.MatchSeconds = Mathf.Max(1, minutes) * 60f;
            RoomOptions.Link = link;
            connection.StartHost(link);
        }

        /// <summary>從測試房間或主選單回到主畫面；開著的房間一起關掉。</summary>
        public static void BackToMain()
        {
            ConnectionManager connection = ConnectionManager.Instance;
            if (s_Instance != null)
            {
                s_Instance.m_PendingHost = false;
            }

            if (connection != null && connection.State != ConnectionState.Offline)
            {
                connection.Leave();
            }

            GoTo(MenuScreen.Main);
        }

        // ------------------------------------------------------------------
        // 畫
        // ------------------------------------------------------------------

        void OnGUI()
        {
            ConnectionManager connection = ConnectionManager.Instance;
            if (connection == null)
            {
                return;
            }

            bool connected = connection.State == ConnectionState.Connected;
            if (connected && !MatchState.TestSetupOpen)
            {
                return;
            }

            float width;
            float height;
            Vector2 origin;
            MenuKit.Begin(out width, out height, out origin);
            MenuKit.Backdrop(width, height);
            InputGate.PointerOverUi = true;

            if (connected)
            {
                DrawTestRoom(connection, origin);
                return;
            }

            if (CurrentScreen == MenuScreen.Test || m_PendingHost)
            {
                DrawTestRoom(connection, origin);
                return;
            }

            switch (CurrentScreen)
            {
                case MenuScreen.Play:
                    DrawPlay(connection, origin);
                    break;
                case MenuScreen.Settings:
                    DrawSettings(origin);
                    break;
                case MenuScreen.Help:
                    DrawHelp(origin);
                    break;
                default:
                    DrawMain(connection, origin);
                    break;
            }

            if (connection.State == ConnectionState.Connecting)
            {
                DrawConnecting(connection, width, height);
            }
        }

        // ------------------------------------------------------------------
        // 主畫面
        // ------------------------------------------------------------------

        void DrawMain(ConnectionManager connection, Vector2 o)
        {
            float x = o.x + 88f;
            GUI.Label(new Rect(x, o.y + 72f, 460f, 22f), "4 對 1 非對稱逃脫", MenuKit.Eyebrow);
            GUI.Label(new Rect(x - 4f, o.y + 96f, 520f, 84f), "動物逃脫", MenuKit.H1);

            bool showTest = Debug.isDebugBuild || Application.isEditor;
            var items = new List<KeyValuePair<int, Rect>>();
            float y = o.y + 210f;
            var start = new Rect(x, y, 460f, 64f);
            items.Add(new KeyValuePair<int, Rect>(0, start));
            y += 76f;
            Rect test = Rect.zero;
            if (showTest)
            {
                test = new Rect(x, y, 460f, 56f);
                items.Add(new KeyValuePair<int, Rect>(1, test));
                y += 68f;
            }

            var settings = new Rect(x, y, 460f, 56f);
            items.Add(new KeyValuePair<int, Rect>(2, settings));
            y += 68f;
            var help = new Rect(x, y, 460f, 56f);
            items.Add(new KeyValuePair<int, Rect>(3, help));
            y += 68f;
            var quit = new Rect(x, y, 460f, 56f);
            items.Add(new KeyValuePair<int, Rect>(4, quit));

            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Value.Contains(mouse))
                {
                    HoveredItem = items[i].Key;
                }
            }

            if (MenuKit.Button(start, "▶   開始遊戲", MenuKit.Tone.Accent, 22, true, TextAnchor.MiddleLeft))
            {
                GoTo(MenuScreen.Play);
            }

            if (showTest)
            {
                if (MenuKit.Button(test, "測試模式", MenuKit.Tone.Secondary, 19, false, TextAnchor.MiddleLeft))
                {
                    EnterTestMode();
                }

                MenuKit.ChipBlue(new Rect(test.xMax - 24f - 72f, test.y + 17f, 72f, 22f), "開發用");
            }

            if (MenuKit.Button(settings, "設定", MenuKit.Tone.Secondary, 19, false, TextAnchor.MiddleLeft))
            {
                GoTo(MenuScreen.Settings);
            }

            if (MenuKit.Button(help, "操作與規則", MenuKit.Tone.Secondary, 19, false, TextAnchor.MiddleLeft))
            {
                GoTo(MenuScreen.Help);
            }

            if (MenuKit.Button(quit, "離開遊戲", MenuKit.Tone.Ghost, 19, false, TextAnchor.MiddleLeft))
            {
                QuitGame();
            }

            GUI.Label(new Rect(x, o.y + 720f - 48f - 20f, 460f, 20f), "原型｜鍵盤＋滑鼠（手把部分支援）", MenuKit.Small);

            // 右邊的說明
            var panel = new Rect(o.x + 612f, o.y + 150f, 580f, 400f);
            MenuKit.DeepBox(panel);
            float px = panel.x + 40f;
            float pw = panel.width - 80f;
            GUI.Label(new Rect(px, panel.y + 36f, pw, 20f), "游標移到選項上時，這裡顯示說明", MenuKit.Styled(14, false, TextAnchor.MiddleLeft, MenuKit.Muted));
            string title;
            string body;
            DescribeMainItem(HoveredItem, out title, out body);
            GUI.Label(new Rect(px, panel.y + 66f, pw, 40f), title, MenuKit.Styled(30, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
            GUI.Label(new Rect(px, panel.y + 120f, pw, 140f), body, MenuKit.Styled(18, false, TextAnchor.UpperLeft, MenuKit.Soft));

            if (HoveredItem == 0)
            {
                float cw = (pw - 12f) * 0.5f;
                var left = new Rect(px, panel.y + 270f, cw, 94f);
                var right = new Rect(px + cw + 12f, panel.y + 270f, cw, 94f);
                MenuKit.InsetBox(left);
                MenuKit.InsetBox(right);
                GUI.Label(new Rect(left.x + 16f, left.y + 14f, cw - 32f, 18f), "測試模式", MenuKit.Styled(13, false, TextAnchor.MiddleLeft, MenuKit.Muted));
                GUI.Label(new Rect(left.x + 16f, left.y + 36f, cw - 32f, 50f), "自由配置每個視窗的角色、規則開關和開始階段",
                    MenuKit.Styled(15, false, TextAnchor.UpperLeft, MenuKit.TextColor));
                GUI.Label(new Rect(right.x + 16f, right.y + 14f, cw - 32f, 18f), "設定", MenuKit.Styled(13, false, TextAnchor.MiddleLeft, MenuKit.Muted));
                GUI.Label(new Rect(right.x + 16f, right.y + 36f, cw - 32f, 50f), "滑鼠靈敏度、方向鍵轉速、音量、全螢幕",
                    MenuKit.Styled(15, false, TextAnchor.UpperLeft, MenuKit.TextColor));
            }

            // 上方的提示：第二個測試視窗、上一次的錯誤、輸入設定
            float noteY = o.y + 24f;
            if (connection.IsWaitingForLocalHost)
            {
                GUI.Label(new Rect(o.x + 612f, noteY, 580f, 40f), "這是測試用的第二個玩家視窗：主視窗開了房間（開始遊戲或測試模式）之後，這裡會自動加入。",
                    MenuKit.Styled(14, false, TextAnchor.UpperLeft, MenuKit.BotText));
                noteY += 44f;
            }

            if (!string.IsNullOrEmpty(connection.StatusMessage) && connection.State == ConnectionState.Offline)
            {
                GUI.Label(new Rect(o.x + 612f, noteY, 580f, 60f), connection.StatusMessage, MenuKit.Styled(14, false, TextAnchor.UpperLeft, MenuKit.Danger));
                noteY += 64f;
            }

            if (s_GuiInputDisabled)
            {
                GUI.Label(new Rect(o.x + 612f, noteY, 580f, 40f), "按鈕沒反應？請把 Edit > Project Settings > Player > Active Input Handling 改成 Both。",
                    MenuKit.Styled(14, false, TextAnchor.UpperLeft, MenuKit.Danger));
            }
        }

        static void DescribeMainItem(int item, out string title, out string body)
        {
            switch (item)
            {
                case 1:
                    title = "測試模式";
                    body = "開發用的房間：每個人的角色可以自由配、可以重複，比賽中可以原地換身分；可以從任何一個階段開始（例如直接從出口已開開始），"
                           + "也可以加電腦玩家補位。進遊戲後按 F1 打開開發者面板。";
                    return;
                case 2:
                    title = "設定";
                    body = "滑鼠靈敏度、方向鍵轉鏡頭的速度、上下視角反轉、音量、全螢幕。設定存在這台電腦上，下次打開還在。";
                    return;
                case 3:
                    title = "操作與規則";
                    body = "鍵盤怎麼按、動物和人類各要做什麼、怎麼算輸贏。第一次玩先看這裡。";
                    return;
                case 4:
                    title = "離開遊戲";
                    body = Application.isEditor ? "關掉遊戲。（在 Unity 編輯器裡是停止 Play）" : "關掉遊戲。";
                    return;
                default:
                    title = "開始遊戲";
                    body = "和朋友正式對戰。建立房間後分享代碼，或讓同一個 Wi-Fi 的人直接從清單加入。每種身分只能一位，至少 1 個人類和 1 隻動物才能開始。";
                    return;
            }
        }

        static void QuitGame()
        {
#if UNITY_EDITOR
            ConnectionManager connection = ConnectionManager.Instance;
            if (connection != null && connection.IsVirtualPlayerWindow)
            {
                return;
            }

            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------
        // 開始遊戲
        // ------------------------------------------------------------------

        void DrawPlay(ConnectionManager connection, Vector2 o)
        {
            DrawHeader(o, "開始遊戲", 64f, 40f);
            if (!string.IsNullOrEmpty(connection.StatusMessage) && connection.State == ConnectionState.Offline)
            {
                GUI.Label(new Rect(o.x + 420f, o.y + 40f, 796f, 44f), connection.StatusMessage,
                    MenuKit.Styled(14, false, TextAnchor.MiddleLeft, MenuKit.Danger));
            }

            float top = o.y + 112f;
            float cardHeight = 720f - 40f - 112f;
            float cardWidth = (1280f - 128f - 28f) * 0.5f;
            var create = new Rect(o.x + 64f, top, cardWidth, cardHeight);
            var join = new Rect(create.xMax + 28f, top, cardWidth, cardHeight);
            MenuKit.CardBox(create);
            MenuKit.CardBox(join);

            // ---------------- 建立房間 ----------------
            float x = create.x + 32f;
            float w = create.width - 64f;
            float y = create.y + 28f;
            GUI.Label(new Rect(x, y, w, 32f), "建立房間", MenuKit.H2);
            y += 50f;
            GUI.Label(new Rect(x, y, w, 20f), "連線方式", MenuKit.Label);
            y += 28f;
            float half = (w - 8f) * 0.5f;
            if (MenuKit.Choice(new Rect(x, y, half, 52f), "同一個 Wi-Fi", m_PlayLink != LinkMode.Online, false, 17))
            {
                m_PlayLink = LinkMode.Lan;
            }

            if (MenuKit.Choice(new Rect(x + half + 8f, y, half, 52f), "線上（給代碼）", m_PlayLink == LinkMode.Online, false, 17))
            {
                m_PlayLink = LinkMode.Online;
            }

            y += 58f;
            GUI.Label(new Rect(x, y, w, 18f), "線上要先連結 Unity Cloud；同一台電腦測試也選「同一個 Wi-Fi」", MenuKit.Small);
            y += 34f;

            GUI.Label(new Rect(x, y, w, 20f), "地圖", MenuKit.Label);
            y += 28f;
            MapRegistry registry = MapRegistry.Instance;
            if (registry != null && registry.Count > 0)
            {
                int count = Mathf.Min(3, registry.Count);
                float third = (w - 8f * (count - 1)) / count;
                for (int i = 0; i < count; i++)
                {
                    string sub = i < k_MapShort.Length ? k_MapShort[i] : "";
                    if (MenuKit.FramedChoice(new Rect(x + i * (third + 8f), y, third, 60f), registry.maps[i].displayName, sub, registry.SelectedIndex == i))
                    {
                        registry.Select(i);
                    }
                }

                y += 66f;
                if (registry.SelectedIndex >= 0 && registry.SelectedIndex < registry.Count)
                {
                    GUI.Label(new Rect(x, y, w, 36f), registry.maps[registry.SelectedIndex].summary, MenuKit.Styled(13, false, TextAnchor.UpperLeft, MenuKit.Dim));
                }

                y += 36f;
            }

            GUI.Label(new Rect(x, y, w, 20f), "比賽時間", MenuKit.Label);
            y += 28f;
            float timeWidth = (w - 16f) / 3f;
            for (int i = 0; i < k_MatchMinutes.Length; i++)
            {
                if (MenuKit.FramedChoice(new Rect(x + i * (timeWidth + 8f), y, timeWidth, 44f), k_MatchMinutes[i] + " 分鐘", "", m_MatchMinutes == k_MatchMinutes[i], 16))
                {
                    m_MatchMinutes = k_MatchMinutes[i];
                }
            }

            bool idle = connection.IsIdle;
            GUI.enabled = idle;
            if (MenuKit.Button(new Rect(x, create.yMax - 32f - 60f, w, 60f), "建立房間", MenuKit.Tone.Accent, 21, true))
            {
                CreateOfficialRoom(m_PlayLink, m_MatchMinutes);
            }

            GUI.enabled = true;

            // ---------------- 加入房間 ----------------
            x = join.x + 32f;
            w = join.width - 64f;
            y = join.y + 28f;
            GUI.Label(new Rect(x, y, w, 32f), "加入房間", MenuKit.H2);
            y += 50f;
            GUI.Label(new Rect(x, y, w, 20f), "輸入房間代碼（線上房間）", MenuKit.Label);
            y += 28f;
            GUI.SetNextControlName("join-code");
            m_Code = GUI.TextField(new Rect(x, y, w - 112f, 52f), m_Code, 12, MenuKit.FieldLarge).ToUpperInvariant();
            if (string.IsNullOrEmpty(m_Code) && Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != "join-code")
            {
                GUI.Label(new Rect(x + 16f, y, w - 140f, 52f), "例如 K7QX2M", MenuKit.Styled(18, false, TextAnchor.MiddleLeft, MenuKit.Dim));
            }

            GUI.enabled = idle;
            if (MenuKit.Button(new Rect(x + w - 104f, y, 104f, 52f), "加入", MenuKit.Tone.Light, 17, true))
            {
                connection.JoinOnlineRoom(m_Code);
            }

            GUI.enabled = true;
            y += 76f;

            GUI.Label(new Rect(x, y, w - 84f, 44f), "附近的房間（同一個 Wi-Fi，自動尋找）", MenuKit.Label);
            if (MenuKit.Button(new Rect(x + w - 72f, y, 72f, 44f), "重找", MenuKit.Tone.Outline, 15))
            {
                connection.Lan.Refresh();
            }

            y += 52f;
            List<LanRoomInfo> rooms = connection.Lan.Rooms;
            if (rooms.Count == 0)
            {
                var empty = new Rect(x, y, w, 64f);
                MenuKit.DashedBox(empty);
                GUI.Label(new Rect(empty.x + 16f, empty.y, empty.width - 32f, empty.height),
                    string.IsNullOrEmpty(connection.Lan.LastError) ? "還沒找到房間…（同一個 Wi-Fi 有人開了「同一個 Wi-Fi」的房間就會出現在這裡）" : connection.Lan.LastError,
                    MenuKit.Styled(14, false, TextAnchor.MiddleLeft, MenuKit.Muted));
                y += 72f;
            }
            else
            {
                for (int i = 0; i < rooms.Count && i < 3; i++)
                {
                    LanRoomInfo room = rooms[i];
                    var row = new Rect(x, y, w, 66f);
                    MenuKit.InsetBox(row);
                    GUI.Label(new Rect(row.x + 14f, row.y + 10f, w - 140f, 24f), room.Name + (room.IsTest ? "（測試）" : ""),
                        MenuKit.Styled(17, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
                    GUI.Label(new Rect(row.x + 14f, row.y + 36f, w - 140f, 20f),
                        room.Map + "｜" + room.Players + " / " + room.MaxPlayers + " 人｜" + room.Status + "｜" + room.Address,
                        MenuKit.Styled(14, false, TextAnchor.MiddleLeft, MenuKit.Muted));
                    var button = new Rect(row.xMax - 14f - 88f, row.y + 11f, 88f, 44f);
                    GUI.enabled = idle && !room.IsFull;
                    if (MenuKit.Button(button, room.IsFull ? "滿了" : "加入", room.IsFull ? MenuKit.Tone.Outline : MenuKit.Tone.Accent, 16, !room.IsFull))
                    {
                        connection.JoinLanRoom(room);
                    }

                    GUI.enabled = true;
                    y += 74f;
                }
            }

            y = Mathf.Max(y, join.yMax - 32f - 104f);
            if (MenuKit.Button(new Rect(x, y, w, 30f), (m_ShowIp ? "▼ " : "▶ ") + "找不到房間？輸入主機的 IP 位址", MenuKit.Tone.Ghost, 15, false, TextAnchor.MiddleLeft))
            {
                m_ShowIp = !m_ShowIp;
            }

            if (m_ShowIp)
            {
                y += 38f;
                GUI.SetNextControlName("join-ip");
                m_Address = GUI.TextField(new Rect(x, y, w - 112f, 48f), m_Address, 40, MenuKit.Field);
                if (string.IsNullOrEmpty(m_Address) && Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != "join-ip")
                {
                    GUI.Label(new Rect(x + 16f, y, w - 140f, 48f), "例如 192.168.1.23（主機的左上角面板有寫）", MenuKit.Styled(15, false, TextAnchor.MiddleLeft, MenuKit.Dim));
                }

                GUI.enabled = idle;
                if (MenuKit.Button(new Rect(x + w - 104f, y, 104f, 48f), "加入", MenuKit.Tone.Light, 17, true))
                {
                    connection.StartLocalClient(string.IsNullOrWhiteSpace(m_Address) ? "127.0.0.1" : m_Address);
                }

                GUI.enabled = true;
            }
        }

        // ------------------------------------------------------------------
        // 測試房間
        // ------------------------------------------------------------------

        void DrawTestRoom(ConnectionManager connection, Vector2 o)
        {
            bool connected = connection.State == ConnectionState.Connected;
            MatchState match = connected ? MatchState.Instance : null;
            bool isHost = connected && DevActions.IsHost;
            bool canEditRoom = !connected || isHost;

            // 標題
            if (MenuKit.Button(new Rect(o.x + 56f, o.y + 32f, 120f, 44f), "‹  主選單", MenuKit.Tone.Outline, 16))
            {
                BackToMain();
            }

            GUI.Label(new Rect(o.x + 196f, o.y + 30f, 200f, 48f), "測試模式", MenuKit.H1Small);
            MenuKit.ChipBlue(new Rect(o.x + 356f, o.y + 42f, 330f, 26f), "只在開發版出現，給一般玩家的版本會藏起來");

            float top = o.y + 96f;
            float bottom = o.y + 720f - 32f;
            var room = new Rect(o.x + 56f, top, 1280f - 112f - 24f - 380f, bottom - top);
            var start = new Rect(room.xMax + 24f, top, 380f, bottom - top);
            MenuKit.CardBox(room);
            MenuKit.CardBox(start);

            // ---------------- 左：測試房間 ----------------
            float x = room.x + 24f;
            float w = room.width - 48f;
            float y = room.y + 20f;
            GUI.Label(new Rect(x, y, 120f, 44f), "測試房間", MenuKit.Styled(22, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
            // 連上之後顯示主機開房的連法（連進來的人自己是用 127.0.0.1 還是 IP 連的不重要）。
            LinkMode link = connected
                ? (match != null && match.IsSpawned ? match.RoomLink.Value : connection.Link)
                : (m_PendingHost ? m_PendingLink : RoomOptions.Link);
            float linkX = x + 132f;
            float linkW = (w - 132f - 12f) / 3f;
            string[] linkNames = { "只用這台電腦", "同一個 Wi-Fi", "線上代碼" };
            GUI.enabled = canEditRoom;
            for (int i = 0; i < 3; i++)
            {
                if (MenuKit.Choice(new Rect(linkX + i * (linkW + 6f), y, linkW, 44f), linkNames[i], link == (LinkMode)i, true, 15))
                {
                    if (connected)
                    {
                        ChangeTestLink((LinkMode)i);
                    }
                    else
                    {
                        RoomOptions.Link = (LinkMode)i;
                        HostTestRoom((LinkMode)i);
                    }
                }
            }

            GUI.enabled = true;
            y += 54f;

            // 房間狀態
            if (connected && link == LinkMode.Online && !string.IsNullOrEmpty(connection.JoinCode))
            {
                var codeBox = new Rect(x, y, w, 64f);
                MenuKit.BlueFrameBox(codeBox);
                GUI.Label(new Rect(codeBox.x + 16f, codeBox.y + 10f, 120f, 20f), "● 線上房間開著", MenuKit.Styled(13, false, TextAnchor.MiddleLeft, MenuKit.Green));
                GUI.Label(new Rect(codeBox.x + 16f, codeBox.y + 32f, 120f, 20f), "房間代碼", MenuKit.Small);
                GUI.Label(new Rect(codeBox.x + 140f, codeBox.y, 220f, 64f), connection.JoinCode, MenuKit.Code);
                if (MenuKit.Button(new Rect(codeBox.x + 370f, codeBox.y + 10f, 90f, 44f), "複製", MenuKit.Tone.Blue, 15, true))
                {
                    GUIUtility.systemCopyBuffer = connection.JoinCode;
                    Notice("代碼複製好了");
                }

                GUI.Label(new Rect(codeBox.x + 476f, codeBox.y + 8f, codeBox.width - 490f, 48f), "傳給朋友：開始遊戲 › 加入房間 › 輸入代碼",
                    MenuKit.Styled(13, false, TextAnchor.MiddleLeft, MenuKit.Muted));
                y += 72f;
            }
            else
            {
                string status;
                Color color = MenuKit.Green;
                if (!connected)
                {
                    if (connection.State == ConnectionState.Connecting || m_PendingHost)
                    {
                        status = "正在開房間…";
                        color = MenuKit.Muted;
                    }
                    else
                    {
                        status = string.IsNullOrEmpty(connection.StatusMessage) ? "房間沒有開。" : connection.StatusMessage;
                        color = MenuKit.Danger;
                    }
                }
                else if (!isHost)
                {
                    status = "● 你加入了測試房間：大家各自選身分，等房主按「開始測試」";
                }
                else if (link == LinkMode.ThisComputer)
                {
                    status = "● 房間開著（只有這台電腦連得進來）：要多一個視窗，到 Multiplayer Play Mode 勾選 Player 2";
                }
                else
                {
                    status = "● 房間開著：同一個 Wi-Fi 的人在「開始遊戲 › 加入房間」就看得到"
                             + (string.IsNullOrEmpty(connection.LocalAddress) ? "" : "（這台電腦的 IP：" + connection.LocalAddress + "）")
                             + "。這台電腦要多一個視窗，到 Multiplayer Play Mode 勾選";
                }

                GUI.Label(new Rect(x, y, w, 40f), status, MenuKit.Styled(14, false, TextAnchor.UpperLeft, color));
                if (!connected && connection.State == ConnectionState.Offline && !m_PendingHost)
                {
                    if (MenuKit.Button(new Rect(x + w - 120f, y + 44f, 120f, 36f), "再試一次", MenuKit.Tone.Blue, 15, true))
                    {
                        HostTestRoom(RoomOptions.Link);
                    }
                }

                y += 48f;
            }

            // 玩家列表
            var slots = new List<PlayerSlot>();
            int bots = 0;
            if (connected)
            {
                for (int i = 0; i < PlayerSlot.All.Count; i++)
                {
                    if (PlayerSlot.All[i] != null)
                    {
                        slots.Add(PlayerSlot.All[i]);
                        if (PlayerSlot.All[i].IsBotSlot)
                        {
                            bots++;
                        }
                    }
                }

                slots.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
            }

            int people = slots.Count - bots;
            GUI.Label(new Rect(x, y, 260f, 22f), "房間裡的玩家（" + people + " 人" + (bots > 0 ? "＋電腦 " + bots + " 個" : "") + "）",
                MenuKit.Styled(16, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
            bool selfPick = !connected || match == null || match.HasRule(TestRule.SelfPick);
            GUI.Label(new Rect(x + 262f, y, w - 262f, 22f), selfPick
                    ? (isHost ? "大家各自選，你是房主，也能在這裡幫任何人改" : "選你自己這一列；房主也能幫任何人改")
                    : "只有房主能分配身分",
                MenuKit.Styled(13, false, TextAnchor.MiddleLeft, MenuKit.Muted));
            y += 30f;

            float rowHeight = slots.Count > 4 ? 46f : 50f;
            for (int i = 0; i < slots.Count; i++)
            {
                DrawPlayerRow(new Rect(x, y, w, rowHeight), slots[i], slots, link, isHost, selfPick);
                y += rowHeight + 6f;
            }

            // 加電腦玩家／等人加入
            var addRow = new Rect(x, y, w, rowHeight);
            MenuKit.DashedBox(addRow);
            int max = GameConfig.Instance.maxPlayers;
            if (connected && isHost && slots.Count < max && PlayerSpawner.Instance != null)
            {
                if (MenuKit.Button(new Rect(addRow.x + 12f, addRow.y + (rowHeight - 36f) * 0.5f, 132f, 36f), "＋ 加電腦玩家", MenuKit.Tone.AccentOutline, 14, true))
                {
                    string reason;
                    PlayerSlot bot = PlayerSpawner.Instance.AddBot(Role.None, out reason);
                    Notice(bot != null ? "加了 P" + bot.PlayerNumber + "（電腦）" : "加不了：" + reason);
                }

                GUI.Label(new Rect(addRow.x + 156f, addRow.y, addRow.width - 168f, rowHeight), "還在等人的話，空位可以先放電腦（目前只能當動物）；有人加入時電腦會自動讓位",
                    MenuKit.Styled(13, false, TextAnchor.MiddleLeft, MenuKit.Muted));
            }
            else
            {
                GUI.Label(new Rect(addRow.x + 16f, addRow.y, addRow.width - 32f, rowHeight),
                    !connected ? "房間開好之後，房間裡的人會列在這裡"
                    : slots.Count >= max ? "房間滿了（最多 " + max + " 人，電腦也算）"
                    : "等待加入…　其他人從「開始遊戲 › 加入房間」進來，會出現在這裡",
                    MenuKit.Styled(14, false, TextAnchor.MiddleLeft, MenuKit.Muted));
            }

            // 規則開關
            float rulesTop = room.yMax - 20f - 22f - 64f;
            GUI.Label(new Rect(x, rulesTop, w, 22f), "規則開關（正式遊戲裡都是關的）", MenuKit.Label);
            TestRule rules = connected && match != null ? (TestRule)match.TestRules.Value : RoomOptions.Rules;
            TestRule[] ruleOrder = { TestRule.SelfPick, TestRule.DuplicateRoles, TestRule.SwapAnytime, TestRule.NoTeamNeeded };
            float colWidth = (w - 20f) * 0.5f;
            for (int i = 0; i < ruleOrder.Length; i++)
            {
                TestRule rule = ruleOrder[i];
                var rect = new Rect(x + (i % 2) * (colWidth + 20f), rulesTop + 28f + (i / 2) * 32f, colWidth, 28f);
                bool on = (rules & rule) != 0;
                bool next = MenuKit.Check(rect, on, RoomOptions.RuleName(rule), canEditRoom, true, 14);
                if (next != on)
                {
                    if (connected && match != null)
                    {
                        match.ServerSetRule(rule, next);
                    }
                    else
                    {
                        RoomOptions.Rules = next ? (RoomOptions.Rules | rule) : (RoomOptions.Rules & ~rule);
                    }
                }
            }

            // ---------------- 右：怎麼開始 ----------------
            x = start.x + 24f;
            w = start.width - 48f;
            y = start.y + 20f;
            GUI.Label(new Rect(x, y, w, 30f), "怎麼開始", MenuKit.Styled(22, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
            y += 42f;
            GUI.Label(new Rect(x, y, w, 20f), "地圖", MenuKit.Label);
            y += 26f;
            MapRegistry registry = MapRegistry.Instance;
            if (registry != null && registry.Count > 0)
            {
                int count = Mathf.Min(3, registry.Count);
                float third = (w - 6f * (count - 1)) / count;
                int active = connected ? registry.ActiveIndex : registry.SelectedIndex;
                GUI.enabled = canEditRoom;
                for (int i = 0; i < count; i++)
                {
                    if (MenuKit.Choice(new Rect(x + i * (third + 6f), y, third, 44f), registry.maps[i].displayName, active == i, true, 15))
                    {
                        if (connected && match != null)
                        {
                            match.SetMap(i);
                        }
                        else
                        {
                            registry.Select(i);
                        }
                    }
                }

                GUI.enabled = true;
                y += 56f;
            }

            GUI.Label(new Rect(x, y, w, 20f), "從哪裡開始", MenuKit.Label);
            y += 28f;
            TestStart phase = connected && match != null ? match.StartPhase.Value : RoomOptions.StartPhase;
            for (int i = 0; i <= (int)TestStart.ExitOpen; i++)
            {
                var value = (TestStart)i;
                if (MenuKit.Radio(new Rect(x, y, w, 34f), phase == value, RoomOptions.StartPhaseName(value), canEditRoom, 15))
                {
                    if (connected && match != null)
                    {
                        match.ServerSetStartPhase(value);
                    }
                    else
                    {
                        RoomOptions.StartPhase = value;
                    }
                }

                y += 36f;
            }

            y += 8f;
            var hint = new Rect(x, y, w, 72f);
            MenuKit.InsetBox(hint);
            GUI.Label(new Rect(hint.x + 12f, hint.y + 8f, hint.width - 24f, hint.height - 16f),
                "進遊戲後按 F1 打開開發者面板：房主改時間、混亂值、出口；每個人都能換自己的角色、瞬移",
                MenuKit.Styled(14, false, TextAnchor.UpperLeft, MenuKit.Soft));

            var startButton = new Rect(x, start.yMax - 20f - 58f, w, 58f);
            if (Time.unscaledTime - m_NoticeTime < 4f && !string.IsNullOrEmpty(m_Notice))
            {
                GUI.Label(new Rect(x, startButton.y - 30f, w, 24f), m_Notice, MenuKit.Styled(14, true, TextAnchor.MiddleLeft, MenuKit.BotText));
            }

            if (connected && !isHost)
            {
                var wait = startButton;
                MenuKit.DashedBox(wait);
                GUI.Label(wait, "等房主按「開始測試」", MenuKit.Styled(18, true, TextAnchor.MiddleCenter, MenuKit.Muted));
            }
            else
            {
                GUI.enabled = connected && match != null;
                if (MenuKit.Button(startButton, "開始測試", MenuKit.Tone.Blue, 21, true))
                {
                    string reason;
                    if (!match.ServerBeginTest(out reason))
                    {
                        Notice("不能開始：" + reason);
                    }
                }

                GUI.enabled = true;
            }
        }

        void DrawPlayerRow(Rect row, PlayerSlot slot, List<PlayerSlot> slots, LinkMode link, bool isHost, bool selfPick)
        {
            bool bot = slot.IsBotSlot;
            if (bot)
            {
                MenuKit.BotRowBox(row);
            }
            else
            {
                MenuKit.InsetBox(row);
            }

            bool mine = slot == PlayerSlot.Local;
            string name = bot ? "P" + slot.PlayerNumber + " 電腦" : "P" + slot.PlayerNumber + (mine ? " 你" : "") + (slot.IsHostsOwn ? "（房主）" : "");
            string sub;
            Color subColor = MenuKit.Muted;
            if (bot)
            {
                sub = "電腦控制";
                subColor = MenuKit.BotText;
            }
            else if (slot.IsHostsOwn)
            {
                sub = isHost ? "這台電腦" : "開房間的人";
            }
            else if (mine)
            {
                sub = "這個視窗";
            }
            else
            {
                subColor = MenuKit.BlueChipText;
                sub = link == LinkMode.Online ? "從線上連進來"
                    : link == LinkMode.ThisComputer ? "這台電腦的另一個視窗" : "從 Wi-Fi 連進來";
            }

            float y = row.y;
            GUI.Label(new Rect(row.x + 12f, y + 4f, 140f, 22f), name, MenuKit.Styled(15, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
            GUI.Label(new Rect(row.x + 12f, y + row.height - 24f, 140f, 18f), sub, MenuKit.Styled(12, false, TextAnchor.MiddleLeft, subColor));

            bool canEdit = isHost || (mine && selfPick);
            Role current = slot.CurrentRole.Value;
            float bx = row.x + 152f;
            float bw = 62f;
            float bh = row.height - 12f;
            int buttons = bot ? 5 : 6;
            for (int i = 0; i < buttons; i++)
            {
                Role role = i < Roles.Selectable.Length ? Roles.Selectable[i] : Role.None;
                string label = role == Role.None ? "只看" : GameConfig.Instance.GetRole(role).displayName;
                bool selected = current == role;
                var rect = new Rect(bx + i * (bw + 6f), y + 6f, bw, bh);
                bool enabled = canEdit && !(bot && role == Role.Human);
                GUI.enabled = enabled;
                bool clicked;
                if (role == Role.None && !selected)
                {
                    clicked = MenuKit.Button(rect, label, MenuKit.Tone.Faint, 14);
                }
                else
                {
                    MenuKit.Tone tone = selected ? (bot ? MenuKit.Tone.Accent : MenuKit.Tone.Blue) : MenuKit.Tone.Outline;
                    clicked = MenuKit.Button(rect, label, tone, 14, selected);
                }

                GUI.enabled = true;
                if (clicked && !selected)
                {
                    PickRole(slot, role, isHost, mine);
                }
            }

            float tagX = bx + buttons * (bw + 6f) + 4f;
            if (current != Role.None && CountRole(slots, current) > 1)
            {
                MenuKit.ChipBlue(new Rect(tagX, y + (row.height - 22f) * 0.5f, 46f, 22f), "重複");
            }

            if (bot && isHost && PlayerSpawner.Instance != null)
            {
                if (MenuKit.Button(new Rect(row.xMax - 12f - 36f, y + (row.height - 36f) * 0.5f, 36f, 36f), "×", MenuKit.Tone.Outline, 18))
                {
                    PlayerSpawner.Instance.RemoveBot(slot);
                    Notice("移除了 P" + slot.PlayerNumber + "（電腦）");
                }
            }
        }

        void PickRole(PlayerSlot slot, Role role, bool isHost, bool mine)
        {
            if (isHost && PlayerSpawner.Instance != null)
            {
                if (!PlayerSpawner.Instance.AssignRole(slot, role))
                {
                    Notice(role != Role.None ? GameConfig.Instance.GetRole(role).displayName + "已經有人選了（可以打開「同一種身分可以重複」）" : "現在不能換");
                }

                return;
            }

            if (mine)
            {
                slot.RequestRole(role);
            }
        }

        static int CountRole(List<PlayerSlot> slots, Role role)
        {
            int count = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].CurrentRole.Value == role)
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------
        // 設定
        // ------------------------------------------------------------------

        void DrawSettings(Vector2 o)
        {
            DrawHeader(o, "設定", 64f, 40f);
            var card = new Rect(o.x + 64f, o.y + 112f, 760f, 470f);
            MenuKit.CardBox(card);
            float x = card.x + 32f;
            float y = card.y + 28f;
            float labelWidth = 220f;
            float valueWidth = 120f;
            float sliderWidth = card.width - 64f - labelWidth - valueWidth - 40f;
            float sliderX = x + labelWidth + 20f;

            GUI.Label(new Rect(x, y, 300f, 24f), "操作", MenuKit.Styled(18, true, TextAnchor.MiddleLeft, MenuKit.Muted));
            y += 36f;

            GUI.Label(new Rect(x, y, labelWidth, 56f), "滑鼠靈敏度", MenuKit.Styled(18, false, TextAnchor.MiddleLeft, MenuKit.TextColor));
            float mouse = MenuKit.Slider(new Rect(sliderX, y + 24f, sliderWidth, 8f), UserSettings.MouseSensitivity,
                UserSettings.MinMouseSensitivity, UserSettings.MaxMouseSensitivity);
            if (Mathf.RoundToInt(mouse) != UserSettings.MouseSensitivity)
            {
                UserSettings.MouseSensitivity = Mathf.RoundToInt(mouse);
            }

            GUI.Label(new Rect(sliderX + sliderWidth + 20f, y, valueWidth, 56f), UserSettings.MouseSensitivity.ToString(),
                MenuKit.Styled(17, false, TextAnchor.MiddleRight, MenuKit.Soft));
            y += 56f;

            GUI.Label(new Rect(x, y, labelWidth, 56f), "方向鍵轉鏡頭速度", MenuKit.Styled(18, false, TextAnchor.MiddleLeft, MenuKit.TextColor));
            float turn = MenuKit.Slider(new Rect(sliderX, y + 24f, sliderWidth, 8f), UserSettings.TurnSpeed, UserSettings.MinTurnSpeed, UserSettings.MaxTurnSpeed);
            int roundedTurn = Mathf.RoundToInt(turn / 10f) * 10;
            if (roundedTurn != UserSettings.TurnSpeed)
            {
                UserSettings.TurnSpeed = roundedTurn;
            }

            GUI.Label(new Rect(sliderX + sliderWidth + 20f, y, valueWidth, 56f), UserSettings.TurnSpeed + " 度／秒",
                MenuKit.Styled(17, false, TextAnchor.MiddleRight, MenuKit.Soft));
            y += 56f;

            GUI.Label(new Rect(x, y, labelWidth, 56f), "上下視角反轉", MenuKit.Styled(18, false, TextAnchor.MiddleLeft, MenuKit.TextColor));
            bool invert = MenuKit.Check(new Rect(sliderX, y + 14f, 60f, 28f), UserSettings.InvertY, "", true, false);
            if (invert != UserSettings.InvertY)
            {
                UserSettings.InvertY = invert;
            }

            GUI.Label(new Rect(sliderX + sliderWidth + 20f, y, valueWidth, 56f), UserSettings.InvertY ? "開" : "關",
                MenuKit.Styled(17, false, TextAnchor.MiddleRight, UserSettings.InvertY ? MenuKit.Soft : MenuKit.Muted));
            y += 72f;

            GUI.Label(new Rect(x, y, 300f, 24f), "聲音與畫面", MenuKit.Styled(18, true, TextAnchor.MiddleLeft, MenuKit.Muted));
            y += 36f;

            GUI.Label(new Rect(x, y, labelWidth, 56f), "音量", MenuKit.Styled(18, false, TextAnchor.MiddleLeft, MenuKit.TextColor));
            float volume = MenuKit.Slider(new Rect(sliderX, y + 24f, sliderWidth, 8f), UserSettings.Volume, 0f, 100f);
            int roundedVolume = Mathf.RoundToInt(volume / 5f) * 5;
            if (roundedVolume != UserSettings.Volume)
            {
                UserSettings.Volume = roundedVolume;
            }

            GUI.Label(new Rect(sliderX + sliderWidth + 20f, y, valueWidth, 56f), UserSettings.Volume + "%",
                MenuKit.Styled(17, false, TextAnchor.MiddleRight, MenuKit.Soft));
            y += 56f;

            GUI.Label(new Rect(x, y, labelWidth, 56f), "畫面", MenuKit.Styled(18, false, TextAnchor.MiddleLeft, MenuKit.TextColor));
            float half = (sliderWidth - 8f) * 0.5f;
            if (MenuKit.Choice(new Rect(sliderX, y + 6f, half, 44f), "視窗", !UserSettings.Fullscreen, false, 16))
            {
                UserSettings.Fullscreen = false;
            }

            if (MenuKit.Choice(new Rect(sliderX + half + 8f, y + 6f, half, 44f), "全螢幕", UserSettings.Fullscreen, false, 16))
            {
                UserSettings.Fullscreen = true;
            }

            if (Application.isEditor)
            {
                GUI.Label(new Rect(sliderX + sliderWidth + 20f, y, valueWidth, 56f), "編輯器裡沒作用",
                    MenuKit.Styled(12, false, TextAnchor.MiddleRight, MenuKit.Dim));
            }

            var buttons = new Rect(card.x, card.yMax + 20f, card.width, 52f);
            if (MenuKit.Button(new Rect(buttons.xMax - 140f - 12f - 140f, buttons.y, 140f, 52f), "恢復預設", MenuKit.Tone.Outline, 17))
            {
                UserSettings.ResetToDefaults();
            }

            if (MenuKit.Button(new Rect(buttons.xMax - 140f, buttons.y, 140f, 52f), "完成", MenuKit.Tone.Accent, 17, true))
            {
                UserSettings.Save();
                GoTo(MenuScreen.Main);
            }
        }

        // ------------------------------------------------------------------
        // 操作與規則
        // ------------------------------------------------------------------

        static readonly string[] k_Controls =
        {
            "WASD　移動（A／D 橫著走）",
            "滑鼠或 ←→　轉鏡頭；↑↓ 前後走",
            "空白鍵　跳",
            "Shift　慢走（聲音比較小）",
            "E　互動（按一下或按住，看旁邊是什麼）",
            "滑鼠左鍵　（人類）揮網",
            "Esc　放開滑鼠，才點得到介面",
            "1～4　在實驗室裡做表情"
        };

        static readonly string[] k_Animals =
        {
            "開場關在收容區：在門邊按住 E 撬鎖（烏龜從池塘出發）",
            "弄倒有橘色標記的東西，混亂值到 100% 出口解鎖",
            "到出口按住 E 開門，走進門外的貨車就算逃出",
            "被網住：連按空白鍵掙脫（人類走過來扛你時會打麻醉針，睡著就不能掙扎，醒來會自己跳下來）",
            "隊友被麻醉睡在地上：走過去碰牠一下就會醒",
            "隊友被關進籠子：在籠子旁按住 E 救人",
            "第 3 次被抓會送進實驗室；拿門禁卡在實驗室門口按住 E，可以把裡面的放出來",
            "聽人類的腳步聲判斷他在哪"
        };

        static readonly string[] k_Human =
        {
            "滑鼠左鍵揮網，網住後按 E 打麻醉針、扛起來（牠會睡 {sleep} 秒）",
            "扛到籠子旁按 E 關進去（第 1 次 120 秒、第 2 次 80 秒，第 3 次直接送實驗室）",
            "在保全控制台按住 E 5 秒：混亂值 −15%",
            "動物身上有鈴鐺，聽聲音找牠們",
            "守在籠子旁 10 秒，籠子的倒數會暫停"
        };

        void DrawHelp(Vector2 o)
        {
            DrawHeader(o, "操作與規則", 64f, 40f);
            float top = o.y + 112f;
            float height = 470f;
            float gap = 20f;
            float cardWidth = (1280f - 128f - gap * 2f) / 3f;
            DrawListCard(new Rect(o.x + 64f, top, cardWidth, height), "操作", k_Controls);
            DrawListCard(new Rect(o.x + 64f + cardWidth + gap, top, cardWidth, height), "動物（最多 4 隻）", k_Animals);
            DrawListCard(new Rect(o.x + 64f + (cardWidth + gap) * 2f, top, cardWidth, height), "人類（1 位）", k_Human);

            var result = new Rect(o.x + 64f, top + height + 20f, 1280f - 128f, 86f);
            MenuKit.DeepBox(result);
            GUI.Label(new Rect(result.x + 24f, result.y + 12f, 200f, 24f), "輸贏", MenuKit.Styled(18, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
            GUI.Label(new Rect(result.x + 24f, result.y + 40f, result.width - 48f, 40f),
                "一局 10 分鐘（開房時可以改成 5 或 8 分鐘）。結束時看貨車上有幾隻：4 隻動物完勝、3 隻動物勝、2 隻平手、0～1 隻人類勝。",
                MenuKit.Styled(16, false, TextAnchor.UpperLeft, MenuKit.Soft));
        }

        static void DrawListCard(Rect card, string title, string[] lines)
        {
            MenuKit.CardBox(card);
            GUI.Label(new Rect(card.x + 24f, card.y + 20f, card.width - 48f, 30f), title, MenuKit.Styled(22, true, TextAnchor.MiddleLeft, MenuKit.TextColor));
            float y = card.y + 64f;
            GUIStyle style = MenuKit.Styled(15, false, TextAnchor.UpperLeft, MenuKit.Soft);
            string sleep = GameConfig.Instance.tranqSleepSeconds.ToString("0.#");
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Replace("{sleep}", sleep);
                float h = style.CalcHeight(new GUIContent(line), card.width - 64f);
                GUI.Label(new Rect(card.x + 24f, y, 14f, 22f), "•", MenuKit.Styled(15, true, TextAnchor.UpperLeft, MenuKit.Accent));
                GUI.Label(new Rect(card.x + 40f, y, card.width - 64f, h + 2f), line, style);
                y += h + 12f;
            }
        }

        // ------------------------------------------------------------------
        // 共用
        // ------------------------------------------------------------------

        static void DrawHeader(Vector2 o, string title, float left, float top)
        {
            if (MenuKit.Button(new Rect(o.x + left, o.y + top, 120f, 44f), "‹  主選單", MenuKit.Tone.Outline, 16))
            {
                GoTo(MenuScreen.Main);
            }

            GUI.Label(new Rect(o.x + left + 140f, o.y + top - 2f, 400f, 48f), title, MenuKit.H1Small);
        }

        void DrawConnecting(ConnectionManager connection, float width, float height)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            }

            GUI.color = previous;
            var panel = new Rect((width - 420f) * 0.5f, (height - 170f) * 0.5f, 420f, 170f);
            MenuKit.CardBox(panel);
            string message = string.IsNullOrEmpty(connection.StatusMessage) ? "連線中…" : connection.StatusMessage;
            GUI.Label(new Rect(panel.x + 24f, panel.y + 24f, panel.width - 48f, 60f), message, MenuKit.Styled(18, true, TextAnchor.MiddleCenter, MenuKit.TextColor));
            if (MenuKit.Button(new Rect(panel.x + (panel.width - 160f) * 0.5f, panel.yMax - 24f - 48f, 160f, 48f), "取消", MenuKit.Tone.Outline, 16))
            {
                connection.Leave();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            CurrentScreen = MenuScreen.Main;
            HoveredItem = 0;
            s_Instance = null;
        }
    }
}
