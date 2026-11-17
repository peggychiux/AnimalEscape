using System.Collections.Generic;
using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>
    /// 一塊水域。掛在水域的隱形擋牆上（Water 圖層的 BoxCollider）：
    /// 不會游泳的角色被擋牆擋在外面；會游泳的角色（Swimmer 圖層）可以走進來，速度依 GameConfig 的水中倍率改變。
    /// 這裡只負責回答「這個位置是不是在水裡」，場景裡開著的那張地圖的水域才算。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class WaterZone : MonoBehaviour
    {
        /// <summary>目前開著的水域（只有顯示中的那張地圖）。</summary>
        public static readonly List<WaterZone> Active = new List<WaterZone>();

        float m_MinX;
        float m_MaxX;
        float m_MinZ;
        float m_MaxZ;

        void OnEnable()
        {
            // 水域都是沒有旋轉的長方形，直接記下四個邊。
            var box = GetComponent<BoxCollider>();
            Vector3 center = transform.TransformPoint(box.center);
            Vector3 scale = transform.lossyScale;
            float halfX = Mathf.Abs(box.size.x * scale.x) * 0.5f;
            float halfZ = Mathf.Abs(box.size.z * scale.z) * 0.5f;
            m_MinX = center.x - halfX;
            m_MaxX = center.x + halfX;
            m_MinZ = center.z - halfZ;
            m_MaxZ = center.z + halfZ;

            if (!Active.Contains(this))
            {
                Active.Add(this);
            }
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        /// <summary>這個位置（只看水平方向）是不是在這塊水域裡。margin 是往外多算幾公尺。</summary>
        public bool Contains(Vector3 position, float margin)
        {
            return position.x > m_MinX - margin && position.x < m_MaxX + margin
                && position.z > m_MinZ - margin && position.z < m_MaxZ + margin;
        }

        /// <summary>這個位置是不是在任何一塊水域裡。</summary>
        public static bool IsInWater(Vector3 position, float margin = 0f)
        {
            return Find(position, margin) != null;
        }

        /// <summary>包含這個位置的水域；不在水裡就是 null。</summary>
        public static WaterZone Find(Vector3 position, float margin = 0f)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                WaterZone zone = Active[i];
                if (zone != null && zone.Contains(position, margin))
                {
                    return zone;
                }
            }

            return null;
        }

        static readonly Vector3[] s_Candidates = new Vector3[4];

        /// <summary>
        /// 從水裡的一個位置找最近的岸邊（不會游泳的動物在水裡被放下來時用）。
        /// clearance 是離水邊要留多少距離（角色的半徑再多一點）。isFree 用來檢查那個位置站不站得下。
        /// </summary>
        public static bool TryFindShore(Vector3 position, float clearance, System.Predicate<Vector3> isFree, out Vector3 shore)
        {
            shore = position;
            WaterZone zone = Find(position);
            if (zone == null)
            {
                return false;
            }

            s_Candidates[0] = new Vector3(zone.m_MinX - clearance, position.y, position.z);
            s_Candidates[1] = new Vector3(zone.m_MaxX + clearance, position.y, position.z);
            s_Candidates[2] = new Vector3(position.x, position.y, zone.m_MinZ - clearance);
            s_Candidates[3] = new Vector3(position.x, position.y, zone.m_MaxZ + clearance);

            bool found = false;
            float best = float.MaxValue;
            for (int i = 0; i < s_Candidates.Length; i++)
            {
                Vector3 candidate = s_Candidates[i];
                float distance = (candidate - position).sqrMagnitude;
                if (distance >= best || IsInWater(candidate, clearance * 0.5f))
                {
                    continue;
                }

                if (isFree != null && !isFree(candidate))
                {
                    continue;
                }

                best = distance;
                shore = candidate;
                found = true;
            }

            return found;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
