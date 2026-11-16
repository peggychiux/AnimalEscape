using AnimalEscape.Core;
using AnimalEscape.Networking;
using UnityEngine;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 原型用的大廳：建立或加入房間。用最簡單的 IMGUI 畫，完全不需要在編輯器裡拉介面。
    /// 進房間之後的介面在 GameHud。正式介面之後會改用 Unity UI 或 UI Toolkit。
    /// 注意：IMGUI 在執行時要收得到滑鼠鍵盤，Player Settings 的 Active Input Handling 必須是 Both。
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static readonly bool s_GuiInputDisabled = true;
#else
        static readonly bool s_GuiInputDisabled = false;
#endif

        string m_Address = "127.0.0.1";
        string m_Code = "";

        void OnGUI()
        {
            var connection = ConnectionManager.Instance;
            if (connection == null || connection.State == ConnectionState.Connected)
            {
                return;
            }

            float width;
            float height;
            UiKit.Begin(out width, out height);
            InputGate.PointerOverUi = true;

            if (connection.State == ConnectionState.Offline)
            {
                DrawLobby(connection, width, height);
            }
            else
            {
                DrawConnecting(connection, width, height);
            }
        }

        void DrawLobby(ConnectionManager connection, float width, float height)
        {
            // 面板靠左放，右邊才看得到正在預覽的地圖。
            var panel = new Rect(24f, (height - 624f) * 0.5f, 460f, 624f);
            UiKit.Panel(panel);
            GUILayout.BeginArea(new Rect(panel.x + 22f, panel.y + 16f, panel.width - 44f, panel.height - 32f));

            GUILayout.Label("動物逃脫　原型 M1", UiKit.Title);
            GUILayout.Space(4f);

            if (s_GuiInputDisabled)
            {
                GUILayout.Label("按鈕沒反應？請把 Edit > Project Settings > Player > Active Input Handling 改成 Both。", UiKit.Warning);
            }

            if (!string.IsNullOrEmpty(connection.StatusMessage))
            {
                GUILayout.Label(connection.StatusMessage, UiKit.Warning);
            }

            if (connection.IsWaitingForLocalHost)
            {
                GUILayout.Label("這是測試用的第二個玩家視窗：主視窗建立本機房間後，這裡會自動加入。", UiKit.Warning);
            }

            GUILayout.Space(8f);
            DrawMapSelector();
            GUILayout.Label("本機房間（同一台電腦，或同一個區域網路）", UiKit.Header);
            if (GUILayout.Button("建立本機房間", UiKit.Button, GUILayout.Height(36f)))
            {
                connection.StartLocalHost();
            }

            GUILayout.BeginHorizontal();
            m_Address = GUILayout.TextField(m_Address, 40, UiKit.Field, GUILayout.Height(36f), GUILayout.Width(190f));
            if (GUILayout.Button("加入本機房間", UiKit.Button, GUILayout.Height(36f)))
            {
                connection.StartLocalClient(m_Address);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("同一台電腦測試時，位址維持 127.0.0.1 就可以。要先有人建立房間才加入得了。", UiKit.Hint);

            GUILayout.Space(14f);
            GUILayout.Label("線上房間（不同電腦，用代碼加入）", UiKit.Header);
            if (GUILayout.Button("建立線上房間", UiKit.Button, GUILayout.Height(36f)))
            {
                connection.CreateOnlineRoom();
            }

            GUILayout.BeginHorizontal();
            m_Code = GUILayout.TextField(m_Code, 16, UiKit.Field, GUILayout.Height(36f), GUILayout.Width(190f));
            if (GUILayout.Button("輸入代碼加入", UiKit.Button, GUILayout.Height(36f)))
            {
                connection.JoinOnlineRoom(m_Code);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("線上房間要先在 Project Settings > Services 連結 Unity Cloud 專案。", UiKit.Hint);

            GUILayout.EndArea();
        }

        /// <summary>選地圖：開房的人決定用哪一張，選了右邊馬上預覽。加入別人房間的人不用選。</summary>
        static void DrawMapSelector()
        {
            MapRegistry registry = MapRegistry.Instance;
            if (registry == null || registry.Count < 2)
            {
                return;
            }

            GUILayout.Label("地圖（開房的人決定）", UiKit.Header);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < registry.Count; i++)
            {
                bool selected = registry.ActiveIndex == i;
                GUI.enabled = !selected;
                string label = (selected ? "● " : "") + registry.maps[i].displayName;
                if (GUILayout.Button(label, UiKit.Button, GUILayout.Height(32f)))
                {
                    registry.Select(i);
                }

                GUI.enabled = true;
            }

            GUILayout.EndHorizontal();
            if (registry.Active != null)
            {
                GUILayout.Label(registry.Active.summary, UiKit.Hint);
            }

            GUILayout.Space(10f);
        }

        void DrawConnecting(ConnectionManager connection, float width, float height)
        {
            var panel = new Rect((width - 380f) * 0.5f, (height - 150f) * 0.5f, 380f, 150f);
            UiKit.Panel(panel);
            GUILayout.BeginArea(new Rect(panel.x + 22f, panel.y + 18f, panel.width - 44f, panel.height - 36f));

            string message = string.IsNullOrEmpty(connection.StatusMessage) ? "連線中…" : connection.StatusMessage;
            GUILayout.Label(message, UiKit.Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("取消", UiKit.Button, GUILayout.Height(34f)))
            {
                connection.Leave();
            }

            GUILayout.EndArea();
        }
    }
}
