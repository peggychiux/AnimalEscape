using System.Collections.Generic;
using AnimalEscape.Core;
using Unity.Netcode;
using UnityEngine;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 每位連進房間的玩家各有一個「玩家狀態」物件（畫面上看不到），離開房間才會消失。
    /// 目前記錄玩家選的身分；之後被抓次數、是否已逃出等，也會放在這裡。
    /// 角色（PlayerAvatar）則是另一個物件，換身分時會重新產生。
    /// </summary>
    /// <summary>一隻動物這一局的結果。</summary>
    public enum MatchResult : byte
    {
        None = 0,

        /// <summary>貨車開走時在車上。</summary>
        Escaped = 1,

        /// <summary>沒上車（被關、在實驗室、或還在場上沒趕上）。</summary>
        Caught = 2,

        /// <summary>比賽中按了「離開遊戲」。</summary>
        Left = 3
    }

    public class PlayerSlot : NetworkBehaviour
    {
        /// <summary>房間裡所有玩家的狀態（每台電腦各自維護）。</summary>
        public static readonly List<PlayerSlot> All = new List<PlayerSlot>();

        /// <summary>這台電腦自己的玩家狀態。</summary>
        public static PlayerSlot Local { get; private set; }

        /// <summary>主機在產生前先填好：這是不是電腦玩家。</summary>
        [System.NonSerialized] public bool PendingIsBot;

        /// <summary>這是電腦玩家（主機產生的、沒有人在操作；擁有者是主機）。</summary>
        public readonly NetworkVariable<bool> IsBot = new NetworkVariable<bool>(false);

        /// <summary>玩家目前的身分，由主機決定，所有人都看得到。</summary>
        public readonly NetworkVariable<Role> CurrentRole = new NetworkVariable<Role>(Role.None);

        /// <summary>最近一次選身分被主機拒絕的身分與時間（只在自己電腦上有值，用來顯示提示）。</summary>
        public Role DeniedRole { get; private set; }

        public float DeniedTime { get; private set; } = -100f;

        /// <summary>主機在產生這個物件前先填好的座位號碼，產生時會寫進 Seat 同步給所有人。</summary>
        [System.NonSerialized] public int PendingSeat;

        /// <summary>
        /// 座位號碼（從 1 開始）。玩家進房間時，主機會給他「目前沒人用的最小號碼」；
        /// 有人離開後，號碼會讓給下一位進來的人，所以房間裡的編號不會越跳越大。
        /// </summary>
        public readonly NetworkVariable<int> Seat = new NetworkVariable<int>(0);

        /// <summary>
        /// （動物）這一局被抓進籠子幾次，決定下一次的倒數秒數（120／80），第 3 次直接送進實驗室。
        /// 還沒有「一局」的流程，所以先在每次選身分（重新出生）時歸零。
        /// </summary>
        public readonly NetworkVariable<int> TimesCaged = new NetworkVariable<int>(0);

        /// <summary>
        /// （動物）這一局是不是已經被門禁卡從實驗室救出來過。救出來過的，下次被抓就直接回實驗室（門禁卡整場只能用一次）。
        /// 和 TimesCaged 一樣，每次選身分時歸零。
        /// </summary>
        public readonly NetworkVariable<bool> ReleasedFromLab = new NetworkVariable<bool>(false);

        /// <summary>
        /// 這位玩家在實驗室裡按了「離開遊戲」、但人還留在房間裡（主機才會這樣：主機離開房間會把整個房間關掉，
        /// 所以主機只退出這一局，房間繼續開著）。角色已經收掉，介面顯示「你離開了這一局」。再選身分時清掉。
        /// </summary>
        public readonly NetworkVariable<bool> LeftMatch = new NetworkVariable<bool>(false);

        /// <summary>（動物）上一局的結果：逃出、沒逃出、中途離開。結算畫面用；每一局開始時歸零。</summary>
        public readonly NetworkVariable<MatchResult> Result = new NetworkVariable<MatchResult>(MatchResult.None);

        /// <summary>顯示用的玩家編號（P1、P2…）。</summary>
        public int PlayerNumber
        {
            get { return Seat.Value > 0 ? Seat.Value : (int)OwnerClientId + 1; }
        }

        /// <summary>是不是電腦玩家（主機在產生前就知道，其他電腦等同步）。</summary>
        public bool IsBotSlot
        {
            get { return IsBot.Value || (IsServer && PendingIsBot); }
        }

        /// <summary>
        /// 找出某位連線玩家（真人）的玩家狀態；找不到時回傳 null。電腦玩家不算（它們的擁有者是主機，編號和主機一樣）。
        /// 從角色找玩家狀態請用 PlayerAvatar.Slot。
        /// </summary>
        public static PlayerSlot FindByClient(ulong clientId)
        {
            for (int i = 0; i < All.Count; i++)
            {
                PlayerSlot slot = All[i];
                if (slot != null && slot.IsSpawned && slot.OwnerClientId == clientId && !slot.IsBotSlot)
                {
                    return slot;
                }
            }

            return null;
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this))
            {
                All.Add(this);
            }

            if (IsServer && PendingSeat > 0)
            {
                Seat.Value = PendingSeat;
            }

            if (IsServer && PendingIsBot)
            {
                IsBot.Value = true;
            }

            if (IsOwner && !IsBotSlot)
            {
                Local = this;
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this)
            {
                Local = null;
            }
        }

        /// <summary>
        /// 玩家端呼叫：請主機把我換成這個身分。傳 Role.None 表示收掉目前的角色，回到選身分畫面。
        /// </summary>
        public void RequestRole(Role role)
        {
            if (IsSpawned && IsOwner)
            {
                RequestRoleRpc(role);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestRoleRpc(Role role)
        {
            if (PlayerSpawner.Instance != null)
            {
                PlayerSpawner.Instance.AssignRole(this, role);
            }
        }

        /// <summary>玩家端呼叫：在實驗室裡退出這一局（角色收掉、人留在房間裡）。只有在實驗室裡才有效。</summary>
        public void RequestLeaveMatch()
        {
            if (IsSpawned && IsOwner)
            {
                LeaveMatchRpc();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void LeaveMatchRpc()
        {
            if (PlayerSpawner.Instance != null)
            {
                PlayerSpawner.Instance.LeaveMatch(this);
            }
        }

        /// <summary>
        /// 主機端呼叫：通知這位玩家，他選的身分不能用（例如人類已經有人選了）。
        /// </summary>
        public void NotifyRoleDenied(Role role)
        {
            if (IsSpawned && IsServer)
            {
                RoleDeniedRpc(role);
            }
        }

        [Rpc(SendTo.Owner)]
        void RoleDeniedRpc(Role role)
        {
            DeniedRole = role;
            DeniedTime = Time.unscaledTime;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            Local = null;
        }
    }
}
