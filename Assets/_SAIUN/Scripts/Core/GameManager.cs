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
        // ---- 프레임레이트 ----
        // 하늘은 천천히 움직이므로 60이면 충분하다. 다른 창을 보고 있을 때는 절반으로 낮춰 전력을 아낀다.
        private const int ActiveFrameRate = 60;
        private const int AwayFrameRate = 30;

        [SerializeField] private PomodoroStateMachine stateMachine;
        [SerializeField] private PomodoroTimer timer;
        [SerializeField] private SaiunDatabase database;

        [Tooltip("창 위치 저장·복원에 쓴다. 없어도 동작한다.")]
        [SerializeField] private WindowController windowController;

        [Tooltip("방해 앱 감시. 없어도 동작한다.")]
        [SerializeField] private ForegroundWatcher watcher;

        [Tooltip("작물 목록. 수확 기록에 집중 작물 이름을 남길 때 쓴다.")]
        [SerializeField] private CropCatalog cropCatalog;

        public PomodoroStateMachine StateMachine => stateMachine;
        public PomodoroTimer Timer => timer;
        public SaiunDatabase Database => database;
        public ForegroundWatcher Watcher => watcher;
        public CropCatalog CropCatalog => cropCatalog;

        /// <summary>다음 세션에 쓸 설정. 시작 시 PlayerPrefs에 저장된다. Awake 전에 읽혀도 동작한다.</summary>
        public SessionConfig CurrentConfig
        {
            get => _currentConfig ??= SettingsStore.LoadSessionConfig();
            private set => _currentConfig = value;
        }

        /// <summary>세션 기록이 DB에 저장된 직후 발행.</summary>
        public event Action<SessionRecord> OnSessionRecorded;

        /// <summary>수확이 기록되고 보유량이 오른 직후 발행. 인자는 작물 종류.</summary>
        public event Action<string> OnHarvested;

        /// <summary>전체 데이터 초기화가 끝난 직후 발행.</summary>
        public event Action OnDataReset;

        /// <summary>튜토리얼을 다시 보여 달라는 요청. 튜토리얼 뷰가 구독한다.</summary>
        public event Action OnTutorialRequested;

        /// <summary>앱을 끄기 직전. 세션 기록은 이미 끝난 뒤다. 테스트에서 종료를 가로챌 때도 쓴다.</summary>
        public event Action OnQuitRequested;

        /// <summary>시계 화면 하늘을 하루 순환으로 켜거나(참) 실제 시각으로 돌렸다(거짓).</summary>
        public event Action<bool> OnIdleSkyCycleChanged;

        /// <summary>시계 하늘을 셈할 도시를 바꿨다(도시 이름).</summary>
        public event Action<string> OnSkyPlaceChanged;

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
            timer.StartSession(CurrentConfig.Clone());
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

        /// <summary>장기 휴식(수확 가능) 중에 휴식을 끝내고 작물을 거둔다. 기록은 세션 종료 처리가 맡는다.</summary>
        public void RequestHarvest()
        {
            if (stateMachine.CurrentState != PomodoroState.LongBreak) return;
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

        // ---- 시스템 설정 요청 (사양서 v1.1 12-2) ----

        /// <summary>항상 위를 켜거나 끄고 저장한다.</summary>
        /// <summary>화면 오른쪽 세로 전체 사이드바로 붙이거나 떠 있는 카드로 돌아간다.</summary>
        public void RequestSetWindowSidebar(bool sidebar)
        {
            SettingsStore.WindowSidebar = sidebar;
            if (windowController != null) windowController.SetSidebar(sidebar);
        }

        public void RequestSetAlwaysOnTop(bool alwaysOnTop)
        {
            SettingsStore.AlwaysOnTop = alwaysOnTop;
            if (windowController != null) windowController.SetAlwaysOnTop(alwaysOnTop);
        }

        /// <summary>창을 우측 상단 기본 위치로 옮기고, 저장된 위치를 지운다.</summary>
        public void RequestResetWindowPosition()
        {
            SettingsStore.ClearWindowPosition();
            if (windowController != null) windowController.MoveToDefaultPosition();
        }

        /// <summary>유예 시간을 바꾸고 저장한다. 범위 밖 값은 5~30초로 자른다.</summary>
        public void RequestSetGraceSeconds(int seconds)
        {
            SettingsStore.GraceSeconds = seconds;
            if (watcher != null) watcher.GraceSeconds = SettingsStore.GraceSeconds;
        }

        /// <summary>시계 하늘을 셈할 도시를 정한다. 해 뜨고 지는 시각과 달이 그곳을 따른다.</summary>
        public void RequestSetSkyPlace(string placeName)
        {
            SettingsStore.SkyPlace = placeName;
            OnSkyPlaceChanged?.Invoke(placeName);
        }

        /// <summary>시계 화면 하늘이 하루를 천천히 돌지(참), 실제 시각을 따를지(거짓) 정한다.</summary>
        public void RequestSetIdleSkyCycle(bool cycle)
        {
            SettingsStore.IdleSkyCycle = cycle;
            OnIdleSkyCycleChanged?.Invoke(cycle);
        }

        public void RequestSetSoundEnabled(bool enabled)
        {
            SettingsStore.SoundEnabled = enabled;
        }

        public void RequestSetSoundVolume(float volume)
        {
            SettingsStore.SoundVolume = volume;
        }

        /// <summary>날씨 트리거 '랜덤'을 켜거나 끈다. 다음 세트부터 반영된다.</summary>
        public void RequestSetWeatherRandom(bool enabled)
        {
            SettingsStore.WeatherRandom = enabled;
        }

        /// <summary>날씨 트리거 '집중 상태 연동'을 켜거나 끈다. 다음 상태 전이부터 반영된다.</summary>
        public void RequestSetWeatherFocusLinked(bool enabled)
        {
            SettingsStore.WeatherFocusLinked = enabled;
        }

        /// <summary>튜토리얼을 다시 보여 준다. 완료 표시를 지우고 요청을 알린다.</summary>
        public void RequestReplayTutorial()
        {
            SettingsStore.TutorialCompleted = false;
            OnTutorialRequested?.Invoke();
        }

        /// <summary>
        /// 세션·수확·보유량 기록을 모두 지운다. 설정값과 방해 앱 목록은 남긴다.
        /// 진행 중인 세션의 기록이 초기화 직후 다시 쌓이지 않도록 Idle에서만 받는다.
        /// </summary>
        public bool RequestResetAllData()
        {
            if (stateMachine.CurrentState != PomodoroState.Idle || database == null) return false;
            database.DeleteAllRows();
            OnDataReset?.Invoke();
            return true;
        }

        /// <summary>
        /// 앱을 끈다. 진행 중인 세션은 정지 버튼을 누른 것처럼 먼저 기록한 뒤 끈다.
        /// 에디터에서는 재생만 멈춘다.
        /// </summary>
        public void RequestQuit()
        {
            PrepareQuit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // 세션을 정리하고 종료를 알린다. 테스트는 실제로 끄지 않고 이것만 부른다.
        private void PrepareQuit()
        {
            if (stateMachine != null && stateMachine.CurrentState != PomodoroState.Idle)
            {
                // 유예·실패 중에는 Idle로 바로 갈 수 없어 전이표를 따라 정리한다.
                if (stateMachine.CurrentState == PomodoroState.Interrupted) stateMachine.ChangeState(PomodoroState.Failed);
                if (stateMachine.CurrentState == PomodoroState.Failed) stateMachine.ChangeState(PomodoroState.Idle);
                else timer.Cancel();
            }
            OnQuitRequested?.Invoke();
        }

        /// <summary>다음 세션의 태스크 텍스트를 바꾼다. 길이 제한은 SessionConfig가 적용한다.</summary>
        public void SetTaskText(string text)
        {
            CurrentConfig.TaskText = text;
        }

        // ---- 내부 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            if (from == PomodoroState.Idle && to == PomodoroState.Focus)
            {
                _sessionStartTime = SaiunDatabase.Now();
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
                CropType = FocusCropId,
                Result = result,
            };

            int sessionId = database.InsertSession(record);
            _sessionStartTime = null;

            // 사양서 v1.1 9장: 수확 1회 = 해당 작물 1개.
            bool harvested = result == SessionRecord.ResultHarvested;
            if (harvested) database.AddHarvest(sessionId, record.CropType);

            OnSessionRecorded?.Invoke(record);
            if (harvested) OnHarvested?.Invoke(record.CropType);
        }

        // 세션마다 화분에 심는 작물. 목록이 없으면 기록에 이름을 남기지 않는다(수확도 기록되지 않는다).
        private string FocusCropId => cropCatalog != null && cropCatalog.FocusCrop != null ? cropCatalog.FocusCrop.Id : null;

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

            // 사이드바로 쓰던 사람은 카드 자리를 기억해 둔 채 곧바로 오른쪽 가장자리에 붙는다.
            if (SettingsStore.WindowSidebar) windowController.SetSidebar(true);
        }

        private void HandleWindowMoved(Vector2Int position)
        {
            SettingsStore.SaveWindowPosition(position.x, position.y);
        }

        private void ApplyFrameRate()
        {
            int target = _hasFocus ? ActiveFrameRate : AwayFrameRate;

            // 포커스가 바뀔 때 한 번만 바꾼다. 매 프레임 설정하지 않는다.
            if (target == _appliedFrameRate) return;
            _appliedFrameRate = target;
            Application.targetFrameRate = target;
        }
    }
}
