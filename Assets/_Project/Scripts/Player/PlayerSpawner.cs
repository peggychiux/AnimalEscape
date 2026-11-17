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
    /// 規則：每一種身分都只能有一位（人類一位，四種動物各一隻）。
    /// 出生點跟著目前的地圖走（MapRegistry）：人類在研究室，動物在各自的收容區（門會重新鎖上），烏龜在池塘裡。
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

        // 主機端記錄：每位玩家目前的角色
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

        /// <summary>主機端：收掉所有人的角色，讓大家回到選身分（換地圖時用）。</summary>
        public void ResetAllRoles()
        {
            var slots = new List<PlayerSlot>(PlayerSlot.All);
            for (int i = 0; i < slots.Count; i++)
            {
                AssignRole(slots[i], Role.None, true);
            }
        }

        void OnClientConnected(ulong clientId)
        {
            if (m_Net == null || !m_Net.IsServer)
            {
                return;
            }

            if (m_Net.ConnectedClientsIds.Count > GameConfig.Instance.maxPlayers)
            {
                m_Net.DisconnectClient(clientId, "房間已滿");
                return;
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
            // 玩家離線時，他的角色會由 Netcode 自動清掉，這裡只要移除記錄。
            m_Avatars.Remove(clientId);
        }

        void OnServerStopped(bool wasHost)
        {
            m_Avatars.Clear();
        }

        /// <summary>
        /// 主機端：把玩家換成指定的身分。Role.None 表示只收掉角色，讓玩家回到選身分畫面。
        /// 被網住、被扛著、關在籠子裡的動物不能換身分（不然換一下就逃掉了）；在實驗室裡可以；force = true 時不管（換地圖時用）。
        /// </summary>
        public void AssignRole(PlayerSlot slot, Role role, bool force = false)
        {
            if (m_Net == null || !m_Net.IsServer || slot == null || !slot.IsSpawned)
            {
                return;
            }

            ulong clientId = slot.OwnerClientId;

            // 比賽中不能換身分（M4）；開始比賽、換地圖、離開遊戲這些由主機強制的不受影響。
            if (!force && MatchFlow.Instance != null && MatchFlow.Instance.IsPlaying)
            {
                return;
            }

            // 被網住、被扛著、關在籠子裡都不能換身分（不然換一下就逃掉了）；在實驗室裡可以（重新出生）。
            // 之後有了「比賽中」的流程（M4），比賽中一律不能換。
            PlayerAvatar current;
            if (!force && m_Avatars.TryGetValue(clientId, out current) && current != null && current.IsSpawned
                && current.Capture != null && current.Capture.State.Value != CaptureState.Free
                && current.Capture.State.Value != CaptureState.InLab && current.Capture.State.Value != CaptureState.Escaped)
            {
                return;
            }

            if (role != Role.None && IsRoleTakenByOther(slot, role))
            {
                slot.NotifyRoleDenied(role);
                return;
            }

            if (role != Role.None && avatarPrefab == null)
            {
                Debug.LogError("[PlayerSpawner] 沒有設定角色的預製物，請用選單 Tools > Animal Escape > 重建原型場景。");
                return;
            }

            // 先收掉舊角色
            PlayerAvatar old;
            if (m_Avatars.TryGetValue(clientId, out old) && old != null && old.IsSpawned)
            {
                old.GetComponent<NetworkObject>().Despawn(true);
            }

            m_Avatars.Remove(clientId);
            slot.CurrentRole.Value = role;
            slot.LeftMatch.Value = false;

            // 還沒有「一局」的流程：每次重新出生，被關的次數和「被門禁卡救過」都歸零。
            slot.TimesCaged.Value = 0;
            slot.ReleasedFromLab.Value = false;

            if (role == Role.None)
            {
                return;
            }

            // 動物每次出生都是關在自己的收容區裡，門要重新鎖上（烏龜的池塘沒有門，這一行對牠沒有作用）。
            if (Roles.IsAnimal(role) && MatchState.Instance != null)
            {
                MatchState.Instance.ServerSetHabitatOpen(role, false);
            }

            Vector3 position;
            Quaternion rotation;
            GetSpawnPoint(role, out position, out rotation);

            GameObject instance = Instantiate(avatarPrefab, position, rotation);
            var avatar = instance.GetComponent<PlayerAvatar>();
            var networkObject = instance.GetComponent<NetworkObject>();
            if (avatar == null || networkObject == null)
            {
                Debug.LogError("[PlayerSpawner] 角色的預製物上缺少 PlayerAvatar 或 NetworkObject。");
                Destroy(instance);
                return;
            }

            avatar.PendingRole = role;
            networkObject.SpawnWithOwnership(clientId, true);
            m_Avatars[clientId] = avatar;
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
            if (!m_Avatars.TryGetValue(slot.OwnerClientId, out current) || current == null || !current.IsSpawned
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
            for (int i = 0; i < slots.Count; i++)
            {
                PlayerSlot slot = slots[i];
                if (slot == null || !slot.IsSpawned || slot.CurrentRole.Value == Role.None)
                {
                    continue;
                }

                Role role = slot.CurrentRole.Value;
                PlayerAvatar avatar;
                if (!m_Avatars.TryGetValue(slot.OwnerClientId, out avatar) || avatar == null || !avatar.IsSpawned
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
                GetSpawnPoint(role, out position, out rotation);
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
