using System;
using System.IO;
using System.Threading.Tasks;
using AnimalEscape.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using Mps = Unity.Services.Multiplayer;

namespace AnimalEscape.Networking
{
    public enum ConnectionState
    {
        Offline,
        Connecting,
        Connected
    }

    /// <summary>
    /// 負責開房間與加入房間。有三種連法（主選單選）：
    /// 1. 只用這台電腦：只聽 127.0.0.1，同一台電腦的其他視窗（Multiplayer Play Mode）加得進來。
    /// 2. 同一個 Wi-Fi：直接用 IP 連線，不需要任何雲端設定；房間會在區域網路上廣播（LanDiscovery），別人在「附近的房間」看得到。
    /// 3. 線上：透過 Unity 的 Relay（Multiplayer Services 的 Session），用代碼加入，給不同地方的電腦連線用。
    /// 都是「主機模式」：開房間的人同時是主機，所有判定都由主機處理。
    /// </summary>
    public class ConnectionManager : MonoBehaviour
    {
        public static ConnectionManager Instance { get; private set; }

        public ConnectionState State { get; private set; } = ConnectionState.Offline;

        /// <summary>顯示給玩家看的狀態或錯誤訊息。</summary>
        public string StatusMessage { get; private set; } = "";

        /// <summary>線上房間的加入代碼（本機房間為空字串）。</summary>
        public string JoinCode { get; private set; } = "";

        public bool IsOnlineRoom { get; private set; }

        /// <summary>現在（或上一個）房間的連法。</summary>
        public LinkMode Link { get; private set; } = LinkMode.Lan;

        /// <summary>同一個 Wi-Fi 找房間／廣播房間。</summary>
        public LanDiscovery Lan { get; private set; }

        /// <summary>這台電腦在區域網路上的 IP（開了「同一個 Wi-Fi」的房間時顯示給別人輸入）。</summary>
        public string LocalAddress { get; private set; } = "";

        NetworkManager m_Net;
        Mps.ISession m_Session;
        bool m_LeaveRequested;
        int m_Attempt;
        bool m_AutoJoinLocal;
        float m_NextAutoJoinTime;

        // 手動加入本機房間時最多等幾秒（每次嘗試約 1 秒）；自動加入時主視窗確定已開房，所以更短。
        const int k_ManualConnectAttempts = 8;
        const int k_AutoConnectAttempts = 5;
        int m_DefaultConnectAttempts = 60;

        // 主視窗開本機房間時會寫一個記號檔，虛擬玩家視窗看到它才連線（只在編輯器裡使用）。
        string m_MarkerPath;
        bool m_WroteMarker;
        long m_StaleMarkerTicks;

        /// <summary>沒有連線、上一個連線也已經關乾淨了，可以馬上開新房間或加入。</summary>
        public bool IsIdle
        {
            get { return State == ConnectionState.Offline && m_Net != null && !m_Net.IsListening && !m_Net.ShutdownInProgress; }
        }

        /// <summary>這個視窗是 Multiplayer Play Mode 的虛擬玩家（測試用的第二個視窗）。</summary>
        public bool IsVirtualPlayerWindow
        {
            get { return IsVirtualPlayer(); }
        }

        /// <summary>這是虛擬玩家視窗，正在等主視窗開本機房間（開了就會自動加入）。</summary>
        public bool IsWaitingForLocalHost
        {
            get { return m_AutoJoinLocal && State == ConnectionState.Offline; }
        }

        void Awake()
        {
            Instance = this;
            Application.runInBackground = true;
            Lan = new LanDiscovery(GameConfig.Instance.lanDiscoveryPort);
        }

        void Start()
        {
            m_Net = NetworkManager.Singleton;
            if (m_Net == null)
            {
                StatusMessage = "場景裡找不到 NetworkManager。請用選單 Tools > Animal Escape > 重建原型場景。";
                return;
            }

            m_Net.OnClientConnectedCallback += OnClientConnected;
            m_Net.OnClientStopped += OnClientStopped;
            m_Net.OnTransportFailure += OnTransportFailure;

            var transport = m_Net.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport != null)
            {
                m_DefaultConnectAttempts = transport.MaxConnectAttempts;
            }

            // 用 Multiplayer Play Mode 測試時，虛擬玩家的視窗自動加入主視窗開的本機房間。
            m_MarkerPath = Application.isEditor ? GetMarkerPath() : null;
            m_AutoJoinLocal = GameConfig.Instance.virtualPlayersAutoJoinLocal && IsVirtualPlayer();
            m_NextAutoJoinTime = Time.unscaledTime + 0.5f;
        }

        void OnApplicationQuit()
        {
            DeleteLocalHostMarker();
        }

        void OnDestroy()
        {
            DeleteLocalHostMarker();
            if (Lan != null)
            {
                Lan.Dispose();
            }

            if (m_Net != null)
            {
                m_Net.OnClientConnectedCallback -= OnClientConnected;
                m_Net.OnClientStopped -= OnClientStopped;
                m_Net.OnTransportFailure -= OnTransportFailure;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Update()
        {
            Lan.Tick();

            // 保險：就算沒收到連線事件，只要確認已連上就切換狀態。
            if (State == ConnectionState.Connecting && m_Net != null && m_Net.IsListening && m_Net.IsConnectedClient)
            {
                MarkConnected();
            }

            if (m_AutoJoinLocal && State == ConnectionState.Offline && Time.unscaledTime >= m_NextAutoJoinTime
                && m_Net != null && !m_Net.IsListening && !m_Net.ShutdownInProgress)
            {
                // 主視窗真的開了本機房間才連線，避免對著不存在的房間一直連線失敗。
                m_NextAutoJoinTime = Time.unscaledTime + 0.5f;
                if (IsLocalHostMarkerFresh())
                {
                    ConnectLocalClient("127.0.0.1", "正在加入主視窗的本機房間…", k_AutoConnectAttempts);
                }
            }
        }

        // ------------------------------------------------------------------
        // 本機房間（直接連線）
        // ------------------------------------------------------------------

        /// <summary>照連法開房間：只用這台電腦、同一個 Wi-Fi、或線上。房間種類和比賽時間先寫在 RoomOptions。</summary>
        public void StartHost(LinkMode link)
        {
            if (link == LinkMode.Online)
            {
                CreateOnlineRoom();
            }
            else
            {
                StartLocalHost(link);
            }
        }

        /// <summary>開「同一個 Wi-Fi」的房間（測試工具的 host 指令和舊的按鈕用）。</summary>
        public void StartLocalHost()
        {
            StartLocalHost(LinkMode.Lan);
        }

        public void StartLocalHost(LinkMode link)
        {
            m_AutoJoinLocal = false;
            if (!CanStart())
            {
                return;
            }

            var transport = GetTransport();
            if (transport == null)
            {
                return;
            }

            // 同一個 Wi-Fi：監聽 0.0.0.0，其他電腦也能用這台電腦的 IP 加入。只用這台電腦：只聽 127.0.0.1。
            bool lan = link != LinkMode.ThisComputer;
            transport.SetConnectionData("127.0.0.1", GameConfig.Instance.localPort, lan ? "0.0.0.0" : "127.0.0.1");
            transport.MaxConnectAttempts = m_DefaultConnectAttempts;
            Begin(false, "正在建立房間…");
            Link = lan ? LinkMode.Lan : LinkMode.ThisComputer;
            LocalAddress = lan ? LanDiscovery.FindLocalAddress() : "";
            if (m_Net.StartHost())
            {
                WriteLocalHostMarker();
                if (lan)
                {
                    Lan.StartResponder(DescribeRoomForLan);
                }
            }
            else
            {
                Fail("無法建立房間（連接埠可能被占用，或已經有另一個視窗開了房間）。");
            }
        }

        public void StartLocalClient(string address)
        {
            StartLocalClient(address, GameConfig.Instance.localPort);
        }

        public void StartLocalClient(string address, ushort port)
        {
            m_AutoJoinLocal = false;
            address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            ConnectLocalClient(address, "正在連線到 " + address + "…", k_ManualConnectAttempts, port);
        }

        /// <summary>加入「附近的房間」列表裡的一個房間。</summary>
        public void JoinLanRoom(LanRoomInfo room)
        {
            if (room != null)
            {
                StartLocalClient(room.Address, room.Port != 0 ? room.Port : GameConfig.Instance.localPort);
            }
        }

        /// <summary>給「附近的房間」列表的房間資料：名稱、地圖、人數、上限、狀態、是不是測試房間、連接埠。</summary>
        string[] DescribeRoomForLan()
        {
            string map = AnimalEscape.Core.MapRegistry.Instance != null && AnimalEscape.Core.MapRegistry.Instance.Active != null
                ? AnimalEscape.Core.MapRegistry.Instance.Active.displayName
                : "";
            int players = m_Net != null && m_Net.IsServer ? m_Net.ConnectedClientsIds.Count + RoomStatus.BotCount : 0;
            return new[]
            {
                RoomStatus.RoomName,
                map,
                players.ToString(),
                GameConfig.Instance.maxPlayers.ToString(),
                RoomStatus.PhaseText,
                RoomStatus.IsTestRoom ? "1" : "0",
                GameConfig.Instance.localPort.ToString()
            };
        }

        void ConnectLocalClient(string address, string message, int maxAttempts)
        {
            ConnectLocalClient(address, message, maxAttempts, GameConfig.Instance.localPort);
        }

        void ConnectLocalClient(string address, string message, int maxAttempts, ushort port)
        {
            if (!CanStart())
            {
                return;
            }

            var transport = GetTransport();
            if (transport == null)
            {
                return;
            }

            transport.SetConnectionData(address, port);
            transport.MaxConnectAttempts = maxAttempts;
            Begin(false, message);
            Link = address == "127.0.0.1" ? LinkMode.ThisComputer : LinkMode.Lan;
            LocalAddress = "";
            if (!m_Net.StartClient())
            {
                Fail("無法開始連線，請確認位址是否正確。");
            }
        }

        // ------------------------------------------------------------------
        // 線上房間（Relay）
        // ------------------------------------------------------------------

        public async void CreateOnlineRoom()
        {
            m_AutoJoinLocal = false;
            if (!CanStart())
            {
                return;
            }

            RestoreDefaultConnectAttempts();
            int attempt = Begin(true, "正在建立線上房間…");
            Link = LinkMode.Online;
            LocalAddress = "";
            try
            {
                await EnsureSignedInAsync();
                if (attempt != m_Attempt)
                {
                    return;
                }

                var options = Mps.SessionOptionsExtensions.WithRelayNetwork(
                    new Mps.SessionOptions { MaxPlayers = GameConfig.Instance.maxPlayers });
                Mps.ISession session = await Mps.MultiplayerService.Instance.CreateSessionAsync(options);
                if (!AcceptSession(session, attempt))
                {
                    return;
                }

                JoinCode = session.Code;
            }
            catch (Exception e)
            {
                if (attempt == m_Attempt)
                {
                    Fail(DescribeServiceError("建立線上房間失敗", e));
                }
            }
        }

        public async void JoinOnlineRoom(string code)
        {
            m_AutoJoinLocal = false;
            if (!CanStart())
            {
                return;
            }

            code = code == null ? "" : code.Trim();
            if (code.Length == 0)
            {
                StatusMessage = "請先輸入房間代碼。";
                return;
            }

            RestoreDefaultConnectAttempts();
            int attempt = Begin(true, "正在加入線上房間…");
            Link = LinkMode.Online;
            LocalAddress = "";
            try
            {
                await EnsureSignedInAsync();
                if (attempt != m_Attempt)
                {
                    return;
                }

                Mps.ISession session = await Mps.MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                if (!AcceptSession(session, attempt))
                {
                    return;
                }

                JoinCode = session.Code;
            }
            catch (Exception e)
            {
                if (attempt == m_Attempt)
                {
                    Fail(DescribeServiceError("加入線上房間失敗", e));
                }
            }
        }

        // ------------------------------------------------------------------
        // 離開
        // ------------------------------------------------------------------

        /// <summary>清掉狀態訊息（換畫面時用）。</summary>
        public void ClearStatus()
        {
            if (State == ConnectionState.Offline)
            {
                StatusMessage = "";
            }
        }

        public async void Leave()
        {
            m_AutoJoinLocal = false;
            m_LeaveRequested = true;
            m_Attempt++;

            var session = m_Session;
            m_Session = null;

            State = ConnectionState.Offline;
            StatusMessage = "";
            JoinCode = "";

            if (session != null)
            {
                await LeaveSessionQuietly(session);
            }

            if (m_Net != null)
            {
                m_Net.Shutdown();
            }
        }

        // ------------------------------------------------------------------
        // 內部
        // ------------------------------------------------------------------

        bool CanStart()
        {
            if (m_Net == null)
            {
                StatusMessage = "場景裡找不到 NetworkManager。";
                return false;
            }

            if (State != ConnectionState.Offline)
            {
                return false;
            }

            if (m_Net.IsListening || m_Net.ShutdownInProgress)
            {
                StatusMessage = "上一個連線還在關閉中，請稍等一下再試。";
                return false;
            }

            return true;
        }

        UnityTransport GetTransport()
        {
            var transport = m_Net.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport == null)
            {
                StatusMessage = "NetworkManager 沒有設定 UnityTransport。";
            }

            return transport;
        }

        int Begin(bool online, string message)
        {
            m_Attempt++;
            m_LeaveRequested = false;
            IsOnlineRoom = online;
            JoinCode = "";
            StatusMessage = message;
            State = ConnectionState.Connecting;
            return m_Attempt;
        }

        void Fail(string message)
        {
            m_NextAutoJoinTime = Time.unscaledTime + 2f;
            m_Attempt++;
            State = ConnectionState.Offline;
            JoinCode = "";
            StatusMessage = message;

            var session = m_Session;
            m_Session = null;
            if (session != null)
            {
                _ = LeaveSessionQuietly(session);
            }

            if (m_Net != null)
            {
                m_Net.Shutdown();
            }
        }

        void MarkConnected()
        {
            State = ConnectionState.Connected;
            StatusMessage = "";
        }

        bool AcceptSession(Mps.ISession session, int attempt)
        {
            // 等待期間玩家已經按了取消或離開：把剛建立好的房間退掉。
            if (attempt != m_Attempt)
            {
                _ = LeaveSessionQuietly(session);
                return false;
            }

            m_Session = session;
            return true;
        }

        static async Task LeaveSessionQuietly(Mps.ISession session)
        {
            try
            {
                await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Connection] 離開線上房間時發生例外（通常可以忽略）：" + e.Message);
            }
        }

        static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                options.SetProfile(BuildProfileName());
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        /// <summary>
        /// 同一台電腦開多個視窗測試時，每個視窗要用不同的匿名身分，否則會被當成同一個玩家而無法加入。
        /// 編輯器（含 Multiplayer Play Mode 的虛擬玩家）用專案路徑區分；打包版每次啟動用一個新的身分。
        /// </summary>
        static string BuildProfileName()
        {
            int hash = Application.isEditor
                ? Application.dataPath.GetHashCode()
                : Guid.NewGuid().GetHashCode();
            return "p" + (hash & 0x7fffffff);
        }

        /// <summary>
        /// 是否為 Multiplayer Play Mode 的虛擬玩家視窗（它的專案路徑在主專案的 Library/VP/ 底下）。
        /// </summary>
        static bool IsVirtualPlayer()
        {
            return Application.isEditor && Application.dataPath.Replace('\\', '/').Contains("/Library/VP/");
        }

        void RestoreDefaultConnectAttempts()
        {
            var transport = m_Net != null ? m_Net.NetworkConfig.NetworkTransport as UnityTransport : null;
            if (transport != null)
            {
                transport.MaxConnectAttempts = m_DefaultConnectAttempts;
            }
        }

        // ------------------------------------------------------------------
        // 本機房間的記號檔（給 Multiplayer Play Mode 的虛擬玩家自動加入用）
        // ------------------------------------------------------------------

        static string GetMarkerPath()
        {
            // 虛擬玩家的專案在「主專案/Library/VP/代號/」底下，記號檔統一放在主專案的 Temp 資料夾。
            string dataPath = Application.dataPath.Replace('\\', '/');
            int index = dataPath.IndexOf("/Library/VP/", StringComparison.Ordinal);
            string projectRoot = index >= 0 ? dataPath.Substring(0, index) : Path.GetDirectoryName(dataPath);
            return Path.Combine(projectRoot, "Temp", "animal-escape-local-host.txt");
        }

        void WriteLocalHostMarker()
        {
            if (string.IsNullOrEmpty(m_MarkerPath))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_MarkerPath));
                File.WriteAllText(m_MarkerPath, DateTime.UtcNow.Ticks.ToString());
                m_WroteMarker = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Connection] 無法寫入本機房間的記號檔，虛擬玩家視窗不會自動加入：" + e.Message);
            }
        }

        void DeleteLocalHostMarker()
        {
            if (!m_WroteMarker)
            {
                return;
            }

            m_WroteMarker = false;
            try
            {
                if (File.Exists(m_MarkerPath))
                {
                    File.Delete(m_MarkerPath);
                }
            }
            catch (Exception)
            {
            }
        }

        long ReadMarkerTicks()
        {
            if (string.IsNullOrEmpty(m_MarkerPath))
            {
                return 0;
            }

            try
            {
                return File.Exists(m_MarkerPath) ? File.GetLastWriteTimeUtc(m_MarkerPath).Ticks : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        bool IsLocalHostMarkerFresh()
        {
            long ticks = ReadMarkerTicks();
            return ticks != 0 && ticks != m_StaleMarkerTicks;
        }

        static string DescribeServiceError(string prefix, Exception e)
        {
            Debug.LogException(e);

            string typeName = e.GetType().Name;
            string message = e.Message ?? "";
            if (typeName.Contains("NotLinked") || message.Contains("link your Unity project"))
            {
                return prefix + "：這個專案還沒連結 Unity Cloud。請到 Edit > Project Settings > Services 連結後再試（本機房間不需要連結）。";
            }

            return prefix + "：" + message;
        }

        // ------------------------------------------------------------------
        // Netcode 事件
        // ------------------------------------------------------------------

        void OnClientConnected(ulong clientId)
        {
            if (m_Net != null && clientId == m_Net.LocalClientId && State == ConnectionState.Connecting)
            {
                MarkConnected();
            }
        }

        void OnClientStopped(bool wasHost)
        {
            var previous = State;
            State = ConnectionState.Offline;
            JoinCode = "";
            DeleteLocalHostMarker();
            Lan.StopResponder();

            if (m_AutoJoinLocal && previous == ConnectionState.Connecting)
            {
                // 有記號檔卻連不上：多半是主視窗異常關閉留下的舊記號，之後忽略它。
                m_StaleMarkerTicks = ReadMarkerTicks();
            }

            if (m_LeaveRequested)
            {
                StatusMessage = "";
            }
            else if (previous == ConnectionState.Connecting)
            {
                // 自動加入模式下，主視窗還沒開房間是正常情況，不顯示錯誤，稍後再試。
                StatusMessage = m_AutoJoinLocal ? "" : WithReason("連線失敗：找不到房間，或主機沒有回應。");
            }
            else if (previous == ConnectionState.Connected)
            {
                StatusMessage = WithReason("與房間的連線已中斷。");
            }

            m_NextAutoJoinTime = Time.unscaledTime + 1.5f;

            // 線上房間：網路斷了就一併離開 Session，避免卡在名單裡。
            var session = m_Session;
            m_Session = null;
            if (session != null)
            {
                m_Attempt++;
                _ = LeaveSessionQuietly(session);
            }
        }

        void OnTransportFailure()
        {
            StatusMessage = "網路傳輸發生錯誤，連線已關閉。";
        }

        string WithReason(string message)
        {
            string reason = m_Net != null ? m_Net.DisconnectReason : null;
            return string.IsNullOrEmpty(reason) ? message : message + "（" + reason + "）";
        }
    }
}
