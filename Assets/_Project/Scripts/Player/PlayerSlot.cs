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
    public class PlayerSlot : NetworkBehaviour
    {
        /// <summary>房間裡所有玩家的狀態（每台電腦各自維護）。</summary>
        public static readonly List<PlayerSlot> All = new List<PlayerSlot>();

        /// <summary>這台電腦自己的玩家狀態。</summary>
        public static PlayerSlot Local { get; private set; }

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

        /// <summary>顯示用的玩家編號（P1、P2…），也用來決定動物的出生點。</summary>
        public int PlayerNumber
        {
            get { return Seat.Value > 0 ? Seat.Value : (int)OwnerClientId + 1; }
        }

        /// <summary>找出某位連線玩家的玩家狀態；找不到時回傳 null。</summary>
        public static PlayerSlot FindByClient(ulong clientId)
        {
            for (int i = 0; i < All.Count; i++)
            {
                PlayerSlot slot = All[i];
                if (slot != null && slot.IsSpawned && slot.OwnerClientId == clientId)
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

            if (IsOwner)
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
