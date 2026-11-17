using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>
    /// 專案用到的圖層。
    /// - Water：水域的隱形擋牆。不會游泳的角色會被它擋住；鏡頭、視線、網子都略過它。
    /// - Bars：看得穿的鐵網（收容區的籠子、門）。所有角色都過不去、網子也打不穿，但鏡頭和視線略過它，
    ///   所以關在裡面的動物用第三人稱看得到自己，外面的人也看得到裡面。
    /// - Swimmer：可以下水的角色（人類、烏龜）放在這一層，這一層和 Water 互不碰撞。
    /// </summary>
    public static class GameLayers
    {
        /// <summary>Unity 內建的 Water 圖層編號。</summary>
        public const int Water = 4;

        public const int Bars = 8;
        public const int Swimmer = 9;

        /// <summary>判斷視線、鏡頭避障時要檢查的圖層：預設的全部，扣掉水域擋牆和看得穿的鐵網。</summary>
        public const int SightMask = Physics.DefaultRaycastLayers & ~(1 << Water) & ~(1 << Bars);

        /// <summary>判斷網子有沒有被擋住時要檢查的圖層：實心的東西加上鐵網（隔著籠子抓不到），水不算。</summary>
        public const int NetMask = Physics.DefaultRaycastLayers & ~(1 << Water);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void SetUpCollisions()
        {
            // 會游泳的角色不被水域的擋牆擋住。專案設定裡也有存（PrototypeBuilder），這裡再設一次保險。
            Physics.IgnoreLayerCollision(Swimmer, Water, true);
        }
    }
}
