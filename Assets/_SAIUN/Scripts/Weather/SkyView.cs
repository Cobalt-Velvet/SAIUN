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
    ///  - 웅대적운: 해가 오를수록(집중 세션이 무르익을수록) 자라고, 쉴 때도 몇 분 주기로 자랐다 가라앉아 늘 볼 수 있다.
    ///    먹구름이 오면 끝까지 솟고 어두워지며, 낮은 먹구름 층이 하늘을 덮는다.
    ///  - 채운: 탑과 따로, 왼쪽 빈 하늘의 렌즈구름 무리 가장자리에 빛깔 띠가 선다. 몇 분마다 피었다 사라지며 대부분의 시간 보인다.
    ///    탑이 다 자라면 꼭대기의 갓구름에도 채운이 선다(가끔 보는 장면). SAIUN을 만든 이유가 웅대적운과 채운이다.
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
        private static readonly int LensId = Shader.PropertyToID("_Lens");
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
        [Tooltip("쉴 때 탑이 자랐다 가라앉는 한 주기(분)")]
        [SerializeField, Min(0.1f)] private float idleCycleMinutes = 6f;

        [Tooltip("쉴 때 자람의 범위(가장 낮을 때~가장 높을 때)")]
        [SerializeField] private Vector2 idleGrowthRange = new Vector2(0.45f, 0.92f);

        [Tooltip("해 진행률이 이 구간을 지나는 동안 탑이 끝까지 자란다(오후 대류)")]
        [SerializeField] private Vector2 focusGrowthProgress = new Vector2(0.1f, 0.7f);

        [Tooltip("자람이 목표를 따라가는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float growthResponse = 20f;

        [Header("채운 갓구름")]
        [Tooltip("탑이 이만큼 자라면 꼭대기에 갓구름이 얹히기 시작한다")]
        [SerializeField, Range(0f, 1f)] private float capGrowth = 0.8f;

        [Tooltip("갓구름이 다 나타나는 데 필요한 자람 폭")]
        [SerializeField, Range(0.01f, 0.5f)] private float capFadeRange = 0.1f;

        [Header("채운 렌즈구름")]
        [Tooltip("렌즈구름이 피었다 사라지는 한 주기(분)")]
        [SerializeField, Min(0.1f)] private float lensCycleMinutes = 11f;

        [Tooltip("한 주기 가운데 렌즈구름이 떠 있는 비율(피어나고 사라지는 시간 포함)")]
        [SerializeField, Range(0.1f, 1f)] private float lensPresence = 0.72f;

        [Tooltip("피어나고 사라지는 데 걸리는 비율(주기 대비)")]
        [SerializeField, Range(0.01f, 0.3f)] private float lensFade = 0.1f;

        [Tooltip("처음 켰을 때 주기의 어디서 시작할지(0~1). 켜자마자 떠 있게 한다.")]
        [SerializeField, Range(0f, 1f)] private float lensStartPhase = 0.25f;

        /// <summary>지금 탑이 자란 정도(0~1).</summary>
        public float TowerGrowth { get; private set; }

        /// <summary>지금 채운 갓구름의 세기(0~1). 0이면 갓구름이 없다.</summary>
        public float CapVisibility { get; private set; }

        /// <summary>지금 채운 렌즈구름이 피어난 정도(0~1). 먹구름은 셰이더가 따로 가린다.</summary>
        public float LensVisibility { get; private set; }

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
        private float _cycleTime;
        private float _skyTime;
        private float _sinceRefresh;

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

            TowerGrowth = GrowthTarget(Storminess());
            UpdateCap(Storminess());
            UpdateLens();
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
            _cycleTime += deltaTime;
            _skyTime += deltaTime * evolveSpeed;
            TowerGrowth = Mathf.Lerp(TowerGrowth, GrowthTarget(storm), 1f - Mathf.Exp(-deltaTime / growthResponse));
            UpdateCap(storm);
            UpdateLens();
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
            _material.SetFloat(LensId, LensVisibility);
            _material.SetFloat(SkyTimeId, _skyTime);
        }

        // 다 자라면 꼭대기에 채운 갓구름이 얹힌다. 먹구름이 오면 사라진다.
        private void UpdateCap(float storm)
        {
            float cap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(capGrowth, capGrowth + capFadeRange, TowerGrowth));
            CapVisibility = cap * (1f - storm);
        }

        // 렌즈구름은 주기의 앞쪽 lensPresence 동안 떠 있고, 앞뒤 lensFade 동안 피어나고 사라진다.
        private void UpdateLens()
        {
            float phase = Mathf.Repeat(lensStartPhase + _cycleTime / (lensCycleMinutes * 60f), 1f);
            float fade = Mathf.Min(lensFade, lensPresence / 2f);
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, fade, phase));
            float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lensPresence - fade, lensPresence, phase));
            LensVisibility = Mathf.Min(rise, fall);
        }

        // 쉴 때는 몇 분 주기로 자랐다 가라앉고, 해가 오를수록 더 자란다. 먹구름이면 끝까지 솟는다.
        private float GrowthTarget(float storm)
        {
            float cycle = 0.5f - 0.5f * Mathf.Cos(_cycleTime / (idleCycleMinutes * 60f) * Mathf.PI * 2f);
            float idle = Mathf.Lerp(idleGrowthRange.x, idleGrowthRange.y, cycle);
            float progress = sun != null ? sun.Progress : 0f;
            float focus = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(focusGrowthProgress.x, focusGrowthProgress.y, progress));
            return Mathf.Lerp(Mathf.Max(idle, focus), 1f, storm);
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
