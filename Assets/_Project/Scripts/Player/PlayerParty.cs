using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Match;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 實驗室派對：表情動作（數字鍵 1～4）、踢球、在大螢幕控台前按 E 換人看。
    ///
    /// - 表情：玩家端送出「我要做第幾號表情」，主機確認（要是在實驗室裡的動物）後記下開始時間和編號，
    ///   每台電腦各自照這兩個值把角色的外觀（Visual_身分）晃一晃、轉一轉。只動外觀，不動碰撞和位置。
    /// - 踢球：擁有角色的那台電腦用距離判斷「我用夠快的速度撞上球了」，就請主機踢那顆球（LabBall.RequestKick）。
    /// - 大螢幕：只影響自己的畫面（LabRoom 的小鏡頭），不需要連線。
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public class PlayerParty : NetworkBehaviour
    {
        public const int EmoteCount = 4;

        /// <summary>表情的名字（編號 1～4；0 是沒有）。</summary>
        public static readonly string[] EmoteNames = { "", "揮手", "跳舞", "轉圈", "開心跳" };

        const float k_KickMinSpeed = 1.2f;
        const float k_KickCooldown = 0.3f;

        public static PlayerParty Local { get; private set; }

        // 注意宣告順序：EmoteStartedAt 要比 Emote 早到（重複做同一個表情時，只有開始時間會變）。

        /// <summary>最近一次表情開始的時刻（主機時間）。</summary>
        public readonly NetworkVariable<double> EmoteStartedAt = new NetworkVariable<double>(-100.0);

        /// <summary>最近一次做的是第幾號表情（1～4）。</summary>
        public readonly NetworkVariable<byte> Emote = new NetworkVariable<byte>(0);

        PlayerAvatar m_Avatar;
        PlayerCapture m_Capture;
        PlayerMovement m_Movement;
        InputAction[] m_EmoteActions;
        InputAction m_InteractAction;
        readonly Dictionary<LabBall, float> m_LastKick = new Dictionary<LabBall, float>();
        bool m_VisualTouched;

        /// <summary>現在正在播的表情（沒有就是 0）。</summary>
        public int CurrentEmote
        {
            get
            {
                float elapsed = (float)(PlayerCapture.Now - EmoteStartedAt.Value);
                return elapsed >= 0f && elapsed < GameConfig.Instance.emoteSeconds ? Emote.Value : 0;
            }
        }

        /// <summary>現在能不能做表情：在實驗室裡的動物（設定打開的話，自由的動物在外面也可以）。</summary>
        public bool CanEmote
        {
            get
            {
                if (m_Avatar == null || m_Capture == null || !Roles.IsAnimal(m_Avatar.AvatarRole.Value))
                {
                    return false;
                }

                CaptureState state = m_Capture.State.Value;
                return state == CaptureState.InLab || (GameConfig.Instance.emotesOutsideLab && state == CaptureState.Free);
            }
        }

        /// <summary>（在實驗室裡）站在大螢幕控台附近。</summary>
        public bool AtConsole
        {
            get
            {
                LabRoom lab = LabRoom.Current;
                return m_Capture != null && m_Capture.IsInLab && lab != null
                    && lab.DistanceToConsole(transform.position) <= GameConfig.Instance.labConsoleRange;
            }
        }

        /// <summary>做第 id 號表情（1～4）。鍵盤和測試工具都走這裡。</summary>
        public void RequestEmote(int id)
        {
            if (IsSpawned && IsOwner && id >= 1 && id <= EmoteCount && CanEmote)
            {
                EmoteRpc((byte)id);
            }
        }

        /// <summary>（在控台旁）大螢幕換下一位隊友。回傳有沒有換（不在控台旁就不會換）。</summary>
        public bool RequestNextScreen()
        {
            LabRoom lab = LabRoom.Current;
            if (!IsOwner || !AtConsole || lab == null)
            {
                return false;
            }

            lab.RequestNextScreenTarget();
            return true;
        }

        public override void OnNetworkSpawn()
        {
            m_Avatar = GetComponent<PlayerAvatar>();
            m_Capture = GetComponent<PlayerCapture>();
            m_Movement = GetComponent<PlayerMovement>();

            if (IsOwner)
            {
                Local = this;
                m_EmoteActions = new InputAction[EmoteCount];
                for (int i = 0; i < EmoteCount; i++)
                {
                    var action = new InputAction("Emote " + (i + 1), InputActionType.Button);
                    action.AddBinding("<Keyboard>/" + (i + 1));
                    action.AddBinding("<Keyboard>/numpad" + (i + 1));
                    action.Enable();
                    m_EmoteActions[i] = action;
                }

                m_InteractAction = new InputAction("Lab Console", InputActionType.Button);
                m_InteractAction.AddBinding("<Keyboard>/e");
                m_InteractAction.AddBinding("<Gamepad>/buttonWest");
                m_InteractAction.Enable();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (m_EmoteActions != null)
            {
                for (int i = 0; i < m_EmoteActions.Length; i++)
                {
                    m_EmoteActions[i].Disable();
                    m_EmoteActions[i].Dispose();
                }

                m_EmoteActions = null;
            }

            if (m_InteractAction != null)
            {
                m_InteractAction.Disable();
                m_InteractAction.Dispose();
                m_InteractAction = null;
            }

            if (Local == this)
            {
                Local = null;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void EmoteRpc(byte id)
        {
            if (id < 1 || id > EmoteCount || !CanEmote)
            {
                return;
            }

            EmoteStartedAt.Value = PlayerCapture.Now;
            Emote.Value = id;
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }

            if (Application.isFocused && m_EmoteActions != null)
            {
                for (int i = 0; i < m_EmoteActions.Length; i++)
                {
                    if (m_EmoteActions[i].WasPressedThisFrame())
                    {
                        RequestEmote(i + 1);
                    }
                }

                if (m_InteractAction != null && m_InteractAction.WasPressedThisFrame())
                {
                    RequestNextScreen();
                }
            }

            CheckKick();
        }

        /// <summary>（擁有者端）用夠快的速度朝著球撞上去 → 請主機踢球。</summary>
        void CheckKick()
        {
            if (LabBall.Active.Count == 0 || m_Movement == null || m_Capture == null || !m_Capture.CanMove)
            {
                return;
            }

            Vector3 velocity = m_Movement.PlanarVelocity;
            float speed = velocity.magnitude;
            if (speed < k_KickMinSpeed)
            {
                return;
            }

            RoleSettings settings = m_Avatar.Settings;
            float height = Mathf.Max(settings.height, settings.radius * 2f);
            for (int i = 0; i < LabBall.Active.Count; i++)
            {
                LabBall ball = LabBall.Active[i];
                if (ball == null || !ball.IsSpawned)
                {
                    continue;
                }

                Vector3 offset = ball.transform.position - transform.position;
                if (offset.y > height + ball.radius || offset.y < -ball.radius - 0.3f)
                {
                    continue;
                }

                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > ball.radius + settings.radius + 0.2f || distance < 0.01f)
                {
                    continue;
                }

                Vector3 toBall = offset / distance;
                if (Vector3.Dot(velocity / speed, toBall) < 0.2f)
                {
                    continue;
                }

                float last;
                if (m_LastKick.TryGetValue(ball, out last) && Time.time - last < k_KickCooldown)
                {
                    continue;
                }

                m_LastKick[ball] = Time.time;
                ball.RequestKick((toBall + velocity / speed).normalized, speed);
            }
        }

        // ------------------------------------------------------------------
        // 所有電腦：表情的外觀
        // ------------------------------------------------------------------

        void LateUpdate()
        {
            if (!IsSpawned || m_Avatar == null)
            {
                return;
            }

            Transform visual = m_Avatar.Visual;
            if (visual == null)
            {
                return;
            }

            int emote = CurrentEmote;
            if (emote == 0)
            {
                if (m_VisualTouched)
                {
                    visual.localRotation = Quaternion.identity;
                    m_VisualTouched = false;
                }

                return;
            }

            // PlayerAvatar.Update 每一格會先把外觀的位置設回原位（含泡水下沉），這裡再加上表情的位移。
            float duration = Mathf.Max(0.1f, GameConfig.Instance.emoteSeconds);
            float elapsed = Mathf.Max(0f, (float)(PlayerCapture.Now - EmoteStartedAt.Value));
            float t = Mathf.Clamp01(elapsed / duration);
            float fade = Mathf.Clamp01((duration - elapsed) / 0.25f);
            Vector3 offset = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            float size = m_Avatar.Settings.height;

            switch (emote)
            {
                case 1:
                    // 揮手：左右搖擺、身體往前點頭
                    rotation = Quaternion.Euler(Mathf.Sin(elapsed * 7f) * 8f * fade, 0f, Mathf.Sin(elapsed * 14f) * 20f * fade);
                    break;
                case 2:
                    // 跳舞：上下彈、左右扭
                    offset.y = Mathf.Abs(Mathf.Sin(elapsed * 8f)) * size * 0.3f * fade;
                    rotation = Quaternion.Euler(0f, Mathf.Sin(elapsed * 8f) * 35f * fade, Mathf.Sin(elapsed * 4f) * 10f * fade);
                    break;
                case 3:
                    // 轉圈：轉兩圈（頭尾慢、中間快）
                    rotation = Quaternion.Euler(0f, Mathf.SmoothStep(0f, 720f, t), 0f);
                    break;
                case 4:
                    // 開心跳：連跳三下
                    offset.y = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * size * 0.7f * fade;
                    break;
            }

            visual.localPosition += offset;
            visual.localRotation = rotation;
            m_VisualTouched = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Local = null;
        }
    }
}
