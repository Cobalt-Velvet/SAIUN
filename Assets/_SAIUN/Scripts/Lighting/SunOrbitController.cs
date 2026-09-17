using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Timer;
using UnityEngine;

namespace _SAIUN.Scripts.Lighting
{
    /// <summary>
    /// 포모도로 진행률을 Directional Light의 수평각으로 선형 매핑한다.
    /// 진행률 0 → 동쪽(+70°), 0.5 → 정수리(0°), 1 → 서쪽(−70°). 수직 고도는 45° 고정.
    /// FOCUS 구간에서만 갱신하고, 그 밖의 상태에서는 마지막 각도를 유지한다.
    /// </summary>
    public class SunOrbitController : MonoBehaviour
    {
        // ---- 확정값 (사양서 8장) ----
        public const float StartHorizontalAngle = 70f;
        public const float EndHorizontalAngle = -70f;
        public const float VerticalAngle = 45f;

        [SerializeField] private Light sun;
        [SerializeField] private PomodoroTimer timer;

        [Header("실측용 미리보기")]
        [Tooltip("에디터에서 이 값을 움직이면 그 진행률의 광원 각도가 즉시 적용된다. 실행 중에는 무시한다.")]
        [SerializeField, Range(0f, 1f)] private float previewProgress;

        /// <summary>현재 적용된 수평각(도).</summary>
        public float HorizontalAngle { get; private set; } = StartHorizontalAngle;

        private void Awake()
        {
            if (sun == null) sun = GetComponent<Light>();
            if (timer == null) timer = FindFirstObjectByType<PomodoroTimer>();

            if (sun == null) Debug.LogError("SunOrbitController: Light 참조가 없습니다.");
            if (timer == null) Debug.LogError("SunOrbitController: PomodoroTimer를 찾지 못했습니다.");
        }

#if UNITY_EDITOR
        // 진행률 0 / 0.5 / 1 세 시점의 그림자를 재생 없이 비교하기 위한 실측 도구.
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

        /// <summary>진행률(0~1)에 맞는 각도를 광원에 적용한다.</summary>
        public void Apply(float progress)
        {
            HorizontalAngle = HorizontalAngleFor(progress);
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(VerticalAngle, HorizontalAngle, 0f);
            }
        }

        /// <summary>사양서 4장 수식. 범위 밖 진행률은 0~1로 자른다.</summary>
        public static float HorizontalAngleFor(float progress)
        {
            return Mathf.Lerp(StartHorizontalAngle, EndHorizontalAngle, Mathf.Clamp01(progress));
        }
    }
}
