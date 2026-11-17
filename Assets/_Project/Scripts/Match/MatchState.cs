using AnimalEscape.Core;
using AnimalEscape.Players;
using Unity.Netcode;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 這一場比賽的共用狀態，由主機開房時產生一個，所有人都看得到。
    /// 目前記錄用哪一張地圖、哪幾個收容區的門已經被撬開；之後倒數計時、混亂值、比賽階段也會放在這裡。
    /// </summary>
    public class MatchState : NetworkBehaviour
    {
        public static MatchState Instance { get; private set; }

        /// <summary>主機在產生這個物件前先填好的地圖編號。</summary>
        [System.NonSerialized] public int PendingMapIndex;

        /// <summary>目前用哪一張地圖（MapRegistry.maps 的編號），由主機決定。</summary>
        public readonly NetworkVariable<int> MapIndex = new NetworkVariable<int>(0);

        /// <summary>
        /// 哪幾個收容區的門已經開了：第 n 個位元對應 Role 的數值（例如兔子是 2）。
        /// 動物每次出生（選身分）時，牠的收容區會重新鎖上；換地圖時全部重新鎖上。
        /// </summary>
        public readonly NetworkVariable<int> OpenHabitats = new NetworkVariable<int>(0);

        /// <summary>這種動物的收容區，門是不是已經開了。</summary>
        public bool IsHabitatOpen(Role role)
        {
            return (OpenHabitats.Value & (1 << (int)role)) != 0;
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
            }

            MapIndex.OnValueChanged += OnMapChanged;
            ShowMap(MapIndex.Value);
        }

        public override void OnNetworkDespawn()
        {
            MapIndex.OnValueChanged -= OnMapChanged;
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

            if (PlayerSpawner.Instance != null)
            {
                PlayerSpawner.Instance.ResetAllRoles();
            }

            // 新地圖的收容區全部從鎖著開始。
            OpenHabitats.Value = 0;
            registry.Select(index);
            MapIndex.Value = index;
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
    }
}
