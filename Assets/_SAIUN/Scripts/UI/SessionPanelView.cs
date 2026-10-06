using System;
using _SAIUN.Scripts.Core;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 시작 버튼을 누르면 하단 바 위로 올라오는 세션 설정 패널 (사양서 v1.1 12-1, P4-04).
    /// 집중·휴식·세트를 슬라이더와 숫자로 고른다. 화분에 심는 작물은 늘 같아 고르지 않는다.
    /// 값은 사본에서만 바꾸고, 시작할 때 GameManager에 넘긴다(마지막 값은 GameManager가 저장한다).
    /// 태스크 입력은 하단 바의 입력칸을 그대로 쓴다.
    /// </summary>
    public class SessionPanelView : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private GameManager gameManager;

        [Tooltip("열고 닫는 패널 본체")]
        [SerializeField] private GameObject panel;

        [SerializeField] private Button closeButton;
        [SerializeField] private SliderField focusField;
        [SerializeField] private SliderField shortBreakField;
        [SerializeField] private SliderField longBreakField;
        [SerializeField] private SliderField setsField;

        [Header("문구")]
        [SerializeField] private string focusLabel = "집중(분)";
        [SerializeField] private string shortBreakLabel = "짧은 휴식(분)";
        [SerializeField] private string longBreakLabel = "긴 휴식(분)";
        [SerializeField] private string setsLabel = "세트";

        /// <summary>패널이 열려 있는지.</summary>
        public bool IsOpen => panel != null && panel.activeSelf;

        /// <summary>지금 패널에서 고르고 있는 설정(사본).</summary>
        public SessionConfig Draft { get; private set; }

        /// <summary>열리고 닫힐 때 발행. 인자는 열렸는지.</summary>
        public event Action<bool> OnOpenChanged;

        private bool _wired;

        private void Awake()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (panel != null) panel.SetActive(false);
            Wire();
        }

        // ---- 열고 닫기 ----

        /// <summary>현재 설정으로 채워서 연다.</summary>
        public void Open()
        {
            if (gameManager == null || panel == null) return;
            Wire();

            Draft = gameManager.CurrentConfig.Clone();
            focusField.Setup(focusLabel, SessionConfig.MinFocusMinutes, SessionConfig.MaxFocusMinutes, Draft.FocusMinutes);
            shortBreakField.Setup(shortBreakLabel, SessionConfig.MinShortBreakMinutes, SessionConfig.MaxShortBreakMinutes, Draft.ShortBreakMinutes);
            longBreakField.Setup(longBreakLabel, SessionConfig.MinLongBreakMinutes, SessionConfig.MaxLongBreakMinutes, Draft.LongBreakMinutes);
            setsField.Setup(setsLabel, SessionConfig.MinTotalSets, SessionConfig.MaxTotalSets, Draft.TotalSets);

            panel.SetActive(true);
            OnOpenChanged?.Invoke(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            OnOpenChanged?.Invoke(false);
        }

        /// <summary>고른 설정으로 세션을 시작한다. 태스크는 하단 바에 적힌 것을 쓴다.</summary>
        public void Confirm()
        {
            if (!IsOpen || Draft == null) return;
            Draft.TaskText = gameManager.CurrentConfig.TaskText;
            SessionConfig chosen = Draft;
            Close();
            gameManager.RequestStart(chosen);
        }

        // ---- 내부 ----

        private void Wire()
        {
            if (_wired) return;
            _wired = true;
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            focusField.OnValueChanged += v => Draft.FocusMinutes = v;
            shortBreakField.OnValueChanged += v => Draft.ShortBreakMinutes = v;
            longBreakField.OnValueChanged += v => Draft.LongBreakMinutes = v;
            setsField.OnValueChanged += v => Draft.TotalSets = v;
        }
    }
}
