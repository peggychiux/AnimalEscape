using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Match;
using UnityEngine;
using UnityEngine.AI;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 電腦玩家的找路資料（NavMesh），只在主機上、而且房間裡有電腦玩家時才建立。
    ///
    /// 不在編輯器裡預先烘焙：場景是程式產生的、三張地圖共用座標，所以在主機上用 Unity 內建的執行時建立（NavMeshBuilder）
    /// 從「目前開著的那張地圖」的碰撞算出來，換地圖時重建。建立是在背景做的（UpdateNavMeshDataAsync），不會卡住畫面；
    /// 建好之前 IsReady 是 false，電腦玩家先站著等。
    ///
    /// 不算障礙的東西：收容區的門（鎖著時電腦動物會自己撬開）、出口的門（混亂值滿了之後才會去）、角色、會被物理推動的東西（實驗室的球）、
    /// 觸發用的碰撞。實驗室的門一直算障礙（電腦動物不會走進去）。
    /// 身體大小用 GameConfig 的 botAgentRadius／botAgentHeight：高度 1.0 公尺，所以通風管（0.8）、植栽桌底下都不走，第一版先不教捷徑。
    ///
    /// 建兩份：一般的（水的擋牆算障礙）給兔子、猴子、浣熊；會游泳的（水的擋牆不算，池底可以走）給烏龜。
    /// 兩份用不同的 agentTypeID，查詢時用 Filter(會不會游泳) 分開，不會互相混到。
    /// </summary>
    public static class BotNavigation
    {
        // 三張地圖都是 80 x 60 公尺、原點在正中央；出口外的貨車停在牆外最多約 7 公尺，所以四邊各多留 12 公尺。
        // 高度從地板下一點到 6.5 公尺。
        static readonly Bounds k_Bounds = new Bounds(new Vector3(0f, 2.25f, 0f), new Vector3(104f, 9f, 84f));

        static NavMeshData s_Data;
        static NavMeshDataInstance s_Instance;
        static AsyncOperation s_Operation;
        static NavMeshData s_SwimData;
        static NavMeshDataInstance s_SwimInstance;
        static AsyncOperation s_SwimOperation;
        static int s_SwimAgentType;
        static bool s_HasSwimAgent; // agentTypeID 可能是負數，不能拿 -1 當「還沒建」
        static MapInfo s_BuiltFor;
        static float s_StartedAt;

        /// <summary>上一次建立花了幾秒（測試工具查看用）。</summary>
        public static float LastBuildSeconds { get; private set; }

        /// <summary>上一次建立用了幾個碰撞（測試工具查看用）。</summary>
        public static int LastSourceCount { get; private set; }

        /// <summary>目前這張地圖的找路資料建好了沒有。</summary>
        public static bool IsReady
        {
            get
            {
                MapRegistry registry = MapRegistry.Instance;
                bool ready = s_Operation != null && s_Operation.isDone && s_SwimOperation != null && s_SwimOperation.isDone
                    && s_BuiltFor != null && registry != null && registry.Active == s_BuiltFor;
                if (ready && LastBuildSeconds <= 0f)
                {
                    LastBuildSeconds = Mathf.Max(0.001f, Time.realtimeSinceStartup - s_StartedAt);
                }

                return ready;
            }
        }

        /// <summary>（主機，電腦玩家每一格呼叫）目前開著的地圖還沒有找路資料就開始建立。</summary>
        public static void EnsureBuilt()
        {
            MapRegistry registry = MapRegistry.Instance;
            MapInfo active = registry != null ? registry.Active : null;
            if (active == null || active == s_BuiltFor)
            {
                return;
            }

            Build(active);
        }

        static void Build(MapInfo map)
        {
            Clear();

            GameConfig config = GameConfig.Instance;
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(k_Bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);

            var ignored = new HashSet<Collider>();
            for (int i = 0; i < HabitatGate.Active.Count; i++)
            {
                if (HabitatGate.Active[i] != null && HabitatGate.Active[i].blocker != null)
                {
                    ignored.Add(HabitatGate.Active[i].blocker);
                }
            }

            for (int i = 0; i < ExitDoor.Active.Count; i++)
            {
                if (ExitDoor.Active[i] != null && ExitDoor.Active[i].blocker != null)
                {
                    ignored.Add(ExitDoor.Active[i].blocker);
                }
            }

            sources.RemoveAll(source => ShouldIgnore(source, ignored));
            LastSourceCount = sources.Count;

            NavMeshBuildSettings settings = Configure(NavMesh.GetSettingsByID(0), config);
            s_Data = new NavMeshData(settings.agentTypeID);
            s_Instance = NavMesh.AddNavMeshData(s_Data);
            s_Operation = NavMeshBuilder.UpdateNavMeshDataAsync(s_Data, settings, sources, k_Bounds);

            // 會游泳的：同樣的碰撞，拿掉水的擋牆。
            if (!s_HasSwimAgent)
            {
                s_SwimAgentType = NavMesh.CreateSettings().agentTypeID;
                s_HasSwimAgent = true;
            }

            var swimSources = sources.FindAll(source => !IsWaterBlocker(source));
            NavMeshBuildSettings swimSettings = Configure(NavMesh.GetSettingsByID(s_SwimAgentType), config);
            s_SwimData = new NavMeshData(swimSettings.agentTypeID);
            s_SwimInstance = NavMesh.AddNavMeshData(s_SwimData);
            s_SwimOperation = NavMeshBuilder.UpdateNavMeshDataAsync(s_SwimData, swimSettings, swimSources, k_Bounds);
            s_BuiltFor = map;
            s_StartedAt = Time.realtimeSinceStartup;
            LastBuildSeconds = 0f;
        }

        static NavMeshBuildSettings Configure(NavMeshBuildSettings settings, GameConfig config)
        {
            settings.agentRadius = config.botAgentRadius;
            settings.agentHeight = config.botAgentHeight;
            settings.agentClimb = 0.25f;
            settings.agentSlope = 40f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.1f;
            settings.overrideTileSize = true;
            settings.tileSize = 128;
            settings.minRegionArea = 1f;
            return settings;
        }

        static bool IsWaterBlocker(NavMeshBuildSource source)
        {
            return source.component != null && source.component.gameObject.layer == GameLayers.Water;
        }

        /// <summary>查詢用的條件：會游泳的（烏龜）用水可以走的那一份，其他用一般的。</summary>
        public static NavMeshQueryFilter Filter(bool swimmer)
        {
            var filter = new NavMeshQueryFilter();
            filter.agentTypeID = swimmer && s_HasSwimAgent ? s_SwimAgentType : 0;
            filter.areaMask = NavMesh.AllAreas;
            return filter;
        }

        static bool ShouldIgnore(NavMeshBuildSource source, HashSet<Collider> ignored)
        {
            Component component = source.component;
            if (component == null)
            {
                return false;
            }

            var collider = component as Collider;
            if (collider != null && (collider.isTrigger || ignored.Contains(collider)))
            {
                return true;
            }

            if (component.GetComponentInParent<PlayerAvatar>() != null)
            {
                return true;
            }

            Rigidbody body = component.GetComponentInParent<Rigidbody>();
            return body != null && !body.isKinematic;
        }

        /// <summary>收掉找路資料（主機關房、或要重建時）。</summary>
        public static void Clear()
        {
            if (s_Instance.valid)
            {
                s_Instance.Remove();
            }

            if (s_SwimInstance.valid)
            {
                s_SwimInstance.Remove();
            }

            s_Instance = default(NavMeshDataInstance);
            s_SwimInstance = default(NavMeshDataInstance);
            s_Data = null;
            s_SwimData = null;
            s_Operation = null;
            s_SwimOperation = null;
            s_BuiltFor = null;
        }

        /// <summary>這個位置附近（maxDistance 公尺內）最近的可以走的點。</summary>
        public static bool Snap(Vector3 position, float maxDistance, NavMeshQueryFilter filter, out Vector3 snapped)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(position, out hit, maxDistance, filter))
            {
                snapped = hit.position;
                return true;
            }

            snapped = position;
            return false;
        }

        /// <summary>一條路徑的長度（公尺）。</summary>
        public static float PathLength(NavMeshPath path)
        {
            Vector3[] corners = path.corners;
            float length = 0f;
            for (int i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return length;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Instance = default(NavMeshDataInstance);
            s_SwimInstance = default(NavMeshDataInstance);
            s_Data = null;
            s_SwimData = null;
            s_Operation = null;
            s_SwimOperation = null;
            s_SwimAgentType = 0;
            s_HasSwimAgent = false;
            s_BuiltFor = null;
            LastBuildSeconds = 0f;
            LastSourceCount = 0;
        }
    }
}
