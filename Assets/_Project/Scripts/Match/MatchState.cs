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

        /// <summary>這是正式房間還是測試房間（主機開房時決定，之後不變）。</summary>
        public readonly NetworkVariable<RoomMode> Mode = new NetworkVariable<RoomMode>(RoomMode.Official);

        /// <summary>主機開房用的連法（只用這台電腦／同一個 Wi-Fi／線上）。連進來的人用這個顯示「這是什麼房間」，不是自己怎麼連的。</summary>
        public readonly NetworkVariable<LinkMode> RoomLink = new NetworkVariable<LinkMode>(LinkMode.Lan);

        /// <summary>測試模式的規則開關（TestRule 的位元）；正式房間一律是 0。</summary>
        public readonly NetworkVariable<int> TestRules = new NetworkVariable<int>(0);

        /// <summary>測試模式要從哪個階段開始（房主在測試房間選，大家都看得到）。</summary>
        public readonly NetworkVariable<TestStart> StartPhase = new NetworkVariable<TestStart>(TestStart.MatchStart);

        /// <summary>測試房間的設定畫面開著（還沒按「開始測試」，或按了 F1 面板的「回到測試房間」）。</summary>
        public readonly NetworkVariable<bool> InTestSetup = new NetworkVariable<bool>(false);

        /// <summary>測試房間的「出生在哪裡」（房主選，大家都看得到；正式房間一律是預設）。出生點由 PlayerSpawner.GetSpawnPoint 照這個改。</summary>
        public readonly NetworkVariable<TestSpawn> SpawnSpace = new NetworkVariable<TestSpawn>(TestSpawn.Default);

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
        ulong m_DroppedById = None;   // 自己按 Q 丟下卡的角色：要先走開才撿得回去

        // ------------------------------------------------------------------
        // 房間種類與測試模式的規則（每台電腦都可以問）
        // ------------------------------------------------------------------

        public bool IsTest
        {
            get { return Mode.Value == RoomMode.Test; }
        }

        /// <summary>這個測試規則有沒有打開（正式房間一律 false）。</summary>
        public bool HasRule(TestRule rule)
        {
            return IsTest && (TestRules.Value & (int)rule) != 0;
        }

        static bool CurrentHasRule(TestRule rule)
        {
            MatchState match = Instance;
            return match != null && match.IsSpawned && match.HasRule(rule);
        }

        /// <summary>現在的房間是測試房間。</summary>
        public static bool IsTestRoom
        {
            get { return Instance != null && Instance.IsSpawned && Instance.IsTest; }
        }

        /// <summary>同一種身分可以重複（測試模式的規則）。</summary>
        public static bool AllowsDuplicateRoles
        {
            get { return CurrentHasRule(TestRule.DuplicateRoles); }
        }

        /// <summary>比賽中可以換身分（測試模式的規則）。</summary>
        public static bool AllowsSwapDuringMatch
        {
            get { return CurrentHasRule(TestRule.SwapAnytime); }
        }

        /// <summary>不用湊齊人類和動物也能開始（測試模式的規則）。</summary>
        public static bool SkipsTeamRequirement
        {
            get { return CurrentHasRule(TestRule.NoTeamNeeded); }
        }

        /// <summary>只有房主能分配身分（測試模式把「大家自己選角色」關掉時）。</summary>
        public static bool OnlyHostAssignsRoles
        {
            get { return IsTestRoom && !Instance.HasRule(TestRule.SelfPick); }
        }

        /// <summary>測試房間的設定畫面現在開著。</summary>
        public static bool TestSetupOpen
        {
            get { return IsTestRoom && Instance.InTestSetup.Value; }
        }

        /// <summary>
        /// 測試房間的「逛地圖」：選好角色進地圖到處走，不比賽、不計時，收容區全開；
        /// 這段期間換身分、換地圖都不會把收容區鎖回去，比賽條也沒有「開始比賽」。
        /// </summary>
        public static bool IsExploring
        {
            get { return IsTestRoom && Instance.StartPhase.Value == TestStart.Explore && !Instance.InTestSetup.Value; }
        }

        /// <summary>主機端：打開或關掉一個測試規則（只有測試房間有效）。</summary>
        public void ServerSetRule(TestRule rule, bool on)
        {
            if (!IsServer || !IsTest)
            {
                return;
            }

            int value = on ? (TestRules.Value | (int)rule) : (TestRules.Value & ~(int)rule);
            TestRules.Value = value;
            RoomOptions.Rules = (TestRule)value;
        }

        /// <summary>主機端：測試房間的「出生在哪裡」（下次出生、開始測試、回出生點時生效）。</summary>
        public void ServerSetSpawnSpace(TestSpawn space)
        {
            if (IsServer && IsTest)
            {
                SpawnSpace.Value = space;
                RoomOptions.SpawnSpace = space;
            }
        }

        /// <summary>主機端：測試要從哪個階段開始。</summary>
        public void ServerSetStartPhase(TestStart phase)
        {
            if (IsServer && IsTest)
            {
                StartPhase.Value = phase;
                RoomOptions.StartPhase = phase;
            }
        }

        /// <summary>
        /// 主機端：「開始測試」（測試房間的大按鈕、F1 面板的「重新開始這一局」、結算的「再來一局」都走這裡）。
        /// 準備中：只是關掉設定畫面（自由練習）；其他階段：開始比賽（不用湊齊隊伍時照樣能開始），再把狀態推到那個階段。
        /// </summary>
        public bool ServerBeginTest(out string reason)
        {
            reason = "";
            if (!IsServer || !IsTest)
            {
                reason = "不是測試房間的主機";
                return false;
            }

            MatchFlow flow = GetComponent<MatchFlow>();
            TestStart phase = StartPhase.Value;
            if (phase == TestStart.Lobby || phase == TestStart.Explore)
            {
                if (flow != null && flow.Phase.Value != MatchPhase.Lobby)
                {
                    flow.ServerResetToLobby();
                }

                if (PlayerSpawner.Instance != null)
                {
                    PlayerSpawner.Instance.RespawnAllForMatch();
                }

                // 逛地圖：收容區全開，動物出生在收容區裡也能直接走出去。
                if (phase == TestStart.Explore)
                {
                    ServerOpenAllHabitats();
                }
                else
                {
                    ServerLockAllHabitats();
                }

                InTestSetup.Value = false;
                return true;
            }

            if (flow == null)
            {
                reason = "找不到比賽流程";
                return false;
            }

            if (flow.IsPlaying)
            {
                flow.ServerResetToLobby();
            }

            if (!flow.ServerStartMatch(out reason, !HasRule(TestRule.NoTeamNeeded)))
            {
                return false;
            }

            if (phase >= TestStart.Escaped)
            {
                ServerOpenAllHabitats();
            }

            if (phase >= TestStart.ChaosFull)
            {
                flow.ServerSetChaos(100f);
            }

            if (phase >= TestStart.ExitOpen)
            {
                flow.ServerOpenExit();
            }

            InTestSetup.Value = false;
            return true;
        }

        /// <summary>主機端：回到測試房間的設定畫面（比賽回到準備中，所有人回出生點，身分保留）。</summary>
        public void ServerReturnToTestSetup()
        {
            if (!IsServer || !IsTest)
            {
                return;
            }

            MatchFlow flow = GetComponent<MatchFlow>();
            if (flow != null)
            {
                flow.ServerResetToLobby();
            }

            if (PlayerSpawner.Instance != null)
            {
                PlayerSpawner.Instance.RespawnAllForMatch();
            }

            ServerLockAllHabitats();
            ServerResetLab(-1);
            InTestSetup.Value = true;
        }

        /// <summary>主機端：所有動物的收容區都打開。</summary>
        public void ServerOpenAllHabitats()
        {
            if (!IsServer)
            {
                return;
            }

            for (int i = 0; i < Roles.Selectable.Length; i++)
            {
                if (Roles.IsAnimal(Roles.Selectable[i]))
                {
                    ServerSetHabitatOpen(Roles.Selectable[i], true);
                }
            }
        }

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

                // 主選單選的房間種類；測試房間一開就是設定畫面。
                Mode.Value = RoomOptions.Mode;
                RoomLink.Value = Networking.ConnectionManager.Instance != null ? Networking.ConnectionManager.Instance.Link : RoomOptions.Link;
                TestRules.Value = RoomOptions.Mode == RoomMode.Test ? (int)RoomOptions.Rules : 0;
                StartPhase.Value = RoomOptions.StartPhase;
                SpawnSpace.Value = RoomOptions.Mode == RoomMode.Test ? RoomOptions.SpawnSpace : TestSpawn.Default;
                InTestSetup.Value = RoomOptions.Mode == RoomMode.Test;

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

            // 測試房間的「逛地圖」：新地圖的收容區也全開。
            if (IsExploring)
            {
                ServerOpenAllHabitats();
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
            m_DroppedById = None;
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
            m_DroppedById = None;
        }

        /// <summary>
        /// 主機端：拿著卡的動物自己按 Q 丟下（丟在腳邊，給隊友撿）。
        /// 丟下的那一隻要先走開（離卡超過 keycardPickupRange＋keycardDropLeaveMargin）才撿得回去，別的動物照常撿。
        /// </summary>
        public void ServerDropKeycardBy(PlayerAvatar holder)
        {
            if (!IsServer || holder == null || Keycard.Value != KeycardStatus.Held || KeycardHolder.Value != holder.NetworkObjectId)
            {
                return;
            }

            ServerDropKeycard(holder.transform.position);
            m_DroppedById = holder.NetworkObjectId;
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
                float vertical = Mathf.Abs(offset.y);
                offset.y = 0f;
                float distance = offset.magnitude;
                if (m_DroppedById != None && avatar.NetworkObjectId == m_DroppedById)
                {
                    // 自己按 Q 丟下的：走開之後才算數，不然丟下的那一刻又撿回來。
                    if (vertical > 1.5f || distance > config.keycardPickupRange + config.keycardDropLeaveMargin)
                    {
                        m_DroppedById = None;
                    }

                    continue;
                }

                if (vertical > 1.5f)
                {
                    continue;
                }

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
            m_DroppedById = None;
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
