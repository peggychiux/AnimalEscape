using System.Collections.Generic;
using AnimalEscape.Core;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 收容區的門。開場時每種動物關在自己的收容區裡，門是鎖著的；動物到門邊按住 E 撬鎖（PlayerLockpick），
    /// 主機判定撬開之後把「這個收容區開了」記在 MatchState，所有電腦的門就一起打開。
    /// 這個元件本身不連線，只是照 MatchState 的狀態開關碰撞、轉動門板。
    /// 物件的位置就是門的正中央（地面），自己的 +Z 朝收容區外面。
    /// </summary>
    public class HabitatGate : MonoBehaviour
    {
        /// <summary>目前開著的那張地圖上所有的門。</summary>
        public static readonly List<HabitatGate> Active = new List<HabitatGate>();

        [Tooltip("這是哪一種動物的收容區")]
        public Role role = Role.None;

        [Tooltip("門板繞著它轉的軸")]
        public Transform hinge;

        [Tooltip("鎖著時擋路的碰撞")]
        public Collider blocker;

        [Tooltip("打開時門板轉幾度（負的是往外開）")]
        public float openAngle = -105f;

        [Tooltip("門板每秒轉幾度")]
        public float swingSpeed = 240f;

        [Tooltip("收容區內部的範圍（世界座標），用來判斷動物是不是還關在裡面")]
        public Vector3 interiorCenter;
        public Vector3 interiorSize;

        float m_Angle;

        /// <summary>門是不是開著（由主機決定，所有電腦一致）。還沒進房間時一律是鎖著的。</summary>
        public bool IsOpen
        {
            get { return MatchState.Instance != null && MatchState.Instance.IsHabitatOpen(role); }
        }

        /// <summary>從這個位置到門正中央的水平距離。</summary>
        public float PlanarDistance(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>這個位置是不是在收容區裡面。</summary>
        public bool Contains(Vector3 position)
        {
            Vector3 offset = position - interiorCenter;
            return Mathf.Abs(offset.x) <= interiorSize.x * 0.5f
                && Mathf.Abs(offset.z) <= interiorSize.z * 0.5f
                && offset.y >= -interiorSize.y * 0.5f - 0.5f && offset.y <= interiorSize.y * 0.5f;
        }

        /// <summary>目前這張地圖上，某一種動物的收容區的門；沒有就是 null。</summary>
        public static HabitatGate Find(Role role)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                HabitatGate gate = Active[i];
                if (gate != null && gate.role == role)
                {
                    return gate;
                }
            }

            return null;
        }

        /// <summary>離這個位置最近、還鎖著、而且在 range 公尺以內的門；沒有就是 null。</summary>
        public static HabitatGate FindLockedInReach(Vector3 position, float range)
        {
            HabitatGate best = null;
            float bestDistance = range;
            for (int i = 0; i < Active.Count; i++)
            {
                HabitatGate gate = Active[i];
                if (gate == null || gate.IsOpen)
                {
                    continue;
                }

                float distance = gate.PlanarDistance(position);
                if (distance <= bestDistance && Mathf.Abs(position.y - gate.transform.position.y) < 2.5f)
                {
                    best = gate;
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

            Apply(true);
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        void Update()
        {
            Apply(false);
        }

        void Apply(bool instantly)
        {
            bool open = IsOpen;
            if (blocker != null && blocker.enabled == open)
            {
                blocker.enabled = !open;
            }

            float target = open ? openAngle : 0f;
            m_Angle = instantly ? target : Mathf.MoveTowards(m_Angle, target, swingSpeed * Time.deltaTime);
            if (hinge != null)
            {
                hinge.localRotation = Quaternion.Euler(0f, m_Angle, 0f);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
