using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>
    /// 介面與遊戲操作之間的簡單協調：滑鼠在介面上時，點擊不會被當成「鎖定滑鼠控制鏡頭」。
    /// </summary>
    public static class InputGate
    {
        public static bool PointerOverUi { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            PointerOverUi = false;
        }
    }
}
