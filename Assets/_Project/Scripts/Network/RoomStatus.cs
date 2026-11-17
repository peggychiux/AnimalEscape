using AnimalEscape.Core;
using AnimalEscape.Match;
using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.Networking
{
    /// <summary>給區域網路「附近的房間」列表和介面用的房間摘要（名稱、狀態、是不是測試房間、電腦玩家數）。</summary>
    public static class RoomStatus
    {
        /// <summary>房間名稱：這台電腦的名稱（區域網路上本來就看得到），太長就截斷。</summary>
        public static string RoomName
        {
            get
            {
                string name = SystemInfo.deviceName;
                if (string.IsNullOrEmpty(name) || name == SystemInfo.unsupportedIdentifier)
                {
                    name = "主機";
                }

                if (name.Length > 18)
                {
                    name = name.Substring(0, 18);
                }

                return name + " 的房間";
            }
        }

        public static bool IsTestRoom
        {
            get { return MatchState.IsTestRoom; }
        }

        public static int BotCount
        {
            get { return PlayerSpawner.BotCount; }
        }

        /// <summary>準備中／比賽中／結算／測試房間設定中。</summary>
        public static string PhaseText
        {
            get
            {
                if (MatchState.TestSetupOpen)
                {
                    return "測試房間設定中";
                }

                MatchFlow flow = MatchFlow.Instance;
                if (flow == null)
                {
                    return "準備中";
                }

                switch (flow.Phase.Value)
                {
                    case MatchPhase.Playing:
                        return "比賽中";
                    case MatchPhase.Results:
                        return "結算";
                    default:
                        return "準備中";
                }
            }
        }
    }
}
