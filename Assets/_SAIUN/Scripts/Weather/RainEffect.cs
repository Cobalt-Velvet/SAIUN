using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 비 (사양서 v1.1 10장). 카메라 앞에서 떨어지는 빗줄기로, 강도는 내리는 양, 바람은 기울기가 된다.
    /// 파티클은 카메라 자식으로 두고 카메라 기준으로 움직여서 화면에서 늘 위에서 아래로 떨어진다.
    /// </summary>
    public class RainEffect : MonoBehaviour
    {
        [SerializeField] private WeatherController weather;
        [SerializeField] private ParticleSystem rain;

        [Tooltip("강도 1일 때 초당 빗줄기 수")]
        [SerializeField, Min(0f)] private float maxDropsPerSecond = 420f;

        [Tooltip("떨어지는 속도(유닛/초, 화면 아래 방향)")]
        [SerializeField, Min(0f)] private float fallSpeed = 9f;

        [Tooltip("바람 세기 1당 옆으로 흐르는 속도(유닛/초)")]
        [SerializeField, Min(0f)] private float slantPerWind = 0.6f;

        /// <summary>지금 초당 빗줄기 수.</summary>
        public float DropsPerSecond { get; private set; }

        /// <summary>지금 빗줄기가 옆으로 흐르는 속도. 양수면 화면 오른쪽.</summary>
        public float Slant { get; private set; }

        private void Awake()
        {
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
            if (rain == null) rain = GetComponent<ParticleSystem>();
            if (rain != null && !rain.isPlaying) rain.Play();
        }

        private void Update()
        {
            Tick();
        }

        /// <summary>현재 날씨를 파티클에 옮긴다. 테스트는 직접 부른다.</summary>
        internal void Tick()
        {
            if (weather == null || rain == null) return;

            DropsPerSecond = weather.RainIntensity * maxDropsPerSecond;
            ParticleSystem.EmissionModule emission = rain.emission;
            emission.rateOverTime = DropsPerSecond;

            float across = Vector3.Dot(weather.WindDirection, SceneMetrics.CameraRotation * Vector3.right);
            Slant = across * weather.WindStrength * slantPerWind;

            // 세 축은 같은 곡선 모드여야 한다.
            ParticleSystem.VelocityOverLifetimeModule velocity = rain.velocityOverLifetime;
            velocity.x = Slant;
            velocity.y = -fallSpeed;
            velocity.z = 0f;
        }
    }
}
