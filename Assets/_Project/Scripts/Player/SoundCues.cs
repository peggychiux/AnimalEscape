using System.Collections.Generic;
using UnityEngine;

namespace AnimalEscape.Players
{
    /// <summary>聲音的種類。</summary>
    public enum SoundKind : byte
    {
        /// <summary>人類的腳步聲（動物聽得到）。</summary>
        Footstep = 0,

        /// <summary>動物項圈的鈴鐺（人類聽得到）。</summary>
        Bell = 1,

        /// <summary>動物被網住、被關進籠子時的叫聲（所有人聽得到）。</summary>
        Cry = 2,

        /// <summary>東西被動物弄倒的撞擊聲（M4，提示只給人類）。</summary>
        Crash = 3
    }

    /// <summary>
    /// 這台電腦的玩家剛剛「聽到」的聲音（只記在本機）。畫面邊緣的聲音提示（GameHud）照這裡畫，測試工具也從這裡讀。
    /// 同一個來源、同一種聲音只留最新的一筆，所以一直在走的人類只會有一個提示跟著移動。
    /// </summary>
    public static class SoundCues
    {
        public struct Cue
        {
            public SoundKind Kind;
            public int SourceId;
            public Vector3 Position;
            public float Time;

            /// <summary>0～1，越近越大聲。</summary>
            public float Loudness;
            public float Distance;
        }

        public struct Tally
        {
            public int Count;
            public float MinDistance;
            public float MaxDistance;
        }

        public static readonly List<Cue> Recent = new List<Cue>();

        static readonly Tally[] s_Tallies = new Tally[4];

        public static void Add(SoundKind kind, int sourceId, Vector3 position, float loudness, float distance)
        {
            float now = UnityEngine.Time.unscaledTime;
            for (int i = Recent.Count - 1; i >= 0; i--)
            {
                if ((Recent[i].Kind == kind && Recent[i].SourceId == sourceId) || now - Recent[i].Time > 10f)
                {
                    Recent.RemoveAt(i);
                }
            }

            Recent.Add(new Cue
            {
                Kind = kind,
                SourceId = sourceId,
                Position = position,
                Time = now,
                Loudness = loudness,
                Distance = distance
            });

            Tally tally = s_Tallies[(int)kind];
            tally.MinDistance = tally.Count == 0 ? distance : Mathf.Min(tally.MinDistance, distance);
            tally.MaxDistance = tally.Count == 0 ? distance : Mathf.Max(tally.MaxDistance, distance);
            tally.Count++;
            s_Tallies[(int)kind] = tally;
        }

        /// <summary>（測試工具用）上次歸零之後，每種聲音聽到幾次、最近和最遠的距離。讀完就歸零。</summary>
        public static Tally TakeTally(SoundKind kind)
        {
            Tally tally = s_Tallies[(int)kind];
            s_Tallies[(int)kind] = default(Tally);
            return tally;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Recent.Clear();
            for (int i = 0; i < s_Tallies.Length; i++)
            {
                s_Tallies[i] = default(Tally);
            }
        }
    }
}
