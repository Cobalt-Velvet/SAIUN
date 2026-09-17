using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using UnityEngine;

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
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);

        // 패키지 앱(메모장 등)도 조회되도록 제한 정보 권한만 요청한다.
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int MaxImagePathLength = 1024;

        [SerializeField] private PomodoroStateMachine stateMachine;

        [Tooltip("유예 시간(초). GameManager가 설정값으로 덮어쓴다.")]
        [SerializeField] private int graceSeconds = SettingsStore.DefaultGraceSeconds;

        public BlacklistStore Blacklist { get; } = new BlacklistStore();

        /// <summary>유예 중인 방해 프로세스 이름. 유예 밖에서는 null.</summary>
        public string DetectedProcess { get; private set; }

        /// <summary>유예 남은 시간(초).</summary>
        public float GraceRemainingSeconds { get; private set; }

        /// <summary>마지막 폴링에서 본 전면 프로세스 이름. 진단용.</summary>
        public string LastForegroundProcess { get; private set; }

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
        private static bool s_lookupFailureLogged;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (stateMachine == null) stateMachine = GetComponent<PomodoroStateMachine>();
            if (stateMachine == null) Debug.LogError("ForegroundWatcher: PomodoroStateMachine 참조가 없습니다.");

            Blacklist.Load();
            GraceSeconds = graceSeconds;
            Debug.Log($"ForegroundWatcher: 블랙리스트 {Blacklist.Blacklist.Count}개 / 화이트리스트 {Blacklist.Whitelist.Count}개 ({BlacklistStore.FilePath})");

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
            if (process != LastForegroundProcess)
            {
                // 전면 앱이 바뀔 때만 남긴다. 블랙리스트 진단용.
                Debug.Log($"ForegroundWatcher: 전면 = {process ?? "(없음)"}");
            }
            LastForegroundProcess = process;
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
            Debug.Log("ForegroundWatcher: 감시 시작");
        }

        private void StopLoop()
        {
            if (_loop == null) return;
            StopCoroutine(_loop);
            _loop = null;
            Debug.Log("ForegroundWatcher: 감시 중지");
        }

        // 사양서 v1.1 6-1의 GetForegroundWindow 방식. 프로세스 이름은 System.Diagnostics.Process 대신
        // QueryFullProcessImageName으로 얻는다(Mono의 Process는 패키지 앱에서 실패한다). 실패하면 null.
        private static string GetForegroundProcessName()
        {
#if !UNITY_EDITOR
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
            {
                LogLookupFailureOnce($"OpenProcess 실패 pid={pid} err={Marshal.GetLastWin32Error()}");
                return null;
            }

            try
            {
                var buffer = new StringBuilder(MaxImagePathLength);
                uint size = (uint)buffer.Capacity;
                if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
                {
                    LogLookupFailureOnce($"QueryFullProcessImageName 실패 pid={pid} err={Marshal.GetLastWin32Error()}");
                    return null;
                }
                return Path.GetFileName(buffer.ToString(0, (int)size));
            }
            finally
            {
                CloseHandle(handle);
            }
#else
            return null;
#endif
        }

        private static void LogLookupFailureOnce(string detail)
        {
            if (s_lookupFailureLogged) return;
            s_lookupFailureLogged = true;
            Debug.LogWarning($"ForegroundWatcher: 전면 프로세스 조회 실패 ({detail}). 이후 같은 경고는 생략합니다.");
        }
    }
}
