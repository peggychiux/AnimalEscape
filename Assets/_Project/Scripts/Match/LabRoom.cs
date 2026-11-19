using System;
using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 實驗室：每張地圖一間，在研究室的角落，四面是不透明的牆、有屋頂、只有一道門。
    /// 籠子倒數歸零或第 3 次被抓的動物會被送進來（PlayerCapture.ServerSendToLab），在裡面開派對：
    /// 自由走動、踢球、做表情，用牆上的大螢幕看還在場上的隊友。隊友拿門禁卡在門外按住 E 刷卡，裡面的動物就全部放出來。
    ///
    /// 這個元件本身不連線，只負責：
    /// - 記住房間的範圍、門的位置、球和門禁卡的候選位置（主機用來擺東西、判定距離）；
    /// - 每台電腦各自的效果：派對燈換顏色、刷卡時門板升起來、門邊讀卡機的燈號；
    /// - 大螢幕：只有「這台電腦的玩家正在實驗室裡」時，才開一台小鏡頭跟著隊友拍、畫到螢幕上（每個人看自己選的隊友）。
    /// </summary>
    public class LabRoom : MonoBehaviour
    {
        /// <summary>目前開著的那張地圖上的實驗室（每張地圖只有一間）。</summary>
        public static readonly List<LabRoom> Active = new List<LabRoom>();

        public static LabRoom Current
        {
            get { return Active.Count > 0 ? Active[0] : null; }
        }

        [Header("範圍（世界座標）")]
        [Tooltip("房間內部的中心（地板高度）")]
        public Vector3 interiorCenter;

        [Tooltip("房間內部的大小（x、z 是地板範圍，y 是天花板高度）")]
        public Vector3 interiorSize;

        [Tooltip("門洞正中央、牆面上、地板高度的那一點")]
        public Vector3 doorPoint;

        [Tooltip("從房間裡往門外的方向（水平、長度 1）")]
        public Vector3 doorOutward = Vector3.forward;

        [Header("物件")]
        [Tooltip("門板的外觀（刷卡時往上升，碰撞不在這裡，門永遠是關的）")]
        public Transform doorPanel;

        [Tooltip("門邊讀卡機的燈")]
        public Renderer readerLamp;

        [Tooltip("大螢幕（材質要是 Unlit，畫面會蓋在主貼圖上）")]
        public Renderer screen;

        [Tooltip("大螢幕前的控台：站在這附近按 E 換人看")]
        public Vector3 consolePoint;

        [Tooltip("派對燈（只是外觀）")]
        public Light[] partyLights = Array.Empty<Light>();

        [Header("擺放位置")]
        [Tooltip("球的出生位置（主機開房、換地圖時在這裡放球）")]
        public Vector3[] ballSpots = Array.Empty<Vector3>();

        [Tooltip("門禁卡的候選位置（每次重新開始時隨機挑一個）")]
        public Vector3[] keycardSpots = Array.Empty<Vector3>();

        const float k_DoorOpenSeconds = 2.5f;
        static readonly Color k_LampLocked = new Color(0.95f, 0.18f, 0.15f);
        static readonly Color k_LampUsed = new Color(0.25f, 0.95f, 0.35f);
        static readonly Color k_ScreenOff = new Color(0.06f, 0.07f, 0.09f);

        Vector3 m_DoorPanelClosed;
        Collider m_DoorBlocker;   // 門的碰撞（平常一直都在）；測試房間「逛地圖」時關掉，大家都能走進來看
        MaterialPropertyBlock m_Block;
        Camera m_ScreenCamera;
        RenderTexture m_ScreenTexture;
        Material m_ScreenMaterial;
        bool m_ScreenOn;
        PlayerAvatar m_ScreenTarget;
        bool m_NextTargetRequested;
        Vector3 m_ScreenCameraPosition;
        bool m_ScreenCameraPlaced;

        /// <summary>（這台電腦）大螢幕正在拍的隊友；沒有隊友在場上（拍全景）或螢幕沒開時是 null。</summary>
        public PlayerAvatar ScreenTarget
        {
            get { return m_ScreenOn ? m_ScreenTarget : null; }
        }

        /// <summary>（這台電腦）大螢幕現在有沒有在播。</summary>
        public bool ScreenOn
        {
            get { return m_ScreenOn; }
        }

        /// <summary>門外、刷卡要站的那一點。</summary>
        public Vector3 OutsidePoint
        {
            get { return doorPoint + doorOutward * 0.9f; }
        }

        /// <summary>這個位置是不是在實驗室裡面。</summary>
        public bool Contains(Vector3 position)
        {
            Vector3 offset = position - interiorCenter;
            return Mathf.Abs(offset.x) <= interiorSize.x * 0.5f
                && Mathf.Abs(offset.z) <= interiorSize.z * 0.5f
                && offset.y >= -0.5f && offset.y <= interiorSize.y;
        }

        /// <summary>從這個位置到門外那一點的水平距離（刷卡用）。站在房間裡面的不算（回傳很大的數）。</summary>
        public float DistanceToDoorOutside(Vector3 position)
        {
            if (Contains(position))
            {
                return float.MaxValue;
            }

            Vector3 offset = position - OutsidePoint;
            if (Mathf.Abs(offset.y) > 1.5f)
            {
                return float.MaxValue;
            }

            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>從這個位置到控台的水平距離。</summary>
        public float DistanceToConsole(Vector3 position)
        {
            Vector3 offset = position - consolePoint;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>
        /// 主機用：房間裡一個放得下這個大小的空位（被送進來的動物站的位置）。從房間中央往外一圈一圈找，避開門口。
        /// isFree 回答「這一點放得下嗎」（呼叫的人自己用物理檢查）。
        /// </summary>
        public Vector3 FindInsideSpot(float radius, Func<Vector3, bool> isFree)
        {
            float halfX = interiorSize.x * 0.5f - radius - 0.6f;
            float halfZ = interiorSize.z * 0.5f - radius - 0.6f;
            Vector3 center = interiorCenter + Vector3.up * 0.05f;
            Vector3 door = doorPoint;
            for (int ring = 0; ring < 6; ring++)
            {
                int steps = ring == 0 ? 1 : ring * 8;
                for (int i = 0; i < steps; i++)
                {
                    float angle = (i / (float)steps) * Mathf.PI * 2f + ring * 0.4f;
                    var point = new Vector3(
                        center.x + Mathf.Clamp(Mathf.Cos(angle) * ring * 1.1f, -halfX, halfX),
                        center.y,
                        center.z + Mathf.Clamp(Mathf.Sin(angle) * ring * 1.1f, -halfZ, halfZ));

                    Vector3 toDoor = point - door;
                    toDoor.y = 0f;
                    if (toDoor.magnitude < 1.8f)
                    {
                        continue;
                    }

                    if (isFree == null || isFree(point))
                    {
                        return point;
                    }
                }
            }

            return center;
        }

        /// <summary>
        /// 主機用：門外第 index 個放人的位置（刷卡放出來的動物）。先試門口正前方，再往左右兩邊排開、再往外一排。
        /// </summary>
        public Vector3 FindReleaseSpot(int index, Func<Vector3, bool> isFree)
        {
            Vector3 side = Vector3.Cross(Vector3.up, doorOutward).normalized;
            float[] lateral = { 0f, -1.1f, 1.1f, -2.2f, 2.2f, 0f, -1.1f, 1.1f, -2.2f, 2.2f };
            float[] outward = { 1.5f, 1.5f, 1.5f, 1.5f, 1.5f, 2.7f, 2.7f, 2.7f, 2.7f, 2.7f };
            Vector3 origin = doorPoint + Vector3.up * 0.05f;
            int found = 0;
            for (int i = 0; i < lateral.Length; i++)
            {
                Vector3 point = origin + doorOutward * outward[i] + side * lateral[i];
                if (isFree != null && !isFree(point))
                {
                    continue;
                }

                if (found == index)
                {
                    return point;
                }

                found++;
            }

            return origin + doorOutward * (1.5f + index * 0.8f);
        }

        /// <summary>（這台電腦）大螢幕換下一位隊友。</summary>
        public void RequestNextScreenTarget()
        {
            m_NextTargetRequested = true;
        }

        void Awake()
        {
            if (doorPanel != null)
            {
                m_DoorPanelClosed = doorPanel.localPosition;

                // 門的碰撞和門板放在同一個父物件底下，名字是 Blocker（PrototypeBuilder.BuildLabRoom）。
                Transform blocker = doorPanel.parent != null ? doorPanel.parent.Find("Blocker") : null;
                m_DoorBlocker = blocker != null ? blocker.GetComponent<Collider>() : null;
            }

            m_Block = new MaterialPropertyBlock();
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
            SetScreenOn(false);
        }

        void OnDestroy()
        {
            if (m_ScreenTexture != null)
            {
                m_ScreenTexture.Release();
                Destroy(m_ScreenTexture);
            }

            if (m_ScreenCamera != null)
            {
                Destroy(m_ScreenCamera.gameObject);
            }

            if (m_ScreenMaterial != null)
            {
                Destroy(m_ScreenMaterial);
            }
        }

        void Update()
        {
            MatchState match = MatchState.Instance;
            UpdatePartyLights();
            UpdateDoor(match);
            UpdateLamp(match);

            PlayerCapture local = PlayerCapture.Local;
            bool localInLab = local != null && local.IsSpawned && local.IsInLab;
            SetScreenOn(localInLab);
        }

        void LateUpdate()
        {
            if (m_ScreenOn)
            {
                UpdateScreenCamera();
            }
        }

        // ------------------------------------------------------------------
        // 外觀效果
        // ------------------------------------------------------------------

        void UpdatePartyLights()
        {
            float t = Time.time;
            for (int i = 0; i < partyLights.Length; i++)
            {
                Light light = partyLights[i];
                if (light == null)
                {
                    continue;
                }

                float hue = Mathf.Repeat(t * 0.12f + i / (float)Mathf.Max(1, partyLights.Length), 1f);
                light.color = Color.HSVToRGB(hue, 0.75f, 1f);
            }
        }

        void UpdateDoor(MatchState match)
        {
            if (doorPanel == null)
            {
                return;
            }

            // 測試房間的「逛地圖」：門一直開著（每台電腦各自關掉門的碰撞、把門板升起來），誰都能走進來看。
            bool exploring = MatchState.IsExploring;
            if (m_DoorBlocker != null && m_DoorBlocker.enabled == exploring)
            {
                m_DoorBlocker.enabled = !exploring;
            }

            // 刷開的那一刻門板升起來一下（只是外觀；門的碰撞一直都在，人類不會趁機跑進來）。
            float lift = 0f;
            if (exploring)
            {
                lift = 2.1f;
            }
            else if (match != null)
            {
                float elapsed = (float)(PlayerCapture.Now - match.LabOpenedAt.Value);
                if (elapsed >= 0f && elapsed < k_DoorOpenSeconds)
                {
                    float up = Mathf.Clamp01(elapsed / 0.4f);
                    float down = Mathf.Clamp01((k_DoorOpenSeconds - elapsed) / 0.6f);
                    lift = Mathf.Min(up, down) * 2.1f;
                }
            }

            doorPanel.localPosition = m_DoorPanelClosed + Vector3.up * lift;
        }

        void UpdateLamp(MatchState match)
        {
            if (readerLamp == null)
            {
                return;
            }

            bool used = match != null && match.Keycard.Value == KeycardStatus.Used;
            Color color = used ? k_LampUsed : k_LampLocked;

            // 拿著卡的人在門口時燈會閃，提醒「可以刷」。
            if (!used && match != null && match.Keycard.Value == KeycardStatus.Held)
            {
                PlayerAvatar holder = match.KeycardHolderAvatar;
                if (holder != null && DistanceToDoorOutside(holder.transform.position) <= GameConfig.Instance.keycardSwipeRange
                    && Mathf.Repeat(Time.time * 2f, 1f) < 0.5f)
                {
                    color = new Color(1f, 0.85f, 0.2f);
                }
            }

            readerLamp.GetPropertyBlock(m_Block);
            m_Block.SetColor("_BaseColor", color);
            m_Block.SetColor("_Color", color);
            readerLamp.SetPropertyBlock(m_Block);
        }

        // ------------------------------------------------------------------
        // 大螢幕（每台電腦各自的鏡頭，只在自己在實驗室裡時才開）
        // ------------------------------------------------------------------

        void SetScreenOn(bool on)
        {
            if (on == m_ScreenOn)
            {
                return;
            }

            m_ScreenOn = on;
            if (on)
            {
                EnsureScreenCamera();
                m_ScreenCameraPlaced = false;
            }

            if (m_ScreenCamera != null)
            {
                m_ScreenCamera.enabled = on;
            }

            if (screen != null)
            {
                if (m_ScreenMaterial == null)
                {
                    m_ScreenMaterial = new Material(screen.sharedMaterial);
                    screen.sharedMaterial = m_ScreenMaterial;
                }

                m_ScreenMaterial.mainTexture = on ? m_ScreenTexture : null;
                m_ScreenMaterial.color = on ? Color.white : k_ScreenOff;
            }

            if (!on)
            {
                m_ScreenTarget = null;
            }
        }

        void EnsureScreenCamera()
        {
            if (m_ScreenCamera != null)
            {
                return;
            }

            m_ScreenTexture = new RenderTexture(640, 360, 16, RenderTextureFormat.ARGB32);
            m_ScreenTexture.name = "Lab Screen";
            m_ScreenTexture.Create();

            var cameraObject = new GameObject("Lab Screen Camera");
            cameraObject.transform.SetParent(transform, false);
            m_ScreenCamera = cameraObject.AddComponent<Camera>();
            m_ScreenCamera.targetTexture = m_ScreenTexture;
            m_ScreenCamera.fieldOfView = 55f;
            m_ScreenCamera.nearClipPlane = 0.1f;
            m_ScreenCamera.farClipPlane = 200f;
            m_ScreenCamera.depth = -10f;
            m_ScreenCamera.enabled = false;
        }

        /// <summary>還在場上（不在實驗室）的動物，照玩家編號排；自己不算。</summary>
        static bool IsWatchable(PlayerAvatar avatar)
        {
            return avatar != null && avatar.IsSpawned && !avatar.IsMine && Roles.IsAnimal(avatar.AvatarRole.Value)
                && avatar.Capture != null && !avatar.Capture.IsInLab;
        }

        static PlayerAvatar NextWatchable(PlayerAvatar current)
        {
            PlayerAvatar first = null;
            PlayerAvatar after = null;
            int currentNumber = current != null ? current.PlayerNumber : -1;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (!IsWatchable(avatar))
                {
                    continue;
                }

                int number = avatar.PlayerNumber;
                if (first == null || number < first.PlayerNumber)
                {
                    first = avatar;
                }

                if (number > currentNumber && (after == null || number < after.PlayerNumber))
                {
                    after = avatar;
                }
            }

            return after ?? first;
        }

        void UpdateScreenCamera()
        {
            if (m_ScreenCamera == null)
            {
                return;
            }

            bool valid = IsWatchable(m_ScreenTarget);
            if (!valid || m_NextTargetRequested)
            {
                PlayerAvatar next = NextWatchable(valid ? m_ScreenTarget : null);
                if (next != m_ScreenTarget)
                {
                    m_ScreenCameraPlaced = false;
                }

                m_ScreenTarget = next;
            }

            m_NextTargetRequested = false;
            Transform view = m_ScreenCamera.transform;
            if (m_ScreenTarget == null)
            {
                // 沒有隊友在場上：拍整張地圖的全景。
                view.SetPositionAndRotation(new Vector3(0f, 62f, -50f), Quaternion.Euler(52f, 0f, 0f));
                m_ScreenCamera.fieldOfView = 60f;
                m_ScreenCameraPlaced = false;
                return;
            }

            // 跟在隊友背後、高一點的位置往下看；中間被擋住就拉近。
            Transform target = m_ScreenTarget.transform;
            RoleSettings settings = m_ScreenTarget.Settings;
            Vector3 pivot = target.position + Vector3.up * (settings.height + 0.4f);
            Vector3 back = -target.forward;
            back.y = 0f;
            back = back.sqrMagnitude > 0.0001f ? back.normalized : Vector3.back;
            Vector3 wanted = pivot + back * 4.2f + Vector3.up * 1.6f;
            Vector3 toWanted = wanted - pivot;
            float distance = toWanted.magnitude;
            RaycastHit hit;
            if (Physics.SphereCast(pivot, 0.2f, toWanted / distance, out hit, distance, GameLayers.SightMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerAvatar>() == null)
            {
                wanted = pivot + toWanted / distance * Mathf.Max(0.6f, hit.distance - 0.1f);
            }

            m_ScreenCameraPosition = m_ScreenCameraPlaced
                ? Vector3.Lerp(m_ScreenCameraPosition, wanted, 1f - Mathf.Exp(-6f * Time.deltaTime))
                : wanted;
            m_ScreenCameraPlaced = true;
            m_ScreenCamera.fieldOfView = 55f;
            view.SetPositionAndRotation(m_ScreenCameraPosition,
                Quaternion.LookRotation(target.position + Vector3.up * (settings.height * 0.6f) - m_ScreenCameraPosition));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
