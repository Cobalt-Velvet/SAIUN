using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 풍향계와 풍속계 (2026-09-19 사용자 요청: 바람의 세기와 방향을 보여 주는 것).
    /// 화살표는 바람이 불어오는 쪽을 가리키고(실제 풍향계처럼: 넓은 꼬리 깃이 바람에 밀려 바람 아래쪽으로 돈다),
    /// 세기에 따라 조금씩 떨린다. 낮은 구름도 같은 바람을 타고 화살표가 가리키는 쪽에서 흘러온다.
    /// 위의 컵 세 개는 바람이 셀수록 빨리 돈다. 바람이 오목한 입을 밀므로 볼록한 등을 앞세워 돈다
    /// (컵 입이 접선 +쪽을 보므로 위에서 보아 시계 방향, Y축 음의 방향).
    /// 방향은 관성을 두고 돌아 급하게 튀지 않는다.
    /// </summary>
    public class WeatherVane : MonoBehaviour
    {
        [SerializeField] private WeatherController weather;

        [Tooltip("수평으로 도는 화살표. 앞(+Z)이 화살촉이다.")]
        [SerializeField] private Transform vane;

        [Tooltip("세로축으로 도는 풍속계 컵")]
        [SerializeField] private Transform cups;

        [Header("화살표")]
        [Tooltip("새 방향을 따라가는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float turnSmoothTime = 1.2f;

        [Tooltip("바람 세기 1당 떨리는 각(도)")]
        [SerializeField, Min(0f)] private float flutterPerWind = 1.6f;

        [Tooltip("떨림 빠르기(초당 횟수)")]
        [SerializeField, Min(0f)] private float flutterFrequency = 1.7f;

        [Header("풍속계")]
        [Tooltip("바람이 없을 때 도는 빠르기(도/초)")]
        [SerializeField, Min(0f)] private float idleSpin = 12f;

        [Tooltip("바람 세기 1당 더해지는 빠르기(도/초)")]
        [SerializeField, Min(0f)] private float spinPerWind = 110f;

        /// <summary>지금 화살표가 가리키는 방향(도, 월드 Y축 회전). 흔들림은 뺀 값이다.</summary>
        public float Heading { get; private set; }

        /// <summary>지금 컵이 도는 빠르기(도/초, 크기). 도는 방향은 위에서 보아 시계 방향이다.</summary>
        public float SpinSpeed { get; private set; }

        private float _headingVelocity;
        private float _spinAngle;
        private bool _started;

        private void Awake()
        {
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
        }

        private void Update()
        {
            Tick(Time.deltaTime, Time.time);
        }

        /// <summary>한 프레임 분량. 테스트는 시간을 정해 직접 부른다.</summary>
        internal void Tick(float deltaTime, float time)
        {
            if (weather == null) return;

            // 화살촉은 바람이 불어오는 쪽(바람이 불어 가는 방향의 반대)을 가리킨다.
            Vector3 upwind = -weather.WindDirection;
            float target = Mathf.Atan2(upwind.x, upwind.z) * Mathf.Rad2Deg;
            if (!_started)
            {
                Heading = target;
                _started = true;
            }
            else
            {
                Heading = Mathf.SmoothDampAngle(Heading, target, ref _headingVelocity, turnSmoothTime, Mathf.Infinity, deltaTime);
            }

            float flutter = Mathf.Sin(time * flutterFrequency * Mathf.PI * 2f) * flutterPerWind * weather.WindStrength;
            if (vane != null) vane.rotation = Quaternion.Euler(0f, Heading + flutter, 0f);

            SpinSpeed = idleSpin + spinPerWind * weather.WindStrength;
            _spinAngle = Mathf.Repeat(_spinAngle - SpinSpeed * deltaTime, 360f);
            if (cups != null) cups.localRotation = Quaternion.Euler(0f, _spinAngle, 0f);
        }
    }
}
