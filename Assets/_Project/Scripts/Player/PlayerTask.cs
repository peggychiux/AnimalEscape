using AnimalEscape.Core;
using AnimalEscape.Match;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.Players
{
    /// <summary>M4 的「按 E」動作是哪一種。</summary>
    public enum TaskKind : byte
    {
        None = 0,

        /// <summary>（動物）弄倒東西：小東西按一下，中大的要按住。</summary>
        Break = 1,

        /// <summary>（人類）在保全控制台按住，重開系統（混亂值下降）。</summary>
        Console = 2,

        /// <summary>（動物）混亂值滿了之後，在出口按住開門（大家一起按）。</summary>
        Exit = 3
    }

    /// <summary>
    /// M4 的按 E 動作：動物破壞東西、合作開出口；人類操作保全控制台。
    ///
    /// 做法和撬鎖（PlayerLockpick）一樣：玩家端只送出「我開始做什麼、對哪一個」「我放手了」，主機確認距離、狀態之後計時，
    /// 時間到了由 MatchFlow 處理結果（加混亂值、減混亂值、累積開門進度）。中途放手、走開、被網住就中斷。
    /// 小東西是「按一下就倒」：主機收到開始就直接弄倒，不計時。
    ///
    /// 按 E 的動作很多，同一個位置可以做好幾件事時的優先順序：救人 ＞ 刷門禁卡 ＞ 開出口 ＞ 撬鎖 ＞ 破壞。
    /// 人類扛著動物、或旁邊有被網住可以扛的動物時，不會操作控制台（E 是扛起／放下）。
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public class PlayerTask : NetworkBehaviour
    {
        const double k_RequestInterval = 0.25;
        const float k_ServerRangeTolerance = 0.6f;

        public static PlayerTask Local { get; private set; }

        // 注意宣告順序：StartedAt、Target 要比 Kind 早到。

        /// <summary>開始的時刻（主機時間）。</summary>
        public readonly NetworkVariable<double> StartedAt = new NetworkVariable<double>(-100.0);

        /// <summary>對哪一個：破壞是 Breakable.index、控制台是 SecurityConsole.index、開門不用。</summary>
        public readonly NetworkVariable<int> Target = new NetworkVariable<int>(-1);

        /// <summary>正在做什麼；None 表示沒有在做。</summary>
        public readonly NetworkVariable<TaskKind> Kind = new NetworkVariable<TaskKind>(TaskKind.None);

        PlayerAvatar m_Avatar;
        PlayerCapture m_Capture;
        PlayerRescue m_Rescue;
        PlayerKeycard m_Keycard;
        PlayerLockpick m_Lockpick;
        InputAction m_HoldAction;
        bool m_ExternalHold;
        bool m_ExternalTap;
        double m_NextRequestAt;

        public bool IsWorking
        {
            get { return Kind.Value != TaskKind.None; }
        }

        /// <summary>現在這件事要按住幾秒（開門是大家一起累積的，看 MatchFlow.ExitProgress）。</summary>
        public float Duration
        {
            get { return GetDuration(Kind.Value, Target.Value); }
        }

        /// <summary>目前的進度 0～1（開門是全隊共用的進度）。</summary>
        public float Progress
        {
            get
            {
                if (Kind.Value == TaskKind.Exit)
                {
                    return MatchFlow.Instance != null ? MatchFlow.Instance.ExitProgress.Value : 0f;
                }

                float duration = Duration;
                if (!IsWorking || duration <= 0f)
                {
                    return 0f;
                }

                return Mathf.Clamp01((float)(PlayerCapture.Now - StartedAt.Value) / duration);
            }
        }

        static float GetDuration(TaskKind kind, int target)
        {
            GameConfig config = GameConfig.Instance;
            switch (kind)
            {
                case TaskKind.Break:
                {
                    Breakable item = Breakable.Find(target);
                    return item != null ? config.GetBreakSeconds(item.size) : 0f;
                }

                case TaskKind.Console:
                    return config.consoleSeconds;
                case TaskKind.Exit:
                    return config.exitOpenSeconds;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 站在這裡按 E 會做哪一件事、對哪一個（玩家端的提示和主機端的確認用同一個判斷）。
        /// tolerance 是主機端額外放寬的距離（容許網路延遲造成的位置誤差）。
        /// </summary>
        public TaskKind FindCandidate(out int target, float tolerance = 0f)
        {
            target = -1;
            MatchFlow flow = MatchFlow.Instance;
            if (flow == null || !flow.InteractionsAllowed || m_Avatar == null || m_Capture == null)
            {
                return TaskKind.None;
            }

            GameConfig config = GameConfig.Instance;
            Vector3 position = transform.position;
            if (m_Avatar.AvatarRole.Value == Role.Human)
            {
                if (m_Capture.IsCarrying || m_Capture.FindPickupTarget() != null)
                {
                    return TaskKind.None;
                }

                SecurityConsole console = SecurityConsole.FindInReach(position, config.consoleRange + tolerance);
                if (console != null && flow.IsConsoleUsable(console.index))
                {
                    target = console.index;
                    return TaskKind.Console;
                }

                return TaskKind.None;
            }

            if (!Roles.IsAnimal(m_Avatar.AvatarRole.Value) || m_Capture.State.Value != CaptureState.Free)
            {
                return TaskKind.None;
            }

            // 比這些優先的動作（救人、刷卡）做得到時，讓給它們。
            if ((m_Rescue != null && m_Rescue.CageInReach != null)
                || (m_Keycard != null && m_Keycard.DoorInReach != null && PlayerKeycard.LabHasAnimals()))
            {
                return TaskKind.None;
            }

            ExitDoor door = ExitDoor.Current;
            if (door != null && flow.ExitUnlocked && !flow.ExitOpen.Value && door.InOpeningZone(position, config.exitReach + tolerance))
            {
                return TaskKind.Exit;
            }

            if (m_Lockpick != null && m_Lockpick.GateInReach != null)
            {
                return TaskKind.None;
            }

            Breakable item = Breakable.FindInReach(position, config.breakRange + tolerance);
            if (item != null)
            {
                target = item.index;
                return TaskKind.Break;
            }

            return TaskKind.None;
        }

        /// <summary>用程式「按住／放開」E（測試工具用）。和按住 E 走同一段程式。</summary>
        public void SetExternalHold(bool hold)
        {
            m_ExternalHold = hold;
        }

        /// <summary>用程式「按一下」E（測試工具用，弄倒小東西）。</summary>
        public void ExternalTap()
        {
            m_ExternalTap = true;
        }

        public override void OnNetworkSpawn()
        {
            m_Avatar = GetComponent<PlayerAvatar>();
            m_Capture = GetComponent<PlayerCapture>();
            m_Rescue = GetComponent<PlayerRescue>();
            m_Keycard = GetComponent<PlayerKeycard>();
            m_Lockpick = GetComponent<PlayerLockpick>();

            if (IsOwner)
            {
                Local = this;
                m_HoldAction = new InputAction("Task", InputActionType.Button);
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
                Breakable.LocalTarget = -1;
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
            bool focused = Application.isFocused && m_HoldAction != null;
            bool hold = m_ExternalHold || (focused && m_HoldAction.IsPressed());
            bool tapped = m_ExternalTap || (focused && m_HoldAction.WasPressedThisFrame());
            m_ExternalTap = false;

            int target;
            TaskKind kind = FindCandidate(out target);
            double now = Time.unscaledTimeAsDouble;

            // 告訴畫面「按 E 會弄倒哪一個」：那個東西會閃，頭上的標記換成 E（Breakable、GameHud 用）。
            Breakable.LocalTarget = Kind.Value == TaskKind.Break ? Target.Value : (kind == TaskKind.Break ? target : -1);

            // 小東西按一下就倒：只在「按下去的那一下」送出，按住不放走過一排花盆不會全部弄倒。
            bool instant = kind == TaskKind.Break && GetDuration(kind, target) <= 0f;
            if (instant)
            {
                if (tapped)
                {
                    BeginRpc(kind, target);
                }
                else if (IsWorking && now >= m_NextRequestAt)
                {
                    m_NextRequestAt = now + k_RequestInterval;
                    CancelRpc();
                }

                return;
            }

            if (kind != TaskKind.None && hold)
            {
                if ((Kind.Value != kind || Target.Value != target) && now >= m_NextRequestAt)
                {
                    m_NextRequestAt = now + k_RequestInterval;
                    BeginRpc(kind, target);
                }
            }
            else if (IsWorking && now >= m_NextRequestAt)
            {
                m_NextRequestAt = now + k_RequestInterval;
                CancelRpc();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BeginRpc(TaskKind kind, int target)
        {
            if (!ServerIsValid(kind, target))
            {
                return;
            }

            if (kind == TaskKind.Break && GetDuration(kind, target) <= 0f)
            {
                ServerStop();
                if (MatchFlow.Instance != null)
                {
                    MatchFlow.Instance.ServerBreak(Breakable.Find(target));
                }

                return;
            }

            if (Kind.Value == kind && Target.Value == target)
            {
                return;
            }

            StartedAt.Value = PlayerCapture.Now;
            Target.Value = target;
            Kind.Value = kind;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void CancelRpc()
        {
            ServerStop();
        }

        void ServerStop()
        {
            if (Kind.Value != TaskKind.None)
            {
                Kind.Value = TaskKind.None;
            }
        }

        /// <summary>主機端：這件事現在還做得到嗎（狀態、距離、東西還在、控制台沒在冷卻、門還沒開…）。</summary>
        bool ServerIsValid(TaskKind kind, int target)
        {
            MatchFlow flow = MatchFlow.Instance;
            if (kind == TaskKind.None || flow == null || !flow.InteractionsAllowed)
            {
                return false;
            }

            GameConfig config = GameConfig.Instance;
            Vector3 position = transform.position;
            switch (kind)
            {
                case TaskKind.Break:
                {
                    Breakable item = Breakable.Find(target);
                    return item != null && !item.IsBroken && IsFreeAnimal()
                        && item.DistanceFrom(position) <= config.breakRange + k_ServerRangeTolerance;
                }

                case TaskKind.Console:
                {
                    SecurityConsole console = SecurityConsole.Find(target);
                    return console != null && m_Avatar.AvatarRole.Value == Role.Human && !m_Capture.IsCarrying
                        && flow.IsConsoleUsable(target)
                        && console.DistanceFrom(position) <= config.consoleRange + k_ServerRangeTolerance;
                }

                case TaskKind.Exit:
                {
                    ExitDoor door = ExitDoor.Current;
                    return door != null && IsFreeAnimal() && flow.ExitUnlocked && !flow.ExitOpen.Value
                        && door.InOpeningZone(position, config.exitReach + k_ServerRangeTolerance);
                }

                default:
                    return false;
            }
        }

        bool IsFreeAnimal()
        {
            return m_Avatar != null && Roles.IsAnimal(m_Avatar.AvatarRole.Value)
                && m_Capture != null && m_Capture.State.Value == CaptureState.Free;
        }

        void ServerUpdate()
        {
            if (!IsWorking)
            {
                return;
            }

            TaskKind kind = Kind.Value;
            int target = Target.Value;
            if (!ServerIsValid(kind, target))
            {
                ServerStop();
                return;
            }

            // 開門沒有自己的計時，由 MatchFlow 數有幾隻在按、累積全隊的進度。
            if (kind == TaskKind.Exit)
            {
                return;
            }

            if (PlayerCapture.Now - StartedAt.Value < GetDuration(kind, target))
            {
                return;
            }

            ServerStop();
            MatchFlow flow = MatchFlow.Instance;
            if (flow == null)
            {
                return;
            }

            if (kind == TaskKind.Break)
            {
                flow.ServerBreak(Breakable.Find(target));
            }
            else if (kind == TaskKind.Console)
            {
                flow.ServerUseConsole(target);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Local = null;
        }
    }
}
