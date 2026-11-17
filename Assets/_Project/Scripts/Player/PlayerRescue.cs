using AnimalEscape.Core;
using AnimalEscape.Match;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 救援（M3）：自由的動物走到關著隊友的捕捉籠旁邊，按住 E，按滿秒數（GameConfig.rescueSeconds）就把隊友救出來。
    ///
    /// 做法和撬鎖（PlayerLockpick）一樣：玩家端只送出「我開始救幾號籠」「我放手了」，主機確認距離、狀態之後計時，
    /// 時間到了由主機把籠子裡的動物放出來（PlayerCapture.ServerRescue），放在籠子外面靠救援者這一側，並給保護時間。
    /// 中途放手、走開、自己被網住，進度就歸零。同時站在鎖著的收容區門邊時，救人優先（PlayerLockpick 會讓開）。
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public class PlayerRescue : NetworkBehaviour
    {
        const double k_RequestInterval = 0.25;
        const float k_ServerRangeTolerance = 0.6f;

        public static PlayerRescue Local { get; private set; }

        // 注意宣告順序：StartedAt 要比 TargetCage 早到（同 PlayerLockpick）。

        /// <summary>開始救的時刻（主機時間）。</summary>
        public readonly NetworkVariable<double> StartedAt = new NetworkVariable<double>(-100.0);

        /// <summary>正在救幾號籠；0 表示沒有在救。</summary>
        public readonly NetworkVariable<int> TargetCage = new NetworkVariable<int>(0);

        PlayerAvatar m_Avatar;
        PlayerCapture m_Capture;
        InputAction m_HoldAction;
        bool m_ExternalHold;
        double m_NextRequestAt;

        public bool IsRescuing
        {
            get { return TargetCage.Value != 0; }
        }

        public float Duration
        {
            get { return GameConfig.Instance.rescueSeconds; }
        }

        /// <summary>目前的進度 0～1（沒有在救就是 0）。</summary>
        public float Progress
        {
            get
            {
                float duration = Duration;
                if (!IsRescuing || duration <= 0f)
                {
                    return 0f;
                }

                return Mathf.Clamp01((float)(PlayerCapture.Now - StartedAt.Value) / duration);
            }
        }

        /// <summary>現在能不能救人：要是自由的動物。</summary>
        public bool CanRescue
        {
            get
            {
                return m_Avatar != null && Roles.IsAnimal(m_Avatar.AvatarRole.Value)
                    && m_Capture != null && m_Capture.State.Value == CaptureState.Free;
            }
        }

        /// <summary>伸手構得到、裡面關著隊友的籠子；沒有就是 null。</summary>
        public CaptureCage CageInReach
        {
            get
            {
                return CanRescue
                    ? CaptureCage.FindNearest(transform.position, GameConfig.Instance.rescueRange, false)
                    : null;
            }
        }

        /// <summary>用程式「按住／放開」救援鍵（測試工具用）。和按住 E 走同一段程式。</summary>
        public void SetExternalHold(bool hold)
        {
            m_ExternalHold = hold;
        }

        public override void OnNetworkSpawn()
        {
            m_Avatar = GetComponent<PlayerAvatar>();
            m_Capture = GetComponent<PlayerCapture>();

            if (IsOwner)
            {
                Local = this;
                m_HoldAction = new InputAction("Rescue", InputActionType.Button);
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
                || (Application.isFocused && m_HoldAction != null && m_HoldAction.IsPressed());
            CaptureCage cage = hold ? CageInReach : null;
            double now = Time.unscaledTimeAsDouble;

            if (cage != null)
            {
                if (TargetCage.Value != cage.number && now >= m_NextRequestAt)
                {
                    m_NextRequestAt = now + k_RequestInterval;
                    BeginRpc(cage.number);
                }
            }
            else if (IsRescuing && now >= m_NextRequestAt)
            {
                m_NextRequestAt = now + k_RequestInterval;
                CancelRpc();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BeginRpc(int cageNumber)
        {
            if (TargetCage.Value == cageNumber || !ServerCanRescue(cageNumber))
            {
                return;
            }

            StartedAt.Value = PlayerCapture.Now;
            TargetCage.Value = cageNumber;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void CancelRpc()
        {
            ServerStop();
        }

        void ServerStop()
        {
            if (TargetCage.Value != 0)
            {
                TargetCage.Value = 0;
            }
        }

        bool ServerCanRescue(int cageNumber)
        {
            if (!CanRescue)
            {
                return false;
            }

            CaptureCage cage = CaptureCage.Find(cageNumber);
            return cage != null && !cage.IsEmpty
                && cage.PlanarDistance(transform.position) <= GameConfig.Instance.rescueRange + k_ServerRangeTolerance;
        }

        void ServerUpdate()
        {
            if (!IsRescuing)
            {
                return;
            }

            int number = TargetCage.Value;
            if (!ServerCanRescue(number))
            {
                ServerStop();
                return;
            }

            if (PlayerCapture.Now - StartedAt.Value < Duration)
            {
                return;
            }

            ServerStop();
            CaptureCage cage = CaptureCage.Find(number);
            PlayerCapture occupant = cage != null ? cage.Occupant : null;
            if (occupant != null)
            {
                occupant.ServerRescue(transform.position);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Local = null;
        }
    }
}
