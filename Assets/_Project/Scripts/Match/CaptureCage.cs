using System.Collections.Generic;
using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 捕捉籠（M3）。每張地圖 5 個，編號 1～5。人類扛著動物走到籠子旁按 E，動物就被關進來；
    /// 隊友在籠子旁按住 E 可以救出來。
    ///
    /// 這個元件本身不連線：「哪一隻關在哪一個籠子」記在動物身上（PlayerCapture.CageNumber），
    /// 每台電腦都從那裡推算籠子有沒有人，不需要另外同步。物件的位置就是籠子正中央（地面）。
    /// </summary>
    public class CaptureCage : MonoBehaviour
    {
        /// <summary>目前開著的那張地圖上所有的捕捉籠。</summary>
        public static readonly List<CaptureCage> Active = new List<CaptureCage>();

        [Tooltip("籠子的編號（1～5），介面上顯示「N 號籠」")]
        public int number;

        [Tooltip("籠子內部的寬度（公尺，正方形）")]
        public float size = 2.5f;

        /// <summary>動物被關進來時站的位置。</summary>
        public Vector3 InsidePoint
        {
            get { return transform.position + Vector3.up * 0.05f; }
        }

        /// <summary>現在關在這個籠子裡的動物；空的就是 null。</summary>
        public PlayerCapture Occupant
        {
            get
            {
                for (int i = 0; i < PlayerAvatar.All.Count; i++)
                {
                    PlayerAvatar avatar = PlayerAvatar.All[i];
                    PlayerCapture capture = avatar != null && avatar.IsSpawned ? avatar.Capture : null;
                    if (capture != null && capture.State.Value == CaptureState.Caged && capture.CageNumber.Value == number)
                    {
                        return capture;
                    }
                }

                return null;
            }
        }

        public bool IsEmpty
        {
            get { return Occupant == null; }
        }

        /// <summary>從這個位置到籠子中心的水平距離。</summary>
        public float PlanarDistance(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>編號是 number 的籠子；沒有就是 null。</summary>
        public static CaptureCage Find(int number)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active[i] != null && Active[i].number == number)
                {
                    return Active[i];
                }
            }

            return null;
        }

        /// <summary>離這個位置最近、range 公尺以內的籠子。wantEmpty 為 true 時只找空的，false 時只找有關動物的。</summary>
        public static CaptureCage FindNearest(Vector3 position, float range, bool wantEmpty)
        {
            CaptureCage best = null;
            float bestDistance = range;
            for (int i = 0; i < Active.Count; i++)
            {
                CaptureCage cage = Active[i];
                if (cage == null || cage.IsEmpty != wantEmpty || Mathf.Abs(position.y - cage.transform.position.y) > 2.5f)
                {
                    continue;
                }

                float distance = cage.PlanarDistance(position);
                if (distance <= bestDistance)
                {
                    best = cage;
                    bestDistance = distance;
                }
            }

            return best;
        }

        void OnEnable()
        {
            if (!Active.Contains(this))
            {
                Active.Add(this);
            }
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
