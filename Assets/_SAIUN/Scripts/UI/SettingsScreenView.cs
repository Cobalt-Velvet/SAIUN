using System;
using System.Collections.Generic;
using System.Text;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 기어 아이콘으로 여는 시스템 설정 화면 (사양서 v1.1 12-2, P4-04).
    /// 방해 앱·날씨·창·사운드·데이터를 다룬다. 캐릭터 항목은 2026-09-21 사용자 결정으로 캐릭터와 함께 없앴다.
    /// 날씨 트리거 중 실제 날씨 연동은 API 키가 정해질 때까지 두지 않는다.
    /// 실행 중인 시스템에 닿는 설정은 GameManager에 요청하고, 방해 앱 목록은 감시자의 저장소를 직접 고친다(바로 저장된다).
    /// </summary>
    public class SettingsScreenView : MonoBehaviour
    {
        private const int MinutesPerHour = 60;
        private const int PercentScale = 100;
        private const string ExecutableSuffix = ".exe";

        [Header("참조")]
        [SerializeField] private GameManager gameManager;

        [Tooltip("열고 닫는 화면 본체")]
        [SerializeField] private GameObject screen;

        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;

        [Tooltip("확인 대화상자(종료 등)")]
        [SerializeField] private MessageDialogView dialog;

        [Header("방해 앱")]
        [SerializeField] private RectTransform blacklistRows;
        [SerializeField] private RectTransform whitelistRows;

        [Tooltip("목록 한 줄 틀. 자식 Name 글자와 Remove 버튼을 쓴다.")]
        [SerializeField] private GameObject rowTemplate;

        [SerializeField] private TMP_InputField blacklistInput;
        [SerializeField] private Button blacklistAddButton;
        [SerializeField] private TMP_Dropdown runningDropdown;
        [SerializeField] private Button runningAddButton;
        [SerializeField] private TMP_InputField whitelistInput;
        [SerializeField] private Button whitelistAddButton;
        [SerializeField] private SliderField graceField;

        [Header("날씨")]
        [SerializeField] private Toggle weatherRandomToggle;
        [SerializeField] private Toggle weatherFocusToggle;

        [Header("창")]
        [SerializeField] private Toggle alwaysOnTopToggle;
        [SerializeField] private Toggle windowSidebarToggle;
        [SerializeField] private Toggle windowGlassToggle;
        [SerializeField] private Button resetPositionButton;

        [Header("사운드")]
        [SerializeField] private Toggle soundToggle;
        [SerializeField] private SliderField volumeField;

        [Header("데이터")]
        [SerializeField] private TMP_Text focusTimeText;
        [SerializeField] private TMP_Text harvestCountText;
        [SerializeField] private TMP_Text inventoryText;
        [SerializeField] private Button tutorialButton;
        [SerializeField] private Button resetDataButton;
        [SerializeField] private TMP_Text resetDataLabel;
        [SerializeField] private Button quitButton;

        [Tooltip("초기화 확인 단계가 이 시간 안에 이어지지 않으면 처음으로 돌아간다(초)")]
        [SerializeField, Min(1f)] private float resetConfirmSeconds = 5f;

        [Header("문구")]
        [SerializeField] private string graceLabel = "유예(초)";
        [SerializeField] private string volumeLabel = "볼륨(%)";
        [SerializeField] private string runningPlaceholder = "실행 중인 앱";
        [SerializeField] private string focusTimeFormat = "누적 집중 {0}시간 {1}분";
        [SerializeField] private string harvestCountFormat = "총 수확 {0}회";
        [SerializeField] private string inventoryEmpty = "보유한 수확물이 없어요";
        [SerializeField] private string resetLabel = "전체 데이터 초기화";
        [SerializeField] private string resetConfirm1Label = "정말 지울까요? (1/2)";
        [SerializeField] private string resetConfirm2Label = "되돌릴 수 없어요. 지우기 (2/2)";
        [SerializeField] private string resetDoneLabel = "초기화했어요";
        [SerializeField] private string resetBusyLabel = "세션이 끝난 뒤에 할 수 있어요";
        [SerializeField] private string cancelLabel = "취소";
        [SerializeField] private string quitTitle = "SAIUN을 끌까요?";
        [SerializeField] private string quitIdleBody = "다음에 켜면 지금 설정 그대로 시작해요.";
        [SerializeField] private string quitSessionBody = "진행 중인 세션은 정지한 것으로 기록돼요.";
        [SerializeField] private string quitConfirm = "종료";

        /// <summary>화면이 열려 있는지.</summary>
        public bool IsOpen => screen != null && screen.activeSelf;

        /// <summary>초기화 확인 단계. 0이면 대기, 1·2는 확인 중.</summary>
        public int ResetStep { get; private set; }

        private readonly List<GameObject> _rows = new List<GameObject>();
        private List<string> _running = new List<string>();
        private float _resetStepTime;
        private bool _wired;

        // 테스트에서 가짜 시계를 넣는다.
        private Func<float> _clock = () => Time.unscaledTime;

        private BlacklistStore Lists => gameManager.Watcher != null ? gameManager.Watcher.Blacklist : null;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogError("SettingsScreenView: GameManager를 찾지 못했습니다.");
                enabled = false;
                return;
            }

            if (rowTemplate != null) rowTemplate.SetActive(false);
            if (screen != null) screen.SetActive(false);
            Wire();
        }

        private void OnEnable()
        {
            if (gameManager == null) return;
            gameManager.OnSessionRecorded += HandleRecordsChanged;
            gameManager.OnDataReset += HandleDataReset;
        }

        private void OnDisable()
        {
            if (gameManager == null) return;
            gameManager.OnSessionRecorded -= HandleRecordsChanged;
            gameManager.OnDataReset -= HandleDataReset;
        }

        private void Update()
        {
            // 확인 단계는 잠깐만 유지한다. 잘못 누른 채로 남아 있지 않게 한다.
            if (ResetStep > 0 && _clock() - _resetStepTime > resetConfirmSeconds) SetResetStep(0);
        }

        // ---- 열고 닫기 ----

        public void Open()
        {
            if (screen == null) return;
            Wire();

            graceField.Setup(graceLabel, SettingsStore.MinGraceSeconds, SettingsStore.MaxGraceSeconds, SettingsStore.GraceSeconds);
            volumeField.Setup(volumeLabel, 0, PercentScale, Mathf.RoundToInt(SettingsStore.SoundVolume * PercentScale));
            if (weatherRandomToggle != null) weatherRandomToggle.SetIsOnWithoutNotify(SettingsStore.WeatherRandom);
            if (weatherFocusToggle != null) weatherFocusToggle.SetIsOnWithoutNotify(SettingsStore.WeatherFocusLinked);
            if (alwaysOnTopToggle != null) alwaysOnTopToggle.SetIsOnWithoutNotify(SettingsStore.AlwaysOnTop);
            if (windowSidebarToggle != null) windowSidebarToggle.SetIsOnWithoutNotify(SettingsStore.WindowSidebar);
            if (windowGlassToggle != null) windowGlassToggle.SetIsOnWithoutNotify(SettingsStore.WindowGlass);
            if (soundToggle != null) soundToggle.SetIsOnWithoutNotify(SettingsStore.SoundEnabled);

            RefreshLists();
            RefreshRunning();
            RefreshData();
            SetResetStep(0);

            screen.SetActive(true);
        }

        public void Close()
        {
            if (screen != null) screen.SetActive(false);
            SetResetStep(0);
        }

        // ---- 방해 앱 ----

        /// <summary>블랙리스트에 추가한다. 이미 있거나 비어 있으면 무시한다.</summary>
        public bool AddToBlacklist(string processName)
        {
            if (Lists == null || !Lists.AddToBlacklist(ToExecutableName(processName))) return false;
            RefreshLists();
            RefreshRunning();
            return true;
        }

        public bool AddToWhitelist(string processName)
        {
            if (Lists == null || !Lists.AddToWhitelist(ToExecutableName(processName))) return false;
            RefreshLists();
            return true;
        }

        /// <summary>초기화 버튼을 누른 것과 같다. 두 번 확인한 뒤 세 번째에 지운다.</summary>
        public void PressResetData()
        {
            if (ResetStep < 2)
            {
                SetResetStep(ResetStep + 1);
                return;
            }

            SetResetStep(0);
            bool done = gameManager.RequestResetAllData();
            if (resetDataLabel != null) resetDataLabel.text = done ? resetDoneLabel : resetBusyLabel;
        }

        // ---- 종료 ----

        /// <summary>종료를 확인받는다. 세션 중이면 정지로 기록된다고 알린다.</summary>
        public void ConfirmQuit()
        {
            bool running = gameManager.StateMachine.CurrentState != PomodoroState.Idle;
            if (dialog == null)
            {
                gameManager.RequestQuit();
                return;
            }
            dialog.Show(quitTitle, running ? quitSessionBody : quitIdleBody, quitConfirm, gameManager.RequestQuit, cancelLabel);
        }

        /// <summary>테스트용 시계 교체.</summary>
        internal void SetClock(Func<float> clock)
        {
            _clock = clock ?? (() => Time.unscaledTime);
        }

        // ---- 내부 ----

        private void Wire()
        {
            if (_wired) return;
            _wired = true;

            if (openButton != null) openButton.onClick.AddListener(() => { if (IsOpen) Close(); else Open(); });
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            if (blacklistAddButton != null) blacklistAddButton.onClick.AddListener(() => AddFromInput(blacklistInput, AddToBlacklist));
            if (whitelistAddButton != null) whitelistAddButton.onClick.AddListener(() => AddFromInput(whitelistInput, AddToWhitelist));
            if (runningAddButton != null) runningAddButton.onClick.AddListener(AddSelectedRunning);

            graceField.OnValueChanged += gameManager.RequestSetGraceSeconds;
            volumeField.OnValueChanged += percent => gameManager.RequestSetSoundVolume(percent / (float)PercentScale);
            if (weatherRandomToggle != null) weatherRandomToggle.onValueChanged.AddListener(gameManager.RequestSetWeatherRandom);
            if (weatherFocusToggle != null) weatherFocusToggle.onValueChanged.AddListener(gameManager.RequestSetWeatherFocusLinked);
            if (alwaysOnTopToggle != null) alwaysOnTopToggle.onValueChanged.AddListener(gameManager.RequestSetAlwaysOnTop);
            if (windowSidebarToggle != null) windowSidebarToggle.onValueChanged.AddListener(gameManager.RequestSetWindowSidebar);
            if (windowGlassToggle != null) windowGlassToggle.onValueChanged.AddListener(gameManager.RequestSetWindowGlass);
            if (soundToggle != null) soundToggle.onValueChanged.AddListener(gameManager.RequestSetSoundEnabled);
            if (resetPositionButton != null) resetPositionButton.onClick.AddListener(gameManager.RequestResetWindowPosition);


            if (tutorialButton != null) tutorialButton.onClick.AddListener(() =>
            {
                Close();
                gameManager.RequestReplayTutorial();
            });
            if (resetDataButton != null) resetDataButton.onClick.AddListener(PressResetData);
            if (quitButton != null) quitButton.onClick.AddListener(ConfirmQuit);
        }

        private static void AddFromInput(TMP_InputField input, Func<string, bool> add)
        {
            if (input == null) return;
            if (add(input.text)) input.SetTextWithoutNotify(string.Empty);
        }

        private void AddSelectedRunning()
        {
            if (runningDropdown == null || _running.Count == 0) return;
            int index = runningDropdown.value;
            if (index >= 0 && index < _running.Count) AddToBlacklist(_running[index]);
        }

        // 사용자가 "chrome"처럼 적어도 목록에는 실행 파일 이름으로 남긴다.
        private static string ToExecutableName(string processName)
        {
            string trimmed = processName?.Trim() ?? string.Empty;
            if (BlacklistStore.Normalize(trimmed).Length == 0) return string.Empty;
            return trimmed.EndsWith(ExecutableSuffix, StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ExecutableSuffix;
        }

        private void RefreshLists()
        {
            foreach (GameObject row in _rows)
            {
                row.SetActive(false);
                Destroy(row);
            }
            _rows.Clear();

            if (Lists == null) return;
            foreach (string name in Lists.Blacklist) AddRow(blacklistRows, name, n => Lists.RemoveFromBlacklist(n));
            foreach (string name in Lists.Whitelist) AddRow(whitelistRows, name, n => Lists.RemoveFromWhitelist(n));
        }

        private void AddRow(RectTransform container, string processName, Func<string, bool> remove)
        {
            if (container == null || rowTemplate == null) return;

            GameObject row = Instantiate(rowTemplate, container);
            row.name = $"Row_{processName}";
            row.SetActive(true);
            _rows.Add(row);

            Transform label = row.transform.Find("Name");
            if (label != null && label.TryGetComponent(out TMP_Text text)) text.text = processName;

            Transform removeChild = row.transform.Find("Remove");
            if (removeChild != null && removeChild.TryGetComponent(out Button button))
            {
                button.onClick.AddListener(() =>
                {
                    if (remove(processName))
                    {
                        RefreshLists();
                        RefreshRunning();
                    }
                });
            }
        }

        // 이미 블랙리스트에 있는 앱은 드롭다운에서 뺀다.
        private void RefreshRunning()
        {
            if (runningDropdown == null) return;

            _running = ForegroundWatcher.GetRunningAppNames();
            if (Lists != null) _running.RemoveAll(Lists.IsBlacklisted);

            runningDropdown.ClearOptions();
            runningDropdown.AddOptions(_running.Count > 0 ? _running : new List<string> { runningPlaceholder });
            runningDropdown.SetValueWithoutNotify(0);
            runningDropdown.interactable = _running.Count > 0;
            if (runningAddButton != null) runningAddButton.interactable = _running.Count > 0;
        }

        private void RefreshData()
        {
            SaiunDatabase database = gameManager.Database;
            if (database == null) return;

            int minutes = database.GetTotalFocusMinutes();
            if (focusTimeText != null) focusTimeText.text = string.Format(focusTimeFormat, minutes / MinutesPerHour, minutes % MinutesPerHour);
            if (harvestCountText != null) harvestCountText.text = string.Format(harvestCountFormat, database.GetHarvestCount());
            if (inventoryText != null) inventoryText.text = InventorySummary(database.GetInventory());
        }

        // "쌀 3 · 밀 1"처럼 작물 목록 순서대로 적는다. 목록에 없는 작물은 id로 적는다.
        private string InventorySummary(List<InventoryRecord> inventory)
        {
            if (inventory.Count == 0) return inventoryEmpty;

            CropCatalog catalog = gameManager.CropCatalog;
            var builder = new StringBuilder();
            IEnumerable<CropDefinition> order = catalog != null ? catalog.Crops : Array.Empty<CropDefinition>();
            var written = new HashSet<string>();

            foreach (CropDefinition crop in order)
            {
                InventoryRecord item = inventory.Find(r => r.CropType == crop.Id);
                if (item == null || item.Quantity <= 0) continue;
                Append(builder, crop.DisplayName, item.Quantity);
                written.Add(item.CropType);
            }
            foreach (InventoryRecord item in inventory)
            {
                if (item.Quantity > 0 && !written.Contains(item.CropType)) Append(builder, item.CropType, item.Quantity);
            }

            return builder.Length > 0 ? builder.ToString() : inventoryEmpty;
        }

        private static void Append(StringBuilder builder, string name, int quantity)
        {
            if (builder.Length > 0) builder.Append(" · ");
            builder.Append(name).Append(' ').Append(quantity);
        }

        private void SetResetStep(int step)
        {
            ResetStep = step;
            _resetStepTime = _clock();
            if (resetDataLabel == null) return;
            resetDataLabel.text = step == 1 ? resetConfirm1Label : step == 2 ? resetConfirm2Label : resetLabel;
        }

        private void HandleRecordsChanged(SessionRecord record)
        {
            if (IsOpen) RefreshData();
        }

        private void HandleDataReset()
        {
            RefreshData();
        }
    }
}
