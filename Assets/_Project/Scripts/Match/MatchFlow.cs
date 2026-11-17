using AnimalEscape.Core;
using AnimalEscape.Players;
using Unity.Netcode;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>比賽進行到哪裡。</summary>
    public enum MatchPhase : byte
    {
        /// <summary>準備中：選身分、自由練習（破壞、控制台、出口都能用，但不計時、不會結束）。主機按「開始比賽」。</summary>
        Lobby = 0,

        /// <summary>比賽中：計時、不能換身分，貨車開走或所有動物都被抓就結束。</summary>
        Playing = 1,

        /// <summary>結算：顯示結果；可以換身分，主機按「再來一局」。</summary>
        Results = 2
    }

    /// <summary>這一局的結果（看貨車上有幾隻動物）。</summary>
    public enum MatchOutcome : byte
    {
        None = 0,
        AnimalsPerfect = 1,
        AnimalsWin = 2,
        Draw = 3,
        HumanWins = 4
    }

    /// <summary>這一局為什麼結束。</summary>
    public enum MatchEndReason : byte
    {
        None = 0,

        /// <summary>出口打開之後，貨車倒數結束開走了。</summary>
        TruckLeft = 1,

        /// <summary>場上沒有自由的動物了（都上了車、被關或在實驗室），貨車提早開走。</summary>
        AllAboard = 2,

        /// <summary>10 分鐘到了。</summary>
        TimeUp = 3,

        /// <summary>所有動物都在籠子裡或實驗室裡，沒有人上車。</summary>
        AllCaught = 4,

        /// <summary>場上一隻動物都沒有了（都離開了）。</summary>
        NoAnimals = 5
    }

    /// <summary>
    /// 比賽的流程與 M4 的全館狀態：準備 → 比賽中 → 結算、10 分鐘計時、混亂值、哪些東西被破壞了、保全控制台的冷卻、
    /// 出口開門進度、貨車倒數、這一局的結果。和 MatchState 掛在同一個網路物件上（主機開房時產生），所有判定都在主機。
    ///
    /// 動物和人類的「按住 E」（破壞、控制台、開門）在角色身上的 PlayerTask；這裡負責算結果：
    /// 破壞 → 加混亂值；控制台 → 減混亂值、開始冷卻；開門 → 依幾隻一起按累積進度；門開了 → 走進貨車的動物變成「上了貨車」。
    /// </summary>
    public class MatchFlow : NetworkBehaviour
    {
        public static MatchFlow Instance { get; private set; }

        public const int ConsoleCount = 3;

        // 注意宣告順序：結果（Outcome、EscapedCount…）要比 Phase 早，StartedAt 也要比 Phase 早：
        // Phase 變成「結算」或「比賽中」的那一刻，結果和開始時間已經是新的。

        public readonly NetworkVariable<MatchOutcome> Outcome = new NetworkVariable<MatchOutcome>(MatchOutcome.None);
        public readonly NetworkVariable<MatchEndReason> EndReason = new NetworkVariable<MatchEndReason>(MatchEndReason.None);

        /// <summary>結束時貨車上有幾隻。</summary>
        public readonly NetworkVariable<int> EscapedCount = new NetworkVariable<int>(0);

        /// <summary>這一局開始時有幾隻動物。</summary>
        public readonly NetworkVariable<int> AnimalCount = new NetworkVariable<int>(0);

        /// <summary>一局幾秒（主機開房時決定：正式模式可選 5／8／10 分鐘；測試模式的 F1 面板可以改）。要比 StartedAt 早。</summary>
        public readonly NetworkVariable<float> MatchSeconds = new NetworkVariable<float>(600f);

        /// <summary>這一局開始的時刻（主機時間）。</summary>
        public readonly NetworkVariable<double> StartedAt = new NetworkVariable<double>(-100.0);

        /// <summary>計時暫停時停在剩幾秒（測試模式的 F1 面板）；沒有暫停是 -1。要比 StartedAt 晚（繼續時先改 StartedAt）。</summary>
        public readonly NetworkVariable<float> PausedLeft = new NetworkVariable<float>(-1f);

        public readonly NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);

        /// <summary>全館混亂值 0～100（％）。滿 100 出口解鎖，之後不會再降。</summary>
        public readonly NetworkVariable<float> Chaos = new NetworkVariable<float>(0f);

        /// <summary>混亂值滿 100 的時刻（主機時間），公告用；還沒滿是 -100。</summary>
        public readonly NetworkVariable<double> UnlockedAt = new NetworkVariable<double>(-100.0);

        /// <summary>這張地圖上哪些東西壞了：第 n 個位元對應 Breakable.index。</summary>
        public readonly NetworkVariable<ulong> BrokenMask = new NetworkVariable<ulong>(0UL);

        /// <summary>三台控制台各自冷卻到什麼時候（主機時間）。</summary>
        public readonly NetworkVariable<double> Console1ReadyAt = new NetworkVariable<double>(-100.0);
        public readonly NetworkVariable<double> Console2ReadyAt = new NetworkVariable<double>(-100.0);
        public readonly NetworkVariable<double> Console3ReadyAt = new NetworkVariable<double>(-100.0);

        /// <summary>出口的開門進度 0～1。</summary>
        public readonly NetworkVariable<float> ExitProgress = new NetworkVariable<float>(0f);

        /// <summary>現在有幾隻動物在一起開門（介面用）。</summary>
        public readonly NetworkVariable<int> ExitHelpers = new NetworkVariable<int>(0);

        /// <summary>貨車什麼時候開走（主機時間）；要比 ExitOpen 早。</summary>
        public readonly NetworkVariable<double> TruckLeavesAt = new NetworkVariable<double>(-100.0);

        /// <summary>出口的門打開了沒有。</summary>
        public readonly NetworkVariable<bool> ExitOpen = new NetworkVariable<bool>(false);

        float m_ExitProgressExact;
        float m_ExitSyncTimer;

        public bool IsPlaying
        {
            get { return Phase.Value == MatchPhase.Playing; }
        }

        /// <summary>能不能破壞、用控制台、開門（結算時不行）。</summary>
        public bool InteractionsAllowed
        {
            get { return Phase.Value != MatchPhase.Results; }
        }

        /// <summary>混亂值滿了、出口已經解鎖。</summary>
        public bool ExitUnlocked
        {
            get { return Chaos.Value >= 99.99f; }
        }

        /// <summary>這一局還剩幾秒（比賽中才有意義）。</summary>
        public float TimeLeft
        {
            get
            {
                if (PausedLeft.Value >= 0f)
                {
                    return PausedLeft.Value;
                }

                return Mathf.Max(0f, (float)(StartedAt.Value + MatchSeconds.Value - PlayerCapture.Now));
            }
        }

        /// <summary>計時暫停中（測試模式）。</summary>
        public bool TimerPaused
        {
            get { return PausedLeft.Value >= 0f; }
        }

        /// <summary>貨車還有幾秒開走（門還沒開就是 0）。</summary>
        public float TruckTimeLeft
        {
            get { return ExitOpen.Value ? Mathf.Max(0f, (float)(TruckLeavesAt.Value - PlayerCapture.Now)) : 0f; }
        }

        public bool IsBroken(int index)
        {
            return index >= 0 && index < 64 && (BrokenMask.Value & (1UL << index)) != 0;
        }

        public double GetConsoleReadyAt(int index)
        {
            switch (index)
            {
                case 0:
                    return Console1ReadyAt.Value;
                case 1:
                    return Console2ReadyAt.Value;
                case 2:
                    return Console3ReadyAt.Value;
                default:
                    return -100.0;
            }
        }

        NetworkVariable<double> ConsoleVariable(int index)
        {
            return index == 0 ? Console1ReadyAt : (index == 1 ? Console2ReadyAt : (index == 2 ? Console3ReadyAt : null));
        }

        /// <summary>這一台控制台現在能不能用（沒在冷卻、混亂值還沒滿、不是結算中）。</summary>
        public bool IsConsoleUsable(int index)
        {
            return InteractionsAllowed && !ExitUnlocked && PlayerCapture.Now >= GetConsoleReadyAt(index);
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                // 主選單選的比賽時間（沒選就用 GameConfig 的預設）。
                MatchSeconds.Value = RoomOptions.MatchSeconds > 0f ? RoomOptions.MatchSeconds : GameConfig.Instance.matchSeconds;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>（主機的按鈕）開始比賽／再來一局。只有主機按才有效；不能開始的原因記在 LastStartError。</summary>
        public void RequestStartMatch()
        {
            if (!IsServer)
            {
                return;
            }

            string reason;
            ServerStartMatch(out reason, !MatchState.SkipsTeamRequirement);
            LastStartError = reason;
        }

        /// <summary>（主機）上次按開始時不能開始的原因；開始成功是空字串。</summary>
        public string LastStartError { get; private set; } = "";

        // ------------------------------------------------------------------
        // 主機端：開始、結束、重設
        // ------------------------------------------------------------------

        /// <summary>
        /// 主機端：開始比賽（「開始比賽」和「再來一局」都走這裡）。至少要 1 個人類和 1 隻動物。
        /// 所有人回到出生點、收容區全部鎖上、混亂值歸零、壞掉的東西恢復、實驗室的球和門禁卡重放。
        /// </summary>
        public bool ServerStartMatch(out string reason)
        {
            return ServerStartMatch(out reason, !MatchState.SkipsTeamRequirement);
        }

        /// <summary>同上；requireTeams = false 時不用湊齊人類和動物（測試模式的規則開關）。</summary>
        public bool ServerStartMatch(out string reason, bool requireTeams)
        {
            reason = "";
            if (!IsServer)
            {
                reason = "只有主機能開始";
                return false;
            }

            if (Phase.Value == MatchPhase.Playing)
            {
                reason = "已經在比賽中了";
                return false;
            }

            int humans;
            int animals;
            CountRoles(out humans, out animals);
            if (requireTeams && (humans < 1 || animals < 1))
            {
                reason = "至少要有 1 個人類和 1 隻動物";
                return false;
            }

            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot slot = PlayerSlot.All[i];
                if (slot != null && slot.IsSpawned)
                {
                    slot.Result.Value = MatchResult.None;
                }
            }

            ServerResetState();
            AnimalCount.Value = animals;

            MatchState match = MatchState.Instance;
            if (match != null)
            {
                match.ServerLockAllHabitats();
            }

            if (PlayerSpawner.Instance != null)
            {
                PlayerSpawner.Instance.RespawnAllForMatch();
            }

            // 角色都重新產生了，門禁卡（記的是角色）和球要在這之後才重放。
            if (match != null)
            {
                match.ServerResetLab(-1);
            }

            StartedAt.Value = PlayerCapture.Now;
            PausedLeft.Value = -1f;
            Phase.Value = MatchPhase.Playing;
            return true;
        }

        /// <summary>主機端：換地圖時回到準備階段（MatchState.SetMap 呼叫）。</summary>
        public void ServerResetToLobby()
        {
            if (!IsServer)
            {
                return;
            }

            ServerResetState();
            Outcome.Value = MatchOutcome.None;
            EndReason.Value = MatchEndReason.None;
            PausedLeft.Value = -1f;
            Phase.Value = MatchPhase.Lobby;
        }

        void ServerResetState()
        {
            Chaos.Value = 0f;
            UnlockedAt.Value = -100.0;
            BrokenMask.Value = 0UL;
            Console1ReadyAt.Value = -100.0;
            Console2ReadyAt.Value = -100.0;
            Console3ReadyAt.Value = -100.0;
            m_ExitProgressExact = 0f;
            ExitProgress.Value = 0f;
            ExitHelpers.Value = 0;
            TruckLeavesAt.Value = -100.0;
            ExitOpen.Value = false;
            EscapedCount.Value = 0;
        }

        static void CountRoles(out int humans, out int animals)
        {
            humans = 0;
            animals = 0;
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot slot = PlayerSlot.All[i];
                if (slot == null || !slot.IsSpawned)
                {
                    continue;
                }

                Role role = slot.CurrentRole.Value;
                if (role == Role.Human)
                {
                    humans++;
                }
                else if (Roles.IsAnimal(role))
                {
                    animals++;
                }
            }
        }

        /// <summary>主機端：結束這一局，算出結果（看貨車上有幾隻），進入結算。</summary>
        public void ServerEndMatch(MatchEndReason reason)
        {
            if (!IsServer || Phase.Value != MatchPhase.Playing)
            {
                return;
            }

            int escaped = 0;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar == null || !avatar.IsSpawned || !Roles.IsAnimal(avatar.AvatarRole.Value) || avatar.Capture == null)
                {
                    continue;
                }

                bool onTruck = avatar.Capture.State.Value == CaptureState.Escaped;
                if (onTruck)
                {
                    escaped++;
                }

                PlayerSlot slot = avatar.Slot;
                if (slot != null && slot.Result.Value == MatchResult.None)
                {
                    slot.Result.Value = onTruck ? MatchResult.Escaped : MatchResult.Caught;
                }
            }

            EscapedCount.Value = escaped;
            EndReason.Value = reason;
            Outcome.Value = escaped >= 4 ? MatchOutcome.AnimalsPerfect
                : escaped == 3 ? MatchOutcome.AnimalsWin
                : escaped == 2 ? MatchOutcome.Draw
                : MatchOutcome.HumanWins;
            Phase.Value = MatchPhase.Results;
        }

        // ------------------------------------------------------------------
        // 主機端：混亂值、破壞、控制台
        // ------------------------------------------------------------------

        /// <summary>主機端：某個東西被弄倒了。已經壞了就不算。</summary>
        public void ServerBreak(Breakable item)
        {
            if (!IsServer || item == null || item.index < 0 || item.index >= 64 || IsBroken(item.index) || !InteractionsAllowed)
            {
                return;
            }

            BrokenMask.Value = BrokenMask.Value | (1UL << item.index);
            ServerAddChaos(GameConfig.Instance.GetChaos(item.size));
        }

        /// <summary>主機端：加（或減）混亂值。滿 100 就鎖死在 100（保全系統當機，出口解鎖），之後不會再降。</summary>
        public void ServerAddChaos(float amount)
        {
            if (!IsServer || ExitUnlocked)
            {
                return;
            }

            float value = Mathf.Clamp(Chaos.Value + amount, 0f, 100f);
            Chaos.Value = value;
            if (value >= 99.99f)
            {
                Chaos.Value = 100f;
                UnlockedAt.Value = PlayerCapture.Now;
            }
        }

        /// <summary>主機端：直接設定混亂值（測試工具用）。</summary>
        public void ServerSetChaos(float value)
        {
            if (!IsServer)
            {
                return;
            }

            Chaos.Value = Mathf.Clamp(value, 0f, 100f);
            if (Chaos.Value >= 99.99f)
            {
                UnlockedAt.Value = PlayerCapture.Now;
            }
            else
            {
                UnlockedAt.Value = -100.0;
            }
        }

        /// <summary>主機端：人類用了第 index 台控制台。</summary>
        public void ServerUseConsole(int index)
        {
            NetworkVariable<double> readyAt = ConsoleVariable(index);
            if (!IsServer || readyAt == null || !IsConsoleUsable(index))
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            readyAt.Value = PlayerCapture.Now + config.consoleCooldown;
            ServerAddChaos(-config.consoleChaosReduction);
        }

        /// <summary>主機端：把這一局剩下的時間改成 seconds 秒（測試工具用）。</summary>
        public void ServerSetTimeLeft(float seconds)
        {
            if (!IsServer || !IsPlaying)
            {
                return;
            }

            seconds = Mathf.Max(0f, seconds);
            if (TimerPaused)
            {
                PausedLeft.Value = seconds;
            }
            else
            {
                StartedAt.Value = PlayerCapture.Now + seconds - MatchSeconds.Value;
            }
        }

        /// <summary>主機端：一局改成幾秒（還沒開始時用；比賽中請用 ServerSetTimeLeft）。</summary>
        public void ServerSetMatchSeconds(float seconds)
        {
            if (IsServer)
            {
                MatchSeconds.Value = Mathf.Clamp(seconds, 5f, 3600f);
            }
        }

        /// <summary>主機端：暫停／繼續計時（測試模式的 F1 面板）。</summary>
        public void ServerSetPaused(bool paused)
        {
            if (!IsServer || !IsPlaying || paused == TimerPaused)
            {
                return;
            }

            if (paused)
            {
                PausedLeft.Value = TimeLeft;
            }
            else
            {
                // 先改開始時間再取消暫停（宣告順序：StartedAt 比 PausedLeft 早），中間不會跳一下。
                StartedAt.Value = PlayerCapture.Now + PausedLeft.Value - MatchSeconds.Value;
                PausedLeft.Value = -1f;
            }
        }

        /// <summary>主機端：壞掉的東西全部恢復（混亂值不變；測試模式的 F1 面板）。</summary>
        public void ServerRestoreBreakables()
        {
            if (IsServer)
            {
                BrokenMask.Value = 0UL;
            }
        }

        /// <summary>主機端：貨車改成 seconds 秒後開走（測試工具用；門要已經開了）。</summary>
        public void ServerSetTruckTime(float seconds)
        {
            if (IsServer && ExitOpen.Value)
            {
                TruckLeavesAt.Value = PlayerCapture.Now + seconds;
            }
        }

        /// <summary>主機端：直接打開出口（測試工具用）。</summary>
        public void ServerOpenExit()
        {
            if (!IsServer || ExitOpen.Value)
            {
                return;
            }

            m_ExitProgressExact = 1f;
            ExitProgress.Value = 1f;
            ExitHelpers.Value = 0;
            TruckLeavesAt.Value = PlayerCapture.Now + GameConfig.Instance.truckSeconds;
            ExitOpen.Value = true;
        }

        // ------------------------------------------------------------------
        // 主機端，每一格
        // ------------------------------------------------------------------

        void Update()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ServerUpdateExit();
            ServerUpdateTruck();
            if (IsPlaying)
            {
                ServerCheckEnd();
            }
        }

        /// <summary>出口：混亂值滿了之後，站在門前按住 E 的動物越多，開得越快。進度不會因為放手而歸零。</summary>
        void ServerUpdateExit()
        {
            if (ExitOpen.Value || !ExitUnlocked || !InteractionsAllowed)
            {
                if (ExitHelpers.Value != 0)
                {
                    ExitHelpers.Value = 0;
                }

                return;
            }

            int helpers = 0;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                PlayerTask task = avatar != null && avatar.IsSpawned ? avatar.Task : null;
                if (task != null && task.Kind.Value == TaskKind.Exit)
                {
                    helpers++;
                }
            }

            if (ExitHelpers.Value != helpers)
            {
                ExitHelpers.Value = helpers;
            }

            if (helpers == 0)
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            float rate = (1f + config.exitHelperBonus * (helpers - 1)) / Mathf.Max(0.1f, config.exitOpenSeconds);
            m_ExitProgressExact = Mathf.Min(1f, m_ExitProgressExact + rate * Time.deltaTime);
            m_ExitSyncTimer -= Time.deltaTime;
            if (m_ExitProgressExact >= 1f)
            {
                ServerOpenExit();
                return;
            }

            if (m_ExitSyncTimer <= 0f)
            {
                m_ExitSyncTimer = 0.1f;
                ExitProgress.Value = m_ExitProgressExact;
            }
        }

        /// <summary>門開了之後：走進貨車車廂的自由動物＝上了貨車；上了車又走出車廂＝下車，變回自由。</summary>
        void ServerUpdateTruck()
        {
            ExitDoor door = ExitDoor.Current;
            if (!ExitOpen.Value || door == null || Phase.Value == MatchPhase.Results)
            {
                return;
            }

            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                PlayerCapture capture = avatar != null && avatar.IsSpawned ? avatar.Capture : null;
                if (capture == null || !Roles.IsAnimal(avatar.AvatarRole.Value))
                {
                    continue;
                }

                bool inTruck = door.InTruck(avatar.transform.position);
                if (capture.State.Value == CaptureState.Free && inTruck)
                {
                    capture.ServerSetEscaped(true);
                }
                else if (capture.State.Value == CaptureState.Escaped && !inTruck)
                {
                    capture.ServerSetEscaped(false);
                }
            }
        }

        /// <summary>比賽中：時間到、貨車開走、或場上沒有自由的動物了，就結束。</summary>
        void ServerCheckEnd()
        {
            double now = PlayerCapture.Now;
            if (TimeLeft <= 0f && !TimerPaused)
            {
                ServerEndMatch(MatchEndReason.TimeUp);
                return;
            }

            if (ExitOpen.Value && now >= TruckLeavesAt.Value)
            {
                ServerEndMatch(MatchEndReason.TruckLeft);
                return;
            }

            // 剛開始的一兩秒角色還在重新產生，先不判斷。
            if (now - StartedAt.Value < 2.0)
            {
                return;
            }

            int animals = 0;
            int inPlay = 0;
            int escaped = 0;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                PlayerCapture capture = avatar != null && avatar.IsSpawned ? avatar.Capture : null;
                if (capture == null || !Roles.IsAnimal(avatar.AvatarRole.Value))
                {
                    continue;
                }

                animals++;
                CaptureState state = capture.State.Value;
                if (state == CaptureState.Escaped)
                {
                    escaped++;
                }
                else if (state == CaptureState.Free || state == CaptureState.Netted || state == CaptureState.Carried)
                {
                    inPlay++;
                }
            }

            if (animals == 0)
            {
                // 測試模式（不用湊齊人類和動物）：場上沒有動物也照樣跑，只靠時間和貨車結束。
                if (!MatchState.SkipsTeamRequirement)
                {
                    ServerEndMatch(MatchEndReason.NoAnimals);
                }
            }
            else if (inPlay == 0)
            {
                ServerEndMatch(escaped > 0 ? MatchEndReason.AllAboard : MatchEndReason.AllCaught);
            }
        }
    }
}
