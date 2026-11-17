using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>
    /// 介面與遊戲操作之間的簡單協調：
    /// - 滑鼠在介面上時，點擊不會被當成「鎖定滑鼠控制鏡頭」。
    /// - 蓋住整個畫面的選單（測試房間）開著、或正在介面的輸入框裡打字時，角色不讀鍵盤（不然打字時角色會亂走）。
    /// </summary>
    public static class InputGate
    {
        public static bool PointerOverUi { get; set; }

        /// <summary>蓋住整個畫面的選單開著（例如測試房間的設定畫面）。</summary>
        public static bool MenuOpen { get; set; }

        /// <summary>正在介面的輸入框裡打字。</summary>
        public static bool Typing { get; set; }

        /// <summary>角色現在能不能讀鍵盤：視窗在最前面（同一台電腦開多個視窗測試時只有被點選的那個會動）、沒有開選單、沒有在打字。</summary>
        public static bool GameInputAllowed
        {
            get { return Application.isFocused && !MenuOpen && !Typing; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            PointerOverUi = false;
            MenuOpen = false;
            Typing = false;
        }
    }
}
