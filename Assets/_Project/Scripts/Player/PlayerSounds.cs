using AnimalEscape.Core;
using AnimalEscape.Match;
using UnityEngine;

namespace AnimalEscape.Players
{
    /// <summary>
    /// 角色發出的聲音：人類的腳步聲（動物聽得到）、動物項圈的鈴鐺（人類聽得到）、動物被抓時的叫聲（所有人聽得到）。
    ///
    /// 不連線：每台電腦各自用「角色在這台電腦上的位置變化」算出牠走多快，再決定要不要發出聲音、這台電腦的玩家聽不聽得到。
    /// 跑得越快聲音傳得越遠；按住 Shift 慢走只有很近才聽得到；站著不動完全沒有聲音。範圍和間隔在 GameConfig 的「聲音」。
    /// 聽到的聲音會播出來（3D，聽得出方向），同時記到 SoundCues，畫面邊緣會出現提示。
    /// 由 PlayerAvatar 在生成時加上，不在預製物裡。
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public class PlayerSounds : MonoBehaviour
    {
        PlayerAvatar m_Avatar;
        AudioSource m_Source;
        Vector3 m_LastPosition;
        bool m_HasLastPosition;
        float m_Speed;
        float m_StepTimer;
        CaptureState m_LastState;

        /// <summary>這台電腦上量到的水平速度（平滑過的，公尺／秒）。</summary>
        public float MeasuredSpeed
        {
            get { return m_Speed; }
        }

        void Awake()
        {
            m_Avatar = GetComponent<PlayerAvatar>();
            m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 1f;
            m_Source.dopplerLevel = 0f;

            // 遠近由我們自己算（聽得到的範圍外完全沒有聲音），Unity 只負責左右聲道（方向）。
            m_Source.rolloffMode = AudioRolloffMode.Custom;
            m_Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            m_Source.maxDistance = 500f;
        }

        void OnEnable()
        {
            m_HasLastPosition = false;
        }

        void Update()
        {
            if (m_Avatar == null || !m_Avatar.IsSpawned)
            {
                return;
            }

            GameConfig config = GameConfig.Instance;
            PlayerCapture capture = m_Avatar.Capture;
            CaptureState state = capture != null ? capture.State.Value : CaptureState.Free;
            if (state != m_LastState)
            {
                // 被網住、被關進籠子的那一下會叫。
                if (m_HasLastPosition && (state == CaptureState.Netted || state == CaptureState.Caged))
                {
                    EmitCry(capture, state, config);
                }

                m_LastState = state;
            }

            UpdateMovement(state, config);
        }

        void UpdateMovement(CaptureState state, GameConfig config)
        {
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 position = transform.position;
            if (!m_HasLastPosition)
            {
                m_LastPosition = position;
                m_HasLastPosition = true;
                return;
            }

            Vector3 delta = position - m_LastPosition;
            delta.y = 0f;
            m_LastPosition = position;
            RoleSettings settings = m_Avatar.Settings;
            float instant = delta.magnitude / deltaTime;

            // 一下子移動太遠是瞬移（被放下、被救出、測試工具搬過去），不算走路。
            if (instant > settings.moveSpeed * 3f + 5f)
            {
                instant = 0f;
            }

            m_Speed = Mathf.Lerp(m_Speed, instant, 1f - Mathf.Exp(-deltaTime * 10f));

            // 只有自己能走動的時候才有腳步聲／鈴鐺聲：被扛著、被網住、出局都沒有。籠子裡走動照樣會響。
            bool canMove = state == CaptureState.Free || state == CaptureState.Caged;
            if (!canMove || m_Speed < config.silentBelowSpeed)
            {
                // 一開始走動，第一聲很快就會出來。
                m_StepTimer = Mathf.Min(m_StepTimer, 0.08f);
                return;
            }

            bool human = m_Avatar.AvatarRole.Value == Role.Human;
            bool running = m_Speed > settings.moveSpeed * config.runSpeedFraction;
            m_StepTimer -= Time.deltaTime;
            if (m_StepTimer > 0f)
            {
                return;
            }

            if (human)
            {
                m_StepTimer = running ? config.footstepIntervalRun : config.footstepIntervalSneak;
                Emit(SoundKind.Footstep, SoundBank.Footstep, running ? config.humanFootstepRangeRun : config.humanFootstepRangeSneak,
                    transform.position, config);
            }
            else
            {
                m_StepTimer = running ? config.bellIntervalRun : config.bellIntervalSneak;
                Emit(SoundKind.Bell, SoundBank.Bell, running ? config.bellRangeRun : config.bellRangeSneak,
                    transform.position, config);
            }
        }

        void EmitCry(PlayerCapture capture, CaptureState state, GameConfig config)
        {
            Vector3 position = transform.position;
            if (state == CaptureState.Caged && capture != null)
            {
                // 別台電腦上，關進籠子的動物要等位置同步過來才會到籠子裡；直接用籠子的位置。
                CaptureCage cage = CaptureCage.Find(capture.CageNumber.Value);
                if (cage != null)
                {
                    position = cage.InsidePoint;
                }
            }

            Emit(SoundKind.Cry, SoundBank.Cry, config.cryRange, position, config);
        }

        /// <summary>
        /// 這台電腦的玩家聽不聽得到這個聲音：腳步聲只有動物聽得到、鈴鐺只有人類聽得到、叫聲大家都聽得到；
        /// 自己發出的聲音、出局的玩家、範圍外都聽不到。聽得到就播出來，並記到 SoundCues。
        /// </summary>
        void Emit(SoundKind kind, AudioClip clip, float range, Vector3 position, GameConfig config)
        {
            PlayerAvatar listener = LocalListener();
            if (listener == null || listener == m_Avatar || range <= 0f)
            {
                return;
            }

            bool listenerIsHuman = listener.AvatarRole.Value == Role.Human;
            if ((kind == SoundKind.Footstep && listenerIsHuman) || (kind == SoundKind.Bell && !listenerIsHuman))
            {
                return;
            }

            float distance = Vector3.Distance(listener.transform.position, position);
            if (distance > range)
            {
                return;
            }

            float loudness = 1f - distance / range;
            if (kind == SoundKind.Cry)
            {
                // 叫聲全圖都聽得到，太遠也不要小到聽不見。
                loudness = Mathf.Max(0.35f, loudness);
            }

            SoundCues.Add(kind, GetInstanceID(), position, loudness, distance);
            if (clip != null && m_Source != null)
            {
                m_Source.pitch = Random.Range(0.92f, 1.08f);
                m_Source.PlayOneShot(clip, config.soundVolume * Mathf.Lerp(0.15f, 1f, loudness));
            }
        }

        /// <summary>這台電腦的玩家自己的角色（在實驗室裡就是 null：實驗室有牆有屋頂，裡面的人聽不到外面）。</summary>
        public static PlayerAvatar LocalListener()
        {
            for (int i = 0; i < PlayerAvatar.All.Count; i++)
            {
                PlayerAvatar avatar = PlayerAvatar.All[i];
                if (avatar != null && avatar.IsSpawned && avatar.IsMine)
                {
                    return avatar.Capture != null && avatar.Capture.IsInLab ? null : avatar;
                }
            }

            return null;
        }
    }
}
