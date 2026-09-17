using System;
using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Timer
{
    /// <summary>
    /// 포모도로 카운트다운과 세트 전환.
    /// 상태머신을 구독해 구간을 시작·정지하고, 구간이 끝나면 상태머신에 다음 상태를 요청한다.
    /// 경과 시간은 프레임 누적이 아니라 구간 시작 시각과의 차이로 계산한다.
    /// </summary>
    public class PomodoroTimer : MonoBehaviour
    {
        private const int SecondsPerMinute = 60;

        [SerializeField] private PomodoroStateMachine stateMachine;

        [Tooltip("검증용 배속. 1이면 실시간. 빌드에서는 1로 둔다.")]
        [SerializeField, Min(1f)] private float timeScale = 1f;

        /// <summary>현재 구간의 남은 시간(초).</summary>
        public float RemainingSeconds { get; private set; }

        /// <summary>현재 구간 진행률 0.0 ~ 1.0.</summary>
        public float Progress { get; private set; }

        /// <summary>현재 세트 번호. 1부터 시작.</summary>
        public int CurrentSet { get; private set; }

        /// <summary>완료한 집중 세트 수.</summary>
        public int CompletedSets { get; private set; }

        /// <summary>현재 구간 전체 길이(초).</summary>
        public float PhaseDurationSeconds { get; private set; }

        /// <summary>구간 카운트다운이 살아 있는지. 일시정지 중에도 true.</summary>
        public bool IsRunning { get; private set; }

        public bool IsPaused { get; private set; }

        /// <summary>진행 중인 세션의 설정 복사본. 세션 밖에서는 null.</summary>
        public SessionConfig Config { get; private set; }

        /// <summary>남은 시간의 정수 초가 바뀔 때마다 발행. 사실상 1초마다.</summary>
        public event Action OnTick;

        /// <summary>한 구간(집중·휴식)이 끝났을 때 발행. 상태 전이 요청보다 먼저 발행된다.</summary>
        public event Action OnPhaseCompleted;

        // 시계. 기본은 실시간이며, 테스트에서 가짜 시계로 바꿀 수 있다.
        private Func<double> _clock = () => Time.realtimeSinceStartupAsDouble;
        private double _phaseStartTime;
        private double _pausedAt;
        private double _pausedTotal;
        private int _lastTickSecond = -1;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (stateMachine == null) stateMachine = GetComponent<PomodoroStateMachine>();
            if (stateMachine == null)
            {
                Debug.LogError("PomodoroTimer: PomodoroStateMachine 참조가 없습니다.");
            }
        }

        private void OnEnable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged -= HandleStateChanged;
        }

        private void Update()
        {
            if (!IsRunning || IsPaused) return;
            Tick();
        }

        // ---- 공개 API ----

        /// <summary>세션을 시작한다. Idle 상태에서만 동작한다.</summary>
        public void StartSession(SessionConfig config)
        {
            if (config == null)
            {
                Debug.LogWarning("PomodoroTimer: config가 null이라 세션을 시작하지 않습니다.");
                return;
            }

            if (stateMachine.CurrentState != PomodoroState.Idle)
            {
                Debug.LogWarning($"PomodoroTimer: {stateMachine.CurrentState} 상태에서는 세션을 시작할 수 없습니다.");
                return;
            }

            Config = config.Clone();
            CurrentSet = 1;
            CompletedSets = 0;

            // 상태 전이가 성공하면 HandleStateChanged가 집중 구간 카운트다운을 시작한다.
            stateMachine.ChangeState(PomodoroState.Focus);
        }

        public void Pause()
        {
            if (!IsRunning || IsPaused) return;
            IsPaused = true;
            _pausedAt = _clock();
        }

        public void Resume()
        {
            if (!IsRunning || !IsPaused) return;
            _pausedTotal += _clock() - _pausedAt;
            IsPaused = false;
        }

        /// <summary>세션을 취소하고 Idle로 돌아간다. 전이 가능 여부는 상태머신이 판정한다.</summary>
        public void Cancel()
        {
            if (stateMachine.CurrentState == PomodoroState.Idle) return;
            stateMachine.ChangeState(PomodoroState.Idle);
        }

        // ---- 내부 ----

        /// <summary>한 프레임 분량의 갱신. 테스트에서 가짜 시계와 함께 직접 호출한다.</summary>
        internal void Tick()
        {
            double now = _clock();
            // 일시정지 중이면 아직 누적되지 않은 정지 시간도 제외한다.
            double paused = _pausedTotal + (IsPaused ? now - _pausedAt : 0d);
            double elapsed = (now - _phaseStartTime - paused) * timeScale;
            float remaining = Mathf.Max(0f, PhaseDurationSeconds - (float)elapsed);

            RemainingSeconds = remaining;
            Progress = PhaseDurationSeconds > 0f
                ? Mathf.Clamp01((float)elapsed / PhaseDurationSeconds)
                : 1f;

            int second = Mathf.CeilToInt(remaining);
            if (second != _lastTickSecond)
            {
                _lastTickSecond = second;
                OnTick?.Invoke();
            }

            if (remaining <= 0f) CompletePhase();
        }

        /// <summary>테스트용 시계 교체. null이면 실시간으로 되돌린다.</summary>
        internal void SetClock(Func<double> clock)
        {
            _clock = clock ?? (() => Time.realtimeSinceStartupAsDouble);
        }

        private void BeginCountdown(int minutes)
        {
            PhaseDurationSeconds = minutes * SecondsPerMinute;
            RemainingSeconds = PhaseDurationSeconds;
            Progress = 0f;
            _phaseStartTime = _clock();
            _pausedTotal = 0d;
            _lastTickSecond = -1;
            IsPaused = false;
            IsRunning = true;

            // 시작 직후 표시가 바로 갱신되도록 첫 틱을 발행한다.
            Tick();
        }

        private void StopCountdown()
        {
            IsRunning = false;
            IsPaused = false;
            RemainingSeconds = 0f;
            Progress = 0f;
            PhaseDurationSeconds = 0f;
        }

        private void CompletePhase()
        {
            PomodoroState phase = stateMachine.CurrentState;
            StopCountdown();
            OnPhaseCompleted?.Invoke();

            switch (phase)
            {
                case PomodoroState.Focus:
                    // 집중이 끝나면 마지막 세트인지 확인해 장기·단기 휴식으로 나눈다.
                    CompletedSets = CurrentSet;
                    stateMachine.ChangeState(CurrentSet >= Config.TotalSets
                        ? PomodoroState.LongBreak
                        : PomodoroState.ShortBreak);
                    break;

                case PomodoroState.ShortBreak:
                    // 휴식이 끝나면 다음 세트의 집중으로 돌아간다.
                    CurrentSet++;
                    stateMachine.ChangeState(PomodoroState.Focus);
                    break;

                case PomodoroState.LongBreak:
                    // 장기 휴식까지 끝나면 세션 종료.
                    stateMachine.ChangeState(PomodoroState.Idle);
                    break;

                default:
                    Debug.LogWarning($"PomodoroTimer: {phase} 상태에서 구간 완료가 발생했습니다.");
                    break;
            }
        }

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            if (Config == null && to != PomodoroState.Idle && to != PomodoroState.Failed)
            {
                // StartSession을 거치지 않고 상태가 바뀐 경우. 기본 설정으로 진행한다.
                Debug.LogWarning("PomodoroTimer: 세션 설정 없이 상태가 바뀌어 기본 설정을 사용합니다.");
                Config = new SessionConfig();
                CurrentSet = 1;
                CompletedSets = 0;
            }

            switch (to)
            {
                case PomodoroState.Focus:
                    // 유예에서 복귀한 경우는 멈췄던 카운트다운을 잇고, 그 외에는 새 집중 구간이다.
                    if (from == PomodoroState.Interrupted) Resume();
                    else BeginCountdown(Config.FocusMinutes);
                    break;

                case PomodoroState.ShortBreak:
                    BeginCountdown(Config.ShortBreakMinutes);
                    break;

                case PomodoroState.LongBreak:
                    BeginCountdown(Config.LongBreakMinutes);
                    break;

                case PomodoroState.Interrupted:
                    // 유예 중에는 집중 시간이 흐르지 않는다.
                    Pause();
                    break;

                case PomodoroState.Idle:
                case PomodoroState.Failed:
                    StopCountdown();
                    break;
            }
        }
    }
}
