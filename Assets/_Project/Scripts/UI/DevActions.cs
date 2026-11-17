using AnimalEscape.Core;
using AnimalEscape.Match;
using AnimalEscape.Players;
using Unity.Netcode;
using UnityEngine;

namespace AnimalEscape.UI
{
    /// <summary>
    /// 測試模式 F1 開發者面板的每一個按鈕（測試工具 DevTestDriver 的 dev 指令也走這裡，兩邊做的事一樣）。
    /// 「比賽」「其他」只有房主能用（直接改主機上的狀態）；換自己的角色、瞬移每個人都能用。
    /// 每個方法回傳一句給人看的結果。
    /// </summary>
    public static class DevActions
    {
        public enum Spot
        {
            Exit,
            LabDoor,
            NearestCage,
            Spawn
        }

        public static bool IsHost
        {
            get
            {
                NetworkManager net = NetworkManager.Singleton;
                return net != null && net.IsHost;
            }
        }

        /// <summary>F1 面板現在能不能用（測試房間才有）。</summary>
        public static bool Available
        {
            get { return MatchState.IsTestRoom; }
        }

        static string NeedHost(out bool ok)
        {
            ok = Available && IsHost && MatchFlow.Instance != null && MatchState.Instance != null;
            if (!Available)
            {
                return "只有測試房間可以用";
            }

            return ok ? "" : "只有房主能改";
        }

        // ------------------------------------------------------------------
        // 比賽（房主）
        // ------------------------------------------------------------------

        /// <summary>剩餘時間改成幾秒（比賽中）；還沒開始時改的是一局的長度。</summary>
        public static string SetTime(float seconds)
        {
            bool ok;
            string error = NeedHost(out ok);
            if (!ok)
            {
                return error;
            }

            MatchFlow flow = MatchFlow.Instance;
            seconds = Mathf.Clamp(seconds, 0f, 3600f);
            if (flow.IsPlaying)
            {
                flow.ServerSetTimeLeft(seconds);
                return "剩餘時間改成 " + Format(seconds);
            }

            flow.ServerSetMatchSeconds(Mathf.Max(5f, seconds));
            return "還沒開始比賽：一局改成 " + Format(Mathf.Max(5f, seconds));
        }

        public static string TogglePause()
        {
            bool ok;
            string error = NeedHost(out ok);
            if (!ok)
            {
                return error;
            }

            MatchFlow flow = MatchFlow.Instance;
            if (!flow.IsPlaying)
            {
                return "比賽中才能暫停";
            }

            flow.ServerSetPaused(!flow.TimerPaused);
            return flow.TimerPaused ? "計時暫停" : "計時繼續";
        }

        public static string SetChaos(float value)
        {
            bool ok;
            string error = NeedHost(out ok);
            if (!ok)
            {
                return error;
            }

            MatchFlow.Instance.ServerSetChaos(value);
            return "混亂值 " + Mathf.RoundToInt(Mathf.Clamp(value, 0f, 100f)) + "%";
        }

        public static string AddChaos(float amount)
        {
            MatchFlow flow = MatchFlow.Instance;
            return flow != null ? SetChaos(flow.Chaos.Value + amount) : "還沒開房間";
        }

        public static string OpenExit()
        {
            bool ok;
            string error = NeedHost(out ok);
            if (!ok)
            {
                return error;
            }

            MatchFlow flow = MatchFlow.Instance;
            if (flow.ExitOpen.Value)
            {
                return "出口已經開了";
            }

            if (!flow.ExitUnlocked)
            {
                flow.ServerSetChaos(100f);
            }

            flow.ServerOpenExit();
            return "出口打開了（混亂值也設成 100%）";
        }

        public static string TruckTenSeconds()
        {
            bool ok;
            string error = NeedHost(out ok);
            if (!ok)
            {
                return error;
            }

            MatchFlow flow = MatchFlow.Instance;
            if (!flow.ExitOpen.Value)
            {
                return "出口還沒開，沒有貨車倒數";
            }

            flow.ServerSetTruckTime(10f);
            return "貨車 10 秒後開走";
        }

        public static string RestoreBreakables()
        {
            bool ok;
            string error = NeedHost(out ok);
            if (!ok)
            {
                return error;
            }

            MatchFlow.Instance.ServerRestoreBreakables();
            return "壞掉的東西全部恢復了（混亂值不變）";
        }

        // ------------------------------------------------------------------
        // 我的角色（每個人）
        // ------------------------------------------------------------------

        public static string SwapRoleInPlace(Role role)
        {
            PlayerSlot local = PlayerSlot.Local;
            if (!Available || local == null)
            {
                return "只有測試房間可以用";
            }

            if (local.CurrentRole.Value == role)
            {
                return "你已經是" + GameConfig.Instance.GetRole(role).displayName + "了";
            }

            local.RequestRoleInPlace(role);
            return "換成" + GameConfig.Instance.GetRole(role).displayName;
        }

        public static string Request(DevRequest request)
        {
            PlayerSlot local = PlayerSlot.Local;
            if (!Available || local == null)
            {
                return "只有測試房間可以用";
            }

            local.RequestDev(request);
            return "已送出";
        }

        /// <summary>瞬移（自己的角色，在自己的電腦上搬）。出生點走主機（順便把狀態清乾淨）。</summary>
        public static string Teleport(Spot spot)
        {
            PlayerCapture mine = PlayerCapture.Local;
            if (!Available)
            {
                return "只有測試房間可以用";
            }

            if (mine == null || !mine.IsSpawned)
            {
                return "你還沒有角色";
            }

            if (spot == Spot.Spawn)
            {
                return Request(DevRequest.BackToSpawn);
            }

            CaptureState state = mine.State.Value;
            if (state != CaptureState.Free && state != CaptureState.Escaped)
            {
                return "被抓住、關著或在實驗室時不能瞬移（先按「從籠子放出來」）";
            }

            Vector3 position;
            Vector3 facing;
            Vector3 here = mine.transform.position;
            switch (spot)
            {
                case Spot.Exit:
                {
                    ExitDoor door = ExitDoor.Current;
                    if (door == null)
                    {
                        return "這張地圖找不到出口";
                    }

                    position = door.InsidePoint - door.transform.forward * 0.8f;
                    facing = door.transform.forward;
                    break;
                }

                case Spot.LabDoor:
                {
                    LabRoom lab = LabRoom.Current;
                    if (lab == null)
                    {
                        return "這張地圖找不到實驗室";
                    }

                    position = lab.OutsidePoint + lab.doorOutward * 0.6f;
                    facing = -lab.doorOutward;
                    break;
                }

                default:
                {
                    // 最近的籠子，空的或有關動物的都算（CaptureCage.FindNearest 只能二選一）。
                    CaptureCage cage = null;
                    float best = float.MaxValue;
                    for (int i = 0; i < CaptureCage.Active.Count; i++)
                    {
                        CaptureCage candidate = CaptureCage.Active[i];
                        if (candidate != null && candidate.PlanarDistance(here) < best)
                        {
                            cage = candidate;
                            best = candidate.PlanarDistance(here);
                        }
                    }

                    if (cage == null)
                    {
                        return "這張地圖找不到籠子";
                    }

                    Vector3 away = here - cage.transform.position;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.01f)
                    {
                        away = Vector3.right;
                    }

                    away.Normalize();
                    position = cage.transform.position + away * (cage.size * 0.5f + 1.3f);
                    facing = -away;
                    break;
                }
            }

            position.y = Mathf.Max(position.y, 0.05f);
            Quaternion rotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.z).normalized, Vector3.up);
            return mine.OwnerTeleport(position, rotation) ? "瞬移了" : "現在不能瞬移";
        }

        // ------------------------------------------------------------------
        // 其他（房主）
        // ------------------------------------------------------------------

        public static string NextMap()
        {
            bool ok;
            string error = NeedHost(out ok);
            if (!ok)
            {
                return error;
            }

            MapRegistry registry = MapRegistry.Instance;
            if (registry == null || registry.Count < 2)
            {
                return "只有一張地圖";
            }

            int next = registry.Wrap(registry.ActiveIndex + 1);
            MatchFlow flow = MatchFlow.Instance;
            if (flow.IsPlaying)
            {
                // 比賽中不能換地圖：先回到準備中再換（測試房間）。
                flow.ServerResetToLobby();
            }

            MatchState.Instance.SetMap(next);
            return "換到" + registry.maps[next].displayName + "（大家照原本的身分重新出生，比賽回到準備中）";
        }

        public static string RestartRound()
        {
            bool ok;
            string error = NeedHost(out ok);
            if (!ok)
            {
                return error;
            }

            string reason;
            bool started = MatchState.Instance.ServerBeginTest(out reason);
            return started
                ? "從「" + RoomOptions.StartPhaseName(MatchState.Instance.StartPhase.Value) + "」重新開始"
                : "不能開始：" + reason;
        }

        public static string ReturnToSetup()
        {
            bool ok;
            string error = NeedHost(out ok);
            if (!ok)
            {
                return error;
            }

            MatchState.Instance.ServerReturnToTestSetup();
            return "回到測試房間";
        }

        public static string Format(float seconds)
        {
            int total = Mathf.RoundToInt(Mathf.Max(0f, seconds));
            return (total / 60) + " 分 " + (total % 60).ToString("00") + " 秒";
        }
    }
}
