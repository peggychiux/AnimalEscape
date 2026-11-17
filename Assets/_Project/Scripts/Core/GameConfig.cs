using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>
    /// 所有可以調整的遊戲數值都放在這裡（對應的資源檔：Assets/_Project/Config/Resources/GameConfig.asset）。
    /// 調數值時只改那個資源檔，不要把數字寫死在程式裡。
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "Animal Escape/Game Config")]
    public class GameConfig : ScriptableObject
    {
        public const string ResourcePath = "GameConfig";

        [Header("連線")]
        [Min(2)] public int maxPlayers = 5;
        public ushort localPort = 7777;

        [Header("角色（改了尺寸或顏色後，要重建原型場景，外觀才會更新）")]
        public RoleSettings human = new RoleSettings
        {
            displayName = "人類",
            height = 1.8f,
            radius = 0.35f,
            moveSpeed = 5f,
            jumpHeight = 1f,
            cameraDistance = 5f,
            color = new Color(0.95f, 0.55f, 0.15f)
        };

        public RoleSettings rabbit = new RoleSettings
        {
            displayName = "兔子",
            height = 0.6f,
            radius = 0.25f,
            moveSpeed = 6.5f,
            jumpHeight = 1.6f,
            cameraDistance = 4f,
            color = new Color(0.95f, 0.95f, 0.92f)
        };

        public RoleSettings monkey = new RoleSettings
        {
            displayName = "猴子",
            height = 0.9f,
            radius = 0.28f,
            moveSpeed = 5.5f,
            jumpHeight = 1.3f,
            cameraDistance = 4f,
            color = new Color(0.55f, 0.36f, 0.20f)
        };

        public RoleSettings raccoon = new RoleSettings
        {
            displayName = "浣熊",
            height = 0.7f,
            radius = 0.27f,
            moveSpeed = 5f,
            jumpHeight = 1f,
            cameraDistance = 4f,
            color = new Color(0.52f, 0.54f, 0.58f)
        };

        public RoleSettings turtle = new RoleSettings
        {
            displayName = "烏龜",
            height = 0.5f,
            radius = 0.25f,
            moveSpeed = 3.5f,
            jumpHeight = 0.5f,
            cameraDistance = 4f,
            color = new Color(0.30f, 0.62f, 0.32f)
        };

        [Header("移動（所有角色共用）")]
        public float turnSpeed = 720f;
        public float gravity = -20f;

        [Tooltip("按住 Shift 慢走時的速度倍率")]
        [Range(0.1f, 1f)] public float sneakSpeedMultiplier = 0.5f;

        [Header("鏡頭")]
        [Tooltip("人類的視角。預設第一人稱：視野受限，動物才有躲藏和繞路的空間。Play 中也可以直接切換比較。")]
        public ViewMode humanViewMode = ViewMode.FirstPerson;

        [Tooltip("動物的視角。預設第三人稱：看得到自己的角色，也能越過障礙物觀察四周。Play 中也可以直接切換比較。")]
        public ViewMode animalViewMode = ViewMode.ThirdPerson;

        public float mouseSensitivity = 0.12f;
        public float gamepadLookSpeed = 150f;

        [Tooltip("第一人稱的視野角度（垂直方向，度）")]
        [Range(40f, 100f)] public float firstPersonFieldOfView = 70f;

        [Tooltip("第一人稱最多可以往上看幾度（負數）")]
        public float firstPersonMinPitch = -80f;

        [Tooltip("第一人稱最多可以往下看幾度")]
        public float firstPersonMaxPitch = 80f;

        [Tooltip("第三人稱的視野角度（垂直方向，度）")]
        [Range(40f, 100f)] public float thirdPersonFieldOfView = 60f;

        [Tooltip("第三人稱最多可以往上看幾度（負數）")]
        public float minPitch = -20f;

        [Tooltip("第三人稱最多可以往下看幾度")]
        public float maxPitch = 65f;

        [Header("追與抓：網子（全部是初始值，待測試）")]
        [Tooltip("網子打得到多遠（公尺，從人類的中心算到動物的邊緣）")]
        public float netRange = 2.6f;

        [Tooltip("網子涵蓋的角度：人類正前方左右各幾度")]
        [Range(10f, 180f)] public float netHalfAngle = 50f;

        [Tooltip("按下去之後過幾秒才判定有沒有打中（揮下來的時間，動物可以趁這時候閃開）")]
        public float netWindup = 0.2f;

        [Tooltip("揮一次之後要等幾秒才能再揮（不管有沒有打中）")]
        public float netCooldown = 1.5f;

        [Tooltip("動物和人類的高度差超過這個數字就打不到（公尺）")]
        public float netMaxHeightDifference = 1.8f;

        [Tooltip("判斷網子有沒有被障礙物擋住時，從人類身上多高的地方算起（公尺，大約是手的高度）")]
        public float netOriginHeight = 1.1f;

        [Header("追與抓：掙脫")]
        [Tooltip("被網住倒在地上時，每按一下增加多少掙脫值（滿是 1）")]
        [Range(0.005f, 1f)] public float struggleGainNetted = 0.06f;

        [Tooltip("被扛著時，每按一下增加多少掙脫值（比在地上難掙脫）")]
        [Range(0.005f, 1f)] public float struggleGainCarried = 0.035f;

        [Tooltip("沒有在按的時候，掙脫值每秒掉多少")]
        public float struggleDecayPerSecond = 0.05f;

        [Tooltip("兩次按鍵至少要隔幾秒才算數（防止用程式狂按）")]
        public float struggleMinPressInterval = 0.05f;

        [Tooltip("掙脫或被隊友救下來之後，幾秒內網子抓不到")]
        public float breakFreeProtectionSeconds = 3f;

        [Header("追與抓：扛走與推撞")]
        [Tooltip("人類離被網住的動物多近才能扛起來（公尺，算到動物的邊緣）")]
        public float pickupRange = 2f;

        [Tooltip("人類扛著動物時的速度倍率")]
        [Range(0.1f, 1f)] public float carrySpeedMultiplier = 0.7f;

        [Tooltip("動物至少要用這個速度（公尺／秒）撞上扛著隊友的人類，才算推撞。慢走不算。")]
        public float bumpMinSpeed = 3.3f;

        [Tooltip("動物的邊緣離人類的邊緣多近以內，就算撞到了（公尺）")]
        public float bumpReach = 0.2f;

        [Tooltip("人類被撞一次之後，幾秒內不會再被撞")]
        public float bumpCooldown = 1f;

        [Tooltip("人類被隊友撞到、放下動物之後，踉蹌幾秒（走得很慢、不能揮網也不能扛）")]
        public float staggerSecondsOnBump = 2f;

        [Tooltip("扛著的動物自己掙脫之後，人類踉蹌幾秒")]
        public float staggerSecondsOnEscape = 1.2f;

        [Tooltip("人類踉蹌時的速度倍率")]
        [Range(0f, 1f)] public float staggerSpeedMultiplier = 0.3f;

        [Header("水（倍率＝在水裡的速度 ÷ 平常的速度；0 表示不能下水，會被水邊擋住）")]
        [Tooltip("人類涉水時的速度倍率（變慢）")]
        public float humanWaterSpeedMultiplier = 0.5f;

        [Tooltip("烏龜在水裡的速度倍率（游得比走路快）")]
        public float turtleWaterSpeedMultiplier = 1.6f;

        public float rabbitWaterSpeedMultiplier = 0f;
        public float monkeyWaterSpeedMultiplier = 0f;
        public float raccoonWaterSpeedMultiplier = 0f;

        [Tooltip("不會游泳的動物萬一人在水裡（例如在水中央從人類肩上掙脫、附近又沒有岸可以落地），爬上岸時的速度倍率")]
        [Range(0.1f, 1f)] public float strandedWaterSpeedMultiplier = 0.5f;

        [Tooltip("在水裡時，角色的外觀往下沉多少（公尺），看起來像泡在水裡。最多沉到身高的六成。")]
        public float waterSinkDepth = 0.35f;

        [Header("開場越獄：撬鎖（按住不放的秒數；0 表示這個身分不能撬鎖）")]
        public float raccoonLockpickSeconds = 5f;
        public float monkeyLockpickSeconds = 8f;
        public float rabbitLockpickSeconds = 11f;

        [Tooltip("烏龜開場沒有被關，這是牠幫隊友撬鎖時需要的秒數")]
        public float turtleLockpickSeconds = 14f;

        [Tooltip("離門的正中央多近才能撬鎖（公尺）")]
        public float lockpickRange = 1.8f;

        [Tooltip("門被撬開的那一刻，離門多近以內的動物會得到幾秒保護（和掙脫後的保護一樣長），免得人類守在門口")]
        public float lockpickProtectRadius = 6f;

        [Header("捕捉籠與救援（M3，全部是初始值，待測試）")]
        [Tooltip("第 1、2、3 次被關進籠子的倒數秒數。倒數歸零就出局。")]
        public float[] cageSeconds = { 120f, 80f, 40f };

        [Tooltip("人類扛著動物，離籠子中心多近以內按 E 就會關進去（公尺）")]
        public float putInCageRange = 2.8f;

        [Tooltip("隊友在籠子旁按住 E 幾秒可以把籠子裡的動物救出來")]
        public float rescueSeconds = 3f;

        [Tooltip("隊友離籠子中心多近以內才能救（公尺）")]
        public float rescueRange = 2.4f;

        [Tooltip("被救出來之後幾秒內網子抓不到（保護中被網子打到只會變慢）")]
        public float rescueProtectionSeconds = 5f;

        [Tooltip("保護中被網子打到時，變慢幾秒")]
        public float protectedNetSlowSeconds = 1.5f;

        [Tooltip("保護中被網子打到時的速度倍率")]
        [Range(0.1f, 1f)] public float protectedNetSlowMultiplier = 0.5f;

        [Tooltip("反守籠：人類離關著動物的籠子多近算「在守」（公尺）")]
        public float campRadius = 8f;

        [Tooltip("反守籠：人類連續守超過幾秒，那個籠子的倒數就暫停（人類離開才繼續）。籠裡的動物不會因此自己出來。")]
        public float campSecondsBeforePause = 10f;

        [Tooltip("籠邊警報：人類離關著動物的籠子多近以內，動物們會收到「人類在 X 號籠附近」（公尺）")]
        public float cageAlarmRadius = 15f;

        [Header("聲音（全部是初始值，待試玩調整）")]
        [Tooltip("人類跑步的腳步聲，動物在多遠以內聽得到（公尺）")]
        public float humanFootstepRangeRun = 20f;

        [Tooltip("人類慢走（按住 Shift）的腳步聲，動物在多遠以內聽得到（公尺）")]
        public float humanFootstepRangeSneak = 8f;

        [Tooltip("動物跑步時項圈鈴鐺的聲音，人類在多遠以內聽得到（公尺）")]
        public float bellRangeRun = 18f;

        [Tooltip("動物慢走（按住 Shift）時鈴鐺的聲音，人類在多遠以內聽得到（公尺）")]
        public float bellRangeSneak = 5f;

        [Tooltip("被網住、被關進籠子時的叫聲，多遠以內聽得到（公尺；100 等於整張地圖）")]
        public float cryRange = 100f;

        [Tooltip("速度超過自己全速的幾成算「跑」（聲音傳得遠）；慢走是 0.5，扛著動物是 0.7")]
        [Range(0.1f, 1f)] public float runSpeedFraction = 0.65f;

        [Tooltip("速度低於這個值（公尺／秒）算站著不動，完全沒有聲音")]
        public float silentBelowSpeed = 0.5f;

        [Tooltip("腳步聲的間隔（秒）：跑、慢走")]
        public float footstepIntervalRun = 0.32f;
        public float footstepIntervalSneak = 0.55f;

        [Tooltip("鈴鐺響的間隔（秒）：跑、慢走")]
        public float bellIntervalRun = 0.3f;
        public float bellIntervalSneak = 0.6f;

        [Tooltip("畫面邊緣的聲音提示停留幾秒後淡掉")]
        public float soundCueSeconds = 2f;

        [Range(0f, 1f)] public float soundVolume = 0.8f;

        [Header("介面")]
        [Tooltip("其他玩家頭上的名牌，離多遠以內才顯示（公尺）。被牆或障礙物擋住時不論多近都不顯示，才不會洩漏躲藏的位置。")]
        public float nameTagMaxDistance = 25f;

        [Header("測試")]
        [Tooltip("用 Multiplayer Play Mode 測試時，虛擬玩家（Player 2～4）的視窗會自動加入主視窗開的本機房間，不用每次手動按加入。")]
        public bool virtualPlayersAutoJoinLocal = true;

        /// <summary>這個身分用哪一種視角。</summary>
        public ViewMode GetViewMode(Role role)
        {
            return role == Role.Human ? humanViewMode : animalViewMode;
        }

        public RoleSettings GetRole(Role role)
        {
            switch (role)
            {
                case Role.Rabbit:
                    return rabbit;
                case Role.Monkey:
                    return monkey;
                case Role.Raccoon:
                    return raccoon;
                case Role.Turtle:
                    return turtle;
                default:
                    return human;
            }
        }

        /// <summary>第幾次被關（從 1 開始）的倒數秒數；超過設定的次數就是 0（表示直接出局）。</summary>
        public float GetCageSeconds(int timesCaged)
        {
            if (cageSeconds == null || timesCaged < 1 || timesCaged > cageSeconds.Length)
            {
                return 0f;
            }

            return cageSeconds[timesCaged - 1];
        }

        /// <summary>最多可以被關幾次；再被抓一次就直接出局（預設 3 次，第 4 次出局）。</summary>
        public int MaxTimesCaged
        {
            get { return cageSeconds != null ? cageSeconds.Length : 0; }
        }

        /// <summary>這個身分在水裡的速度倍率。0 表示不能下水。</summary>
        public float GetWaterSpeedMultiplier(Role role)
        {
            switch (role)
            {
                case Role.Human:
                    return humanWaterSpeedMultiplier;
                case Role.Turtle:
                    return turtleWaterSpeedMultiplier;
                case Role.Rabbit:
                    return rabbitWaterSpeedMultiplier;
                case Role.Monkey:
                    return monkeyWaterSpeedMultiplier;
                case Role.Raccoon:
                    return raccoonWaterSpeedMultiplier;
                default:
                    return 0f;
            }
        }

        /// <summary>這個身分能不能下水。</summary>
        public bool CanEnterWater(Role role)
        {
            return GetWaterSpeedMultiplier(role) > 0f;
        }

        /// <summary>這個身分撬開一道鎖要按住幾秒。0 表示不能撬（人類）。</summary>
        public float GetLockpickSeconds(Role role)
        {
            switch (role)
            {
                case Role.Raccoon:
                    return raccoonLockpickSeconds;
                case Role.Monkey:
                    return monkeyLockpickSeconds;
                case Role.Rabbit:
                    return rabbitLockpickSeconds;
                case Role.Turtle:
                    return turtleLockpickSeconds;
                default:
                    return 0f;
            }
        }

        static GameConfig s_Instance;

        public static GameConfig Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = Resources.Load<GameConfig>(ResourcePath);
                    if (s_Instance == null)
                    {
                        Debug.LogWarning("[GameConfig] 找不到 Resources/GameConfig，先用程式裡的預設值。可用選單 Tools > Animal Escape > 重建原型場景 重新產生。");
                        s_Instance = CreateInstance<GameConfig>();
                    }
                }

                return s_Instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Instance = null;
        }
    }
}
