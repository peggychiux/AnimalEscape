using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using AnimalEscape.Core;
using AnimalEscape.Match;
using AnimalEscape.Networking;
using AnimalEscape.Players;
using AnimalEscape.UI;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace AnimalEscape.EditorTools
{
    /// <summary>
    /// 開發用的「測試遙控器」（只存在於編輯器，不會進到遊戲裡）。
    ///
    /// 用途：不用搶鍵盤滑鼠，就能自動測試多人情境。它會定時讀取主專案的 Logs/claude-commands.txt，
    /// 一行一個指令照順序執行，結果寫到 Logs/claude-test-results.txt。主視窗和 Multiplayer Play Mode
    /// 的虛擬玩家視窗都會讀同一個檔案，各自只執行給自己的指令。
    ///
    /// 指令格式：  編號 對象 動作 參數…      （# 開頭的是註解）
    ///   編號：整數，必須一直往上加；每個視窗只執行比自己上次執行過更大的編號。
    ///   對象：main（主視窗）、vp（虛擬玩家視窗）、P1～P5（那位玩家所在的視窗）、all（所有視窗）
    ///   動作：
    ///     play / stop           進入、離開 Play 模式（只有主視窗）
    ///     refresh               重新匯入並編譯（只有主視窗）
    ///     rebuild               重建原型場景與預製物（只有主視窗，要先 stop）。做完會自動重新編譯一次，
    ///                           虛擬玩家視窗才會拿到新的預製物；等 claude-compile.txt 的時間更新了再 play
    ///     host / join / leave   建立本機房間、加入本機房間、離開房間
    ///     role 身分              選身分：Human、Rabbit、Monkey、Raccoon、Turtle；None 表示回到選身分畫面
    ///     walk x z [力道] [jump] [sneak] [thru]
    ///                           讓自己的角色直線走到 (x, z)，走到或被擋住就停。力道 0～1（1 是全速）；
    ///                           加 jump 表示起步時跳一下，加 sneak 表示慢走，
    ///                           加 thru 表示只是路過這一點（不減速，接著做下一個 walk，路線才順）
    ///     jump                  原地跳一下
    ///     swing                 （人類）揮網，和按滑鼠左鍵一樣
    ///     interact              （人類）扛起附近被網住的動物；已經扛著就是放下。和按 E 一樣
    ///     struggle [次數] [每秒幾下]
    ///                           （動物）連按掙脫，和連按空白鍵一樣；預設 1 下、每秒 8 下。掙脫了就提早結束
    ///     pick [秒數上限]        （動物）按住撬鎖鍵，和按住 E 一樣；門開了、或超過上限（預設 20 秒）就放手，回報花了幾秒
    ///     unlock 身分|all        （主機）直接把某一種動物的收容區打開；lock 身分|all 是重新鎖上
    ///     rescue [秒數上限]      （動物）在關著隊友的籠子旁按住救援鍵，和按住 E 一樣；救出來、或超過上限（預設 15 秒）就放手
    ///     cagetime 秒數 [籠號]   （主機）把籠子裡動物的倒數直接改成指定秒數（不給籠號就是全部），測出局用
    ///     sounds                這個視窗的玩家「聽到」幾次腳步聲、鈴鐺、叫聲（上次 sounds 之後），和最近、最遠的距離；讀完歸零
    ///     hold rescue|pick on|off
    ///                           只按住／放開救援鍵或撬鎖鍵，不等結果（後面的指令照常執行），用來在進度條跑到一半時截圖
    ///     spectate              （出局的玩家）換下一位隊友看，和按空白鍵一樣
    ///     teleport x y z [yaw]  把自己的角色直接放到指定位置
    ///     look yaw pitch [秒]   設定鏡頭方向；有給秒數就平順地轉過去
    ///     follow on|off         鏡頭自動轉向角色前進的方向
    ///     map a|b|c             換地圖。還沒開房時是改大廳的選擇；開房後只有主機能換（所有人會回到選身分）
    ///     view human|animal first|third
    ///                           暫時切換人類或動物的視角（只改執行中的設定；正式的設定在 GameConfig.asset）
    ///     shot 檔名              把遊戲畫面存成 Logs/shots/檔名.png
    ///     camshot 檔名 [x y z yaw pitch [fov]]
    ///                           用一台臨時的鏡頭拍一張「沒有介面」的畫面，存成 Logs/shots/檔名.png（1600x1200）。
    ///                           不給位置就是從正上方往下拍整張地圖（上北下南）；給位置就是從那裡用透視鏡頭拍。
    ///     record 名稱 秒數 [fps] 開始錄影（不會等它錄完，後面的指令照常執行），畫面存成
    ///                           Logs/recordings/名稱/f_000001.jpg…，另有 frames.csv 記錄每張的時間
    ///     res 寬 高 / res restore  把 Game 視窗固定成指定解析度（錄影用）／還原成原本的設定
    ///     report                列出這個視窗看到的連線狀態和所有角色的位置
    ///     nethash               列出這個視窗的連線設定雜湊和每個網路預製物的識別碼。虛擬玩家連不進來、
    ///                           主視窗出現「NetworkConfig mismatch」時，兩邊各下一次，比對哪裡不一樣
    ///     wait 秒數
    ///     t0 時間 / until 秒數    多個視窗對時用：t0 設定共同的起點（1970 年起算的秒數），
    ///                           until 等到「起點之後第幾秒」才往下做
    ///
    /// 注意：walk 和 jump 是透過 PlayerMovement 的「外部控制」餵方向給角色，速度、轉身、重力、碰撞
    /// 都和真的用鍵盤操作走同一段程式，只差沒有經過讀鍵盤那一步；鍵盤本身還是要偶爾用手實際按一次確認。
    /// </summary>
    [InitializeOnLoad]
    public static class DevTestDriver
    {
        const string k_CommandFileName = "claude-commands.txt";
        const string k_ResultFileName = "claude-test-results.txt";
        const string k_ShotsFolderName = "shots";
        const string k_RecordingsFolderName = "recordings";
        const string k_LastIdKey = "AnimalEscape.DevTestDriver.LastId";
        const string k_TimeZeroKey = "AnimalEscape.DevTestDriver.TimeZero";
        const string k_PreviousSizeKey = "AnimalEscape.DevTestDriver.PreviousGameViewSize";
        const string k_RecordSizeName = "ClaudeRecording";
        const double k_PollInterval = 0.2;
        const double k_UndecidedTimeout = 15.0;

        static readonly bool s_IsVirtualPlayer;
        static readonly string s_LogsDir;
        static readonly string s_CommandPath;
        static readonly string s_ResultPath;

        static int s_LastId;
        static double s_NextPoll;
        static double s_LastTick;

        // 正在執行、需要跨好幾格才做得完的指令。回傳 true 表示做完了。
        static Func<float, bool> s_Active;
        static string s_ActiveLabel = "";

        // 剛執行的指令會讓程式重新載入（進出 Play、重新編譯），下一個指令要晚一點再讀
        static bool s_PauseAfterCommand;

        // 多個視窗對時用的共同起點（1970 年起算的秒數）
        static double s_TimeZero;

        // 用 thru 路過一個點之後，角色會先照原方向繼續走一小段，等下一個 walk 接手；沒人接手就停下來。
        static PlayerMovement s_CoastingMovement;
        static double s_CoastUntil;

        // 正在錄影的名稱（空字串表示沒有在錄）
        static string s_RecordingName = "";

        // 還不確定是不是給自己的指令（例如指定 P2，但這個視窗還沒連上線、不知道自己是幾號）
        static int s_UndecidedId = -1;
        static double s_UndecidedSince;

        static DevTestDriver()
        {
            // 虛擬玩家的專案在「主專案/Library/VP/代號/」底下，指令檔和結果檔統一放在主專案的 Logs。
            string projectRoot = Application.dataPath.Replace('\\', '/');
            if (projectRoot.EndsWith("/Assets", StringComparison.Ordinal))
            {
                projectRoot = projectRoot.Substring(0, projectRoot.Length - "/Assets".Length);
            }

            int index = projectRoot.IndexOf("/Library/VP/", StringComparison.Ordinal);
            s_IsVirtualPlayer = index >= 0;
            string mainRoot = s_IsVirtualPlayer ? projectRoot.Substring(0, index) : projectRoot;

            s_LogsDir = mainRoot + "/Logs";
            s_CommandPath = s_LogsDir + "/" + k_CommandFileName;
            s_ResultPath = s_LogsDir + "/" + k_ResultFileName;

            // 這次開啟編輯器之後第一次載入：指令檔裡原本就有的舊指令一律略過，只執行之後新加的。
            s_LastId = SessionState.GetInt(k_LastIdKey, -1);
            if (s_LastId < 0)
            {
                SetLastId(FindLargestId());
            }

            s_LastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Update;
        }

        // ------------------------------------------------------------------
        // 主迴圈
        // ------------------------------------------------------------------

        static void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Clamp((float)(now - s_LastTick), 0f, 0.05f);
            s_LastTick = now;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            if (s_CoastingMovement != null && now > s_CoastUntil)
            {
                s_CoastingMovement.ClearExternalInput();
                s_CoastingMovement = null;
            }

            if (s_Active != null)
            {
                bool done;
                try
                {
                    done = s_Active(deltaTime);
                }
                catch (Exception e)
                {
                    Report(s_ActiveLabel, "發生例外：" + e.GetType().Name + " " + e.Message);
                    done = true;
                }

                if (!done)
                {
                    return;
                }

                // 做完了，這一格就接著看下一個指令，動作之間才不會有空檔。
                s_Active = null;
                s_NextPoll = 0.0;
            }

            if (now < s_NextPoll)
            {
                return;
            }

            s_NextPoll = now + k_PollInterval;

            // 一格之內可以連續執行好幾個「馬上做完」的指令，直到遇到需要時間的指令為止。
            for (int i = 0; i < 20; i++)
            {
                if (!StartNextCommand(now) || s_Active != null || s_PauseAfterCommand)
                {
                    break;
                }
            }

            if (s_PauseAfterCommand)
            {
                // 進出 Play、重新編譯之後程式會重新載入，先停一下再讀下一個指令。
                s_PauseAfterCommand = false;
                s_NextPoll = now + 1.0;
            }
        }

        /// <summary>找出下一個還沒做、而且是給這個視窗的指令並開始執行。有執行就回傳 true。</summary>
        static bool StartNextCommand(double now)
        {
            string[] lines = ReadCommandLines();
            if (lines == null)
            {
                return false;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                int id;
                string target;
                string verb;
                string[] args;
                if (!ParseLine(lines[i], out id, out target, out verb, out args) || id <= s_LastId)
                {
                    continue;
                }

                bool undecided;
                bool mine = IsForMe(target, out undecided);
                if (undecided)
                {
                    // 等這個視窗連上線、知道自己是幾號玩家再決定；等太久就當作不是給自己的。
                    if (s_UndecidedId != id)
                    {
                        s_UndecidedId = id;
                        s_UndecidedSince = now;
                    }

                    if (now - s_UndecidedSince < k_UndecidedTimeout)
                    {
                        return false;
                    }

                    mine = false;
                }

                SetLastId(id);
                if (!mine)
                {
                    continue;
                }

                string label = "#" + id + " " + verb;
                try
                {
                    Execute(label, verb, args);
                }
                catch (Exception e)
                {
                    Report(label, "發生例外：" + e.GetType().Name + " " + e.Message);
                    s_Active = null;
                }

                return true;
            }

            return false;
        }

        static bool IsForMe(string target, out bool undecided)
        {
            undecided = false;
            switch (target)
            {
                case "all":
                    return true;
                case "main":
                    return !s_IsVirtualPlayer;
                case "vp":
                    return s_IsVirtualPlayer;
            }

            int number;
            if (target.Length >= 2 && (target[0] == 'P' || target[0] == 'p')
                && int.TryParse(target.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                PlayerSlot local = PlayerSlot.Local;
                if (local != null)
                {
                    return local.PlayerNumber == number;
                }

                undecided = EditorApplication.isPlaying;
                return false;
            }

            return false;
        }

        // ------------------------------------------------------------------
        // 指令
        // ------------------------------------------------------------------

        static void Execute(string label, string verb, string[] args)
        {
            switch (verb)
            {
                case "play":
                    s_PauseAfterCommand = true;
                    if (s_IsVirtualPlayer)
                    {
                        Report(label, "略過（虛擬玩家視窗會跟著主視窗進出 Play 模式）");
                    }
                    else if (EditorApplication.isPlaying)
                    {
                        Report(label, "已經在 Play 模式");
                    }
                    else
                    {
                        Report(label, "進入 Play 模式");
                        EditorApplication.isPlaying = true;
                    }

                    break;

                case "stop":
                    s_PauseAfterCommand = true;
                    if (!s_IsVirtualPlayer && EditorApplication.isPlaying)
                    {
                        Report(label, "離開 Play 模式");
                        EditorApplication.isPlaying = false;
                    }
                    else
                    {
                        Report(label, "不在 Play 模式");
                    }

                    break;

                case "refresh":
                    s_PauseAfterCommand = true;
                    if (!s_IsVirtualPlayer)
                    {
                        Report(label, "重新匯入");
                        AssetDatabase.Refresh();
                    }

                    break;

                case "rebuild":
                    s_PauseAfterCommand = true;
                    if (s_IsVirtualPlayer)
                    {
                        break;
                    }

                    if (EditorApplication.isPlaying)
                    {
                        Report(label, "失敗：要先 stop 才能重建場景");
                    }
                    else
                    {
                        PrototypeBuilder.Build();
                        Report(label, "已重建場景（接著會重新編譯一次，讓虛擬玩家視窗同步新的預製物；等 claude-compile.txt 更新再 play）");

                        // 預製物的內容有變時（例如加了新元件），虛擬玩家視窗手上還是舊的那一份，
                        // 連線時會因為識別碼對不上被主機拒絕（NetworkConfig mismatch）。
                        // 重新編譯會讓所有視窗重新載入，資源也跟著同步。
                        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                    }

                    break;

                case "host":
                    StartConnection(label, true);
                    break;

                case "join":
                    StartConnection(label, false);
                    break;

                case "leave":
                    if (ConnectionManager.Instance != null)
                    {
                        ConnectionManager.Instance.Leave();
                        Report(label, "已離開房間");
                    }

                    break;

                case "role":
                    StartRole(label, args);
                    break;

                case "walk":
                    StartWalk(label, args);
                    break;

                case "teleport":
                    Teleport(label, args);
                    break;

                case "jump":
                {
                    PlayerAvatar avatar = LocalAvatar();
                    if (avatar != null)
                    {
                        avatar.GetComponent<PlayerMovement>().Jump();
                        Report(label, "跳");
                    }
                    else
                    {
                        Report(label, "失敗：沒有自己的角色");
                    }

                    break;
                }

                case "swing":
                {
                    PlayerAvatar avatar = LocalAvatar();
                    PlayerCapture capture = avatar != null ? avatar.Capture : null;
                    if (capture == null)
                    {
                        Report(label, "失敗：沒有自己的角色");
                    }
                    else
                    {
                        bool accepted = capture.CanSwing;
                        capture.RequestSwing();
                        Report(label, accepted ? "揮網" : "揮網（現在不能揮，主機不會理：" + DescribeAvatar(avatar) + "）");
                    }

                    break;
                }

                case "interact":
                {
                    PlayerAvatar avatar = LocalAvatar();
                    PlayerCapture capture = avatar != null ? avatar.Capture : null;
                    if (capture == null)
                    {
                        Report(label, "失敗：沒有自己的角色");
                    }
                    else
                    {
                        capture.RequestInteract();
                        Report(label, "按了扛起／放下");
                    }

                    break;
                }

                case "struggle":
                    StartStruggle(label, args);
                    break;

                case "pick":
                    StartPick(label, args);
                    break;

                case "rescue":
                    StartRescue(label, args);
                    break;

                case "cagetime":
                    SetCageTime(label, args);
                    break;

                case "sounds":
                {
                    var builder = new StringBuilder("聽到：");
                    string[] names = { "腳步聲", "鈴鐺", "叫聲" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        SoundCues.Tally tally = SoundCues.TakeTally((SoundKind)i);
                        builder.Append(i > 0 ? "｜" : "").Append(names[i]).Append(' ').Append(tally.Count).Append(" 次");
                        if (tally.Count > 0)
                        {
                            builder.Append("（").Append(Format(tally.MinDistance)).Append("～").Append(Format(tally.MaxDistance)).Append(" 公尺）");
                        }
                    }

                    Report(label, builder.ToString());
                    break;
                }

                case "hold":
                {
                    PlayerAvatar avatar = LocalAvatar();
                    string which = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                    bool on = args.Length < 2 || args[1].ToLowerInvariant() != "off";
                    if (avatar == null)
                    {
                        Report(label, "失敗：沒有自己的角色");
                    }
                    else if (which == "rescue")
                    {
                        avatar.GetComponent<PlayerRescue>().SetExternalHold(on);
                        Report(label, "救援鍵" + (on ? "按住" : "放開"));
                    }
                    else if (which == "pick")
                    {
                        avatar.GetComponent<PlayerLockpick>().SetExternalHold(on);
                        Report(label, "撬鎖鍵" + (on ? "按住" : "放開"));
                    }
                    else
                    {
                        Report(label, "失敗：要寫 hold rescue on|off 或 hold pick on|off");
                    }

                    break;
                }

                case "spectate":
                {
                    PlayerAvatar avatar = LocalAvatar();
                    PlayerCapture capture = avatar != null ? avatar.Capture : null;
                    if (capture == null || !capture.IsOut)
                    {
                        Report(label, "失敗：自己沒有出局");
                        break;
                    }

                    capture.RequestNextSpectate();
                    Report(label, "換人看");
                    break;
                }

                case "unlock":
                case "lock":
                    SetHabitats(label, args, verb == "unlock");
                    break;

                case "look":
                    if (PlayerCamera.Instance != null)
                    {
                        float seconds = args.Length > 2 ? Number(args, 2) : 0f;
                        PlayerCamera.Instance.LookTo(Number(args, 0), Number(args, 1), seconds);
                        Report(label, "鏡頭 yaw=" + Format(Number(args, 0)) + " pitch=" + Format(Number(args, 1))
                            + (seconds > 0f ? "（" + Format(seconds) + " 秒內轉過去）" : ""));
                    }

                    break;

                case "follow":
                    if (PlayerCamera.Instance != null)
                    {
                        bool on = args.Length == 0 || args[0].ToLowerInvariant() != "off";
                        PlayerCamera.Instance.AutoFollow = on;
                        Report(label, on ? "鏡頭自動跟隨：開" : "鏡頭自動跟隨：關");
                    }

                    break;

                case "map":
                {
                    MapRegistry registry = MapRegistry.Instance;
                    if (registry == null || registry.Count == 0)
                    {
                        Report(label, "失敗：不在 Play 模式，或場景裡沒有地圖");
                        break;
                    }

                    string choice = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                    int index;
                    if (choice.Length == 1 && choice[0] >= 'a' && choice[0] <= 'z')
                    {
                        index = choice[0] - 'a';
                    }
                    else if (!int.TryParse(choice, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                    {
                        Report(label, "失敗：要寫 map a、map b、map c");
                        break;
                    }

                    index = registry.Wrap(index);
                    if (MatchState.Instance == null)
                    {
                        registry.Select(index);
                    }
                    else if (MatchState.Instance.IsServer)
                    {
                        MatchState.Instance.SetMap(index);
                    }
                    else
                    {
                        Report(label, "失敗：已經在房間裡，只有主機能換地圖");
                        break;
                    }

                    Report(label, "地圖=" + registry.maps[index].displayName);
                    break;
                }

                case "view":
                {
                    string side = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                    string mode = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                    ViewMode viewMode = mode.StartsWith("third") ? ViewMode.ThirdPerson : ViewMode.FirstPerson;
                    if (side.StartsWith("human"))
                    {
                        GameConfig.Instance.humanViewMode = viewMode;
                    }
                    else if (side.StartsWith("animal"))
                    {
                        GameConfig.Instance.animalViewMode = viewMode;
                    }
                    else
                    {
                        Report(label, "失敗：要寫 view human first、view animal third 這樣");
                        break;
                    }

                    Report(label, "人類=" + GameConfig.Instance.humanViewMode + " 動物=" + GameConfig.Instance.animalViewMode
                        + "（暫時的，測完記得切回原本的設定）");
                    break;
                }

                case "record":
                    StartRecording(label, args);
                    break;

                case "res":
                    SetGameViewResolution(label, args);
                    break;

                case "t0":
                    s_TimeZero = double.Parse(args[0], CultureInfo.InvariantCulture);
                    SessionState.SetString(k_TimeZeroKey, args[0]);
                    Report(label, "起點設好了（" + Format((float)(s_TimeZero - EpochNow())) + " 秒後）");
                    break;

                case "until":
                    StartUntil(label, Number(args, 0));
                    break;

                case "shot":
                    StartShot(label, args);
                    break;

                case "camshot":
                    CameraShot(label, args);
                    break;

                case "report":
                    Report(label, Describe());
                    break;

                case "nethash":
                    Report(label, DescribeNetworkConfig());
                    break;

                case "wait":
                    StartWait(label, Number(args, 0));
                    break;

                default:
                    Report(label, "看不懂的動作");
                    break;
            }
        }

        static void StartConnection(string label, bool host)
        {
            double deadline = EditorApplication.timeSinceStartup + 15.0;
            bool requested = false;
            s_ActiveLabel = label;
            s_Active = deltaTime =>
            {
                if (!EditorApplication.isPlaying)
                {
                    Report(label, "失敗：不在 Play 模式");
                    return true;
                }

                ConnectionManager connection = ConnectionManager.Instance;
                if (connection != null && !requested && connection.State == ConnectionState.Offline)
                {
                    requested = true;
                    if (host)
                    {
                        connection.StartLocalHost();
                    }
                    else
                    {
                        connection.StartLocalClient("127.0.0.1");
                    }
                }

                if (connection != null && connection.State == ConnectionState.Connected && PlayerSlot.Local != null)
                {
                    Report(label, "已連線，我是 P" + PlayerSlot.Local.PlayerNumber);
                    return true;
                }

                if (EditorApplication.timeSinceStartup > deadline)
                {
                    Report(label, "失敗：等了 15 秒還沒連上。" + (connection != null ? connection.StatusMessage : "（場景還沒載入）"));
                    return true;
                }

                return false;
            };
        }

        static void StartRole(string label, string[] args)
        {
            Role role;
            if (args.Length < 1 || !Enum.TryParse(args[0], true, out role))
            {
                Report(label, "失敗：身分要寫 Human、Rabbit、Monkey、Raccoon、Turtle 或 None");
                return;
            }

            PlayerSlot local = PlayerSlot.Local;
            if (local == null)
            {
                Report(label, "失敗：還沒連線");
                return;
            }

            // 選身分一定會產生新的角色（就算和現在一樣），所以要等到「不是原本那個」的角色出現才算好；
            // 不然選同一種身分時會看到舊角色就回報成功（2026-11-17 踩過：關在籠子裡被擋下來，卻回報成功）。
            PlayerAvatar before = LocalAvatar();
            local.RequestRole(role);

            double deadline = EditorApplication.timeSinceStartup + 5.0;
            float requestTime = Time.unscaledTime;
            s_ActiveLabel = label;
            s_Active = deltaTime =>
            {
                PlayerAvatar avatar = LocalAvatar();
                bool ready = role == Role.None
                    ? avatar == null && local.CurrentRole.Value == Role.None
                    : avatar != null && avatar != before && avatar.AvatarRole.Value == role && local.CurrentRole.Value == role;
                if (ready)
                {
                    Report(label, role == Role.None ? "已回到選身分" : "身分=" + role + " " + DescribeAvatar(avatar));
                    return true;
                }

                if (local != null && local.DeniedRole == role && local.DeniedTime >= requestTime)
                {
                    Report(label, "被拒絕：" + role + "（已經有人選了）");
                    return true;
                }

                if (EditorApplication.timeSinceStartup > deadline)
                {
                    Report(label, "失敗：等了 5 秒身分沒有變成 " + role);
                    return true;
                }

                return false;
            };
        }

        static void StartWalk(string label, string[] args)
        {
            PlayerAvatar avatar = LocalAvatar();
            if (avatar == null)
            {
                Report(label, "失敗：沒有自己的角色");
                return;
            }

            var movement = avatar.GetComponent<PlayerMovement>();
            var target = new Vector3(Number(args, 0), 0f, Number(args, 1));

            float force = 1f;
            bool jump = false;
            bool sneak = false;
            bool through = false;
            for (int i = 2; i < args.Length; i++)
            {
                string option = args[i].ToLowerInvariant();
                if (option == "jump")
                {
                    jump = true;
                }
                else if (option == "sneak")
                {
                    sneak = true;
                }
                else if (option == "thru")
                {
                    through = true;
                }
                else
                {
                    force = Mathf.Clamp(Number(args, i), 0.05f, 1f);
                }
            }

            float speed = avatar.Settings.moveSpeed * (sneak ? GameConfig.Instance.sneakSpeedMultiplier : 1f);
            Vector3 start = avatar.transform.position;
            double startTime = EditorApplication.timeSinceStartup;

            // 每半秒比較「應該走的距離」和「實際走的距離」，幾乎沒前進就算被擋住。
            double checkTime = startTime;
            Vector3 checkpoint = start;
            bool firstTick = true;

            // 新的 walk 接手了，上一段路過之後的滑行就不用再管。
            s_CoastingMovement = null;

            s_ActiveLabel = label;
            s_Active = deltaTime =>
            {
                if (avatar == null || !avatar.IsSpawned)
                {
                    Report(label, "失敗：角色不見了");
                    return true;
                }

                Vector3 position = avatar.transform.position;
                Vector3 toTarget = target - position;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;
                if (distance <= (through ? 0.45f : 0.08f))
                {
                    if (through)
                    {
                        // 不停下來：先照原方向繼續走，等下一個 walk 接手；一小段時間內沒人接手才停。
                        s_CoastingMovement = movement;
                        s_CoastUntil = EditorApplication.timeSinceStartup + 0.15;
                    }
                    else
                    {
                        movement.ClearExternalInput();
                    }

                    Report(label, (through ? "路過 " : "走到了 ") + DescribeAvatar(avatar) + " 走了=" + Format(Planar(position - start)));
                    return true;
                }

                // 快到的時候放慢，才不會衝過頭；只是路過的點就不減速。
                float push = through ? force : Mathf.Min(force, Mathf.Max(0.15f, distance / (speed * 0.12f)));
                movement.SetExternalInput(toTarget / distance * push, sneak);
                if (firstTick)
                {
                    firstTick = false;
                    if (jump)
                    {
                        movement.Jump();
                    }
                }

                double now = EditorApplication.timeSinceStartup;
                if (now - checkTime >= 0.5)
                {
                    float moved = Planar(avatar.transform.position - checkpoint);
                    if (moved < speed * push * (float)(now - checkTime) * 0.1f)
                    {
                        movement.ClearExternalInput();
                        Report(label, "被擋住 " + DescribeAvatar(avatar) + " 走了=" + Format(Planar(avatar.transform.position - start))
                            + " 離目標=" + Format(distance));
                        return true;
                    }

                    checkTime = now;
                    checkpoint = avatar.transform.position;
                }

                if (now - startTime > 40.0)
                {
                    movement.ClearExternalInput();
                    Report(label, "逾時 " + DescribeAvatar(avatar));
                    return true;
                }

                return false;
            };
        }

        static void Teleport(string label, string[] args)
        {
            PlayerAvatar avatar = LocalAvatar();
            if (avatar == null)
            {
                Report(label, "失敗：沒有自己的角色");
                return;
            }

            var position = new Vector3(Number(args, 0), Number(args, 1), Number(args, 2));
            Quaternion rotation = args.Length > 3 ? Quaternion.Euler(0f, Number(args, 3), 0f) : avatar.transform.rotation;

            // CharacterController 會記住位置，要先停用才搬得動。
            var controller = avatar.GetComponent<CharacterController>();
            var networkTransform = avatar.GetComponent<NetworkTransform>();
            controller.enabled = false;
            if (networkTransform != null)
            {
                networkTransform.Teleport(position, rotation, avatar.transform.localScale);
            }
            else
            {
                avatar.transform.SetPositionAndRotation(position, rotation);
            }

            controller.enabled = true;
            Report(label, "已移動 " + DescribeAvatar(avatar));
        }

        static void StartShot(string label, string[] args)
        {
            ConnectionManager host = ConnectionManager.Instance;
            if (!EditorApplication.isPlaying || host == null)
            {
                Report(label, "失敗：不在 Play 模式");
                return;
            }

            string name = args.Length > 0 ? args[0] : "shot";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            string folder = s_LogsDir + "/" + k_ShotsFolderName;
            Directory.CreateDirectory(folder);
            string path = folder + "/" + name + ".png";

            // 等這一格畫完（包含介面）再把畫面讀出來存檔。
            bool finished = false;
            string error = null;
            long bytes = 0;
            host.StartCoroutine(CaptureGameView(path, (size, problem) =>
            {
                bytes = size;
                error = problem;
                finished = true;
            }));

            double deadline = EditorApplication.timeSinceStartup + 5.0;
            s_ActiveLabel = label;
            s_Active = deltaTime =>
            {
                if (finished)
                {
                    Report(label, error == null
                        ? "已存 " + k_ShotsFolderName + "/" + name + ".png（" + bytes + " bytes）"
                        : "失敗：" + error);
                    return true;
                }

                if (EditorApplication.timeSinceStartup > deadline)
                {
                    Report(label, "失敗：畫面沒有存出來（Game 視窗要看得到才截得到）");
                    return true;
                }

                return false;
            };
        }

        /// <summary>
        /// 用一台臨時的鏡頭把場景畫到貼圖上存檔。不經過 Game 視窗，所以沒有介面，也不受視窗大小影響。
        /// </summary>
        static void CameraShot(string label, string[] args)
        {
            if (args.Length < 1)
            {
                Report(label, "失敗：要給檔名");
                return;
            }

            const int width = 1600;
            const int height = 1200;

            string folder = s_LogsDir + "/" + k_ShotsFolderName;
            Directory.CreateDirectory(folder);
            string name = args[0];
            string path = folder + "/" + name + ".png";

            var cameraObject = new GameObject("DevTest Shot Camera");
            RenderTexture target = null;
            Texture2D texture = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 300f;
                if (args.Length >= 6)
                {
                    camera.transform.SetPositionAndRotation(
                        new Vector3(Number(args, 1), Number(args, 2), Number(args, 3)),
                        Quaternion.Euler(Number(args, 5), Number(args, 4), 0f));
                    camera.fieldOfView = args.Length >= 7 ? Number(args, 6) : 60f;
                }
                else
                {
                    // 正上方往下看，畫面的上方是北（+Z）、右方是東（+X）。
                    camera.orthographic = true;
                    camera.orthographicSize = 31.5f;
                    camera.transform.SetPositionAndRotation(new Vector3(0f, 80f, 0f), Quaternion.Euler(90f, 0f, 0f));
                }

                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                byte[] png = ImageConversion.EncodeToPNG(texture);
                File.WriteAllBytes(path, png);
                camera.targetTexture = null;
                Report(label, "已存 " + k_ShotsFolderName + "/" + name + ".png（" + png.Length + " bytes）");
            }
            catch (Exception e)
            {
                Report(label, "失敗：" + e.GetType().Name + " " + e.Message);
            }
            finally
            {
                RenderTexture.active = previous;
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }

                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        static IEnumerator CaptureGameView(string path, Action<long, string> onDone)
        {
            yield return new WaitForEndOfFrame();

            long bytes = 0;
            string error = null;
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
                texture.Apply();
                byte[] png = ImageConversion.EncodeToPNG(texture);
                File.WriteAllBytes(path, png);
                bytes = png.Length;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + " " + e.Message;
            }

            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }

            onDone(bytes, error);
        }

        static void StartRecording(string label, string[] args)
        {
            ConnectionManager host = ConnectionManager.Instance;
            if (!EditorApplication.isPlaying || host == null)
            {
                Report(label, "失敗：不在 Play 模式");
                return;
            }

            if (s_RecordingName.Length > 0)
            {
                Report(label, "失敗：已經在錄 " + s_RecordingName);
                return;
            }

            string name = args.Length > 0 ? args[0] : "recording";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            float seconds = Mathf.Clamp(args.Length > 1 ? Number(args, 1) : 10f, 1f, 300f);
            float framesPerSecond = Mathf.Clamp(args.Length > 2 ? Number(args, 2) : 30f, 1f, 60f);

            string folder = s_LogsDir + "/" + k_RecordingsFolderName + "/" + name;
            Directory.CreateDirectory(folder);

            s_RecordingName = name;
            host.StartCoroutine(RecordGameView(label, folder, seconds, framesPerSecond));
            Report(label, "開始錄影 " + name + "（" + Format(seconds) + " 秒，每秒 " + Format(framesPerSecond) + " 張）");
        }

        /// <summary>
        /// 每隔固定時間，在一格畫完之後把遊戲畫面（含介面）讀出來存成 JPG。
        /// 每張的實際時間記在 frames.csv，之後合成影片時照這個時間排，速度才會和實際一樣。
        /// </summary>
        static IEnumerator RecordGameView(string label, string folder, float seconds, float framesPerSecond)
        {
            var endOfFrame = new WaitForEndOfFrame();
            var timestamps = new StringBuilder();
            string csvPath = folder + "/frames.csv";
            File.WriteAllText(csvPath, "");

            Texture2D texture = null;
            double start = EpochNow();
            double next = start;
            int count = 0;
            long bytes = 0;
            string error = null;

            while (error == null)
            {
                yield return endOfFrame;

                double now = EpochNow();
                if (now - start >= seconds)
                {
                    break;
                }

                if (now < next)
                {
                    continue;
                }

                // 來不及的時候不補拍，直接排下一張。
                next = Math.Max(next + 1.0 / framesPerSecond, now);

                try
                {
                    int width = Screen.width;
                    int height = Screen.height;
                    if (texture == null || texture.width != width || texture.height != height)
                    {
                        if (texture != null)
                        {
                            UnityEngine.Object.Destroy(texture);
                        }

                        texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                    }

                    texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                    byte[] jpg = ImageConversion.EncodeToJPG(texture, 90);
                    count++;
                    File.WriteAllBytes(folder + "/f_" + count.ToString("000000", CultureInfo.InvariantCulture) + ".jpg", jpg);
                    bytes += jpg.Length;

                    timestamps.Append(count).Append(',')
                        .Append(((long)(now * 1000.0)).ToString(CultureInfo.InvariantCulture)).Append('\n');
                    if (count % 30 == 0)
                    {
                        File.AppendAllText(csvPath, timestamps.ToString());
                        timestamps.Length = 0;
                    }
                }
                catch (Exception e)
                {
                    error = e.GetType().Name + " " + e.Message;
                }
            }

            File.AppendAllText(csvPath, timestamps.ToString());
            int lastWidth = texture != null ? texture.width : 0;
            int lastHeight = texture != null ? texture.height : 0;
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }

            s_RecordingName = "";
            Report(label, error == null
                ? "錄完了：" + count + " 張，" + lastWidth + "x" + lastHeight + "，共 " + (bytes / 1024 / 1024) + " MB，實際每秒 "
                    + Format(count / Mathf.Max(0.01f, (float)(EpochNow() - start))) + " 張"
                : "錄影中斷：" + error + "（已存 " + count + " 張）");
        }

        // ------------------------------------------------------------------
        // Game 視窗解析度（錄影時固定成 16:9，錄完還原）
        // 這裡用到編輯器沒有公開的成員，所以用反射；找不到時只回報、不會出錯。
        // ------------------------------------------------------------------

        static void SetGameViewResolution(string label, string[] args)
        {
            if (s_IsVirtualPlayer)
            {
                Report(label, "略過（只處理主視窗）");
                return;
            }

            object view = MainGameView();
            PropertyInfo indexProperty = view != null ? view.GetType().GetProperty("selectedSizeIndex") : null;

            if (args.Length > 0 && args[0].ToLowerInvariant() == "restore")
            {
                RestoreGameViewSize(label, view, indexProperty);
                return;
            }

            uint width = (uint)Mathf.Clamp(Number(args, 0), 160f, 3840f);
            uint height = (uint)Mathf.Clamp(Number(args, 1), 90f, 2160f);

            // 記住原本選的是哪一個，之後才還原得回去（已經記過就不要蓋掉）。
            if (indexProperty != null && SessionState.GetInt(k_PreviousSizeKey, -1) < 0)
            {
                SessionState.SetInt(k_PreviousSizeKey, (int)indexProperty.GetValue(view, null));
            }

            PlayModeWindow.SetCustomRenderingResolution(width, height, k_RecordSizeName);
            Report(label, "Game 視窗固定為 " + width + "x" + height
                + (indexProperty == null ? "（注意：記不到原本的設定，之後要手動選回 Free Aspect）" : ""));
        }

        static void RestoreGameViewSize(string label, object view, PropertyInfo indexProperty)
        {
            int previous = SessionState.GetInt(k_PreviousSizeKey, -1);
            if (previous < 0)
            {
                Report(label, "沒有需要還原的設定");
                return;
            }

            if (view == null || indexProperty == null)
            {
                Report(label, "失敗：找不到 Game 視窗，請手動把解析度選回 Free Aspect");
                return;
            }

            indexProperty.SetValue(view, previous, null);

            // 把錄影用的那一筆自訂解析度從清單裡拿掉。
            int removed = 0;
            Type sizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
            PropertyInfo instanceProperty = sizesType != null && sizesType.BaseType != null
                ? sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                : null;
            object sizes = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
            object group = sizes != null ? sizesType.GetProperty("currentGroup").GetValue(sizes, null) : null;
            if (group != null)
            {
                Type groupType = group.GetType();
                int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
                for (int i = total - 1; i >= 0; i--)
                {
                    object size = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                    bool custom = (bool)groupType.GetMethod("IsCustomSize").Invoke(group, new object[] { i });
                    string baseText = size != null ? (string)size.GetType().GetProperty("baseText").GetValue(size, null) : null;
                    if (custom && baseText == k_RecordSizeName)
                    {
                        groupType.GetMethod("RemoveCustomSize").Invoke(group, new object[] { i });
                        removed++;
                    }
                }

                sizesType.GetMethod("SaveToHDD").Invoke(sizes, null);
            }

            MethodInfo refresh = view.GetType().GetMethod("UpdateZoomAreaAndParent", BindingFlags.Instance | BindingFlags.NonPublic);
            if (refresh != null)
            {
                refresh.Invoke(view, null);
            }

            var window = view as EditorWindow;
            if (window != null)
            {
                window.Repaint();
            }

            SessionState.EraseInt(k_PreviousSizeKey);
            Report(label, "Game 視窗已還原（原本的選項編號 " + previous + "，移除了 " + removed + " 筆錄影用的解析度）");
        }

        static object MainGameView()
        {
            Type viewType = typeof(Editor).Assembly.GetType("UnityEditor.PlayModeView");
            MethodInfo method = viewType != null
                ? viewType.GetMethod("GetMainPlayModeView", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                : null;
            return method != null ? method.Invoke(null, null) : null;
        }

        // ------------------------------------------------------------------
        // 對時與等待
        // ------------------------------------------------------------------

        static double EpochNow()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        static void StartUntil(string label, float seconds)
        {
            if (s_TimeZero <= 0.0)
            {
                double saved;
                if (double.TryParse(SessionState.GetString(k_TimeZeroKey, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out saved))
                {
                    s_TimeZero = saved;
                }
            }

            if (s_TimeZero <= 0.0)
            {
                Report(label, "失敗：還沒用 t0 設定起點");
                return;
            }

            double target = s_TimeZero + seconds;
            double late = EpochNow() - target;
            if (late >= 0.0)
            {
                // 已經過了這個時間點：不等，但記下來晚了多少，方便調整腳本。
                if (late > 0.15)
                {
                    Report(label, "第 " + Format(seconds) + " 秒：晚了 " + Format((float)late) + " 秒");
                }

                return;
            }

            s_ActiveLabel = label;
            s_Active = deltaTime => EpochNow() >= target;
        }

        /// <summary>連按掙脫：照指定的速度呼叫 RequestStruggle（和按空白鍵走同一條路），掙脫了就提早結束。</summary>
        static void StartStruggle(string label, string[] args)
        {
            PlayerAvatar avatar = LocalAvatar();
            PlayerCapture capture = avatar != null ? avatar.Capture : null;
            if (capture == null)
            {
                Report(label, "失敗：沒有自己的角色");
                return;
            }

            int presses = args.Length > 0 ? Mathf.Clamp(Mathf.RoundToInt(Number(args, 0)), 1, 500) : 1;
            float rate = args.Length > 1 ? Mathf.Clamp(Number(args, 1), 0.5f, 30f) : 8f;
            double interval = 1.0 / rate;
            double next = EditorApplication.timeSinceStartup;
            double finishedAt = -1.0;
            int done = 0;

            s_ActiveLabel = label;
            s_Active = deltaTime =>
            {
                if (avatar == null || !avatar.IsSpawned)
                {
                    Report(label, "失敗：角色不見了");
                    return true;
                }

                double now = EditorApplication.timeSinceStartup;
                if (capture.State.Value == CaptureState.Free)
                {
                    Report(label, "按了 " + done + " 下，現在是自由的 " + DescribeAvatar(avatar));
                    return true;
                }

                if (done < presses)
                {
                    if (now >= next)
                    {
                        capture.RequestStruggle();
                        done++;
                        next += interval;
                    }

                    return false;
                }

                // 按完之後再等一下，讓主機的結果同步回來。
                if (finishedAt < 0.0)
                {
                    finishedAt = now + 0.4;
                }

                if (now < finishedAt)
                {
                    return false;
                }

                Report(label, "按了 " + done + " 下 " + DescribeAvatar(avatar));
                return true;
            };
        }

        /// <summary>
        /// 撬鎖：按住撬鎖鍵（和按住 E 走同一段程式），直到伸手構得到的那道門打開、或超過時間上限。
        /// 回報從按下去到門打開實際花了幾秒。
        /// </summary>
        static void StartPick(string label, string[] args)
        {
            PlayerAvatar avatar = LocalAvatar();
            PlayerLockpick lockpick = avatar != null ? avatar.GetComponent<PlayerLockpick>() : null;
            if (lockpick == null)
            {
                Report(label, "失敗：沒有自己的角色");
                return;
            }

            HabitatGate gate = lockpick.GateInReach;
            if (gate == null)
            {
                HabitatGate own = HabitatGate.Find(avatar.AvatarRole.Value);
                Report(label, "失敗：附近沒有鎖著的門可以撬（能不能撬=" + lockpick.CanPick
                    + (own != null ? "，離自己收容區的門 " + Format(own.PlanarDistance(avatar.transform.position)) + " 公尺、門" + (own.IsOpen ? "開著" : "鎖著") : "")
                    + "）" + DescribeAvatar(avatar));
                return;
            }

            float limit = args.Length > 0 ? Mathf.Clamp(Number(args, 0), 0.1f, 60f) : 20f;
            Role gateRole = gate.role;
            double startedAt = EditorApplication.timeSinceStartup;
            bool everStarted = false;
            lockpick.SetExternalHold(true);

            s_ActiveLabel = label;
            s_Active = deltaTime =>
            {
                if (avatar == null || !avatar.IsSpawned || lockpick == null)
                {
                    Report(label, "失敗：角色不見了");
                    return true;
                }

                double elapsed = EditorApplication.timeSinceStartup - startedAt;
                everStarted |= lockpick.IsPicking;
                if (gate == null || gate.IsOpen)
                {
                    lockpick.SetExternalHold(false);
                    Report(label, "門開了：" + gateRole + " 的收容區，按了 " + Format((float)elapsed) + " 秒 " + DescribeAvatar(avatar));
                    return true;
                }

                if (elapsed >= limit)
                {
                    lockpick.SetExternalHold(false);
                    Report(label, "放手了（按了 " + Format((float)elapsed) + " 秒，門還鎖著；"
                        + (everStarted ? "進度到 " + Format(lockpick.Progress) : "主機沒有開始計時") + "）" + DescribeAvatar(avatar));
                    return true;
                }

                return false;
            };
        }

        /// <summary>救援：按住救援鍵（和按住 E 走同一段程式），直到籠子裡的隊友被救出來、或超過時間上限。</summary>
        static void StartRescue(string label, string[] args)
        {
            PlayerAvatar avatar = LocalAvatar();
            PlayerRescue rescue = avatar != null ? avatar.GetComponent<PlayerRescue>() : null;
            if (rescue == null)
            {
                Report(label, "失敗：沒有自己的角色");
                return;
            }

            CaptureCage cage = rescue.CageInReach;
            if (cage == null)
            {
                Report(label, "失敗：附近沒有關著隊友的籠子（能不能救=" + rescue.CanRescue + "）" + DescribeAvatar(avatar));
                return;
            }

            PlayerCapture occupant = cage.Occupant;
            int number = cage.number;
            float limit = args.Length > 0 ? Mathf.Clamp(Number(args, 0), 0.1f, 60f) : 15f;
            double startedAt = EditorApplication.timeSinceStartup;
            bool everStarted = false;
            rescue.SetExternalHold(true);

            s_ActiveLabel = label;
            s_Active = deltaTime =>
            {
                if (avatar == null || !avatar.IsSpawned)
                {
                    Report(label, "失敗：角色不見了");
                    return true;
                }

                double elapsed = EditorApplication.timeSinceStartup - startedAt;
                everStarted |= rescue.IsRescuing;
                bool freed = occupant == null || !occupant.IsSpawned || occupant.State.Value != CaptureState.Caged;
                if (freed)
                {
                    rescue.SetExternalHold(false);
                    Report(label, "救出來了：" + number + " 號籠，按了 " + Format((float)elapsed) + " 秒"
                        + (occupant != null && occupant.Avatar != null ? "｜被救的 P" + occupant.Avatar.PlayerNumber + " " + DescribeAvatar(occupant.Avatar) : ""));
                    return true;
                }

                if (elapsed >= limit)
                {
                    rescue.SetExternalHold(false);
                    Report(label, "放手了（按了 " + Format((float)elapsed) + " 秒，還沒救出來；"
                        + (everStarted ? "進度到 " + Format(rescue.Progress) : "主機沒有開始計時") + "）" + DescribeAvatar(avatar));
                    return true;
                }

                return false;
            };
        }

        /// <summary>（主機）直接改籠子裡動物的倒數秒數。</summary>
        static void SetCageTime(string label, string[] args)
        {
            MatchState match = MatchState.Instance;
            if (match == null || !match.IsServer)
            {
                Report(label, "失敗：要在開了房間的主機上下這個指令");
                return;
            }

            float seconds = Number(args, 0);
            int only = args.Length > 1 ? Mathf.RoundToInt(Number(args, 1)) : 0;
            int changed = 0;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                PlayerCapture capture = avatar != null ? avatar.Capture : null;
                if (capture != null && capture.State.Value == CaptureState.Caged && (only == 0 || capture.CageNumber.Value == only))
                {
                    capture.ServerSetCageTime(seconds);
                    changed++;
                }
            }

            Report(label, "改了 " + changed + " 隻的倒數 → " + Format(seconds) + " 秒");
        }

        /// <summary>目前這張地圖上每個捕捉籠：關著誰、倒數、暫停。</summary>
        static string DescribeCages()
        {
            var builder = new StringBuilder("捕捉籠：");
            if (CaptureCage.Active.Count == 0)
            {
                return builder.Append("（這張地圖沒有）").ToString();
            }

            bool any = false;
            for (int number = 1; number <= 9; number++)
            {
                CaptureCage cage = CaptureCage.Find(number);
                if (cage == null)
                {
                    continue;
                }

                PlayerCapture occupant = cage.Occupant;
                builder.Append(any ? "、" : "").Append(number).Append('=');
                any = true;
                if (occupant == null || occupant.Avatar == null)
                {
                    builder.Append("空");
                }
                else
                {
                    builder.Append('P').Append(occupant.Avatar.PlayerNumber).Append(' ')
                        .Append(Format(occupant.CageTimeLeft.Value)).Append("s")
                        .Append(occupant.CagePaused.Value ? "（暫停）" : "");
                }
            }

            return builder.ToString();
        }

        /// <summary>（主機）直接打開或鎖上收容區，不用撬。</summary>
        static void SetHabitats(string label, string[] args, bool open)
        {
            MatchState match = MatchState.Instance;
            if (match == null || !match.IsServer)
            {
                Report(label, "失敗：要在開了房間的主機上下這個指令");
                return;
            }

            string which = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
            if (which == "all")
            {
                for (int i = 0; i < Roles.Selectable.Length; i++)
                {
                    match.ServerSetHabitatOpen(Roles.Selectable[i], open);
                }
            }
            else
            {
                Role role;
                if (!Enum.TryParse(which, true, out role) || role == Role.None)
                {
                    Report(label, "失敗：要寫 Rabbit、Monkey、Raccoon、Turtle 或 all");
                    return;
                }

                match.ServerSetHabitatOpen(role, open);
            }

            Report(label, DescribeHabitats());
        }

        /// <summary>目前這張地圖上每個收容區的門是開著還是鎖著。</summary>
        static string DescribeHabitats()
        {
            var builder = new StringBuilder("收容區：");
            if (HabitatGate.Active.Count == 0)
            {
                return builder.Append("（這張地圖沒有）").ToString();
            }

            for (int i = 0; i < HabitatGate.Active.Count; i++)
            {
                HabitatGate gate = HabitatGate.Active[i];
                if (gate != null)
                {
                    builder.Append(i > 0 ? "、" : "").Append(gate.role).Append(gate.IsOpen ? "=開" : "=鎖")
                        .Append("（門在 ").Append(Format(gate.transform.position)).Append("）");
                }
            }

            return builder.ToString();
        }

        static void StartWait(string label, float seconds)
        {
            double until = EditorApplication.timeSinceStartup + Mathf.Clamp(seconds, 0f, 60f);
            s_ActiveLabel = label;
            s_Active = deltaTime =>
            {
                if (EditorApplication.timeSinceStartup < until)
                {
                    return false;
                }

                Report(label, "等完了");
                return true;
            };
        }

        // ------------------------------------------------------------------
        // 狀態描述
        // ------------------------------------------------------------------

        static string Describe()
        {
            var builder = new StringBuilder();
            builder.Append(EditorApplication.isPlaying ? "Play 模式" : "沒有在 Play");

            ConnectionManager connection = ConnectionManager.Instance;
            if (connection != null)
            {
                builder.Append("｜連線=").Append(connection.State);
            }

            if (MapRegistry.Instance != null && MapRegistry.Instance.Active != null)
            {
                builder.Append("｜地圖=").Append(MapRegistry.Instance.Active.displayName);
            }

            builder.Append("｜視角：人類=").Append(GameConfig.Instance.humanViewMode)
                .Append(" 動物=").Append(GameConfig.Instance.animalViewMode);
            if (EditorApplication.isPlaying)
            {
                builder.Append("｜").Append(DescribeHabitats());
                builder.Append("｜").Append(DescribeCages());
            }

            builder.Append("｜玩家數=").Append(PlayerSlot.All.Count);
            for (int i = 0; i < PlayerSlot.All.Count; i++)
            {
                PlayerSlot slot = PlayerSlot.All[i];
                if (slot != null)
                {
                    builder.Append(" [P").Append(slot.PlayerNumber).Append('=').Append(slot.CurrentRole.Value)
                        .Append(slot == PlayerSlot.Local ? "*" : "").Append(']');
                }
            }

            PlayerCamera playerCamera = PlayerCamera.Instance;
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar == null)
                {
                    continue;
                }

                builder.Append("\n    P").Append(avatar.PlayerNumber).Append(avatar.IsOwner ? "（我）" : "")
                    .Append(' ').Append(avatar.AvatarRole.Value).Append(' ').Append(DescribeAvatar(avatar));

                // 別人的名牌現在在我的畫面上會不會顯示（和遊戲介面用的是同一個判斷）
                if (!avatar.IsOwner && playerCamera != null && playerCamera.Camera != null)
                {
                    bool outOfPlay = avatar.Capture != null && avatar.Capture.IsOut;
                    builder.Append(!outOfPlay && GameHud.IsNameTagVisible(playerCamera.Camera, avatar) ? " 名牌=顯示" : " 名牌=不顯示");
                }
            }

            if (playerCamera != null)
            {
                Transform cameraTransform = playerCamera.transform;
                builder.Append("\n    鏡頭 pos=").Append(Format(cameraTransform.position))
                    .Append(" yaw=").Append(Format(cameraTransform.eulerAngles.y))
                    .Append(" pitch=").Append(Format(cameraTransform.eulerAngles.x));
            }

            return builder.ToString();
        }

        /// <summary>連線設定的雜湊（主機和玩家要一樣才連得上）和每個網路預製物的識別碼。</summary>
        static string DescribeNetworkConfig()
        {
            var net = Unity.Netcode.NetworkManager.Singleton;
            if (net == null)
            {
                return "沒有 NetworkManager（要在 Play 模式）";
            }

            var builder = new StringBuilder();
            builder.Append("設定雜湊=").Append(net.NetworkConfig.GetConfig(false));
            var lists = net.NetworkConfig.Prefabs.NetworkPrefabsLists;
            builder.Append(" 清單數=").Append(lists.Count);
            for (int i = 0; i < lists.Count; i++)
            {
                var list = lists[i];
                if (list == null)
                {
                    builder.Append(" [清單遺失]");
                    continue;
                }

                builder.Append(" [").Append(list.name).Append(" guid=")
                    .Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(list))).Append(']');
                foreach (var entry in list.PrefabList)
                {
                    GameObject prefab = entry != null ? entry.Prefab : null;
                    if (prefab == null)
                    {
                        builder.Append(" (預製物遺失)");
                        continue;
                    }

                    var networkObject = prefab.GetComponent<Unity.Netcode.NetworkObject>();
                    builder.Append(' ').Append(prefab.name).Append('=')
                        .Append(networkObject != null ? networkObject.PrefabIdHash.ToString() : "沒有NetworkObject")
                        .Append("/元件數").Append(prefab.GetComponents<Component>().Length);
                }
            }

            return builder.ToString();
        }

        static string DescribeAvatar(PlayerAvatar avatar)
        {
            var controller = avatar.GetComponent<CharacterController>();

            // 身體歪了幾度（站直是 0）。被扛著時是 90；其他時候不是 0 就表示姿勢沒有扶正。
            float tilt = Vector3.Angle(avatar.transform.up, Vector3.up);
            return "pos=" + Format(avatar.transform.position)
                + " yaw=" + Format(avatar.transform.eulerAngles.y)
                + (tilt > 1f ? " 傾斜=" + Format(tilt) : "")
                + " 身高=" + Format(controller.height)
                + " 半徑=" + Format(controller.radius)
                + (avatar.IsBodyHidden ? " 身體隱藏" : "")
                + DescribeWaterAndLock(avatar)
                + DescribeSpeed(avatar)
                + DescribeCapture(avatar.Capture);
        }

        /// <summary>這個視窗量到的移動速度（聲音系統用它判斷跑或慢走）。</summary>
        static string DescribeSpeed(PlayerAvatar avatar)
        {
            var sounds = avatar.GetComponent<PlayerSounds>();
            return sounds != null && sounds.MeasuredSpeed > 0.3f ? " 速度=" + Format(sounds.MeasuredSpeed) : "";
        }

        /// <summary>在不在水裡（外觀沉了多少、圖層）、是不是關在收容區裡、撬鎖的進度。</summary>
        static string DescribeWaterAndLock(PlayerAvatar avatar)
        {
            var builder = new StringBuilder();
            if (avatar.InWater)
            {
                builder.Append(" 在水裡（沉 ").Append(Format(avatar.Sink)).Append("）");
            }

            if (avatar.gameObject.layer != 0)
            {
                builder.Append(" 圖層=").Append(avatar.gameObject.layer == GameLayers.Swimmer ? "會游泳" : avatar.gameObject.layer.ToString());
            }

            var movement = avatar.GetComponent<PlayerMovement>();
            if (movement != null && movement.IsStranded)
            {
                builder.Append(" 掉進水裡正在爬上岸");
            }

            HabitatGate own = HabitatGate.Find(avatar.AvatarRole.Value);
            if (own != null && !own.IsOpen && own.Contains(avatar.transform.position))
            {
                builder.Append(" 關在收容區裡");
            }

            var lockpick = avatar.GetComponent<PlayerLockpick>();
            if (lockpick != null && lockpick.IsPicking)
            {
                builder.Append(" 撬鎖中=").Append(lockpick.Target.Value).Append(' ').Append(Format(lockpick.Progress));
            }

            var rescue = avatar.GetComponent<PlayerRescue>();
            if (rescue != null && rescue.IsRescuing)
            {
                builder.Append(" 救援中=").Append(rescue.TargetCage.Value).Append("號籠 ").Append(Format(rescue.Progress));
            }

            PlayerSlot slot = PlayerSlot.FindByClient(avatar.OwnerClientId);
            if (slot != null && slot.TimesCaged.Value > 0)
            {
                builder.Append(" 被關過=").Append(slot.TimesCaged.Value).Append("次");
            }

            return builder.ToString();
        }

        /// <summary>追與抓的狀態：被網住、被誰扛、扛著誰、保護、踉蹌、網子冷卻、上次揮網的結果。</summary>
        static string DescribeCapture(PlayerCapture capture)
        {
            if (capture == null)
            {
                return "";
            }

            var builder = new StringBuilder();
            if (capture.IsHeld)
            {
                builder.Append(" 狀態=").Append(capture.State.Value).Append(" 掙脫=").Append(Format(capture.Struggle.Value));
            }
            else if (capture.State.Value == CaptureState.Caged)
            {
                builder.Append(" 狀態=Caged ").Append(capture.CageNumber.Value).Append("號籠 剩=").Append(Format(capture.CageTimeLeft.Value)).Append("s")
                    .Append(capture.CagePaused.Value ? "（暫停）" : "");
            }
            else if (capture.State.Value == CaptureState.Out)
            {
                builder.Append(" 狀態=Out");
                if (capture == PlayerCapture.Local)
                {
                    builder.Append(capture.Spectating != null ? " 看P" + capture.Spectating.PlayerNumber : " 看全圖");
                }
            }

            if (capture.IsSlowed)
            {
                builder.Append(" 變慢中");
            }

            PlayerCapture carrier = capture.Carrier;
            if (carrier != null && carrier.Avatar != null)
            {
                builder.Append(" 被P").Append(carrier.Avatar.PlayerNumber).Append("扛著");
            }

            PlayerCapture carried = capture.Carried;
            if (carried != null && carried.Avatar != null)
            {
                builder.Append(" 扛著P").Append(carried.Avatar.PlayerNumber);
            }

            if (capture.IsProtected)
            {
                builder.Append(" 保護=").Append(Format(capture.ProtectionRemaining)).Append("s");
            }

            if (capture.IsStaggered)
            {
                builder.Append(" 踉蹌=").Append(Format(capture.StaggerRemaining)).Append("s");
            }

            if (capture.IsHuman && capture.NetCooldownRemaining > 0f)
            {
                builder.Append(" 網子冷卻=").Append(Format(capture.NetCooldownRemaining)).Append("s");
            }

            if (capture == PlayerCapture.Local && capture.LastSwingOutcome != SwingOutcome.None)
            {
                builder.Append(" 上次揮網=").Append(capture.LastSwingOutcome);
            }

            return builder.ToString();
        }

        static PlayerAvatar LocalAvatar()
        {
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar != null && avatar.IsSpawned && avatar.IsOwner)
                {
                    return avatar;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------
        // 檔案與格式
        // ------------------------------------------------------------------

        static string[] ReadCommandLines()
        {
            try
            {
                return File.Exists(s_CommandPath) ? File.ReadAllLines(s_CommandPath) : null;
            }
            catch (IOException)
            {
                // 檔案剛好正在被寫入，下一輪再讀。
                return null;
            }
        }

        static bool ParseLine(string line, out int id, out string target, out string verb, out string[] args)
        {
            id = 0;
            target = "";
            verb = "";
            args = new string[0];

            line = line.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] == '#')
            {
                return false;
            }

            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return false;
            }

            target = parts[1];
            verb = parts[2].ToLowerInvariant();
            args = new string[parts.Length - 3];
            Array.Copy(parts, 3, args, 0, args.Length);
            return true;
        }

        static int FindLargestId()
        {
            int largest = 0;
            string[] lines = ReadCommandLines();
            if (lines == null)
            {
                return largest;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                int id;
                string target;
                string verb;
                string[] args;
                if (ParseLine(lines[i], out id, out target, out verb, out args) && id > largest)
                {
                    largest = id;
                }
            }

            return largest;
        }

        static void SetLastId(int id)
        {
            s_LastId = id;
            SessionState.SetInt(k_LastIdKey, id);
        }

        static void Report(string label, string message)
        {
            string who = s_IsVirtualPlayer ? "vp" : "main";
            if (PlayerSlot.Local != null)
            {
                who += "/P" + PlayerSlot.Local.PlayerNumber;
            }

            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] "
                + who + " " + label + "：" + message;
            Debug.Log("[DevTest] " + line);

            try
            {
                Directory.CreateDirectory(s_LogsDir);
                File.AppendAllText(s_ResultPath, line + "\n", new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // 另一個視窗剛好也在寫；Console 裡還是看得到這一行。
            }
        }

        static float Number(string[] args, int index)
        {
            float value;
            if (index < args.Length && float.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            throw new ArgumentException("第 " + (index + 1) + " 個參數要是數字");
        }

        static float Planar(Vector3 offset)
        {
            offset.y = 0f;
            return offset.magnitude;
        }

        static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string Format(Vector3 value)
        {
            return "(" + Format(value.x) + ", " + Format(value.y) + ", " + Format(value.z) + ")";
        }
    }
}
