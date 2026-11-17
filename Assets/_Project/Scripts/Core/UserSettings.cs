using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>
    /// 主選單「設定」裡的個人設定（每台電腦各自存在 PlayerPrefs）：滑鼠靈敏度、方向鍵轉鏡頭速度、上下視角反轉、音量、全螢幕。
    /// 遊戲裡讀這裡，不直接讀 GameConfig（GameConfig 是全專案共用的預設值）。
    /// </summary>
    public static class UserSettings
    {
        public const int DefaultMouseSensitivity = 5;
        public const int MinMouseSensitivity = 1;
        public const int MaxMouseSensitivity = 10;
        public const int MinTurnSpeed = 60;
        public const int MaxTurnSpeed = 300;
        public const int DefaultVolume = 80;

        const string k_Mouse = "settings.mouse";
        const string k_Turn = "settings.turnSpeed";
        const string k_Invert = "settings.invertY";
        const string k_Volume = "settings.volume";
        const string k_Fullscreen = "settings.fullscreen";

        static bool s_Loaded;
        static int s_Mouse;
        static int s_Turn;
        static bool s_Invert;
        static int s_Volume;
        static bool s_Fullscreen;

        /// <summary>滑鼠靈敏度 1～10，5 等於 GameConfig.mouseSensitivity。</summary>
        public static int MouseSensitivity
        {
            get { Load(); return s_Mouse; }
            set { Load(); s_Mouse = Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity); PlayerPrefs.SetInt(k_Mouse, s_Mouse); }
        }

        /// <summary>方向鍵轉鏡頭的速度（度／秒）。</summary>
        public static int TurnSpeed
        {
            get { Load(); return s_Turn; }
            set { Load(); s_Turn = Mathf.Clamp(value, MinTurnSpeed, MaxTurnSpeed); PlayerPrefs.SetInt(k_Turn, s_Turn); }
        }

        public static bool InvertY
        {
            get { Load(); return s_Invert; }
            set { Load(); s_Invert = value; PlayerPrefs.SetInt(k_Invert, value ? 1 : 0); }
        }

        /// <summary>音量 0～100。</summary>
        public static int Volume
        {
            get { Load(); return s_Volume; }
            set
            {
                Load();
                s_Volume = Mathf.Clamp(value, 0, 100);
                PlayerPrefs.SetInt(k_Volume, s_Volume);
                AudioListener.volume = s_Volume / 100f;
            }
        }

        /// <summary>全螢幕（編輯器裡沒有作用，打包版才會切換）。</summary>
        public static bool Fullscreen
        {
            get { Load(); return s_Fullscreen; }
            set
            {
                Load();
                s_Fullscreen = value;
                PlayerPrefs.SetInt(k_Fullscreen, value ? 1 : 0);
                ApplyScreenMode();
            }
        }

        /// <summary>實際用的滑鼠靈敏度倍率（乘上滑鼠位移）。</summary>
        public static float MouseLookScale
        {
            get { return GameConfig.Instance.mouseSensitivity * MouseSensitivity / (float)DefaultMouseSensitivity; }
        }

        public static void ResetToDefaults()
        {
            MouseSensitivity = DefaultMouseSensitivity;
            TurnSpeed = Mathf.RoundToInt(GameConfig.Instance.keyboardTurnSpeed);
            InvertY = false;
            Volume = DefaultVolume;
            Fullscreen = false;
            PlayerPrefs.Save();
        }

        public static void Save()
        {
            PlayerPrefs.Save();
        }

        /// <summary>遊戲一開始套用音量和畫面模式。</summary>
        public static void ApplyAll()
        {
            Load();
            AudioListener.volume = s_Volume / 100f;
            ApplyScreenMode();
        }

        static void ApplyScreenMode()
        {
            if (Application.isEditor)
            {
                return;
            }

            Screen.fullScreenMode = s_Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        static void Load()
        {
            if (s_Loaded)
            {
                return;
            }

            s_Loaded = true;
            s_Mouse = Mathf.Clamp(PlayerPrefs.GetInt(k_Mouse, DefaultMouseSensitivity), MinMouseSensitivity, MaxMouseSensitivity);
            int defaultTurn = GameConfig.Instance != null ? Mathf.RoundToInt(GameConfig.Instance.keyboardTurnSpeed) : 160;
            s_Turn = Mathf.Clamp(PlayerPrefs.GetInt(k_Turn, defaultTurn), MinTurnSpeed, MaxTurnSpeed);
            s_Invert = PlayerPrefs.GetInt(k_Invert, 0) == 1;
            s_Volume = Mathf.Clamp(PlayerPrefs.GetInt(k_Volume, DefaultVolume), 0, 100);
            s_Fullscreen = PlayerPrefs.GetInt(k_Fullscreen, 0) == 1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Loaded = false;
        }
    }
}
