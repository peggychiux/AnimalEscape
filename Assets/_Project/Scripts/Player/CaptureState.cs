namespace AnimalEscape.Players
{
    /// <summary>
    /// 動物的捕捉狀態：自由 → 被網住 → 被扛著 → 在籠子裡 → 在實驗室；另外有「上了貨車」。
    /// 人類自己永遠是 Free；人類「正扛著誰」另外記在 PlayerCapture.CarryingId。
    /// </summary>
    public enum CaptureState : byte
    {
        /// <summary>自由行動。</summary>
        Free = 0,

        /// <summary>被網住，倒在地上不能動，可以連按掙脫。</summary>
        Netted = 1,

        /// <summary>被人類扛著走，可以繼續連按掙脫，隊友也可以撞人類來救。</summary>
        Carried = 2,

        /// <summary>被人類關進捕捉籠，籠子在倒數；隊友在籠子旁按住 E 可以救出來。在籠子裡可以走動但出不去。</summary>
        Caged = 3,

        /// <summary>
        /// 在實驗室（籠子倒數歸零，或第 3 次被抓）：被搬進地圖角落的實驗室，可以在裡面自由走動、踢球、做表情、用大螢幕看隊友。
        /// 隊友拿門禁卡來刷門才出得來（整場一次）；也可以按「離開遊戲」回大廳。
        /// </summary>
        InLab = 4,

        /// <summary>上了貨車（M4）：出口的門開了之後走進車廂就是這個狀態，網子抓不到；走下車就變回自由。貨車開走時在車上的算逃出。</summary>
        Escaped = 5
    }

    /// <summary>人類揮一次網子的結果（主機判定後告訴揮網的人）。</summary>
    public enum SwingOutcome : byte
    {
        None = 0,

        /// <summary>揮空。</summary>
        Miss = 1,

        /// <summary>網住了一隻動物。</summary>
        Hit = 2,

        /// <summary>打到了，但對方還在保護時間內，抓不到（只會讓牠變慢一下）。</summary>
        Protected = 3
    }
}
