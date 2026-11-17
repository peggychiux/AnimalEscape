using System.Collections.Generic;
using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 保全控制台（M4）：每張地圖 3 台（編號 0～2）。人類在旁邊按住 E 幾秒（GameConfig.consoleSeconds）重開保全系統，
    /// 混亂值下降，這一台要冷卻一段時間才能再用。混亂值滿 100% 之後保全系統當機，控制台就沒用了。
    ///
    /// 這個元件本身不連線：冷卻到什麼時候記在 MatchFlow（ConsoleReadyAt），這裡只照那個時間換螢幕顏色。
    /// 物件的位置就是控制台底部中心。
    /// </summary>
    public class SecurityConsole : MonoBehaviour
    {
        public static readonly List<SecurityConsole> Active = new List<SecurityConsole>();

        [Tooltip("這張地圖裡的編號（0～2）")]
        public int index;

        [Tooltip("螢幕（可以用＝綠、冷卻中＝黃、當機＝紅）")]
        public Renderer screen;

        static readonly Color k_Ready = new Color(0.25f, 0.95f, 0.45f);
        static readonly Color k_Cooling = new Color(0.98f, 0.78f, 0.2f);
        static readonly Color k_Crashed = new Color(0.95f, 0.2f, 0.18f);

        MaterialPropertyBlock m_Block;

        /// <summary>從這個位置到控制台中心的水平距離。</summary>
        public float DistanceFrom(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            if (Mathf.Abs(offset.y) > 1.5f)
            {
                return float.MaxValue;
            }

            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>這一台還要冷卻幾秒（0 表示可以用）。</summary>
        public float CooldownRemaining
        {
            get
            {
                MatchFlow flow = MatchFlow.Instance;
                return flow != null ? Mathf.Max(0f, (float)(flow.GetConsoleReadyAt(index) - PlayerCapture.Now)) : 0f;
            }
        }

        /// <summary>離這個位置最近、range 公尺以內的控制台；沒有就是 null。</summary>
        public static SecurityConsole FindInReach(Vector3 position, float range)
        {
            SecurityConsole best = null;
            float bestDistance = range;
            for (int i = 0; i < Active.Count; i++)
            {
                SecurityConsole console = Active[i];
                if (console == null)
                {
                    continue;
                }

                float distance = console.DistanceFrom(position);
                if (distance <= bestDistance)
                {
                    best = console;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public static SecurityConsole Find(int index)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active[i] != null && Active[i].index == index)
                {
                    return Active[i];
                }
            }

            return null;
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

        void Update()
        {
            if (screen == null)
            {
                return;
            }

            if (m_Block == null)
            {
                m_Block = new MaterialPropertyBlock();
            }

            MatchFlow flow = MatchFlow.Instance;
            Color color = flow != null && flow.ExitUnlocked ? k_Crashed : (CooldownRemaining > 0f ? k_Cooling : k_Ready);
            screen.GetPropertyBlock(m_Block);
            m_Block.SetColor("_BaseColor", color);
            m_Block.SetColor("_Color", color);
            screen.SetPropertyBlock(m_Block);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
