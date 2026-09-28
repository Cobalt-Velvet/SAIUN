using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Lighting;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 카드 위쪽의 하늘 (2026-09-28, 사용자: "구름이 이 프로그램의 50%", "가슴이 웅장해지고 노스탤지어를 느껴야").
    /// 셰이더(Hidden/SAIUN/Sky)가 웅대적운 탑과 채운 갓구름을 부피로 그리고, 지평선 아래로는 투명해져 유리로 이어진다.
    ///  - 세션 진행률(해)이 하루다: 아침 금빛 → 한낮 파랑 → 늦은 오후 금빛 → 해 질 녘 노을.
    ///  - 웅대적운: 드물게, 특별하게. 맑은 날 한낮~오후에 가끔 솟아 몇 분에 걸쳐 자라고, 머물다 스러진다(예보가 정한다).
    ///    솟을 때마다 모양이 다르다. 먹구름이 오면 적란운으로 끝까지 솟고 어두워진다.
    ///  - 채운: 탑과 따로, 왼쪽 빈 하늘의 렌즈구름 무리 가장자리에 빛깔 띠가 선다. 몇 분마다 피었다 사라지며 대부분의 시간 보인다.
    ///    탑이 다 자라면 꼭대기의 갓구름에도 채운이 선다(가끔 보는 장면). SAIUN을 만든 이유가 웅대적운과 채운이다.
    ///  - 나머지 구름(권운·권적운·권층운·고적운·고층운·층적운·층운·난층운·적운 떼, 모루·유방운·아치구름·꼬리구름,
    ///    구멍구름·물결구름·야광운)은 예보(CloudForecast)가 때·날씨·바람과 무작위로 정한 양을 향해 천천히 옮겨 간다.
    ///  - 쉬는 동안 해가 지면(해의 Twilight) 박명 하늘이 되고, 높은 구름만 붉게 남는다.
    /// 부피 그리기는 무거워 한 번에 화소의 1/8만 새로 그린다(여덟 번에 한 바퀴). 반쯤 새로 그린 장을 보이면 빗살이 지므로
    /// 다 그린 장끼리만 한 바퀴 동안 천천히 섞어 넘긴다(그리는 장 · 지난 장 · 지금 장 · 보이는 장).
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class SkyView : MonoBehaviour
    {
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int GrowthId = Shader.PropertyToID("_Growth");
        private static readonly int StormId = Shader.PropertyToID("_Storm");
        private static readonly int CapId = Shader.PropertyToID("_Cap");
        private static readonly int HighId = Shader.PropertyToID("_High");
        private static readonly int MidId = Shader.PropertyToID("_Mid");
        private static readonly int LowId = Shader.PropertyToID("_Low");
        private static readonly int SpecialId = Shader.PropertyToID("_Special");
        private static readonly int ExtraId = Shader.PropertyToID("_Extra");
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

        // 셰이더에 "전부 그려라"를 알리는 칸 번호
        private const float AllPhases = -1f;

        // 탑 모양 시드 후보. 0~15를 모두 그려 보고 탑으로 잘 읽히는 것만 골랐다
        // (목이 가늘어 버섯처럼 보이거나 큰 구멍이 뚫리는 모양은 뺐다).
        private static readonly float[] ShapeSeeds = { 1f, 3f, 5f, 6f, 11f, 12f, 13f, 15f };

        [SerializeField] private WeatherController weather;

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

        [Tooltip("구름이 피어오르고 흐르는 빠르기(셰이더 시간/초)")]
        [SerializeField, Min(0f)] private float evolveSpeed = 0.15f;

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
        private RenderTexture _work;      // 1/8씩 그리는 중인 장
        private RenderTexture _current;   // 마지막으로 다 그린 장
        private RenderTexture _previous;  // 그 앞에 다 그린 장
        private RenderTexture _display;   // 둘을 섞어 카드에 보이는 장
        private int _towerEvents;
        private float _skyTime;
        private float _sinceRefresh;
        private readonly float[] _coverage = new float[CloudForecast.KindCount];

        private void Awake()
        {
            _image = GetComponent<RawImage>();
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
            if (sun == null) sun = FindFirstObjectByType<SunOrbitController>();
            if (skyMaterial == null)
            {
                Debug.LogError("SkyView: 하늘 재질이 없습니다.");
                enabled = false;
                return;
            }

            // 에셋을 건드리지 않도록 실행 중에는 복사본을 쓴다.
            _material = new Material(skyMaterial) { name = "Sky (runtime)" };
            if (noise != null) _material.SetTexture(NoiseId, noise);
            _material.SetFloat(SeedId, ShapeSeeds[Random.Range(0, ShapeSeeds.Length)]);

            Vector2 size = CardSize() * resolutionScale;
            var sizeInt = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(size.x)), Mathf.Max(1, Mathf.RoundToInt(size.y)));
            _work = NewTarget("Sky Work", sizeInt);
            _current = NewTarget("Sky Current", sizeInt);
            _previous = NewTarget("Sky Previous", sizeInt);
            _display = NewTarget("Sky", sizeInt);
            _material.SetVector(SkySizeId, new Vector4(sizeInt.x, sizeInt.y, 0f, 0f));
            _image.texture = _display;

            // 처음에는 예보의 목표 그대로 시작한다(켜자마자 구름이 몰려오는 모습이 보이지 않게).
            forecast.Tick(0f, Inputs());
            for (int i = 0; i < _coverage.Length; i++) _coverage[i] = forecast.Target((CloudKind)i);
            TowerPresence = forecast.TowerPresence;
            TowerGrowth = forecast.TowerGrowth;
            _towerEvents = forecast.TowerEvents;
            UpdateCap(Storminess());
            ApplyParameters();
        }

        private void Start()
        {
            RenderAll();
        }

        private void OnDestroy()
        {
            foreach (RenderTexture target in new[] { _work, _current, _previous, _display })
            {
                if (target == null) continue;
                target.Release();
                Destroy(target);
            }
            if (_material != null) Destroy(_material);
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
            _material.SetFloat(PhaseId, AllPhases);
            Graphics.Blit(null, _work, _material, SkyPass);
            Graphics.Blit(_work, _current);
            Graphics.Blit(_work, _previous);
            Phase = 0;
            Blend = 1f;
            Present();
        }

        // 격자의 한 칸(화소 1/8)만 새로 그리고 다음 칸으로 넘어간다. 한 바퀴를 다 그리면 그 장을 지금 장으로 올리고
        // 지금 장이던 것을 지난 장으로 내려 처음부터 다시 섞는다.
        private void RenderPhase()
        {
            _material.SetFloat(PhaseId, Phase);
            Graphics.Blit(null, _work, _material, SkyPass);
            Phase = (Phase + 1) % Interleave;
            if (Phase != 0) return;

            RenderTexture oldest = _previous;
            _previous = _current;
            _current = _work;
            _work = oldest;
            Blend = 0f;
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
            _material.SetFloat(GrowthId, TowerGrowth);
            _material.SetFloat(StormId, Storminess());
            _material.SetFloat(CapId, CapVisibility);
            _material.SetFloat(SkyTimeId, _skyTime);
            _material.SetVector(HighId, Pack(CloudKind.Cirrus, CloudKind.Cirrocumulus, CloudKind.Cirrostratus, CloudKind.Noctilucent));
            _material.SetVector(MidId, Pack(CloudKind.Altocumulus, CloudKind.Altostratus, CloudKind.Lenticular, CloudKind.Virga));
            _material.SetVector(LowId, Pack(CloudKind.Stratocumulus, CloudKind.Stratus, CloudKind.Cumulus, CloudKind.Nimbostratus));
            _material.SetVector(SpecialId, Pack(CloudKind.Anvil, CloudKind.Mammatus, CloudKind.Arcus, CloudKind.FallstreakHole));
            _material.SetVector(ExtraId, new Vector4(Coverage(CloudKind.KelvinHelmholtz), Twilight(), TowerPresence, 0f));
        }

        private Vector4 Pack(CloudKind x, CloudKind y, CloudKind z, CloudKind w)
        {
            return new Vector4(Coverage(x), Coverage(y), Coverage(z), Coverage(w));
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
            };
        }

        private float Twilight()
        {
            return sun != null ? sun.Twilight : 0f;
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
