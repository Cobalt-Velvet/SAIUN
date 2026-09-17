using System;
using _SAIUN.Scripts.Crop;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using _SAIUN.Scripts.Timer;
using UnityEngine;

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// 시스템 간 배선.
    /// 상태머신·타이머·DB를 들고 이벤트를 연결한다. 각 시스템은 GameManager를 모른다.
    /// UI는 상태를 직접 바꾸지 않고 이 클래스의 Request* 메서드로 요청한다.
    /// UI 뷰의 OnEnable보다 먼저 준비되도록 실행 순서를 앞당긴다(WindowController보다는 뒤).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class GameManager : MonoBehaviour
    {
        // ---- 프레임레이트 (사양서 8장) ----
        private const int FocusFrameRate = 144;
        private const int ActiveFrameRate = 60;
        private const int AwayFrameRate = 30;

        [SerializeField] private PomodoroStateMachine stateMachine;
        [SerializeField] private PomodoroTimer timer;
        [SerializeField] private SaiunDatabase database;

        [Tooltip("창 위치 저장·복원에 쓴다. 없어도 동작한다.")]
        [SerializeField] private WindowController windowController;

        [Tooltip("방해 앱 감시. 없어도 동작한다.")]
        [SerializeField] private ForegroundWatcher watcher;

        public PomodoroStateMachine StateMachine => stateMachine;
        public PomodoroTimer Timer => timer;
        public SaiunDatabase Database => database;
        public ForegroundWatcher Watcher => watcher;

        /// <summary>다음 세션에 쓸 설정. 시작 시 PlayerPrefs에 저장된다. Awake 전에 읽혀도 동작한다.</summary>
        public SessionConfig CurrentConfig
        {
            get => _currentConfig ??= SettingsStore.LoadSessionConfig();
            private set => _currentConfig = value;
        }

        /// <summary>진행 중(또는 마지막) 세션의 작물 등급. 세션 시작 시 1회 판정한다.</summary>
        public CropGrade CurrentGrade { get; private set; } = CropGrade.Normal;

        /// <summary>세션 기록이 DB에 저장된 직후 발행.</summary>
        public event Action<SessionRecord> OnSessionRecorded;

        private SessionConfig _currentConfig;
        private string _sessionStartTime;
        private bool _hasFocus = true;
        private int _appliedFrameRate = -1;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (stateMachine == null) stateMachine = GetComponent<PomodoroStateMachine>();
            if (timer == null) timer = GetComponent<PomodoroTimer>();
            if (database == null) database = GetComponent<SaiunDatabase>();
            if (watcher == null) watcher = GetComponent<ForegroundWatcher>();

            if (stateMachine == null || timer == null || database == null)
            {
                Debug.LogError("GameManager: 상태머신·타이머·DB 참조가 모두 필요합니다.");
            }

            // targetFrameRate는 VSync가 꺼져 있어야 적용된다.
            QualitySettings.vSyncCount = 0;
            CurrentConfig = SettingsStore.LoadSessionConfig();
            if (watcher != null) watcher.GraceSeconds = SettingsStore.GraceSeconds;
        }

        private void OnEnable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged += HandleStateChanged;
            if (windowController != null) windowController.OnMoved += HandleWindowMoved;
        }

        private void Start()
        {
            ApplyFrameRate();

            // 투명 창 설정이 끝난 뒤에 위치를 복원해야 한다.
            if (windowController == null) return;
            if (windowController.IsReady) RestoreWindow();
            else windowController.OnReady += RestoreWindow;
        }

        private void OnDisable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged -= HandleStateChanged;
            if (windowController != null)
            {
                windowController.OnMoved -= HandleWindowMoved;
                windowController.OnReady -= RestoreWindow;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            ApplyFrameRate();
        }

        // ---- UI 요청 진입점 ----

        /// <summary>주어진 설정으로 세션을 시작한다. 설정은 복사해 보관하고 PlayerPrefs에 저장한다.</summary>
        public void RequestStart(SessionConfig config)
        {
            if (config == null)
            {
                Debug.LogWarning("GameManager: config가 null이라 시작 요청을 무시합니다.");
                return;
            }

            CurrentConfig = config.Clone();
            SettingsStore.SaveSessionConfig(CurrentConfig);
            timer.StartSession(CurrentConfig);
        }

        /// <summary>현재 보관 중인 설정으로 세션을 시작한다.</summary>
        public void RequestStart()
        {
            RequestStart(CurrentConfig);
        }

        public void RequestPause()
        {
            timer.Pause();
        }

        public void RequestResume()
        {
            timer.Resume();
        }

        /// <summary>진행 중인 세션을 취소한다.</summary>
        public void RequestCancel()
        {
            timer.Cancel();
        }

        /// <summary>Failed 상태를 확인하고 Idle로 돌아간다.</summary>
        public void RequestAcknowledgeFailure()
        {
            if (stateMachine.CurrentState != PomodoroState.Failed) return;
            stateMachine.ChangeState(PomodoroState.Idle);
        }

        /// <summary>유예 중 '지금은 괜찮아요'. 이 세션에서 해당 앱을 무시하고 집중으로 돌아간다.</summary>
        public void RequestExcuseDistraction()
        {
            if (watcher == null) return;
            watcher.ExcuseCurrentProcess();
        }

        /// <summary>다음 세션의 태스크 텍스트를 바꾼다. 길이 제한은 SessionConfig가 적용한다.</summary>
        public void SetTaskText(string text)
        {
            CurrentConfig.TaskText = text;
        }

        // ---- 내부 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            ApplyFrameRate();

            if (from == PomodoroState.Idle && to == PomodoroState.Focus)
            {
                _sessionStartTime = SaiunDatabase.Now();
                // 등급은 시작 시점 설정으로 1회만 정하고 진행 중 변경은 반영하지 않는다.
                CurrentGrade = CropGradeRule.GetCropGrade(timer.Config ?? CurrentConfig);
                return;
            }

            switch (to)
            {
                case PomodoroState.Failed:
                    RecordSession(SessionRecord.ResultFailed);
                    break;

                case PomodoroState.Idle:
                    // Failed→Idle은 이미 기록됐다.
                    // LongBreak→Idle은 전 세트 완료(수확), 그 외 Idle 복귀는 취소이며 FAILED로 남긴다.
                    if (from == PomodoroState.Failed) break;
                    RecordSession(from == PomodoroState.LongBreak
                        ? SessionRecord.ResultHarvested
                        : SessionRecord.ResultFailed);
                    break;
            }
        }

        private void RecordSession(string result)
        {
            SessionConfig config = timer.Config ?? CurrentConfig;
            var record = new SessionRecord
            {
                StartTime = _sessionStartTime ?? SaiunDatabase.Now(),
                DurationMin = config.FocusMinutes,
                SetsCompleted = timer.CompletedSets,
                CropType = config.CropType,
                Result = result,
            };

            database.InsertSession(record);
            _sessionStartTime = null;
            OnSessionRecorded?.Invoke(record);
        }

        private void RestoreWindow()
        {
            if (windowController == null) return;
            windowController.OnReady -= RestoreWindow;

            windowController.SetAlwaysOnTop(SettingsStore.AlwaysOnTop);

            if (SettingsStore.HasWindowPosition)
            {
                Vector2Int saved = SettingsStore.LoadWindowPosition();
                windowController.MoveTo(saved.x, saved.y);
            }
            else
            {
                windowController.MoveToDefaultPosition();
            }
        }

        private void HandleWindowMoved(Vector2Int position)
        {
            SettingsStore.SaveWindowPosition(position.x, position.y);
        }

        private void ApplyFrameRate()
        {
            int target;
            if (!_hasFocus) target = AwayFrameRate;
            else if (stateMachine != null && stateMachine.CurrentState == PomodoroState.Focus) target = FocusFrameRate;
            else target = ActiveFrameRate;

            // 상태가 바뀔 때 한 번만 바꾼다. 매 프레임 설정하지 않는다.
            if (target == _appliedFrameRate) return;
            _appliedFrameRate = target;
            Application.targetFrameRate = target;
        }
    }
}
