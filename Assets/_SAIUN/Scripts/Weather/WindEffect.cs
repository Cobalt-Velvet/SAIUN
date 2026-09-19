using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 눈에 보이는 바람: 바람결(가느다란 빛줄기)과 강풍에 날리는 잎 (2026-09-19 사용자 요청).
    /// 바람이 세질수록 결이 많아지고 빨라지며, 일정 세기를 넘으면 잎이 화면을 가로질러 날아간다.
    /// 두 파티클은 카메라 자식이라 화면 기준으로 흐르고, 방향은 바람의 화면 가로 성분을 따른다.
    /// 바람결은 꼬리(Trail)를 끄는 알갱이라 빠를수록 길게, 난류로 굽이치며 흐른다.
    /// </summary>
    public class WindEffect : MonoBehaviour
    {
        [SerializeField] private WeatherController weather;
        [SerializeField] private ParticleSystem streaks;
        [SerializeField] private ParticleSystem leaves;

        [Tooltip("화면 가운데에서 바람이 오는 쪽 가장자리까지 거리(유닛). 잎은 이 밖에서 생겨 화면을 가로지른다.")]
        [SerializeField, Min(0f)] private float edgeOffset = 2.7f;

        [Tooltip("화면 안쪽·바깥쪽으로 부는 바람도 가로로 이만큼은 흐르게 한다(비율)")]
        [SerializeField, Range(0f, 1f)] private float minSideways = 0.35f;

        [Header("바람결")]
        [Tooltip("이 세기부터 바람결이 보인다(0~5)")]
        [SerializeField, Range(0f, 5f)] private float streakThreshold = 1f;

        [Tooltip("세기 5일 때 초당 바람결 수")]
        [SerializeField, Min(0f)] private float maxStreaksPerSecond = 14f;

        [Tooltip("바람 세기 1당 바람결이 흐르는 빠르기(유닛/초)")]
        [SerializeField, Min(0f)] private float streakSpeedPerWind = 0.9f;

        [Header("잎")]
        [Tooltip("이 세기부터 잎이 날린다(0~5)")]
        [SerializeField, Range(0f, 5f)] private float leafThreshold = 3f;

        [Tooltip("세기 5일 때 초당 잎 수")]
        [SerializeField, Min(0f)] private float maxLeavesPerSecond = 3f;

        [Tooltip("바람 세기 1당 잎이 날아가는 빠르기(유닛/초)")]
        [SerializeField, Min(0f)] private float leafSpeedPerWind = 0.55f;

        /// <summary>지금 초당 바람결 수.</summary>
        public float StreaksPerSecond { get; private set; }

        /// <summary>지금 초당 잎 수.</summary>
        public float LeavesPerSecond { get; private set; }

        /// <summary>바람의 화면 가로 방향. 양수면 오른쪽으로 분다.</summary>
        public float ScreenDirection { get; private set; } = 1f;

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

            float across = Vector3.Dot(weather.WindDirection, SceneMetrics.CameraRotation * Vector3.right);
            // 화면 안쪽으로 부는 바람도 가로로 조금은 흐르게, 방향만 따른다.
            ScreenDirection = across >= 0f ? 1f : -1f;
            float strength = weather.WindStrength;
            float sideways = Mathf.Lerp(minSideways, 1f, Mathf.Abs(across));

            StreaksPerSecond = Amount(strength, streakThreshold) * maxStreaksPerSecond;
            LeavesPerSecond = Amount(strength, leafThreshold) * maxLeavesPerSecond;

            // 바람결은 카드 전체에서 생겨 잠깐 흐르다 사라지고, 잎은 가장자리에서 들어와 끝까지 건넌다.
            Drive(streaks, StreaksPerSecond, ScreenDirection * strength * streakSpeedPerWind * sideways, 0f);
            Drive(leaves, LeavesPerSecond, ScreenDirection * strength * leafSpeedPerWind * sideways, edgeOffset);
        }

        // 문턱을 넘은 만큼을 0~1로. 세기 5에서 1이다.
        private static float Amount(float strength, float threshold)
        {
            if (strength <= threshold) return 0f;
            return Mathf.Clamp01((strength - threshold) / (WeatherController.MaxWindStrength - threshold));
        }

        // 바람이 불어오는 쪽 가장자리에서 생겨 반대쪽으로 흐르게 발생 위치와 속도를 맞춘다.
        private static void Drive(ParticleSystem system, float rate, float speed, float edge)
        {
            if (system == null) return;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate;

            // 바람이 문턱 아래로 잦아들면 새로 만들지만 않는다. 떠 있던 것은 마지막 바람을 타고 빠져나가게 둔다(허공에 멈추지 않게).
            if (rate <= 0f) return;

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.x = speed;
            velocity.y = 0f;
            velocity.z = 0f;

            // 발생 상자(세로로 긴 띠)를 바람이 오는 쪽 가장자리로 옮겨 화면을 가로지르게 한다.
            ParticleSystem.ShapeModule shape = system.shape;
            Vector3 position = shape.position;
            position.x = speed >= 0f ? -edge : edge;
            shape.position = position;
        }
    }
}
