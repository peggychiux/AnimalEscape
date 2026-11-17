using System;
using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>房間的種類：正式對戰（主選單的「開始遊戲」），或測試模式（開發用，規則可以放寬）。</summary>
    public enum RoomMode : byte
    {
        Official = 0,
        Test = 1
    }

    /// <summary>測試模式的規則開關（正式遊戲裡都是關的）。</summary>
    [Flags]
    public enum TestRule
    {
        None = 0,

        /// <summary>大家自己選角色（關掉的話只有房主能分配）。</summary>
        SelfPick = 1,

        /// <summary>同一種身分可以重複。</summary>
        DuplicateRoles = 2,

        /// <summary>比賽中隨時可以換身分（F1 面板可以原地換）。</summary>
        SwapAnytime = 4,

        /// <summary>不用湊齊人類和動物也能開始（場上沒有動物也不會結束）。</summary>
        NoTeamNeeded = 8,

        All = SelfPick | DuplicateRoles | SwapAnytime | NoTeamNeeded
    }

    /// <summary>測試模式要從哪個階段開始。</summary>
    public enum TestStart : byte
    {
        /// <summary>準備中（自由練習、不計時）。</summary>
        Lobby = 0,

        /// <summary>比賽開場（動物關在收容區）。</summary>
        MatchStart = 1,

        /// <summary>已越獄（收容區全開）。</summary>
        Escaped = 2,

        /// <summary>混亂值已滿（出口解鎖）。</summary>
        ChaosFull = 3,

        /// <summary>出口已開（貨車倒數中）。</summary>
        ExitOpen = 4
    }

    /// <summary>房間怎麼連：只用這台電腦、同一個 Wi-Fi（區域網路）、線上（Relay，給代碼）。</summary>
    public enum LinkMode : byte
    {
        ThisComputer = 0,
        Lan = 1,
        Online = 2
    }

    /// <summary>
    /// 開房前在主選單選好的設定（只在開房的那台電腦上有用）。主機開房時，MatchState／MatchFlow 從這裡讀，
    /// 再用 NetworkVariable 同步給所有人。
    /// </summary>
    public static class RoomOptions
    {
        public static RoomMode Mode = RoomMode.Official;

        /// <summary>一局幾秒（正式模式可以選 5／8／10 分鐘）；0 表示用 GameConfig 的預設。</summary>
        public static float MatchSeconds;

        public static TestRule Rules = TestRule.All;

        public static TestStart StartPhase = TestStart.MatchStart;

        public static LinkMode Link = LinkMode.Lan;

        public static string StartPhaseName(TestStart phase)
        {
            switch (phase)
            {
                case TestStart.Lobby:
                    return "準備中（自由練習、不計時）";
                case TestStart.MatchStart:
                    return "比賽開場（動物關在收容區）";
                case TestStart.Escaped:
                    return "已越獄（收容區全開）";
                case TestStart.ChaosFull:
                    return "混亂值已滿（出口解鎖）";
                case TestStart.ExitOpen:
                    return "出口已開（貨車倒數中）";
                default:
                    return phase.ToString();
            }
        }

        public static string RuleName(TestRule rule)
        {
            switch (rule)
            {
                case TestRule.SelfPick:
                    return "大家自己選角色（關掉只有房主能分配）";
                case TestRule.DuplicateRoles:
                    return "同一種身分可以重複";
                case TestRule.SwapAnytime:
                    return "比賽中隨時原地換身分";
                case TestRule.NoTeamNeeded:
                    return "不用湊齊人類和動物也能開始";
                default:
                    return rule.ToString();
            }
        }

        /// <summary>回到正式模式的預設（測試工具的 host 指令用）。</summary>
        public static void UseOfficialDefaults()
        {
            Mode = RoomMode.Official;
            MatchSeconds = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Mode = RoomMode.Official;
            MatchSeconds = 0f;
            Rules = TestRule.All;
            StartPhase = TestStart.MatchStart;
            Link = LinkMode.Lan;
        }
    }
}
