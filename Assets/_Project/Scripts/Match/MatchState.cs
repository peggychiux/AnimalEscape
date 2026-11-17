using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Players;
using Unity.Netcode;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>門禁卡現在在哪裡。</summary>
    public enum KeycardStatus : byte
    {
        /// <summary>還沒放（還沒開房，或這張地圖沒有實驗室）。</summary>
        None = 0,

        /// <summary>在地上，自由的動物走過去就撿起來。</summary>
        OnGround = 1,

        /// <summary>有動物拿著（浮在牠頭上，大家都看得到）。</summary>
        Held = 2,

        /// <summary>已經刷過了（整場只能用一次）。</summary>
        Used = 3
    }

    /// <summary>
    /// 這一場比賽的共用狀態，由主機開房時產生一個，所有人都看得到。
    /// 目前記錄：用哪一張地圖、哪幾個收容區的門已經被撬開、門禁卡在哪裡、實驗室什麼時候被刷開；
    /// 實驗室的球也由這裡放（主機）。之後倒數計時、混亂值、比賽階段也會放在這裡。
    /// </summary>
    public class MatchState : NetworkBehaviour
    {
        public static MatchState Instance { get; private set; }

        /// <summary>LabBall、KeycardHolder 用這個值表示「沒有」。</summary>
        public const ulong None = ulong.MaxValue;

        [Tooltip("實驗室裡的球（含 NetworkObject 的預製物，要登記在網路預製物清單裡）")]
        public GameObject labBallPrefab;

        /// <summary>主機在產生這個物件前先填好的地圖編號。</summary>
        [System.NonSerialized] public int PendingMapIndex;

        /// <summary>目前用哪一張地圖（MapRegistry.maps 的編號），由主機決定。</summary>
        public readonly NetworkVariable<int> MapIndex = new NetworkVariable<int>(0);

        /// <summary>
        /// 哪幾個收容區的門已經開了：第 n 個位元對應 Role 的數值（例如兔子是 2）。
        /// 動物每次出生（選身分）時，牠的收容區會重新鎖上；換地圖時全部重新鎖上。
        /// </summary>
        public readonly NetworkVariable<int> OpenHabitats = new NetworkVariable<int>(0);

        // 注意宣告順序：KeycardHolder、KeycardPosition 要比 Keycard 早（同 PlayerCapture.DropPoint 的理由），
        // 狀態變成「有人拿著／掉在地上」的那一刻，拿著的人和位置已經是新的。

        /// <summary>拿著門禁卡的角色（PlayerAvatar 的網路物件編號）；沒有就是 None。</summary>
        public readonly NetworkVariable<ulong> KeycardHolder = new NetworkVariable<ulong>(None);

        /// <summary>門禁卡在地上時的位置（地板高度）。</summary>
        public readonly NetworkVariable<Vector3> KeycardPosition = new NetworkVariable<Vector3>();

        public readonly NetworkVariable<KeycardStatus> Keycard = new NetworkVariable<KeycardStatus>(KeycardStatus.None);

        /// <summary>這一次刷開實驗室時放出了幾隻（介面的公告用）。要比 LabOpenedAt 早。</summary>
        public readonly NetworkVariable<int> LabReleasedCount = new NetworkVariable<int>(0);

        /// <summary>實驗室最近一次被刷開的時刻（主機時間）；門板動畫、公告用。</summary>
        public readonly NetworkVariable<double> LabOpenedAt = new NetworkVariable<double>(-100.0);

        // 只有主機用
        readonly List<NetworkObject> m_Balls = new List<NetworkObject>();
        bool m_ResetLabPending;
        Vector3 m_LastHolderPosition;
        double m_PickupAllowedAt;

        /// <summary>這種動物的收容區，門是不是已經開了。</summary>
        public bool IsHabitatOpen(Role role)
        {
            return (OpenHabitats.Value & (1 << (int)role)) != 0;
        }

        /// <summary>拿著門禁卡的角色；沒有就是 null。</summary>
        public PlayerAvatar KeycardHolderAvatar
        {
            get
            {
                if (Keycard.Value != KeycardStatus.Held || KeycardHolder.Value == None)
                {
                    return null;
                }

                NetworkManager net = NetworkManager.Singleton;
                NetworkObject found;
                return net != null && net.SpawnManager != null
                    && net.SpawnManager.SpawnedObjects.TryGetValue(KeycardHolder.Value, out found) && found != null
                    ? found.GetComponent<PlayerAvatar>()
                    : null;
            }
        }

        /// <summary>主機端：所有收容區都鎖上（開始比賽時）。</summary>
        public void ServerLockAllHabitats()
        {
            if (IsServer && OpenHabitats.Value != 0)
            {
                OpenHabitats.Value = 0;
            }
        }

        /// <summary>主機端：打開或鎖上某一種動物的收容區。</summary>
        public void ServerSetHabitatOpen(Role role, bool open)
        {
            if (!IsServer || role == Role.None)
            {
                return;
            }

            int bit = 1 << (int)role;
            int current = OpenHabitats.Value;
            int wanted = open ? (current | bit) : (current & ~bit);
            if (wanted != current)
            {
                OpenHabitats.Value = wanted;
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                MapIndex.Value = PendingMapIndex;

                // 球和門禁卡等下一格再放（這一格地圖可能還沒切好，也避免在生成途中再生成別的網路物件）。
                m_ResetLabPending = true;
            }

            MapIndex.OnValueChanged += OnMapChanged;
            ShowMap(MapIndex.Value);
        }

        public override void OnNetworkDespawn()
        {
            MapIndex.OnValueChanged -= OnMapChanged;
            m_Balls.Clear();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 主機端：換地圖。所有人的角色會先收掉（回到選身分），因為原本站的位置在新地圖上可能是牆或水。
        /// </summary>
        public void SetMap(int index)
        {
            MapRegistry registry = MapRegistry.Instance;
            if (!IsServer || registry == null)
            {
                return;
            }

            index = registry.Wrap(index);
            if (index == MapIndex.Value)
            {
                return;
            }

            // 比賽中不能換地圖（介面上也不會出現按鈕）。
            MatchFlow flow = GetComponent<MatchFlow>();
            if (flow != null && flow.IsPlaying)
            {
                return;
            }

            if (PlayerSpawner.Instance != null)
            {
                PlayerSpawner.Instance.ResetAllRoles();
            }

            // 新地圖的收容區全部從鎖著開始。
            OpenHabitats.Value = 0;
            registry.Select(index);
            MapIndex.Value = index;

            // 新地圖的實驗室：重新放球、門禁卡重新隨機一個位置；比賽回到準備階段（混亂值歸零、東西恢復）。
            ServerResetLab(-1);
            if (flow != null)
            {
                flow.ServerResetToLobby();
            }

            // 電腦玩家用原本的身分在新地圖重新出生。
            if (PlayerSpawner.Instance != null)
            {
                PlayerSpawner.Instance.RespawnBots();
            }
        }

        void OnMapChanged(int previous, int current)
        {
            ShowMap(current);
        }

        static void ShowMap(int index)
        {
            if (MapRegistry.Instance != null)
            {
                MapRegistry.Instance.Show(index);
            }
        }

        void Update()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            if (m_ResetLabPending)
            {
                m_ResetLabPending = false;
                ServerResetLab(-1);
            }

            ServerUpdateKeycard();
        }

        // ------------------------------------------------------------------
        // 主機端：實驗室的球、門禁卡
        // ------------------------------------------------------------------

        /// <summary>
        /// 主機端：重新開始實驗室：收掉舊的球、在現在這張地圖的實驗室放新的球，門禁卡放回候選位置之一。
        /// keycardSpot 是第幾個候選位置（從 0 開始），-1 表示隨機。
        /// </summary>
        public void ServerResetLab(int keycardSpot)
        {
            if (!IsServer)
            {
                return;
            }

            for (int i = 0; i < m_Balls.Count; i++)
            {
                if (m_Balls[i] != null && m_Balls[i].IsSpawned)
                {
                    m_Balls[i].Despawn(true);
                }
            }

            m_Balls.Clear();
            LabRoom lab = LabRoom.Current;
            if (lab != null && labBallPrefab != null)
            {
                for (int i = 0; i < lab.ballSpots.Length; i++)
                {
                    GameObject instance = Instantiate(labBallPrefab, lab.ballSpots[i], Quaternion.identity);
                    var ball = instance.GetComponent<LabBall>();
                    if (ball != null)
                    {
                        ball.HomePosition = lab.ballSpots[i];
                    }

                    var networkObject = instance.GetComponent<NetworkObject>();
                    networkObject.Spawn(true);
                    m_Balls.Add(networkObject);
                }
            }

            ServerResetKeycard(keycardSpot);
            LabReleasedCount.Value = 0;
            LabOpenedAt.Value = -100.0;
        }

        /// <summary>主機端：門禁卡放回候選位置之一（-1 表示隨機），變成「在地上」。</summary>
        public void ServerResetKeycard(int spot)
        {
            if (!IsServer)
            {
                return;
            }

            LabRoom lab = LabRoom.Current;
            if (lab == null || lab.keycardSpots.Length == 0)
            {
                KeycardHolder.Value = None;
                Keycard.Value = KeycardStatus.None;
                return;
            }

            if (spot < 0 || spot >= lab.keycardSpots.Length)
            {
                spot = Random.Range(0, lab.keycardSpots.Length);
            }

            KeycardHolder.Value = None;
            KeycardPosition.Value = lab.keycardSpots[spot];
            Keycard.Value = KeycardStatus.OnGround;
            m_PickupAllowedAt = PlayerCapture.Now;
        }

        /// <summary>主機端：直接把門禁卡交給某個角色（測試工具用）。</summary>
        public void ServerGiveKeycard(PlayerAvatar avatar)
        {
            if (!IsServer || avatar == null || !avatar.IsSpawned)
            {
                return;
            }

            KeycardHolder.Value = avatar.NetworkObjectId;
            m_LastHolderPosition = avatar.transform.position;
            Keycard.Value = KeycardStatus.Held;
        }

        /// <summary>
        /// 主機端，每一格：拿著卡的動物被網住（或不見了）卡就掉在原地；卡在地上時，自由的動物走過去就撿起來。
        /// </summary>
        void ServerUpdateKeycard()
        {
            GameConfig config = GameConfig.Instance;
            if (Keycard.Value == KeycardStatus.Held)
            {
                PlayerAvatar holder = KeycardHolderAvatar;
                if (holder == null || !holder.IsSpawned)
                {
                    ServerDropKeycard(m_LastHolderPosition);
                    return;
                }

                // 被網住、被扛著、被關、被送進實驗室時卡會掉；上了貨車的動物照樣拿著（下車還可以去刷）。
                PlayerCapture capture = holder.Capture;
                CaptureState state = capture != null ? capture.State.Value : CaptureState.Free;
                if (state == CaptureState.Netted || state == CaptureState.Carried || state == CaptureState.Caged
                    || state == CaptureState.InLab)
                {
                    ServerDropKeycard(holder.transform.position);
                    return;
                }

                m_LastHolderPosition = holder.transform.position;
                return;
            }

            if (Keycard.Value != KeycardStatus.OnGround || PlayerCapture.Now < m_PickupAllowedAt)
            {
                return;
            }

            Vector3 card = KeycardPosition.Value;
            PlayerAvatar best = null;
            float bestDistance = config.keycardPickupRange;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar == null || !avatar.IsSpawned || !Roles.IsAnimal(avatar.AvatarRole.Value)
                    || avatar.Capture == null || avatar.Capture.State.Value != CaptureState.Free)
                {
                    continue;
                }

                Vector3 offset = avatar.transform.position - card;
                if (Mathf.Abs(offset.y) > 1.5f)
                {
                    continue;
                }

                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance <= bestDistance)
                {
                    best = avatar;
                    bestDistance = distance;
                }
            }

            if (best != null)
            {
                ServerGiveKeycard(best);
            }
        }

        void ServerDropKeycard(Vector3 position)
        {
            // 落在地上；在水裡的話放到最近的岸邊（不然除了烏龜沒人撿得到）。
            RaycastHit hit;
            if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out hit, 10f, GameLayers.SightMask, QueryTriggerInteraction.Ignore))
            {
                position = hit.point;
            }

            Vector3 shore;
            if (WaterZone.IsInWater(position) && WaterZone.TryFindShore(position, 0.3f, point => true, out shore))
            {
                position = shore;
            }

            KeycardHolder.Value = None;
            KeycardPosition.Value = position;
            Keycard.Value = KeycardStatus.OnGround;
            m_PickupAllowedAt = PlayerCapture.Now + GameConfig.Instance.keycardPickupDelay;
        }

        /// <summary>現在實驗室裡的動物（每台電腦都能查）。</summary>
        public static void CollectLabAnimals(List<PlayerCapture> into)
        {
            into.Clear();
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar != null && avatar.IsSpawned && avatar.Capture != null && avatar.Capture.IsInLab)
                {
                    into.Add(avatar.Capture);
                }
            }
        }

        static readonly List<PlayerCapture> s_LabAnimals = new List<PlayerCapture>();

        /// <summary>
        /// 主機端：swiper 刷卡成功。實驗室裡所有的動物放到門外、給保護時間；門禁卡用掉（整場一次）。
        /// 被放出來的動物記一筆「救出來過」，下次被抓就直接回實驗室。回傳放出了幾隻。
        /// </summary>
        public int ServerUseKeycard(PlayerAvatar swiper)
        {
            LabRoom lab = LabRoom.Current;
            if (!IsServer || lab == null || swiper == null || Keycard.Value != KeycardStatus.Held
                || KeycardHolder.Value != swiper.NetworkObjectId)
            {
                return 0;
            }

            CollectLabAnimals(s_LabAnimals);
            if (s_LabAnimals.Count == 0)
            {
                return 0;
            }

            KeycardHolder.Value = None;
            Keycard.Value = KeycardStatus.Used;
            LabReleasedCount.Value = s_LabAnimals.Count;
            LabOpenedAt.Value = PlayerCapture.Now;

            for (int i = 0; i < s_LabAnimals.Count; i++)
            {
                PlayerCapture animal = s_LabAnimals[i];
                RoleSettings settings = animal.Avatar.Settings;
                float radius = settings.radius;
                float height = Mathf.Max(settings.height, radius * 2f);
                Vector3 spot = lab.FindReleaseSpot(i, point => PlayerCapture.IsSpaceFree(point, radius, height, Physics.DefaultRaycastLayers));
                animal.ServerReleaseFromLab(spot);
            }

            int count = s_LabAnimals.Count;
            s_LabAnimals.Clear();
            return count;
        }
    }
}
