using AnimalEscape.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 角色移動。由「擁有這個角色的玩家」在自己電腦上移動（手感才不會延遲），
    /// 位置再透過 NetworkTransform 同步給其他人。之後的捕捉、救援等判定則交給主機。
    /// 速度和跳躍高度依身分而不同，數值在 GameConfig。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerAvatar))]
    public class PlayerMovement : NetworkBehaviour
    {
        CharacterController m_Controller;
        PlayerAvatar m_Avatar;
        PlayerCapture m_Capture;
        InputAction m_MoveAction;
        InputAction m_ArrowForwardAction;
        InputAction m_ArrowTurnAction;
        InputAction m_JumpAction;
        InputAction m_SneakAction;
        float m_VerticalSpeed;

        // 測試工具用：模擬按住 ←→（轉鏡頭）和 ↑↓（前後），走和鍵盤同一段程式，只差沒有讀鍵盤。
        bool m_HasExternalTurn;
        float m_ExternalTurn;
        float m_ExternalForward;

        // 外部控制：測試工具（之後也可以給電腦玩家）用程式指定移動方向，取代鍵盤輸入。
        bool m_HasExternalInput;
        Vector3 m_ExternalMove;
        bool m_ExternalSneak;
        bool m_JumpRequested;

        /// <summary>這個角色現在是不是由程式控制移動（而不是鍵盤）。</summary>
        public bool HasExternalInput
        {
            get { return m_HasExternalInput; }
        }

        /// <summary>
        /// 用程式控制這個角色往指定的方向移動（世界座標，長度 0～1 代表推多少，1 是全速），不讀鍵盤。
        /// 只有擁有這個角色的那台電腦呼叫才有效。要恢復鍵盤操作請呼叫 ClearExternalInput。
        /// </summary>
        public void SetExternalInput(Vector3 worldDirection, bool sneak)
        {
            worldDirection.y = 0f;
            m_ExternalMove = Vector3.ClampMagnitude(worldDirection, 1f);
            m_ExternalSneak = sneak;
            m_HasExternalInput = true;
        }

        public void ClearExternalInput()
        {
            m_HasExternalInput = false;
            m_ExternalMove = Vector3.zero;
            m_ExternalSneak = false;
        }

        /// <summary>
        /// 用程式模擬「按住 ←→ 轉鏡頭、↑↓ 前後走」（turn：−1 左、+1 右；forward：−1 後退、+1 前進），測試工具用。
        /// 和鍵盤走同一段程式（GameConfig.arrowKeyTurning 關掉時，←→ 是橫著走）。呼叫 ClearExternalTurn 放開。
        /// </summary>
        public void SetExternalTurn(float turn, float forward)
        {
            m_ExternalTurn = Mathf.Clamp(turn, -1f, 1f);
            m_ExternalForward = Mathf.Clamp(forward, -1f, 1f);
            m_HasExternalTurn = true;
        }

        public void ClearExternalTurn()
        {
            m_HasExternalTurn = false;
            m_ExternalTurn = 0f;
            m_ExternalForward = 0f;
        }

        /// <summary>要求跳一次（站在地上才跳得起來）。只有擁有這個角色的那台電腦呼叫才有效。</summary>
        public void Jump()
        {
            m_JumpRequested = true;
        }

        /// <summary>
        /// 這一格「想要」的水平速度（公尺／秒，世界座標），撞到東西之前的數字。推撞的判定用它，
        /// 因為撞上去的那一格實際位移已經被擋掉，量實際速度會量到 0。只有擁有者端有值。
        /// </summary>
        public Vector3 PlanarVelocity { get; private set; }

        /// <summary>PlanarVelocity 的大小。</summary>
        public float PlanarSpeed
        {
            get { return PlanarVelocity.magnitude; }
        }

        /// <summary>
        /// （擁有者端）不會游泳的動物現在人在水裡，正在爬上岸。正常情況進不了水，只有被放進去才會發生
        /// （例如在水中央從人類肩上掙脫、四周又沒有岸可以落地）。
        /// </summary>
        public bool IsStranded { get; private set; }

        /// <summary>把上下方向的速度歸零（被別的程式搬到新位置之後呼叫，才不會帶著舊的掉落速度）。</summary>
        public void ResetVerticalSpeed()
        {
            m_VerticalSpeed = 0f;
        }

        public override void OnNetworkSpawn()
        {
            m_Controller = GetComponent<CharacterController>();
            m_Avatar = GetComponent<PlayerAvatar>();
            m_Capture = GetComponent<PlayerCapture>();

            if (!IsOwner)
            {
                return;
            }

            CreateInput();

            if (PlayerCamera.Instance != null)
            {
                PlayerCamera.Instance.SetTarget(transform, m_Avatar.AvatarRole.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            DisposeInput();
            if (PlayerCamera.Instance != null)
            {
                PlayerCamera.Instance.ClearTarget(transform);
            }
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || m_MoveAction == null || m_Controller == null || !m_Controller.enabled)
            {
                // 被扛著的時候 CharacterController 是關掉的，位置由 PlayerCapture 直接擺。
                PlanarVelocity = Vector3.zero;
                return;
            }

            var config = GameConfig.Instance;
            RoleSettings settings = m_Avatar.Settings;
            float deltaTime = Time.deltaTime;

            // 視窗不在最前面時不讀取輸入：同一台電腦開多個視窗測試時，只有正在操作的那個視窗的角色會動。
            bool focused = Application.isFocused;

            // 以鏡頭方向為準的移動方向：WASD（手把左搖桿）往各方向走，A／D 是橫著走。
            // 方向鍵：↑↓ 前後走；←→ 預設是轉鏡頭（GameConfig.arrowKeyTurning，照《黎明死線》），關掉就和 A／D 一樣橫著走。
            var playerCamera = PlayerCamera.Instance;
            Vector2 input = Vector2.zero;
            float arrowTurn = 0f;
            float arrowForward = 0f;
            if (focused)
            {
                input = m_MoveAction.ReadValue<Vector2>();
                arrowTurn = m_ArrowTurnAction.ReadValue<float>();
                arrowForward = m_ArrowForwardAction.ReadValue<float>();
            }

            if (m_HasExternalTurn)
            {
                arrowTurn = m_ExternalTurn;
                arrowForward = m_ExternalForward;
            }

            float turn = 0f;
            input.y += arrowForward;
            if (config.arrowKeyTurning)
            {
                turn = arrowTurn;
            }
            else
            {
                input.x += arrowTurn;
            }

            input = Vector2.ClampMagnitude(input, 1f);
            if (playerCamera != null && turn != 0f)
            {
                playerCamera.AddYaw(turn * config.keyboardTurnSpeed * deltaTime);
            }

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            if (playerCamera != null)
            {
                forward = playerCamera.PlanarForward;
                right = playerCamera.PlanarRight;
            }

            Vector3 move = forward * input.y + right * input.x;
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            bool sneak = focused && m_SneakAction.IsPressed();
            bool jump = focused && m_JumpAction.WasPressedThisFrame();

            // 有外部控制時，用它指定的方向，不管鍵盤（也不管視窗是不是在最前面）。
            if (m_HasExternalInput)
            {
                move = m_ExternalMove;
                sneak = m_ExternalSneak;
            }

            if (m_JumpRequested)
            {
                jump = true;
                m_JumpRequested = false;
            }

            // 被網住時不能走也不能跳（空白鍵這時候是「掙脫」，由 PlayerCapture 處理），但還是會受重力影響。
            if (m_Capture != null && !m_Capture.CanMove)
            {
                move = Vector3.zero;
                jump = false;
            }

            // 按住 Shift 慢走；扛著動物、踉蹌時也會變慢。
            float speed = settings.moveSpeed;
            if (sneak)
            {
                speed *= config.sneakSpeedMultiplier;
            }

            if (m_Capture != null)
            {
                speed *= m_Capture.SpeedMultiplier;
            }

            speed *= UpdateWater(config);

            PlanarVelocity = move * speed;

            // 跳躍與重力
            if (m_Controller.isGrounded)
            {
                if (m_VerticalSpeed < 0f)
                {
                    m_VerticalSpeed = -2f;
                }

                if (jump)
                {
                    m_VerticalSpeed = Mathf.Sqrt(2f * settings.jumpHeight * -config.gravity);
                }
            }

            m_VerticalSpeed += config.gravity * deltaTime;

            Vector3 velocity = move * speed;
            velocity.y = m_VerticalSpeed;
            m_Controller.Move(velocity * deltaTime);
            PushOutOfOtherCharacters();

            if (playerCamera != null && playerCamera.IsFirstPerson)
            {
                // 第一人稱：身體永遠朝視線的方向，別人才看得出你在看哪裡。
                transform.rotation = Quaternion.Euler(0f, playerCamera.Yaw, 0f);
            }
            else if (move.sqrMagnitude > 0.0001f)
            {
                // 第三人稱：面向移動方向
                Quaternion target = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, config.turnSpeed * deltaTime);
            }
        }

        /// <summary>
        /// 水：回傳這一格的速度倍率，並決定自己現在會不會被水邊擋住。
        /// - 會游泳的身分（GameConfig 的水中倍率大於 0：人類、烏龜）放在 Swimmer 圖層，水域的擋牆擋不住牠；
        ///   人在水裡時速度乘上那個倍率（人類變慢、烏龜變快）。
        /// - 不會游泳的身分平常在預設圖層，被擋牆擋在岸上。萬一人已經在水裡，就暫時讓牠穿過擋牆、慢慢爬上岸；
        ///   一離開水就恢復，所以沒辦法再走回去。
        /// </summary>
        float UpdateWater(GameConfig config)
        {
            float multiplier = config.GetWaterSpeedMultiplier(m_Avatar.AvatarRole.Value);
            bool swims = multiplier > 0f;
            bool inWater = WaterZone.IsInWater(transform.position);
            IsStranded = !swims && inWater;

            int layer = swims || IsStranded ? GameLayers.Swimmer : 0;
            if (gameObject.layer != layer)
            {
                gameObject.layer = layer;
            }

            if (!inWater)
            {
                return 1f;
            }

            return swims ? multiplier : config.strandedWaterSpeedMultiplier;
        }

        const float k_MaxPushPerFrame = 0.25f;
        const float k_PushMargin = 0.04f;

        /// <summary>
        /// 別人的角色壓到我身上時，把自己推開。
        ///
        /// 為什麼需要：每個角色只在自己主人的電腦上做碰撞，別人的角色是「照網路同步的位置直接擺過來」的。
        /// 我走向站著不動的人，會被正常擋住；但如果對方也朝我走過來，他的位置是直接跳過來壓在我身上，
        /// 下一格 CharacterController 就不把他當障礙了，兩個人會互相穿過去（2026-11-15 實測）。
        /// 所以每一格移動完再檢查一次：有重疊就沿著「遠離對方」的方向退開。雙方各自這樣做，就會頂住。
        /// </summary>
        void PushOutOfOtherCharacters()
        {
            RoleSettings mine = m_Avatar.Settings;
            float myHeight = Mathf.Max(mine.height, mine.radius * 2f);
            Vector3 position = transform.position;
            Vector3 push = Vector3.zero;

            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar other = PlayerAvatar.All[i];
                if (other == null || other == m_Avatar || !other.IsSpawned)
                {
                    continue;
                }

                // 被扛著的動物沒有碰撞（牠本來就貼在人類身上）。
                if (other.Capture != null && other.Capture.State.Value == CaptureState.Carried)
                {
                    continue;
                }

                RoleSettings theirs = other.Settings;
                float theirHeight = Mathf.Max(theirs.height, theirs.radius * 2f);
                Vector3 offset = position - other.transform.position;

                // 高度沒有重疊就不算（例如一個站在另一個頭上、或一個在箱子上）。
                if (offset.y > theirHeight - 0.1f || -offset.y > myHeight - 0.1f)
                {
                    continue;
                }

                offset.y = 0f;
                float minimum = mine.radius + theirs.radius;
                float distance = offset.magnitude;
                if (distance >= minimum)
                {
                    continue;
                }

                // 完全疊在一起時沒有「遠離」的方向，就往自己背後退。
                // 多退一點點（k_PushMargin），離開 CharacterController 的接觸範圍，下一格它才會重新把對方當障礙。
                Vector3 away = distance > 0.001f ? offset / distance : -transform.forward;
                push += away * (minimum + k_PushMargin - distance);
            }

            if (push.sqrMagnitude > 0.000001f)
            {
                m_Controller.Move(Vector3.ClampMagnitude(push, k_MaxPushPerFrame));
            }
        }

        void CreateInput()
        {
            DisposeInput();

            m_MoveAction = new InputAction("Move", InputActionType.Value);
            m_MoveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            m_MoveAction.AddBinding("<Gamepad>/leftStick");

            // 方向鍵另外讀：↑↓ 前後走，←→ 轉鏡頭（或關掉 arrowKeyTurning 時橫著走）。
            m_ArrowForwardAction = new InputAction("ArrowForward", InputActionType.Value);
            m_ArrowForwardAction.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/downArrow").With("Positive", "<Keyboard>/upArrow");
            m_ArrowTurnAction = new InputAction("ArrowTurn", InputActionType.Value);
            m_ArrowTurnAction.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");

            m_JumpAction = new InputAction("Jump", InputActionType.Button);
            m_JumpAction.AddBinding("<Keyboard>/space");
            m_JumpAction.AddBinding("<Gamepad>/buttonSouth");

            m_SneakAction = new InputAction("Sneak", InputActionType.Button);
            m_SneakAction.AddBinding("<Keyboard>/leftShift");
            m_SneakAction.AddBinding("<Gamepad>/leftStickPress");

            m_MoveAction.Enable();
            m_ArrowForwardAction.Enable();
            m_ArrowTurnAction.Enable();
            m_JumpAction.Enable();
            m_SneakAction.Enable();
        }

        void DisposeInput()
        {
            DisposeAction(ref m_MoveAction);
            DisposeAction(ref m_ArrowForwardAction);
            DisposeAction(ref m_ArrowTurnAction);
            DisposeAction(ref m_JumpAction);
            DisposeAction(ref m_SneakAction);
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
    }
}
