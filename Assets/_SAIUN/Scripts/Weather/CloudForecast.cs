using System;
using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>하늘에 뜨는 구름의 종류. 셰이더 매개변수(_High, _Mid, _Low, _Special, _Extra.x)의 자리 순서와 같다.</summary>
    public enum CloudKind
    {
        Cirrus,           // 권운(새털구름)
        Cirrocumulus,     // 권적운(조개구름)
        Cirrostratus,     // 권층운(햇무리구름)
        Noctilucent,      // 야광운
        Altocumulus,      // 고적운(양떼구름)
        Altostratus,      // 고층운(차일구름)
        Lenticular,       // 렌즈구름(채운)
        Virga,            // 꼬리구름
        Stratocumulus,    // 층적운(두루마리구름)
        Stratus,          // 층운(안개구름)
        Cumulus,          // 적운(뭉게구름 떼)
        Nimbostratus,     // 난층운(비구름)
        Anvil,            // 적란운의 모루
        Mammatus,         // 유방운
        Arcus,            // 아치구름(선반구름)
        FallstreakHole,   // 구멍구름
        KelvinHelmholtz,  // 켈빈-헬름홀츠 물결구름
    }

    /// <summary>맑은 날 하늘의 장면. 몇 분마다 새로 고른다.</summary>
    public enum SkyScene
    {
        Clear,             // 맑음: 탑과 작은 뭉게구름 몇
        FairCumulus,       // 뭉게구름 떼
        Cirrus,            // 새털구름
        Cirrocumulus,      // 조개구름
        Altocumulus,       // 양떼구름
        Stratocumulus,     // 두루마리구름
        MorningFog,        // 아침 안개(층운)
        CirrostratusVeil,  // 햇무리구름 너울
    }

    /// <summary>하늘이 읽는 바깥 상태.</summary>
    public struct SkyInputs
    {
        public float Progress;   // 하루(세션 진행률) 0~1
        public float Twilight;   // 해가 진 정도 0~1
        public float Storm;      // 뇌우(집중 상태 연동 먹구름) 0~1
        public float Rain;       // 비 강도 0~1
        public float Wind;       // 바람 세기 0~1
    }

    /// <summary>장면 하나가 나올 가능성(아침·한낮·저녁 무게, 바람 1당 더하는 무게)과 주된 구름 양의 범위.</summary>
    [Serializable]
    public struct SceneWeight
    {
        [SerializeField] private float morning;
        [SerializeField] private float midday;
        [SerializeField] private float evening;
        [SerializeField] private float perWind;
        [SerializeField] private Vector2 cover;

        public SceneWeight(float morning, float midday, float evening, float perWind, Vector2 cover)
        {
            this.morning = morning;
            this.midday = midday;
            this.evening = evening;
            this.perWind = perWind;
            this.cover = cover;
        }

        public Vector2 Cover => cover;

        /// <summary>하루 중 때(진행률)와 바람에 따른 무게. 아침→한낮→저녁을 곧게 잇는다.</summary>
        public float Weight(float progress, float wind)
        {
            float byTime = progress < Noon
                ? Mathf.Lerp(morning, midday, progress / Noon)
                : Mathf.Lerp(midday, evening, (progress - Noon) / (1f - Noon));
            return Mathf.Max(0f, byTime + perWind * wind);
        }

        // 한낮의 진행률
        private const float Noon = 0.5f;
    }

    /// <summary>
    /// 어떤 구름이 언제, 얼마나 뜰지 정한다 (2026-09-28, 사용자: "나머지 구름도 전부, 출현 랜덤성과 조건도").
    /// 실제 날씨의 순서를 따르되, 위젯에서 자주 볼 수 있도록 시간을 줄였다.
    ///  - 맑은 날: 몇 분마다 장면을 새로 고른다. 하루 중 때와 바람에 따라 무게가 다르다
    ///    (아침엔 안개·양떼구름, 한낮엔 뭉게구름 떼, 저녁엔 새털구름, 바람이 세면 새털구름·두루마리구름).
    ///    뭉게구름은 아침에 적고 해가 지면 스러진다. 같은 장면이 두 번 이어지지 않는다.
    ///  - 비(랜덤 트리거): 온난전선처럼 권층운 → 고층운 → 난층운 순으로 두꺼워지고, 비가 오기 전후에 꼬리구름이 늘어진다.
    ///  - 뇌우(집중 상태 연동 먹구름): 탑이 적란운이 되어 모루가 서고, 몰려오는 순간 아치구름이 잠깐 선다.
    ///    폭풍이 지나가면 가끔 유방운이 남는다.
    ///  - 드문 구름: 조개·양떼구름에 구멍구름, 바람 센 날 켈빈-헬름홀츠 물결구름, 해가 진 뒤 야광운.
    ///  - 채운 렌즈구름: 몇 분 주기로 피었다 사라지며 대부분의 시간 떠 있다. 층구름이 덮거나 폭풍이면 숨는다.
    /// 여기서는 목표 양만 정하고, 하늘(SkyView)이 그 목표로 천천히 옮겨 간다.
    /// </summary>
    [Serializable]
    public class CloudForecast
    {
        /// <summary>구름 종류 수.</summary>
        public static readonly int KindCount = Enum.GetValues(typeof(CloudKind)).Length;

        private static readonly int SceneCount = Enum.GetValues(typeof(SkyScene)).Length;

        // 하루의 때: 뭉게구름이 피기 시작하고 다 피는 진행률
        private const float CumulusRiseStart = 0.1f;
        private const float CumulusRiseEnd = 0.4f;
        private const float CumulusMorningShare = 0.5f;

        // 뇌우: 모루가 서기 시작하고 다 서는 먹구름 정도, 먹구름 층이 덮기 시작하고 다 덮는 정도
        private const float AnvilStart = 0.15f;
        private const float AnvilFull = 0.6f;
        private const float DeckStart = 0.5f;
        private const float DeckFull = 1f;

        // 유방운: 폭풍이 이만큼 짙었다가 이 아래로 걷힐 때 굴린다. 폭풍이 다 걷히면 다음 폭풍을 기다린다.
        private const float MammatusPeak = 0.6f;
        private const float MammatusRelease = 0.45f;
        private const float StormGone = 0.05f;

        // 온난전선: 권층운·고층운·난층운이 서는 전선 정도 구간
        private const float VeilStart = 0f;
        private const float VeilFull = 0.3f;
        private const float AltostratusStart = 0.15f;
        private const float AltostratusFull = 0.6f;
        private const float RainDeckStart = 0.45f;
        private const float RainDeckFull = 0.9f;
        private const float RainDeckCover = 0.9f;
        // 비가 오기 전후(전선이 옅을 때) 꼬리구름이 늘어지는 구간과 그때의 양떼구름 양
        private const float VirgaFrontStart = 0.05f;
        private const float VirgaFrontPeak = 0.25f;
        private const float VirgaFrontEnd = 0.5f;
        private const float VirgaFrontAltocumulus = 0.35f;
        // 전선이 두꺼워지며 맑은 날 구름과 탑이 사라지는 구간
        private const float FairFadeStart = 0.2f;
        private const float FairFadeEnd = 0.7f;
        private const float TowerFrontFade = 0.85f;

        // 곁들이는 높은 구름 가운데 새털구름의 몫(나머지는 조개구름), 양떼구름 장면의 구멍구름 확률 몫
        private const float CompanionCirrusShare = 0.5f;
        private const float AltocumulusHoleShare = 0.5f;

        // 해가 지며 렌즈구름이 스러지기 시작하는 박명
        private const float LensDuskStart = 0.4f;

        // 렌즈구름을 가리는 층구름의 무게(층적운은 틈이 있어 조금 덜 가린다)
        private const float LensStratocumulusBlock = 0.8f;

        // 탑이 보이는 정도: 두루마리구름·안개 장면에서는 층에 가려 흐리다
        private const float TowerUnderStratocumulus = 0.7f;
        private const float TowerUnderFog = 0.45f;

        // 해가 지면 뭉게구름이 스러지는 박명 구간
        private const float DuskFadeStart = 0.3f;
        private const float DuskFadeEnd = 0.9f;
        private const float TwilightEpisodeStart = 0.05f;
        private const float TwilightEpisodeEnd = 0.001f;

        [Header("장면")]
        [Tooltip("장면이 이어지는 시간 범위(분)")]
        [SerializeField] private Vector2 sceneMinutes = new Vector2(5f, 10f);

        [Tooltip("장면별 무게(아침·한낮·저녁·바람 1당)와 주된 구름 양 범위. SkyScene 순서.")]
        [SerializeField] private SceneWeight[] sceneWeights = DefaultSceneWeights();

        [Tooltip("장면에 높은 구름(새털·조개구름)이 옅게 곁들여질 확률")]
        [SerializeField, Range(0f, 1f)] private float companionChance = 0.35f;

        [Tooltip("곁들인 높은 구름의 양")]
        [SerializeField, Range(0f, 1f)] private float companionCover = 0.3f;

        [Tooltip("맑음 장면에도 떠 있는 작은 뭉게구름 양")]
        [SerializeField, Range(0f, 1f)] private float clearCumulus = 0.2f;

        [Tooltip("새털구름 장면에 곁들이는 작은 뭉게구름 양")]
        [SerializeField, Range(0f, 1f)] private float cirrusCumulus = 0.2f;

        [Tooltip("햇무리구름 장면에 곁들이는 새털구름 양")]
        [SerializeField, Range(0f, 1f)] private float veilCirrus = 0.3f;

        [Header("드문 구름")]
        [Tooltip("조개구름 장면에 구멍구름이 뚫릴 확률(양떼구름 장면은 절반)")]
        [SerializeField, Range(0f, 1f)] private float holeChance = 0.25f;

        [Tooltip("양떼구름 장면에 꼬리구름이 늘어질 확률")]
        [SerializeField, Range(0f, 1f)] private float virgaChance = 0.35f;

        [Tooltip("장면이 바뀔 때 물결구름이 설 확률(바람이 없을 때)")]
        [SerializeField, Range(0f, 1f)] private float kelvinChance = 0.04f;

        [Tooltip("바람 세기 1(최대)일 때 물결구름 확률에 더하는 값")]
        [SerializeField, Range(0f, 1f)] private float kelvinWindChance = 0.14f;

        [Tooltip("물결구름이 이어지는 시간 범위(초). 실제로도 몇 분이면 부서진다.")]
        [SerializeField] private Vector2 kelvinSeconds = new Vector2(120f, 240f);

        [Tooltip("해가 질 때마다 야광운이 뜰 확률")]
        [SerializeField, Range(0f, 1f)] private float noctilucentChance = 0.35f;

        [Header("폭풍")]
        [Tooltip("먹구름이 이만큼 몰려오는 순간 아치구름이 선다")]
        [SerializeField, Range(0f, 1f)] private float arcusTrigger = 0.3f;

        [Tooltip("아치구름이 서 있는 시간(초)")]
        [SerializeField, Min(0f)] private float arcusSeconds = 40f;

        [Tooltip("폭풍이 지나간 뒤 유방운이 남을 확률")]
        [SerializeField, Range(0f, 1f)] private float mammatusChance = 0.6f;

        [Tooltip("유방운이 남아 있는 시간(초)")]
        [SerializeField, Min(0f)] private float mammatusSeconds = 90f;

        [Tooltip("뇌우 때 먹구름 층이 덮는 양. 1보다 작아야 틈으로 적란운과 모루가 보인다.")]
        [SerializeField, Range(0f, 1f)] private float stormDeck = 0.7f;

        [Tooltip("비 강도 가운데 뇌우에 딸린 비의 몫(먹구름 1일 때). 나머지가 온난전선 비다.")]
        [SerializeField, Range(0f, 1f)] private float stormRainShare = 0.8f;

        [Tooltip("온난전선이 다 두꺼워지는 비 강도")]
        [SerializeField, Range(0.05f, 1f)] private float frontFullRain = 0.6f;

        [Header("채운 렌즈구름")]
        [Tooltip("렌즈구름이 피었다 사라지는 한 주기(분)")]
        [SerializeField, Min(0.1f)] private float lensCycleMinutes = 11f;

        [Tooltip("한 주기 가운데 렌즈구름이 떠 있는 비율(피어나고 사라지는 시간 포함)")]
        [SerializeField, Range(0.1f, 1f)] private float lensPresence = 0.72f;

        [Tooltip("피어나고 사라지는 데 걸리는 비율(주기 대비)")]
        [SerializeField, Range(0.01f, 0.3f)] private float lensFade = 0.1f;

        [Tooltip("처음 켰을 때 주기의 어디서 시작할지(0~1). 켜자마자 떠 있게 한다.")]
        [SerializeField, Range(0f, 1f)] private float lensStartPhase = 0.25f;

        [Header("옮겨 가는 빠르기")]
        [Tooltip("맑은 날 구름이 새 장면으로 옮겨 가는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float fairResponse = 45f;

        [Tooltip("폭풍·비 구름이 몰려오고 걷히는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float stormResponse = 8f;

        /// <summary>지금 맑은 날 장면.</summary>
        public SkyScene Scene { get; private set; }

        /// <summary>다음 장면까지 남은 시간(초).</summary>
        public float SecondsUntilChange { get; private set; }

        /// <summary>웅대적운 탑이 보이는 정도의 목표(0~1).</summary>
        public float TowerPresence { get; private set; } = 1f;

        /// <summary>이번 박명에 야광운이 뜨는지.</summary>
        public bool NoctilucentTonight { get; private set; }

        private readonly float[] _targets = new float[KindCount];
        private Func<float> _random = () => UnityEngine.Random.value;
        private bool _started;
        private float _sceneCover;
        private CloudKind _companion;
        private bool _hasCompanion;
        private bool _hole;
        private bool _virga;
        private float _kelvinLeft;
        private float _arcusLeft;
        private float _mammatusLeft;
        private float _previousStorm;
        private float _stormPeak;
        private bool _twilightEpisode;
        private float _lensTime;

        /// <summary>구름 종류의 목표 양(0~1).</summary>
        public float Target(CloudKind kind) => _targets[(int)kind];

        /// <summary>테스트용 난수 교체. null이면 원래 것으로 되돌린다.</summary>
        internal void SetRandom(Func<float> random)
        {
            _random = random ?? (() => UnityEngine.Random.value);
        }

        /// <summary>이 구름이 목표로 옮겨 가는 데 걸리는 시간(초). 폭풍·비에 딸린 구름은 빠르다.</summary>
        public float ResponseSeconds(CloudKind kind)
        {
            switch (kind)
            {
                case CloudKind.Nimbostratus:
                case CloudKind.Altostratus:
                case CloudKind.Anvil:
                case CloudKind.Arcus:
                case CloudKind.Mammatus:
                    return stormResponse;
                default:
                    return fairResponse;
            }
        }

        /// <summary>한 걸음 진행하고 목표를 다시 정한다.</summary>
        public void Tick(float deltaTime, SkyInputs inputs)
        {
            if (!_started)
            {
                _started = true;
                _lensTime = lensStartPhase * lensCycleMinutes * 60f;
                _previousStorm = inputs.Storm;
                ChooseScene(inputs);
            }

            SecondsUntilChange -= deltaTime;
            if (SecondsUntilChange <= 0f) ChooseScene(inputs);

            _kelvinLeft = Mathf.Max(0f, _kelvinLeft - deltaTime);
            _lensTime += deltaTime;
            TrackStorm(deltaTime, inputs.Storm);
            TrackTwilight(inputs.Twilight);
            ComputeTargets(inputs);
        }

        // ---- 장면 ----

        /// <summary>새 장면을 고른다(지금 장면은 빼고). 테스트는 직접 부른다.</summary>
        internal void ChooseScene(SkyInputs inputs)
        {
            float total = 0f;
            var weights = new float[SceneCount];
            for (int i = 0; i < SceneCount; i++)
            {
                bool same = _sceneCover > 0f && i == (int)Scene;
                weights[i] = same || i >= sceneWeights.Length ? 0f : sceneWeights[i].Weight(inputs.Progress, inputs.Wind);
                total += weights[i];
            }

            float roll = _random() * total;
            int chosen = 0;
            for (int i = 0; i < SceneCount; i++)
            {
                if (weights[i] <= 0f) continue;
                chosen = i;
                roll -= weights[i];
                if (roll < 0f) break;
            }

            Scene = (SkyScene)chosen;
            Vector2 cover = sceneWeights[chosen].Cover;
            _sceneCover = Mathf.Max(Mathf.Lerp(cover.x, cover.y, _random()), Mathf.Epsilon);
            SecondsUntilChange = Mathf.Lerp(sceneMinutes.x, sceneMinutes.y, _random()) * 60f;

            _hasCompanion = HasCompanion(Scene) && _random() < companionChance;
            _companion = _random() < CompanionCirrusShare ? CloudKind.Cirrus : CloudKind.Cirrocumulus;
            _hole = (Scene == SkyScene.Cirrocumulus && _random() < holeChance)
                    || (Scene == SkyScene.Altocumulus && _random() < holeChance * AltocumulusHoleShare);
            _virga = Scene == SkyScene.Altocumulus && _random() < virgaChance;
            if (_random() < kelvinChance + kelvinWindChance * inputs.Wind)
            {
                _kelvinLeft = Mathf.Lerp(kelvinSeconds.x, kelvinSeconds.y, _random());
            }
        }

        // 높은 구름을 곁들여도 어색하지 않은 장면(이미 높은 구름이 주인공이거나 하늘을 덮는 장면은 뺀다)
        private static bool HasCompanion(SkyScene scene)
        {
            return scene == SkyScene.Clear || scene == SkyScene.FairCumulus || scene == SkyScene.Altocumulus
                   || scene == SkyScene.Stratocumulus;
        }

        // ---- 폭풍·박명 사건 ----

        private void TrackStorm(float deltaTime, float storm)
        {
            _arcusLeft = Mathf.Max(0f, _arcusLeft - deltaTime);
            _mammatusLeft = Mathf.Max(0f, _mammatusLeft - deltaTime);

            // 먹구름이 몰려오는 순간 아치구름이 선다.
            if (_previousStorm < arcusTrigger && storm >= arcusTrigger) _arcusLeft = arcusSeconds;

            // 짙은 폭풍이 걷히기 시작하면 가끔 유방운이 남는다.
            _stormPeak = Mathf.Max(_stormPeak, storm);
            if (_stormPeak >= MammatusPeak && _previousStorm >= MammatusRelease && storm < MammatusRelease)
            {
                if (_random() < mammatusChance) _mammatusLeft = mammatusSeconds;
                _stormPeak = 0f;
            }
            if (storm < StormGone) _stormPeak = 0f;
            _previousStorm = storm;
        }

        // 해가 질 때마다 한 번 야광운을 굴린다.
        private void TrackTwilight(float twilight)
        {
            if (!_twilightEpisode && twilight > TwilightEpisodeStart)
            {
                _twilightEpisode = true;
                NoctilucentTonight = _random() < noctilucentChance;
            }
            else if (_twilightEpisode && twilight <= TwilightEpisodeEnd)
            {
                _twilightEpisode = false;
                NoctilucentTonight = false;
            }
        }

        // ---- 목표 ----

        private void ComputeTargets(SkyInputs inputs)
        {
            Array.Clear(_targets, 0, _targets.Length);
            float storm = inputs.Storm;
            float front = Mathf.Clamp01((inputs.Rain - storm * stormRainShare) / frontFullRain);
            float fair = (1f - storm) * (1f - Smooth(FairFadeStart, FairFadeEnd, front));
            float cumulusByDay = Mathf.Lerp(CumulusMorningShare, 1f, Smooth(CumulusRiseStart, CumulusRiseEnd, inputs.Progress))
                                 * (1f - Smooth(DuskFadeStart, DuskFadeEnd, inputs.Twilight));

            // 맑은 날 장면
            float tower = 1f;
            switch (Scene)
            {
                case SkyScene.Clear:
                    Set(CloudKind.Cumulus, clearCumulus * cumulusByDay);
                    break;
                case SkyScene.FairCumulus:
                    Set(CloudKind.Cumulus, _sceneCover * cumulusByDay);
                    break;
                case SkyScene.Cirrus:
                    Set(CloudKind.Cirrus, _sceneCover);
                    Set(CloudKind.Cumulus, cirrusCumulus * cumulusByDay);
                    break;
                case SkyScene.Cirrocumulus:
                    Set(CloudKind.Cirrocumulus, _sceneCover);
                    break;
                case SkyScene.Altocumulus:
                    Set(CloudKind.Altocumulus, _sceneCover);
                    break;
                case SkyScene.Stratocumulus:
                    Set(CloudKind.Stratocumulus, _sceneCover);
                    tower = TowerUnderStratocumulus;
                    break;
                case SkyScene.MorningFog:
                    Set(CloudKind.Stratus, _sceneCover);
                    tower = TowerUnderFog;
                    break;
                case SkyScene.CirrostratusVeil:
                    Set(CloudKind.Cirrostratus, _sceneCover);
                    Set(CloudKind.Cirrus, veilCirrus);
                    break;
            }
            if (_hasCompanion) Raise(_companion, companionCover);
            if (_hole) Set(CloudKind.FallstreakHole, 1f);
            if (_virga) Set(CloudKind.Virga, 1f);
            if (_kelvinLeft > 0f && storm < StormGone) Set(CloudKind.KelvinHelmholtz, 1f);
            for (int i = 0; i < KindCount; i++) _targets[i] *= fair;

            // 온난전선 비: 권층운 → 고층운 → 난층운. 비가 오기 전후에는 양떼구름에서 꼬리구름이 늘어진다.
            Raise(CloudKind.Cirrostratus, Smooth(VeilStart, VeilFull, front));
            Raise(CloudKind.Altostratus, Smooth(AltostratusStart, AltostratusFull, front));
            Raise(CloudKind.Nimbostratus, Smooth(RainDeckStart, RainDeckFull, front) * RainDeckCover);
            float virgaFront = Smooth(VirgaFrontStart, VirgaFrontPeak, front) * (1f - Smooth(VirgaFrontPeak, VirgaFrontEnd, front));
            Raise(CloudKind.Altocumulus, VirgaFrontAltocumulus * virgaFront);
            Raise(CloudKind.Virga, virgaFront);

            // 뇌우: 탑이 적란운이 되어 모루가 서고, 먹구름 층이 틈을 두고 덮는다.
            Raise(CloudKind.Anvil, Smooth(AnvilStart, AnvilFull, storm));
            Raise(CloudKind.Nimbostratus, Smooth(DeckStart, DeckFull, storm) * stormDeck);
            if (_arcusLeft > 0f && storm > StormGone) Set(CloudKind.Arcus, 1f);
            if (_mammatusLeft > 0f) Set(CloudKind.Mammatus, 1f);

            // 박명의 야광운
            if (NoctilucentTonight) Set(CloudKind.Noctilucent, 1f);

            // 채운 렌즈구름: 층구름이 덮거나 폭풍이면 숨는다. 해가 다 지면 빛을 잃은 검은 원반이 되므로 스러진다.
            float blocking = Mathf.Max(Mathf.Max(Target(CloudKind.Stratus), Target(CloudKind.Stratocumulus) * LensStratocumulusBlock),
                Mathf.Max(Target(CloudKind.Altostratus), Target(CloudKind.Nimbostratus)));
            Set(CloudKind.Lenticular, LensCycle() * (1f - blocking) * (1f - storm) * (1f - Smooth(LensDuskStart, 1f, inputs.Twilight)));

            // 탑은 전선 비에 가려 사라지고, 뇌우면 적란운으로 솟아 뚜렷하다.
            TowerPresence = Mathf.Lerp(tower * (1f - TowerFrontFade * Smooth(FairFadeStart, FairFadeEnd, front)), 1f, storm);
        }

        // 렌즈구름은 주기의 앞쪽 lensPresence 동안 떠 있고, 앞뒤 lensFade 동안 피어나고 사라진다.
        private float LensCycle()
        {
            float phase = Mathf.Repeat(_lensTime / (lensCycleMinutes * 60f), 1f);
            float fade = Mathf.Min(lensFade, lensPresence / 2f);
            float rise = Smooth(0f, fade, phase);
            float fall = 1f - Smooth(lensPresence - fade, lensPresence, phase);
            return Mathf.Min(rise, fall);
        }

        private void Set(CloudKind kind, float amount)
        {
            _targets[(int)kind] = Mathf.Clamp01(amount);
        }

        private void Raise(CloudKind kind, float amount)
        {
            _targets[(int)kind] = Mathf.Max(_targets[(int)kind], Mathf.Clamp01(amount));
        }

        // 셰이더의 smoothstep과 같은 곡선
        private static float Smooth(float edge0, float edge1, float x)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));
        }

        // 기본 무게: 아침엔 안개·양떼구름, 한낮엔 뭉게구름 떼, 저녁엔 새털구름. 바람이 세면 새털구름·두루마리구름.
        private static SceneWeight[] DefaultSceneWeights()
        {
            return new[]
            {
                new SceneWeight(1f, 1f, 1f, 0f, new Vector2(1f, 1f)),                     // 맑음
                new SceneWeight(0.8f, 3.5f, 1.8f, 0f, new Vector2(0.4f, 0.6f)),           // 뭉게구름 떼
                new SceneWeight(1.5f, 1.2f, 2.5f, 1.5f, new Vector2(0.45f, 0.75f)),       // 새털구름
                new SceneWeight(1f, 0.8f, 1.2f, 0f, new Vector2(0.5f, 0.75f)),            // 조개구름
                new SceneWeight(2.5f, 1f, 1.5f, 0f, new Vector2(0.45f, 0.65f)),           // 양떼구름
                new SceneWeight(0.7f, 0.7f, 0.7f, 1.2f, new Vector2(0.45f, 0.62f)),       // 두루마리구름
                new SceneWeight(2.5f, 0.1f, 0.2f, 0f, new Vector2(0.5f, 0.7f)),           // 아침 안개
                new SceneWeight(0.8f, 0.8f, 0.8f, 0f, new Vector2(0.6f, 0.85f)),          // 햇무리구름 너울
            };
        }
    }
}
