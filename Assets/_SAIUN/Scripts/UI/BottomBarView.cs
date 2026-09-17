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
        [SerializeField] private string excuseLabel = "괜찮아요";
        [SerializeField] private string harvestLabel = "수확";

        [Header("색상")]
        [SerializeField] private Color backgroundColor = SaiunPalette.BottomBarBackground;
        [SerializeField] private Color inputBackgroundColor = SaiunPalette.InputBackground;
        [SerializeField] private Color inputTextColor = SaiunPalette.InputText;
        [SerializeField] private Color buttonColor = SaiunPalette.MainPoint;
        [SerializeField] private Color buttonTextColor = SaiunPalette.OnMainPoint;

        [Tooltip("장기 휴식(수확 가능) 중 버튼 강조 (사양서 v1.1 13-2)")]
        [SerializeField] private Color harvestButtonColor = SaiunPalette.Harvestable;
        [SerializeField] private Color harvestButtonTextColor = SaiunPalette.OnMainPoint;

        /// <summary>현재 버튼에 표시된 라벨.</summary>
        public string CurrentButtonLabel => primaryButtonLabel != null ? primaryButtonLabel.text : string.Empty;

        /// <summary>버튼이 수확 강조 상태인지.</summary>
        public bool IsHarvestHighlighted { get; private set; }

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
                    // 유예 중에는 취소 대신 세션 예외('지금은 괜찮아요')를 요청한다.
                    gameManager.RequestExcuseDistraction();
                    break;

                case PomodoroState.LongBreak:
                    gameManager.RequestHarvest();
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

            // 장기 휴식은 전 세트를 마친 수확 가능 상태다. 버튼을 누르면 휴식을 끝내고 거둔다.
            IsHarvestHighlighted = state == PomodoroState.LongBreak;

            if (primaryButtonLabel != null)
            {
                primaryButtonLabel.text = idle ? startLabel
                    : state == PomodoroState.Failed ? acknowledgeLabel
                    : state == PomodoroState.Interrupted ? excuseLabel
                    : IsHarvestHighlighted ? harvestLabel
                    : stopLabel;
            }
            ApplyButtonColors();

            // 유예 중에는 감시자가 있을 때만(세션 예외 요청 가능) 버튼을 살린다.
            if (primaryButton != null) primaryButton.interactable = state != PomodoroState.Interrupted || gameManager.Watcher != null;

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

        private void ApplyButtonColors()
        {
            if (primaryButton != null && primaryButton.targetGraphic != null)
            {
                primaryButton.targetGraphic.color = IsHarvestHighlighted ? harvestButtonColor : buttonColor;
            }
            if (primaryButtonLabel != null)
            {
                primaryButtonLabel.color = IsHarvestHighlighted ? harvestButtonTextColor : buttonTextColor;
            }
        }

#if UNITY_EDITOR
        // 인스펙터에서 색을 바꾸면 에디터에서 바로 보이게 한다. 실측용이며 실행에는 영향이 없다.
        private void OnValidate()
        {
            ApplyStyle();
        }
#endif

        /// <summary>
        /// Selectable의 상태 틴트. Unity 기본값은 비활성일 때 회색으로 밀어버려 팔레트를 벗어난다.
        /// 여기서는 밝기와 알파만 건드려 색조를 그대로 둔다.
        /// </summary>
        private static ColorBlock PaletteColorBlock()
        {
            ColorBlock block = ColorBlock.defaultColorBlock;
            block.normalColor = Color.white;
            block.highlightedColor = Color.white;
            block.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            block.selectedColor = Color.white;
            block.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            return block;
        }

        private void ApplyStyle()
        {
            if (background != null) background.color = backgroundColor;

            if (primaryButton != null) primaryButton.colors = PaletteColorBlock();
            ApplyButtonColors();

            if (taskInput != null)
            {
                taskInput.colors = PaletteColorBlock();

                var inputBackground = taskInput.GetComponent<Image>();
                if (inputBackground != null) inputBackground.color = inputBackgroundColor;

                if (taskInput.textComponent != null) taskInput.textComponent.color = inputTextColor;
                if (taskInput.placeholder is Graphic placeholder)
                {
                    placeholder.color = new Color(inputTextColor.r, inputTextColor.g, inputTextColor.b, inputTextColor.a * 0.5f);
                }
            }
        }
    }
}
