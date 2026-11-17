using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Match;
using UnityEngine;
using UnityEngine.AI;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 電腦動物的大腦（第一版）。只在主機上，由 PlayerAvatar 在電腦玩家的角色生成時加上，不連線。
    ///
    /// 它不碰任何遊戲規則，只做「真人會按的鍵」：移動用 PlayerMovement.SetExternalInput（和測試工具的 walk 同一個入口），
    /// 按 E 用 PlayerTask／PlayerLockpick 的 SetExternalHold、ExternalTap，掙脫用 PlayerCapture.RequestStruggle。
    /// 判定照樣全在主機的那些程式裡，所以電腦動物不會作弊，也不會比真人多做什麼。
    ///
    /// 每 GameConfig.botThinkInterval 秒想一次要做什麼，優先順序：
    ///   被網住或被扛著 → 連按掙脫
    ///   關在籠子、實驗室、貨車上 → 站著
    ///   關在自己的收容區 → 走到門邊撬鎖
    ///   看到人類靠近 → 往遠離人類、走得到的地方跑
    ///   混亂值滿了 → 出口還沒開就去開門，開了就上貨車
    ///   其他 → 找最近、走得到的東西弄倒；都沒有就閒晃
    /// 第一版不做：救隊友、刷門禁卡、鑽通風管和桌底、跳圍欄（找路資料本來就不走這些地方，見 BotNavigation）。
    /// </summary>
    public class AnimalBot : MonoBehaviour
    {
        public enum Goal
        {
            Waiting,
            Idle,
            Struggle,
            PickLock,
            Flee,
            OpenExit,
            BoardTruck,
            Break,
            Wander
        }

        /// <summary>主機上所有電腦動物（測試工具查看用）。</summary>
        public static readonly List<AnimalBot> All = new List<AnimalBot>();

        /// <summary>測試用：所有電腦動物站著不動（被網住時照樣會掙脫）。測試工具的 bot freeze on／off。</summary>
        public static bool FreezeForTesting;

        const float k_CornerReach = 0.35f;
        const float k_ProgressDistance = 0.4f;
        const float k_StuckSeconds = 1.2f;

        PlayerAvatar m_Avatar;
        PlayerCapture m_Capture;
        PlayerMovement m_Movement;
        PlayerTask m_Task;
        PlayerLockpick m_Lockpick;

        NavMeshPath m_Path;
        Vector3[] m_Corners = new Vector3[0];
        int m_CornerIndex;
        bool m_HasPath;
        bool m_Direct;
        Vector3 m_DirectTarget;
        Vector3 m_Destination;

        float m_NextThinkAt;
        float m_NextFleePathAt;
        float m_NextStruggleAt;
        float m_WanderUntil;

        Breakable m_Item;
        float m_ItemSince;
        float m_ActSince = -1f;
        readonly Dictionary<int, float> m_SkipUntil = new Dictionary<int, float>();

        PlayerAvatar m_Threat;
        float m_FleeUntil;

        Vector3 m_ProgressPosition;
        float m_ProgressTime;
        int m_StuckCount;

        bool m_HoldTask;
        bool m_HoldPick;
        bool m_Sneak;
        float m_RestUntil;

        static readonly RaycastHit[] s_Hits = new RaycastHit[16];

        /// <summary>現在在做什麼（測試工具查看用）。</summary>
        public Goal CurrentGoal { get; private set; }

        /// <summary>現在的目標（東西的名字、在逃誰…；測試工具查看用）。</summary>
        public string GoalDetail { get; private set; } = "";

        /// <summary>找路用的條件：會游泳的（烏龜）走水可以走的那一份找路資料。</summary>
        public NavMeshQueryFilter Filter
        {
            get
            {
                bool swimmer = m_Avatar != null && GameConfig.Instance.GetWaterSpeedMultiplier(m_Avatar.AvatarRole.Value) > 0f;
                return BotNavigation.Filter(swimmer);
            }
        }

        /// <summary>這個電腦動物的角色。</summary>
        public PlayerAvatar Avatar
        {
            get { return m_Avatar; }
        }

        /// <summary>有沒有路可以走（測試工具查看用）。</summary>
        public bool HasPath
        {
            get { return m_HasPath || m_Direct; }
        }

        void Awake()
        {
            m_Avatar = GetComponent<PlayerAvatar>();
            m_Capture = GetComponent<PlayerCapture>();
            m_Movement = GetComponent<PlayerMovement>();
            m_Task = GetComponent<PlayerTask>();
            m_Lockpick = GetComponent<PlayerLockpick>();
            m_Path = new NavMeshPath();
            m_ProgressTime = Time.time;
        }

        void OnEnable()
        {
            if (!All.Contains(this))
            {
                All.Add(this);
            }
        }

        void OnDisable()
        {
            All.Remove(this);
            ReleaseEverything();
        }

        void Update()
        {
            if (m_Avatar == null || !m_Avatar.IsSpawned || !m_Avatar.IsServer || m_Capture == null || m_Movement == null)
            {
                return;
            }

            BotNavigation.EnsureBuilt();
            float now = Time.time;
            CaptureState state = m_Capture.State.Value;

            if (state == CaptureState.Netted || state == CaptureState.Carried)
            {
                SetGoal(Goal.Struggle, m_Capture.IsAsleep ? "麻醉睡著" : (state == CaptureState.Carried ? "被扛著" : "被網住"));
                StopMoving();
                m_HoldTask = false;
                m_HoldPick = false;
                ApplyHolds();
                if (now >= m_NextStruggleAt && !m_Capture.IsAsleep)
                {
                    m_Capture.RequestStruggle();
                    float interval = 1f / Mathf.Max(1f, GameConfig.Instance.botStrugglePerSecond);
                    m_NextStruggleAt = now + interval * Random.Range(0.7f, 1.3f);
                }

                return;
            }

            // 上了貨車：往車廂裡面走、各自站開，別擋住後面要上車的隊友。
            if (state == CaptureState.Escaped && ExitDoor.Current != null && BotNavigation.IsReady)
            {
                SetGoal(Goal.Idle, "在貨車上");
                m_HoldTask = false;
                m_HoldPick = false;
                ApplyHolds();
                if (now >= m_NextThinkAt)
                {
                    m_NextThinkAt = now + 0.5f;
                    ExitDoor door = ExitDoor.Current;
                    Vector3 inward = door.truckCenter - door.transform.position;
                    inward.y = 0f;
                    Vector3 side = Vector3.Cross(Vector3.up, inward.normalized);
                    int slot = m_Avatar.PlayerNumber % 4;
                    Vector3 spot = door.truckCenter + inward.normalized * 1.2f + side * ((slot - 1.5f) * 1.3f);
                    if (PlanarDistance(transform.position, spot) > 0.6f)
                    {
                        m_Sneak = false;
                        MoveTo(spot);
                    }
                    else
                    {
                        StopMoving();
                    }
                }

                Steer(now);
                return;
            }

            MatchFlow flow = MatchFlow.Instance;
            bool waitingForStart = flow != null && flow.Phase.Value == MatchPhase.Lobby && !GameConfig.Instance.botsActInLobby;
            if (state != CaptureState.Free || flow == null || !flow.InteractionsAllowed || waitingForStart)
            {
                SetGoal(Goal.Idle, state != CaptureState.Free ? StateName(state) : (waitingForStart ? "等比賽開始" : "比賽結束"));
                StopMoving();
                m_HoldTask = false;
                m_HoldPick = false;
                ApplyHolds();
                return;
            }

            if (FreezeForTesting)
            {
                SetGoal(Goal.Idle, "測試暫停");
                StopMoving();
                m_HoldTask = false;
                m_HoldPick = false;
                ApplyHolds();
                return;
            }

            if (!BotNavigation.IsReady)
            {
                SetGoal(Goal.Waiting, "建立找路資料中");
                StopMoving();
                ApplyHolds();
                return;
            }

            if (now >= m_NextThinkAt)
            {
                m_NextThinkAt = now + Mathf.Max(0.05f, GameConfig.Instance.botThinkInterval);
                Think(now, flow);
            }

            Steer(now);
            ApplyHolds();
        }

        // ------------------------------------------------------------------
        // 想要做什麼
        // ------------------------------------------------------------------

        void Think(float now, MatchFlow flow)
        {
            GameConfig config = GameConfig.Instance;
            Vector3 position = transform.position;
            m_Sneak = false;

            // 關在自己的收容區：走到門邊，按住撬鎖。
            HabitatGate gate = HabitatGate.Find(m_Avatar.AvatarRole.Value);
            if (gate != null && !gate.IsOpen && gate.Contains(position))
            {
                SetGoal(Goal.PickLock, "收容區的門");
                m_HoldTask = false;
                if (m_Lockpick != null && m_Lockpick.GateInReach != null)
                {
                    StopMoving();
                    m_HoldPick = true;
                }
                else
                {
                    m_HoldPick = false;
                    Vector3 inward = gate.interiorCenter - gate.transform.position;
                    inward.y = 0f;
                    Vector3 spot = gate.transform.position + (inward.sqrMagnitude > 0.01f ? inward.normalized : Vector3.zero) * 0.7f;
                    MoveTo(spot);
                }

                return;
            }

            m_HoldPick = false;

            // 看到人類靠近：往遠離人類的地方跑。
            PlayerAvatar seen = FindThreat(position);
            if (seen != null)
            {
                m_Threat = seen;
                m_FleeUntil = now + config.botFleeMemorySeconds;
            }

            if (m_Threat != null && m_Threat.IsSpawned && now < m_FleeUntil
                && PlanarDistance(position, m_Threat.transform.position) < config.botSafeDistance)
            {
                SetGoal(Goal.Flee, "P" + m_Threat.PlayerNumber + " 人類");
                m_HoldTask = false;
                m_Item = null;
                if (now >= m_NextFleePathAt || !HasPath)
                {
                    m_NextFleePathAt = now + 0.8f;
                    MoveTo(FindFleePoint(position, m_Threat.transform.position));
                }

                return;
            }

            m_Threat = null;
            m_Sneak = false;

            // 混亂值滿了：去開出口；開了就上貨車。
            ExitDoor door = ExitDoor.Current;
            if (flow.ExitUnlocked && door != null)
            {
                m_Item = null;
                if (flow.ExitOpen.Value)
                {
                    SetGoal(Goal.BoardTruck, "貨車");
                    m_HoldTask = false;
                    MoveTo(door.truckCenter);
                    return;
                }

                SetGoal(Goal.OpenExit, "出口");
                if (door.InOpeningZone(position, config.exitReach * 0.8f))
                {
                    StopMoving();
                    m_HoldTask = true;
                }
                else
                {
                    m_HoldTask = false;
                    MoveTo(door.InsidePoint);
                }

                return;
            }

            // 剛弄倒一樣東西：慢慢閒晃一下再找下一個（不然三隻電腦動物不到一分鐘就把東西全弄倒了）。
            bool resting = now < m_RestUntil;
            if (!resting && BreakThings(now, position))
            {
                return;
            }

            // 休息中，或沒東西可以弄了：閒晃（休息時用慢走，比較安靜）。
            m_HoldTask = false;
            m_Sneak = resting;
            SetGoal(Goal.Wander, resting ? "休息" : "");
            if (now >= m_WanderUntil || !HasPath)
            {
                m_WanderUntil = now + 6f;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    Vector2 offset = Random.insideUnitCircle * 12f;
                    Vector3 spot = position + new Vector3(offset.x, 0f, offset.y);
                    if (IsUsableDestination(spot))
                    {
                        MoveTo(spot);
                        break;
                    }
                }
            }
        }

        /// <summary>找最近、走得到的東西弄倒。回傳 true 表示這一輪在做這件事。</summary>
        bool BreakThings(float now, Vector3 position)
        {
            if (m_Item != null && (m_Item.IsBroken || IsSkipped(m_Item.index, now)))
            {
                if (m_Item.IsBroken && m_ActSince >= 0f)
                {
                    GameConfig config = GameConfig.Instance;
                    m_RestUntil = now + Random.Range(config.botRestMin, Mathf.Max(config.botRestMin, config.botRestMax));
                    m_WanderUntil = 0f;
                }

                m_Item = null;
                m_HoldTask = false;
                m_ActSince = -1f;
                if (now < m_RestUntil)
                {
                    return false;
                }
            }

            if (m_Item == null)
            {
                Vector3 approach;
                m_Item = ChooseItem(position, now, out approach);
                if (m_Item == null)
                {
                    return false;
                }

                m_ItemSince = now;
                m_ActSince = -1f;
                MoveTo(approach);
            }

            SetGoal(Goal.Break, m_Item.displayName + " #" + m_Item.index);

            // 站到碰得到的地方了（按 E 會弄倒某一個東西）：小的按一下，中大的按住。
            int target = -1;
            TaskKind kind = m_Task != null ? m_Task.FindCandidate(out target) : TaskKind.None;
            if (kind == TaskKind.Break || (m_Task != null && m_Task.Kind.Value == TaskKind.Break))
            {
                StopMoving();
                if (m_ActSince < 0f)
                {
                    m_ActSince = now;
                }

                Breakable inReach = Breakable.Find(kind == TaskKind.Break ? target : m_Task.Target.Value);
                if (inReach != null && GameConfig.Instance.GetBreakSeconds(inReach.size) <= 0f)
                {
                    m_HoldTask = false;
                    m_Task.ExternalTap();
                }
                else
                {
                    m_HoldTask = true;
                }

                if (now - m_ActSince > 6f)
                {
                    Skip(m_Item, now, 30f);
                    m_Item = null;
                    m_HoldTask = false;
                }

                return true;
            }

            m_HoldTask = false;
            m_ActSince = -1f;

            // 走到了卻碰不到（或根本沒路）、或走太久：換一個。
            if (!HasPath || now - m_ItemSince > GameConfig.Instance.botGiveUpSeconds)
            {
                Skip(m_Item, now, 30f);
                m_Item = null;
            }

            return true;
        }

        /// <summary>從直線距離最近的幾個東西裡，挑走起來最近的那一個；approach 是要站的位置。</summary>
        Breakable ChooseItem(Vector3 position, float now, out Vector3 approach)
        {
            approach = position;
            var candidates = new List<Breakable>();
            for (int i = 0; i < Breakable.Active.Count; i++)
            {
                Breakable item = Breakable.Active[i];
                if (item != null && !item.IsBroken && !IsSkipped(item.index, now))
                {
                    candidates.Add(item);
                }
            }

            candidates.Sort((a, b) => PlanarDistance(position, a.transform.position).CompareTo(PlanarDistance(position, b.transform.position)));

            Vector3 start;
            if (!BotNavigation.Snap(position, 1.5f, Filter, out start))
            {
                return candidates.Count > 0 ? NearestDirect(candidates[0], out approach) : null;
            }

            Breakable best = null;
            float bestLength = float.MaxValue;
            int checkedCount = 0;
            for (int i = 0; i < candidates.Count && checkedCount < 5; i++)
            {
                Breakable item = candidates[i];
                Vector3 spot;
                float length;
                if (FindApproach(item, start, out spot, out length))
                {
                    checkedCount++;
                    if (length < bestLength)
                    {
                        bestLength = length;
                        best = item;
                        approach = spot;
                    }
                }
                else
                {
                    // 走不到的（例如在水的另一邊、在桌底下）先記著，一陣子內不再試。
                    Skip(item, now, 60f);
                }
            }

            return best;
        }

        static Breakable NearestDirect(Breakable item, out Vector3 approach)
        {
            approach = item.transform.position;
            return item;
        }

        /// <summary>在東西四周找一個地面上、走得到、碰得到它的位置。</summary>
        bool FindApproach(Breakable item, Vector3 start, out Vector3 spot, out float length)
        {
            spot = item.transform.position;
            length = float.MaxValue;
            Vector3 center = item.transform.position;
            center.y = 0f;
            float reach = item.radius + Mathf.Min(0.8f, GameConfig.Instance.breakRange * 0.6f);
            bool found = false;
            for (int k = 0; k < 8; k++)
            {
                Vector3 direction = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                Vector3 probe = center + direction * reach + Vector3.up * 0.2f;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(probe, out hit, 0.5f, Filter) || hit.position.y > 0.5f
                    || !IsUsableDestination(hit.position))
                {
                    continue;
                }

                if (!NavMesh.CalculatePath(start, hit.position, Filter, m_Path) || m_Path.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                float pathLength = BotNavigation.PathLength(m_Path);
                if (pathLength < length)
                {
                    length = pathLength;
                    spot = hit.position;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>看得到（或很近）的人類；沒有就是 null。</summary>
        PlayerAvatar FindThreat(Vector3 position)
        {
            GameConfig config = GameConfig.Instance;
            PlayerAvatar best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar other = PlayerAvatar.All[i];
                if (other == null || !other.IsSpawned || other.AvatarRole.Value != Role.Human
                    || (other.Capture != null && other.Capture.IsInLab))
                {
                    continue;
                }

                float distance = PlanarDistance(position, other.transform.position);
                if (distance > config.botSightDistance || distance >= bestDistance)
                {
                    continue;
                }

                if (distance <= config.botNoticeDistance
                    || CanSee(position + Vector3.up * 0.4f, other.transform.position + Vector3.up * 1.2f))
                {
                    best = other;
                    bestDistance = distance;
                }
            }

            return best;
        }

        static bool CanSee(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            float distance = offset.magnitude;
            if (distance < 0.01f)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(from, offset / distance, s_Hits, distance, GameLayers.SightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_Hits[i].collider.GetComponentInParent<PlayerAvatar>() == null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>往遠離人類的方向找幾個走得到的點，挑「離人類最遠、路不要太繞」的那一個。</summary>
        Vector3 FindFleePoint(Vector3 position, Vector3 threat)
        {
            Vector3 away = position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                away = -transform.forward;
            }

            away.Normalize();
            Vector3 start;
            if (!BotNavigation.Snap(position, 1.5f, Filter, out start))
            {
                return position + away * 6f;
            }

            Vector3 best = position + away * 6f;
            float bestScore = float.MinValue;
            float[] angles = { 0f, -35f, 35f, -70f, 70f, -110f, 110f };
            float[] distances = { 7f, 12f };
            for (int a = 0; a < angles.Length; a++)
            {
                Vector3 direction = Quaternion.Euler(0f, angles[a], 0f) * away;
                for (int d = 0; d < distances.Length; d++)
                {
                    NavMeshHit hit;
                    if (!NavMesh.SamplePosition(position + direction * distances[d], out hit, 2.5f, Filter)
                        || hit.position.y > 0.5f || !IsUsableDestination(hit.position))
                    {
                        continue;
                    }

                    if (!NavMesh.CalculatePath(start, hit.position, Filter, m_Path) || m_Path.status != NavMeshPathStatus.PathComplete
                        || m_Path.corners.Length < 2)
                    {
                        continue;
                    }

                    // 第一段路不能是往人類那邊走。
                    Vector3 firstLeg = m_Path.corners[1] - position;
                    firstLeg.y = 0f;
                    if (firstLeg.sqrMagnitude > 0.01f && Vector3.Dot(firstLeg.normalized, away) < -0.3f)
                    {
                        continue;
                    }

                    float score = PlanarDistance(hit.position, threat) - 0.3f * BotNavigation.PathLength(m_Path);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = hit.position;
                    }
                }
            }

            return best;
        }

        // ------------------------------------------------------------------
        // 走路
        // ------------------------------------------------------------------

        void MoveTo(Vector3 destination)
        {
            Vector3 position = transform.position;
            NavMeshHit startHit;
            if (!NavMesh.SamplePosition(position, out startHit, 1f, Filter))
            {
                // 不在可以走的地方（例如烏龜在池塘裡）：先直接往最近的岸邊走。
                NavMeshHit nearest;
                if (NavMesh.SamplePosition(position, out nearest, 15f, Filter))
                {
                    m_Direct = true;
                    m_DirectTarget = nearest.position;
                    m_HasPath = false;
                }
                else
                {
                    m_Direct = false;
                    m_HasPath = false;
                }

                return;
            }

            m_Direct = false;
            NavMeshHit endHit;
            if (!NavMesh.SamplePosition(destination, out endHit, 2f, Filter)
                || !NavMesh.CalculatePath(startHit.position, endHit.position, Filter, m_Path)
                || m_Path.status == NavMeshPathStatus.PathInvalid)
            {
                m_HasPath = false;
                return;
            }

            // 站在高處（例如跳上了植栽箱）、從這裡走不到目的地：直接往旁邊最近的地面走，掉下去再說。
            if (m_Path.status != NavMeshPathStatus.PathComplete && position.y > 0.4f)
            {
                Vector3 ground;
                if (FindGroundBelow(position, out ground))
                {
                    m_Direct = true;
                    m_DirectTarget = ground;
                    m_HasPath = false;
                    return;
                }
            }

            m_Corners = m_Path.corners;
            m_CornerIndex = 1;
            m_HasPath = m_Corners.Length > 1;
            if (!m_HasPath || Vector3.Distance(m_Destination, endHit.position) > 0.5f)
            {
                m_ProgressPosition = position;
                m_ProgressTime = Time.time;
                m_StuckCount = 0;
            }

            m_Destination = endHit.position;
        }

        /// <summary>高處旁邊最近的地面上、可以走的點（找 4 公尺內，往外繞一圈試）。</summary>
        bool FindGroundBelow(Vector3 position, out Vector3 ground)
        {
            ground = position;
            float best = float.MaxValue;
            bool found = false;
            for (int ring = 1; ring <= 4; ring++)
            {
                for (int k = 0; k < 8; k++)
                {
                    Vector3 probe = position + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * ring;
                    probe.y = 0.1f;
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(probe, out hit, 0.6f, Filter) && hit.position.y < 0.4f)
                    {
                        float distance = PlanarDistance(position, hit.position);
                        if (distance < best)
                        {
                            best = distance;
                            ground = hit.position;
                            found = true;
                        }
                    }
                }

                if (found)
                {
                    return true;
                }
            }

            return false;
        }

        void Steer(float now)
        {
            Vector3 position = transform.position;
            Vector3 target;
            if (m_Direct)
            {
                target = m_DirectTarget;
                if (PlanarDistance(position, target) < k_CornerReach)
                {
                    m_Direct = false;
                    m_Movement.SetExternalInput(Vector3.zero, false);
                    return;
                }
            }
            else if (m_HasPath)
            {
                while (m_CornerIndex < m_Corners.Length && PlanarDistance(position, m_Corners[m_CornerIndex]) < k_CornerReach)
                {
                    m_CornerIndex++;
                }

                if (m_CornerIndex >= m_Corners.Length)
                {
                    m_HasPath = false;
                    m_Movement.SetExternalInput(Vector3.zero, false);
                    return;
                }

                target = m_Corners[m_CornerIndex];
            }
            else
            {
                m_Movement.SetExternalInput(Vector3.zero, false);
                return;
            }

            Vector3 direction = target - position;
            direction.y = 0f;
            m_Movement.SetExternalInput(direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero, m_Sneak);

            // 卡住了：先跳一下；還是卡著就重找路；再卡就放棄這個目標。
            if (PlanarDistance(position, m_ProgressPosition) > k_ProgressDistance)
            {
                m_ProgressPosition = position;
                m_ProgressTime = now;
                m_StuckCount = 0;
                return;
            }

            if (now - m_ProgressTime < k_StuckSeconds)
            {
                return;
            }

            m_ProgressTime = now;
            m_StuckCount++;
            if (m_StuckCount == 1)
            {
                m_Movement.Jump();
            }
            else if (m_StuckCount == 2)
            {
                MoveTo(m_Destination);
            }
            else
            {
                if (m_Item != null)
                {
                    Skip(m_Item, now, 20f);
                    m_Item = null;
                }

                m_HasPath = false;
                m_Direct = false;
                m_StuckCount = 0;
                m_WanderUntil = 0f;
            }
        }

        void StopMoving()
        {
            m_HasPath = false;
            m_Direct = false;
            if (m_Movement != null)
            {
                m_Movement.SetExternalInput(Vector3.zero, false);
            }
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        void ApplyHolds()
        {
            if (m_Task != null)
            {
                m_Task.SetExternalHold(m_HoldTask);
            }

            if (m_Lockpick != null)
            {
                m_Lockpick.SetExternalHold(m_HoldPick);
            }
        }

        void ReleaseEverything()
        {
            m_HoldTask = false;
            m_HoldPick = false;
            ApplyHolds();
            if (m_Movement != null)
            {
                m_Movement.ClearExternalInput();
            }
        }

        void SetGoal(Goal goal, string detail)
        {
            CurrentGoal = goal;
            GoalDetail = detail;
        }

        bool IsSkipped(int index, float now)
        {
            float until;
            return m_SkipUntil.TryGetValue(index, out until) && now < until;
        }

        void Skip(Breakable item, float now, float seconds)
        {
            if (item != null)
            {
                m_SkipUntil[item.index] = now + seconds;
            }
        }

        /// <summary>
        /// 這個點能不能當目的地。找路資料把收容區的門和出口的門都當成沒有（見 BotNavigation），
        /// 所以要另外排除：鎖著的收容區裡面（自己的除外，那是撬鎖的時候）、出口還沒開時的牆外（貨車那邊）。
        /// </summary>
        bool IsUsableDestination(Vector3 point)
        {
            MatchFlow flow = MatchFlow.Instance;
            bool exitOpen = flow != null && flow.ExitOpen.Value;
            if (!exitOpen && (Mathf.Abs(point.x) > 39.5f || Mathf.Abs(point.z) > 29.5f))
            {
                return false;
            }

            Role mine = m_Avatar != null ? m_Avatar.AvatarRole.Value : Role.None;
            for (int i = 0; i < HabitatGate.Active.Count; i++)
            {
                HabitatGate gate = HabitatGate.Active[i];
                if (gate != null && gate.role != mine && !gate.IsOpen && gate.Contains(point))
                {
                    return false;
                }
            }

            return true;
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static string StateName(CaptureState state)
        {
            switch (state)
            {
                case CaptureState.Caged:
                    return "關在籠子裡";
                case CaptureState.InLab:
                    return "在實驗室";
                case CaptureState.Escaped:
                    return "在貨車上";
                default:
                    return state.ToString();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            FreezeForTesting = false;
        }
    }
}
