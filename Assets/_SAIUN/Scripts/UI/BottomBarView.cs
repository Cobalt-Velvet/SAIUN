using _SAIUN.Scripts.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 하단 바. 시작·정지 버튼과 태스크 텍스트를 표시한다.
    /// 상태머신을 구독해 라벨을 바꾸고, 상태 변경은 직접 하지 않고 GameManager에 요청한다.
    /// </summary>
    public class BottomBarView : MonoBehaviour
    {
        /// <summary>Bottom Bar 높이(px). 사양서 8장 확정값.</summary>
        public const int Height = 68;

        [Header("참조")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private Button primaryButton;
        [SerializeField] private TMP_Text primaryButtonLabel;
        [SerializeField] private TMP_InputField taskInput;
        [SerializeField] private Image background;

        [Header("버튼 라벨")]
        [SerializeField] private string startLabel = "시작";
        [SerializeField] private string stopLabel = "정지";
        [SerializeField] private string acknowledgeLabel = "확인";

        [Header("색상 (실측 대기값)")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color textColor = Color.white;

        /// <summary>현재 버튼에 표시된 라벨.</summary>
        public string CurrentButtonLabel => primaryButtonLabel != null ? primaryButtonLabel.text : string.Empty;

        public TMP_InputField TaskInput => taskInput;
        public Button PrimaryButton => primaryButton;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogError("BottomBarView: GameManager를 찾지 못했습니다.");
                enabled = false;
                return;
            }

            if (taskInput != null)
            {
                taskInput.characterLimit = SessionConfig.MaxTaskTextLength;
                taskInput.onValueChanged.AddListener(HandleTaskChanged);
            }

            if (primaryButton != null) primaryButton.onClick.AddListener(HandlePrimaryClick);

            ApplyStyle();
        }

        private void OnEnable()
        {
            if (gameManager == null) return;
            gameManager.StateMachine.OnStateChanged += HandleStateChanged;
            Refresh(gameManager.StateMachine.CurrentState);
        }

        private void OnDisable()
        {
            if (gameManager == null) return;
            gameManager.StateMachine.OnStateChanged -= HandleStateChanged;
        }

        // ---- 이벤트 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            Refresh(to);
        }

        private void HandlePrimaryClick()
        {
            switch (gameManager.StateMachine.CurrentState)
            {
                case PomodoroState.Idle:
                    if (taskInput != null) gameManager.SetTaskText(taskInput.text);
                    gameManager.RequestStart();
                    break;

                case PomodoroState.Failed:
                    gameManager.RequestAcknowledgeFailure();
                    break;

                case PomodoroState.Interrupted:
                    // 유예 중에는 취소할 수 없다. 버튼도 비활성이다.
                    break;

                default:
                    gameManager.RequestCancel();
                    break;
            }
        }

        private void HandleTaskChanged(string text)
        {
            if (gameManager.StateMachine.CurrentState != PomodoroState.Idle) return;
            gameManager.SetTaskText(text);
        }

        // ---- 표시 ----

        private void Refresh(PomodoroState state)
        {
            bool idle = state == PomodoroState.Idle;

            if (primaryButtonLabel != null)
            {
                primaryButtonLabel.text = idle ? startLabel
                    : state == PomodoroState.Failed ? acknowledgeLabel
                    : stopLabel;
            }

            if (primaryButton != null) primaryButton.interactable = state != PomodoroState.Interrupted;

            if (taskInput != null)
            {
                taskInput.interactable = idle;

                // 세션 중에는 시작 시점의 태스크를, Idle에서는 다음 세션용 태스크를 보여준다.
                string text = !idle && gameManager.Timer.Config != null
                    ? gameManager.Timer.Config.TaskText
                    : gameManager.CurrentConfig.TaskText;
                if (taskInput.text != text) taskInput.SetTextWithoutNotify(text);
            }
        }

        private void ApplyStyle()
        {
            if (background != null) background.color = backgroundColor;
            if (primaryButtonLabel != null) primaryButtonLabel.color = textColor;
            if (taskInput != null)
            {
                if (taskInput.textComponent != null) taskInput.textComponent.color = textColor;
                if (taskInput.placeholder is Graphic placeholder)
                {
                    placeholder.color = new Color(textColor.r, textColor.g, textColor.b, textColor.a * 0.5f);
                }
            }
        }
    }
}
