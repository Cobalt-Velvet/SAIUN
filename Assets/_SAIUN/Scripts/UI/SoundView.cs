using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using UnityEngine;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 효과음 5종을 상태 전이에 맞춰 재생한다 (P5-01, 사양서 v1.1 13-1).
    /// 켜기·끄기와 볼륨은 시스템 설정(PlayerPrefs)을 재생할 때마다 읽으므로, 설정을 바꾸면 다음 소리부터 반영된다.
    /// Windows 시스템 알림은 쓰지 않는다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SoundView : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private AudioSource source;

        [Header("효과음")]
        [Tooltip("포모도로 완료(전 세트 집중 종료)")]
        [SerializeField] private AudioClip completeClip;

        [Tooltip("세트 전환(집중 → 단기 휴식)")]
        [SerializeField] private AudioClip transitionClip;

        [Tooltip("수확 완료")]
        [SerializeField] private AudioClip harvestClip;

        [Tooltip("방해 앱 감지 경고")]
        [SerializeField] private AudioClip warningClip;

        [Tooltip("작물 사망")]
        [SerializeField] private AudioClip deathClip;

        /// <summary>마지막으로 재생한 클립. 테스트와 확인용.</summary>
        public AudioClip LastPlayedClip { get; private set; }

        /// <summary>마지막으로 재생한 볼륨(0~1).</summary>
        public float LastPlayedVolume { get; private set; }

        /// <summary>지금까지 재생한 횟수.</summary>
        public int PlayCount { get; private set; }

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // 창 위젯이라 거리감이 필요 없다

            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogError("SoundView: GameManager를 찾지 못했습니다.");
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (gameManager == null) return;
            gameManager.StateMachine.OnStateChanged += HandleStateChanged;
            gameManager.OnHarvested += HandleHarvested;
        }

        private void OnDisable()
        {
            if (gameManager == null) return;
            gameManager.StateMachine.OnStateChanged -= HandleStateChanged;
            gameManager.OnHarvested -= HandleHarvested;
        }

        // ---- 이벤트 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            switch (to)
            {
                case PomodoroState.ShortBreak:
                    Play(transitionClip);
                    break;

                case PomodoroState.LongBreak:
                    Play(completeClip);
                    break;

                case PomodoroState.Interrupted:
                    Play(warningClip);
                    break;

                case PomodoroState.Failed:
                    Play(deathClip);
                    break;
            }
        }

        private void HandleHarvested(string cropType)
        {
            Play(harvestClip);
        }

        private void Play(AudioClip clip)
        {
            if (clip == null || !SettingsStore.SoundEnabled) return;

            float volume = SettingsStore.SoundVolume;
            if (volume <= 0f) return;

            source.PlayOneShot(clip, volume);
            LastPlayedClip = clip;
            LastPlayedVolume = volume;
            PlayCount++;
        }
    }
}
