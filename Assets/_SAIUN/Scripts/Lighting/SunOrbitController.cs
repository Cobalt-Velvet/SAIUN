using System;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Timer;
using UnityEngine;

namespace _SAIUN.Scripts.Lighting
{
    /// <summary>
    /// 포모도로 진행률을 Directional Light의 궤도로 옮긴다 (사양서 v1.1 4장).
    /// 수평각은 진행률을 따라 선형으로 돈다. 사양서 값(+70° → −70°)은 2026-09-29 사용자 결정으로 바꿨다:
    /// 지평선 아래가 바다가 되면서 해가 앞바다로 져야 물에 윤슬 길이 서기 때문이다. 카메라(옆 20°)에서 보아
    /// 아침엔 등 뒤(20°), 한낮엔 왼쪽(105°), 해 질 녘엔 앞바다 조금 왼쪽(190°)이다. 해 질 녘 정원은 역광이 된다.
    /// 카메라 옆 각을 바꾸면 이 두 값도 같이 옮긴다(하늘의 해 방위 = 수평각 + 180° − 카메라 옆 각).
    /// 수직 고도는 아침·저녁에 낮고 한낮에 높아, 그림자가 길어졌다 짧아졌다 한다(4-1 "낮고 긴 그림자"·"짧은 그림자").
    /// 이 고도 변화는 사양서 10장 구현 금지 목록에 있었지만 2026-09-19 사용자 지시로 켰다. 끄면 45° 고정이다.
    /// 빛 색도 진행률을 따라 아침·저녁에 따뜻해지며, 구름 등이 이 색과 방향을 읽는다.
    /// FOCUS 구간에서만 궤도를 갱신하고, 그 밖의 상태에서는 마지막 궤도를 유지한다.
    /// 쉬는 동안은 해가 진다(2026-09-28, 집중 한 번이 하루). 휴식 시간을 따라 천천히 져서 노을이 시간의 흐름대로 변한다
    /// (2026-09-29 사용자 "노을의 세기도 시간의 흐름에 따라"): 지평선의 금빛 → 해가 넘어가며 구름 밑이 붉게 타는 노을 →
    /// 해가 지평선 아래 3~4°일 때 해 진 쪽이 분홍·보랏빛으로 다시 달아오르는 박명광. 짧은 휴식은 여기서 끝나고,
    /// 긴 휴식(세션을 다 마침)은 더 내려가 별이 돋는 푸른 박명까지 간다. 빛이 어둡고 푸르게 가라앉고, 하늘은 Twilight를 읽는다.
    ///
    /// 세션 밖(시계 화면)의 하늘은 시계를 따른다(2026-09-30, 사용자 "idle 상태에서도 미려하게" → "1,3의 토글").
    /// 실제 시각이면 사는 곳의 해 뜨고 지는 시각대로 아침·한낮·노을·밤이 오고, 하루 순환을 켜면 cycleMinutes에 하루를 돈다.
    /// 해가 진 뒤는 실제 해 고도에서 박명과 밤을 읽고, 밤에는 달(실제 위상)이 뜬다(SkyClock).
    /// 시간은 앞으로만 흐른다: 세션이 끝나면 세션의 하늘에서부터 지금 시각까지, 세션이 시작하면 시계 하늘에서 새 아침까지
    /// 몇 초에 빨리 감는다. 그래서 노을 뒤에 밤이, 밤 뒤에 새벽이 오고 해가 거꾸로 돌지 않는다.
    /// </summary>
    public class SunOrbitController : MonoBehaviour
    {
        // ---- 궤도 (사양서 8장 값 70° → −70°를 사용자 결정으로 바꿨다) ----
        public const float StartHorizontalAngle = 20f;
        public const float EndHorizontalAngle = 190f;

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

        [Header("하늘의 해 (하늘 셰이더의 고도, 정원 그림자 고도와 따로)")]
        [SerializeField] private SkyArc skyArc = SkyArc.Default;

        [Header("시계 화면 하늘 (세션 밖)")]
        [Tooltip("하루 순환을 켰을 때 하루가 한 바퀴 도는 시간(분)")]
        [SerializeField, Min(1f)] private float cycleMinutes = 24f;

        [Tooltip("하루 순환에서 밤이 깊을 때 시간이 빨리 흐르는 배율. 긴 밤을 짧게 지나 아침·노을을 더 자주 본다.")]
        [SerializeField, Min(1f)] private float cycleNightSpeedup = 3f;

        [Tooltip("세션이 시작·끝날 때 하늘이 목표 시각까지 빨리 감기는 빠르기(초). 남은 시간이 이 시간마다 약 1/e로 준다.")]
        [SerializeField, Min(0.05f)] private float fastForwardSeconds = 1.2f;

        [Tooltip("달이 가장 높이 떴을 때 하늘의 고도(도)")]
        [SerializeField, Range(5f, 85f)] private float moonHighElevation = 50f;

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

        /// <summary>하늘이 셈한 햇빛을 받고 있는지. 받으면 진행률 그러데이션 대신 그 빛깔·세기를 쓴다.</summary>
        public bool HasSkyLight { get; private set; }

        /// <summary>밤(0~1). 시계 하늘에서 박명이 끝난 뒤 하늘빛이 사그라지는 정도다. 세션 중에는 0이다.</summary>
        public float Night { get; private set; }

        /// <summary>하늘의 해 고도(도). 하늘 셰이더가 쓴다.</summary>
        public float SkyElevation => skyArc.Elevation(Progress, Twilight, Night);

        /// <summary>하늘이 시계를 따르는지(세션 밖, 또는 세션이 막 시작해 새 아침까지 빨리 감는 중).</summary>
        public bool ClockActive { get; private set; }

        /// <summary>시계 하늘의 현지 시각(시, 0~24).</summary>
        public double ClockHours { get; private set; }

        /// <summary>시계 화면 하늘이 실제 시각 대신 하루를 천천히 도는지.</summary>
        public bool IdleCycle { get; private set; }

        /// <summary>달이 지평선 위에 있는지. 달은 시계 하늘에만 뜬다.</summary>
        public bool MoonUp { get; private set; }

        /// <summary>달이 뜬 동안의 진행률(0 뜸 ~ 1 짐). 해와 같은 길을 간다.</summary>
        public float MoonProgress { get; private set; }

        /// <summary>달이 찬 정도(0 그믐 ~ 1 보름).</summary>
        public float MoonLit { get; private set; }

        /// <summary>달의 하늘 고도(도). 떠 있으면 반원을 그리고, 지면 지평선 아래에 둔다.</summary>
        public float MoonSkyElevation => MoonUp ? Mathf.Lerp(0f, moonHighElevation, Mathf.Sin(MoonProgress * Mathf.PI)) : MoonHiddenElevation;

        /// <summary>정원에서 달 쪽을 가리키는 가로 방향(월드).</summary>
        public Vector3 MoonTowardDirection => -(Quaternion.Euler(0f, HorizontalAngleFor(MoonProgress), 0f) * Vector3.forward);

        /// <summary>시계 하늘을 셈하는 곳(설정에서 고른 도시, 고르지 않았으면 컴퓨터 시간대의 도시).</summary>
        public SkyPlace Place { get; private set; } = SkyPlaces.All[0];

        /// <summary>시계(현지 시각). 테스트는 바꿔 끼운다.</summary>
        internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        /// <summary>현지 시각의 표준시 차. 테스트는 바꿔 끼운다.</summary>
        internal Func<DateTime, TimeSpan> UtcOffset { get; set; } = local => TimeZoneInfo.Local.GetUtcOffset(local);

        /// <summary>지금 달의 나이(일). 테스트는 바꿔 끼운다.</summary>
        internal Func<double> MoonAge { get; set; } = () => SolarCalculator.MoonAge(DateTime.UtcNow);

        // 시간은 앞으로만: 목표가 이만큼 안쪽으로 뒤에 있으면 되감지 않고 기다린다. 이만큼 가까우면 붙는다.
        private const double BehindToleranceHours = 0.5;
        private const double LockHours = 1.0 / 60.0;
        private const double HoursPerDay = 24.0;
        private const double SecondsPerMinute = 60.0;
        private const float MoonHiddenElevation = -10f;

        private Color _skyLight = Color.white;
        private float _skyStrength = 1f;
        private SkyClock _sky;
        private GameManager _gameManager;
        private bool _clockStarted;
        private double _cycleHours;
        private double _virtualDays;

        private void Awake()
        {
            if (sun == null) sun = GetComponent<Light>();
            if (timer == null) timer = FindFirstObjectByType<PomodoroTimer>();
            _gameManager = FindFirstObjectByType<GameManager>();
            SetPlace(SkyPlaces.Resolve(SettingsStore.SkyPlace, TimeZoneInfo.Local.Id));
            IdleCycle = SettingsStore.IdleSkyCycle;

            if (sun == null) Debug.LogError("SunOrbitController: Light 참조가 없습니다.");
            if (timer == null) Debug.LogError("SunOrbitController: PomodoroTimer를 찾지 못했습니다.");
        }

        private void OnEnable()
        {
            if (_gameManager == null) return;
            _gameManager.OnIdleSkyCycleChanged += SetIdleCycle;
            _gameManager.OnSkyPlaceChanged += HandlePlaceChanged;
        }

        private void OnDisable()
        {
            if (_gameManager == null) return;
            _gameManager.OnIdleSkyCycleChanged -= SetIdleCycle;
            _gameManager.OnSkyPlaceChanged -= HandlePlaceChanged;
        }

        private void HandlePlaceChanged(string name)
        {
            SetPlace(SkyPlaces.Resolve(name, TimeZoneInfo.Local.Id));
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
            Apply(0f);
            // 앱을 켜면 첫 화면부터 시계 하늘이다.
            if (timer != null && timer.CurrentPhase == PomodoroState.Idle) UpdateIdle(0f);
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        /// <summary>한 프레임 진행한다. 테스트는 시간 간격을 넣어 직접 부른다.</summary>
        internal void Step(float deltaTime)
        {
            if (timer == null) return;
            PomodoroState phase = timer.CurrentPhase;
            if (phase == PomodoroState.Idle)
            {
                UpdateIdle(deltaTime);
                return;
            }

            // 세션이 막 시작했으면 시계 하늘을 새 아침까지 빨리 감은 뒤 세션에 넘긴다.
            if (ClockActive && !AdvanceToMorning(deltaTime)) return;

            if (phase == PomodoroState.Focus)
            {
                Twilight = 0f;
                Apply(timer.Progress);
                return;
            }
            // 휴식은 하루가 다 간 저녁에서 시작한다. 집중의 마지막 프레임을 못 보고 휴식으로 넘어오면(컴퓨터가 잠들었다 깸 등)
            // 해가 한낮에 멈춘 채 노을 없이 저무므로, 저녁 자리로 옮겨 둔다.
            if ((phase == PomodoroState.ShortBreak || phase == PomodoroState.LongBreak) && Progress < 1f) Apply(1f);
            StepTwilight(TwilightGoal(phase, timer.Progress), deltaTime);
        }

        /// <summary>시계 하늘을 셈할 곳을 바꾼다. 해 뜨고 지는 시각이 바뀌므로 시계 하늘이 곧바로 그곳의 지금 하늘이 된다.</summary>
        public void SetPlace(SkyPlace place)
        {
            Place = place;
            if (ClockActive) ApplyClock();
        }

        /// <summary>시계 화면 하늘을 하루 순환으로(켜면) 또는 실제 시각으로(끄면) 둔다. 순환은 지금 하늘에서 이어 돈다.</summary>
        public void SetIdleCycle(bool cycle)
        {
            if (cycle && !IdleCycle) _cycleHours = ClockHours;
            IdleCycle = cycle;
        }

        // ---- 시계 하늘 ----

        // 세션 밖: 실제 시각(또는 순환 시계)을 향해 시계 하늘을 흘리고 적용한다.
        private void UpdateIdle(float deltaTime)
        {
            DateTime now = Clock();
            if (!ClockActive)
            {
                // 앱을 켜면 곧바로 지금 시각에서, 세션에서 돌아오면 세션의 하늘에서부터 시작해 앞으로 흘린다.
                ClockHours = _clockStarted ? SyncSky().HoursFor(Progress, Twilight, now.Date, UtcOffset(now)) : now.TimeOfDay.TotalHours;
                _cycleHours = ClockHours;
                ClockActive = true;
                _clockStarted = true;
            }

            double target = now.TimeOfDay.TotalHours;
            if (IdleCycle)
            {
                double speed = HoursPerDay / (cycleMinutes * SecondsPerMinute) * Mathf.Lerp(1f, cycleNightSpeedup, Night);
                _cycleHours += deltaTime * speed;
                if (_cycleHours >= HoursPerDay)
                {
                    _cycleHours -= HoursPerDay;
                    _virtualDays += 1.0;
                }
                target = _cycleHours;
            }
            FollowClock(target, deltaTime);
            ApplyClock();
        }

        // 세션 시작: 시계 하늘을 새 아침(진행률 0)까지 빨리 감는다. 다 감았으면 true.
        private bool AdvanceToMorning(float deltaTime)
        {
            DateTime today = Clock().Date;
            if (!FollowClock(SyncSky().MorningHours(today, UtcOffset(today)), deltaTime))
            {
                ApplyClock();
                return false;
            }
            ClockActive = false;
            Night = 0f;
            MoonUp = false;
            return true;
        }

        // 시계 하늘을 목표 시각 쪽으로 앞으로만 흘린다. 멀면 빨리 감고(남은 시간이 fastForwardSeconds마다 약 1/e로),
        // 가까우면 붙는다. 목표가 조금 뒤에 있으면 되감지 않고 목표가 따라올 때까지 기다린다. 붙어 있으면 true.
        private bool FollowClock(double targetHours, float deltaTime)
        {
            double ahead = SkyClock.Repeat(targetHours - ClockHours, HoursPerDay);
            if (ahead > HoursPerDay - BehindToleranceHours) return true;
            if (ahead <= LockHours)
            {
                ClockHours = SkyClock.Repeat(targetHours, HoursPerDay);
                return true;
            }
            double step = ahead * (1.0 - Math.Exp(-deltaTime / fastForwardSeconds));
            ClockHours = SkyClock.Repeat(ClockHours + step, HoursPerDay);
            return false;
        }

        // 시계 하늘의 시각을 해·박명·밤·달에 적용한다.
        private void ApplyClock()
        {
            DateTime local = Clock().Date.AddHours(ClockHours);
            double age = SkyClock.Repeat(MoonAge() + (IdleCycle ? _virtualDays : 0.0), SolarCalculator.SynodicMonthDays);
            SkyClockState state = SyncSky().StateAt(local, UtcOffset(local), age);
            Twilight = state.Twilight;
            Night = state.Night;
            MoonUp = state.MoonUp;
            MoonProgress = state.MoonProgress;
            MoonLit = state.MoonLit;
            Apply(state.Progress);
        }

        // 인스펙터에서 자리·고도를 바꿔도 따라가게 매번 맞춘다.
        private SkyClock SyncSky()
        {
            if (_sky == null) _sky = new SkyClock(Place.Latitude, Place.Longitude, skyArc);
            _sky.Latitude = Place.Latitude;
            _sky.Longitude = Place.Longitude;
            _sky.Arc = skyArc;
            return _sky;
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
            ApplyLight();
        }

        /// <summary>
        /// 하늘이 대기를 지난 햇빛으로 셈한 빛깔(가장 밝은 성분 1)과 한낮 대비 세기(0~1)를 받는다(2026-09-29, 정원이 하늘빛을 받게).
        /// 낮은 해는 붉고 어둡고, 해가 지면 0이 되어 정원은 하늘의 주변광만 받는다.
        /// </summary>
        public void SetSkyLight(Color sunlight, float strength)
        {
            _skyLight = sunlight;
            _skyStrength = Mathf.Clamp01(strength);
            HasSkyLight = true;
            ApplyLight();
        }

        // 빛깔·세기를 광원에 적용한다. 하늘 빛이 있으면 그 빛을, 없으면 진행률 그러데이션과 고도 배율을 쓴다.
        private void ApplyLight()
        {
            if (sun == null) return;
            Color baseColor = HasSkyLight ? _skyLight : SunColor;
            float scale = HasSkyLight ? _skyStrength : Mathf.Lerp(horizonIntensity, 1f, Daylight(Progress));
            sun.color = Color.Lerp(baseColor, twilightColor, Twilight);
            sun.intensity = intensity * scale * Mathf.Lerp(1f, twilightIntensity, Twilight);
        }

        /// <summary>사양서 4장 수식(선형). 범위 밖 진행률은 0~1로 자른다.</summary>
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

        // 아침·저녁은 복숭아빛, 한낮은 흰빛. 모래빛 쪽으로 기운 따뜻한 색이다.
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
