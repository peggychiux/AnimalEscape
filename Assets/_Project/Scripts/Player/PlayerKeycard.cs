using AnimalEscape.Core;
using AnimalEscape.Match;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 刷門禁卡：拿著門禁卡的自由動物站在實驗室門外，按住 E，按滿秒數（GameConfig.keycardSwipeSeconds）就把實驗室裡的隊友全部放出來。
    /// 整場只能刷一次（MatchState.ServerUseKeycard 會把卡用掉）；實驗室裡沒有人時刷不了（免得白白浪費）。
    ///
    /// 做法和救援（PlayerRescue）、撬鎖（PlayerLockpick）一樣：玩家端只送出「開始刷／放手」，主機確認之後計時。
    /// 中途放手、走開、被網住，進度就歸零（被網住時卡還會掉在地上，見 MatchState）。
    /// 三種按住 E 的動作同時都可以做時，救人優先、刷卡其次、撬鎖最後。
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public class PlayerKeycard : NetworkBehaviour
    {
        const double k_RequestInterval = 0.25;
        const float k_ServerRangeTolerance = 0.6f;

        public static PlayerKeycard Local { get; private set; }

        // 注意宣告順序：StartedAt 要比 Swiping 早到（同 PlayerLockpick）。

        /// <summary>開始刷卡的時刻（主機時間）。</summary>
        public readonly NetworkVariable<double> StartedAt = new NetworkVariable<double>(-100.0);

        /// <summary>正在刷卡。</summary>
        public readonly NetworkVariable<bool> Swiping = new NetworkVariable<bool>(false);

        PlayerAvatar m_Avatar;
        PlayerCapture m_Capture;
        PlayerRescue m_Rescue;
        InputAction m_HoldAction;
        bool m_ExternalHold;
        double m_NextRequestAt;

        public bool IsSwiping
        {
            get { return Swiping.Value; }
        }

        public float Duration
        {
            get { return GameConfig.Instance.keycardSwipeSeconds; }
        }

        /// <summary>目前的進度 0～1（沒有在刷就是 0）。</summary>
        public float Progress
        {
            get
            {
                float duration = Duration;
                if (!IsSwiping || duration <= 0f)
                {
                    return 0f;
                }

                return Mathf.Clamp01((float)(PlayerCapture.Now - StartedAt.Value) / duration);
            }
        }

        /// <summary>這個角色現在是不是拿著門禁卡。</summary>
        public bool HoldsKeycard
        {
            get
            {
                MatchState match = MatchState.Instance;
                return IsSpawned && match != null && match.Keycard.Value == KeycardStatus.Held
                    && match.KeycardHolder.Value == NetworkObjectId;
            }
        }

        /// <summary>拿著卡、而且是自由的動物。</summary>
        public bool CanSwipe
        {
            get
            {
                return HoldsKeycard && m_Avatar != null && Roles.IsAnimal(m_Avatar.AvatarRole.Value)
                    && m_Capture != null && m_Capture.State.Value == CaptureState.Free;
            }
        }

        /// <summary>拿著卡、站在實驗室門口：回傳那間實驗室；不然是 null。</summary>
        public LabRoom DoorInReach
        {
            get
            {
                LabRoom lab = LabRoom.Current;
                return CanSwipe && lab != null && lab.DistanceToDoorOutside(transform.position) <= GameConfig.Instance.keycardSwipeRange
                    ? lab
                    : null;
            }
        }

        /// <summary>實驗室裡現在有沒有動物（沒有的話刷了也沒用，不讓刷）。</summary>
        public static bool LabHasAnimals()
        {
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar != null && avatar.IsSpawned && avatar.Capture != null && avatar.Capture.IsInLab)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>用程式「按住／放開」刷卡鍵（測試工具用）。和按住 E 走同一段程式。</summary>
        public void SetExternalHold(bool hold)
        {
            m_ExternalHold = hold;
        }

        public override void OnNetworkSpawn()
        {
            m_Avatar = GetComponent<PlayerAvatar>();
            m_Capture = GetComponent<PlayerCapture>();
            m_Rescue = GetComponent<PlayerRescue>();

            // 電腦玩家的角色在主機上也是 IsOwner，但它不讀鍵盤、也不是「這台電腦的玩家」。
            if (IsOwner && !PlayerAvatar.IsBotObject(this))
            {
                Local = this;
                m_HoldAction = new InputAction("Swipe Keycard", InputActionType.Button);
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

        void OwnerUpdate()
        {
            bool hold = m_ExternalHold
                || (InputGate.GameInputAllowed && m_HoldAction != null && m_HoldAction.IsPressed());
            bool rescueFirst = m_Rescue != null && m_Rescue.CageInReach != null;
            LabRoom lab = hold && !rescueFirst && LabHasAnimals() ? DoorInReach : null;
            double now = Time.unscaledTimeAsDouble;

            if (lab != null)
            {
                if (!IsSwiping && now >= m_NextRequestAt)
                {
                    m_NextRequestAt = now + k_RequestInterval;
                    BeginRpc();
                }
            }
            else if (IsSwiping && now >= m_NextRequestAt)
            {
                m_NextRequestAt = now + k_RequestInterval;
                CancelRpc();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BeginRpc()
        {
            if (IsSwiping || !ServerCanSwipe())
            {
                return;
            }

            StartedAt.Value = PlayerCapture.Now;
            Swiping.Value = true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void CancelRpc()
        {
            ServerStop();
        }

        void ServerStop()
        {
            if (Swiping.Value)
            {
                Swiping.Value = false;
            }
        }

        bool ServerCanSwipe()
        {
            LabRoom lab = LabRoom.Current;
            return CanSwipe && lab != null && LabHasAnimals()
                && lab.DistanceToDoorOutside(transform.position) <= GameConfig.Instance.keycardSwipeRange + k_ServerRangeTolerance;
        }

        void ServerUpdate()
        {
            if (!IsSwiping)
            {
                return;
            }

            if (!ServerCanSwipe())
            {
                ServerStop();
                return;
            }

            if (PlayerCapture.Now - StartedAt.Value < Duration)
            {
                return;
            }

            ServerStop();
            if (MatchState.Instance != null)
            {
                MatchState.Instance.ServerUseKeycard(m_Avatar);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Local = null;
        }
    }
}
