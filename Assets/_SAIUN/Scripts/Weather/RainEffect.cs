using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 비 (사양서 v1.1 10장). 카메라 앞에서 떨어지는 빗줄기로, 강도는 내리는 양, 바람은 기울기가 된다.
    /// 파티클은 카메라 자식으로 두고 카메라 기준으로 움직여서 화면에서 늘 위에서 아래로 떨어진다.
    /// 화단 위에는 빗방울이 튀어 오른다.
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

        [Header("튀는 빗방울")]
        [Tooltip("빗방울이 튀는 파티클. 월드 공간에서 돈다.")]
        [SerializeField] private ParticleSystem splash;

        [Tooltip("빗방울이 떨어지는 면의 기준(화단). 이 트랜스폼의 로컬 XZ 사각형 위에 튄다.")]
        [SerializeField] private Transform splashArea;

        [Tooltip("튀는 면의 가로(X)·세로(Z) 크기(유닛)")]
        [SerializeField] private Vector2 splashSize = new Vector2(2.4f, 0.8f);

        [Tooltip("튀는 면의 높이(로컬 Y)")]
        [SerializeField] private float splashHeight = 0.24f;

        [Tooltip("강도 1일 때 초당 튀는 곳 수")]
        [SerializeField, Min(0f)] private float maxSplashesPerSecond = 18f;

        [Tooltip("한 곳에서 튀는 물방울 수")]
        [SerializeField, Min(1)] private int dropsPerSplash = 3;

        /// <summary>지금 초당 빗줄기 수.</summary>
        public float DropsPerSecond { get; private set; }

        /// <summary>지금 빗줄기가 옆으로 흐르는 속도. 양수면 화면 오른쪽.</summary>
        public float Slant { get; private set; }

        /// <summary>지금까지 튄 곳 수. 테스트와 확인용.</summary>
        public int SplashCount { get; private set; }

        private float _splashDebt;
        private bool _fitted;
        private Vector3 _baseLocalPosition;
        private float _baseLifetime;

        private void Awake()
        {
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
            if (rain == null) rain = GetComponent<ParticleSystem>();
            if (rain != null && !rain.isPlaying) rain.Play();
        }

        private void Update()
        {
            Tick();
            Splash(Time.deltaTime);
        }

        /// <summary>
        /// 화각이 카드보다 넓어진 배율(카드 1). 빗줄기가 넓어진 화면 위쪽 가장자리 밖에서 생겨 아래까지 떨어지게,
        /// 생기는 높이와 수명을 같은 배율로 늘린다(사이드바).
        /// </summary>
        internal void FitView(float viewScale)
        {
            if (rain == null) return;
            if (!_fitted)
            {
                _baseLocalPosition = transform.localPosition;
                _baseLifetime = rain.main.startLifetime.constant;
                _fitted = true;
            }
            transform.localPosition = new Vector3(_baseLocalPosition.x, _baseLocalPosition.y * viewScale, _baseLocalPosition.z);
            ParticleSystem.MainModule main = rain.main;
            main.startLifetime = _baseLifetime * viewScale;
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

        /// <summary>강도만큼 화단 위 아무 곳에 빗방울을 튀긴다. 테스트는 직접 부른다.</summary>
        internal void Splash(float deltaTime)
        {
            if (weather == null || splash == null || splashArea == null) return;

            // 한 프레임에 1보다 작은 몫은 다음 프레임으로 넘긴다.
            _splashDebt += weather.RainIntensity * maxSplashesPerSecond * deltaTime;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            while (_splashDebt >= 1f)
            {
                _splashDebt -= 1f;
                var local = new Vector3(
                    (Random.value - 0.5f) * splashSize.x,
                    splashHeight,
                    (Random.value - 0.5f) * splashSize.y);
                emit.position = splashArea.TransformPoint(local);
                splash.Emit(emit, dropsPerSplash);
                SplashCount++;
            }
        }
    }
}
