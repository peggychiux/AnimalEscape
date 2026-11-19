using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Match;
using AnimalEscape.Networking;
using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 進房間之後的介面：左上角的資訊面板、選身分的畫面、其他玩家頭上的名牌，
    /// 追與抓的提示（網子冷卻、扛起、掙脫進度…）、撬鎖，捕捉籠（籠子上的倒數、右上角的狀態列表、救援進度），
    /// 以及實驗室派對（表情、大螢幕、離開遊戲）和門禁卡（撿到、刷卡進度、刷開時的公告）。
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        const float k_DeniedMessageSeconds = 4f;
        const float k_SwingMessageSeconds = 1.6f;

        static readonly Color k_BarStruggle = new Color(1f, 0.78f, 0.25f);
        static readonly Color k_BarCooldown = new Color(0.45f, 0.75f, 0.95f);
        static readonly Color k_BarLockpick = new Color(0.98f, 0.85f, 0.30f);
        static readonly Color k_BarRescue = new Color(0.45f, 0.90f, 0.55f);
        static readonly Color k_BarCage = new Color(0.95f, 0.45f, 0.40f);
        static readonly Color k_BarKeycard = new Color(0.40f, 0.90f, 0.95f);
        static readonly Color k_BarSleep = new Color(0.62f, 0.55f, 0.98f);
        const float k_LabBannerSeconds = 4f;

        const float k_EmptyCageLabelDistance = 22f;

        void Update()
        {
            DevPanel.HandleHotkey();
        }

        void OnGUI()
        {
            var connection = ConnectionManager.Instance;
            if (connection == null || connection.State != ConnectionState.Connected)
            {
                return;
            }

            // 測試房間的設定畫面開著時，整個畫面是主選單（LobbyUI）在畫。
            if (MatchState.TestSetupOpen)
            {
                return;
            }

            float width;
            float height;
            float scale = UiKit.Begin(out width, out height);

            PlayerSlot local = PlayerSlot.Local;
            MatchFlow flow = MatchFlow.Instance;
            bool playing = flow != null && flow.IsPlaying;
            bool choosing = local != null && local.CurrentRole.Value == Role.None;
            bool leftMatch = choosing && local.LeftMatch.Value;

            // 比賽中沒有身分（比賽開始時還沒選、或中途離開了）：只能看，等這一局結束。
            // 測試房間打開「比賽中隨時換身分」時例外：照樣顯示選身分。
            bool waiting = choosing && !leftMatch && playing && !MatchState.AllowsSwapDuringMatch;
            bool watching = leftMatch || waiting;

            if (!choosing || watching)
            {
                DrawCageLabels(scale);
            }

            if (!choosing)
            {
                DrawBreakMarkers(scale);
            }

            DrawNameTags(scale);
            if (!choosing && PlayerCamera.Instance != null && PlayerCamera.Instance.IsFirstPerson)
            {
                DrawCrosshair(width, height);
            }

            Rect interactive = Rect.zero;
            if (!choosing)
            {
                DrawSoundCues(width, height);
                interactive = DrawCaptureHud(connection, width, height);
                DrawStatusList(width);
                DrawLabBanner(width);
            }
            else if (watching)
            {
                // 離開了這一局（主機）或比賽中沒有身分：照樣看得到右上角的狀態和公告。
                DrawStatusList(width);
                DrawLabBanner(width);
            }

            Rect matchArea = DrawMatchBar(flow, width, choosing);
            Rect info = DrawInfoPanel(connection, local, choosing);
            Rect devPanel = DevPanel.Draw(width, height);
            Vector2 mouse = Event.current.mousePosition;
            bool pointerOverUi = info.Contains(mouse) || interactive.Contains(mouse) || matchArea.Contains(mouse) || devPanel.Contains(mouse);

            if (leftMatch)
            {
                DrawLeftMatchPanel(local, width, height);
                pointerOverUi = true;
            }
            else if (waiting)
            {
                DrawWaitPanel(width, height);
            }
            else if (choosing && !DevPanel.Open)
            {
                DrawRolePicker(local, width, height);
                pointerOverUi = true;
            }

            if (!DevPanel.Open && Event.current.type == EventType.Repaint)
            {
                InputGate.Typing = false;
            }

            InputGate.PointerOverUi = pointerOverUi;
        }

        /// <summary>第一人稱時畫面正中央的小準星，讓玩家知道自己正對著哪裡。</summary>
        static void DrawCrosshair(float width, float height)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float x = width * 0.5f;
            float y = height * 0.5f;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x - 3f, y - 3f, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        // ------------------------------------------------------------------
        // 追與抓的提示（畫面下方中央）
        // ------------------------------------------------------------------

        static string NameOf(PlayerCapture capture)
        {
            return capture != null && capture.Avatar != null
                ? "P" + capture.Avatar.PlayerNumber + " " + capture.Avatar.Settings.displayName + (capture.Avatar.IsBot ? "（電腦）" : "")
                : "？";
        }

        /// <summary>畫面下方中央的提示。回傳有按鈕的那一塊面板（滑鼠在上面時不要轉鏡頭）；沒有就是空的。</summary>
        static Rect DrawCaptureHud(ConnectionManager connection, float width, float height)
        {
            PlayerCapture mine = PlayerCapture.Local;
            if (mine == null || !mine.IsSpawned)
            {
                return Rect.zero;
            }

            if (mine.IsHuman)
            {
                DrawHumanCaptureHud(mine, width, height);
                return Rect.zero;
            }

            if (mine.IsInLab)
            {
                return DrawLabHud(connection, mine, width, height);
            }

            DrawAnimalCaptureHud(mine, width, height);
            return Rect.zero;
        }

        // ------------------------------------------------------------------
        // M4：比賽（上方中央的比賽條、結算畫面）
        // ------------------------------------------------------------------

        static readonly Color k_BarChaos = new Color(0.95f, 0.45f, 0.75f);
        static readonly Color k_BarExit = new Color(0.45f, 0.95f, 0.55f);
        static readonly Color k_BarBreak = new Color(0.98f, 0.62f, 0.30f);
        static readonly Color k_BarConsole = new Color(0.45f, 0.75f, 0.95f);

        static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        /// <summary>出口現在的狀況（一句話，所有人看到的一樣）。</summary>
        static string DescribeExit(MatchFlow flow)
        {
            if (flow.ExitOpen.Value)
            {
                return "出口開了！貨車 " + Mathf.CeilToInt(flow.TruckTimeLeft) + " 秒後開走";
            }

            if (!flow.ExitUnlocked)
            {
                PlayerCapture mine = PlayerCapture.Local;
                if (mine != null && mine.IsSpawned)
                {
                    return mine.IsHuman
                        ? "混亂值到 100% 出口就解鎖；在控制台按住 E 可以降低混亂值"
                        : "出口鎖著：弄倒有橘色標記的東西，混亂值到 100% 出口就解鎖";
                }

                return "出口鎖著：混亂值到 100% 保全系統當機，出口才會解鎖";
            }

            if (PlayerCapture.Now - flow.UnlockedAt.Value < 4.0)
            {
                return "保全系統當機！出口解鎖了";
            }

            int helpers = flow.ExitHelpers.Value;
            if (helpers > 0 || flow.ExitProgress.Value > 0f)
            {
                return "出口開門中 " + Mathf.RoundToInt(flow.ExitProgress.Value * 100f) + "%"
                    + (helpers > 0 ? "（" + helpers + " 隻在開）" : "（沒有人在開）");
            }

            return "出口解鎖了：動物到出口按住 E 開門";
        }

        /// <summary>主機現在能不能開始比賽（至少 1 個人類、1 隻動物）。</summary>
        static bool CanStartMatch(out string reason)
        {
            int humans = 0;
            int animals = 0;
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot slot = PlayerSlot.All[i];
                if (slot == null)
                {
                    continue;
                }

                Role role = slot.CurrentRole.Value;
                if (role == Role.Human)
                {
                    humans++;
                }
                else if (Roles.IsAnimal(role))
                {
                    animals++;
                }
            }

            reason = humans < 1 ? "還沒有人選人類" : (animals < 1 ? "還沒有人選動物" : "");
            if (MatchState.SkipsTeamRequirement)
            {
                reason = "";
                return true;
            }

            return humans >= 1 && animals >= 1;
        }

        /// <summary>
        /// 上方中央的比賽條：準備中（主機的「開始比賽」）、比賽中（剩下的時間、混亂值、出口）、結算（結果）。
        /// 回傳有按鈕的那一塊（滑鼠在上面時不要轉鏡頭）。
        /// </summary>
        static Rect DrawMatchBar(MatchFlow flow, float width, bool choosing)
        {
            if (flow == null || !flow.IsSpawned)
            {
                return Rect.zero;
            }

            bool isHost = IsHost();
            switch (flow.Phase.Value)
            {
                case MatchPhase.Results:
                    return choosing ? DrawResultsLine(flow, width) : DrawResultsPanel(flow, width, isHost);

                case MatchPhase.Lobby:
                {
                    var panel = new Rect((width - 560f) * 0.5f, 12f, 560f, 70f);
                    UiKit.Panel(panel);
                    if (MatchState.IsExploring)
                    {
                        // 測試房間的「逛地圖」：不會開始比賽，所以沒有「開始比賽」按鈕。
                        GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 24f, 24f), "逛地圖（不比賽、不計時，所有的門都開）", UiKit.Header);
                        GUI.Label(new Rect(panel.x + 12f, panel.y + 32f, panel.width - 24f, 22f), isHost
                            ? "按 F1 可以換角色、瞬移；要比賽就在 F1 按「回到測試房間」換開始方式"
                            : "按 F1 可以換角色、瞬移；要比賽等房主回到測試房間", UiKit.Hint);
                        UiKit.Bar(new Rect(panel.x + 12f, panel.y + 56f, panel.width - 24f, 8f), flow.Chaos.Value / 100f, k_BarChaos);
                        return panel;
                    }

                    GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 180f, 24f), "準備中（自由練習，不計時）", UiKit.Header);
                    string reason;
                    bool canStart = CanStartMatch(out reason);
                    if (isHost)
                    {
                        GUI.enabled = canStart;
                        if (GUI.Button(new Rect(panel.xMax - 160f, panel.y + 8f, 148f, 30f), "開始比賽", UiKit.Button))
                        {
                            flow.RequestStartMatch();
                        }

                        GUI.enabled = true;
                        GUI.Label(new Rect(panel.x + 12f, panel.y + 32f, panel.width - 24f, 22f), canStart
                            ? "大家選好身分後按「開始比賽」：所有人回到出生點，比賽中不能換身分"
                            : "要至少 1 個人類和 1 隻動物才能開始（" + reason + "）", UiKit.Hint);
                    }
                    else
                    {
                        GUI.Label(new Rect(panel.x + 12f, panel.y + 32f, panel.width - 24f, 22f),
                            "等主機按「開始比賽」（至少要 1 個人類和 1 隻動物）", UiKit.Hint);
                    }

                    UiKit.Bar(new Rect(panel.x + 12f, panel.y + 56f, panel.width - 24f, 8f), flow.Chaos.Value / 100f, k_BarChaos);
                    return panel;
                }

                default:
                {
                    var panel = new Rect((width - 560f) * 0.5f, 12f, 560f, 64f);
                    UiKit.Panel(panel);
                    GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, 120f, 26f), "剩 " + FormatTime(flow.TimeLeft), UiKit.Header);
                    var bar = new Rect(panel.x + 140f, panel.y + 10f, panel.width - 152f, 18f);
                    UiKit.Bar(bar, flow.Chaos.Value / 100f, k_BarChaos);
                    GUI.Label(bar, "混亂值 " + Mathf.FloorToInt(flow.Chaos.Value) + "%", UiKit.Tag);
                    GUI.Label(new Rect(panel.x + 12f, panel.y + 36f, panel.width - 24f, 22f), DescribeExit(flow), UiKit.Center);
                    return Rect.zero;
                }
            }
        }

        static string OutcomeTitle(MatchOutcome outcome)
        {
            switch (outcome)
            {
                case MatchOutcome.AnimalsPerfect:
                    return "動物完勝！";
                case MatchOutcome.AnimalsWin:
                    return "動物勝！";
                case MatchOutcome.Draw:
                    return "平手";
                case MatchOutcome.HumanWins:
                    return "人類勝！";
                default:
                    return "比賽結束";
            }
        }

        static string EndReasonText(MatchEndReason reason)
        {
            switch (reason)
            {
                case MatchEndReason.TruckLeft:
                    return "貨車開走了";
                case MatchEndReason.AllAboard:
                    return "場上的動物都上車了，貨車提早開走";
                case MatchEndReason.TimeUp:
                    return "10 分鐘到了，貨車開走";
                case MatchEndReason.AllCaught:
                    return "所有動物都被關起來了";
                case MatchEndReason.NoAnimals:
                    return "場上沒有動物了";
                default:
                    return "";
            }
        }

        static string ResultText(MatchResult result)
        {
            switch (result)
            {
                case MatchResult.Escaped:
                    return "逃出了！";
                case MatchResult.Caught:
                    return "沒逃出";
                case MatchResult.Left:
                    return "中途離開";
                default:
                    return "";
            }
        }

        /// <summary>結算畫面：結果、為什麼結束、每隻動物有沒有逃出；主機有「再來一局」。</summary>
        static Rect DrawResultsPanel(MatchFlow flow, float width, bool isHost)
        {
            var rows = new System.Collections.Generic.List<PlayerSlot>();
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                if (PlayerSlot.All[i] != null)
                {
                    rows.Add(PlayerSlot.All[i]);
                }
            }

            rows.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
            float panelHeight = 150f + rows.Count * 24f + 50f;
            var panel = new Rect((width - 500f) * 0.5f, 60f, 500f, panelHeight);
            UiKit.Panel(panel);
            GUI.Label(new Rect(panel.x, panel.y + 12f, panel.width, 40f), OutcomeTitle(flow.Outcome.Value), UiKit.Title);
            GUI.Label(new Rect(panel.x, panel.y + 56f, panel.width, 22f), EndReasonText(flow.EndReason.Value), UiKit.Center);
            GUI.Label(new Rect(panel.x, panel.y + 80f, panel.width, 22f), "貨車上 " + flow.EscapedCount.Value + " 隻（這局有 "
                + flow.AnimalCount.Value + " 隻動物）｜4 隻完勝、3 隻動物勝、2 隻平手、0～1 隻人類勝", UiKit.Hint);

            float y = panel.y + 112f;
            for (int i = 0; i < rows.Count; i++)
            {
                PlayerSlot slot = rows[i];
                Role role = slot.CurrentRole.Value;
                string name = "P" + slot.PlayerNumber + " " + (role != Role.None ? GameConfig.Instance.GetRole(role).displayName : "")
                    + (slot.IsBotSlot ? "（電腦）" : "");
                string line = role == Role.Human ? name + "　抓人的一方" : name + "　" + ResultText(slot.Result.Value);
                GUI.Label(new Rect(panel.x + 40f, y, panel.width - 80f, 22f), line, UiKit.Text);
                y += 24f;
            }

            y += 10f;
            if (isHost && MatchState.IsTestRoom)
            {
                // 測試房間：照「從哪裡開始」再來一局，或回到測試房間改設定。
                MatchState match = MatchState.Instance;
                if (GUI.Button(new Rect(panel.x + panel.width * 0.5f - 226f, y, 220f, 34f), "再來一局", UiKit.Button))
                {
                    string ignored;
                    match.ServerBeginTest(out ignored);
                }

                if (GUI.Button(new Rect(panel.x + panel.width * 0.5f + 6f, y, 220f, 34f), "回到測試房間", UiKit.Button))
                {
                    match.ServerReturnToTestSetup();
                }

                GUI.Label(new Rect(panel.x, y + 38f, panel.width, 22f),
                    "再來一局從「" + RoomOptions.StartPhaseName(match.StartPhase.Value) + "」開始", UiKit.Hint);
            }
            else if (isHost)
            {
                string reason;
                bool canStart = CanStartMatch(out reason);
                GUI.enabled = canStart;
                if (GUI.Button(new Rect(panel.x + (panel.width - 220f) * 0.5f, y, 220f, 34f), "再來一局", UiKit.Button))
                {
                    flow.RequestStartMatch();
                }

                GUI.enabled = true;
                GUI.Label(new Rect(panel.x, y + 38f, panel.width, 22f), canStart
                    ? "想換身分的人先按左上角的「換身分」" : "要至少 1 個人類和 1 隻動物（" + reason + "）", UiKit.Hint);
            }
            else
            {
                GUI.Label(new Rect(panel.x, y + 6f, panel.width, 22f), "等主機按「再來一局」｜想換身分可以先按左上角的「換身分」", UiKit.Hint);
            }

            return panel;
        }

        /// <summary>結算中、自己正在選身分時，上方只留一行結果（不擋住選身分的畫面）。</summary>
        static Rect DrawResultsLine(MatchFlow flow, float width)
        {
            var panel = new Rect((width - 560f) * 0.5f, 12f, 560f, 40f);
            UiKit.Panel(panel);
            GUI.Label(panel, OutcomeTitle(flow.Outcome.Value) + "｜貨車上 " + flow.EscapedCount.Value + " 隻｜選好身分後等主機按「再來一局」",
                UiKit.Center);
            return Rect.zero;
        }

        /// <summary>比賽中沒有身分（比賽開始時還沒選）：只能看，等這一局結束。</summary>
        static void DrawWaitPanel(float width, float height)
        {
            var panel = new Rect((width - 440f) * 0.5f, (height - 100f) * 0.5f, 440f, 100f);
            UiKit.Panel(panel);
            GUI.Label(new Rect(panel.x, panel.y + 14f, panel.width, 34f), "比賽進行中", UiKit.Title);
            GUI.Label(new Rect(panel.x, panel.y + 56f, panel.width, 24f), "這一局結束後才能選身分加入", UiKit.Center);
        }

        // ------------------------------------------------------------------
        // 實驗室派對
        // ------------------------------------------------------------------

        static Rect DrawLabHud(ConnectionManager connection, PlayerCapture mine, float width, float height)
        {
            var panel = new Rect((width - 560f) * 0.5f, height - 214f, 560f, 196f);
            UiKit.Panel(panel);
            GUI.Label(new Rect(panel.x, panel.y + 6f, panel.width, 32f), "你被送進實驗室了：派對時間！", UiKit.Title);
            GUI.Label(new Rect(panel.x, panel.y + 40f, panel.width, 22f),
                "自由走動、撞球玩｜數字鍵 1 揮手　2 跳舞　3 轉圈　4 開心跳", UiKit.Center);

            LabRoom lab = LabRoom.Current;
            PlayerParty party = PlayerParty.Local;
            string watching = "沒有隊友在場上（播全景）";
            if (lab != null && lab.ScreenTarget != null)
            {
                watching = "P" + lab.ScreenTarget.PlayerNumber + " " + lab.ScreenTarget.Settings.displayName;
            }

            bool atConsole = party != null && party.AtConsole;
            GUI.Label(new Rect(panel.x, panel.y + 64f, panel.width, 22f), (atConsole
                ? "按 E 換人看｜大螢幕正在播：" : "走到大螢幕前的控台按 E 換人看｜正在播：") + watching, UiKit.Center);

            MatchState match = MatchState.Instance;
            PlayerSlot slot = mine.Avatar != null ? mine.Avatar.Slot : null;
            string rescue;
            if (match == null || match.Keycard.Value == KeycardStatus.None)
            {
                rescue = "這張地圖沒有門禁卡";
            }
            else if (match.Keycard.Value == KeycardStatus.Used)
            {
                rescue = slot != null && slot.ReleasedFromLab.Value
                    ? "你已經被門禁卡救過一次，門禁卡也用掉了：這局出不去了"
                    : "門禁卡已經用過了：這局出不去了";
            }
            else
            {
                rescue = "等隊友拿門禁卡來刷門（" + DescribeKeycard(match) + "）";
            }

            GUI.Label(new Rect(panel.x, panel.y + 88f, panel.width, 22f), rescue, UiKit.Tag);

            // 離開遊戲：這局算沒逃出去。一般玩家回到大廳；主機只退出這一局，房間繼續開著（主機離開房間會把整個房間關掉）。
            bool isHost = IsHost();
            var button = new Rect(panel.x + (panel.width - 300f) * 0.5f, panel.y + 118f, 300f, 34f);
            if (GUI.Button(button, isHost ? "離開遊戲（房間繼續開著）" : "離開遊戲（回到大廳）", UiKit.Button))
            {
                LeaveGameFromLab();
            }

            string note = Cursor.lockState == CursorLockMode.Locked
                ? "按 Esc 放開滑鼠，才點得到按鈕｜離開的話這局算沒逃出去"
                : (isHost ? "你是主機：只會退出這一局，其他人繼續玩；要關房間用左上角的「離開房間」" : "離開的話這局算沒逃出去");
            GUI.Label(new Rect(panel.x, panel.y + 160f, panel.width, 22f), note, UiKit.Hint);
            return panel;
        }

        static bool IsHost()
        {
            var net = Unity.Netcode.NetworkManager.Singleton;
            return net != null && net.IsHost;
        }

        /// <summary>
        /// 實驗室的「離開遊戲」（按鈕和測試工具都走這裡）：一般玩家離開房間、回到大廳；
        /// 主機只退出這一局（角色收掉、房間繼續開著），因為主機一離開房間，所有人都會斷線。
        /// </summary>
        public static void LeaveGameFromLab()
        {
            PlayerSlot local = PlayerSlot.Local;
            if (IsHost())
            {
                if (local != null)
                {
                    local.RequestLeaveMatch();
                }
            }
            else if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.Leave();
            }
        }

        /// <summary>主機在實驗室按了「離開遊戲」之後：角色收掉了，人還在房間裡。</summary>
        static void DrawLeftMatchPanel(PlayerSlot local, float width, float height)
        {
            var panel = new Rect((width - 480f) * 0.5f, (height - 190f) * 0.5f, 480f, 190f);
            UiKit.Panel(panel);
            GUI.Label(new Rect(panel.x, panel.y + 12f, panel.width, 34f), "你離開了這一局", UiKit.Title);
            GUI.Label(new Rect(panel.x, panel.y + 52f, panel.width, 22f), "這局算沒逃出去。房間還開著，其他人會繼續玩。", UiKit.Center);
            GUI.Label(new Rect(panel.x, panel.y + 76f, panel.width, 22f), "要關掉房間，用左上角的「離開房間」（所有人都會回到大廳）", UiKit.Hint);
            bool playing = MatchFlow.Instance != null && MatchFlow.Instance.IsPlaying;
            var button = new Rect(panel.x + (panel.width - 240f) * 0.5f, panel.y + 112f, 240f, 34f);
            GUI.enabled = !playing;
            if (GUI.Button(button, "回到選身分", UiKit.Button))
            {
                local.RequestRole(Role.None);
            }

            GUI.enabled = true;
            GUI.Label(new Rect(panel.x, panel.y + 152f, panel.width, 22f),
                playing ? "這一局結束後才能重新選身分" : "可以重新選身分了", UiKit.Hint);
        }

        /// <summary>門禁卡現在在哪裡（給人看的一句話）。</summary>
        static string DescribeKeycard(MatchState match)
        {
            if (match == null)
            {
                return "";
            }

            switch (match.Keycard.Value)
            {
                case KeycardStatus.OnGround:
                    return "門禁卡在地上，還沒人拿";
                case KeycardStatus.Held:
                {
                    PlayerAvatar holder = match.KeycardHolderAvatar;
                    return holder != null ? "門禁卡在 P" + holder.PlayerNumber + " " + holder.Settings.displayName + " 身上" : "門禁卡被拿走了";
                }

                case KeycardStatus.Used:
                    return "門禁卡已經用過了";
                default:
                    return "這張地圖沒有門禁卡";
            }
        }

        /// <summary>實驗室被刷開時，畫面上方公告幾秒（所有人都看得到）。</summary>
        static void DrawLabBanner(float width)
        {
            MatchState match = MatchState.Instance;
            if (match == null || match.LabReleasedCount.Value <= 0)
            {
                return;
            }

            float elapsed = (float)(PlayerCapture.Now - match.LabOpenedAt.Value);
            if (elapsed < 0f || elapsed > k_LabBannerSeconds)
            {
                return;
            }

            var panel = new Rect((width - 460f) * 0.5f, 90f, 460f, 44f);
            UiKit.Panel(panel);
            GUI.Label(panel, "門禁卡刷開了實驗室！放出了 " + match.LabReleasedCount.Value + " 隻動物", UiKit.Header);
        }

        static void DrawHumanCaptureHud(PlayerCapture mine, float width, float height)
        {
            DrawConsoleHint(mine, width, height);

            var panel = new Rect((width - 420f) * 0.5f, height - 104f, 420f, 84f);
            UiKit.Panel(panel);

            var line1 = new Rect(panel.x + 10f, panel.y + 6f, panel.width - 20f, 26f);
            var line2 = new Rect(panel.x + 10f, panel.y + 32f, panel.width - 20f, 24f);
            var bar = new Rect(panel.x + 14f, panel.y + 62f, panel.width - 28f, 12f);

            PlayerCapture carried = mine.Carried;
            if (carried != null)
            {
                CaptureCage cage = mine.CageInReach;
                if (carried.IsAsleep)
                {
                    // 麻醉針：下面的條是牠還要睡多久（睡醒就會跳下來跑掉）。
                    GUI.Label(line1, "扛著 " + NameOf(carried) + "｜麻醉中，還要睡 " + carried.SleepRemaining.ToString("0.0") + " 秒", UiKit.Center);
                    GUI.Label(line2, cage != null
                        ? "按 E 關進 " + cage.number + " 號籠！"
                        : "趁牠還在睡，走到空籠子旁按 E 關進去（不在籠子旁按 E 是放下）", UiKit.Tag);
                    UiKit.Bar(bar, carried.SleepRemaining / Mathf.Max(0.1f, GameConfig.Instance.tranqSleepSeconds), k_BarSleep);
                    return;
                }

                GUI.Label(line1, "扛著 " + NameOf(carried) + "（走得比較慢，不能揮網）", UiKit.Center);
                GUI.Label(line2, cage != null
                    ? "按 E 關進 " + cage.number + " 號籠！｜下面是牠的掙脫進度"
                    : "走到空籠子旁按 E 關進去（不在籠子旁按 E 是放下）", UiKit.Tag);
                UiKit.Bar(bar, carried.Struggle.Value, k_BarStruggle);
                return;
            }

            float cooldown = mine.NetCooldownRemaining;
            if (mine.IsStaggered)
            {
                // 踉蹌只會在動物跑掉時發生：肩上掙脫、麻醉醒來跳下去、被隊友撞。
                GUI.Label(line1, "動物跑掉了！踉蹌中 " + mine.StaggerRemaining.ToString("0.0") + " 秒", UiKit.Center);
            }
            else if (cooldown > 0f)
            {
                GUI.Label(line1, "網子冷卻中 " + cooldown.ToString("0.0") + " 秒", UiKit.Center);
            }
            else if (mine.Avatar != null && mine.Avatar.InWater)
            {
                GUI.Label(line1, "涉水中，走得比較慢｜滑鼠左鍵揮網", UiKit.Center);
            }
            else
            {
                GUI.Label(line1, "網子準備好了｜滑鼠左鍵揮網", UiKit.Center);
            }

            // 第二行：能扛的時候提示扛起；不然顯示剛才那一下有沒有打中。
            PlayerCapture target = mine.FindPickupTarget();
            if (target != null && !mine.IsStaggered)
            {
                string action = target.IsAsleep
                    ? "按 E 扛起 " + NameOf(target) + "（還在睡 " + target.SleepRemaining.ToString("0") + " 秒）"
                    : (GameConfig.Instance.tranquilizeOnPickup ? "按 E 打麻醉針、扛起 " + NameOf(target) : "按 E 扛起 " + NameOf(target));
                GUI.Label(line2, action, UiKit.Tag);
            }
            else if (Time.unscaledTime - mine.LastSwingOutcomeTime < k_SwingMessageSeconds)
            {
                switch (mine.LastSwingOutcome)
                {
                    case SwingOutcome.Hit:
                        GUI.Label(line2, "網住了 " + NameOf(mine.LastSwingTarget) + "！", UiKit.Tag);
                        break;
                    case SwingOutcome.Protected:
                        GUI.Label(line2, "牠在保護中抓不到，只拖慢了一下", UiKit.Tag);
                        break;
                    case SwingOutcome.Miss:
                        GUI.Label(line2, "揮空了", UiKit.Tag);
                        break;
                }
            }

            float total = Mathf.Max(0.01f, GameConfig.Instance.netCooldown);
            UiKit.Bar(bar, 1f - Mathf.Clamp01(cooldown / total), k_BarCooldown);
        }

        /// <summary>（人類）站在保全控制台旁：可以用就提示、正在用就顯示進度、冷卻中或當機了也告訴他。</summary>
        static void DrawConsoleHint(PlayerCapture mine, float width, float height)
        {
            PlayerTask task = PlayerTask.Local;
            MatchFlow flow = MatchFlow.Instance;
            if (task == null || !task.IsSpawned || flow == null)
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            var panel = new Rect((width - 420f) * 0.5f, height - 150f, 420f, 40f);
            if (task.Kind.Value == TaskKind.Console)
            {
                UiKit.Panel(panel);
                float remaining = task.Duration * (1f - task.Progress);
                var bar = new Rect(panel.x + 12f, panel.y + 24f, panel.width - 24f, 10f);
                GUI.Label(new Rect(panel.x, panel.y + 2f, panel.width, 22f), "重開保全系統… 還要 " + remaining.ToString("0.0") + " 秒", UiKit.Tag);
                UiKit.Bar(bar, task.Progress, k_BarConsole);
                return;
            }

            SecurityConsole console = SecurityConsole.FindInReach(mine.transform.position, config.consoleRange);
            if (console == null)
            {
                return;
            }

            string text;
            if (flow.ExitUnlocked)
            {
                text = "保全系統已經當機了，控制台沒有用";
            }
            else if (console.CooldownRemaining > 0f)
            {
                text = "這台控制台冷卻中，還要 " + Mathf.CeilToInt(console.CooldownRemaining) + " 秒";
            }
            else if (mine.IsCarrying)
            {
                text = "扛著動物時不能用控制台";
            }
            else
            {
                text = "按住 E " + config.consoleSeconds.ToString("0") + " 秒重開保全系統（混亂值 −" + config.consoleChaosReduction.ToString("0") + "%）";
            }

            UiKit.Panel(panel);
            GUI.Label(panel, text, UiKit.Center);
        }

        static void DrawAnimalCaptureHud(PlayerCapture mine, float width, float height)
        {
            CaptureState state = mine.State.Value;
            if (state == CaptureState.Escaped)
            {
                MatchFlow flow = MatchFlow.Instance;
                var panel = new Rect((width - 480f) * 0.5f, height - 110f, 480f, 70f);
                UiKit.Panel(panel);
                GUI.Label(new Rect(panel.x, panel.y + 6f, panel.width, 32f), "你上了貨車！網子抓不到你", UiKit.Title);
                GUI.Label(new Rect(panel.x, panel.y + 40f, panel.width, 22f), "走下車可以回去救隊友"
                    + (flow != null && flow.ExitOpen.Value ? "｜貨車 " + Mathf.CeilToInt(flow.TruckTimeLeft) + " 秒後開走" : ""), UiKit.Center);
                return;
            }

            if (state == CaptureState.Caged)
            {
                GameConfig config = GameConfig.Instance;
                PlayerSlot slot = mine.Avatar != null ? mine.Avatar.Slot : null;
                int times = slot != null ? slot.TimesCaged.Value : 1;
                float total = Mathf.Max(1f, config.GetCageSeconds(times));
                var panel = new Rect((width - 480f) * 0.5f, height - 176f, 480f, 124f);
                UiKit.Panel(panel);
                GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 34f),
                    "關在 " + mine.CageNumber.Value + " 號籠（第 " + times + " 次）", UiKit.Title);
                string detail = mine.CagePaused.Value
                    ? "人類守在旁邊太久，倒數暫停中｜等隊友來救"
                    : "倒數歸零就送進實驗室｜等隊友來救（隊友在籠子旁按住 E）";
                GUI.Label(new Rect(panel.x, panel.y + 44f, panel.width, 24f), detail, UiKit.Center);
                if (times >= config.MaxTimesCaged)
                {
                    GUI.Label(new Rect(panel.x, panel.y + 66f, panel.width, 22f), "這是最後一次：再被抓就直接送進實驗室", UiKit.Tag);
                }

                var bar = new Rect(panel.x + 20f, panel.y + 92f, panel.width - 40f, 20f);
                UiKit.Bar(bar, mine.CageTimeLeft.Value / total, k_BarCage);
                GUI.Label(bar, Mathf.CeilToInt(mine.CageTimeLeft.Value) + " 秒", UiKit.Tag);
                return;
            }

            if (state != CaptureState.Free)
            {
                var panel = new Rect((width - 500f) * 0.5f, height - 170f, 500f, 118f);
                UiKit.Panel(panel);

                if (mine.IsAsleep)
                {
                    // 麻醉針：睡著不能掙扎，只能等醒來或等隊友。
                    string sleepTitle = state == CaptureState.Carried
                        ? "被 " + NameOf(mine.Carrier) + " 麻醉扛走了…"
                        : "你被麻醉了，睡在地上…";
                    GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 34f), sleepTitle, UiKit.Title);
                    GUI.Label(new Rect(panel.x, panel.y + 44f, panel.width, 26f), state == CaptureState.Carried
                        ? "睡著不能掙扎｜醒來就會跳下來｜隊友全速撞人類可以把你撞下來"
                        : "睡著不能掙扎｜醒來就能跑｜隊友碰你一下就會醒", UiKit.Center);
                    var sleepBar = new Rect(panel.x + 20f, panel.y + 80f, panel.width - 40f, 22f);
                    UiKit.Bar(sleepBar, mine.SleepRemaining / Mathf.Max(0.1f, GameConfig.Instance.tranqSleepSeconds), k_BarSleep);
                    GUI.Label(sleepBar, "還要睡 " + mine.SleepRemaining.ToString("0.0") + " 秒", UiKit.Tag);
                    return;
                }

                string title = state == CaptureState.Carried
                    ? "被 " + NameOf(mine.Carrier) + " 扛走了！"
                    : "被網住了！";
                GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 34f), title, UiKit.Title);
                GUI.Label(new Rect(panel.x, panel.y + 44f, panel.width, 26f),
                    state == CaptureState.Netted && GameConfig.Instance.tranquilizeOnPickup
                        ? "連按 空白鍵 掙脫｜停下來會往回掉｜人類扛你時會打麻醉針" : "連按 空白鍵 掙脫｜停下來會往回掉", UiKit.Center);
                UiKit.Bar(new Rect(panel.x + 20f, panel.y + 80f, panel.width - 40f, 22f), mine.Struggle.Value, k_BarStruggle);
                return;
            }

            DrawSleepingTeammateHint(mine, width, height);

            // 救援：正在救就顯示進度條；站在關著隊友的籠子旁就提示可以救。（比撬鎖優先）
            PlayerRescue rescue = PlayerRescue.Local;
            if (rescue != null && rescue.IsSpawned)
            {
                CaptureCage target = rescue.IsRescuing ? CaptureCage.Find(rescue.TargetCage.Value) : rescue.CageInReach;
                PlayerCapture occupant = target != null ? target.Occupant : null;
                if (rescue.IsRescuing)
                {
                    var panel = new Rect((width - 460f) * 0.5f, height - 150f, 460f, 98f);
                    UiKit.Panel(panel);
                    float remaining = rescue.Duration * (1f - rescue.Progress);
                    GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 30f), "救 " + NameOf(occupant) + "… 還要 " + remaining.ToString("0.0") + " 秒", UiKit.Title);
                    GUI.Label(new Rect(panel.x, panel.y + 40f, panel.width, 22f), "按住 E 不要放，也不要走開", UiKit.Tag);
                    UiKit.Bar(new Rect(panel.x + 20f, panel.y + 68f, panel.width - 40f, 18f), rescue.Progress, k_BarRescue);
                    return;
                }

                if (target != null)
                {
                    var panel = new Rect((width - 460f) * 0.5f, height - 84f, 460f, 40f);
                    UiKit.Panel(panel);
                    GUI.Label(panel, "按住 E 把 " + NameOf(occupant) + " 救出來（要 " + rescue.Duration.ToString("0.#") + " 秒）", UiKit.Center);
                    return;
                }
            }

            // 門禁卡：正在刷就顯示進度條；拿著卡站在實驗室門口就提示可以刷。（比撬鎖優先）
            PlayerKeycard keycard = PlayerKeycard.Local;
            if (keycard != null && keycard.IsSpawned && keycard.HoldsKeycard)
            {
                if (keycard.IsSwiping)
                {
                    var panel = new Rect((width - 460f) * 0.5f, height - 150f, 460f, 98f);
                    UiKit.Panel(panel);
                    float remaining = keycard.Duration * (1f - keycard.Progress);
                    GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 30f), "刷門禁卡… 還要 " + remaining.ToString("0.0") + " 秒", UiKit.Title);
                    GUI.Label(new Rect(panel.x, panel.y + 40f, panel.width, 22f), "按住 E 不要放，也不要走開", UiKit.Tag);
                    UiKit.Bar(new Rect(panel.x + 20f, panel.y + 68f, panel.width - 40f, 18f), keycard.Progress, k_BarKeycard);
                    return;
                }

                bool atDoor = keycard.DoorInReach != null;
                var hint = new Rect((width - 520f) * 0.5f, height - 104f, 520f, 60f);
                UiKit.Panel(hint);
                string text;
                if (!atDoor)
                {
                    text = "你拿著門禁卡！到實驗室門口按住 E " + keycard.Duration.ToString("0") + " 秒，把裡面的隊友放出來";
                }
                else if (PlayerKeycard.LabHasAnimals())
                {
                    text = "按住 E 刷門禁卡（要 " + keycard.Duration.ToString("0") + " 秒，整場只能刷一次）";
                }
                else
                {
                    text = "實驗室裡現在沒有隊友，先留著卡（整場只能刷一次）";
                }

                GUI.Label(new Rect(hint.x, hint.y + 6f, hint.width, 26f), text, UiKit.Center);
                GUI.Label(new Rect(hint.x, hint.y + 34f, hint.width, 20f), "按 Q 把門禁卡丟在地上，隊友走過去就能撿", UiKit.Tag);
                return;
            }

            // 開出口（M4）：正在開就顯示全隊的進度；站在解鎖的出口前就提示可以開。（比撬鎖優先）
            PlayerTask task = PlayerTask.Local;
            int taskTarget = -1;
            TaskKind candidate = task != null && task.IsSpawned ? task.FindCandidate(out taskTarget) : TaskKind.None;
            if (task != null && task.IsSpawned && (task.Kind.Value == TaskKind.Exit || candidate == TaskKind.Exit))
            {
                MatchFlow flow = MatchFlow.Instance;
                if (task.Kind.Value == TaskKind.Exit && flow != null)
                {
                    var panel = new Rect((width - 460f) * 0.5f, height - 150f, 460f, 98f);
                    UiKit.Panel(panel);
                    GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 30f), "開門中… " + Mathf.RoundToInt(flow.ExitProgress.Value * 100f) + "%"
                        + "（" + flow.ExitHelpers.Value + " 隻一起開）", UiKit.Title);
                    GUI.Label(new Rect(panel.x, panel.y + 40f, panel.width, 22f), "按住 E 不要放；越多隻一起按越快，放手進度會留著", UiKit.Tag);
                    UiKit.Bar(new Rect(panel.x + 20f, panel.y + 68f, panel.width - 40f, 18f), flow.ExitProgress.Value, k_BarExit);
                    return;
                }

                var hint = new Rect((width - 460f) * 0.5f, height - 84f, 460f, 40f);
                UiKit.Panel(hint);
                GUI.Label(hint, "按住 E 開出口（越多隻一起按越快）", UiKit.Center);
                return;
            }

            // 撬鎖：正在撬就顯示進度條；站在鎖著的門邊就提示可以撬。
            PlayerLockpick lockpick = PlayerLockpick.Local;
            if (lockpick != null && lockpick.IsSpawned)
            {
                if (lockpick.IsPicking)
                {
                    var panel = new Rect((width - 460f) * 0.5f, height - 150f, 460f, 98f);
                    UiKit.Panel(panel);
                    float remaining = lockpick.Duration * (1f - lockpick.Progress);
                    GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 30f), "撬鎖中… 還要 " + remaining.ToString("0.0") + " 秒", UiKit.Title);
                    GUI.Label(new Rect(panel.x, panel.y + 40f, panel.width, 22f), "按住 E 不要放，也不要走開", UiKit.Tag);
                    UiKit.Bar(new Rect(panel.x + 20f, panel.y + 68f, panel.width - 40f, 18f), lockpick.Progress, k_BarLockpick);
                    return;
                }

                HabitatGate gate = lockpick.GateInReach;
                if (gate != null)
                {
                    var panel = new Rect((width - 460f) * 0.5f, height - 84f, 460f, 40f);
                    UiKit.Panel(panel);
                    GUI.Label(panel, "按住 E 撬鎖（要 " + lockpick.Duration.ToString("0") + " 秒）", UiKit.Center);
                    return;
                }
            }

            // 破壞（M4）：正在推就顯示進度條；站在東西旁邊就提示。
            if (task != null && task.IsSpawned && (task.Kind.Value == TaskKind.Break || candidate == TaskKind.Break))
            {
                GameConfig config = GameConfig.Instance;
                int index = task.Kind.Value == TaskKind.Break ? task.Target.Value : taskTarget;
                Breakable item = Breakable.Find(index);
                if (item != null)
                {
                    string name = string.IsNullOrEmpty(item.displayName) ? "東西" : item.displayName;
                    string chaos = "混亂值 +" + config.GetChaos(item.size).ToString("0") + "%";
                    if (task.Kind.Value == TaskKind.Break)
                    {
                        var panel = new Rect((width - 460f) * 0.5f, height - 150f, 460f, 98f);
                        UiKit.Panel(panel);
                        float remaining = task.Duration * (1f - task.Progress);
                        GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 30f), "推倒" + name + "… 還要 " + remaining.ToString("0.0") + " 秒", UiKit.Title);
                        GUI.Label(new Rect(panel.x, panel.y + 40f, panel.width, 22f), "按住 E 不要放｜" + chaos + "，人類會聽到", UiKit.Tag);
                        UiKit.Bar(new Rect(panel.x + 20f, panel.y + 68f, panel.width - 40f, 18f), task.Progress, k_BarBreak);
                        return;
                    }

                    float seconds = config.GetBreakSeconds(item.size);
                    var hint = new Rect((width - 460f) * 0.5f, height - 84f, 460f, 40f);
                    UiKit.Panel(hint);
                    GUI.Label(hint, (seconds <= 0f ? "按 E 弄倒" + name : "按住 E " + seconds.ToString("0.#") + " 秒推倒" + name)
                        + "（" + chaos + "）", UiKit.Center);
                    return;
                }
            }

            string message = null;
            if (mine.IsSlowed)
            {
                message = "被網子擦到了，變慢一下！";
            }
            else if (mine.IsProtected)
            {
                message = "保護中：" + mine.ProtectionRemaining.ToString("0.0") + " 秒內網子抓不到你，快跑！";
            }
            else if (IsLockedInHabitat(mine))
            {
                message = "你被關在收容區裡！走到黃色的門邊，按住 E 撬鎖";
            }
            else if (IsTeammateBeingCarried(mine))
            {
                message = "隊友被扛走了！全速撞上人類可以把牠救下來";
            }
            else if (mine.Avatar != null && mine.Avatar.InWater)
            {
                message = GameConfig.Instance.CanEnterWater(mine.Avatar.AvatarRole.Value)
                    ? "在水裡游得比走路快；人類下水會變慢"
                    : "掉進水裡了！快爬上岸";
            }

            if (message != null)
            {
                var panel = new Rect((width - 460f) * 0.5f, height - 84f, 460f, 40f);
                UiKit.Panel(panel);
                GUI.Label(panel, message, UiKit.Center);
            }
        }

        /// <summary>這隻動物是不是還關在自己的收容區裡（門還沒被撬開）。</summary>
        /// <summary>
        /// （自由的動物）8 公尺內有被麻醉、睡在地上的隊友：提示走過去碰一下就能叫醒（主機用距離判斷，碰到就醒）。
        /// 畫在其他提示的上面，不會擋住救人、撬鎖這些提示。隊友之間的狀態本來就互相知道，所以不做遮蔽判斷。
        /// </summary>
        static void DrawSleepingTeammateHint(PlayerCapture mine, float width, float height)
        {
            PlayerCapture nearest = null;
            float nearestDistance = 8f;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                PlayerCapture other = avatar != null && avatar.IsSpawned ? avatar.Capture : null;
                if (other == null || other == mine || other.State.Value != CaptureState.Netted || !other.IsAsleep)
                {
                    continue;
                }

                float distance = Vector3.Distance(avatar.transform.position, mine.transform.position);
                if (distance < nearestDistance)
                {
                    nearest = other;
                    nearestDistance = distance;
                }
            }

            if (nearest == null)
            {
                return;
            }

            var panel = new Rect((width - 460f) * 0.5f, height - 236f, 460f, 40f);
            UiKit.Panel(panel);
            GUI.Label(panel, NameOf(nearest) + " 被麻醉睡著了：走過去碰牠一下就會醒（還要睡 "
                + Mathf.CeilToInt(nearest.SleepRemaining) + " 秒）", UiKit.Center);
        }

        static bool IsLockedInHabitat(PlayerCapture mine)
        {
            if (mine.Avatar == null)
            {
                return false;
            }

            HabitatGate gate = HabitatGate.Find(mine.Avatar.AvatarRole.Value);
            return gate != null && !gate.IsOpen && gate.Contains(mine.transform.position);
        }

        static bool IsTeammateBeingCarried(PlayerCapture mine)
        {
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar != null && avatar.Capture != null && avatar.Capture != mine
                    && avatar.Capture.State.Value == CaptureState.Carried)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // 左上角資訊面板
        // ------------------------------------------------------------------

        Rect DrawInfoPanel(ConnectionManager connection, PlayerSlot local, bool choosing)
        {
            bool hasCode = !string.IsNullOrEmpty(connection.JoinCode);
            int playerCount = PlayerSlot.All.Count;
            var net = Unity.Netcode.NetworkManager.Singleton;
            bool isHost = net != null && net.IsHost;
            MapRegistry registry = MapRegistry.Instance;
            MatchState match = MatchState.Instance;
            bool hasMap = registry != null && registry.Active != null;
            bool playing = MatchFlow.Instance != null && MatchFlow.Instance.IsPlaying;
            bool canChangeMap = isHost && match != null && registry != null && registry.Count > 1 && !playing;

            bool isAnimal = !choosing && local != null && Roles.IsAnimal(local.CurrentRole.Value);
            // 高度照實際的行數算：操作說明（方向鍵轉鏡頭時兩行）、選了身分之後的兩行說明＋換身分按鈕、動物另外四行。
            float helpLines = GameConfig.Instance.arrowKeyTurning ? 2f : 1f;
            // 電腦動物的臨時按鈕（主機、比賽外才有；之後搬進主選單的測試模式）。
            bool canManageBots = isHost && !playing && PlayerSpawner.Instance != null;
            int botCount = PlayerSpawner.BotCount;
            bool showBotMessage = Time.unscaledTime - s_BotMessageTime < 4f && !string.IsNullOrEmpty(s_BotMessage);
            bool testRoom = MatchState.IsTestRoom;
            bool hasAddress = !connection.IsOnlineRoom && isHost && !string.IsNullOrEmpty(connection.LocalAddress);
            float panelHeight = 168f + helpLines * 22f + playerCount * 22f + (hasCode ? 34f : 0f) + (choosing ? 0f : 116f) + (isAnimal ? 88f : 0f)
                + (hasMap ? 24f : 0f) + (canChangeMap ? 36f : 0f) + (canManageBots ? 36f : 0f) + (showBotMessage ? 22f : 0f)
                + (testRoom ? 30f : 0f) + (hasAddress ? 22f : 0f);
            var panel = new Rect(12f, 12f, 300f, panelHeight);

            UiKit.Panel(panel);
            GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, panel.height - 20f));

            // 用主機開房時的連法（連進來的人自己是怎麼連的不重要）。
            LinkMode link = match != null && match.IsSpawned ? match.RoomLink.Value : connection.Link;
            string roomType = link == LinkMode.Online ? "線上房間" : (link == LinkMode.ThisComputer ? "本機房間" : "Wi-Fi 房間");
            GUILayout.Label((testRoom ? "測試｜" : "") + roomType + "｜" + (isHost ? "你是主機" : "你是玩家"), UiKit.Header);
            if (testRoom)
            {
                // Mac 的 F1 預設是調亮度（要按 fn＋F1），所以也給一顆按鈕和 ` 鍵（2026-11-18 使用者按 F1 沒反應）。
                if (GUILayout.Button(DevPanel.Open ? "關掉開發者面板（F1 或 ` 鍵）" : "打開開發者面板（F1 或 ` 鍵）", UiKit.Button, GUILayout.Height(26f)))
                {
                    DevPanel.SetOpen(!DevPanel.Open);
                }
            }

            if (hasAddress)
            {
                GUILayout.Label("這台電腦的 IP：" + connection.LocalAddress, UiKit.Hint);
            }

            if (hasMap)
            {
                GUILayout.Label("地圖：" + registry.Active.displayName, UiKit.Text);
            }

            if (hasCode)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("房間代碼：" + connection.JoinCode, UiKit.Text);
                if (GUILayout.Button("複製", UiKit.Button, GUILayout.Width(64f), GUILayout.Height(28f)))
                {
                    GUIUtility.systemCopyBuffer = connection.JoinCode;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Label("玩家 " + playerCount + " / " + GameConfig.Instance.maxPlayers, UiKit.Text);
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot slot = PlayerSlot.All[i];
                if (slot == null)
                {
                    continue;
                }

                Role role = slot.CurrentRole.Value;
                string roleName = role != Role.None
                    ? GameConfig.Instance.GetRole(role).displayName
                    : (slot.LeftMatch.Value ? "離開了這一局" : "選擇中…");
                string line = "P" + slot.PlayerNumber + "　" + roleName + (slot == local ? "（你）" : "") + (slot.IsBotSlot ? "（電腦）" : "");
                GUILayout.Label(line, UiKit.Hint);
            }

            GUILayout.Space(4f);
            if (GameConfig.Instance.arrowKeyTurning)
            {
                GUILayout.Label("WASD 移動　滑鼠或 ←→ 轉鏡頭", UiKit.Hint);
                GUILayout.Label("空白鍵跳　Shift 慢走", UiKit.Hint);
            }
            else
            {
                GUILayout.Label("WASD 移動　空白鍵跳　Shift 慢走", UiKit.Hint);
            }
            if (!choosing && local != null)
            {
                GUILayout.Label(local.CurrentRole.Value == Role.Human
                    ? "滑鼠左鍵揮網　E 扛起／放下／關進籠子"
                    : "被網住：連按空白鍵掙脫，停下來會往回掉", UiKit.Hint);
                GUILayout.Label(local.CurrentRole.Value == Role.Human
                    ? "在保全控制台按住 E：混亂值 −15%"
                    : "在東西旁邊按 E 弄倒（大的要按住）", UiKit.Hint);
                if (isAnimal)
                {
                    GUILayout.Label("在鎖住的門邊：按住 E 撬鎖", UiKit.Hint);
                    GUILayout.Label("在關著隊友的籠子旁：按住 E 救人", UiKit.Hint);
                    GUILayout.Label("拿著門禁卡在實驗室門口：按住 E 刷卡", UiKit.Hint);
                    GUILayout.Label("混亂值滿了：在出口按住 E 開門，走進貨車", UiKit.Hint);
                }
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            GUILayout.Label(locked ? "按 Esc 放開滑鼠（才能點這裡的按鈕）" : "點一下畫面，回到遊戲操作", UiKit.Hint);

            GUILayout.FlexibleSpace();
            if (!choosing && local != null)
            {
                // 被網住、被扛著、關在籠子裡的時候不能換身分（主機也會擋），不然換一下就逃掉了。在實驗室裡可以。
                PlayerCapture mine = PlayerCapture.Local;
                CaptureState state = mine != null ? mine.State.Value : CaptureState.Free;
                bool captured = state != CaptureState.Free && state != CaptureState.InLab && state != CaptureState.Escaped;
                bool lockedByMatch = playing && !MatchState.AllowsSwapDuringMatch;
                bool hostAssigns = MatchState.OnlyHostAssignsRoles && !isHost;
                GUI.enabled = !captured && !lockedByMatch && !hostAssigns;
                string caption = hostAssigns ? "換身分（房主分配）"
                    : (lockedByMatch ? "換身分（比賽中不能換）" : (captured ? "換身分（被抓住時不能換）" : "換身分"));
                if (GUILayout.Button(caption, UiKit.Button, GUILayout.Height(30f)))
                {
                    local.RequestRole(Role.None);
                }

                GUI.enabled = true;
            }

            if (canChangeMap)
            {
                // 只有主機可以換；換的時候所有人會回到選身分。
                int next = registry.Wrap(registry.ActiveIndex + 1);
                if (GUILayout.Button("換地圖 → " + registry.maps[next].displayName, UiKit.Button, GUILayout.Height(30f)))
                {
                    match.SetMap(next);
                }
            }

            if (canManageBots)
            {
                // 臨時按鈕：加一隻電腦動物（挑第一個還沒人用的動物）、移除最後加的那一隻。
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("＋ 加電腦動物", UiKit.Button, GUILayout.Height(30f)))
                {
                    string reason;
                    PlayerSlot bot = PlayerSpawner.Instance.AddBot(Role.None, out reason);
                    s_BotMessage = bot != null ? "加了 P" + bot.PlayerNumber + "（電腦）" : "加不了：" + reason;
                    s_BotMessageTime = Time.unscaledTime;
                }

                GUI.enabled = botCount > 0;
                if (GUILayout.Button("－ 移除", UiKit.Button, GUILayout.Width(80f), GUILayout.Height(30f)))
                {
                    PlayerSpawner.Instance.RemoveLastBot();
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (showBotMessage)
                {
                    GUILayout.Label(s_BotMessage, UiKit.Hint);
                }
            }

            if (GUILayout.Button(isHost ? "關掉房間，回主選單" : "離開房間，回主選單", UiKit.Button, GUILayout.Height(30f)))
            {
                connection.Leave();
            }

            GUILayout.EndArea();
            return panel;
        }

        static string s_BotMessage = "";
        static float s_BotMessageTime = -100f;

        // ------------------------------------------------------------------
        // 選身分
        // ------------------------------------------------------------------

        void DrawRolePicker(PlayerSlot local, float width, float height)
        {
            var panel = new Rect((width - 440f) * 0.5f, (height - 400f) * 0.5f, 440f, 400f);
            UiKit.Panel(panel);
            GUILayout.BeginArea(new Rect(panel.x + 22f, panel.y + 16f, panel.width - 44f, panel.height - 32f));

            GUILayout.Label("選擇身分", UiKit.Title);
            GUILayout.Space(4f);

            bool hostAssigns = MatchState.OnlyHostAssignsRoles && !IsHost();
            if (Time.unscaledTime - local.DeniedTime < k_DeniedMessageSeconds)
            {
                GUILayout.Label(PlayerSlot.DescribeDenial(local.DeniedRole, local.DeniedReason), UiKit.Warning);
            }
            else if (hostAssigns)
            {
                GUILayout.Label("這個測試房間由房主分配身分，請等房主幫你選。", UiKit.Warning);
            }
            else if (MatchState.AllowsDuplicateRoles)
            {
                GUILayout.Label("測試房間：同一種身分可以重複。人類從研究室出發；動物開場關在自己的收容區。", UiKit.Hint);
            }
            else
            {
                GUILayout.Label("每種身分只能有一位。人類從研究室出發；動物開場關在自己的收容區。", UiKit.Hint);
            }

            GUILayout.Space(6f);

            for (int i = 0; i < Roles.Selectable.Length; i++)
            {
                Role role = Roles.Selectable[i];
                RoleSettings settings = GameConfig.Instance.GetRole(role);
                bool unavailable = hostAssigns || (!MatchState.AllowsDuplicateRoles && IsRoleTakenByOther(local, role));

                string label = settings.displayName
                               + "　身高 " + settings.height.ToString("0.0") + " m"
                               + "｜速度 " + settings.moveSpeed.ToString("0.0")
                               + "｜跳 " + settings.jumpHeight.ToString("0.0") + " m"
                               + (unavailable && !hostAssigns ? "（已有人選）" : "");

                GUI.enabled = !unavailable;
                if (GUILayout.Button(label, UiKit.Button, GUILayout.Height(40f)))
                {
                    local.RequestRole(role);
                }

                GUI.enabled = true;
                GUILayout.Space(4f);
            }

            GUILayout.EndArea();
        }

        static bool IsRoleTakenByOther(PlayerSlot local, Role role)
        {
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot other = PlayerSlot.All[i];
                if (other != null && other != local && other.CurrentRole.Value == role)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // 聲音提示（畫面邊緣）
        // ------------------------------------------------------------------

        static readonly Color k_FootstepColor = new Color(1f, 0.58f, 0.22f);
        static readonly Color k_BellColor = new Color(1f, 0.9f, 0.32f);
        static readonly Color k_CryColor = new Color(1f, 0.32f, 0.3f);
        static readonly Color k_CrashColor = new Color(0.95f, 0.5f, 0.95f);

        /// <summary>
        /// 剛聽到的聲音，在畫面邊緣顯示一個聲波圖示，指出大概的方向（以鏡頭為準）；越近越大，幾秒後淡掉。
        /// 只給方向和遠近（大、中、小三種），不給確切位置或距離數字。
        /// </summary>
        static void DrawSoundCues(float width, float height)
        {
            if (Event.current.type != EventType.Repaint || SoundCues.Recent.Count == 0)
            {
                return;
            }

            PlayerCamera playerCamera = PlayerCamera.Instance;
            if (playerCamera == null || playerCamera.Camera == null)
            {
                return;
            }

            float duration = Mathf.Max(0.1f, GameConfig.Instance.soundCueSeconds);
            Transform view = playerCamera.Camera.transform;
            Vector3 forward = view.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = view.up;
                forward.y = 0f;
            }

            forward.Normalize();
            var center = new Vector2(width * 0.5f, height * 0.5f);
            float radiusX = width * 0.4f;
            float radiusY = height * 0.36f;
            Matrix4x4 matrix = GUI.matrix;
            Color previous = GUI.color;
            float now = Time.unscaledTime;

            for (int i = 0; i < SoundCues.Recent.Count; i++)
            {
                SoundCues.Cue cue = SoundCues.Recent[i];
                float age = now - cue.Time;
                if (age > duration)
                {
                    continue;
                }

                Vector3 toSound = cue.Position - view.position;
                toSound.y = 0f;
                float angle = toSound.sqrMagnitude < 0.01f ? 0f : Vector3.SignedAngle(forward, toSound, Vector3.up);
                float radians = angle * Mathf.Deg2Rad;
                var position = new Vector2(center.x + Mathf.Sin(radians) * radiusX, center.y - Mathf.Cos(radians) * radiusY);

                // 遠近只分三種大小
                float size = cue.Loudness > 0.66f ? 84f : (cue.Loudness > 0.33f ? 64f : 46f);
                Color color = cue.Kind == SoundKind.Footstep ? k_FootstepColor
                    : (cue.Kind == SoundKind.Bell ? k_BellColor : (cue.Kind == SoundKind.Crash ? k_CrashColor : k_CryColor));
                color.a = Mathf.Clamp01(1f - age / duration) * 0.95f;
                GUI.color = color;

                GUI.matrix = matrix;
                GUIUtility.RotateAroundPivot(angle, position);
                GUI.DrawTexture(new Rect(position.x - size * 0.5f, position.y - size * 0.5f, size, size), SoundCueIcon.Texture);
                GUI.matrix = matrix;

                string label = cue.Kind == SoundKind.Footstep ? "腳步聲"
                    : (cue.Kind == SoundKind.Bell ? "鈴鐺" : (cue.Kind == SoundKind.Crash ? "東西倒了！" : "叫聲！"));
                GUI.color = new Color(1f, 1f, 1f, color.a);
                GUI.Label(new Rect(position.x - 50f, position.y + size * 0.5f - 4f, 100f, 22f), label, UiKit.Tag);
            }

            GUI.matrix = matrix;
            GUI.color = previous;
        }

        // ------------------------------------------------------------------
        // 捕捉籠：籠子上方的標籤、右上角的列表
        // ------------------------------------------------------------------

        /// <summary>
        /// 籠子上方的標籤。關著動物的籠子一律顯示（誰被關在哪裡是公開的，隊友才找得到）；
        /// 空籠子只在附近、而且看得到的時候顯示編號。
        /// </summary>
        static void DrawCageLabels(float scale)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            PlayerCamera playerCamera = PlayerCamera.Instance;
            if (playerCamera == null || playerCamera.Camera == null)
            {
                return;
            }

            Camera camera = playerCamera.Camera;
            Vector3 eye = camera.transform.position;
            for (int i = 0; i < CaptureCage.Active.Count; i++)
            {
                CaptureCage cage = CaptureCage.Active[i];
                if (cage == null)
                {
                    continue;
                }

                Vector3 world = cage.transform.position + Vector3.up * 2.2f;
                PlayerCapture occupant = cage.Occupant;
                if (occupant == null)
                {
                    float distance = (world - eye).magnitude;
                    if (distance > k_EmptyCageLabelDistance || !CanSee(eye, world))
                    {
                        continue;
                    }
                }

                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                {
                    continue;
                }

                float x = screen.x / scale;
                float y = (Screen.height - screen.y) / scale;
                string label = cage.number + " 號籠";
                float tagWidth = 80f;
                if (occupant != null)
                {
                    label += "｜" + NameOf(occupant) + "｜" + Mathf.CeilToInt(occupant.CageTimeLeft.Value) + " 秒"
                        + (occupant.CagePaused.Value ? "（暫停中）" : "");
                    tagWidth = occupant.CagePaused.Value ? 290f : 220f;
                }

                var rect = new Rect(x - tagWidth * 0.5f, y - 11f, tagWidth, 22f);
                UiKit.Panel(rect);
                GUI.Label(rect, label, UiKit.Tag);
            }
        }

        // ------------------------------------------------------------------
        // 可以弄倒的東西的標記（M4）
        // ------------------------------------------------------------------

        static readonly Color k_MarkerColor = new Color(1f, 0.55f, 0.10f);
        static readonly Color k_MarkerTargetColor = new Color(1f, 0.86f, 0.15f);
        static Texture2D s_MarkerDot;
        static GUIStyle s_MarkerText;
        static GUIStyle s_MarkerKey;

        /// <summary>上一次畫面上畫了幾個破壞標記（測試工具查看用）。</summary>
        public static int BreakMarkersDrawn { get; private set; }

        /// <summary>
        /// 動物畫面上，看得到的、還沒壞的東西頭上畫一個橘色圓點，寫著弄倒它加多少混亂值（+2／+5／+10）。
        /// 太遠（GameConfig.breakMarkerRange）或被牆、樹籬擋住的不畫；越遠越小、越淡。
        /// 按 E 會弄倒的那一個換成黃色的「E」，下面寫名字。混亂值滿了（出口解鎖）之後就不畫了，弄倒東西已經沒有用。
        /// 東西的位置是固定的、大家都知道，所以這不算洩漏情報；只是預設不給人類看（GameConfig.breakMarkersForHuman）。
        /// </summary>
        static void DrawBreakMarkers(float scale)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            BreakMarkersDrawn = 0;
            PlayerCamera playerCamera = PlayerCamera.Instance;
            if (!ShouldShowBreakMarkers() || playerCamera == null || playerCamera.Camera == null)
            {
                return;
            }

            EnsureMarkerStyles();
            GameConfig config = GameConfig.Instance;
            Camera camera = playerCamera.Camera;
            Vector3 eye = camera.transform.position;
            float range = Mathf.Max(1f, config.breakMarkerRange);
            Color previous = GUI.color;
            for (int i = 0; i < Breakable.Active.Count; i++)
            {
                Breakable item = Breakable.Active[i];
                if (item == null || item.IsBroken)
                {
                    continue;
                }

                bool target = item.index == Breakable.LocalTarget;
                Vector3 world = item.transform.position + Vector3.up * (item.TopHeight + 0.3f);
                float distance = (world - eye).magnitude;
                if (!target && (distance > range || !CanSeeBreakable(eye, world, item)))
                {
                    continue;
                }

                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                {
                    continue;
                }

                float x = screen.x / scale;
                float y = (Screen.height - screen.y) / scale;
                string chaos = "+" + config.GetChaos(item.size).ToString("0");
                if (target)
                {
                    float size = 40f * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * 8f));
                    GUI.color = k_MarkerTargetColor.linear;
                    GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), s_MarkerDot);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(x - 20f, y - 20f, 40f, 40f), "E", s_MarkerKey);

                    // 名字放在 E 的上面，不要蓋住東西本身。
                    string name = string.IsNullOrEmpty(item.displayName) ? "東西" : item.displayName;
                    var rect = new Rect(x - 70f, y - 48f, 140f, 22f);
                    UiKit.Panel(rect);
                    GUI.Label(rect, name + " " + chaos + "%", UiKit.Tag);
                }
                else
                {
                    float far = Mathf.Clamp01(distance / range);
                    float baseSize = item.size == BreakSize.Large ? 38f : (item.size == BreakSize.Medium ? 34f : 30f);
                    float size = baseSize * Mathf.Lerp(1f, 0.8f, far);
                    float alpha = Mathf.Lerp(1f, 0.45f, Mathf.InverseLerp(0.5f, 1f, far));
                    Color color = k_MarkerColor.linear;
                    color.a = alpha;
                    GUI.color = color;
                    GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), s_MarkerDot);
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.Label(new Rect(x - 20f, y - 12f, 40f, 24f), chaos, s_MarkerText);
                }

                BreakMarkersDrawn++;
            }

            GUI.color = previous;
        }

        /// <summary>這台電腦的玩家現在該不該看到破壞標記。</summary>
        static bool ShouldShowBreakMarkers()
        {
            MatchFlow flow = MatchFlow.Instance;
            if (flow == null || !flow.IsSpawned || !flow.InteractionsAllowed || flow.ExitUnlocked)
            {
                return false;
            }

            PlayerCapture mine = PlayerCapture.Local;
            if (mine == null || !mine.IsSpawned)
            {
                return false;
            }

            if (mine.IsHuman)
            {
                return GameConfig.Instance.breakMarkersForHuman;
            }

            // 被網住、被扛著、關在籠子裡、在實驗室裡都弄不了東西，就不畫（在貨車上的可以下車去弄）。
            CaptureState state = mine.State.Value;
            return state == CaptureState.Free || state == CaptureState.Escaped;
        }

        /// <summary>從鏡頭看不看得到這個東西頭上的標記；東西自己的碰撞和角色不算遮蔽。</summary>
        static bool CanSeeBreakable(Vector3 eye, Vector3 point, Breakable item)
        {
            Vector3 toPoint = point - eye;
            float distance = toPoint.magnitude;
            if (distance < 0.01f)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(eye, toPoint / distance, s_SightHits, distance,
                GameLayers.SightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = s_SightHits[i].collider;
                if (hit.transform.IsChildOf(item.transform) || hit.GetComponentInParent<PlayerAvatar>() != null)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetMarkerStatics()
        {
            BreakMarkersDrawn = 0;
            s_MarkerDot = null;
            s_MarkerText = null;
            s_MarkerKey = null;
            s_BotMessage = "";
            s_BotMessageTime = -100f;
        }

        static void EnsureMarkerStyles()
        {
            if (s_MarkerDot == null)
            {
                // 邊緣柔和的白色圓點（顏色用 GUI.color 給）。
                const int size = 64;
                s_MarkerDot = new Texture2D(size, size, TextureFormat.RGBA32, false);
                s_MarkerDot.wrapMode = TextureWrapMode.Clamp;
                float radius = size * 0.5f - 1.5f;
                for (int py = 0; py < size; py++)
                {
                    for (int px = 0; px < size; px++)
                    {
                        float dx = px + 0.5f - size * 0.5f;
                        float dy = py + 0.5f - size * 0.5f;
                        float edge = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                        s_MarkerDot.SetPixel(px, py, new Color(1f, 1f, 1f, edge));
                    }
                }

                s_MarkerDot.Apply();
            }

            if (s_MarkerText == null)
            {
                s_MarkerText = new GUIStyle(UiKit.Tag) { fontSize = 13 };
                s_MarkerText.normal.textColor = Color.black;
                s_MarkerKey = new GUIStyle(UiKit.Tag) { fontSize = 18 };
                s_MarkerKey.normal.textColor = Color.black;
            }
        }

        static readonly List<PlayerCapture> s_LabAnimals = new List<PlayerCapture>();

        /// <summary>
        /// 右上角的狀態列表（所有人都看得到，這些都是公開的資訊）：
        /// 關著動物的籠子（倒數、是不是暫停中）、實驗室裡有誰、門禁卡在哪裡。
        /// 動物另外看得到「籠邊警報」：人類進到關著動物的籠子附近（GameConfig.cageAlarmRadius）就標出來。
        /// </summary>
        static void DrawStatusList(float width)
        {
            PlayerCapture mine = PlayerCapture.Local;
            bool showAlarm = mine != null && !mine.IsHuman;
            GameConfig config = GameConfig.Instance;
            MatchState match = MatchState.Instance;

            int cages = 0;
            for (int i = 0; i < CaptureCage.Active.Count; i++)
            {
                if (CaptureCage.Active[i] != null && !CaptureCage.Active[i].IsEmpty)
                {
                    cages++;
                }
            }

            MatchState.CollectLabAnimals(s_LabAnimals);
            bool hasKeycard = match != null && match.Keycard.Value != KeycardStatus.None;
            string onTruck = DescribeOnTruck();
            int lines = cages + (s_LabAnimals.Count > 0 ? 1 : 0) + (hasKeycard ? 1 : 0) + (onTruck != null ? 1 : 0);
            if (lines == 0)
            {
                return;
            }

            var panel = new Rect(width - 392f, 12f, 380f, 16f + lines * 24f + (cages > 0 ? 26f : 0f));
            UiKit.Panel(panel);
            float y = panel.y + 8f;
            if (cages > 0)
            {
                GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 24f, 26f), "捕捉籠", UiKit.Header);
                y = panel.y + 34f;
                for (int number = 1; number <= 9; number++)
                {
                    CaptureCage cage = CaptureCage.Find(number);
                    PlayerCapture occupant = cage != null ? cage.Occupant : null;
                    if (occupant == null)
                    {
                        continue;
                    }

                    string line = number + " 號籠　" + NameOf(occupant) + "　" + Mathf.CeilToInt(occupant.CageTimeLeft.Value) + " 秒";
                    if (occupant.CagePaused.Value)
                    {
                        line += "｜暫停中";
                    }

                    if (showAlarm && IsHumanNear(cage, config.cageAlarmRadius))
                    {
                        line += "｜人類在附近！";
                    }

                    GUI.Label(new Rect(panel.x + 12f, y, panel.width - 24f, 22f), line, UiKit.Hint);
                    y += 24f;
                }
            }

            if (s_LabAnimals.Count > 0)
            {
                var names = new System.Text.StringBuilder("實驗室：");
                for (int i = 0; i < s_LabAnimals.Count; i++)
                {
                    names.Append(i > 0 ? "、" : "").Append(NameOf(s_LabAnimals[i]));
                }

                GUI.Label(new Rect(panel.x + 12f, y, panel.width - 24f, 22f), names.ToString(), UiKit.Hint);
                y += 24f;
            }

            if (onTruck != null)
            {
                GUI.Label(new Rect(panel.x + 12f, y, panel.width - 24f, 22f), onTruck, UiKit.Hint);
                y += 24f;
            }

            if (hasKeycard)
            {
                GUI.Label(new Rect(panel.x + 12f, y, panel.width - 24f, 22f), DescribeKeycard(match), UiKit.Hint);
            }
        }

        /// <summary>「貨車上：P2 兔子、P3 猴子」；沒有人在車上就是 null。</summary>
        static string DescribeOnTruck()
        {
            System.Text.StringBuilder names = null;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar != null && avatar.IsSpawned && avatar.Capture != null && avatar.Capture.State.Value == CaptureState.Escaped)
                {
                    names = names == null ? new System.Text.StringBuilder("貨車上：") : names.Append("、");
                    names.Append(NameOf(avatar.Capture));
                }
            }

            return names != null ? names.ToString() : null;
        }

        static bool IsHumanNear(CaptureCage cage, float radius)
        {
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar != null && avatar.IsSpawned && avatar.AvatarRole.Value == Role.Human
                    && cage.PlanarDistance(avatar.transform.position) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // 其他玩家頭上的名牌
        // ------------------------------------------------------------------

        void DrawNameTags(float scale)
        {
            // OnGUI 每一格會被呼叫好幾次，名牌只需要在真正繪製的那一次處理。
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var playerCamera = PlayerCamera.Instance;
            if (playerCamera == null || playerCamera.Camera == null)
            {
                return;
            }

            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar == null || !avatar.IsSpawned || avatar.IsMine)
                {
                    continue;
                }

                if (!IsNameTagVisible(playerCamera.Camera, avatar))
                {
                    continue;
                }

                RoleSettings settings = avatar.Settings;
                Vector3 world = avatar.transform.position + Vector3.up * (settings.height + 0.35f);
                Vector3 screen = playerCamera.Camera.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                {
                    continue;
                }

                // 螢幕座標的原點在左下角，IMGUI 在左上角，而且要換算成縮放後的座標。
                float x = screen.x / scale;
                float y = (Screen.height - screen.y) / scale;
                string label = "P" + avatar.PlayerNumber + " " + settings.displayName + (avatar.IsBot ? "（電腦）" : "");
                float tagWidth = 116f;
                PlayerCapture capture = avatar.Capture;
                if (capture != null)
                {
                    string note = null;
                    if (capture.IsAsleep)
                    {
                        note = "睡著 " + Mathf.CeilToInt(capture.SleepRemaining);
                    }
                    else if (capture.State.Value == CaptureState.Netted)
                    {
                        note = "被網住";
                    }
                    else if (capture.State.Value == CaptureState.Carried)
                    {
                        note = "被扛著";
                    }
                    else if (capture.State.Value == CaptureState.Caged)
                    {
                        note = "在 " + capture.CageNumber.Value + " 號籠";
                    }
                    else if (capture.State.Value == CaptureState.Escaped)
                    {
                        note = "在貨車上";
                    }
                    else if (capture.IsInLab)
                    {
                        var party = avatar.GetComponent<PlayerParty>();
                        int emote = party != null ? party.CurrentEmote : 0;
                        note = emote > 0 ? PlayerParty.EmoteNames[emote] + "！" : "在實驗室";
                    }
                    else if (capture.IsProtected)
                    {
                        note = "保護中";
                    }
                    else if (capture.IsStaggered)
                    {
                        note = "踉蹌";
                    }

                    if (note != null)
                    {
                        label += "｜" + note;
                        tagWidth = 170f;
                    }
                }

                var rect = new Rect(x - tagWidth * 0.5f, y - 11f, tagWidth, 22f);
                UiKit.Panel(rect);
                GUI.Label(rect, label, UiKit.Tag);
            }
        }

        /// <summary>
        /// 這台鏡頭現在「看不看得到」這個角色，決定要不要顯示他的名牌。
        /// 太遠、或被牆和障礙物擋住都不顯示；不然名牌會穿牆，躲起來的動物（或埋伏的人類）位置就曝光了。
        /// </summary>
        public static bool IsNameTagVisible(Camera viewCamera, PlayerAvatar avatar)
        {
            Vector3 eye = viewCamera.transform.position;
            Vector3 head = avatar.transform.position + Vector3.up * (avatar.Settings.height * 0.9f);
            float maxDistance = GameConfig.Instance.nameTagMaxDistance;
            return (head - eye).sqrMagnitude <= maxDistance * maxDistance && CanSee(eye, head);
        }

        static readonly RaycastHit[] s_SightHits = new RaycastHit[16];

        /// <summary>
        /// 從鏡頭到對方頭部之間有沒有被場景（牆、箱子、通風管…）擋住。
        /// 擋在中間的如果是角色（包含自己），不算遮蔽。
        /// </summary>
        static bool CanSee(Vector3 eye, Vector3 point)
        {
            Vector3 toPoint = point - eye;
            float distance = toPoint.magnitude;
            if (distance < 0.01f)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(eye, toPoint / distance, s_SightHits, distance,
                GameLayers.SightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_SightHits[i].collider.GetComponentInParent<PlayerAvatar>() == null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
