using AnimalEscape.Core;
using AnimalEscape.Match;
using AnimalEscape.Networking;
using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 進房間之後的介面：左上角的資訊面板、選身分的畫面、其他玩家頭上的名牌，
    /// 追與抓的提示（網子冷卻、扛起、掙脫進度…）、撬鎖，以及捕捉籠（籠子上的倒數、右上角的籠子列表、救援進度、出局觀戰）。
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

        const float k_EmptyCageLabelDistance = 22f;

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

            if (!choosing)
            {
                DrawCageLabels(scale);
            }

            DrawNameTags(scale);
            if (!choosing && PlayerCamera.Instance != null && PlayerCamera.Instance.IsFirstPerson)
            {
                DrawCrosshair(width, height);
            }

            if (!choosing)
            {
                DrawSoundCues(width, height);
                DrawCaptureHud(width, height);
                DrawCageList(width);
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
                CaptureCage cage = mine.CageInReach;
                GUI.Label(line2, cage != null
                    ? "按 E 關進 " + cage.number + " 號籠！｜下面是牠的掙脫進度"
                    : "走到空籠子旁按 E 關進去（不在籠子旁按 E 是放下）", UiKit.Tag);
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

        static void DrawAnimalCaptureHud(PlayerCapture mine, float width, float height)
        {
            CaptureState state = mine.State.Value;
            if (state == CaptureState.Out)
            {
                var panel = new Rect((width - 460f) * 0.5f, height - 130f, 460f, 84f);
                UiKit.Panel(panel);
                GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 34f), "你出局了", UiKit.Title);
                PlayerAvatar watching = mine.Spectating;
                GUI.Label(new Rect(panel.x, panel.y + 46f, panel.width, 26f), watching != null
                    ? "正在看 P" + watching.PlayerNumber + " " + watching.Settings.displayName + "｜空白鍵換人看"
                    : "沒有隊友在場上了｜可以按左上角的「換身分」重新開始", UiKit.Center);
                return;
            }

            if (state == CaptureState.Caged)
            {
                GameConfig config = GameConfig.Instance;
                PlayerSlot slot = PlayerSlot.FindByClient(mine.OwnerClientId);
                int times = slot != null ? slot.TimesCaged.Value : 1;
                float total = Mathf.Max(1f, config.GetCageSeconds(times));
                var panel = new Rect((width - 480f) * 0.5f, height - 176f, 480f, 124f);
                UiKit.Panel(panel);
                GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 34f),
                    "關在 " + mine.CageNumber.Value + " 號籠（第 " + times + " 次）", UiKit.Title);
                string detail = mine.CagePaused.Value
                    ? "人類守在旁邊太久，倒數暫停中｜等隊友來救"
                    : "倒數歸零就出局｜等隊友來救（隊友在籠子旁按住 E）";
                GUI.Label(new Rect(panel.x, panel.y + 44f, panel.width, 24f), detail, UiKit.Center);
                if (times >= config.MaxTimesCaged)
                {
                    GUI.Label(new Rect(panel.x, panel.y + 66f, panel.width, 22f), "這是最後一次：再被抓就直接出局", UiKit.Tag);
                }

                var bar = new Rect(panel.x + 20f, panel.y + 92f, panel.width - 40f, 20f);
                UiKit.Bar(bar, mine.CageTimeLeft.Value / total, k_BarCage);
                GUI.Label(bar, Mathf.CeilToInt(mine.CageTimeLeft.Value) + " 秒", UiKit.Tag);
                return;
            }

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
            bool canChangeMap = isHost && match != null && registry != null && registry.Count > 1;

            bool isAnimal = !choosing && local != null && Roles.IsAnimal(local.CurrentRole.Value);
            float panelHeight = 172f + playerCount * 22f + (hasCode ? 34f : 0f) + (choosing ? 0f : 72f) + (isAnimal ? 44f : 0f)
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
                    ? "滑鼠左鍵揮網　E 扛起／放下／關進籠子"
                    : "被網住或被扛著：連按空白鍵掙脫", UiKit.Hint);
                if (isAnimal)
                {
                    GUILayout.Label("在鎖住的門邊：按住 E 撬鎖", UiKit.Hint);
                    GUILayout.Label("在關著隊友的籠子旁：按住 E 救人", UiKit.Hint);
                }
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            GUILayout.Label(locked ? "按 Esc 放開滑鼠（才能點這裡的按鈕）" : "點一下畫面，回到遊戲操作", UiKit.Hint);

            GUILayout.FlexibleSpace();
            if (!choosing && local != null)
            {
                // 被網住、被扛著、關在籠子裡的時候不能換身分（主機也會擋），不然換一下就逃掉了。出局了可以。
                PlayerCapture mine = PlayerCapture.Local;
                bool captured = mine != null && mine.State.Value != CaptureState.Free && mine.State.Value != CaptureState.Out;
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
                GUILayout.Label("每種身分只能有一位。人類從研究室出發；動物開場關在自己的收容區。", UiKit.Hint);
            }

            GUILayout.Space(6f);

            for (int i = 0; i < Roles.Selectable.Length; i++)
            {
                Role role = Roles.Selectable[i];
                RoleSettings settings = GameConfig.Instance.GetRole(role);
                bool unavailable = IsRoleTakenByOther(local, role);

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
                Color color = cue.Kind == SoundKind.Footstep ? k_FootstepColor : (cue.Kind == SoundKind.Bell ? k_BellColor : k_CryColor);
                color.a = Mathf.Clamp01(1f - age / duration) * 0.95f;
                GUI.color = color;

                GUI.matrix = matrix;
                GUIUtility.RotateAroundPivot(angle, position);
                GUI.DrawTexture(new Rect(position.x - size * 0.5f, position.y - size * 0.5f, size, size), SoundCueIcon.Texture);
                GUI.matrix = matrix;

                string label = cue.Kind == SoundKind.Footstep ? "腳步聲" : (cue.Kind == SoundKind.Bell ? "鈴鐺" : "叫聲！");
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

        /// <summary>
        /// 右上角：目前關著動物的籠子、倒數、是不是暫停中。
        /// 動物另外看得到「籠邊警報」：人類進到那個籠子附近（GameConfig.cageAlarmRadius）就標出來。
        /// </summary>
        static void DrawCageList(float width)
        {
            PlayerCapture mine = PlayerCapture.Local;
            bool showAlarm = mine != null && !mine.IsHuman;
            GameConfig config = GameConfig.Instance;

            int count = 0;
            for (int i = 0; i < CaptureCage.Active.Count; i++)
            {
                if (CaptureCage.Active[i] != null && !CaptureCage.Active[i].IsEmpty)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return;
            }

            var panel = new Rect(width - 392f, 12f, 380f, 40f + count * 24f);
            UiKit.Panel(panel);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 24f, 26f), "捕捉籠", UiKit.Header);
            float y = panel.y + 34f;
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
                if (avatar == null || !avatar.IsSpawned || avatar.IsOwner
                    || (avatar.Capture != null && avatar.Capture.State.Value == CaptureState.Out))
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
                    else if (capture.State.Value == CaptureState.Caged)
                    {
                        note = "在 " + capture.CageNumber.Value + " 號籠";
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
