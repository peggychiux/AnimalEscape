using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace AnimalEscape.Networking
{
    /// <summary>區域網路上找到的一個房間（主選單「附近的房間」列表用）。</summary>
    public class LanRoomInfo
    {
        public int RoomId;
        public string Address = "";
        public ushort Port;
        public string Name = "";
        public string Map = "";
        public int Players;
        public int MaxPlayers;
        public string Status = "";
        public bool IsTest;
        public float SeenAt;

        public bool IsFull
        {
            get { return MaxPlayers > 0 && Players >= MaxPlayers; }
        }
    }

    /// <summary>
    /// 同一個 Wi-Fi（區域網路）自動找房間：
    /// - 主機（開了「同一個 Wi-Fi」的房間）在固定的 UDP 連接埠等「有房間嗎？」，收到就回一句房間的資料。
    /// - 找房間的人每 2 秒往區域網路廣播一次問句（也問 127.0.0.1，同一台電腦的視窗也找得到），把回答收集成列表。
    /// 全部在主執行緒用 Available 輪詢，不開背景執行緒。找不到時（路由器擋廣播、防火牆）還可以直接輸入主機的 IP。
    /// </summary>
    public class LanDiscovery : IDisposable
    {
        const string k_Query = "AE-Q1";
        const string k_Reply = "AE-R1";
        const float k_QueryInterval = 2f;
        const float k_RoomTimeout = 5f;

        readonly int m_Port;
        UdpClient m_Responder;
        UdpClient m_Finder;
        Func<string[]> m_DescribeRoom;
        int m_RoomId;
        float m_NextQueryAt;
        bool m_Searching;

        /// <summary>目前找到的房間（最近 5 秒內有回答的）。</summary>
        public readonly List<LanRoomInfo> Rooms = new List<LanRoomInfo>();

        /// <summary>找房間時發生的問題（例如連接埠被占用）；沒有就是空字串。</summary>
        public string LastError { get; private set; } = "";

        public bool IsResponding
        {
            get { return m_Responder != null; }
        }

        public bool IsSearching
        {
            get { return m_Searching; }
        }

        public LanDiscovery(int port)
        {
            m_Port = port;
        }

        // ------------------------------------------------------------------
        // 主機：回答「有房間嗎？」
        // ------------------------------------------------------------------

        /// <summary>
        /// 開始回答。describe 回傳房間資料：名稱、地圖、人數、上限、狀態、是不是測試房間（0／1）、遊戲連接埠。
        /// </summary>
        public void StartResponder(Func<string[]> describe)
        {
            StopResponder();
            m_DescribeRoom = describe;
            m_RoomId = UnityEngine.Random.Range(1, int.MaxValue);
            try
            {
                var client = new UdpClient();
                client.ExclusiveAddressUse = false;
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, m_Port));
                m_Responder = client;
            }
            catch (Exception e)
            {
                LastError = "區域網路的房間廣播開不起來（" + e.Message + "）：別人看不到這個房間，但還是可以輸入 IP 加入。";
                Debug.LogWarning("[LanDiscovery] " + LastError);
            }
        }

        public void StopResponder()
        {
            if (m_Responder != null)
            {
                try
                {
                    m_Responder.Close();
                }
                catch (Exception)
                {
                }

                m_Responder = null;
            }
        }

        // ------------------------------------------------------------------
        // 找房間的人
        // ------------------------------------------------------------------

        public void StartSearching()
        {
            if (m_Searching)
            {
                return;
            }

            m_Searching = true;
            Rooms.Clear();
            m_NextQueryAt = 0f;
            try
            {
                m_Finder = new UdpClient(0);
                m_Finder.EnableBroadcast = true;
            }
            catch (Exception e)
            {
                LastError = "沒辦法找附近的房間（" + e.Message + "）";
                Debug.LogWarning("[LanDiscovery] " + LastError);
                m_Finder = null;
            }
        }

        public void StopSearching()
        {
            m_Searching = false;
            if (m_Finder != null)
            {
                try
                {
                    m_Finder.Close();
                }
                catch (Exception)
                {
                }

                m_Finder = null;
            }
        }

        /// <summary>馬上再問一次（「重新尋找」按鈕）。</summary>
        public void Refresh()
        {
            Rooms.Clear();
            m_NextQueryAt = 0f;
        }

        /// <summary>每一格呼叫（ConnectionManager.Update）。</summary>
        public void Tick()
        {
            PollResponder();
            if (!m_Searching || m_Finder == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now >= m_NextQueryAt)
            {
                m_NextQueryAt = now + k_QueryInterval;
                SendQueries();
            }

            PollFinder(now);
            Rooms.RemoveAll(room => now - room.SeenAt > k_RoomTimeout);
        }

        void PollResponder()
        {
            if (m_Responder == null)
            {
                return;
            }

            try
            {
                while (m_Responder.Available > 0)
                {
                    IPEndPoint sender = null;
                    byte[] data = m_Responder.Receive(ref sender);
                    if (Encoding.UTF8.GetString(data) != k_Query || m_DescribeRoom == null)
                    {
                        continue;
                    }

                    string[] fields = m_DescribeRoom();
                    var builder = new StringBuilder(k_Reply);
                    builder.Append('\n').Append(m_RoomId);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        builder.Append('\n').Append((fields[i] ?? "").Replace('\n', ' '));
                    }

                    byte[] reply = Encoding.UTF8.GetBytes(builder.ToString());
                    m_Responder.Send(reply, reply.Length, sender);
                }
            }
            catch (Exception e)
            {
                // 對方已經關掉之類的錯誤：這一次不回，不影響房間。
                Debug.Log("[LanDiscovery] 回答找房間時出錯（可以忽略）：" + e.Message);
            }
        }

        void SendQueries()
        {
            byte[] query = Encoding.UTF8.GetBytes(k_Query);
            var targets = new List<IPAddress> { IPAddress.Loopback, IPAddress.Broadcast };
            targets.AddRange(GetDirectedBroadcasts());
            for (int i = 0; i < targets.Count; i++)
            {
                try
                {
                    m_Finder.Send(query, query.Length, new IPEndPoint(targets[i], m_Port));
                }
                catch (Exception)
                {
                    // 某些網路介面不能廣播：略過，其他的照送。
                }
            }
        }

        void PollFinder(float now)
        {
            try
            {
                while (m_Finder.Available > 0)
                {
                    IPEndPoint sender = null;
                    byte[] data = m_Finder.Receive(ref sender);
                    LanRoomInfo info = Parse(Encoding.UTF8.GetString(data), sender);
                    if (info == null)
                    {
                        continue;
                    }

                    info.SeenAt = now;
                    int index = Rooms.FindIndex(room => room.RoomId == info.RoomId);
                    if (index < 0)
                    {
                        Rooms.Add(info);
                    }
                    else
                    {
                        // 同一個房間從 127.0.0.1 和區域網路位址都回答時，保留先記下的位址，只更新內容。
                        info.Address = Rooms[index].Address;
                        Rooms[index] = info;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[LanDiscovery] 收房間資料時出錯（可以忽略）：" + e.Message);
            }
        }

        static LanRoomInfo Parse(string text, IPEndPoint sender)
        {
            string[] lines = text.Split('\n');
            if (lines.Length < 9 || lines[0] != k_Reply)
            {
                return null;
            }

            var info = new LanRoomInfo { Address = sender.Address.ToString() };
            int.TryParse(lines[1], out info.RoomId);
            info.Name = lines[2];
            info.Map = lines[3];
            int.TryParse(lines[4], out info.Players);
            int.TryParse(lines[5], out info.MaxPlayers);
            info.Status = lines[6];
            info.IsTest = lines[7] == "1";
            ushort port;
            info.Port = ushort.TryParse(lines[8], out port) ? port : (ushort)0;
            return info;
        }

        /// <summary>每張網路卡的區域廣播位址（例如 192.168.1.255）。</summary>
        static List<IPAddress> GetDirectedBroadcasts()
        {
            var result = new List<IPAddress>();
            try
            {
                foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (item.OperationalStatus != OperationalStatus.Up || item.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    foreach (UnicastIPAddressInformation unicast in item.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        byte[] address = unicast.Address.GetAddressBytes();
                        byte[] mask = null;
                        try
                        {
                            mask = unicast.IPv4Mask != null ? unicast.IPv4Mask.GetAddressBytes() : null;
                        }
                        catch (Exception)
                        {
                        }

                        if (mask == null || mask.Length != 4)
                        {
                            mask = new byte[] { 255, 255, 255, 0 };
                        }

                        var broadcast = new byte[4];
                        for (int i = 0; i < 4; i++)
                        {
                            broadcast[i] = (byte)(address[i] | ~mask[i]);
                        }

                        result.Add(new IPAddress(broadcast));
                    }
                }
            }
            catch (Exception)
            {
            }

            return result;
        }

        /// <summary>這台電腦在區域網路上的 IPv4 位址（給別人輸入用）；找不到就是空字串。</summary>
        public static string FindLocalAddress()
        {
            string fallback = "";
            try
            {
                foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (item.OperationalStatus != OperationalStatus.Up || item.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    foreach (UnicastIPAddressInformation unicast in item.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        byte[] bytes = unicast.Address.GetAddressBytes();
                        bool linkLocal = bytes[0] == 169 && bytes[1] == 254;
                        bool privateRange = bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                                            || (bytes[0] == 192 && bytes[1] == 168);
                        if (privateRange)
                        {
                            return unicast.Address.ToString();
                        }

                        if (!linkLocal && fallback.Length == 0)
                        {
                            fallback = unicast.Address.ToString();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return fallback;
        }

        public void Dispose()
        {
            StopResponder();
            StopSearching();
        }
    }
}
