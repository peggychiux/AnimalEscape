using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>
    /// 專案用到的圖層。目前只有一個特別的：水域的隱形擋牆放在 Unity 內建的 Water 圖層，
    /// 角色會被它擋住（走不進水裡），但鏡頭和「看不看得到對方」的判斷要略過它。
    /// </summary>
    public static class GameLayers
    {
        /// <summary>Unity 內建的 Water 圖層編號。</summary>
        public const int Water = 4;

        /// <summary>判斷視線、鏡頭避障時要檢查的圖層：預設的全部，扣掉水域擋牆。</summary>
        public const int SightMask = Physics.DefaultRaycastLayers & ~(1 << Water);
    }
}
