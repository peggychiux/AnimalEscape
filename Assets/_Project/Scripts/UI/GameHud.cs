using AnimalEscape.Core;
using AnimalEscape.Match;
using AnimalEscape.Networking;
using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 進房間之後的介面：左上角的資訊面板、選身分的畫面、其他玩家頭上的名牌，
    /// 以及追與抓的提示（網子冷卻、扛起、掙脫進度…）。
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        const float k_DeniedMessageSeconds = 4f;
        const float k_SwingMessageSeconds = 1.6f;

        static readonly Color k_BarStruggle = new Color(1f, 0.78f, 0.25f);
        static readonly Color k_BarCooldown = new Color(0.45f, 0.75f, 0.95f);

        void OnGUI()
        {
            var connection = ConnectionManager.Instance;
            if (connection == null || connection.State != ConnectionState.Connected)
            {
                return;
            }

            float width;
            float height;
            float scale = UiKit.Begin(out width, out height);

            PlayerSlot local = PlayerSlot.Local;
            bool choosing = local != null && local.CurrentRole.Value == Role.None;

            DrawNameTags(scale);
            if (!choosing && PlayerCamera.Instance != null && PlayerCamera.Instance.IsFirstPerson)
            {
                DrawCrosshair(width, height);
            }

            if (!choosing)
            {
                DrawCaptureHud(width, height);
            }

            Rect info = DrawInfoPanel(connection, local, choosing);
            bool pointerOverUi = info.Contains(Event.current.mousePosition);

            if (choosing)
            {
                DrawRolePicker(local, width, height);
                pointerOverUi = true;
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
                ? "P" + capture.Avatar.PlayerNumber + " " + capture.Avatar.Settings.displayName
                : "？";
        }

        static void DrawCaptureHud(float width, float height)
        {
            PlayerCapture mine = PlayerCapture.Local;
            if (mine == null || !mine.IsSpawned)
            {
                return;
            }

            if (mine.IsHuman)
            {
                DrawHumanCaptureHud(mine, width, height);
            }
            else
            {
                DrawAnimalCaptureHud(mine, width, height);
            }
        }

        static void DrawHumanCaptureHud(PlayerCapture mine, float width, float height)
        {
            var panel = new Rect((width - 420f) * 0.5f, height - 104f, 420f, 84f);
            UiKit.Panel(panel);

            var line1 = new Rect(panel.x + 10f, panel.y + 6f, panel.width - 20f, 26f);
            var line2 = new Rect(panel.x + 10f, panel.y + 32f, panel.width - 20f, 24f);
            var bar = new Rect(panel.x + 14f, panel.y + 62f, panel.width - 28f, 12f);

            PlayerCapture carried = mine.Carried;
            if (carried != null)
            {
                GUI.Label(line1, "扛著 " + NameOf(carried) + "（走得比較慢，不能揮網）", UiKit.Center);
                GUI.Label(line2, "E 放下｜下面是牠的掙脫進度", UiKit.Tag);
                UiKit.Bar(bar, carried.Struggle.Value, k_BarStruggle);
                return;
            }

            float cooldown = mine.NetCooldownRemaining;
            if (mine.IsStaggered)
            {
                GUI.Label(line1, "被撞到了！踉蹌中 " + mine.StaggerRemaining.ToString("0.0") + " 秒", UiKit.Center);
            }
            else if (cooldown > 0f)
            {
                GUI.Label(line1, "網子冷卻中 " + cooldown.ToString("0.0") + " 秒", UiKit.Center);
            }
            else
            {
                GUI.Label(line1, "網子準備好了｜滑鼠左鍵揮網", UiKit.Center);
            }

            // 第二行：能扛的時候提示扛起；不然顯示剛才那一下有沒有打中。
            PlayerCapture target = mine.FindPickupTarget();
            if (target != null && !mine.IsStaggered)
            {
                GUI.Label(line2, "按 E 扛起 " + NameOf(target), UiKit.Tag);
            }
            else if (Time.unscaledTime - mine.LastSwingOutcomeTime < k_SwingMessageSeconds)
            {
                switch (mine.LastSwingOutcome)
                {
                    case SwingOutcome.Hit:
                        GUI.Label(line2, "網住了 " + NameOf(mine.LastSwingTarget) + "！", UiKit.Tag);
                        break;
                    case SwingOutcome.Protected:
                        GUI.Label(line2, "牠剛掙脫，暫時抓不到", UiKit.Tag);
                        break;
                    case SwingOutcome.Miss:
                        GUI.Label(line2, "揮空了", UiKit.Tag);
                        break;
                }
            }

            float total = Mathf.Max(0.01f, GameConfig.Instance.netCooldown);
            UiKit.Bar(bar, 1f - Mathf.Clamp01(cooldown / total), k_BarCooldown);
        }

        static void DrawAnimalCaptureHud(PlayerCapture mine, float width, float height)
        {
            CaptureState state = mine.State.Value;
            if (state != CaptureState.Free)
            {
                var panel = new Rect((width - 460f) * 0.5f, height - 170f, 460f, 118f);
                UiKit.Panel(panel);

                string title = state == CaptureState.Carried
                    ? "被 " + NameOf(mine.Carrier) + " 扛走了！"
                    : "被網住了！";
                GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 34f), title, UiKit.Title);
                GUI.Label(new Rect(panel.x, panel.y + 44f, panel.width, 26f), "連按 空白鍵 掙脫", UiKit.Center);
                UiKit.Bar(new Rect(panel.x + 20f, panel.y + 80f, panel.width - 40f, 22f), mine.Struggle.Value, k_BarStruggle);
                return;
            }

            string message = null;
            if (mine.IsProtected)
            {
                message = "剛掙脫：" + mine.ProtectionRemaining.ToString("0.0") + " 秒內網子抓不到你，快跑！";
            }
            else if (IsTeammateBeingCarried(mine))
            {
                message = "隊友被扛走了！全速撞上人類可以把牠救下來";
            }

            if (message != null)
            {
                var panel = new Rect((width - 460f) * 0.5f, height - 84f, 460f, 40f);
                UiKit.Panel(panel);
                GUI.Label(panel, message, UiKit.Center);
            }
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
            bool canChangeMap = isHost && match != null && registry != null && registry.Count > 1;

            float panelHeight = 172f + playerCount * 22f + (hasCode ? 34f : 0f) + (choosing ? 0f : 72f)
                + (hasMap ? 24f : 0f) + (canChangeMap ? 36f : 0f);
            var panel = new Rect(12f, 12f, 300f, panelHeight);

            UiKit.Panel(panel);
            GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, panel.height - 20f));

            string roomType = connection.IsOnlineRoom ? "線上房間" : "本機房間";
            GUILayout.Label(roomType + "｜" + (isHost ? "你是主機" : "你是玩家"), UiKit.Header);
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
                string roleName = role == Role.None ? "選擇中…" : GameConfig.Instance.GetRole(role).displayName;
                string line = "P" + slot.PlayerNumber + "　" + roleName + (slot == local ? "（你）" : "");
                GUILayout.Label(line, UiKit.Hint);
            }

            GUILayout.Space(4f);
            GUILayout.Label("WASD 移動　空白鍵跳　Shift 慢走", UiKit.Hint);
            if (!choosing && local != null)
            {
                GUILayout.Label(local.CurrentRole.Value == Role.Human
                    ? "滑鼠左鍵揮網　E 扛起／放下"
                    : "被網住或被扛著：連按空白鍵掙脫", UiKit.Hint);
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            GUILayout.Label(locked ? "按 Esc 放開滑鼠（才能點這裡的按鈕）" : "點一下畫面，回到遊戲操作", UiKit.Hint);

            GUILayout.FlexibleSpace();
            if (!choosing && local != null)
            {
                // 被網住或被扛著的時候不能換身分（主機也會擋），不然換一下就逃掉了。
                PlayerCapture mine = PlayerCapture.Local;
                bool captured = mine != null && mine.State.Value != CaptureState.Free;
                GUI.enabled = !captured;
                if (GUILayout.Button(captured ? "換身分（被抓住時不能換）" : "換身分", UiKit.Button, GUILayout.Height(30f)))
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

            if (GUILayout.Button("離開房間", UiKit.Button, GUILayout.Height(30f)))
            {
                connection.Leave();
            }

            GUILayout.EndArea();
            return panel;
        }

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

            if (Time.unscaledTime - local.DeniedTime < k_DeniedMessageSeconds && local.DeniedRole != Role.None)
            {
                string deniedName = GameConfig.Instance.GetRole(local.DeniedRole).displayName;
                GUILayout.Label(deniedName + "已經有人選了，請選別的身分。", UiKit.Warning);
            }
            else
            {
                GUILayout.Label("人類只能有一位，負責抓動物；動物可以重複選。", UiKit.Hint);
            }

            GUILayout.Space(6f);
            bool humanTaken = IsHumanTakenByOther(local);

            for (int i = 0; i < Roles.Selectable.Length; i++)
            {
                Role role = Roles.Selectable[i];
                RoleSettings settings = GameConfig.Instance.GetRole(role);
                bool unavailable = role == Role.Human && humanTaken;

                string label = settings.displayName
                               + "　身高 " + settings.height.ToString("0.0") + " m"
                               + "｜速度 " + settings.moveSpeed.ToString("0.0")
                               + "｜跳 " + settings.jumpHeight.ToString("0.0") + " m"
                               + (unavailable ? "（已有人選）" : "");

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

        static bool IsHumanTakenByOther(PlayerSlot local)
        {
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot other = PlayerSlot.All[i];
                if (other != null && other != local && other.CurrentRole.Value == Role.Human)
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
                if (avatar == null || !avatar.IsSpawned || avatar.IsOwner)
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
                string label = "P" + avatar.PlayerNumber + " " + settings.displayName;
                float tagWidth = 116f;
                PlayerCapture capture = avatar.Capture;
                if (capture != null)
                {
                    string note = null;
                    if (capture.State.Value == CaptureState.Netted)
                    {
                        note = "被網住";
                    }
                    else if (capture.State.Value == CaptureState.Carried)
                    {
                        note = "被扛著";
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
