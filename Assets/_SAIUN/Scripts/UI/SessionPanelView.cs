using System;
using System.Collections.Generic;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 시작 버튼을 누르면 하단 바 위로 올라오는 세션 설정 패널 (사양서 v1.1 12-1, P4-04).
    /// 집중·휴식·세트를 슬라이더와 숫자로 고르고, 해금된 작물 중 하나를 고른다.
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
        [SerializeField] private RectTransform cropGrid;

        [Tooltip("작물 버튼 틀. 자식 Name·Detail 글자를 쓴다.")]
        [SerializeField] private Button cropTemplate;

        [Tooltip("작물 대체·등급 안내")]
        [SerializeField] private TMP_Text hintText;

        [Header("문구")]
        [SerializeField] private string focusLabel = "집중(분)";
        [SerializeField] private string shortBreakLabel = "짧은 휴식(분)";
        [SerializeField] private string longBreakLabel = "긴 휴식(분)";
        [SerializeField] private string setsLabel = "세트";
        [SerializeField] private string requirementFormat = "{0}분 × {1}";
        [SerializeField] private string lockedFocusFormat = "집중 {0}시간";
        [SerializeField] private string lockedHarvestFormat = "수확 {0}회";
        [SerializeField] private string fallbackFormat = "{0} 조건 {1}분 × {2}세트 미달 · 이번 작물 {3}";
        [SerializeField] private string gradeFormat = "작물 등급: {0}";
        [SerializeField] private string[] gradeNames = { "일반", "고급", "희귀", "전설" };

        [Header("색상")]
        [SerializeField] private Color cropColor = SaiunPalette.WithAlpha(SaiunPalette.JungleTeal, 0.55f);
        [SerializeField] private Color cropTextColor = SaiunPalette.Eggshell;
        [SerializeField] private Color selectedCropColor = SaiunPalette.MainPoint;
        [SerializeField] private Color selectedCropTextColor = SaiunPalette.OnMainPoint;

        /// <summary>패널이 열려 있는지.</summary>
        public bool IsOpen => panel != null && panel.activeSelf;

        /// <summary>지금 패널에서 고르고 있는 설정(사본).</summary>
        public SessionConfig Draft { get; private set; }

        /// <summary>열리고 닫힐 때 발행. 인자는 열렸는지.</summary>
        public event Action<bool> OnOpenChanged;

        private readonly List<(CropDefinition Crop, Button Button)> _cropButtons = new List<(CropDefinition, Button)>();
        private bool _wired;

        private void Awake()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (cropTemplate != null) cropTemplate.gameObject.SetActive(false);
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

            BuildCropButtons();
            Refresh();

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

        /// <summary>작물을 고른다. 필요 설정보다 낮으면 그 설정까지 올려 준다.</summary>
        public void SelectCrop(string cropId)
        {
            if (Draft == null) return;
            Draft.CropType = cropId;

            CropDefinition crop = gameManager.CropCatalog != null ? gameManager.CropCatalog.Find(cropId) : null;
            if (crop != null && !crop.IsDefault)
            {
                if (Draft.FocusMinutes < crop.RequiredFocusMinutes) Draft.FocusMinutes = crop.RequiredFocusMinutes;
                if (Draft.TotalSets < crop.RequiredSets) Draft.TotalSets = crop.RequiredSets;
                focusField.SetValue(Draft.FocusMinutes);
                setsField.SetValue(Draft.TotalSets);
            }
            Refresh();
        }

        // ---- 내부 ----

        private void Wire()
        {
            if (_wired) return;
            _wired = true;
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            focusField.OnValueChanged += v => { Draft.FocusMinutes = v; Refresh(); };
            shortBreakField.OnValueChanged += v => Draft.ShortBreakMinutes = v;
            longBreakField.OnValueChanged += v => Draft.LongBreakMinutes = v;
            setsField.OnValueChanged += v => { Draft.TotalSets = v; Refresh(); };
        }

        // 해금 상태가 세션마다 바뀔 수 있어 열 때마다 새로 만든다.
        private void BuildCropButtons()
        {
            foreach ((CropDefinition _, Button button) in _cropButtons)
            {
                // Destroy는 프레임 끝에 반영되므로, 그 전에 레이아웃에서 먼저 뺀다.
                button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }
            _cropButtons.Clear();

            CropCatalog catalog = gameManager.CropCatalog;
            if (catalog == null || cropTemplate == null || cropGrid == null) return;

            foreach (CropDefinition crop in catalog.Crops)
            {
                if (crop == null) continue;
                Button button = Instantiate(cropTemplate, cropGrid);
                button.name = $"Crop_{crop.Id}";
                button.gameObject.SetActive(true);

                bool unlocked = gameManager.IsCropUnlocked(crop);
                button.interactable = unlocked;
                SetText(button, "Name", crop.DisplayName);
                SetText(button, "Detail", unlocked
                    ? string.Format(requirementFormat, crop.RequiredFocusMinutes, crop.RequiredSets)
                    : LockText(crop));

                string id = crop.Id;
                button.onClick.AddListener(() => SelectCrop(id));
                _cropButtons.Add((crop, button));
            }
        }

        private string LockText(CropDefinition crop)
        {
            return crop.UnlockCondition == UnlockCondition.HarvestCount
                ? string.Format(lockedHarvestFormat, crop.UnlockThreshold)
                : string.Format(lockedFocusFormat, crop.UnlockThreshold);
        }

        private void Refresh()
        {
            if (Draft == null) return;

            CropCatalog catalog = gameManager.CropCatalog;
            CropDefinition planted = catalog != null ? catalog.ChooseFor(Draft, gameManager.IsCropUnlocked) : null;

            foreach ((CropDefinition crop, Button button) in _cropButtons)
            {
                bool selected = crop.Id == Draft.CropType;
                if (button.targetGraphic != null) button.targetGraphic.color = selected ? selectedCropColor : cropColor;
                foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.color = selected ? selectedCropTextColor : cropTextColor;
                }
            }

            if (hintText == null) return;
            CropDefinition chosen = catalog != null ? catalog.Find(Draft.CropType) : null;
            if (chosen != null && planted != null && planted != chosen)
            {
                hintText.text = string.Format(fallbackFormat, chosen.DisplayName,
                    chosen.RequiredFocusMinutes, chosen.RequiredSets, planted.DisplayName);
            }
            else
            {
                int grade = (int)CropGradeRule.GetCropGrade(Draft);
                hintText.text = string.Format(gradeFormat, grade < gradeNames.Length ? gradeNames[grade] : grade.ToString());
            }
        }

        private static void SetText(Button button, string childName, string value)
        {
            Transform child = button.transform.Find(childName);
            if (child != null && child.TryGetComponent(out TMP_Text text)) text.text = value;
        }
    }
}
