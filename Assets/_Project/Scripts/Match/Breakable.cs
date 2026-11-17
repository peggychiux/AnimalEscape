using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 地圖上可以破壞的東西（M4）：花盆、燒杯、飼料桶、紙箱堆、大盆栽、實驗機器…。動物走到旁邊按 E（小東西按一下、中大的要按住），
    /// 東西倒下來，全館混亂值上升，人類在一段距離內聽得到。壞了這一局就不會恢復。
    ///
    /// 這個元件本身不連線：有沒有壞記在 MatchFlow.BrokenMask（第 index 個位元），每台電腦照它擺「立著」或「倒下」，
    /// 剛倒下的那一刻在本機播聲音、給人類畫面邊緣的提示。編號（index）每張地圖從 0 排起，由建場景的程式指定。
    /// 物件的位置就是東西的底部中心（地板或桌面上）。
    ///
    /// 讓動物認得出哪些東西可以弄倒（使用者 2026-11-17：原本和一般擺設長得一樣，不夠明顯）：
    /// 動物的畫面上，看得到的東西頭上有橘色標記（GameHud.DrawBreakMarkers）；站到碰得到的距離時，
    /// 按 E 會弄倒的那一個整個閃橘黃色（這裡的 UpdateHighlight，只在本機改顏色，不影響別人的畫面）。
    /// </summary>
    public class Breakable : MonoBehaviour
    {
        /// <summary>目前開著的那張地圖上所有可以破壞的東西。</summary>
        public static readonly List<Breakable> Active = new List<Breakable>();

        /// <summary>本機玩家現在按 E 會弄倒（或正在推）的那一個的編號；-1 表示沒有。由 PlayerTask 的擁有者每一格更新。</summary>
        public static int LocalTarget = -1;

        [Tooltip("這張地圖裡的編號（0～63），對應 MatchFlow.BrokenMask 的位元")]
        public int index;

        public BreakSize size = BreakSize.Small;

        [Tooltip("給人看的名字（例如「花盆」），介面提示用")]
        public string displayName = "";

        [Tooltip("東西的半徑（公尺，從中心到邊緣），判斷碰不碰得到用")]
        public float radius = 0.3f;

        [Tooltip("倒下時會轉動的外觀（它的原點在東西的底部）")]
        public Transform visual;

        [Tooltip("立著的時候擋路的碰撞（大東西才有）；倒下之後關掉")]
        public Collider blocker;

        const float k_FallSeconds = 0.35f;

        static readonly Color k_HighlightColor = new Color(1f, 0.85f, 0.25f);
        static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int s_ColorId = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock s_Block;

        Renderer[] m_Renderers;
        Color[] m_BaseColors;
        bool m_Highlighted;

        bool m_Initialized;
        bool m_Broken;
        float m_FallTime = 1f;
        float m_FallSide = 1f;
        Quaternion m_Upright;

        /// <summary>立著的時候，東西的頂端比底部高多少（公尺），標記畫在這上面。</summary>
        public float TopHeight { get; private set; } = 0.5f;

        /// <summary>這一局是不是已經壞了（所有電腦一致）。</summary>
        public bool IsBroken
        {
            get { return MatchFlow.Instance != null && MatchFlow.Instance.IsBroken(index); }
        }

        /// <summary>從這個位置到東西邊緣的水平距離；高度差太多（例如東西在高櫃上）就回傳很大的數。</summary>
        public float DistanceFrom(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            if (offset.y < -2.2f || offset.y > 1.5f)
            {
                return float.MaxValue;
            }

            offset.y = 0f;
            return Mathf.Max(0f, offset.magnitude - radius);
        }

        /// <summary>離這個位置最近、碰得到、還沒壞的東西；沒有就是 null。</summary>
        public static Breakable FindInReach(Vector3 position, float range)
        {
            Breakable best = null;
            float bestDistance = range;
            for (int i = 0; i < Active.Count; i++)
            {
                Breakable item = Active[i];
                if (item == null || item.IsBroken)
                {
                    continue;
                }

                float distance = item.DistanceFrom(position);
                if (distance <= bestDistance)
                {
                    best = item;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>目前這張地圖上編號是 index 的東西；沒有就是 null。</summary>
        public static Breakable Find(int index)
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

        void Awake()
        {
            if (visual != null)
            {
                m_Upright = visual.localRotation;
            }

            // 往哪邊倒每個東西固定（看起來比較自然），只是外觀。
            m_FallSide = (index % 2 == 0) ? 1f : -1f;

            // 外觀的高度和原本的顏色（閃爍時以它為底），從場景裡的外觀直接量，不用另外存。
            m_Renderers = (visual != null ? visual : transform).GetComponentsInChildren<Renderer>(true);
            m_BaseColors = new Color[m_Renderers.Length];
            bool hasBounds = false;
            var bounds = new Bounds(transform.position, Vector3.zero);
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                Material material = m_Renderers[i].sharedMaterial;
                m_BaseColors[i] = material == null ? Color.white
                    : (material.HasProperty(s_BaseColorId) ? material.GetColor(s_BaseColorId)
                        : (material.HasProperty(s_ColorId) ? material.GetColor(s_ColorId) : Color.white));
                if (hasBounds)
                {
                    bounds.Encapsulate(m_Renderers[i].bounds);
                }
                else
                {
                    bounds = m_Renderers[i].bounds;
                    hasBounds = true;
                }
            }

            if (hasBounds)
            {
                TopHeight = Mathf.Max(0.2f, bounds.max.y - transform.position.y);
            }
        }

        void OnEnable()
        {
            if (!Active.Contains(this))
            {
                Active.Add(this);
            }

            m_Initialized = false;
        }

        void OnDisable()
        {
            Active.Remove(this);
            SetHighlight(false);
        }

        void Update()
        {
            UpdateFall();
            SetHighlight(!m_Broken && LocalTarget == index);
        }

        void UpdateFall()
        {
            bool broken = IsBroken;
            if (!m_Initialized)
            {
                // 剛開始（或剛換到這張地圖、剛加入房間）：直接擺成現在的樣子，不播聲音。
                m_Initialized = true;
                m_Broken = broken;
                m_FallTime = broken ? k_FallSeconds : 0f;
                Apply();
                return;
            }

            if (broken != m_Broken)
            {
                m_Broken = broken;
                m_FallTime = 0f;
                if (broken)
                {
                    PlayCrash();
                }
            }

            if (m_FallTime < k_FallSeconds)
            {
                m_FallTime = Mathf.Min(k_FallSeconds, m_FallTime + Time.deltaTime);
                Apply();
            }
        }

        /// <summary>按 E 會弄倒的那一個：外觀在原本的顏色和橘黃色之間閃（只改本機的畫面）。</summary>
        void SetHighlight(bool on)
        {
            if (m_Renderers == null)
            {
                return;
            }

            if (!on)
            {
                if (m_Highlighted)
                {
                    m_Highlighted = false;
                    for (int i = 0; i < m_Renderers.Length; i++)
                    {
                        if (m_Renderers[i] != null)
                        {
                            m_Renderers[i].SetPropertyBlock(null);
                        }
                    }
                }

                return;
            }

            if (s_Block == null)
            {
                s_Block = new MaterialPropertyBlock();
            }

            m_Highlighted = true;
            float pulse = 0.65f + 0.3f * Mathf.Sin(Time.unscaledTime * 8f);
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                if (m_Renderers[i] == null)
                {
                    continue;
                }

                Color color = Color.Lerp(m_BaseColors[i], k_HighlightColor, pulse);
                s_Block.Clear();
                s_Block.SetColor(s_BaseColorId, color);
                s_Block.SetColor(s_ColorId, color);
                m_Renderers[i].SetPropertyBlock(s_Block);
            }
        }

        void Apply()
        {
            float t = m_Broken ? Mathf.Clamp01(m_FallTime / k_FallSeconds) : 1f;
            if (visual != null)
            {
                // 以底部為軸往一邊倒下（倒到 85 度，看起來是躺在地上）。
                float angle = m_Broken ? Mathf.Lerp(0f, 85f, t * t) : 0f;
                visual.localRotation = Quaternion.Euler(0f, 0f, angle * m_FallSide) * m_Upright;
            }

            if (blocker != null)
            {
                blocker.enabled = !m_Broken;
            }
        }

        /// <summary>
        /// 剛倒下：所有人都聽得到撞擊聲（照距離變小聲），但只有人類的畫面邊緣會出現提示，範圍照大小（GameConfig）。
        /// 實驗室裡的人聽不到（和其他聲音一樣）。
        /// </summary>
        void PlayCrash()
        {
            GameConfig config = GameConfig.Instance;
            PlayerAvatar listener = PlayerSounds.LocalListener();
            if (listener == null)
            {
                return;
            }

            float range = config.GetBreakHearRange(size);
            float distance = Vector3.Distance(listener.transform.position, transform.position);
            if (distance > range)
            {
                return;
            }

            float loudness = 1f - distance / Mathf.Max(0.1f, range);
            if (listener.AvatarRole.Value == Role.Human)
            {
                SoundCues.Add(SoundKind.Crash, GetInstanceID(), transform.position, loudness, distance);
            }

            AudioSource.PlayClipAtPoint(SoundBank.Crash, transform.position,
                config.soundVolume * Mathf.Lerp(0.25f, 1f, loudness) * (size == BreakSize.Large ? 1f : (size == BreakSize.Medium ? 0.8f : 0.6f)));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
            LocalTarget = -1;
            s_Block = null;
        }
    }
}
