using AnimalEscape.Core;
using AnimalEscape.Match;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 追與抓（M2）：人類揮網、動物被網住與掙脫、人類扛走、隊友推撞；
    /// 捕捉籠（M3）：人類把扛著的動物關進籠子、籠子倒數、反守籠、出局後觀看隊友（救援在 PlayerRescue）。
    ///
    /// 判定全部在主機：玩家端只送出「我按了揮網／扛起／掙脫」「我撞到人了」，主機檢查距離、角度、冷卻之後才改狀態，
    /// 狀態再用 NetworkVariable 同步給所有人。數值都在 GameConfig 的「追與抓」。
    ///
    /// 每個角色身上都有這個元件（人類和動物共用一個預製物），用到哪些欄位看身分：
    ///   動物：State、Struggle、CarrierId、ProtectedUntil、DropPoint、CageNumber、CageTimeLeft、CagePaused、SlowUntil
    ///   人類：CarryingId、NetReadyAt、StaggerUntil、SwingStartedAt
    ///
    /// 被扛著的動物怎麼跟著人類：每台電腦各自在 LateUpdate 把牠擺到人類肩上（不靠牠自己的位置同步），
    /// 所以不會因為網路延遲而抖。放下來時，主機先決定落地的位置（DropPoint），再由擁有牠的那台電腦把自己搬過去。
    /// </summary>
    [DefaultExecutionOrder(-50)] // LateUpdate 要比鏡頭早，被扛著的人鏡頭才不會慢一格
    [RequireComponent(typeof(PlayerAvatar))]
    public class PlayerCapture : NetworkBehaviour
    {
        public const string NetToolName = "NetTool";
        public const string NetCoverName = "NetCover";

        /// <summary>CarrierId、CarryingId 用這個值表示「沒有」。</summary>
        public const ulong None = ulong.MaxValue;

        const float k_SwingAnimationSeconds = 0.45f;
        const float k_PointBlankDistance = 0.7f;

        /// <summary>這台電腦自己的角色身上的那一個。</summary>
        public static PlayerCapture Local { get; private set; }

        // 注意：DropPoint 一定要宣告在 State 前面。同一批更新裡，Netcode 照宣告順序套用並通知，
        // 這樣 State 變成「不是被扛著」的那一刻，DropPoint 已經是新的落地位置了。
        public readonly NetworkVariable<Vector3> DropPoint = new NetworkVariable<Vector3>();

        /// <summary>（動物）關在幾號籠（1～5）；0 表示沒有被關。和 DropPoint 一樣要宣告在 State 前面。</summary>
        public readonly NetworkVariable<int> CageNumber = new NetworkVariable<int>(0);

        public readonly NetworkVariable<CaptureState> State = new NetworkVariable<CaptureState>(CaptureState.Free);

        /// <summary>（動物，在籠子裡）倒數還剩幾秒。主機每 0.1 秒同步一次。</summary>
        public readonly NetworkVariable<float> CageTimeLeft = new NetworkVariable<float>(0f);

        /// <summary>（動物，在籠子裡）人類守在籠子旁太久，倒數暫停中。</summary>
        public readonly NetworkVariable<bool> CagePaused = new NetworkVariable<bool>(false);

        /// <summary>（動物）保護中被網子打到，變慢到什麼時候（主機時間）。</summary>
        public readonly NetworkVariable<double> SlowUntil = new NetworkVariable<double>(-100.0);

        /// <summary>掙脫值 0～1，滿了就掙脫。</summary>
        public readonly NetworkVariable<float> Struggle = new NetworkVariable<float>(0f);

        /// <summary>（動物）正扛著我的人類是哪個網路物件。</summary>
        public readonly NetworkVariable<ulong> CarrierId = new NetworkVariable<ulong>(None);

        /// <summary>（人類）我正扛著的動物是哪個網路物件。</summary>
        public readonly NetworkVariable<ulong> CarryingId = new NetworkVariable<ulong>(None);

        /// <summary>（動物）保護到什麼時候（主機時間）。這之前網子抓不到。</summary>
        public readonly NetworkVariable<double> ProtectedUntil = new NetworkVariable<double>(-100.0);

        /// <summary>（人類）網子什麼時候可以再揮（主機時間）。</summary>
        public readonly NetworkVariable<double> NetReadyAt = new NetworkVariable<double>(-100.0);

        /// <summary>（人類）踉蹌到什麼時候（主機時間）。</summary>
        public readonly NetworkVariable<double> StaggerUntil = new NetworkVariable<double>(-100.0);

        /// <summary>（人類）最近一次揮網是什麼時候開始的（主機時間），大家用它來播揮網的動作。</summary>
        public readonly NetworkVariable<double> SwingStartedAt = new NetworkVariable<double>(-100.0);

        PlayerAvatar m_Avatar;
        PlayerMovement m_Movement;
        CharacterController m_Controller;
        NetworkTransform m_NetworkTransform;
        Transform m_NetTool;
        Transform m_NetCover;
        float m_NetToolPitch = k_RestPitch;

        InputAction m_MouseUseAction;
        InputAction m_PadUseAction;
        InputAction m_InteractAction;
        InputAction m_StruggleAction;

        // 只有主機用
        double m_PendingSwingAt = -1.0;
        double m_LastStrugglePressAt = -100.0;
        double m_LastBumpedAt = -100.0;
        float m_StruggleExact;
        float m_StruggleSyncTimer;
        float m_CageTimeExact;
        float m_CageSyncTimer;
        float m_CampSeconds;

        // 只有擁有者用：出局後正在看哪一位隊友
        PlayerAvatar m_Spectating;

        // 只有擁有者用
        float m_LastBumpReportTime = -100f;

        /// <summary>（人類，擁有者端）最近一次揮網的結果，介面拿來顯示「網住了／揮空」。</summary>
        public SwingOutcome LastSwingOutcome { get; private set; }

        /// <summary>LastSwingOutcome 是什麼時候收到的（Time.unscaledTime）。</summary>
        public float LastSwingOutcomeTime { get; private set; } = -100f;

        /// <summary>最近一次網住的是誰（沒有就是 null）。</summary>
        public PlayerCapture LastSwingTarget { get; private set; }

        // ------------------------------------------------------------------
        // 給別的程式查的狀態
        // ------------------------------------------------------------------

        /// <summary>主機時間（所有電腦大致一致），冷卻、保護這些計時都用它。</summary>
        public static double Now
        {
            get
            {
                NetworkManager net = NetworkManager.Singleton;
                return net != null ? net.ServerTime.Time : 0.0;
            }
        }

        public PlayerAvatar Avatar
        {
            get { return m_Avatar; }
        }

        public bool IsHuman
        {
            get { return m_Avatar != null && m_Avatar.AvatarRole.Value == Role.Human; }
        }

        /// <summary>（動物）現在能不能自己走動。被網住、被扛著、出局都不行；在籠子裡可以走（出不去）。</summary>
        public bool CanMove
        {
            get { return State.Value == CaptureState.Free || State.Value == CaptureState.Caged; }
        }

        /// <summary>（動物）是不是被網住或被扛著（這兩種情況可以連按掙脫）。</summary>
        public bool IsHeld
        {
            get { return State.Value == CaptureState.Netted || State.Value == CaptureState.Carried; }
        }

        public bool IsOut
        {
            get { return State.Value == CaptureState.Out; }
        }

        public bool IsSlowed
        {
            get { return Now < SlowUntil.Value; }
        }

        /// <summary>（擁有者端，出局後）正在看的隊友；沒有就是 null。</summary>
        public PlayerAvatar Spectating
        {
            get { return m_Spectating; }
        }

        /// <summary>（人類）扛著動物時，按 E 會關進去的那個空籠子；沒有就是 null。主機判定和介面提示用同一個。</summary>
        public CaptureCage CageInReach
        {
            get
            {
                return IsHuman && IsCarrying
                    ? CaptureCage.FindNearest(transform.position, GameConfig.Instance.putInCageRange, true)
                    : null;
            }
        }

        /// <summary>（人類）是不是正扛著動物。</summary>
        public bool IsCarrying
        {
            get { return CarryingId.Value != None; }
        }

        public bool IsStaggered
        {
            get { return Now < StaggerUntil.Value; }
        }

        public bool IsProtected
        {
            get { return Now < ProtectedUntil.Value; }
        }

        public float StaggerRemaining
        {
            get { return Mathf.Max(0f, (float)(StaggerUntil.Value - Now)); }
        }

        public float ProtectionRemaining
        {
            get { return Mathf.Max(0f, (float)(ProtectedUntil.Value - Now)); }
        }

        public float NetCooldownRemaining
        {
            get { return Mathf.Max(0f, (float)(NetReadyAt.Value - Now)); }
        }

        /// <summary>（人類）現在按揮網會不會被主機接受。</summary>
        public bool CanSwing
        {
            get { return IsHuman && !IsCarrying && !IsStaggered && NetCooldownRemaining <= 0f; }
        }

        /// <summary>移動速度要乘上的倍率：扛著動物、踉蹌時會變慢。</summary>
        public float SpeedMultiplier
        {
            get
            {
                GameConfig config = GameConfig.Instance;
                float multiplier = 1f;
                if (IsCarrying)
                {
                    multiplier *= config.carrySpeedMultiplier;
                }

                if (IsStaggered)
                {
                    multiplier *= config.staggerSpeedMultiplier;
                }

                if (IsSlowed)
                {
                    multiplier *= config.protectedNetSlowMultiplier;
                }

                return multiplier;
            }
        }

        /// <summary>（動物）正扛著我的人類；沒有就是 null。</summary>
        public PlayerCapture Carrier
        {
            get { return Find(CarrierId.Value); }
        }

        /// <summary>（人類）我正扛著的動物；沒有就是 null。</summary>
        public PlayerCapture Carried
        {
            get { return Find(CarryingId.Value); }
        }

        /// <summary>用網路物件編號找角色身上的 PlayerCapture。</summary>
        public static PlayerCapture Find(ulong networkObjectId)
        {
            if (networkObjectId == None)
            {
                return null;
            }

            NetworkManager net = NetworkManager.Singleton;
            if (net == null || net.SpawnManager == null)
            {
                return null;
            }

            NetworkObject found;
            return net.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out found) && found != null
                ? found.GetComponent<PlayerCapture>()
                : null;
        }

        /// <summary>（人類）附近最近一隻「被網住、可以扛起來」的動物；沒有就是 null。主機判定和介面提示用同一個。</summary>
        public PlayerCapture FindPickupTarget()
        {
            GameConfig config = GameConfig.Instance;
            PlayerCapture best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                PlayerCapture capture = avatar != null ? avatar.Capture : null;
                if (capture == null || capture == this || capture.State.Value != CaptureState.Netted)
                {
                    continue;
                }

                Vector3 offset = avatar.transform.position - transform.position;
                float heightDifference = Mathf.Abs(offset.y);
                offset.y = 0f;
                float distance = offset.magnitude - avatar.Settings.radius;
                if (distance <= config.pickupRange && heightDifference <= config.netMaxHeightDifference && distance < bestDistance)
                {
                    best = capture;
                    bestDistance = distance;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------
        // 生成與結束
        // ------------------------------------------------------------------

        public override void OnNetworkSpawn()
        {
            m_Avatar = GetComponent<PlayerAvatar>();
            m_Movement = GetComponent<PlayerMovement>();
            m_Controller = GetComponent<CharacterController>();
            m_NetworkTransform = GetComponent<NetworkTransform>();
            m_NetTool = transform.Find(NetToolName);
            m_NetCover = transform.Find(NetCoverName);

            State.OnValueChanged += OnStateChanged;
            m_Avatar.AvatarRole.OnValueChanged += OnRoleChanged;
            FitNetCover();

            if (IsOwner)
            {
                Local = this;
                CreateInput();
            }

            RefreshVisuals();
        }

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= OnStateChanged;
            if (m_Avatar != null)
            {
                m_Avatar.AvatarRole.OnValueChanged -= OnRoleChanged;
            }

            DisposeInput();
            if (IsOwner)
            {
                StopSpectating(false);
            }

            if (Local == this)
            {
                Local = null;
            }

            // 整個房間正在關閉時不用處理（所有東西都會一起消失）。
            NetworkManager net = NetworkManager.Singleton;
            if (IsServer && net != null && net.IsListening && !net.ShutdownInProgress)
            {
                ServerCleanUpOnDespawn();
            }
        }

        void OnRoleChanged(Role previous, Role current)
        {
            FitNetCover();
            RefreshVisuals();
        }

        /// <summary>主機端：這個角色要消失了（換身分、離線、換地圖），把和它有關的扛人關係解開。</summary>
        void ServerCleanUpOnDespawn()
        {
            // 我是扛人的：把扛著的動物放掉（直接恢復自由，不然牠會卡在一個不存在的人肩上）。
            PlayerCapture carried = Find(CarryingId.Value);
            if (carried != null && carried != this && carried.IsSpawned && carried.State.Value == CaptureState.Carried)
            {
                carried.ServerRelease(CaptureState.Free, GroundPointBelow(transform.position), true, false);
            }

            // 我是被扛的：讓人類的手空出來。
            PlayerCapture carrier = Find(CarrierId.Value);
            if (carrier != null && carrier != this && carrier.IsSpawned && carrier.CarryingId.Value == NetworkObjectId)
            {
                carrier.CarryingId.Value = None;
            }
        }

        static Vector3 GroundPointBelow(Vector3 position)
        {
            RaycastHit hit;
            if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out hit, 10f, GameLayers.SightMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * 0.05f;
            }

            return position;
        }

        // ------------------------------------------------------------------
        // 玩家端：送出操作（鍵盤滑鼠和測試工具都走這幾個方法）
        // ------------------------------------------------------------------

        /// <summary>（人類）揮網。只有擁有這個角色的那台電腦呼叫才有效。</summary>
        public void RequestSwing()
        {
            if (IsSpawned && IsOwner)
            {
                SwingRpc();
            }
        }

        /// <summary>（人類）扛起附近被網住的動物；已經扛著就是放下。</summary>
        public void RequestInteract()
        {
            if (IsSpawned && IsOwner)
            {
                InteractRpc();
            }
        }

        /// <summary>（動物）掙扎一下。被網住或被扛著時才有用。</summary>
        public void RequestStruggle()
        {
            if (IsSpawned && IsOwner && IsHeld)
            {
                StruggleRpc();
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
                ReadInput();
                CheckBump();
                if (State.Value == CaptureState.Out)
                {
                    UpdateSpectate();
                }
            }
        }

        void ReadInput()
        {
            // 視窗不在最前面時不讀取輸入（同一台電腦開多個視窗測試時，只有正在操作的那個視窗有反應）。
            if (m_InteractAction == null || !Application.isFocused)
            {
                return;
            }

            // 滑鼠左鍵只有在「滑鼠已經鎖在畫面裡」時才算；不然點一下回到遊戲的那一下也會揮網。
            bool use = m_PadUseAction.WasPressedThisFrame()
                || (Cursor.lockState == CursorLockMode.Locked && m_MouseUseAction.WasPressedThisFrame());

            if (IsHuman)
            {
                if (use)
                {
                    RequestSwing();
                }

                if (m_InteractAction.WasPressedThisFrame())
                {
                    RequestInteract();
                }
            }
            else if (IsHeld && (use || m_StruggleAction.WasPressedThisFrame()))
            {
                RequestStruggle();
            }
        }

        /// <summary>
        /// （擁有者端，每一格）自由的動物用夠快的速度、朝著「正扛著隊友的人類」衝到貼身的距離＝推撞，回報給主機判定。
        ///
        /// 用距離判斷，不用 CharacterController 的碰撞通知（OnControllerColliderHit）：兩個角色迎面對衝時，
        /// 對方的位置是照網路同步直接擺過來的，常常還沒「撞到」就已經疊在一起，碰撞通知有一半的機率不會來。
        /// </summary>
        void CheckBump()
        {
            if (IsHuman || State.Value != CaptureState.Free || m_Movement == null)
            {
                return;
            }

            if (Time.time - m_LastBumpReportTime < 0.4f)
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            Vector3 velocity = m_Movement.PlanarVelocity;
            float speed = velocity.magnitude;
            if (speed < config.bumpMinSpeed)
            {
                return;
            }

            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                PlayerCapture other = avatar != null ? avatar.Capture : null;
                if (other == null || other == this || !other.IsHuman || !other.IsCarrying)
                {
                    continue;
                }

                Vector3 offset = avatar.transform.position - transform.position;
                if (Mathf.Abs(offset.y) > 1.5f)
                {
                    continue;
                }

                offset.y = 0f;
                float distance = offset.magnitude;
                float touching = avatar.Settings.radius + m_Avatar.Settings.radius;
                if (distance > touching + config.bumpReach)
                {
                    continue;
                }

                // 要是朝著人類衝過去的才算；從旁邊擦過、或正在跑開都不算。
                if (distance > 0.05f && Vector3.Dot(velocity / speed, offset / distance) < 0.3f)
                {
                    continue;
                }

                m_LastBumpReportTime = Time.time;
                BumpRpc(other.NetworkObjectId);
                return;
            }
        }

        // ------------------------------------------------------------------
        // 主機端：判定
        // ------------------------------------------------------------------

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SwingRpc()
        {
            if (!CanSwing || m_PendingSwingAt >= 0.0)
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            double now = Now;
            SwingStartedAt.Value = now;
            NetReadyAt.Value = now + config.netCooldown;

            // 網子揮下來要一點時間，到時候才判定有沒有打中。
            m_PendingSwingAt = now + config.netWindup;
        }

        void ServerUpdate()
        {
            GameConfig config = GameConfig.Instance;

            if (m_PendingSwingAt >= 0.0 && Now >= m_PendingSwingAt)
            {
                m_PendingSwingAt = -1.0;
                ServerResolveSwing();
            }

            if (State.Value == CaptureState.Caged)
            {
                ServerUpdateCage(config);
            }

            if (IsHeld)
            {
                // 掙脫值沒在按的時候會慢慢掉。每 0.1 秒才同步一次，不用每一格都送。
                m_StruggleExact = Mathf.Max(0f, m_StruggleExact - config.struggleDecayPerSecond * Time.deltaTime);
                m_StruggleSyncTimer -= Time.deltaTime;
                if (m_StruggleSyncTimer <= 0f)
                {
                    m_StruggleSyncTimer = 0.1f;
                    if (Mathf.Abs(Struggle.Value - m_StruggleExact) > 0.001f)
                    {
                        Struggle.Value = m_StruggleExact;
                    }
                }

                // 保險：扛我的人不見了（例如斷線的那一格），就恢復自由。
                if (State.Value == CaptureState.Carried && Carrier == null)
                {
                    ServerRelease(CaptureState.Free, GroundPointBelow(transform.position), true);
                }
            }

            if (CarryingId.Value != None && Carried == null)
            {
                CarryingId.Value = None;
            }
        }

        static readonly RaycastHit[] s_PathHits = new RaycastHit[16];

        /// <summary>主機端：網子揮到底了，看有沒有網到動物（一次最多一隻，取最近的）。</summary>
        void ServerResolveSwing()
        {
            if (!IsHuman || IsCarrying)
            {
                return;
            }

            // 揮到一半被撞到（踉蹌）就算揮空。
            if (IsStaggered)
            {
                SwingOutcomeRpc(SwingOutcome.Miss, None);
                return;
            }

            GameConfig config = GameConfig.Instance;
            Vector3 origin = transform.position;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            PlayerCapture best = null;
            float bestDistance = float.MaxValue;
            bool blockedByProtection = false;
            PlayerCapture protectedTarget = null;
            float protectedDistance = float.MaxValue;

            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                PlayerCapture capture = avatar != null ? avatar.Capture : null;
                if (capture == null || capture == this || capture.IsHuman || capture.State.Value != CaptureState.Free)
                {
                    continue;
                }

                RoleSettings settings = avatar.Settings;
                Vector3 offset = avatar.transform.position - origin;
                float heightDifference = Mathf.Abs(offset.y);
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance - settings.radius > config.netRange || heightDifference > config.netMaxHeightDifference)
                {
                    continue;
                }

                // 貼在身上的不看角度（不然站在腳邊反而打不到）。
                if (distance > k_PointBlankDistance && Vector3.Angle(forward, offset) > config.netHalfAngle)
                {
                    continue;
                }

                Vector3 hand = origin + Vector3.up * config.netOriginHeight;
                Vector3 body = avatar.transform.position + Vector3.up * (Mathf.Max(settings.height, settings.radius * 2f) * 0.5f);
                if (!HasClearPath(hand, body))
                {
                    continue;
                }

                if (capture.IsProtected)
                {
                    // 保護中：抓不到，但會被網子拖慢一下（同時打到好幾隻保護中的，只拖慢最近的那隻）。
                    if (protectedTarget == null || distance < protectedDistance)
                    {
                        protectedTarget = capture;
                        protectedDistance = distance;
                    }

                    blockedByProtection = true;
                    continue;
                }

                if (distance < bestDistance)
                {
                    best = capture;
                    bestDistance = distance;
                }
            }

            if (best != null)
            {
                best.ServerSetNetted();
                SwingOutcomeRpc(SwingOutcome.Hit, best.NetworkObjectId);
            }
            else
            {
                if (protectedTarget != null)
                {
                    protectedTarget.SlowUntil.Value = Now + config.protectedNetSlowSeconds;
                }

                SwingOutcomeRpc(blockedByProtection ? SwingOutcome.Protected : SwingOutcome.Miss,
                    protectedTarget != null ? protectedTarget.NetworkObjectId : None);
            }
        }

        /// <summary>
        /// 兩點之間有沒有被場景擋住（牆、櫃子、樹籬、管道、收容區的鐵網…）。角色本身不算，水上的隱形擋牆也不算。
        /// </summary>
        static bool HasClearPath(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            float distance = direction.magnitude;
            if (distance < 0.01f)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(from, direction / distance, s_PathHits, distance,
                GameLayers.NetMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_PathHits[i].collider.GetComponentInParent<PlayerAvatar>() == null)
                {
                    return false;
                }
            }

            return true;
        }

        [Rpc(SendTo.Owner)]
        void SwingOutcomeRpc(SwingOutcome outcome, ulong targetId)
        {
            LastSwingOutcome = outcome;
            LastSwingOutcomeTime = Time.unscaledTime;
            LastSwingTarget = Find(targetId);
        }

        void ServerSetNetted()
        {
            m_StruggleExact = 0f;
            m_StruggleSyncTimer = 0.1f;
            Struggle.Value = 0f;
            CarrierId.Value = None;
            State.Value = CaptureState.Netted;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void InteractRpc()
        {
            if (!IsHuman || IsStaggered)
            {
                return;
            }

            // 已經扛著：旁邊有空籠子就關進去；沒有就放下（動物還在網子裡，掙脫值保留）。
            PlayerCapture carried = Carried;
            if (carried != null)
            {
                CaptureCage cage = CageInReach;
                if (cage != null)
                {
                    ServerPutInCage(carried, cage);
                }
                else
                {
                    carried.ServerRelease(CaptureState.Netted, FindDropPoint(carried, false), false);
                }

                return;
            }

            PlayerCapture target = FindPickupTarget();
            if (target != null)
            {
                target.CarrierId.Value = NetworkObjectId;
                target.State.Value = CaptureState.Carried;
                CarryingId.Value = target.NetworkObjectId;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void StruggleRpc()
        {
            if (!IsHeld)
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            double now = Now;
            if (now - m_LastStrugglePressAt < config.struggleMinPressInterval)
            {
                return;
            }

            m_LastStrugglePressAt = now;
            float gain = State.Value == CaptureState.Carried ? config.struggleGainCarried : config.struggleGainNetted;
            m_StruggleExact = Mathf.Min(1f, m_StruggleExact + gain);
            m_StruggleSyncTimer = 0.1f;
            Struggle.Value = m_StruggleExact;

            if (m_StruggleExact >= 1f)
            {
                ServerBreakFree(false);
            }
        }

        /// <summary>（由撞人的動物送出）我撞到了扛著隊友的人類。</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BumpRpc(ulong humanId)
        {
            if (IsHuman || State.Value != CaptureState.Free)
            {
                return;
            }

            PlayerCapture human = Find(humanId);
            if (human == null || !human.IsHuman)
            {
                return;
            }

            PlayerCapture carried = human.Carried;
            if (carried == null || carried == this)
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            double now = Now;
            if (now - human.m_LastBumpedAt < config.bumpCooldown)
            {
                return;
            }

            // 主機自己再確認一次距離（放寬一點，容許網路延遲造成的位置誤差）。
            Vector3 offset = human.transform.position - transform.position;
            offset.y = 0f;
            float touching = human.m_Avatar.Settings.radius + m_Avatar.Settings.radius;
            if (offset.magnitude > touching + 1.5f)
            {
                return;
            }

            human.m_LastBumpedAt = now;
            carried.ServerBreakFree(true);
        }

        /// <summary>主機端：這隻動物恢復自由（自己掙脫，或被隊友撞下來）。</summary>
        // ------------------------------------------------------------------
        // 主機端：捕捉籠（M3）
        // ------------------------------------------------------------------

        /// <summary>
        /// （人類身上呼叫）把扛著的動物關進籠子。被關的次數記在玩家狀態（PlayerSlot.TimesCaged）；
        /// 超過可以被關的次數（預設第 4 次）就直接出局。
        /// </summary>
        void ServerPutInCage(PlayerCapture animal, CaptureCage cage)
        {
            GameConfig config = GameConfig.Instance;
            PlayerSlot slot = PlayerSlot.FindByClient(animal.OwnerClientId);
            int times = (slot != null ? slot.TimesCaged.Value : 0) + 1;
            if (slot != null)
            {
                slot.TimesCaged.Value = times;
            }

            if (CarryingId.Value == animal.NetworkObjectId)
            {
                CarryingId.Value = None;
            }

            animal.CarrierId.Value = None;
            animal.m_StruggleExact = 0f;
            animal.Struggle.Value = 0f;

            // 先寫位置和籠子編號，再改狀態（見 DropPoint 宣告處的說明）。
            animal.DropPoint.Value = cage.InsidePoint;
            float seconds = config.GetCageSeconds(times);
            if (seconds <= 0f)
            {
                animal.ServerSetOut();
                return;
            }

            animal.CageNumber.Value = cage.number;
            animal.m_CageTimeExact = seconds;
            animal.m_CageSyncTimer = 0f;
            animal.m_CampSeconds = 0f;
            animal.CageTimeLeft.Value = seconds;
            animal.CagePaused.Value = false;
            animal.State.Value = CaptureState.Caged;
        }

        /// <summary>主機端，每一格：籠子倒數、反守籠。</summary>
        void ServerUpdateCage(GameConfig config)
        {
            CaptureCage cage = CaptureCage.Find(CageNumber.Value);
            if (cage == null)
            {
                // 籠子不見了（不應該發生；保險起見放出來）。
                ServerRescue(transform.position);
                return;
            }

            // 反守籠：有人類在籠子旁邊，連續待太久就暫停倒數；人類一離開範圍就重新算。
            bool humanNear = false;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar != null && avatar.IsSpawned && avatar.AvatarRole.Value == Role.Human
                    && cage.PlanarDistance(avatar.transform.position) <= config.campRadius)
                {
                    humanNear = true;
                    break;
                }
            }

            m_CampSeconds = humanNear ? m_CampSeconds + Time.deltaTime : 0f;
            bool paused = m_CampSeconds >= config.campSecondsBeforePause;
            if (!paused)
            {
                m_CageTimeExact -= Time.deltaTime;
            }

            if (m_CageTimeExact <= 0f)
            {
                ServerSetOut();
                return;
            }

            if (CagePaused.Value != paused)
            {
                CagePaused.Value = paused;
            }

            // 倒數每 0.1 秒同步一次就夠了。
            m_CageSyncTimer -= Time.deltaTime;
            if (m_CageSyncTimer <= 0f)
            {
                m_CageSyncTimer = 0.1f;
                CageTimeLeft.Value = m_CageTimeExact;
            }
        }

        /// <summary>主機端：直接改倒數（測試工具用）。</summary>
        public void ServerSetCageTime(float seconds)
        {
            if (IsServer && State.Value == CaptureState.Caged)
            {
                m_CageTimeExact = Mathf.Max(0.01f, seconds);
                CageTimeLeft.Value = m_CageTimeExact;
            }
        }

        /// <summary>主機端：出局（倒數歸零，或被關的次數超過上限）。角色從場上消失，玩家改成看隊友。</summary>
        public void ServerSetOut()
        {
            if (!IsServer)
            {
                return;
            }

            CageNumber.Value = 0;
            CagePaused.Value = false;
            CageTimeLeft.Value = 0f;
            State.Value = CaptureState.Out;
        }

        /// <summary>
        /// 主機端：從籠子裡救出來。放到籠子外面、靠救援者那一側的空位，並給保護時間。
        /// </summary>
        public void ServerRescue(Vector3 rescuerPosition)
        {
            if (!IsServer || State.Value != CaptureState.Caged)
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            CaptureCage cage = CaptureCage.Find(CageNumber.Value);
            Vector3 drop = transform.position;
            if (cage != null)
            {
                drop = FindSpotOutsideCage(cage, rescuerPosition);
            }

            CageNumber.Value = 0;
            CagePaused.Value = false;
            DropPoint.Value = drop;
            ProtectedUntil.Value = Now + config.rescueProtectionSeconds;
            State.Value = CaptureState.Free;
        }

        Vector3 FindSpotOutsideCage(CaptureCage cage, Vector3 rescuerPosition)
        {
            RoleSettings settings = m_Avatar.Settings;
            float radius = settings.radius;
            float height = Mathf.Max(settings.height, radius * 2f);
            Vector3 center = cage.InsidePoint;
            float distance = cage.size * 0.5f + radius + 0.35f;

            // 先試救援者那一邊，再試其他三邊。
            Vector3 toward = rescuerPosition - center;
            toward.y = 0f;
            float baseYaw = toward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toward).eulerAngles.y : 0f;
            float[] offsets = { 0f, 90f, -90f, 180f, 45f, -45f, 135f, -135f };
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, baseYaw + offsets[i], 0f) * Vector3.forward;
                Vector3 point = center + direction * distance;
                if (IsSpaceFree(point, radius, height, Physics.DefaultRaycastLayers))
                {
                    return point;
                }
            }

            return center + Quaternion.Euler(0f, baseYaw, 0f) * Vector3.forward * distance;
        }

        // ------------------------------------------------------------------
        // 擁有者端：出局後看隊友
        // ------------------------------------------------------------------

        bool m_SpectateNextRequested;

        /// <summary>（出局的玩家）換下一位隊友看（測試工具用；和按空白鍵一樣）。</summary>
        public void RequestNextSpectate()
        {
            m_SpectateNextRequested = true;
        }

        /// <summary>出局後，鏡頭跟著還在場上的隊友；空白鍵換下一位。沒有隊友就看整張地圖。</summary>
        void UpdateSpectate()
        {
            PlayerCamera playerCamera = PlayerCamera.Instance;
            if (playerCamera == null)
            {
                return;
            }

            bool next = m_SpectateNextRequested
                || (Application.isFocused && m_StruggleAction != null && m_StruggleAction.WasPressedThisFrame());
            m_SpectateNextRequested = false;
            bool valid = m_Spectating != null && m_Spectating.IsSpawned && IsWatchable(m_Spectating);
            if (valid && !next && playerCamera.HasTarget)
            {
                return;
            }

            PlayerAvatar pick = NextWatchable(valid ? m_Spectating : null);
            if (pick != null && pick == m_Spectating && playerCamera.HasTarget)
            {
                return;
            }

            StopSpectating(false);
            if (pick != null)
            {
                m_Spectating = pick;
                playerCamera.SetTarget(pick.transform, pick.AvatarRole.Value);
            }
            else
            {
                // 沒有隊友可以看：回到俯瞰整張地圖。
                playerCamera.ClearTarget(transform);
            }
        }

        /// <summary>不再看隊友（把那位隊友的身體顯示回來）。toIdle 為 true 時鏡頭回到俯瞰整張地圖。</summary>
        void StopSpectating(bool toIdle)
        {
            PlayerCamera playerCamera = PlayerCamera.Instance;
            if (m_Spectating != null)
            {
                m_Spectating.SetBodyHidden(false);
                if (playerCamera != null)
                {
                    playerCamera.ClearTarget(m_Spectating.transform);
                }
            }

            m_Spectating = null;
            if (toIdle && playerCamera != null)
            {
                playerCamera.ClearTarget(transform);
            }
        }

        bool IsWatchable(PlayerAvatar avatar)
        {
            return avatar != null && avatar != m_Avatar && avatar.IsSpawned && Roles.IsAnimal(avatar.AvatarRole.Value)
                && avatar.Capture != null && avatar.Capture.State.Value != CaptureState.Out;
        }

        /// <summary>照玩家編號排，找 current 的下一位可以看的隊友（繞一圈）。</summary>
        PlayerAvatar NextWatchable(PlayerAvatar current)
        {
            PlayerAvatar first = null;
            PlayerAvatar after = null;
            int currentNumber = current != null ? current.PlayerNumber : -1;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (!IsWatchable(avatar))
                {
                    continue;
                }

                int number = avatar.PlayerNumber;
                if (first == null || number < first.PlayerNumber)
                {
                    first = avatar;
                }

                if (number > currentNumber && (after == null || number < after.PlayerNumber))
                {
                    after = avatar;
                }
            }

            return after ?? first;
        }

        void ServerBreakFree(bool byBump)
        {
            GameConfig config = GameConfig.Instance;
            PlayerCapture carrier = Carrier;
            if (State.Value == CaptureState.Carried && carrier != null)
            {
                // 人類會踉蹌一下，動物才有時間跑掉。落地的位置優先選人類背後。
                carrier.StaggerUntil.Value = Now + (byBump ? config.staggerSecondsOnBump : config.staggerSecondsOnEscape);
                ServerRelease(CaptureState.Free, carrier.FindDropPoint(this, true), true);
            }
            else
            {
                ServerRelease(CaptureState.Free, transform.position, true);
            }
        }

        /// <summary>主機端：讓這隻動物接下來幾秒內網子抓不到（撬開收容區的門時用）。已經有更久的保護就不縮短。</summary>
        public void ServerProtect(float seconds)
        {
            if (IsServer && IsSpawned)
            {
                double until = Now + seconds;
                if (until > ProtectedUntil.Value)
                {
                    ProtectedUntil.Value = until;
                }
            }
        }

        /// <summary>
        /// 主機端：結束「被扛著」或「被網住」。newState 是 Free（恢復自由）或 Netted（被放回地上，還在網子裡）。
        /// drop 是落地的位置，只有原本被扛著時才會用到。
        /// </summary>
        void ServerRelease(CaptureState newState, Vector3 drop, bool protect, bool freeCarrierHands = true)
        {
            // freeCarrierHands = false：扛我的人自己正在消失，不用（也不該）再去改他的狀態。
            PlayerCapture carrier = freeCarrierHands ? Carrier : null;
            if (carrier != null && carrier.IsSpawned && carrier.CarryingId.Value == NetworkObjectId)
            {
                carrier.CarryingId.Value = None;
            }

            // 先寫落地位置，再改狀態（見 DropPoint 宣告處的說明）。
            DropPoint.Value = drop;
            CarrierId.Value = None;

            if (newState == CaptureState.Free)
            {
                m_StruggleExact = 0f;
                Struggle.Value = 0f;
                if (protect)
                {
                    ProtectedUntil.Value = Now + GameConfig.Instance.breakFreeProtectionSeconds;
                }
            }

            State.Value = newState;
        }

        /// <summary>
        /// （人類身上呼叫）在自己周圍找一個放得下這隻動物的空位。
        /// behindFirst：先找背後（動物掙脫時，落在人類背後比較跑得掉）；否則先找正前方（人類自己放下）。
        /// 都沒有空位就放在腳邊。
        /// </summary>
        Vector3 FindDropPoint(PlayerCapture animal, bool behindFirst)
        {
            RoleSettings mine = m_Avatar.Settings;
            RoleSettings theirs = animal.m_Avatar.Settings;
            float radius = theirs.radius;
            float height = Mathf.Max(theirs.height, radius * 2f);
            float distance = mine.radius + radius + 0.2f;
            Vector3 origin = GroundPointBelow(transform.position);
            float baseYaw = transform.eulerAngles.y + (behindFirst ? 180f : 0f);

            // 會游泳的動物（烏龜）可以落在水裡；其他動物要落在岸上（水域的擋牆也算障礙）。
            bool swims = GameConfig.Instance.CanEnterWater(animal.m_Avatar.AvatarRole.Value);
            int mask = swims ? GameLayers.NetMask : Physics.DefaultRaycastLayers;

            float[] offsets = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, baseYaw + offsets[i], 0f) * Vector3.forward;
                Vector3 point = origin + direction * distance;
                if (IsSpaceFree(point, radius, height, mask))
                {
                    return point;
                }
            }

            // 人類涉水走到水中央時，四周都是水：不會游泳的動物改落在最近的岸邊。
            Vector3 shore;
            if (!swims && WaterZone.TryFindShore(origin, radius + 0.15f,
                    candidate => IsSpaceFree(candidate, radius, height, mask), out shore))
            {
                return shore;
            }

            return origin;
        }

        static bool IsSpaceFree(Vector3 point, float radius, float height, int mask)
        {
            Vector3 bottom = point + Vector3.up * (radius + 0.08f);
            Vector3 top = point + Vector3.up * Mathf.Max(radius + 0.08f, height - radius);
            return !Physics.CheckCapsule(bottom, top, radius * 0.95f, mask, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------
        // 所有電腦：狀態改變時的處理、跟著人類、外觀
        // ------------------------------------------------------------------

        void OnStateChanged(CaptureState previous, CaptureState current)
        {
            if (m_Controller != null)
            {
                if (current == CaptureState.Carried)
                {
                    // 被扛著時不需要碰撞，位置由 LateUpdate 直接擺到人類肩上。
                    m_Controller.enabled = false;
                }
                else if (previous == CaptureState.Carried || (previous == CaptureState.Caged && current == CaptureState.Free))
                {
                    // 兩種情況要搬到主機指定的位置（DropPoint）：從肩上下來（放下、掙脫、關進籠子、出局），
                    // 和被救出籠子（搬到籠子外面）。
                    // 被扛著時是「躺在肩上」的姿勢（SnapToCarrier 在每台電腦各自擺的），放下來要扶正。
                    // 躺著時身體的「上」指向人類的背後，所以反方向就是人類面對的方向，落地後就朝那邊站好。
                    Vector3 facing = previous == CaptureState.Carried ? -transform.up : transform.forward;
                    facing.y = 0f;
                    Quaternion upright = facing.sqrMagnitude > 0.001f
                        ? Quaternion.LookRotation(facing.normalized, Vector3.up)
                        : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

                    // CharacterController 會記住自己的位置：搬動之前一定要先停用（從籠子裡救出來時它原本是開著的，
                    // 沒停用的話下一次移動會被拉回籠子裡；2026-11-17 實測）。
                    m_Controller.enabled = false;

                    if (IsOwner)
                    {
                        // 位置是擁有者說了算，所以由我把自己搬到主機指定的落地位置（不經過插值，直接到位）。
                        if (m_NetworkTransform != null)
                        {
                            m_NetworkTransform.Teleport(DropPoint.Value, upright, transform.localScale);
                        }
                        else
                        {
                            transform.SetPositionAndRotation(DropPoint.Value, upright);
                        }

                        if (m_Movement != null)
                        {
                            m_Movement.ResetVerticalSpeed();
                        }
                    }
                    else
                    {
                        // 別人的電腦：位置等擁有者同步過來就好，但姿勢要自己扶正。位置同步只管水平轉向（Y 軸），
                        // 前後左右的傾斜它不會改回來，不扶正的話這隻動物在別人的畫面上會一直躺著
                        // （2026-11-15 使用者在試玩影片裡發現：人類畫面裡的兔子一開始就是躺著的）。
                        transform.rotation = upright;
                    }

                    m_Controller.enabled = current != CaptureState.Out;
                }
                else if (current == CaptureState.Out)
                {
                    m_Controller.enabled = false;
                }
            }

            // 出局了就不再是自己鏡頭的主角；回到場上（重選身分會產生新的角色）時由新角色接手鏡頭。
            if (IsOwner && previous == CaptureState.Out && current != CaptureState.Out)
            {
                StopSpectating(true);
            }

            RefreshVisuals();
        }

        void LateUpdate()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (State.Value == CaptureState.Carried)
            {
                SnapToCarrier();
            }

            AnimateNetTool();
        }

        /// <summary>被扛著：直接擺到人類的右肩後面，臉朝上躺著。每台電腦各自擺，不等位置同步。</summary>
        void SnapToCarrier()
        {
            PlayerCapture carrier = Carrier;
            if (carrier == null || carrier.m_Avatar == null)
            {
                return;
            }

            Transform body = carrier.transform;
            float carrierHeight = carrier.m_Avatar.Settings.height;
            var shoulder = new Vector3(0.38f, carrierHeight * 0.75f, -0.25f);
            transform.SetPositionAndRotation(body.TransformPoint(shoulder), body.rotation * Quaternion.Euler(-90f, 0f, 0f));
        }

        /// <summary>網子罩（罩在被網住的動物身上）的大小跟著身分走。</summary>
        void FitNetCover()
        {
            if (m_NetCover == null || m_Avatar == null)
            {
                return;
            }

            RoleSettings settings = m_Avatar.Settings;
            float height = Mathf.Max(settings.height, settings.radius * 2f);
            float diameter = height * 1.25f + 0.25f;
            m_NetCover.localPosition = new Vector3(0f, height * 0.5f, 0f);
            m_NetCover.localScale = new Vector3(diameter, diameter, diameter);
        }

        void RefreshVisuals()
        {
            if (m_NetCover != null)
            {
                m_NetCover.gameObject.SetActive(IsHeld);
            }
        }

        const float k_RestPitch = -55f;     // 平常舉著
        const float k_LoweredPitch = -12f;  // 冷卻中垂下來（動物看得出現在不能揮）
        const float k_RaisedPitch = -85f;   // 揮之前先舉高
        const float k_DownPitch = 42f;      // 揮到底

        /// <summary>人類手上的網子：平常舉著、揮的時候由上往下、冷卻中垂著、扛著動物時收起來。</summary>
        void AnimateNetTool()
        {
            if (m_NetTool == null)
            {
                return;
            }

            bool show = IsHuman && !IsCarrying;
            if (m_NetTool.gameObject.activeSelf != show)
            {
                m_NetTool.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            float yaw = 18f;
            float elapsed = (float)(Now - SwingStartedAt.Value);
            if (elapsed >= 0f && elapsed < k_SwingAnimationSeconds)
            {
                // 前 20% 舉高，接著揮到底（大約就是主機判定打中的時間），最後停一下。
                float t = elapsed / k_SwingAnimationSeconds;
                if (t < 0.2f)
                {
                    m_NetToolPitch = Mathf.Lerp(k_RestPitch, k_RaisedPitch, t / 0.2f);
                }
                else if (t < 0.5f)
                {
                    float swing = (t - 0.2f) / 0.3f;
                    m_NetToolPitch = Mathf.Lerp(k_RaisedPitch, k_DownPitch, swing * swing);
                    yaw = Mathf.Lerp(18f, -8f, swing);
                }
                else
                {
                    m_NetToolPitch = k_DownPitch;
                    yaw = -8f;
                }
            }
            else
            {
                float target = NetCooldownRemaining > 0f || IsStaggered ? k_LoweredPitch : k_RestPitch;
                m_NetToolPitch = Mathf.MoveTowards(m_NetToolPitch, target, 360f * Time.deltaTime);
            }

            m_NetTool.localRotation = Quaternion.Euler(m_NetToolPitch, yaw, 0f);
        }

        // ------------------------------------------------------------------
        // 輸入
        // ------------------------------------------------------------------

        void CreateInput()
        {
            DisposeInput();

            m_MouseUseAction = new InputAction("Use (Mouse)", InputActionType.Button);
            m_MouseUseAction.AddBinding("<Mouse>/leftButton");

            m_PadUseAction = new InputAction("Use (Gamepad)", InputActionType.Button);
            m_PadUseAction.AddBinding("<Gamepad>/rightTrigger");

            m_InteractAction = new InputAction("Interact", InputActionType.Button);
            m_InteractAction.AddBinding("<Keyboard>/e");
            m_InteractAction.AddBinding("<Gamepad>/buttonWest");

            m_StruggleAction = new InputAction("Struggle", InputActionType.Button);
            m_StruggleAction.AddBinding("<Keyboard>/space");
            m_StruggleAction.AddBinding("<Gamepad>/buttonSouth");

            m_MouseUseAction.Enable();
            m_PadUseAction.Enable();
            m_InteractAction.Enable();
            m_StruggleAction.Enable();
        }

        void DisposeInput()
        {
            DisposeAction(ref m_MouseUseAction);
            DisposeAction(ref m_PadUseAction);
            DisposeAction(ref m_InteractAction);
            DisposeAction(ref m_StruggleAction);
        }

        static void DisposeAction(ref InputAction action)
        {
            if (action != null)
            {
                action.Disable();
                action.Dispose();
                action = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Local = null;
        }
    }
}
