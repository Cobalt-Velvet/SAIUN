using System;
using _SAIUN.Scripts.Core;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>지금 화면에 걸린 시각 알림 종류.</summary>
    public enum ScreenAlert
    {
        None,
        BreakStart,
        SessionComplete,
        Distraction,
        CropDeath,
    }

    /// <summary>나타났다 머물렀다 사라지는 한 번의 세기 곡선. 시간 단위는 초.</summary>
    [Serializable]
    public struct AlertPulse
    {
        [SerializeField, Min(0f)] private float fadeIn;
        [SerializeField, Min(0f)] private float hold;
        [SerializeField, Min(0f)] private float fadeOut;
        [SerializeField, Range(0f, 1f)] private float peak;

        public AlertPulse(float fadeIn, float hold, float fadeOut, float peak)
        {
            this.fadeIn = fadeIn;
            this.hold = hold;
            this.fadeOut = fadeOut;
            this.peak = peak;
        }

        public float Duration => fadeIn + hold + fadeOut;

        /// <summary>시작 후 t초의 세기(0~peak). 곡선 밖이면 0.</summary>
        public float Evaluate(float t)
        {
            if (t < 0f || t >= Duration) return 0f;
            if (t < fadeIn) return peak * Mathf.SmoothStep(0f, 1f, t / fadeIn);
            if (t < fadeIn + hold) return peak;
            return peak * (1f - Mathf.SmoothStep(0f, 1f, (t - fadeIn - hold) / fadeOut));
        }
    }

    /// <summary>
    /// 화면 테두리 발광·점멸과 화면 어둡게 하기 (사양서 v1.1 13-2, P5-02).
    /// 휴식 시작은 부드러운 발광, 포모도로 완료는 밝은 발광, 방해 앱 유예 중에는 붉은 점멸,
    /// 작물이 죽으면 화면 전체가 잠시 어두워진다. 상태머신을 구독하기만 한다.
    /// 클릭을 가로채지 않도록 두 그래픽 모두 레이캐스트를 끈다.
    /// </summary>
    public class ScreenAlertView : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private GameManager gameManager;

        [Tooltip("창 가장자리 안쪽으로 번지는 빛")]
        [SerializeField] private RawImage edgeGlow;

        [Tooltip("화면 전체를 덮는 어두운 막")]
        [SerializeField] private Image dimOverlay;

        [Header("가장자리 빛 모양")]
        [Tooltip("가장자리에서 안쪽으로 빛이 번지는 폭(픽셀)")]
        [SerializeField, Range(4f, 80f)] private float glowWidth = 26f;

        [Tooltip("번짐이 줄어드는 곡선. 클수록 가장자리에 몰린다.")]
        [SerializeField, Range(0.5f, 4f)] private float glowFalloff = 2f;

        [Tooltip("모서리 반경(픽셀). 유리 테두리와 맞춘다.")]
        [SerializeField, Range(0f, 24f)] private float cornerRadius = 8f;

        [Header("휴식 시작: 부드러운 발광")]
        [SerializeField] private Color breakColor = SaiunPalette.BreakAccent;
        [SerializeField] private AlertPulse breakPulse = new AlertPulse(0.8f, 0.4f, 1.6f, 0.75f);

        [Header("포모도로 완료: 발광")]
        [SerializeField] private Color completeColor = SaiunPalette.Harvestable;
        [SerializeField] private AlertPulse completePulse = new AlertPulse(0.4f, 1.4f, 2f, 1f);

        [Header("방해 앱 경고: 붉은 점멸 (유예 동안 지속)")]
        [SerializeField] private Color warningColor = SaiunPalette.Warning;
        [SerializeField, Min(0.1f)] private float blinkPeriod = 0.9f;
        [SerializeField, Range(0f, 1f)] private float blinkMinAlpha = 0.2f;
        [SerializeField, Range(0f, 1f)] private float blinkMaxAlpha = 1f;
        [Tooltip("유예가 끝났을 때 점멸이 사라지는 시간(초)")]
        [SerializeField, Min(0f)] private float blinkFadeOut = 0.35f;

        [Header("작물 사망: 화면 어둡게")]
        [SerializeField] private Color dimColor = SaiunPalette.Charcoal;
        [SerializeField] private AlertPulse dimPulse = new AlertPulse(0.35f, 0.9f, 1.4f, 0.6f);

        /// <summary>지금 가장자리 빛을 차지한 알림. 사망 어둡게 하기는 따로 겹칠 수 있다.</summary>
        public ScreenAlert ActiveGlow { get; private set; } = ScreenAlert.None;

        public float GlowAlpha { get; private set; }
        public Color GlowColor { get; private set; }
        public float DimAlpha { get; private set; }

        private Func<float> _clock = () => Time.unscaledTime;
        private Texture2D _texture;
        private Vector2Int _size = new Vector2Int(SceneMetrics.WindowWidth, SceneMetrics.WindowHeight);
        private float _glowStart;
        private AlertPulse _glowPulse;
        private float _fadeStart;
        private float _fadeFrom;
        private bool _dimming;
        private float _dimStart;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogError("ScreenAlertView: GameManager를 찾지 못했습니다.");
                enabled = false;
                return;
            }

            if (edgeGlow != null)
            {
                edgeGlow.raycastTarget = false;
                BuildGlowTexture();
            }
            if (dimOverlay != null) dimOverlay.raycastTarget = false;

            Apply();
        }

        private void OnEnable()
        {
            if (gameManager == null) return;
            gameManager.StateMachine.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (gameManager == null) return;
            gameManager.StateMachine.OnStateChanged -= HandleStateChanged;
        }

        private void OnDestroy()
        {
            if (_texture == null) return;
            if (Application.isPlaying) Destroy(_texture);
            else DestroyImmediate(_texture);
        }

        private void Update()
        {
            Tick();
        }

        // ---- 이벤트 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            // 점멸은 유예 동안만 유지하고, 벗어나면 그 자리에서 서서히 끈다.
            if (from == PomodoroState.Interrupted && to != PomodoroState.Interrupted) StopBlink();

            switch (to)
            {
                case PomodoroState.ShortBreak:
                    StartPulse(ScreenAlert.BreakStart, breakPulse, breakColor);
                    break;

                case PomodoroState.LongBreak:
                    StartPulse(ScreenAlert.SessionComplete, completePulse, completeColor);
                    break;

                case PomodoroState.Interrupted:
                    ActiveGlow = ScreenAlert.Distraction;
                    GlowColor = warningColor;
                    _glowStart = _clock();
                    break;

                case PomodoroState.Failed:
                    _dimming = true;
                    _dimStart = _clock();
                    break;
            }

            Tick();
        }

        // ---- 세기 계산 ----

        /// <summary>현재 시각에 맞춰 세기를 계산하고 그래픽에 반영한다. 테스트는 가짜 시계와 함께 직접 부른다.</summary>
        internal void Tick()
        {
            float now = _clock();

            switch (ActiveGlow)
            {
                case ScreenAlert.Distraction:
                    // 들어오는 순간 가장 밝게 번쩍이고 주기마다 다시 밝아진다.
                    float wave = 0.5f + 0.5f * Mathf.Cos((now - _glowStart) / blinkPeriod * Mathf.PI * 2f);
                    GlowAlpha = Mathf.Lerp(blinkMinAlpha, blinkMaxAlpha, wave);
                    break;

                case ScreenAlert.BreakStart:
                case ScreenAlert.SessionComplete:
                    float elapsed = now - _glowStart;
                    GlowAlpha = _glowPulse.Evaluate(elapsed);
                    if (elapsed >= _glowPulse.Duration) ActiveGlow = ScreenAlert.None;
                    break;

                default:
                    float fade = blinkFadeOut > 0f ? (now - _fadeStart) / blinkFadeOut : 1f;
                    GlowAlpha = fade < 1f ? _fadeFrom * (1f - fade) : 0f;
                    break;
            }

            if (_dimming)
            {
                float elapsed = now - _dimStart;
                DimAlpha = dimPulse.Evaluate(elapsed);
                if (elapsed >= dimPulse.Duration) _dimming = false;
            }
            else
            {
                DimAlpha = 0f;
            }

            Apply();
        }

        /// <summary>테스트용 시계 교체. null이면 실제 시간으로 되돌린다.</summary>
        internal void SetClock(Func<float> clock)
        {
            _clock = clock ?? (() => Time.unscaledTime);
        }

        private void StartPulse(ScreenAlert alert, AlertPulse pulse, Color color)
        {
            ActiveGlow = alert;
            GlowColor = color;
            _glowPulse = pulse;
            _glowStart = _clock();
        }

        private void StopBlink()
        {
            if (ActiveGlow != ScreenAlert.Distraction) return;
            _fadeFrom = GlowAlpha;
            _fadeStart = _clock();
            ActiveGlow = ScreenAlert.None;
        }

        private void Apply()
        {
            if (edgeGlow != null)
            {
                // 보이지 않을 때는 그리지 않는다.
                edgeGlow.enabled = GlowAlpha > 0f;
                edgeGlow.color = SaiunPalette.WithAlpha(GlowColor, GlowAlpha);
            }

            if (dimOverlay != null)
            {
                dimOverlay.enabled = DimAlpha > 0f;
                dimOverlay.color = SaiunPalette.WithAlpha(dimColor, DimAlpha);
            }
        }

        // ---- 텍스처 ----

        /// <summary>창 크기(논리 화소)가 바뀌면 가장자리 빛을 다시 굽는다(카드 ↔ 사이드바).</summary>
        internal void Resize(Vector2Int size)
        {
            _size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
            if (edgeGlow == null) return;
            if (_texture != null)
            {
                if (Application.isPlaying) Destroy(_texture);
                else DestroyImmediate(_texture);
                _texture = null;
            }
            BuildGlowTexture();
        }

        // 가장자리에서 안쪽으로 옅어지는 흰 빛. 색은 RawImage.color로 입힌다. 창 크기가 바뀔 때만 다시 만든다.
        private void BuildGlowTexture()
        {
            int width = _size.x;
            int height = _size.y;
            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "ScreenAlertGlow",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[width * height];
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float radius = Mathf.Min(cornerRadius, Mathf.Min(halfWidth, halfHeight));

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float inset = -RoundedBoxDistance(x + 0.5f - halfWidth, y + 0.5f - halfHeight, halfWidth, halfHeight, radius);
                    float edge = Mathf.Clamp01(inset + 0.5f);
                    float glow = Mathf.Pow(1f - Mathf.Clamp01(inset / glowWidth), glowFalloff);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(edge * glow * 255f));
                }
            }

            _texture.SetPixels32(pixels);
            _texture.Apply(false, true);
            edgeGlow.texture = _texture;
        }

        /// <summary>둥근 사각형의 부호 있는 거리. 안쪽이 음수, 경계가 0이다.</summary>
        private static float RoundedBoxDistance(float px, float py, float halfWidth, float halfHeight, float radius)
        {
            float qx = Mathf.Abs(px) - (halfWidth - radius);
            float qy = Mathf.Abs(py) - (halfHeight - radius);
            float outsideX = Mathf.Max(qx, 0f);
            float outsideY = Mathf.Max(qy, 0f);
            float outside = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }
    }
}
