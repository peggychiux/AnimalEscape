using AnimalEscape.Players;
using UnityEngine;

namespace AnimalEscape.Match
{
    /// <summary>
    /// 門禁卡的外觀（場景裡只有一張，三張地圖共用）。不連線：每台電腦照 MatchState 的門禁卡狀態擺：
    /// 掉在地上時浮在離地 0.55 公尺的地方慢慢轉；有人拿著時浮在那隻動物頭上（大家都看得到誰拿著卡）；
    /// 還沒開房或已經用掉時藏起來。用不受光影響的亮色材質，暗處也看得到，但不會發光照亮旁邊（才不會隔著牆洩漏位置）。
    /// </summary>
    public class KeycardView : MonoBehaviour
    {
        [Tooltip("卡片本體（會被打開／關掉、轉動）")]
        public Transform card;

        void LateUpdate()
        {
            if (card == null)
            {
                return;
            }

            MatchState match = MatchState.Instance;
            Vector3 position;
            bool show = TryGetPosition(match, out position);
            if (card.gameObject.activeSelf != show)
            {
                card.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            float t = Time.time;
            card.SetPositionAndRotation(position + Vector3.up * (Mathf.Sin(t * 2.4f) * 0.06f),
                Quaternion.Euler(0f, t * 90f, 12f));
        }

        static bool TryGetPosition(MatchState match, out Vector3 position)
        {
            position = Vector3.zero;
            if (match == null || !match.IsSpawned)
            {
                return false;
            }

            switch (match.Keycard.Value)
            {
                case KeycardStatus.OnGround:
                    position = match.KeycardPosition.Value + Vector3.up * 0.55f;
                    return true;
                case KeycardStatus.Held:
                {
                    PlayerAvatar holder = match.KeycardHolderAvatar;
                    if (holder == null)
                    {
                        return false;
                    }

                    position = holder.transform.position + Vector3.up * (holder.Settings.height + 0.4f);
                    return true;
                }

                default:
                    return false;
            }
        }
    }
}
