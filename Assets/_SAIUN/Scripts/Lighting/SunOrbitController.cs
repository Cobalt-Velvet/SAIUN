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
    /// FOCUS 구간에서만 궤도를 갱신하고, 그 밖의 상태에서는 마지막 궤도를 유지한다.
    /// 쉬는 동안은 해가 진다(2026-09-28, 집중 한 번이 하루). 휴식 시간을 따라 천천히 져서 노을이 시간의 흐름대로 변한다
    /// (2026-09-29 사용자 "노을의 세기도 시간의 흐름에 따라"): 지평선의 금빛 → 해가 넘어가며 구름 밑이 붉게 타는 노을 →
    /// 해가 지평선 아래 3~4°일 때 해 진 쪽이 분홍·보랏빛으로 다시 달아오르는 박명광. 짧은 휴식은 여기서 끝나고,
    /// 긴 휴식(세션을 다 마침)은 더 내려가 별이 돋는 푸른 박명까지 간다. 빛이 어둡고 푸르게 가라앉고, 하늘은 Twilight를 읽는다.
    /// 세션이 끝나 시계로 돌아가면 다시 저녁으로, 집중이 시작되면 새 아침이다.
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

        [Header("박명 (쉬는 동안 해가 진다)")]
        [Tooltip("해가 가장 빨리 질 때 다 지기까지 걸리는 시간(분). 휴식이 짧아도 이보다 빨리 지거나 돌아오지 않는다.")]
        [SerializeField, Min(0.1f)] private float twilightMinutes = 2.5f;

        [Tooltip("짧은 휴식이 끝날 때 해가 진 정도(0~1). 해 진 쪽이 분홍·보랏빛으로 달아오르는 박명광에 닿는다.")]
        [SerializeField, Range(0f, 1f)] private float shortBreakTwilight = 0.45f;

        [Tooltip("긴 휴식에서 푸른 박명(Twilight 1)에 닿는 진행률. 그 뒤로는 별이 돋은 박명에 머문다.")]
        [SerializeField, Range(0.05f, 1f)] private float longBreakDusk = 0.6f;

        [Tooltip("다 진 뒤의 빛 세기 배율")]
        [SerializeField, Range(0f, 1f)] private float twilightIntensity = 0.3f;

        [Tooltip("다 진 뒤의 빛 색(하늘빛만 남은 푸른 빛)")]
        [SerializeField] private Color twilightColor = new Color(0.55f, 0.62f, 0.95f);

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

        /// <summary>해가 진 정도(0 저녁 ~ 1 박명이 깊음). 쉬는 동안 오른다.</summary>
        public float Twilight { get; private set; }

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
            if (timer == null) return;
            PomodoroState phase = timer.CurrentPhase;
            if (phase == PomodoroState.Focus)
            {
                Twilight = 0f;
                Apply(timer.Progress);
                return;
            }
            StepTwilight(TwilightGoal(phase, timer.Progress), Time.deltaTime);
        }

        // 쉬는 동안은 휴식 진행률을 따라 해가 지고, 시계로 돌아가면 저녁으로 돌아온다. 유예·실패는 집중 중의 일이라 그대로 둔다.
        private float TwilightGoal(PomodoroState phase, float phaseProgress)
        {
            switch (phase)
            {
                case PomodoroState.ShortBreak:
                    return shortBreakTwilight * Mathf.Clamp01(phaseProgress);
                case PomodoroState.LongBreak:
                    return Mathf.Clamp01(phaseProgress / longBreakDusk);
                case PomodoroState.Idle:
                    return 0f;
                default:
                    return Twilight;
            }
        }

        /// <summary>박명을 목표 쪽으로 한 걸음 옮기고 빛에 적용한다. 테스트는 직접 부른다.</summary>
        internal void StepTwilight(float goal, float deltaTime)
        {
            float next = Mathf.MoveTowards(Twilight, goal, deltaTime / (twilightMinutes * 60f));
            if (Mathf.Approximately(next, Twilight)) return;
            Twilight = next;
            Apply(Progress);
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
            sun.color = Color.Lerp(SunColor, twilightColor, Twilight);
            sun.intensity = intensity * Mathf.Lerp(horizonIntensity, 1f, Daylight(Progress)) * Mathf.Lerp(1f, twilightIntensity, Twilight);
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
