using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Match;
using Unity.Netcode;
using UnityEngine;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 主機端的玩家管理（判定都由主機處理）：
    /// 1. 玩家連進來時，幫他產生一個玩家狀態（PlayerSlot）。
    /// 2. 玩家選好身分後，在對應的出生點產生角色（PlayerAvatar）；換身分時先收掉舊的再產生新的。
    /// 3. 開房時產生這一場的共用狀態（MatchState），裡面記錄用哪一張地圖。
    /// 規則：每一種身分都只能有一位（人類一位，四種動物各一隻）。測試房間可以打開「同一種身分可以重複」，
    /// 重複的人出生在同一個出生點旁邊，左右錯開。
    /// 出生點跟著目前的地圖走（MapRegistry）：人類在研究室，動物在各自的收容區（門會重新鎖上），烏龜在池塘裡。
    /// 4. 電腦玩家（AddBot／RemoveBot）：主機自己產生一個沒有連線的玩家狀態（擁有者是主機），角色由主機上的 AnimalBot 操作。
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        public static PlayerSpawner Instance { get; private set; }

        [Tooltip("玩家狀態的預製物（PlayerSlot）")]
        public GameObject slotPrefab;

        [Tooltip("角色的預製物（PlayerAvatar），人類和動物共用")]
        public GameObject avatarPrefab;

        [Tooltip("比賽共用狀態的預製物（MatchState）")]
        public GameObject matchStatePrefab;

        NetworkManager m_Net;

        // 主機端記錄：每位玩家目前的角色。鍵是玩家狀態（PlayerSlot）的 NetworkObjectId，
        // 不是連線編號：電腦玩家的擁有者是主機，連線編號和主機自己一樣。
        readonly Dictionary<ulong, PlayerAvatar> m_Avatars = new Dictionary<ulong, PlayerAvatar>();

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            m_Net = NetworkManager.Singleton;
            if (m_Net != null)
            {
                m_Net.OnClientConnectedCallback += OnClientConnected;
                m_Net.OnClientDisconnectCallback += OnClientDisconnected;
                m_Net.OnServerStopped += OnServerStopped;
                m_Net.OnServerStarted += OnServerStarted;
            }
        }

        void OnDestroy()
        {
            if (m_Net != null)
            {
                m_Net.OnClientConnectedCallback -= OnClientConnected;
                m_Net.OnClientDisconnectCallback -= OnClientDisconnected;
                m_Net.OnServerStopped -= OnServerStopped;
                m_Net.OnServerStarted -= OnServerStarted;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        void OnServerStarted()
        {
            if (m_Net == null || !m_Net.IsServer || matchStatePrefab == null || MatchState.Instance != null)
            {
                return;
            }

            // 開房時用大廳選的那張地圖。
            GameObject instance = Instantiate(matchStatePrefab);
            var state = instance.GetComponent<MatchState>();
            var networkObject = instance.GetComponent<NetworkObject>();
            if (state == null || networkObject == null)
            {
                Debug.LogError("[PlayerSpawner] 比賽狀態的預製物上缺少 MatchState 或 NetworkObject。");
                Destroy(instance);
                return;
            }

            state.PendingMapIndex = MapRegistry.Instance != null ? MapRegistry.Instance.SelectedIndex : 0;
            networkObject.Spawn(true);
        }

        /// <summary>
        /// 主機端：收掉所有人的角色，讓大家回到選身分（換地圖時用）。電腦玩家也先收掉，
        /// 新地圖開好之後由 RespawnBots 用原本的身分重新出生（不然它們會一直停在「選擇中」）。
        /// 測試房間連真人也保留身分（在新地圖照原本的身分重新出生），不用每換一次地圖就重選。
        /// </summary>
        public void ResetAllRoles()
        {
            m_BotRolesToRestore.Clear();
            bool keepEveryone = MatchState.IsTestRoom;
            var slots = new List<PlayerSlot>(PlayerSlot.All);
            for (int i = 0; i < slots.Count; i++)
            {
                PlayerSlot slot = slots[i];
                if (slot != null && (slot.IsBotSlot || keepEveryone) && slot.CurrentRole.Value != Role.None)
                {
                    m_BotRolesToRestore[slot] = slot.CurrentRole.Value;
                }

                AssignRole(slot, Role.None, true);
            }
        }

        readonly Dictionary<PlayerSlot, Role> m_BotRolesToRestore = new Dictionary<PlayerSlot, Role>();

        /// <summary>主機端（換地圖之後）：電腦玩家（測試房間是所有人）用原本的身分在新地圖重新出生。</summary>
        public void RespawnBots()
        {
            foreach (KeyValuePair<PlayerSlot, Role> pair in m_BotRolesToRestore)
            {
                if (pair.Key != null && pair.Key.IsSpawned)
                {
                    AssignRole(pair.Key, pair.Value, true);
                }
            }

            m_BotRolesToRestore.Clear();
        }

        // ------------------------------------------------------------------
        // 電腦玩家
        // ------------------------------------------------------------------

        /// <summary>動物身分的預設順序（電腦動物沒指定身分時，挑第一個還沒人用的）。</summary>
        static readonly Role[] k_BotRoleOrder = { Role.Rabbit, Role.Monkey, Role.Raccoon, Role.Turtle };

        /// <summary>房間裡有幾個電腦玩家。</summary>
        public static int BotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < PlayerSlot.All.Count; i++)
                {
                    if (PlayerSlot.All[i] != null && PlayerSlot.All[i].IsBotSlot)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 主機端：加一個電腦動物。preferred 是想要的身分（Role.None 表示隨便，挑第一個還沒人用的動物）。
        /// 失敗時回傳 null，reason 寫原因（房間滿了、比賽中、四種動物都有人了…）。
        /// </summary>
        public PlayerSlot AddBot(Role preferred, out string reason)
        {
            reason = "";
            if (m_Net == null || !m_Net.IsServer || slotPrefab == null)
            {
                reason = "不是主機";
                return null;
            }

            if (MatchFlow.Instance != null && MatchFlow.Instance.IsPlaying)
            {
                reason = "比賽中不能加";
                return null;
            }

            if (PlayerSlot.All.Count >= GameConfig.Instance.maxPlayers)
            {
                reason = "房間滿了（最多 " + GameConfig.Instance.maxPlayers + " 人，電腦也算）";
                return null;
            }

            Role role = Role.None;
            bool duplicates = MatchState.AllowsDuplicateRoles;
            if (Roles.IsAnimal(preferred) && (duplicates || !IsRoleTakenByOther(null, preferred)))
            {
                role = preferred;
            }
            else if (preferred != Role.None)
            {
                reason = Roles.IsAnimal(preferred) ? preferred + " 已經有人了" : "電腦玩家目前只能當動物";
                return null;
            }
            else
            {
                for (int i = 0; i < k_BotRoleOrder.Length; i++)
                {
                    if (!IsRoleTakenByOther(null, k_BotRoleOrder[i]))
                    {
                        role = k_BotRoleOrder[i];
                        break;
                    }
                }

                // 測試房間可以重複：四種都有人了，就再多一隻兔子。
                if (role == Role.None && duplicates)
                {
                    role = k_BotRoleOrder[0];
                }
            }

            if (role == Role.None)
            {
                reason = "四種動物都有人了";
                return null;
            }

            GameObject instance = Instantiate(slotPrefab);
            var networkObject = instance.GetComponent<NetworkObject>();
            var slot = instance.GetComponent<PlayerSlot>();
            if (networkObject == null || slot == null)
            {
                Destroy(instance);
                reason = "玩家狀態的預製物有問題";
                return null;
            }

            slot.PendingSeat = FindFreeSeat();
            slot.PendingIsBot = true;
            networkObject.Spawn(true);
            AssignRole(slot, role, true);
            return slot;
        }

        /// <summary>主機端：移除一個電腦玩家（角色和玩家狀態都收掉）。</summary>
        public bool RemoveBot(PlayerSlot bot)
        {
            if (m_Net == null || !m_Net.IsServer || bot == null || !bot.IsSpawned || !bot.IsBotSlot)
            {
                return false;
            }

            AssignRole(bot, Role.None, true);
            m_BotRolesToRestore.Remove(bot);
            bot.GetComponent<NetworkObject>().Despawn(true);
            return true;
        }

        /// <summary>主機端：移除編號最大的那個電腦玩家。沒有電腦玩家就回傳 false。</summary>
        public bool RemoveLastBot()
        {
            PlayerSlot last = null;
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot slot = PlayerSlot.All[i];
                if (slot != null && slot.IsSpawned && slot.IsBotSlot && (last == null || slot.PlayerNumber > last.PlayerNumber))
                {
                    last = slot;
                }
            }

            return RemoveBot(last);
        }

        /// <summary>主機端：某個玩家狀態目前的角色（沒有就是 null）。</summary>
        public PlayerAvatar GetAvatar(PlayerSlot slot)
        {
            PlayerAvatar avatar;
            return slot != null && m_Avatars.TryGetValue(slot.NetworkObjectId, out avatar) && avatar != null && avatar.IsSpawned
                ? avatar
                : null;
        }

        void OnClientConnected(ulong clientId)
        {
            if (m_Net == null || !m_Net.IsServer)
            {
                return;
            }

            // 人數上限包含電腦玩家；真人進來時位子不夠，就先讓一個電腦玩家出去。
            if (m_Net.ConnectedClientsIds.Count + BotCount > GameConfig.Instance.maxPlayers)
            {
                if (!RemoveLastBot() || m_Net.ConnectedClientsIds.Count > GameConfig.Instance.maxPlayers)
                {
                    m_Net.DisconnectClient(clientId, "房間已滿");
                    return;
                }
            }

            if (slotPrefab == null)
            {
                Debug.LogError("[PlayerSpawner] 沒有設定玩家狀態的預製物，請用選單 Tools > Animal Escape > 重建原型場景。");
                return;
            }

            // 這位玩家已經有玩家狀態就不重複產生。
            NetworkClient client;
            if (m_Net.ConnectedClients.TryGetValue(clientId, out client) && client.PlayerObject != null)
            {
                return;
            }

            GameObject instance = Instantiate(slotPrefab);
            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError("[PlayerSpawner] 玩家狀態的預製物上沒有 NetworkObject。");
                Destroy(instance);
                return;
            }

            var slot = instance.GetComponent<PlayerSlot>();
            if (slot != null)
            {
                slot.PendingSeat = FindFreeSeat();
            }

            // destroyWithScene = true：換場景或關閉房間時會一起清掉。
            networkObject.SpawnAsPlayerObject(clientId, true);
        }

        /// <summary>目前沒人用的最小座位號碼（從 1 開始）。</summary>
        static int FindFreeSeat()
        {
            int seat = 1;
            while (IsSeatTaken(seat))
            {
                seat++;
            }

            return seat;
        }

        static bool IsSeatTaken(int seat)
        {
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot slot = PlayerSlot.All[i];
                if (slot != null && slot.PlayerNumber == seat)
                {
                    return true;
                }
            }

            return false;
        }

        void OnClientDisconnected(ulong clientId)
        {
            // 玩家離線時，他的角色會由 Netcode 自動清掉，這裡只要移除記錄（已經不在的、和屬於這位玩家的）。
            var stale = new List<ulong>();
            foreach (KeyValuePair<ulong, PlayerAvatar> pair in m_Avatars)
            {
                PlayerAvatar avatar = pair.Value;
                if (avatar == null || !avatar.IsSpawned || (!avatar.IsBot && avatar.OwnerClientId == clientId))
                {
                    stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                m_Avatars.Remove(stale[i]);
            }
        }

        void OnServerStopped(bool wasHost)
        {
            m_Avatars.Clear();
            m_BotRolesToRestore.Clear();
            BotNavigation.Clear();
        }

        /// <summary>
        /// 主機端：把玩家換成指定的身分。Role.None 表示只收掉角色，讓玩家回到選身分畫面。
        /// 被網住、被扛著、關在籠子裡的動物不能換身分（不然換一下就逃掉了）；在實驗室裡可以；force = true 時不管（換地圖時用）。
        /// inPlace = true：新角色出生在舊角色站的地方（測試模式 F1 面板的「原地換」），收容區也不會重新鎖上。
        /// 回傳有沒有換成功。
        /// </summary>
        public bool AssignRole(PlayerSlot slot, Role role, bool force = false, bool inPlace = false)
        {
            if (m_Net == null || !m_Net.IsServer || slot == null || !slot.IsSpawned)
            {
                return false;
            }

            ulong clientId = slot.OwnerClientId;
            ulong key = slot.NetworkObjectId;

            // 比賽中不能換身分（M4）；開始比賽、換地圖、離開遊戲這些由主機強制的不受影響。測試房間可以打開「比賽中隨時換」。
            if (!force && MatchFlow.Instance != null && MatchFlow.Instance.IsPlaying && !MatchState.AllowsSwapDuringMatch)
            {
                slot.NotifyRoleDenied(role, RoleDenial.DuringMatch);
                return false;
            }

            // 被網住、被扛著、關在籠子裡都不能換身分（不然換一下就逃掉了）；在實驗室裡可以（重新出生）。
            PlayerAvatar current;
            if (!force && m_Avatars.TryGetValue(key, out current) && current != null && current.IsSpawned
                && current.Capture != null && current.Capture.State.Value != CaptureState.Free
                && current.Capture.State.Value != CaptureState.InLab && current.Capture.State.Value != CaptureState.Escaped)
            {
                slot.NotifyRoleDenied(role, RoleDenial.Captured);
                return false;
            }

            if (role != Role.None && !MatchState.AllowsDuplicateRoles && IsRoleTakenByOther(slot, role))
            {
                slot.NotifyRoleDenied(role, RoleDenial.Taken);
                return false;
            }

            if (role != Role.None && avatarPrefab == null)
            {
                Debug.LogError("[PlayerSpawner] 沒有設定角色的預製物，請用選單 Tools > Animal Escape > 重建原型場景。");
                return false;
            }

            // 先收掉舊角色（原地換的話，先記下它站在哪裡）
            PlayerAvatar old;
            bool hasOldSpot = false;
            Vector3 oldPosition = Vector3.zero;
            Quaternion oldRotation = Quaternion.identity;
            if (m_Avatars.TryGetValue(key, out old) && old != null && old.IsSpawned)
            {
                hasOldSpot = true;
                oldPosition = old.transform.position;
                oldRotation = Quaternion.Euler(0f, old.transform.eulerAngles.y, 0f);
                old.GetComponent<NetworkObject>().Despawn(true);
            }

            inPlace = inPlace && hasOldSpot;

            m_Avatars.Remove(key);
            slot.CurrentRole.Value = role;
            slot.LeftMatch.Value = false;

            // 還沒有「一局」的流程：每次重新出生，被關的次數和「被門禁卡救過」都歸零。
            slot.TimesCaged.Value = 0;
            slot.ReleasedFromLab.Value = false;

            if (role == Role.None)
            {
                return true;
            }

            // 動物每次出生都是關在自己的收容區裡，門要重新鎖上（烏龜的池塘沒有門，這一行對牠沒有作用）。
            // 原地換身分時不鎖；同一種身分重複時，另一位已經在場上，也不要把他關回去；測試房間「逛地圖」時也不鎖。
            if (Roles.IsAnimal(role) && MatchState.Instance != null && !inPlace && CountOthersWithRole(slot, role) == 0
                && !MatchState.IsExploring)
            {
                MatchState.Instance.ServerSetHabitatOpen(role, false);
            }

            Vector3 position;
            Quaternion rotation;
            if (inPlace)
            {
                position = oldPosition;
                rotation = oldRotation;
            }
            else
            {
                GetSpawnPoint(role, CountOthersWithRole(slot, role), out position, out rotation);
            }

            GameObject instance = Instantiate(avatarPrefab, position, rotation);
            var avatar = instance.GetComponent<PlayerAvatar>();
            var networkObject = instance.GetComponent<NetworkObject>();
            if (avatar == null || networkObject == null)
            {
                Debug.LogError("[PlayerSpawner] 角色的預製物上缺少 PlayerAvatar 或 NetworkObject。");
                Destroy(instance);
                return false;
            }

            avatar.PendingRole = role;
            avatar.PendingIsBot = slot.IsBotSlot;
            avatar.PendingSlotId = slot.NetworkObjectId;
            networkObject.SpawnWithOwnership(clientId, true);
            m_Avatars[key] = avatar;
            return true;
        }

        /// <summary>
        /// 主機端：這位玩家在實驗室裡按了「離開遊戲」但留在房間裡（主機自己就是這樣：主機一離開房間，整個房間就關了）。
        /// 收掉他的角色、標記「離開了這一局」；這局算他沒逃出去。只有角色在實驗室裡時才有效。
        /// </summary>
        public void LeaveMatch(PlayerSlot slot)
        {
            if (m_Net == null || !m_Net.IsServer || slot == null || !slot.IsSpawned)
            {
                return;
            }

            PlayerAvatar current;
            if (!m_Avatars.TryGetValue(slot.NetworkObjectId, out current) || current == null || !current.IsSpawned
                || current.Capture == null || !current.Capture.IsInLab)
            {
                return;
            }

            AssignRole(slot, Role.None, true);
            slot.LeftMatch.Value = true;
            if (MatchFlow.Instance != null && MatchFlow.Instance.IsPlaying)
            {
                slot.Result.Value = MatchResult.Left;
            }
        }

        /// <summary>
        /// 主機端（開始比賽時）：每位選好身分的玩家回到自己身分的出生點，狀態全部清掉（被網住、被關、在實驗室、在貨車上…），
        /// 被關的次數歸零。還沒選身分的玩家這一局就不參加。收容區由 MatchFlow 另外全部鎖上。
        ///
        /// 角色不重新產生，而是原地重設再搬回出生點：重新產生的話，正在走動的玩家送出的最後幾筆位置會送到已經被收掉的角色上，
        /// 主機會跳 Netcode 的警告（Deferred OnSpawn … not received within the timeout，2026-11-17 實測）。
        /// </summary>
        public void RespawnAllForMatch()
        {
            var slots = new List<PlayerSlot>(PlayerSlot.All);
            slots.RemoveAll(item => item == null);
            slots.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
            var ordinals = new Dictionary<Role, int>();
            for (int i = 0; i < slots.Count; i++)
            {
                PlayerSlot slot = slots[i];
                if (slot == null || !slot.IsSpawned || slot.CurrentRole.Value == Role.None)
                {
                    continue;
                }

                Role role = slot.CurrentRole.Value;
                int ordinal;
                ordinals.TryGetValue(role, out ordinal);
                ordinals[role] = ordinal + 1;

                PlayerAvatar avatar;
                if (!m_Avatars.TryGetValue(slot.NetworkObjectId, out avatar) || avatar == null || !avatar.IsSpawned
                    || avatar.AvatarRole.Value != role || avatar.Capture == null)
                {
                    // 沒有角色（不應該發生）就照舊產生一個。
                    AssignRole(slot, role, true);
                    continue;
                }

                slot.TimesCaged.Value = 0;
                slot.ReleasedFromLab.Value = false;
                slot.LeftMatch.Value = false;

                Vector3 position;
                Quaternion rotation;
                GetSpawnPoint(role, ordinal, out position, out rotation);
                avatar.Capture.ServerResetForMatch(position, rotation);
            }
        }

        /// <summary>這個身分是不是已經被別人選走了（每一種身分只能有一位）。</summary>
        static bool IsRoleTakenByOther(PlayerSlot requester, Role role)
        {
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot other = PlayerSlot.All[i];
                if (other != null && other != requester && other.CurrentRole.Value == role)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>除了 slot 自己，還有幾位是這個身分（測試房間可以重複）。</summary>
        static int CountOthersWithRole(PlayerSlot slot, Role role)
        {
            int count = 0;
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot other = PlayerSlot.All[i];
                if (other != null && other != slot && other.CurrentRole.Value == role)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 出生點。ordinal 是同一個身分的第幾位（從 0 開始）：第 0 位站在出生點上，之後左右交錯 0.9 公尺
        /// （測試房間可以重複身分，不然大家會疊在同一個點上）。
        /// </summary>
        public static void GetSpawnPoint(Role role, int ordinal, out Vector3 position, out Quaternion rotation)
        {
            GetSpawnPoint(role, out position, out rotation);
            if (ordinal <= 0)
            {
                return;
            }

            float side = ((ordinal + 1) / 2) * 0.9f * (ordinal % 2 == 1 ? 1f : -1f);
            position += rotation * Vector3.right * side;
        }

        static void GetSpawnPoint(Role role, out Vector3 position, out Quaternion rotation)
        {
            MapInfo map = MapRegistry.Instance != null ? MapRegistry.Instance.Active : null;
            Transform point = map != null ? map.GetSpawn(role) : null;
            if (point != null)
            {
                position = point.position;
                rotation = point.rotation;
            }
            else
            {
                Debug.LogWarning("[PlayerSpawner] 這張地圖沒有設定 " + role + " 的出生點，先放在地圖正中央。請重建原型場景。");
                position = new Vector3(0f, 0.05f, 0f);
                rotation = Quaternion.identity;
            }
        }
    }
}
