using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 눈에 보이는 바람: 바람결(가느다란 빛줄기)과 강풍에 날리는 잎 (2026-09-19 사용자 요청).
    /// 바람이 세질수록 결이 많아지고 빨라지며, 일정 세기를 넘으면 잎이 날아간다.
    /// 둘 다 세상 공간에서 실제 바람 방향(WeatherController.WindDirection)으로 흐른다(2026-09-29 사용자 "실제 바람 방향과
    /// 무관한가?"): 예전엔 화면 가로 방향의 부호만 따라 바다 쪽으로 부는 바람도 옆으로 흘렀다. 이제 풍향계·낮은 구름·
    /// 물결과 같은 바람을 타서, 바다 쪽 바람이면 멀어지며 작아지고 나 쪽 바람이면 다가온다.
    /// 흐르는 곳은 데크 앞 모래밭·바다 위 눈높이 아래 구역이라 하늘에는 긋지 않는다(씬 조립기가 구역을 잡는다).
    /// 잎은 구역의 바람이 불어오는 쪽 가장자리에서 들어와 끝까지 건넌다. 바람결은 구역 어디서나 잠깐 흐르다 사라진다.
    /// </summary>
    public class WindEffect : MonoBehaviour
    {
        [SerializeField] private WeatherController weather;
        [SerializeField] private ParticleSystem streaks;
        [SerializeField] private ParticleSystem leaves;

        [Tooltip("구역 가운데에서 바람이 불어오는 쪽 가장자리까지 거리(월드). 잎은 여기서 들어온다.")]
        [SerializeField, Min(0f)] private float reach = 6f;

        [Tooltip("잎이 들어오는 띠의 폭(바람을 가로지르는 길이, 월드)")]
        [SerializeField, Min(0f)] private float leafFrontWidth = 12f;

        [Header("바람결")]
        [Tooltip("이 세기부터 바람결이 보인다(0~5)")]
        [SerializeField, Range(0f, 5f)] private float streakThreshold = 1f;

        [Tooltip("세기 5일 때 초당 바람결 수")]
        [SerializeField, Min(0f)] private float maxStreaksPerSecond = 14f;

        [Tooltip("바람 세기 1당 바람결이 흐르는 빠르기(월드/초)")]
        [SerializeField, Min(0f)] private float streakSpeedPerWind = 0.9f;

        [Header("잎")]
        [Tooltip("이 세기부터 잎이 날린다(0~5)")]
        [SerializeField, Range(0f, 5f)] private float leafThreshold = 3f;

        [Tooltip("세기 5일 때 초당 잎 수")]
        [SerializeField, Min(0f)] private float maxLeavesPerSecond = 3f;

        [Tooltip("바람 세기 1당 잎이 날아가는 빠르기(월드/초)")]
        [SerializeField, Min(0f)] private float leafSpeedPerWind = 0.55f;

        /// <summary>지금 초당 바람결 수.</summary>
        public float StreaksPerSecond { get; private set; }

        /// <summary>지금 초당 잎 수.</summary>
        public float LeavesPerSecond { get; private set; }

        /// <summary>바람결·잎이 흐르는 방향(월드 수평, 단위 벡터). 바람이 불어 가는 쪽이다.</summary>
        public Vector3 FlowDirection { get; private set; } = Vector3.forward;

        private void Awake()
        {
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
            if (streaks != null && !streaks.isPlaying) streaks.Play();
            if (leaves != null && !leaves.isPlaying) leaves.Play();
        }

        private void Update()
        {
            Tick();
        }

        /// <summary>현재 바람을 파티클에 옮긴다. 테스트는 직접 부른다.</summary>
        internal void Tick()
        {
            if (weather == null) return;

            Vector3 flat = Vector3.ProjectOnPlane(weather.WindDirection, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f) FlowDirection = flat.normalized;
            float strength = weather.WindStrength;

            StreaksPerSecond = Amount(strength, streakThreshold) * maxStreaksPerSecond;
            LeavesPerSecond = Amount(strength, leafThreshold) * maxLeavesPerSecond;

            // 바람결은 구역 어디서나 생기고, 잎은 바람이 불어오는 쪽 가장자리의 띠에서 들어온다.
            Drive(streaks, StreaksPerSecond, FlowDirection * (strength * streakSpeedPerWind), false);
            Drive(leaves, LeavesPerSecond, FlowDirection * (strength * leafSpeedPerWind), true);
        }

        // 문턱을 넘은 만큼을 0~1로. 세기 5에서 1이다.
        private static float Amount(float strength, float threshold)
        {
            if (strength <= threshold) return 0f;
            return Mathf.Clamp01((strength - threshold) / (WeatherController.MaxWindStrength - threshold));
        }

        private void Drive(ParticleSystem system, float rate, Vector3 velocity, bool enterFromUpwind)
        {
            if (system == null) return;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate;

            // 바람이 문턱 아래로 잦아들면 새로 만들지만 않는다. 떠 있던 것은 마지막 바람을 타고 빠져나가게 둔다(허공에 멈추지 않게).
            if (rate <= 0f) return;

            ParticleSystem.VelocityOverLifetimeModule flow = system.velocityOverLifetime;
            flow.space = ParticleSystemSimulationSpace.World;
            flow.x = velocity.x;
            flow.y = 0f;
            flow.z = velocity.z;

            if (!enterFromUpwind) return;

            // 들어오는 띠를 바람이 불어오는 쪽으로 옮기고, 바람을 가로지르게 눕힌다.
            ParticleSystem.ShapeModule shape = system.shape;
            Vector3 entry = -FlowDirection * reach;
            shape.position = new Vector3(entry.x, shape.position.y, entry.z);
            shape.rotation = new Vector3(0f, Mathf.Atan2(FlowDirection.x, FlowDirection.z) * Mathf.Rad2Deg, 0f);
            Vector3 scale = shape.scale;
            shape.scale = new Vector3(leafFrontWidth, scale.y, scale.z);
        }
    }
}
