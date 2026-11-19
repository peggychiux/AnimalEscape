using System.Collections.Generic;
using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 出口（M4）：每張地圖一個，在外牆上（離所有收容區都遠）。門外停著貨車。
    /// 混亂值滿 100% 之後，動物在門前按住 E 合作開門（越多隻越快）；門打開，貨車開始倒數。
    /// 走進貨車車廂就是「上了貨車」（CaptureState.Escaped，網子抓不到），走下車就變回自由。
    /// 車廂的入口只有 1.2 公尺高：動物都鑽得進去，人類（1.8 公尺）進不去，所以人類上不了車。
    ///
    /// 這個元件本身不連線：門開了沒有、開門進度記在 MatchFlow，這裡只照著開關門的碰撞、升起門板、換燈號。
    /// 物件的位置就是門洞正中央（牆面、地板高度），自己的 +Z 朝地圖外面（朝貨車）。
    /// </summary>
    public class ExitDoor : MonoBehaviour
    {
        public static readonly List<ExitDoor> Active = new List<ExitDoor>();

        public static ExitDoor Current
        {
            get { return Active.Count > 0 ? Active[0] : null; }
        }

        [Tooltip("門洞的寬度（公尺）")]
        public float width = 6f;

        [Tooltip("門板的外觀（打開時往上升）")]
        public Transform doorPanel;

        [Tooltip("門關著時擋路的碰撞")]
        public Collider blocker;

        [Tooltip("門上的燈：鎖著＝紅、解鎖＝黃、打開＝綠")]
        public Renderer lamp;

        [Tooltip("貨車車廂內部的範圍（世界座標）：中心和大小")]
        public Vector3 truckCenter;
        public Vector3 truckSize;

        static readonly Color k_Locked = new Color(0.95f, 0.18f, 0.15f);
        static readonly Color k_Unlocked = new Color(1f, 0.82f, 0.2f);
        static readonly Color k_Open = new Color(0.25f, 0.95f, 0.35f);

        Vector3 m_PanelClosed;
        float m_Lift;
        MaterialPropertyBlock m_Block;

        // 貨車入口的橫梁（只留 1.2 公尺高，人類進不去）和它前面的黃線；測試房間「逛地圖」時拿掉，人類也能進車廂看。
        Collider m_LintelCollider;
        Renderer[] m_LintelRenderers = new Renderer[0];

        /// <summary>門前、地圖裡面那一側的點（站這裡開門）。</summary>
        public Vector3 InsidePoint
        {
            get { return transform.position - transform.forward * 1.2f; }
        }

        /// <summary>這個位置是不是站在門前、可以開門的範圍（門洞寬度內、從牆面往地圖裡 reach 公尺以內）。</summary>
        public bool InOpeningZone(Vector3 position, float reach)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= width * 0.5f + 0.3f && local.z <= 0.2f && local.z >= -reach && Mathf.Abs(local.y) < 1.5f;
        }

        /// <summary>這個位置是不是在貨車車廂裡面。</summary>
        public bool InTruck(Vector3 position)
        {
            Vector3 offset = position - truckCenter;
            return Mathf.Abs(offset.x) <= truckSize.x * 0.5f && Mathf.Abs(offset.z) <= truckSize.z * 0.5f
                && offset.y >= -0.5f && offset.y <= truckSize.y;
        }

        void Awake()
        {
            if (doorPanel != null)
            {
                m_PanelClosed = doorPanel.localPosition;
            }

            m_Block = new MaterialPropertyBlock();

            // 名字照 PrototypeBuilder.BuildExit：出口底下的 Truck／Low Entrance、Entrance Stripe。
            var renderers = new List<Renderer>();
            Transform lintel = transform.Find("Truck/Low Entrance");
            if (lintel != null)
            {
                m_LintelCollider = lintel.GetComponent<Collider>();
                renderers.AddRange(lintel.GetComponentsInChildren<Renderer>());
            }

            Transform stripe = transform.Find("Truck/Entrance Stripe");
            if (stripe != null)
            {
                renderers.AddRange(stripe.GetComponentsInChildren<Renderer>());
            }

            m_LintelRenderers = renderers.ToArray();
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
            MatchFlow flow = MatchFlow.Instance;

            // 測試房間的「逛地圖」：每台電腦各自把門打開、拿掉貨車入口的橫梁，誰都能走進車廂看。
            // 這不是真的開門（MatchFlow.ExitOpen 沒變）：貨車不會倒數，走進車廂也不算上了貨車。
            bool exploring = MatchState.IsExploring;
            bool open = exploring || (flow != null && flow.ExitOpen.Value);
            if (blocker != null && blocker.enabled == open)
            {
                blocker.enabled = !open;
            }

            if (m_LintelCollider != null && m_LintelCollider.enabled == exploring)
            {
                m_LintelCollider.enabled = !exploring;
                for (int i = 0; i < m_LintelRenderers.Length; i++)
                {
                    if (m_LintelRenderers[i] != null)
                    {
                        m_LintelRenderers[i].enabled = !exploring;
                    }
                }
            }

            m_Lift = Mathf.MoveTowards(m_Lift, open ? 2.4f : 0f, 2.5f * Time.deltaTime);
            if (doorPanel != null)
            {
                doorPanel.localPosition = m_PanelClosed + Vector3.up * m_Lift;
            }

            if (lamp != null)
            {
                Color color = open ? k_Open : (flow != null && flow.ExitUnlocked ? k_Unlocked : k_Locked);
                lamp.GetPropertyBlock(m_Block);
                m_Block.SetColor("_BaseColor", color);
                m_Block.SetColor("_Color", color);
                lamp.SetPropertyBlock(m_Block);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
