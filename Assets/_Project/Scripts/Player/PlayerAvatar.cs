using System.Collections.Generic;
using AnimalEscape.Core;
using Unity.Netcode;
using UnityEngine;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 玩家在場上的角色（人類或某一種動物）。
    /// 依身分套用碰撞大小，並顯示對應的外觀。外觀是預製物裡名稱為「Visual_身分」的子物件，
    /// 平常全部關閉，只打開符合身分的那一個。
    /// 注意：這個元件在預製物上要排在 PlayerMovement 前面，移動和鏡頭才拿得到正確的身分。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerAvatar : NetworkBehaviour
    {
        public const string VisualPrefix = "Visual_";

        /// <summary>目前場上所有角色（每台電腦各自維護）。</summary>
        public static readonly List<PlayerAvatar> All = new List<PlayerAvatar>();

        /// <summary>主機在產生角色前先填好，產生時會寫進 AvatarRole 同步給所有人。</summary>
        [System.NonSerialized] public Role PendingRole = Role.None;

        public readonly NetworkVariable<Role> AvatarRole = new NetworkVariable<Role>(Role.None);

        /// <summary>同一個角色身上的捕捉狀態（被網住、被扛著、正扛著誰…）。</summary>
        public PlayerCapture Capture { get; private set; }

        /// <summary>同一個角色身上的 M4 按 E 動作（破壞、控制台、開出口）。</summary>
        public PlayerTask Task { get; private set; }

        CharacterController m_Controller;
        Renderer[] m_BodyRenderers = new Renderer[0];
        bool m_BodyHidden;
        Transform m_Visual;

        /// <summary>這個角色現在是不是在水裡（每台電腦各自用牠的位置判斷，被扛著的不算）。</summary>
        public bool InWater { get; private set; }

        /// <summary>在水裡時外觀往下沉了多少（公尺）。第一人稱的鏡頭也會跟著低一點。</summary>
        public float Sink { get; private set; }

        /// <summary>目前身分的外觀（Visual_身分）。表情動作（PlayerParty）會在它上面加一點位移和旋轉。</summary>
        public Transform Visual
        {
            get { return m_Visual; }
        }

        public RoleSettings Settings
        {
            get { return GameConfig.Instance.GetRole(AvatarRole.Value); }
        }

        /// <summary>顯示用的玩家編號（P1、P2…），和這位玩家的玩家狀態（PlayerSlot）一致。</summary>
        public int PlayerNumber
        {
            get
            {
                PlayerSlot slot = PlayerSlot.FindByClient(OwnerClientId);
                return slot != null ? slot.PlayerNumber : (int)OwnerClientId + 1;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this))
            {
                All.Add(this);
            }

            m_Controller = GetComponent<CharacterController>();
            Capture = GetComponent<PlayerCapture>();
            Task = GetComponent<PlayerTask>();

            // 聲音（腳步聲、鈴鐺、叫聲）在生成時才加上，不放在預製物裡：每台電腦各自處理，不需要連線。
            if (GetComponent<PlayerSounds>() == null)
            {
                gameObject.AddComponent<PlayerSounds>();
            }

            if (IsServer && PendingRole != Role.None)
            {
                AvatarRole.Value = PendingRole;
            }

            AvatarRole.OnValueChanged += OnRoleChanged;
            ApplyRole(AvatarRole.Value);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            AvatarRole.OnValueChanged -= OnRoleChanged;
        }

        void OnRoleChanged(Role previous, Role current)
        {
            ApplyRole(current);
        }

        void ApplyRole(Role role)
        {
            RoleSettings settings = GameConfig.Instance.GetRole(role);

            // CharacterController 會自己記住位置。先停用、設定好再啟用，它才會用物件現在的位置
            // （Netcode 把角色放到出生點之後，如果沒有這樣做，第一次移動會被拉回原點）。
            float height = Mathf.Max(settings.height, settings.radius * 2f);
            m_Controller.enabled = false;
            m_Controller.stepOffset = 0.05f;
            m_Controller.height = height;
            m_Controller.radius = settings.radius;
            m_Controller.center = new Vector3(0f, height * 0.5f, 0f);
            m_Controller.skinWidth = Mathf.Max(0.01f, settings.radius * 0.08f);
            m_Controller.stepOffset = Mathf.Min(0.3f, height * 0.35f);

            // 被扛著的角色不需要碰撞（位置由 PlayerCapture 直接擺到人類肩上）。其他情況都要打開（在實驗室裡也要）。
            m_Controller.enabled = Capture == null || Capture.State.Value != CaptureState.Carried;

            // 會游泳的身分放在 Swimmer 圖層（不被水域的擋牆擋住）。擁有者端之後由 PlayerMovement 每一格維護。
            gameObject.layer = GameConfig.Instance.CanEnterWater(role) ? GameLayers.Swimmer : 0;

            // 只顯示符合身分的外觀
            string wanted = VisualPrefix + role;
            m_BodyRenderers = new Renderer[0];
            m_Visual = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith(VisualPrefix))
                {
                    bool active = child.name == wanted;
                    child.localPosition = Vector3.zero;
                    child.gameObject.SetActive(active);
                    if (active)
                    {
                        m_Visual = child;
                        m_BodyRenderers = child.GetComponentsInChildren<Renderer>(true);
                    }
                }
            }

            Sink = 0f;
            ApplyBodyHidden();
        }

        void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            // 在水裡時把外觀往下沉一截（下半身沒入地板底下），看起來像泡在水裡。只是外觀，碰撞和位置不變。
            bool carried = Capture != null && Capture.State.Value == CaptureState.Carried;
            InWater = !carried && WaterZone.IsInWater(transform.position);
            float target = InWater ? Mathf.Min(GameConfig.Instance.waterSinkDepth, Settings.height * 0.6f) : 0f;
            Sink = carried ? 0f : Mathf.MoveTowards(Sink, target, 1.5f * Time.deltaTime);
            if (m_Visual != null)
            {
                m_Visual.localPosition = new Vector3(0f, -Sink, 0f);
            }
        }

        /// <summary>這台電腦上，這個角色的身體目前是不是藏起來的。</summary>
        public bool IsBodyHidden
        {
            get { return m_BodyHidden; }
        }

        /// <summary>
        /// 把身體藏起來（只留影子）或顯示回來。只影響這台電腦的畫面：
        /// 鏡頭被牆壁或通風管擠到貼近角色時，藏起自己的身體才看得到前面。
        /// </summary>
        public void SetBodyHidden(bool hidden)
        {
            if (m_BodyHidden == hidden)
            {
                return;
            }

            m_BodyHidden = hidden;
            ApplyBodyHidden();
        }

        void ApplyBodyHidden()
        {
            var mode = m_BodyHidden
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.On;

            for (int i = 0; i < m_BodyRenderers.Length; i++)
            {
                if (m_BodyRenderers[i] != null)
                {
                    m_BodyRenderers[i].shadowCastingMode = mode;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
        }
    }
}
