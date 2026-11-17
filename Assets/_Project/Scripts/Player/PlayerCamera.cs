using AnimalEscape.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 玩家的鏡頭。每台電腦只有一台，只跟著自己的角色；滑鼠（或手把右搖桿）轉視角。
    /// 視角依身分而不同（GameConfig 的 humanViewMode、animalViewMode）：
    /// - 人類預設第一人稱：鏡頭在眼睛的位置，看不到自己的身體（影子還在），身體會跟著視線轉。視野受限，動物才躲得掉。
    /// - 動物預設第三人稱：鏡頭跟在角色後上方，碰到牆會自動拉近。看得到自己，也能越過障礙物觀察四周。
    /// 眼睛高度、鏡頭距離都依角色身分而不同。
    /// </summary>
    public class PlayerCamera : MonoBehaviour
    {
        public static PlayerCamera Instance { get; private set; }

        public Camera Camera { get; private set; }

        // 第三人稱：鏡頭離角色比這個距離近時，藏起自己的身體；離得比第二個距離遠才顯示回來。
        const float k_HideBodyDistance = 0.9f;
        const float k_ShowBodyDistance = 1.2f;

        // 自動跟隨時，鏡頭每秒最多轉幾度
        const float k_AutoFollowMaxSpeed = 220f;

        static readonly RaycastHit[] s_CameraHits = new RaycastHit[16];

        // LookTo 的轉動進度
        float m_TurnFromYaw;
        float m_TurnFromPitch;
        float m_TurnToYaw;
        float m_TurnToPitch;
        float m_TurnDuration;
        float m_TurnTime;
        Vector3 m_LastTargetPosition;

        Transform m_Target;
        PlayerAvatar m_TargetAvatar;
        PlayerMovement m_TargetMovement;
        Role m_TargetRole;
        float m_Distance = 5f;
        float m_PivotHeight = 1.5f;
        float m_EyeHeight = 1.6f;
        float m_Yaw;
        float m_Pitch;
        int m_SkipLookFrames;
        Vector3 m_IdlePosition;
        Quaternion m_IdleRotation;
        float m_IdleFieldOfView = 60f;

        public bool HasTarget
        {
            get { return m_Target != null; }
        }

        /// <summary>目前跟著的角色是不是用第一人稱視角（依身分，由 GameConfig 決定）。</summary>
        public bool IsFirstPerson
        {
            get { return m_Target != null && GameConfig.Instance.GetViewMode(m_TargetRole) == ViewMode.FirstPerson; }
        }

        /// <summary>鏡頭在水平面上的前方（角色移動以這個方向為準）。</summary>
        public Vector3 PlanarForward
        {
            get { return Quaternion.Euler(0f, m_Yaw, 0f) * Vector3.forward; }
        }

        public Vector3 PlanarRight
        {
            get { return Quaternion.Euler(0f, m_Yaw, 0f) * Vector3.right; }
        }

        /// <summary>鏡頭目前的水平角度（0 朝北、90 朝東）。</summary>
        public float Yaw
        {
            get { return m_Yaw; }
        }

        /// <summary>鏡頭目前往下看的角度（負的是往上看）。</summary>
        public float Pitch
        {
            get { return m_Pitch; }
        }

        /// <summary>
        /// 打開後，角色移動時鏡頭會自己慢慢轉到角色前進的方向（預設關閉，由玩家用滑鼠控制）。
        /// 只在「用程式控制角色移動」的時候才會作用（測試、錄影）；玩家自己用鍵盤操作時不會轉，
        /// 不然第一人稱下往旁邊走，鏡頭會跟著一直轉。
        /// </summary>
        public bool AutoFollow { get; set; }

        void Awake()
        {
            Instance = this;
            Camera = GetComponent<Camera>();
            m_IdlePosition = transform.position;
            m_IdleRotation = transform.rotation;
            m_Yaw = transform.eulerAngles.y;
            if (Camera != null)
            {
                m_IdleFieldOfView = Camera.fieldOfView;
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>開始跟著某個角色。視角、眼睛高度和鏡頭距離照這個身分的設定。</summary>
        public void SetTarget(Transform target, Role role)
        {
            RoleSettings settings = GameConfig.Instance.GetRole(role);
            m_Target = target;
            m_TargetRole = role;
            m_TargetAvatar = target.GetComponent<PlayerAvatar>();
            m_TargetMovement = target.GetComponent<PlayerMovement>();
            m_Distance = Mathf.Max(1f, settings.cameraDistance);
            m_PivotHeight = Mathf.Max(0.45f, settings.height * 0.85f);
            m_EyeHeight = Mathf.Max(0.2f, settings.height * 0.9f);
            m_Yaw = target.eulerAngles.y;
            m_Pitch = IsFirstPerson ? 0f : 15f;
            m_TurnDuration = 0f;
            m_LastTargetPosition = target.position;
            SetCursorLocked(true);
        }

        /// <summary>直接設定鏡頭方向（yaw 是水平角度，0 朝北、90 朝東；pitch 是往下看的角度）。</summary>
        public void SetLook(float yaw, float pitch)
        {
            m_Yaw = yaw;
            m_Pitch = ClampPitch(pitch);
            m_TurnDuration = 0f;
        }

        /// <summary>在指定的秒數內，平順地把鏡頭轉到指定方向。</summary>
        public void LookTo(float yaw, float pitch, float seconds)
        {
            if (seconds <= 0.01f)
            {
                SetLook(yaw, pitch);
                return;
            }

            m_TurnFromYaw = m_Yaw;
            m_TurnFromPitch = m_Pitch;
            m_TurnToYaw = m_Yaw + Mathf.DeltaAngle(m_Yaw, yaw);
            m_TurnToPitch = ClampPitch(pitch);
            m_TurnDuration = seconds;
            m_TurnTime = 0f;
        }

        public void ClearTarget(Transform target)
        {
            if (m_Target != target)
            {
                return;
            }

            m_Target = null;
            m_TargetAvatar = null;
            m_TargetMovement = null;
            SetCursorLocked(false);

            // 沒有角色時（大廳、選身分），回到俯瞰整張地圖的位置。
            transform.SetPositionAndRotation(m_IdlePosition, m_IdleRotation);
            if (Camera != null)
            {
                Camera.fieldOfView = m_IdleFieldOfView;
            }
        }

        float ClampPitch(float pitch)
        {
            var config = GameConfig.Instance;
            return IsFirstPerson
                ? Mathf.Clamp(pitch, config.firstPersonMinPitch, config.firstPersonMaxPitch)
                : Mathf.Clamp(pitch, config.minPitch, config.maxPitch);
        }

        void LateUpdate()
        {
            if (m_Target == null)
            {
                return;
            }

            var config = GameConfig.Instance;
            UpdateCursorLock();

            // 視窗不在最前面時不讀取輸入：同一台電腦開多個視窗測試時，只有正在操作的那個視窗會轉視角。
            Vector2 look = Vector2.zero;
            if (Application.isFocused)
            {
                var mouse = Mouse.current;
                if (m_SkipLookFrames > 0)
                {
                    // 剛鎖定滑鼠時游標會被移到畫面中央，這幾格的位移不是玩家的操作，略過以免視角突然跳一下。
                    m_SkipLookFrames--;
                }
                else if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                {
                    look += mouse.delta.ReadValue() * config.mouseSensitivity;
                }

                var gamepad = Gamepad.current;
                if (gamepad != null)
                {
                    look += gamepad.rightStick.ReadValue() * (config.gamepadLookSpeed * Time.deltaTime);
                }
            }

            UpdateAssistedLook(Time.deltaTime);

            m_Yaw += look.x;
            m_Pitch = ClampPitch(m_Pitch - look.y);

            Quaternion rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);

            if (IsFirstPerson)
            {
                // 第一人稱：鏡頭就在眼睛的位置。自己的身體一律藏起來（只留影子），不然會擋住整個畫面。
                if (Camera != null)
                {
                    Camera.fieldOfView = config.firstPersonFieldOfView;
                }

                if (m_TargetAvatar != null)
                {
                    m_TargetAvatar.SetBodyHidden(true);
                }

                // 涉水時身體往下沉了一截，眼睛的高度跟著低一點（走進水裡看得出來）。
                float eyeHeight = m_EyeHeight - (m_TargetAvatar != null ? m_TargetAvatar.Sink : 0f);
                transform.SetPositionAndRotation(m_Target.position + Vector3.up * eyeHeight, rotation);
                return;
            }

            if (Camera != null)
            {
                Camera.fieldOfView = config.thirdPersonFieldOfView;
            }

            Vector3 pivot = m_Target.position + Vector3.up * m_PivotHeight;
            Vector3 backward = rotation * Vector3.back;

            // 鏡頭和角色之間有牆或天花板時，把鏡頭拉近，避免穿牆。其他角色不算障礙，不然有人站在背後鏡頭就會突然貼上來。
            float distance = m_Distance;
            int hitCount = Physics.SphereCastNonAlloc(pivot, 0.15f, backward, s_CameraHits, m_Distance,
                GameLayers.SightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = s_CameraHits[i];

                // 起點就已經碰到的東西（距離 0、沒有接觸點）不算，和原本單一 SphereCast 的行為一致。
                if (hit.distance <= 0f && hit.point == Vector3.zero)
                {
                    continue;
                }

                if (hit.distance < distance && hit.collider.GetComponentInParent<PlayerAvatar>() == null)
                {
                    distance = hit.distance;
                }
            }

            if (distance < m_Distance)
            {
                distance = Mathf.Max(distance, 0.4f);
            }

            // 鏡頭被擠到貼近角色時（例如在通風管裡），把自己的身體藏起來，才看得到前面。
            // 藏和顯示用不同的距離，避免在邊界來回閃爍。
            if (m_TargetAvatar != null)
            {
                if (distance < k_HideBodyDistance)
                {
                    m_TargetAvatar.SetBodyHidden(true);
                }
                else if (distance > k_ShowBodyDistance)
                {
                    m_TargetAvatar.SetBodyHidden(false);
                }
            }

            transform.SetPositionAndRotation(pivot + backward * distance, rotation);
        }

        /// <summary>程式輔助的鏡頭轉動：LookTo 的平順轉向，以及自動跟隨角色前進方向。</summary>
        void UpdateAssistedLook(float deltaTime)
        {
            Vector3 moved = m_Target.position - m_LastTargetPosition;
            m_LastTargetPosition = m_Target.position;
            moved.y = 0f;

            if (m_TurnDuration > 0f)
            {
                m_TurnTime += deltaTime;
                float t = Mathf.Clamp01(m_TurnTime / m_TurnDuration);
                float eased = t * t * (3f - 2f * t);
                m_Yaw = Mathf.Lerp(m_TurnFromYaw, m_TurnToYaw, eased);
                m_Pitch = Mathf.Lerp(m_TurnFromPitch, m_TurnToPitch, eased);
                if (t >= 1f)
                {
                    m_TurnDuration = 0f;
                }

                return;
            }

            // 只有角色正被程式控制、而且有在前進（每秒超過 1 公尺）才跟；差得越多轉得越快，接近時慢慢停下來。
            bool scripted = m_TargetMovement != null && m_TargetMovement.HasExternalInput;
            if (AutoFollow && scripted && deltaTime > 0f && moved.magnitude > deltaTime)
            {
                float heading = Mathf.Atan2(moved.x, moved.z) * Mathf.Rad2Deg;
                float difference = Mathf.DeltaAngle(m_Yaw, heading);
                float step = Mathf.Min(Mathf.Abs(difference) * 4f, k_AutoFollowMaxSpeed) * deltaTime;
                m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, heading, step);
            }
        }

        void UpdateCursorLock()
        {
            if (!Application.isFocused)
            {
                return;
            }

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetCursorLocked(false);
            }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame
                     && Cursor.lockState != CursorLockMode.Locked && !InputGate.PointerOverUi)
            {
                SetCursorLocked(true);
            }
        }

        void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            if (locked)
            {
                m_SkipLookFrames = 3;
            }
        }
    }
}
