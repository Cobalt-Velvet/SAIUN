using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Timer;
using UnityEngine;

namespace _SAIUN.Scripts.Lighting
{
    /// <summary>
    /// 포모도로 진행률을 Directional Light의 궤도로 옮긴다 (사양서 v1.1 4장).
    /// 수평각은 진행률 0 → 동쪽(+70°), 0.5 → 정수리(0°), 1 → 서쪽(−70°)으로 선형이다.
    /// 수직 고도는 아침·저녁에 낮고 한낮에 높아, 그림자가 길어졌다 짧아졌다 한다(4-1 "낮고 긴 그림자"·"짧은 그림자").
    /// 이 고도 변화는 사양서 10장 구현 금지 목록에 있었지만 2026-09-19 사용자 지시로 켰다. 끄면 45° 고정이다.
    /// 빛 색도 진행률을 따라 아침·저녁에 따뜻해지며, 구름 등이 이 색과 방향을 읽는다.
    /// FOCUS 구간에서만 갱신하고, 그 밖의 상태에서는 마지막 궤도를 유지한다.
    /// </summary>
    public class SunOrbitController : MonoBehaviour
    {
        // ---- 확정값 (사양서 8장) ----
        public const float StartHorizontalAngle = 70f;
        public const float EndHorizontalAngle = -70f;

        /// <summary>고도 변화를 끌 때 쓰는 사양서 고정 고도.</summary>
        public const float VerticalAngle = 45f;

        [SerializeField] private Light sun;
        [SerializeField] private PomodoroTimer timer;

        [Header("고도 (그림자 길이)")]
        [Tooltip("끄면 사양서의 45° 고정으로 돌아간다")]
        [SerializeField] private bool varyElevation = true;

        [Tooltip("진행률 0·1(해 뜰 때·질 때)의 고도. 낮을수록 그림자가 길다.")]
        [SerializeField, Range(5f, 85f)] private float horizonElevation = 22f;

        [Tooltip("진행률 0.5(한낮)의 고도")]
        [SerializeField, Range(5f, 90f)] private float noonElevation = 68f;

        [Header("빛 색·세기 (실측 대기)")]
        [Tooltip("진행률에 따른 빛 색. 아침·저녁은 따뜻하게, 한낮은 희게.")]
        [SerializeField] private Gradient sunColor = DefaultSunColor();

        [Tooltip("한낮의 빛 세기. 광원의 세기는 이 값에서 계산하므로 광원 쪽 값을 직접 고치지 않는다.")]
        [SerializeField, Min(0f)] private float intensity = 1f;

        [Tooltip("해 뜰 때·질 때의 세기 배율. 한낮은 1이다.")]
        [SerializeField, Range(0f, 1f)] private float horizonIntensity = 0.8f;

        [Header("실측용 미리보기")]
        [Tooltip("에디터에서 이 값을 움직이면 그 진행률의 광원이 즉시 적용된다. 실행 중에는 무시한다.")]
        [SerializeField, Range(0f, 1f)] private float previewProgress;

        /// <summary>현재 적용된 수평각(도).</summary>
        public float HorizontalAngle { get; private set; } = StartHorizontalAngle;

        /// <summary>현재 적용된 고도(도).</summary>
        public float Elevation { get; private set; } = VerticalAngle;

        /// <summary>현재 진행률(0~1).</summary>
        public float Progress { get; private set; }

        /// <summary>현재 빛 색. 세기 배율은 곱하지 않은 색이다.</summary>
        public Color SunColor { get; private set; } = Color.white;

        /// <summary>빛이 나아가는 방향(월드).</summary>
        public Vector3 LightDirection => Quaternion.Euler(Elevation, HorizontalAngle, 0f) * Vector3.forward;

        private void Awake()
        {
            if (sun == null) sun = GetComponent<Light>();
            if (timer == null) timer = FindFirstObjectByType<PomodoroTimer>();

            if (sun == null) Debug.LogError("SunOrbitController: Light 참조가 없습니다.");
            if (timer == null) Debug.LogError("SunOrbitController: PomodoroTimer를 찾지 못했습니다.");
        }

#if UNITY_EDITOR
        // 진행률에 따른 그림자·빛 색을 재생 없이 비교하기 위한 실측 도구.
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            if (sun == null) sun = GetComponent<Light>();
            Apply(previewProgress);
        }
#endif

        private void Start()
        {
            // 세션 밖에서는 아침 위치로 둔다.
            Apply(0f);
        }

        private void Update()
        {
            if (timer == null || timer.CurrentPhase != PomodoroState.Focus) return;
            Apply(timer.Progress);
        }

        /// <summary>진행률(0~1)에 맞는 궤도·색을 광원에 적용한다.</summary>
        public void Apply(float progress)
        {
            Progress = Mathf.Clamp01(progress);
            HorizontalAngle = HorizontalAngleFor(Progress);
            Elevation = varyElevation ? ElevationFor(Progress, horizonElevation, noonElevation) : VerticalAngle;
            SunColor = sunColor != null ? sunColor.Evaluate(Progress) : Color.white;

            if (sun == null) return;
            sun.transform.rotation = Quaternion.Euler(Elevation, HorizontalAngle, 0f);
            sun.color = SunColor;
            sun.intensity = intensity * Mathf.Lerp(horizonIntensity, 1f, Daylight(Progress));
        }

        /// <summary>사양서 4장 수식. 범위 밖 진행률은 0~1로 자른다.</summary>
        public static float HorizontalAngleFor(float progress)
        {
            return Mathf.Lerp(StartHorizontalAngle, EndHorizontalAngle, Mathf.Clamp01(progress));
        }

        /// <summary>해의 고도. 양 끝에서 horizon, 한가운데서 noon인 반원 궤도다.</summary>
        public static float ElevationFor(float progress, float horizon, float noon)
        {
            return Mathf.Lerp(horizon, noon, Daylight(progress));
        }

        // 0(해 뜰 때·질 때) ~ 1(한낮). 반원 궤도의 높이다.
        private static float Daylight(float progress)
        {
            return Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
        }

        // 아침·저녁은 복숭아빛, 한낮은 흰빛. 팔레트의 Eggshell 쪽으로 기운 따뜻한 색이다.
        private static Gradient DefaultSunColor()
        {
            var warm = new Color(1f, 0.84f, 0.7f);
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(warm, 0f),
                    new GradientColorKey(Color.white, 0.35f),
                    new GradientColorKey(Color.white, 0.65f),
                    new GradientColorKey(warm, 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
