using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace _SAIUN.Scripts.Distraction
{
    /// <summary>
    /// 포그라운드 프로세스를 1초마다 폴링해 방해 앱을 감지한다.
    /// FOCUS에서 방해 앱이 앞에 오면 INTERRUPTED로 보내고, 유예 시간 안에 돌아오면 FOCUS로,
    /// 넘기면 FAILED로 보낸다. 세션 예외('지금은 괜찮아요')는 그 세션에서만 해당 프로세스를 무시한다.
    /// </summary>
    public class ForegroundWatcher : MonoBehaviour
    {
        /// <summary>폴링 주기(초). 사양서 8장 확정값.</summary>
        public const float PollIntervalSeconds = 1f;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [SerializeField] private PomodoroStateMachine stateMachine;

        [Tooltip("유예 시간(초). GameManager가 설정값으로 덮어쓴다.")]
        [SerializeField] private int graceSeconds = SettingsStore.DefaultGraceSeconds;

        public BlacklistStore Blacklist { get; } = new BlacklistStore();

        /// <summary>유예 중인 방해 프로세스 이름. 유예 밖에서는 null.</summary>
        public string DetectedProcess { get; private set; }

        /// <summary>유예 남은 시간(초).</summary>
        public float GraceRemainingSeconds { get; private set; }

        public bool IsWatching => _loop != null;

        public int GraceSeconds
        {
            get => graceSeconds;
            set => graceSeconds = Mathf.Clamp(value, SettingsStore.MinGraceSeconds, SettingsStore.MaxGraceSeconds);
        }

        /// <summary>방해 앱을 감지해 유예를 시작할 때. 인자는 프로세스 이름.</summary>
        public event Action<string> OnDistractionDetected;

        /// <summary>유예 중 매 폴링마다 남은 초.</summary>
        public event Action<float> OnGraceTick;

        /// <summary>유예 안에 돌아와 FOCUS로 복귀할 때.</summary>
        public event Action OnRecovered;

        /// <summary>유예를 넘겨 FAILED로 보내기 직전.</summary>
        public event Action OnGraceExpired;

        // 포그라운드 프로세스 이름 공급자. 빌드에서는 Win32, 에디터·테스트에서는 주입한다.
        internal Func<string> ProcessNameProvider;

        private readonly HashSet<string> _sessionExceptions = new HashSet<string>();
        private Coroutine _loop;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (stateMachine == null) stateMachine = GetComponent<PomodoroStateMachine>();
            if (stateMachine == null) Debug.LogError("ForegroundWatcher: PomodoroStateMachine 참조가 없습니다.");

            Blacklist.Load();
            GraceSeconds = graceSeconds;

#if !UNITY_EDITOR
            ProcessNameProvider ??= GetForegroundProcessName;
#endif
        }

        private void OnEnable()
        {
            if (stateMachine == null) return;
            stateMachine.OnStateChanged += HandleStateChanged;
            if (ShouldWatch(stateMachine.CurrentState)) StartLoop();
        }

        private void OnDisable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged -= HandleStateChanged;
            StopLoop();
        }

        // ---- 공개 API ----

        /// <summary>P4-02 세션 예외. 유예 중인 프로세스를 이 세션에서만 무시하고 FOCUS로 돌아간다.</summary>
        public void ExcuseCurrentProcess()
        {
            if (stateMachine.CurrentState != PomodoroState.Interrupted || string.IsNullOrEmpty(DetectedProcess)) return;
            _sessionExceptions.Add(BlacklistStore.Normalize(DetectedProcess));
            Recover();
        }

        public bool IsExcusedThisSession(string processName)
        {
            return _sessionExceptions.Contains(BlacklistStore.Normalize(processName));
        }

        // ---- 폴링 ----

        /// <summary>한 번의 폴링. 테스트에서 직접 호출한다.</summary>
        internal void Poll()
        {
            string process = ProcessNameProvider?.Invoke();
            bool distracting = !string.IsNullOrEmpty(process)
                               && Blacklist.IsDistracting(process)
                               && !IsExcusedThisSession(process);

            switch (stateMachine.CurrentState)
            {
                case PomodoroState.Focus:
                    if (!distracting) return;
                    DetectedProcess = process;
                    GraceRemainingSeconds = graceSeconds;
                    stateMachine.ChangeState(PomodoroState.Interrupted);
                    OnDistractionDetected?.Invoke(process);
                    OnGraceTick?.Invoke(GraceRemainingSeconds);
                    break;

                case PomodoroState.Interrupted:
                    if (!distracting)
                    {
                        Recover();
                        return;
                    }

                    GraceRemainingSeconds = Mathf.Max(0f, GraceRemainingSeconds - PollIntervalSeconds);
                    OnGraceTick?.Invoke(GraceRemainingSeconds);
                    if (GraceRemainingSeconds > 0f) return;

                    OnGraceExpired?.Invoke();
                    stateMachine.ChangeState(PomodoroState.Failed);
                    break;
            }
        }

        private IEnumerator Loop()
        {
            var wait = new WaitForSecondsRealtime(PollIntervalSeconds);
            while (true)
            {
                yield return wait;
                Poll();
            }
        }

        // ---- 내부 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            if (from == PomodoroState.Idle && to == PomodoroState.Focus) _sessionExceptions.Clear();

            if (ShouldWatch(to)) StartLoop();
            else StopLoop();

            if (to == PomodoroState.Idle || to == PomodoroState.Failed)
            {
                DetectedProcess = null;
                GraceRemainingSeconds = 0f;
            }
        }

        private static bool ShouldWatch(PomodoroState state)
        {
            return state == PomodoroState.Focus || state == PomodoroState.Interrupted;
        }

        private void Recover()
        {
            DetectedProcess = null;
            GraceRemainingSeconds = 0f;
            stateMachine.ChangeState(PomodoroState.Focus);
            OnRecovered?.Invoke();
        }

        private void StartLoop()
        {
            if (_loop != null || !isActiveAndEnabled) return;
            _loop = StartCoroutine(Loop());
        }

        private void StopLoop()
        {
            if (_loop == null) return;
            StopCoroutine(_loop);
            _loop = null;
        }

        // 사양서 v1.1 6-1. 실패하면 null을 돌려 감지를 건너뛴다.
        private static string GetForegroundProcessName()
        {
#if !UNITY_EDITOR
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return null;
                using (Process process = Process.GetProcessById((int)pid))
                {
                    return process.ProcessName;
                }
            }
            catch (Exception)
            {
                return null;
            }
#else
            return null;
#endif
        }
    }
}
