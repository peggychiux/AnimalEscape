using AnimalEscape.Core;
using AnimalEscape.Match;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 開場越獄：動物在鎖住的收容區門邊「按住 E」撬鎖，按滿秒數門就打開（秒數依身分不同，在 GameConfig）。
    ///
    /// 判定在主機：玩家端只送出「我開始撬這道門」「我放手了」，主機確認牠是自由的動物、離門夠近、門還鎖著，
    /// 才開始計時；時間到了由主機把門打開（MatchState.OpenHabitats），並給門邊的動物幾秒保護。
    /// 中途放手、走開、被網住，進度就歸零。任何動物都可以撬任何一道門（從裡面或外面都行），所以先出來的可以去救隊友。
    ///
    /// 之後 M3 的「按住救隊友」可以照同一個做法：開始／取消兩個 RPC，主機計時。
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public class PlayerLockpick : NetworkBehaviour
    {
        const double k_RequestInterval = 0.25;

        // 主機檢查距離時多給的寬限（公尺）：位置是玩家端算的，主機看到的會慢一點點。
        const float k_ServerRangeTolerance = 0.6f;

        /// <summary>這台電腦自己的角色身上的那一個。</summary>
        public static PlayerLockpick Local { get; private set; }

        // 注意宣告順序：Netcode 照宣告順序套用，StartedAt 要比 Target 早到，進度條才不會閃一下舊的時間。

        /// <summary>開始撬鎖的時刻（主機時間）。</summary>
        public readonly NetworkVariable<double> StartedAt = new NetworkVariable<double>(-100.0);

        /// <summary>正在撬哪一種動物的收容區的門；Role.None 表示沒有在撬。</summary>
        public readonly NetworkVariable<Role> Target = new NetworkVariable<Role>(Role.None);

        PlayerAvatar m_Avatar;
        PlayerCapture m_Capture;
        PlayerRescue m_Rescue;
        PlayerKeycard m_Keycard;
        InputAction m_HoldAction;
        bool m_ExternalHold;
        double m_NextRequestAt;

        public bool IsPicking
        {
            get { return Target.Value != Role.None; }
        }

        /// <summary>這個身分撬開一道鎖要幾秒；0 表示不能撬。</summary>
        public float Duration
        {
            get { return m_Avatar != null ? GameConfig.Instance.GetLockpickSeconds(m_Avatar.AvatarRole.Value) : 0f; }
        }

        /// <summary>目前的進度 0～1（沒有在撬就是 0）。</summary>
        public float Progress
        {
            get
            {
                float duration = Duration;
                if (!IsPicking || duration <= 0f)
                {
                    return 0f;
                }

                return Mathf.Clamp01((float)(PlayerCapture.Now - StartedAt.Value) / duration);
            }
        }

        /// <summary>現在能不能撬鎖：要是動物、而且沒有被網住或扛著。</summary>
        public bool CanPick
        {
            get
            {
                return m_Avatar != null && Roles.IsAnimal(m_Avatar.AvatarRole.Value) && Duration > 0f
                    && (m_Capture == null || m_Capture.State.Value == CaptureState.Free);
            }
        }

        /// <summary>伸手構得到、而且還鎖著的門；沒有就是 null。</summary>
        public HabitatGate GateInReach
        {
            get
            {
                return CanPick
                    ? HabitatGate.FindLockedInReach(transform.position, GameConfig.Instance.lockpickRange)
                    : null;
            }
        }

        /// <summary>
        /// 用程式「按住／放開」撬鎖鍵（測試工具用，之後也可以給電腦玩家）。和按住 E 走同一段程式。
        /// 只有擁有這個角色的那台電腦呼叫才有效。
        /// </summary>
        public void SetExternalHold(bool hold)
        {
            m_ExternalHold = hold;
        }

        public override void OnNetworkSpawn()
        {
            m_Avatar = GetComponent<PlayerAvatar>();
            m_Capture = GetComponent<PlayerCapture>();
            m_Rescue = GetComponent<PlayerRescue>();
            m_Keycard = GetComponent<PlayerKeycard>();

            // 電腦玩家的角色在主機上也是 IsOwner，但它不讀鍵盤、也不是「這台電腦的玩家」。
            if (IsOwner && !PlayerAvatar.IsBotObject(this))
            {
                Local = this;
                m_HoldAction = new InputAction("Pick Lock", InputActionType.Button);
                m_HoldAction.AddBinding("<Keyboard>/e");
                m_HoldAction.AddBinding("<Gamepad>/buttonWest");
                m_HoldAction.Enable();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (m_HoldAction != null)
            {
                m_HoldAction.Disable();
                m_HoldAction.Dispose();
                m_HoldAction = null;
            }

            if (Local == this)
            {
                Local = null;
            }
        }

        void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                ServerUpdate();
            }

            if (IsOwner)
            {
                OwnerUpdate();
            }
        }

        // ------------------------------------------------------------------
        // 玩家端
        // ------------------------------------------------------------------

        void OwnerUpdate()
        {
            // 視窗不在最前面時不讀鍵盤（同一台電腦開多個視窗測試時，只有正在操作的那個視窗有反應）。
            bool hold = m_ExternalHold
                || (InputGate.GameInputAllowed && m_HoldAction != null && m_HoldAction.IsPressed());
            // 同時站在籠子旁邊、實驗室門口和收容區門邊時（很少見），按 E 是救人優先、刷卡其次。
            bool rescueFirst = (m_Rescue != null && m_Rescue.CageInReach != null)
                || (m_Keycard != null && m_Keycard.DoorInReach != null && PlayerKeycard.LabHasAnimals());
            HabitatGate gate = hold && !rescueFirst ? GateInReach : null;
            double now = Time.unscaledTimeAsDouble;

            if (gate != null)
            {
                // 還沒開始（或換了一道門）就請主機開始計時。主機的回覆要一點時間才會同步回來，所以隔一下才重送。
                if (Target.Value != gate.role && now >= m_NextRequestAt)
                {
                    m_NextRequestAt = now + k_RequestInterval;
                    BeginRpc(gate.role);
                }
            }
            else if (IsPicking && now >= m_NextRequestAt)
            {
                m_NextRequestAt = now + k_RequestInterval;
                CancelRpc();
            }
        }

        // ------------------------------------------------------------------
        // 主機端
        // ------------------------------------------------------------------

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BeginRpc(Role gateRole)
        {
            if (Target.Value == gateRole || !ServerCanPick(gateRole))
            {
                return;
            }

            StartedAt.Value = PlayerCapture.Now;
            Target.Value = gateRole;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void CancelRpc()
        {
            ServerStop();
        }

        void ServerStop()
        {
            if (Target.Value != Role.None)
            {
                Target.Value = Role.None;
            }
        }

        bool ServerCanPick(Role gateRole)
        {
            if (!CanPick)
            {
                return false;
            }

            HabitatGate gate = HabitatGate.Find(gateRole);
            return gate != null && !gate.IsOpen
                && gate.PlanarDistance(transform.position) <= GameConfig.Instance.lockpickRange + k_ServerRangeTolerance;
        }

        void ServerUpdate()
        {
            if (!IsPicking)
            {
                return;
            }

            Role target = Target.Value;
            if (!ServerCanPick(target))
            {
                ServerStop();
                return;
            }

            if (PlayerCapture.Now - StartedAt.Value < Duration)
            {
                return;
            }

            ServerStop();
            MatchState match = MatchState.Instance;
            if (match == null)
            {
                return;
            }

            match.ServerSetHabitatOpen(target, true);
            ServerProtectAnimalsNear(HabitatGate.Find(target));
        }

        /// <summary>門打開的那一刻，撬鎖的動物和門邊的動物都得到幾秒保護，免得人類守在門口一開就網。</summary>
        void ServerProtectAnimalsNear(HabitatGate gate)
        {
            GameConfig config = GameConfig.Instance;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar == null || !avatar.IsSpawned || avatar.Capture == null || !Roles.IsAnimal(avatar.AvatarRole.Value))
                {
                    continue;
                }

                bool near = gate != null
                    && (gate.Contains(avatar.transform.position)
                        || gate.PlanarDistance(avatar.transform.position) <= config.lockpickProtectRadius);
                if (avatar == m_Avatar || near)
                {
                    avatar.Capture.ServerProtect(config.breakFreeProtectionSeconds);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Local = null;
        }
    }
}
