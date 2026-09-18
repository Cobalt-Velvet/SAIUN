using System;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 자연 현상의 상태: 바람·비·먹구름 (사양서 v1.1 10장, P2-05).
    /// 바람은 항상 불고 30~120초마다 방향과 세기(0~5)를 새로 정한다.
    /// 비는 날씨 트리거가 정한다. '랜덤'은 집중 세트가 시작될 때마다 비를 굴리고,
    /// '집중 상태 연동'은 방해 앱 유예에 들어가면 먹구름과 비로 바꾼다. 두 트리거는 함께 켤 수 있다.
    /// 값은 목표로 부드럽게 옮겨 가며, 구름·비·작물·캐릭터가 이 값을 읽어 표현한다.
    /// </summary>
    public class WeatherController : MonoBehaviour
    {
        // ---- 확정값 (사양서 v1.1 10장) ----
        public const float MaxWindStrength = 5f;
        public const float MinWindChangeSeconds = 30f;
        public const float MaxWindChangeSeconds = 120f;

        private const float FullTurnDegrees = 360f;

        [SerializeField] private PomodoroStateMachine stateMachine;

        [Header("바람")]
        [Tooltip("세기와 방향이 새 목표로 따라붙는 빠르기(1/초)")]
        [SerializeField, Min(0f)] private float windResponse = 0.3f;

        [Header("비: 랜덤 트리거")]
        [Tooltip("집중 세트가 시작될 때 비가 올 확률")]
        [SerializeField, Range(0f, 1f)] private float rainChancePerSet = 0.3f;

        [Tooltip("비가 올 때 강도 범위(0~1)")]
        [SerializeField] private Vector2 rainIntensityRange = new Vector2(0.35f, 1f);

        [Tooltip("비가 이어지는 시간 범위(초)")]
        [SerializeField] private Vector2 rainDurationRange = new Vector2(180f, 900f);

        [Tooltip("비 강도가 목표로 따라붙는 빠르기(1/초)")]
        [SerializeField, Min(0f)] private float rainResponse = 0.6f;

        [Header("집중 상태 연동")]
        [Tooltip("유예 중 먹구름에 딸려 오는 비 강도")]
        [SerializeField, Range(0f, 1f)] private float stormRainIntensity = 0.8f;

        [Tooltip("먹구름이 몰려오고 걷히는 빠르기(1/초)")]
        [SerializeField, Min(0f)] private float stormResponse = 1.5f;

        /// <summary>바람 세기 0~5.</summary>
        public float WindStrength { get; private set; }

        /// <summary>바람이 불어 가는 수평 방향(월드, 단위 벡터).</summary>
        public Vector3 WindDirection { get; private set; } = Vector3.forward;

        /// <summary>비 강도 0~1. 0이면 비가 없다.</summary>
        public float RainIntensity { get; private set; }

        /// <summary>먹구름 정도 0~1.</summary>
        public float Storminess { get; private set; }

        /// <summary>바람 세기를 0~1로 줄인 값. 표현 쪽에서 쓰기 편하게.</summary>
        public float WindAmount => WindStrength / MaxWindStrength;

        // 테스트용: 다음 바람 변화까지 남은 시간.
        internal float SecondsUntilWindChange => _nextWindChange - _clock();

        private Func<float> _clock = () => Time.time;
        private Func<float> _random = () => UnityEngine.Random.value;
        private float _windTarget;
        private float _windAngle;
        private float _windAngleTarget;
        private float _nextWindChange;
        private float _rainTarget;
        private float _rainEndsAt;
        private float _stormTarget;
        private bool _initialized;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (stateMachine == null) stateMachine = FindFirstObjectByType<PomodoroStateMachine>();
            if (stateMachine == null) Debug.LogError("WeatherController: PomodoroStateMachine을 찾지 못했습니다.");
            Initialize();
        }

        private void OnEnable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged -= HandleStateChanged;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        // ---- 갱신 ----

        /// <summary>한 프레임 분량의 갱신. 테스트는 가짜 시계와 함께 직접 부른다.</summary>
        internal void Tick(float deltaTime)
        {
            float now = _clock();
            if (now >= _nextWindChange) ChooseWind(now);

            float wind = Blend(windResponse, deltaTime);
            WindStrength = Mathf.Lerp(WindStrength, _windTarget, wind);
            _windAngle = Mathf.LerpAngle(_windAngle, _windAngleTarget, wind);
            WindDirection = Quaternion.Euler(0f, _windAngle, 0f) * Vector3.forward;

            if (_rainTarget > 0f && now >= _rainEndsAt) _rainTarget = 0f;
            float rainGoal = Mathf.Max(_rainTarget, _stormTarget * stormRainIntensity);
            RainIntensity = Mathf.Lerp(RainIntensity, rainGoal, Blend(rainResponse, deltaTime));
            Storminess = Mathf.Lerp(Storminess, _stormTarget, Blend(stormResponse, deltaTime));
        }

        /// <summary>테스트용 시계·난수 교체. null이면 원래 것으로 되돌린다.</summary>
        internal void SetSources(Func<float> clock, Func<float> random)
        {
            _clock = clock ?? (() => Time.time);
            _random = random ?? (() => UnityEngine.Random.value);
            _initialized = false;
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // 켜자마자 구름이 흐르도록 첫 바람은 바로 적용한다.
            ChooseWind(_clock());
            WindStrength = _windTarget;
            _windAngle = _windAngleTarget;
            WindDirection = Quaternion.Euler(0f, _windAngle, 0f) * Vector3.forward;
        }

        private void ChooseWind(float now)
        {
            _windTarget = Range(0f, MaxWindStrength);
            _windAngleTarget = Range(0f, FullTurnDegrees);
            _nextWindChange = now + Range(MinWindChangeSeconds, MaxWindChangeSeconds);
        }

        // ---- 트리거 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            float now = _clock();

            // 랜덤: 집중 세트가 새로 시작될 때마다 굴린다. 유예에서 돌아온 것은 새 세트가 아니다.
            if (to == PomodoroState.Focus && from != PomodoroState.Interrupted && SettingsStore.WeatherRandom)
            {
                RollRain(now);
            }

            // 집중 상태 연동: 유예에 들어가면 먹구름, 집중으로 돌아오면 걷힌다. 실패하면 세션이 끝날 때까지 남는다.
            if (SettingsStore.WeatherFocusLinked)
            {
                if (to == PomodoroState.Interrupted || to == PomodoroState.Failed) _stormTarget = 1f;
                else if (to == PomodoroState.Focus) _stormTarget = 0f;
            }

            // 세션이 끝나면 시계 위젯으로 돌아가므로 하늘을 갠다.
            if (to == PomodoroState.Idle)
            {
                _rainTarget = 0f;
                _stormTarget = 0f;
            }
        }

        private void RollRain(float now)
        {
            if (_random() < rainChancePerSet)
            {
                _rainTarget = Range(rainIntensityRange.x, rainIntensityRange.y);
                _rainEndsAt = now + Range(rainDurationRange.x, rainDurationRange.y);
            }
            else
            {
                _rainTarget = 0f;
            }
        }

        private float Range(float min, float max)
        {
            return Mathf.Lerp(min, max, _random());
        }

        // 프레임 길이와 무관하게 같은 빠르기로 목표에 다가가는 보간 비율.
        private static float Blend(float response, float deltaTime)
        {
            return 1f - Mathf.Exp(-response * deltaTime);
        }
    }
}
