using AnimalEscape.Core;
using AnimalEscape.Players;
using Unity.Netcode;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 這一場比賽的共用狀態，由主機開房時產生一個，所有人都看得到。
    /// 目前只記錄用哪一張地圖；之後倒數計時、混亂值、比賽階段也會放在這裡。
    /// </summary>
    public class MatchState : NetworkBehaviour
    {
        public static MatchState Instance { get; private set; }

        /// <summary>主機在產生這個物件前先填好的地圖編號。</summary>
        [System.NonSerialized] public int PendingMapIndex;

        /// <summary>目前用哪一張地圖（MapRegistry.maps 的編號），由主機決定。</summary>
        public readonly NetworkVariable<int> MapIndex = new NetworkVariable<int>(0);

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
