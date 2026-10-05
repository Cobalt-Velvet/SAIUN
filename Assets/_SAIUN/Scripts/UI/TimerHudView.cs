using System;
using System.Collections;
using System.Collections.Generic;
using _SAIUN.Scripts.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 타이머·시계 HUD.
    /// Idle에서는 현재 시각을 대형으로, 세션 중에는 타이머를 대형·현재 시각을 소형으로 2단 표시한다.
    /// 세트 진행 도트를 함께 그린다.
    /// </summary>
    public class TimerHudView : MonoBehaviour
    {
        private const int SecondsPerMinute = 60;

        [Header("참조")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private TMP_Text primaryText;
        [SerializeField] private TMP_Text secondaryText;
        [SerializeField] private TMP_Text phaseLabel;
        [SerializeField] private RectTransform dotsContainer;
        [SerializeField] private Image dotTemplate;

        [Header("표시 형식")]
        [SerializeField] private string clockFormat = "HH:mm";
        [SerializeField] private string focusLabel = "집중";
        [SerializeField] private string shortBreakLabel = "짧은 휴식";
        [SerializeField] private string longBreakLabel = "긴 휴식";
        [SerializeField] private string interruptedLabel = "유예";
        [Tooltip("유예 중 남은 초를 넣어 보여 줄 형식. {0}이 남은 초다.")]
        [SerializeField] private string graceFormat = "유예 {0}초";
        [SerializeField] private string failedLabel = "실패";

        [Header("색상")]
        [SerializeField] private Color textColor = SaiunPalette.HudText;
        [SerializeField] private Color dotCompletedColor = SaiunPalette.SetDotCompleted;
        [SerializeField] private Color dotPendingColor = SaiunPalette.SetDotPending;

        private readonly List<Image> _dots = new List<Image>();
        private string _clockText = string.Empty;
        private Coroutine _clockRoutine;

        // ---- 읽기 전용 상태 (테스트·다른 시스템용) ----
        public string PrimaryText => primaryText != null ? primaryText.text : string.Empty;
        public string SecondaryText => secondaryText != null ? secondaryText.text : string.Empty;
        public string PhaseText => phaseLabel != null ? phaseLabel.text : string.Empty;
        public bool IsSecondaryVisible => secondaryText != null && secondaryText.gameObject.activeSelf;
        public bool AreDotsVisible => dotsContainer != null && dotsContainer.gameObject.activeSelf;
        public int DotCount => _dots.Count;
        public int FilledDotCount { get; private set; }

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogError("TimerHudView: GameManager를 찾지 못했습니다.");
                enabled = false;
                return;
            }

            if (dotTemplate != null) dotTemplate.gameObject.SetActive(false);
            ApplyStyle();
        }

        private void OnEnable()
        {
            if (gameManager == null) return;
            gameManager.StateMachine.OnStateChanged += HandleStateChanged;
            gameManager.Timer.OnTick += HandleTick;
            if (gameManager.Watcher != null) gameManager.Watcher.OnGraceTick += HandleGraceTick;

            UpdateClock(force: true);
            Refresh(gameManager.StateMachine.CurrentState);
            _clockRoutine = StartCoroutine(ClockLoop());
        }

        private void OnDisable()
        {
            if (gameManager == null) return;
            gameManager.StateMachine.OnStateChanged -= HandleStateChanged;
            gameManager.Timer.OnTick -= HandleTick;
            if (gameManager.Watcher != null) gameManager.Watcher.OnGraceTick -= HandleGraceTick;

            if (_clockRoutine != null)
            {
                StopCoroutine(_clockRoutine);
                _clockRoutine = null;
            }
        }

        // ---- 이벤트 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            Refresh(to);
        }

        private void HandleTick()
        {
            RefreshTimer();
        }

        // 유예 중에는 라벨에 남은 초를 함께 보여준다.
        private void HandleGraceTick(float remainingSeconds)
        {
            if (phaseLabel == null || gameManager.StateMachine.CurrentState != PomodoroState.Interrupted) return;
            phaseLabel.text = string.Format(graceFormat, Mathf.CeilToInt(remainingSeconds));
        }

        // 시계는 매 프레임이 아니라 1초마다 확인하고, 표시 문자열이 바뀔 때만 갱신한다.
        private IEnumerator ClockLoop()
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                yield return wait;
                UpdateClock(force: false);
            }
        }

        // ---- 표시 ----

        private void Refresh(PomodoroState state)
        {
            bool showTimer = state != PomodoroState.Idle && state != PomodoroState.Failed;

            if (secondaryText != null) secondaryText.gameObject.SetActive(showTimer);
            if (phaseLabel != null)
            {
                phaseLabel.gameObject.SetActive(state != PomodoroState.Idle);
                phaseLabel.text = PhaseLabelFor(state);
            }

            if (dotsContainer != null)
            {
                dotsContainer.gameObject.SetActive(state != PomodoroState.Idle);
                SessionConfig config = gameManager.Timer.Config;
                if (config != null) EnsureDots(config.TotalSets);
            }

            RefreshTimer();
            ApplyClock();
        }

        private void RefreshTimer()
        {
            PomodoroState state = gameManager.StateMachine.CurrentState;
            bool showTimer = state != PomodoroState.Idle && state != PomodoroState.Failed;

            if (primaryText != null && showTimer)
            {
                primaryText.text = FormatRemaining(gameManager.Timer.RemainingSeconds);
            }

            FilledDotCount = Mathf.Clamp(gameManager.Timer.CompletedSets, 0, _dots.Count);
            for (int i = 0; i < _dots.Count; i++)
            {
                _dots[i].color = i < FilledDotCount ? dotCompletedColor : dotPendingColor;
            }
        }

        private void UpdateClock(bool force)
        {
            string now = DateTime.Now.ToString(clockFormat);
            if (!force && now == _clockText) return;
            _clockText = now;
            ApplyClock();
        }

        private void ApplyClock()
        {
            PomodoroState state = gameManager.StateMachine.CurrentState;
            bool showTimer = state != PomodoroState.Idle && state != PomodoroState.Failed;

            if (showTimer)
            {
                if (secondaryText != null) secondaryText.text = _clockText;
            }
            else
            {
                if (primaryText != null) primaryText.text = _clockText;
            }
        }

        private void EnsureDots(int count)
        {
            if (dotTemplate == null || _dots.Count == count) return;

            foreach (Image dot in _dots) Destroy(dot.gameObject);
            _dots.Clear();

            for (int i = 0; i < count; i++)
            {
                Image dot = Instantiate(dotTemplate, dotsContainer);
                dot.name = $"Dot{i + 1}";
                dot.gameObject.SetActive(true);
                _dots.Add(dot);
            }
        }

        private string PhaseLabelFor(PomodoroState state)
        {
            switch (state)
            {
                case PomodoroState.Focus: return focusLabel;
                case PomodoroState.ShortBreak: return shortBreakLabel;
                case PomodoroState.LongBreak: return longBreakLabel;
                case PomodoroState.Interrupted: return interruptedLabel;
                case PomodoroState.Failed: return failedLabel;
                default: return string.Empty;
            }
        }

#if UNITY_EDITOR
        // 인스펙터에서 색을 바꾸면 에디터에서 바로 보이게 한다. 실측용이며 실행에는 영향이 없다.
        private void OnValidate()
        {
            ApplyStyle();
        }
#endif

        private void ApplyStyle()
        {
            if (primaryText != null) primaryText.color = textColor;
            if (secondaryText != null) secondaryText.color = textColor;
            if (phaseLabel != null) phaseLabel.color = textColor;

            // 실행 중에는 EnsureDots가 만든 도트가 색을 따로 받는다. 여기서는 템플릿만 맞춘다.
            if (dotTemplate != null) dotTemplate.color = dotPendingColor;
        }

        /// <summary>남은 초를 mm:ss로 만든다. 올림이라 59.2초는 01:00으로 보인다.</summary>
        public static string FormatRemaining(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / SecondsPerMinute:00}:{total % SecondsPerMinute:00}";
        }
    }
}
