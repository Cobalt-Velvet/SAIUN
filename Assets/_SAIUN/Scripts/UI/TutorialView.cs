using System;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>튜토리얼 한 단계의 글과 가리킬 UI.</summary>
    [Serializable]
    public struct TutorialStep
    {
        [SerializeField] private string title;
        [SerializeField, TextArea] private string body;
        [Tooltip("이 단계에서 테두리로 가리킬 UI. 비우면 가리키지 않는다.")]
        [SerializeField] private RectTransform target;

        public TutorialStep(string title, string body, RectTransform target)
        {
            this.title = title;
            this.body = body;
            this.target = target;
        }

        public string Title => title;
        public string Body => body;
        public RectTransform Target => target;
    }

    /// <summary>
    /// 첫 실행 튜토리얼 오버레이 (사양서 v1.1 14장, P5-03).
    /// 단계마다 '다음'과 '건너뛰기'가 있고, 끝내거나 건너뛰면 PlayerPrefs에 완료로 남겨 다시 띄우지 않는다.
    /// 설정 화면의 '튜토리얼 다시 보기'로 처음부터 다시 볼 수 있다.
    /// 오버레이가 뒤의 클릭을 막아, 안내를 읽는 동안 세션이 실수로 시작되지 않게 한다.
    /// </summary>
    public class TutorialView : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private GameManager gameManager;

        [Tooltip("열고 닫는 오버레이 본체")]
        [SerializeField] private GameObject overlay;

        [SerializeField] private TMP_Text stepText;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private Button nextButton;
        [SerializeField] private TMP_Text nextLabel;
        [SerializeField] private Button skipButton;

        [Tooltip("가리키는 UI를 두르는 테두리")]
        [SerializeField] private RectTransform highlight;

        [Header("단계")]
        [SerializeField] private TutorialStep[] steps = Array.Empty<TutorialStep>();

        [Header("문구")]
        [SerializeField] private string stepFormat = "{0} / {1}";
        [SerializeField] private string nextText = "다음";
        [SerializeField] private string finishText = "시작하기";

        [Header("테두리")]
        [Tooltip("가리키는 UI보다 이만큼 크게 두른다(픽셀)")]
        [SerializeField, Min(0f)] private float highlightPadding = 6f;

        [Tooltip("테두리가 숨 쉬듯 밝아졌다 어두워지는 주기(초)")]
        [SerializeField, Min(0.1f)] private float highlightPeriod = 1.4f;

        [SerializeField, Range(0f, 1f)] private float highlightMinAlpha = 0.35f;

        /// <summary>오버레이가 떠 있는지.</summary>
        public bool IsShowing => overlay != null && overlay.activeSelf;

        /// <summary>지금 단계(0부터). 떠 있지 않으면 -1.</summary>
        public int StepIndex { get; private set; } = -1;

        public int StepCount => steps.Length;

        private Graphic _highlightGraphic;
        private readonly Vector3[] _corners = new Vector3[4];

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (overlay != null) overlay.SetActive(false);
            if (highlight != null) _highlightGraphic = highlight.GetComponent<Graphic>();

            if (nextButton != null) nextButton.onClick.AddListener(Next);
            if (skipButton != null) skipButton.onClick.AddListener(Finish);
        }

        private void OnEnable()
        {
            if (gameManager != null) gameManager.OnTutorialRequested += Show;
        }

        private void OnDisable()
        {
            if (gameManager != null) gameManager.OnTutorialRequested -= Show;
        }

        private void Start()
        {
            // 최초 실행에만 띄운다.
            if (!SettingsStore.TutorialCompleted) Show();
        }

        private void LateUpdate()
        {
            if (!IsShowing || highlight == null || !highlight.gameObject.activeSelf) return;

            // 가리키는 UI가 레이아웃으로 움직여도 따라가고, 은은하게 깜빡인다.
            FitHighlight(steps[StepIndex].Target);
            if (_highlightGraphic != null)
            {
                float wave = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime / highlightPeriod * Mathf.PI * 2f);
                Color color = _highlightGraphic.color;
                color.a = Mathf.Lerp(highlightMinAlpha, 1f, wave);
                _highlightGraphic.color = color;
            }
        }

        // ---- 공개 API ----

        /// <summary>처음 단계부터 보여 준다.</summary>
        public void Show()
        {
            if (overlay == null || steps.Length == 0) return;
            overlay.SetActive(true);
            ShowStep(0);
        }

        /// <summary>다음 단계로. 마지막 단계면 끝낸다.</summary>
        public void Next()
        {
            if (!IsShowing) return;
            if (StepIndex + 1 < steps.Length) ShowStep(StepIndex + 1);
            else Finish();
        }

        /// <summary>끝내거나 건너뛴다. 어느 쪽이든 완료로 남긴다(사양서 14장).</summary>
        public void Finish()
        {
            SettingsStore.TutorialCompleted = true;
            StepIndex = -1;
            if (overlay != null) overlay.SetActive(false);
        }

        // ---- 내부 ----

        private void ShowStep(int index)
        {
            StepIndex = index;
            TutorialStep step = steps[index];

            if (stepText != null) stepText.text = string.Format(stepFormat, index + 1, steps.Length);
            if (titleText != null) titleText.text = step.Title;
            if (bodyText != null) bodyText.text = step.Body;
            if (nextLabel != null) nextLabel.text = index == steps.Length - 1 ? finishText : nextText;

            if (highlight == null) return;
            bool pointing = step.Target != null && step.Target.gameObject.activeInHierarchy;
            highlight.gameObject.SetActive(pointing);
            if (pointing) FitHighlight(step.Target);
        }

        // 가리키는 UI의 네 모서리를 화면 좌표를 거쳐 테두리의 부모 좌표로 옮겨 그 사각형에 맞춘다.
        // 하단 바처럼 다른(카메라 공간) 캔버스에 있는 UI도 가리키므로 월드 좌표를 그대로 쓰지 않는다.
        private void FitHighlight(RectTransform target)
        {
            if (target == null) return;
            var parent = (RectTransform)highlight.parent;
            target.GetWorldCorners(_corners);
            Camera targetCamera = CanvasCamera(target);
            Camera ownCamera = CanvasCamera(parent);
            Vector2 min = ToLocal(parent, _corners[0], targetCamera, ownCamera);
            Vector2 max = ToLocal(parent, _corners[2], targetCamera, ownCamera);

            highlight.anchorMin = highlight.anchorMax = parent.pivot;
            highlight.pivot = new Vector2(0.5f, 0.5f);
            highlight.anchoredPosition = (min + max) / 2f;
            highlight.sizeDelta = max - min + Vector2.one * (highlightPadding * 2f);
        }

        private static Vector2 ToLocal(RectTransform parent, Vector3 world, Camera from, Camera to)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(from, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, to, out Vector2 local);
            return local;
        }

        // 오버레이 캔버스는 카메라가 없다(null). 카메라 공간 캔버스는 그 카메라로 화면에 옮긴다.
        private static Camera CanvasCamera(Transform element)
        {
            Canvas canvas = element.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            canvas = canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }
    }
}
