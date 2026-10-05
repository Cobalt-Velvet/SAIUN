using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Lighting;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 카드 위쪽의 하늘. 웅장함과 그리움을 주는 것이 목표다.
    /// 셰이더(Hidden/SAIUN/Sky)가 웅대적운 탑과 채운 갓구름을 부피로 그리고, 지평선 아래는 하늘을 비추는 바다와 모래밭이다.
    ///  - 세션 진행률(해)이 하루다: 아침 금빛 → 한낮 파랑 → 늦은 오후 금빛 → 해 질 녘 노을.
    ///  - 웅대적운: 드물게, 특별하게. 맑은 날 한낮~오후에 가끔 솟아 몇 분에 걸쳐 자라고, 머물다 스러진다(예보가 정한다).
    ///    솟을 때마다 모양이 다르다. 먹구름이 오면 적란운으로 끝까지 솟고 어두워진다.
    ///  - 채운: 탑과 따로, 왼쪽 빈 하늘에 무지개 빛이 번지는 얇은 비단 구름(채운 너울)이 뜬다. 몇 분마다 피었다 사라지며,
    ///    필 때마다 자리가 달라진다.
    ///    탑이 다 자라면 꼭대기의 갓구름에도 채운이 선다(가끔 보는 장면). SAIUN을 만든 이유가 웅대적운과 채운이다.
    ///  - 나머지 구름(권운·권적운·권층운·고적운·고층운·층적운·층운·난층운·적운 떼, 모루·유방운·아치구름·꼬리구름,
    ///    구멍구름·물결구름·야광운)은 예보(CloudForecast)가 때·날씨·바람과 무작위로 정한 양을 향해 천천히 옮겨 간다.
    ///  - 하늘빛은 셰이더가 대기 산란으로 셈한다. 해 방향은 정원 그림자를 만드는 해(SunOrbitController)의 방위를 카메라에서 본
    ///    그대로 쓰고, 고도는 아침엔 금빛 아침 해, 저녁엔 지평선에 닿는 해로 넓힌다. 해가 움직이면 하늘빛도 따라 바뀐다.
    ///  - 쉬는 동안 해가 지면(해의 Twilight) 노을·박명 하늘이 되고, 높은 구름만 붉게 남는다.
    ///  - 구름은 층마다 다른 바람을 탄다. 낮은 구름은 땅 바람
    ///    (풍향계·빗줄기·바람결과 같은 바람)을 타고, 높이 오를수록 바람이 빨라지고 시계 방향으로 비껴 돌며 방향도 한결같다.
    ///    채운 너울과 웅대적운 탑은 바람에 흘러가지 않고 제자리에 선다(눈이 머물 자리가 되게).
    ///  - 지평선 아래는 바다다. 물이 하늘·구름·노을을
    ///    비추고, 해가 앞바다로 지는 저녁엔 윤슬 길이 선다. 물결은 땅 바람을 따라 흐르고 파도가 모래밭에 밀려왔다 빠진다.
    ///    바다는 보이는 장을 만들 때 매 프레임 그리므로 윤슬이 반짝인다(하늘은 여전히 1/8씩 그린다).
    ///  - 창 전체 유리(설정)를 켜면 하늘과 바다 없이 구름만 바탕화면 유리 위에 띄운다.
    ///    바탕화면을 읽는 유리는 이때만 켠다.
    /// 부피 그리기는 무거워 한 번에 화소의 1/8만 새로 그린다(여덟 번에 한 바퀴). 반쯤 새로 그린 장을 보이면 빗살이 지므로
    /// 다 그린 장끼리만 한 바퀴 동안 천천히 섞어 넘긴다(그리는 장 · 지난 장 · 지금 장 · 보이는 장).
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class SkyView : MonoBehaviour
    {
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int MoonId = Shader.PropertyToID("_Moon");
        private static readonly int AltHighId = Shader.PropertyToID("_AltHigh");
        private static readonly int AltLowId = Shader.PropertyToID("_AltLow");
        private static readonly int GlassId = Shader.PropertyToID("_Glass");
        private static readonly int DriftId = Shader.PropertyToID("_Drift");
        private static readonly int DriftHighId = Shader.PropertyToID("_DriftHigh");
        private static readonly int SeaTimeId = Shader.PropertyToID("_SeaTime");
        private static readonly int SeaWindId = Shader.PropertyToID("_SeaWind");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int ViewId = Shader.PropertyToID("_View");
        private static readonly int GrowthId = Shader.PropertyToID("_Growth");
        private static readonly int StormId = Shader.PropertyToID("_Storm");
        private static readonly int CapId = Shader.PropertyToID("_Cap");
        private static readonly int HighId = Shader.PropertyToID("_High");
        private static readonly int MidId = Shader.PropertyToID("_Mid");
        private static readonly int LowId = Shader.PropertyToID("_Low");
        private static readonly int SpecialId = Shader.PropertyToID("_Special");
        private static readonly int ExtraId = Shader.PropertyToID("_Extra");
        private static readonly int VeilPlaceId = Shader.PropertyToID("_VeilPlace");
        private static readonly int VeilFormId = Shader.PropertyToID("_VeilForm");
        private static readonly int SkyTimeId = Shader.PropertyToID("_SkyTime");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int PhaseId = Shader.PropertyToID("_Phase");
        private static readonly int SkySizeId = Shader.PropertyToID("_SkySize");
        private static readonly int NoiseId = Shader.PropertyToID("_Noise");
        private static readonly int PrevTexId = Shader.PropertyToID("_PrevTex");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");

        // 셰이더 패스: 하늘 그리기, 두 장 섞기
        private const int SkyPass = 0;
        private const int MixPass = 1;

        /// <summary>한 바퀴(화소 전체)를 나눠 그리는 횟수. 셰이더의 4×2 격자와 같다.</summary>
        public const int Interleave = 8;

        // 밤이 이만큼 깊으면 하늘을 비추는 빛이 해에서 달로 넘어간다(셰이더의 MoonLights와 같다).
        private const float MoonLightNight = 0.5f;

        // 한 장을 그리는 동안 이보다 해가 움직이거나(도) 밤이 바뀌면 남은 칸을 한 번에 그린다.
        private const float SheetLightTolerance = 0.4f;
        private const float SheetNightTolerance = 0.02f;

        // 셰이더에 "전부 그려라"를 알리는 칸 번호
        private const float AllPhases = -1f;

        // 바람 빠르기(km/분)를 한 프레임 거리로 옮길 때
        private const float SecondsPerMinute = 60f;

        // 잔물결 빠르기(m/s)를 km로
        private const float MetersPerKilometer = 1000f;

        // 셰이더의 잔물결 무늬가 되풀이되는 거리(km). 잔물결 주파수가 이 거리에서 정수 번 돈다(sea_common.glsl WaveHeight).
        private const float SeaDriftPeriod = 25f;

        // 구멍구름이 이보다 옅으면 닫힌 것으로 보고, 다음에 뚫릴 때 제자리에서 다시 흐른다.
        private const float HoleClosed = 0.001f;

        // 바다에 닿는 햇빛: 맑은 대기의 연직 광학 두께(레일리 + 미 + 오존, 빨강·초록·파랑). 셰이더의 대기와 같은 값이다.
        private static readonly Vector3 VerticalOpticalDepth = new Vector3(0.069f, 0.162f, 0.274f);

        // 카스텐-영 공기 질량 식의 계수
        private const float AirMassA = 0.50572f;
        private const float AirMassB = 6.07995f;
        private const float AirMassC = 1.6364f;

        // 탑 모양 시드 후보. 0~15를 모두 그려 보고 탑으로 잘 읽히는 것만 골랐다
        // (목이 가늘어 버섯처럼 보이거나 큰 구멍이 뚫리는 모양은 뺐다).
        private static readonly float[] ShapeSeeds = { 1f, 3f, 5f, 6f, 11f, 12f, 13f, 15f };

        [SerializeField] private WeatherController weather;

        [Tooltip("쉬는 중인지 읽을 상태머신")]
        [SerializeField] private PomodoroStateMachine stateMachine;

        [Tooltip("창 전체 유리 설정을 받을 게임 관리자")]
        [SerializeField] private GameManager gameManager;

        [Tooltip("바탕화면을 읽어 흐리게 까는 유리. 창 전체 유리일 때만 켠다(아니면 하늘과 바다가 창을 다 덮는다).")]
        [SerializeField] private GameObject desktopGlass;

        [Tooltip("하루의 흐름을 읽을 해. 없으면 한낮으로 본다.")]
        [SerializeField] private SunOrbitController sun;

        [Tooltip("Hidden/SAIUN/Sky 재질. 실행 중에는 복사본을 쓴다.")]
        [SerializeField] private Material skyMaterial;

        [Tooltip("구름 결 노이즈(3D). SAIUN/Rebuild Sky Noise가 만든다.")]
        [SerializeField] private Texture3D noise;

        [Header("그리기")]
        [Tooltip("창 크기 대비 하늘 텍스처 해상도")]
        [SerializeField, Range(0.25f, 1f)] private float resolutionScale = 1f;

        [Tooltip("1/8씩 새로 그리는 간격(초). 화소 하나는 이 값의 8배마다 새로 그려진다.")]
        [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;

        [Tooltip("구름이 피어오르고 결이 바뀌는 빠르기(셰이더 시간/초). 흘러가는 빠르기는 아래 바람이 정한다.")]
        [SerializeField, Min(0f)] private float evolveSpeed = 0.15f;

        [Header("바람 (구름층마다 다른 빠르기)")]
        [Tooltip("낮은 구름(적운·층적운·층운·난층운)이 흐르는 빠르기(km/분): x 바람이 없을 때, y 가장 셀 때. 땅 바람을 탄다.")]
        [SerializeField] private Vector2 lowWindSpeed = new Vector2(0.15f, 0.5f);

        [Tooltip("중층 구름(고적운·고층운·물결구름)이 흐르는 빠르기(km/분): x 바람이 없을 때, y 가장 셀 때")]
        [SerializeField] private Vector2 midWindSpeed = new Vector2(0.35f, 0.8f);

        [Tooltip("높은 구름(권운·권적운·권층운)이 흐르는 빠르기(km/분). 제트기류를 타 가장 빠르다.")]
        [SerializeField] private Vector2 highWindSpeed = new Vector2(0.9f, 1.5f);

        [Tooltip("중층 바람이 땅 바람에서 시계 방향으로 비껴 도는 각(도). 높이 오를수록 바람이 돈다.")]
        [SerializeField] private float midWindVeer = 25f;

        [Tooltip("높은 층 바람이 땅 바람에서 비껴 도는 각(도)")]
        [SerializeField] private float highWindVeer = 50f;

        [Tooltip("중층 바람이 땅 바람의 방향 바뀜을 따라가는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float midWindLag = 180f;

        [Tooltip("높은 층 바람이 따라가는 데 걸리는 시간(초). 높은 바람은 땅 바람보다 한결같다.")]
        [SerializeField, Min(0.01f)] private float highWindLag = 480f;

        [Tooltip("바다의 잔물결이 땅 바람을 따라 흐르는 빠르기(m/s): x 바람이 없을 때, y 가장 셀 때. 물가로 오는 너울은 바람과 따로다.")]
        [SerializeField] private Vector2 seaChopSpeed = new Vector2(0.6f, 2.4f);

        [Tooltip("해가 화면 가운데에서 가로로 이 각(도) 안이면 '해가 가까이 보인다'로 본다(해 둘레 채운 조각)")]
        [SerializeField, Range(10f, 90f)] private float sunNearAzimuth = 50f;

        [Header("구름층 높이")]
        [SerializeField] private CloudHeights cloudHeights = new CloudHeights();

        [Header("웅대적운")]
        [Tooltip("탑의 자람·보이는 정도가 예보를 따라가는 데 걸리는 시간(초). 예보가 이미 천천히 바꾸므로 짧게 둔다.")]
        [SerializeField, Min(0.01f)] private float growthResponse = 4f;

        [Header("채운 갓구름")]
        [Tooltip("탑이 이만큼 자라면 꼭대기에 갓구름이 얹히기 시작한다")]
        [SerializeField, Range(0f, 1f)] private float capGrowth = 0.8f;

        [Tooltip("갓구름이 다 나타나는 데 필요한 자람 폭")]
        [SerializeField, Range(0.01f, 0.5f)] private float capFadeRange = 0.1f;

        [Header("구름 예보")]
        [Tooltip("어떤 구름이 언제 얼마나 뜰지 정하는 예보")]
        [SerializeField] private CloudForecast forecast = new CloudForecast();

        /// <summary>지금 탑이 자란 정도(0~1).</summary>
        public float TowerGrowth { get; private set; }

        /// <summary>지금 채운 갓구름의 세기(0~1). 0이면 갓구름이 없다.</summary>
        public float CapVisibility { get; private set; }

        /// <summary>웅대적운 탑이 보이는 정도(0~1). 층구름·비구름이 덮으면 흐려진다.</summary>
        public float TowerPresence { get; private set; }

        /// <summary>창 전체가 유리인지(하늘빛 없이 구름만 뜬다).</summary>
        public bool WindowGlass { get; private set; }

        /// <summary>낮은 구름층이 바람에 흘러간 거리(하늘 좌표 x·z, km).</summary>
        public Vector2 LowDrift { get; private set; }

        /// <summary>중층 구름이 흘러간 거리(km).</summary>
        public Vector2 MidDrift { get; private set; }

        /// <summary>높은 구름이 흘러간 거리(km).</summary>
        public Vector2 HighDrift { get; private set; }

        /// <summary>바다의 물결·윤슬·파도 시간(초).</summary>
        public float SeaTime { get; private set; }

        /// <summary>
        /// 바다의 잔물결이 바람을 따라 흘러간 거리(하늘 좌표 x·z, km). 셰이더의 잔물결 무늬가 되풀이되는 거리로 되감는다.
        /// 무늬를 바람 방향으로 돌리지 않고 이 거리만큼만 밀어, 바람이 바뀌어도 물결이 튀지 않는다.
        /// </summary>
        public Vector2 SeaDrift { get; private set; }

        /// <summary>하늘 좌표의 해 방향(+z 앞, +x 오른쪽, +y 위).</summary>
        public Vector3 SunDirection { get; private set; } = Vector3.up;

        /// <summary>하늘 좌표의 달 방향. 달이 없으면 바로 아래(지평선 밑)다.</summary>
        public Vector3 MoonDirection { get; private set; } = Vector3.down;

        /// <summary>하늘·구름·바다를 비추는 빛이 달인지(깊은 밤). 셰이더와 같은 기준이다.</summary>
        public bool MoonLights => Night() > MoonLightNight;

        /// <summary>구름 예보.</summary>
        public CloudForecast Forecast => forecast;

        /// <summary>지금 하늘에 뜬 구름의 양(0~1). 예보의 목표로 천천히 옮겨 간다.</summary>
        public float Coverage(CloudKind kind) => _coverage[(int)kind];

        /// <summary>다음에 새로 그릴 칸(0~7).</summary>
        public int Phase { get; private set; }

        /// <summary>카드에 보이는 하늘 텍스처.</summary>
        public RenderTexture Target => _display;

        /// <summary>지난 장에서 지금 장으로 넘어간 정도(0~1).</summary>
        public float Blend { get; private set; } = 1f;

        private RawImage _image;
        private Material _material;
        // 그리는 중인 장의 하늘 매개변수. 장을 시작할 때 _material에서 한 번 복사해 그 장의 여덟 칸을 모두 같은 순간으로 그린다.
        private Material _sheetMaterial;
        private RenderTexture _work;      // 1/8씩 그리는 중인 장
        private RenderTexture _current;   // 마지막으로 다 그린 장
        private RenderTexture _previous;  // 그 앞에 다 그린 장
        private RenderTexture _display;   // 둘을 섞어 카드에 보이는 장
        private int _towerEvents;
        private float _skyTime;
        private float _midWindAngle;
        private float _highWindAngle;
        private Vector2 _holeDrift;
        private float _sinceRefresh;
        private Vector3 _sheetSun = Vector3.up;
        private float _sheetNight;
        private readonly float[] _coverage = new float[CloudForecast.KindCount];

        // 구름층 높이를 고를 때 쓰는 대리자(매 프레임 새로 만들지 않게 담아 둔다)
        private static readonly System.Func<float> RandomValue = () => Random.value;
        private System.Func<CloudKind, float> _coverageOf;

        private void Awake()
        {
            _image = GetComponent<RawImage>();
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
            if (stateMachine == null) stateMachine = FindFirstObjectByType<PomodoroStateMachine>();
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager != null) gameManager.OnWindowGlassChanged += ApplyGlass;
            ApplyGlass(SettingsStore.WindowGlass);
            if (sun == null) sun = FindFirstObjectByType<SunOrbitController>();
            if (skyMaterial == null)
            {
                Debug.LogError("SkyView: 하늘 재질이 없습니다.");
                enabled = false;
                return;
            }

            // 에셋을 건드리지 않도록 실행 중에는 복사본을 쓴다.
            _material = new Material(skyMaterial) { name = "Sky (runtime)" };
            _sheetMaterial = new Material(skyMaterial) { name = "Sky (sheet)" };
            if (noise != null) _material.SetTexture(NoiseId, noise);
            _material.SetFloat(SeedId, ShapeSeeds[Random.Range(0, ShapeSeeds.Length)]);

            Vector2 size = CardSize() * resolutionScale;
            CreateTargets(new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(size.x)), Mathf.Max(1, Mathf.RoundToInt(size.y))));

            // 처음에는 예보의 목표 그대로 시작한다(켜자마자 구름이 몰려오는 모습이 보이지 않게).
            forecast.Tick(0f, Inputs());
            for (int i = 0; i < _coverage.Length; i++) _coverage[i] = forecast.Target((CloudKind)i);
            _coverageOf = Coverage;
            cloudHeights.Reset(sun != null ? sun.Progress : 0.5f, RandomValue);
            TowerPresence = forecast.TowerPresence;
            TowerGrowth = forecast.TowerGrowth;
            _towerEvents = forecast.TowerEvents;
            float surface = WindAngle();
            _midWindAngle = surface + midWindVeer;
            _highWindAngle = surface + highWindVeer;
            UpdateCap(Storminess());
            ApplyParameters();
        }

        private void Start()
        {
            RenderAll();
        }

        private void OnDestroy()
        {
            ReleaseTargets();
            if (_material != null) Destroy(_material);
            if (_sheetMaterial != null) Destroy(_sheetMaterial);
            if (gameManager != null) gameManager.OnWindowGlassChanged -= ApplyGlass;
        }

        /// <summary>창 전체를 유리로 하거나 되돌린다. 유리면 하늘빛 없이 구름만 바탕화면 위에 뜬다.</summary>
        internal void ApplyGlass(bool on)
        {
            WindowGlass = on;
            if (desktopGlass != null) desktopGlass.SetActive(on);
            if (_material != null) _material.SetFloat(GlassId, on ? 1f : 0f);
            if (_material != null && _display != null) RenderAll();
        }

        /// <summary>
        /// 하늘의 눈: x 지평선에서 올려다보는 각(도), y 초점(화면 높이 1 기준), z 오른쪽으로 돈 각(도). 정원 카메라와 같아야 한다.
        /// </summary>
        public Vector3 View { get; private set; } = new Vector3(SceneMetrics.CameraTiltUpDegrees, SceneMetrics.CameraFocal, 0f);

        /// <summary>하늘의 눈을 바꾼다(창 모양이 바뀔 때). 곧바로 한 장 다시 그린다.</summary>
        internal void SetView(float tiltUpDegrees, float focal, float yawDegrees)
        {
            View = new Vector3(tiltUpDegrees, focal, yawDegrees);
            if (_material == null) return;
            _material.SetVector(ViewId, new Vector4(View.x, View.y, View.z, 0f));
            if (_display != null) RenderAll();
        }

        /// <summary>하늘 텍스처 크기를 바꾼다(창 모양이 바뀔 때). 지난 장들은 버리고 새로 그린다.</summary>
        internal void Resize(Vector2Int size)
        {
            if (_material == null) return;
            if (_display != null && _display.width == size.x && _display.height == size.y) return;
            ReleaseTargets();
            CreateTargets(new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y)));
            RenderAll();
        }

        private void CreateTargets(Vector2Int size)
        {
            _work = NewTarget("Sky Work", size);
            _current = NewTarget("Sky Current", size);
            _previous = NewTarget("Sky Previous", size);
            _display = NewTarget("Sky", size);
            _material.SetVector(SkySizeId, new Vector4(size.x, size.y, 0f, 0f));
            _image.texture = _display;
        }

        private void ReleaseTargets()
        {
            foreach (RenderTexture target in new[] { _work, _current, _previous, _display })
            {
                if (target == null) continue;
                target.Release();
                Destroy(target);
            }
        }

        // 셰이더가 톤매핑까지 마친 값을 쓰고, sRGB 텍스처가 화면용으로 바꿔 둔다. 지난 그림을 남겨야 하므로 지우지 않는다.
        private static RenderTexture NewTarget(string targetName, Vector2Int size)
        {
            var target = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = targetName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            target.Create();
            return target;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>탑을 키우거나 가라앉히고, 때가 되면 하늘의 다음 1/8을 그린다. 테스트는 직접 부른다.</summary>
        internal void Tick(float deltaTime)
        {
            if (_material == null) return;
            float storm = Storminess();
            _skyTime += deltaTime * evolveSpeed;
            SeaTime += deltaTime;
            UpdateWind(deltaTime);
            UpdateClouds(deltaTime);
            UpdateCap(storm);
            ApplyParameters();

            _sinceRefresh += deltaTime;
            if (_sinceRefresh >= refreshInterval)
            {
                _sinceRefresh = 0f;
                RenderPhase();
            }

            Blend = Mathf.Min(1f, Blend + deltaTime / (refreshInterval * Interleave));
            Present();
        }

        /// <summary>하늘 전체를 한 번에 그려 곧바로 보인다(처음, 또는 크게 바뀌었을 때).</summary>
        internal void RenderAll()
        {
            if (_material == null) return;
            BeginSheet();
            _sheetMaterial.SetFloat(PhaseId, AllPhases);
            Graphics.Blit(null, _work, _sheetMaterial, SkyPass);
            Graphics.Blit(_work, _current);
            Graphics.Blit(_work, _previous);
            Phase = 0;
            Blend = 1f;
            Present();
        }

        // 격자의 한 칸(화소 1/8)만 새로 그리고 다음 칸으로 넘어간다. 한 바퀴를 다 그리면 그 장을 지금 장으로 올리고
        // 지금 장이던 것을 지난 장으로 내려 처음부터 다시 섞는다.
        // 한 장의 여덟 칸은 장을 시작할 때 고정한 매개변수로 그린다. 그동안에도 구름은 바람에 흐르므로, 칸마다 그 순간의
        // 매개변수로 그리면 구름 가장자리가 칸마다 한두 화소씩 어긋나 4화소 간격 빗살이 되고, 빛내림이 그 빗살을 해 쪽으로
        // 늘인다. 움직임은 다 그린 장끼리 섞어 넘기며 보인다.
        // 한 장을 그리는 동안 빛이 크게 바뀌면(세션 시작·끝의 빨리 감기) 고정한 하늘이 금세 낡으므로 지금 값으로 한 번에 그린다.
        private void RenderPhase()
        {
            if (Phase == 0) BeginSheet();
            bool lightMoved = Vector3.Angle(SunDirection, _sheetSun) > SheetLightTolerance
                              || Mathf.Abs(Night() - _sheetNight) > SheetNightTolerance;
            if (lightMoved) BeginSheet();
            _sheetMaterial.SetFloat(PhaseId, lightMoved ? AllPhases : Phase);
            Graphics.Blit(null, _work, _sheetMaterial, SkyPass);
            Phase = lightMoved ? 0 : (Phase + 1) % Interleave;
            if (Phase != 0) return;

            RenderTexture oldest = _previous;
            _previous = _current;
            _current = _work;
            _work = oldest;
            Blend = 0f;
        }

        // 새 장을 시작한다: 지금 매개변수를 고정하고 그때의 해·밤을 기억한다.
        private void BeginSheet()
        {
            _sheetMaterial.CopyPropertiesFromMaterial(_material);
            _sheetSun = SunDirection;
            _sheetNight = Night();
        }

        // 지난 장과 지금 장을 섞어 보이는 장에 쓴다.
        private void Present()
        {
            _material.SetTexture(PrevTexId, _previous);
            _material.SetFloat(BlendId, Blend);
            Graphics.Blit(_current, _display, _material, MixPass);
        }

        private void ApplyParameters()
        {
            _material.SetFloat(ProgressId, sun != null ? sun.Progress : 0.5f);
            SunDirection = SkySunDirection();
            MoonDirection = SkyMoonDirection();
            _material.SetVector(SunDirId, SunDirection);
            _material.SetVector(MoonId, new Vector4(MoonDirection.x, MoonDirection.y, MoonDirection.z, sun != null ? sun.MoonLit : 0f));
            _material.SetVector(AltHighId, new Vector4(cloudHeights.Altitude(CloudKind.Cirrus),
                cloudHeights.Altitude(CloudKind.Cirrocumulus), cloudHeights.Altitude(CloudKind.Cirrostratus), 0f));
            _material.SetVector(AltLowId, new Vector4(cloudHeights.Altitude(CloudKind.Altocumulus),
                cloudHeights.Altitude(CloudKind.Altostratus), cloudHeights.Altitude(CloudKind.Stratocumulus), cloudHeights.CumulusBase));
            _material.SetFloat(GlassId, WindowGlass ? 1f : 0f);
            _material.SetVector(DriftId, new Vector4(LowDrift.x, LowDrift.y, MidDrift.x, MidDrift.y));
            _material.SetVector(DriftHighId, new Vector4(HighDrift.x, HighDrift.y, _holeDrift.x, _holeDrift.y));
            _material.SetVector(SeaWindId, new Vector4(SeaDrift.x, SeaDrift.y, weather != null ? weather.WindAmount : 0f, 0f));
            _material.SetFloat(SeaTimeId, SeaTime);
            // 물을 비추는 빛(깊은 밤이면 달)이 대기를 지나 남은 빛깔
            Vector3 light = MoonLights ? MoonDirection : SunDirection;
            _material.SetVector(SunColorId, SunlightAtSea(Mathf.Asin(Mathf.Clamp(light.y, -1f, 1f)) * Mathf.Rad2Deg));
            _material.SetVector(ViewId, new Vector4(View.x, View.y, View.z, 0f));
            _material.SetFloat(GrowthId, TowerGrowth);
            _material.SetFloat(StormId, Storminess());
            _material.SetFloat(CapId, CapVisibility);
            _material.SetFloat(SkyTimeId, _skyTime);
            _material.SetVector(HighId, Pack(CloudKind.Cirrus, CloudKind.Cirrocumulus, CloudKind.Cirrostratus, CloudKind.Noctilucent));
            _material.SetVector(MidId, Pack(CloudKind.Altocumulus, CloudKind.Altostratus, CloudKind.IridescentVeil, CloudKind.Virga));
            _material.SetVector(LowId, Pack(CloudKind.Stratocumulus, CloudKind.Stratus, CloudKind.Cumulus, CloudKind.Nimbostratus));
            _material.SetVector(SpecialId, Pack(CloudKind.Anvil, CloudKind.Mammatus, CloudKind.Arcus, CloudKind.FallstreakHole));
            _material.SetVector(ExtraId, new Vector4(Coverage(CloudKind.KelvinHelmholtz), Twilight(), TowerPresence, Night()));
            _material.SetVector(VeilPlaceId, forecast.VeilPlace);
            _material.SetFloat(VeilFormId, (float)forecast.VeilForm);
        }

        private Vector4 Pack(CloudKind x, CloudKind y, CloudKind z, CloudKind w)
        {
            return new Vector4(Coverage(x), Coverage(y), Coverage(z), Coverage(w));
        }

        // 층마다 제 바람으로 구름을 흘려 보낸다. 낮은 층은 땅 바람 그대로, 위층은 비껴 돌고 방향을 천천히 따라간다.
        // 구멍구름은 뚫린 뒤로 중층과 함께 흘러간다.
        private void UpdateWind(float deltaTime)
        {
            float amount = weather != null ? weather.WindAmount : 0f;
            float surface = WindAngle();
            _midWindAngle = Mathf.LerpAngle(_midWindAngle, surface + midWindVeer, 1f - Mathf.Exp(-deltaTime / midWindLag));
            _highWindAngle = Mathf.LerpAngle(_highWindAngle, surface + highWindVeer, 1f - Mathf.Exp(-deltaTime / highWindLag));

            float minutes = deltaTime / SecondsPerMinute;
            LowDrift += Heading(surface) * (Mathf.Lerp(lowWindSpeed.x, lowWindSpeed.y, amount) * minutes);
            Vector2 mid = Heading(_midWindAngle) * (Mathf.Lerp(midWindSpeed.x, midWindSpeed.y, amount) * minutes);
            MidDrift += mid;
            HighDrift += Heading(_highWindAngle) * (Mathf.Lerp(highWindSpeed.x, highWindSpeed.y, amount) * minutes);
            _holeDrift = Coverage(CloudKind.FallstreakHole) > HoleClosed ? _holeDrift + mid : Vector2.zero;

            Vector2 sea = SeaDrift + Heading(surface) * (Mathf.Lerp(seaChopSpeed.x, seaChopSpeed.y, amount) / MetersPerKilometer * deltaTime);
            SeaDrift = new Vector2(Mathf.Repeat(sea.x, SeaDriftPeriod), Mathf.Repeat(sea.y, SeaDriftPeriod));
        }

        // 땅 바람이 불어 가는 방향을 하늘 좌표의 방위(도, +z에서 +x 쪽으로)로 옮긴다. 바람이 없으면 앞(+z)으로 본다.
        private float WindAngle()
        {
            Vector2 sky = ToSky(weather != null ? weather.WindDirection : Vector3.forward);
            return Mathf.Atan2(sky.x, sky.y) * Mathf.Rad2Deg;
        }

        // 방위(도)의 하늘 좌표 단위 벡터(x, z)
        private static Vector2 Heading(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        // 월드 수평 방향을 하늘 좌표(x 오른쪽, z 앞)로 옮긴다. 하늘은 정원 카메라가 보는 가로 방향을 앞으로 삼는다.
        private static Vector2 ToSky(Vector3 world)
        {
            Quaternion camera = SceneMetrics.CameraRotation;
            Vector3 forward = Vector3.ProjectOnPlane(camera * Vector3.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(camera * Vector3.right, Vector3.up).normalized;
            return new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, forward));
        }

        // 예보를 한 걸음 진행하고, 구름마다 제 빠르기로 목표를 따라간다.
        private void UpdateClouds(float deltaTime)
        {
            forecast.Tick(deltaTime, Inputs());
            for (int i = 0; i < _coverage.Length; i++)
            {
                var kind = (CloudKind)i;
                _coverage[i] = Mathf.Lerp(_coverage[i], forecast.Target(kind), 1f - Mathf.Exp(-deltaTime / forecast.ResponseSeconds(kind)));
            }
            cloudHeights.Tick(deltaTime, sun != null ? sun.Progress : 0.5f, _coverageOf ??= Coverage, RandomValue);
            // 탑은 예보가 이미 천천히 키우고 줄이므로 짧게 따라간다. 새로 솟을 때마다 다른 모양을 고른다.
            float follow = 1f - Mathf.Exp(-deltaTime / growthResponse);
            TowerPresence = Mathf.Lerp(TowerPresence, forecast.TowerPresence, follow);
            TowerGrowth = Mathf.Lerp(TowerGrowth, forecast.TowerGrowth, follow);
            if (forecast.TowerEvents != _towerEvents)
            {
                _towerEvents = forecast.TowerEvents;
                _material.SetFloat(SeedId, ShapeSeeds[Random.Range(0, ShapeSeeds.Length)]);
            }
        }

        private SkyInputs Inputs()
        {
            return new SkyInputs
            {
                Progress = sun != null ? sun.Progress : 0f,
                Twilight = Twilight(),
                Storm = Storminess(),
                Rain = weather != null ? weather.RainIntensity : 0f,
                Wind = weather != null ? weather.WindAmount : 0f,
                Resting = Resting(),
                Idle = stateMachine == null || stateMachine.CurrentState == PomodoroState.Idle,
                SunNear = SunNear(),
            };
        }

        // 해가 화면 안이나 바로 곁(가로로 sunNearAzimuth° 안)에 떠 있는지
        private bool SunNear()
        {
            Vector3 sunDir = SkySunDirection();
            float azimuth = Mathf.DeltaAngle(View.z, Mathf.Atan2(sunDir.x, sunDir.z) * Mathf.Rad2Deg);
            return Mathf.Abs(azimuth) <= sunNearAzimuth && sunDir.y > 0f;
        }

        // 시계(세션 밖)나 휴식이면 쉬는 중이다.
        private bool Resting()
        {
            if (stateMachine == null) return true;
            PomodoroState state = stateMachine.CurrentState;
            return state == PomodoroState.Idle || state == PomodoroState.ShortBreak || state == PomodoroState.LongBreak;
        }

        /// <summary>
        /// 하늘 좌표의 해 방향. 방위는 정원의 해를 카메라가 보는 가로 방향 기준으로 옮기고, 고도는 해의 하늘 고도
        /// (SunOrbitController.SkyElevation: 아침 → 한낮 → 저녁, 박명·밤이면 지평선 아래)다. 해가 없으면 한낮으로 본다.
        /// </summary>
        internal Vector3 SkySunDirection()
        {
            if (sun == null) return Direction(0f, SkyArc.Default.Noon);
            Vector2 toSun = ToSky(-sun.LightDirection);
            return Direction(Mathf.Atan2(toSun.x, toSun.y) * Mathf.Rad2Deg, sun.SkyElevation);
        }

        /// <summary>하늘 좌표의 달 방향. 달은 시계 하늘에서 해와 같은 길을 늦게 따라간다. 지고 없으면 바로 아래다.</summary>
        internal Vector3 SkyMoonDirection()
        {
            if (sun == null || !sun.MoonUp) return Vector3.down;
            Vector2 toMoon = ToSky(sun.MoonTowardDirection);
            return Direction(Mathf.Atan2(toMoon.x, toMoon.y) * Mathf.Rad2Deg, sun.MoonSkyElevation);
        }

        // 방위(도, 앞 0°·오른쪽 +)와 고도(도)의 하늘 좌표 방향
        private static Vector3 Direction(float azimuthDegrees, float elevationDegrees)
        {
            float el = elevationDegrees * Mathf.Deg2Rad;
            float az = azimuthDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        }

        /// <summary>
        /// 대기를 지나 바다에 닿는 햇빛의 빛깔(0~1, 빨강·초록·파랑). 해가 낮을수록 공기를 길게 지나 붉어진다.
        /// 해가 지평선 아래면 지평선에 걸린 해로 셈한다(셰이더가 해가 진 만큼 끈다).
        /// </summary>
        internal static Vector4 SunlightAtSea(float elevationDegrees)
        {
            float elevation = Mathf.Max(elevationDegrees, 0f);
            float airMass = 1f / (Mathf.Sin(elevation * Mathf.Deg2Rad) + AirMassA * Mathf.Pow(elevation + AirMassB, -AirMassC));
            return new Vector4(
                Mathf.Exp(-VerticalOpticalDepth.x * airMass),
                Mathf.Exp(-VerticalOpticalDepth.y * airMass),
                Mathf.Exp(-VerticalOpticalDepth.z * airMass),
                0f);
        }

        private float Twilight()
        {
            return sun != null ? sun.Twilight : 0f;
        }

        private float Night()
        {
            return sun != null ? sun.Night : 0f;
        }

        // 다 자라면 꼭대기에 채운 갓구름이 얹힌다. 먹구름이 오면 사라진다.
        private void UpdateCap(float storm)
        {
            float cap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(capGrowth, capGrowth + capFadeRange, TowerGrowth));
            CapVisibility = cap * (1f - storm) * TowerPresence;
        }

        private float Storminess()
        {
            return weather != null ? weather.Storminess : 0f;
        }

        // 캔버스 배치 전(Awake)에는 크기가 0일 수 있다. 카드는 창 크기라 그 값을 대신 쓴다.
        private Vector2 CardSize()
        {
            Rect rect = ((RectTransform)transform).rect;
            return rect.width > 1f && rect.height > 1f
                ? rect.size
                : new Vector2(SceneMetrics.WindowWidth, SceneMetrics.WindowHeight);
        }
    }
}
