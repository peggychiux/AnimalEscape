using System.Collections.Generic;
using AnimalEscape.Core;
using AnimalEscape.Players;
using Unity.Netcode;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 實驗室派對裡的球。物理只在主機算（NetworkRigidbody：主機的剛體是動的，其他電腦是照同步的位置擺）。
    ///
    /// 角色是 CharacterController，不會自己推動剛體，所以「踢」是這樣做的：擁有角色的那台電腦發現自己用夠快的速度撞上球
    /// （PlayerParty 用距離判斷），就送一個 KickRpc 給主機，主機確認距離後給球一個速度。數值在 GameConfig 的「實驗室與門禁卡」。
    /// 由 MatchState 在開房、換地圖時放到實驗室的球位上。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class LabBall : NetworkBehaviour
    {
        public static readonly List<LabBall> Active = new List<LabBall>();

        [Tooltip("球的半徑（公尺），和碰撞一致")]
        public float radius = 0.35f;

        Rigidbody m_Body;

        /// <summary>主機放球時記住的位置；球跑出實驗室（不應該發生）就放回這裡。</summary>
        [System.NonSerialized] public Vector3 HomePosition;

        public override void OnNetworkSpawn()
        {
            m_Body = GetComponent<Rigidbody>();

            // 有彈性的球（只有主機在算物理，所以只在主機設定）。
            var sphere = GetComponent<Collider>();
            if (IsServer && sphere != null)
            {
                sphere.material = new PhysicsMaterial("Party Ball")
                {
                    bounciness = 0.55f,
                    bounceCombine = PhysicsMaterialCombine.Maximum,
                    dynamicFriction = 0.4f,
                    staticFriction = 0.4f
                };
            }

            if (!Active.Contains(this))
            {
                Active.Add(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            Active.Remove(this);
        }

        /// <summary>（擁有角色的那台電腦）我撞到這顆球了，往 direction 踢、撞上去的速度是 speed。</summary>
        public void RequestKick(Vector3 direction, float speed)
        {
            if (IsSpawned)
            {
                KickRpc(direction, speed);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void KickRpc(Vector3 direction, float speed, RpcParams rpcParams = default)
        {
            if (m_Body == null)
            {
                return;
            }

            // 主機自己確認踢球的人真的在球旁邊（放寬一點，容許網路延遲）。
            PlayerAvatar kicker = FindAvatar(rpcParams.Receive.SenderClientId);
            if (kicker == null)
            {
                return;
            }

            Vector3 offset = transform.position - kicker.transform.position;
            offset.y = 0f;
            if (offset.magnitude > radius + kicker.Settings.radius + 1.5f)
            {
                return;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            speed = Mathf.Clamp(speed, 0f, 9f);
            if (m_Body.isKinematic)
            {
                return;
            }

            m_Body.linearVelocity = direction.normalized * speed * config.ballKickStrength + Vector3.up * config.ballKickLift;
            m_Body.angularVelocity = Vector3.Cross(Vector3.up, direction.normalized) * speed * 3f;
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || m_Body == null)
            {
                return;
            }

            // 保險：球掉出地圖、或飛出實驗室，就放回原位。
            LabRoom lab = LabRoom.Current;
            bool lost = transform.position.y < -5f || (lab != null && !lab.Contains(transform.position + Vector3.down * radius)
                && !lab.Contains(transform.position));
            if (lost)
            {
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
                m_Body.position = HomePosition;
                transform.position = HomePosition;
            }
        }

        static PlayerAvatar FindAvatar(ulong clientId)
        {
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar != null && avatar.IsSpawned && avatar.OwnerClientId == clientId)
                {
                    return avatar;
                }
            }

            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
        }
    }
}
