using System.Collections.Generic;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Lighting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 하늘의 구름 (사양서 v1.1 10장 "기본 항상 표시, 속도·밀도 설정 가능", 2026-09-27 웅대적운·채운).
    ///  - 적운(뭉게구름): 늘 떠 있고 바람을 따라 흐른다.
    ///  - 웅대적운: 지평선(영역 아래 끝)에서 솟는 탑. 해가 오를수록(집중 세션이 무르익을수록) 자라고,
    ///    쉴 때도 몇 분 주기로 자랐다 가라앉아 늘 볼 수 있다. 먹구름이 오면 끝까지 솟아 어두워진다.
    ///  - 채운 갓구름(pileus): 탑이 충분히 자라면 꼭대기에 얇은 갓구름이 얹히고 파스텔 무지갯빛으로 빛난다.
    ///    탑과 적운의 해 쪽 얇은 가장자리에도 채운이 옅게 돈다. SAIUN을 만든 이유가 이 둘이다(사용자, 2026-09-27).
    /// 모양은 셰이더(SAIUN/UI/Cumulus)가 노이즈로 그려 구름마다 다르고 시간에 따라 천천히 변한다.
    /// 셰이더에는 uvRect의 x 정수부로 모양 시드, y 정수부로 종류를, 꼭짓점 색 R로 자람, G로 채운 세기를 넘긴다.
    /// 해 방향에서 빛을 받아 명암이 지고, 광원 색(아침·저녁의 따뜻한 빛)에 물든다.
    /// 먹구름 정도가 오르면 어두워지고, 평소에는 숨어 있던 구름이 더 몰려온다.
    /// 밝은 구름이 밝은 시계 글자 뒤로 지나가면 글자가 묻히므로, 글자 바로 뒤에서만 글자가 드리운 그림자처럼 어두워지고
    /// 조금 옅어진다(셰이더가 화소별로 처리). 투명하게만 하면 큰 탑에 구멍이 뚫린 듯 보여 어둡게 하는 쪽을 주로 쓴다.
    /// 유리 배경 바로 위, 3D 씬 뒤에 그려지도록 배경 캔버스의 Sky Layer 영역에 둔다.
    /// </summary>
    public class CloudLayer : MonoBehaviour
    {
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int StormId = Shader.PropertyToID("_Storm");
        private static readonly int[] ClearRectIds = { Shader.PropertyToID("_ClearRect0"), Shader.PropertyToID("_ClearRect1") };
        private static readonly int ClearOpacityId = Shader.PropertyToID("_ClearOpacity");
        private static readonly int ClearShadeId = Shader.PropertyToID("_ClearShade");

        // 셰이더가 받는 글자 영역 수
        public const int MaxClearRects = 2;

        // 비어 있는 글자 영역. 화면 밖 멀리 두어 어떤 화소도 옅어지지 않는다.
        internal static readonly Vector4 NoClearRect = new Vector4(-10f, -10f, -10f, -10f);

        [SerializeField] private WeatherController weather;

        [Tooltip("빛 색과 방향을 읽을 해. 없으면 흰빛이 위에서 온다고 본다.")]
        [SerializeField] private SunOrbitController sun;

        [Tooltip("구름이 흐르는 영역. Sky Layer에 맞춘다.")]
        [SerializeField] private RectTransform area;

        [Tooltip("구름 한 조각의 틀. 구름 셰이더 머티리얼을 쓴다.")]
        [SerializeField] private RawImage cloudTemplate;

        [Header("적운")]
        [Tooltip("평소 떠 있는 구름 수(밀도)")]
        [SerializeField, Range(0, 16)] private int density = 5;

        [Tooltip("먹구름일 때 더 몰려오는 구름 수")]
        [SerializeField, Range(0, 16)] private int stormExtra = 4;

        [Tooltip("바람이 없어도 흐르는 속도(픽셀/초)")]
        [SerializeField, Min(0f)] private float baseSpeed = 4f;

        [Tooltip("바람 세기 1당 더해지는 속도(픽셀/초)")]
        [SerializeField, Min(0f)] private float speedPerWind = 6f;

        [Tooltip("구름 폭 범위(픽셀). 높이는 폭의 절반이다.")]
        [SerializeField] private Vector2 widthRange = new Vector2(150f, 280f);

        [Tooltip("영역 위에서부터 잰 구름 중심 높이 범위(픽셀)")]
        [SerializeField] private Vector2 depthRange = new Vector2(40f, 240f);

        [Tooltip("멀리 있는 구름일수록 느리게 흐르는 정도")]
        [SerializeField] private Vector2 parallaxRange = new Vector2(0.6f, 1.2f);

        [Tooltip("적운 가장자리의 채운 세기. 해 쪽 얇은 가장자리에만 옅게 돈다.")]
        [SerializeField, Range(0f, 1f)] private float cumulusIridescence = 0.12f;

        [Header("웅대적운")]
        [Tooltip("지평선에서 솟는 탑 구름 수")]
        [SerializeField, Range(0, 3)] private int towers = 1;

        [Tooltip("탑 틀 폭 범위(픽셀). 구름 덩어리는 틀 폭의 80% 안에 들고, 틀 높이는 폭의 1.3배로 자란 만큼 그 안을 채운다.")]
        [SerializeField] private Vector2 towerWidthRange = new Vector2(215f, 245f);

        [Tooltip("영역 위에서 잰 탑 밑면 깊이(픽셀). 지평선(영역 아래 끝) 가까이 둔다.")]
        [SerializeField, Min(0f)] private float towerBaseDepth = 325f;

        [Tooltip("멀리 있어 천천히 흐르는 정도")]
        [SerializeField, Range(0.1f, 1.5f)] private float towerParallax = 0.45f;

        [Tooltip("쉴 때 탑이 자랐다 가라앉는 한 주기(분)")]
        [SerializeField, Min(0.1f)] private float idleCycleMinutes = 6f;

        [Tooltip("쉴 때 자람의 범위(가장 낮을 때~가장 높을 때)")]
        [SerializeField] private Vector2 idleGrowthRange = new Vector2(0.45f, 0.92f);

        [Tooltip("해 진행률이 이 구간을 지나는 동안 탑이 끝까지 자란다(오후 대류)")]
        [SerializeField] private Vector2 focusGrowthProgress = new Vector2(0.1f, 0.7f);

        [Tooltip("자람이 목표를 따라가는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float growthResponse = 20f;

        [Tooltip("탑 가장자리의 채운 세기")]
        [SerializeField, Range(0f, 1f)] private float towerIridescence = 0.5f;

        [Tooltip("맑을 때 탑 불투명도")]
        [SerializeField, Range(0f, 1f)] private float towerOpacity = 0.95f;

        [Tooltip("다 자란 탑 둘레의 적운이 흩어지는 정도. 탑으로 오른 공기가 둘레로 내려앉아 작은 구름을 지운다(하강 기류).")]
        [SerializeField, Range(0f, 1f)] private float towerClearing = 0.85f;

        [Header("채운 갓구름")]
        [Tooltip("탑이 이만큼 자라면 꼭대기에 얇은 갓구름이 얹히기 시작한다")]
        [SerializeField, Range(0f, 1f)] private float capGrowth = 0.8f;

        [Tooltip("탑 폭 대비 갓구름 폭. 가장 높은 봉우리 머리와 양옆 봉우리 어깨까지 덮는다.")]
        [SerializeField, Min(0.1f)] private float capWidthRatio = 0.76f;

        [Tooltip("탑 머리 꼭대기에서 갓구름 틀 가운데까지의 높이(픽셀). 두건 안쪽이 머리에 닿을 듯 말 듯하게 둔다.")]
        [SerializeField] private float capLift = 4f;

        [Tooltip("갓구름 불투명도")]
        [SerializeField, Range(0f, 1f)] private float capOpacity = 0.9f;

        [Tooltip("갓구름의 채운 세기")]
        [SerializeField, Range(0f, 1f)] private float capIridescence = 1f;

        [Header("빛")]
        [Tooltip("구름에 비치는 햇빛의 밝기 배율. 해 색에 곱한다.")]
        [SerializeField, Min(0f)] private float sunlight = 1.05f;

        [Tooltip("맑을 때 구름 불투명도. 시계 글자 뒤로 지나가도 읽히도록 옅게 둔다.")]
        [SerializeField, Range(0f, 1f)] private float clearOpacity = 0.7f;

        [Tooltip("먹구름일 때 불투명도")]
        [SerializeField, Range(0f, 1f)] private float stormOpacity = 0.9f;

        [Header("글자 가림 방지")]
        [Tooltip("구름이 뒤로 지나갈 때 어두워질 글자(시계 등). 앞의 두 개까지 쓴다.")]
        [SerializeField] private TMP_Text[] keepClear = new TMP_Text[0];

        [Tooltip("글자 바로 뒤에서 남기는 불투명도 비율")]
        [SerializeField, Range(0f, 1f)] private float behindTextOpacity = 0.85f;

        [Tooltip("글자 바로 뒤에서 남기는 밝기 비율. 글자가 구름에 드리운 그림자처럼 어두워져 밝은 글자가 읽힌다.")]
        [SerializeField, Range(0f, 1f)] private float behindTextShade = 0.62f;

        /// <summary>지금 흐르는 속도(픽셀/초). 양수면 오른쪽.</summary>
        public float DriftSpeed { get; private set; }

        public int CloudCount => _clouds.Count;

        /// <summary>웅대적운 탑 수.</summary>
        public int TowerCount => _towers.Count;

        /// <summary>지금 탑이 자란 정도(0~1).</summary>
        public float TowerGrowth { get; private set; }

        /// <summary>지금 채운 갓구름의 불투명도. 0이면 갓구름이 없다.</summary>
        public float CapVisibility { get; private set; }

        /// <summary>구름을 비추는 해의 화면 방향(구름에서 해 쪽, 위가 +y).</summary>
        public Vector2 SunScreenDirection { get; private set; } = Vector2.up;

        private sealed class Cloud
        {
            public RectTransform Rect;
            public RawImage Image;
            public float Parallax;
            public bool Extra;
        }

        private sealed class Tower
        {
            public RectTransform Rect;
            public RawImage Image;
            public RectTransform CapRect;
            public RawImage Cap;
            public float Peak;          // 가장 높은 봉우리 머리의 가로 위치(틀 폭 절반 단위). 셰이더와 같은 식으로 구한다.
        }

        // 셰이더 종류 번호(uvRect의 y 정수부)
        internal const int KindCumulus = 0;
        internal const int KindCongestus = 1;
        internal const int KindCap = 2;

        // 틀의 세로/가로 비율. 셰이더의 종류별 높이(p.y 최댓값 = 비율 × 2)와 맞춘다.
        private const float CumulusAspect = 0.5f;
        private const float TowerAspect = 1.3f;
        private const float CapAspect = 0.5f;

        // 셰이더 Congestus의 꼭대기 높이: 자람 0에서 0.95, 1에서 2.45(틀 높이 2.6 중). 갓구름 자리를 셈할 때 쓴다.
        private const float TowerTopMin = 0.95f;
        private const float TowerTopMax = 2.45f;
        private const float TowerHeightUnits = 2.6f;
        private const float TowerLeanRange = 0.2f;
        // 셰이더 Congestus의 봉우리 셋: 가로 간격, 흔들림, 몇 번째가 가장 높은지 고르는 식의 계수
        private const float TurretSpacing = 0.3f;
        private const float TurretJitter = 0.08f;
        private const float TurretChoices = 2.999f;
        // 봉우리가 오를수록 가운데로 모여 머리에서는 처음 자리의 이 비율에 있다.
        private const float TurretConverge = 0.75f;

        // 셰이더 Rand의 번호(salt): 가장 높은 봉우리 고르기, 탑이 기운 정도, 봉우리마다의 흔들림(봉우리 번호를 더한다)
        private const int SaltTallest = 1;
        private const int SaltLean = 2;
        private const int SaltTurret = 70;

        // 갓구름이 나타나는 자람 구간의 폭
        private const float CapFadeRange = 0.1f;

        // 탑과 적운의 가로 거리가 두 폭 절반의 합에 대해 이 비율 안이면 하강 기류를 온전히 받는다.
        private const float ClearingCore = 0.5f;

        // 탑을 영역에 고르게 나눈 칸 안에서 가운데 이 비율 구간에 무작위로 둔다.
        private const float TowerSlotMargin = 0.3f;

        // 셰이더 Lattice와 같은 정수 해시 계수, 결과를 0~1로 옮기는 24비트 마스크
        private const uint LatticePrimeX = 374761393u;
        private const uint LatticePrimeY = 668265263u;
        private const uint LatticeMix = 1274126177u;
        private const int LatticeShiftA = 13;
        private const int LatticeShiftB = 16;
        private const uint LatticeMask = 0x00FFFFFFu;

        // 오른쪽·위 끝 픽셀이 다음 정수(다른 시드·종류)로 넘어가지 않게 폭·높이를 1보다 살짝 작게 둔다.
        private const float SeedUvWidth = 0.999f;
        private const int MaxSeed = 997;

        private readonly List<Cloud> _clouds = new List<Cloud>();
        private readonly List<Tower> _towers = new List<Tower>();
        private float _cycleTime;
        private readonly List<Rect> _textRects = new List<Rect>();
        private Material _material;

        // 캔버스 배치가 끝나기 전(Awake)에는 폭이 0일 수 있다. 캔버스 기준 해상도가 창 크기라 그 폭을 대신 쓴다.
        private float AreaWidth => area.rect.width > 0f ? area.rect.width : SceneMetrics.WindowWidth;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
            if (sun == null) sun = FindFirstObjectByType<SunOrbitController>();
            if (area == null) area = (RectTransform)transform;
            if (cloudTemplate != null)
            {
                cloudTemplate.gameObject.SetActive(false);
                // 에셋을 건드리지 않도록 실행 중에는 복사본을 쓴다. 모든 구름이 이 하나를 나눠 쓴다.
                Material shared = cloudTemplate.material;
                if (shared != null && shared != Graphic.defaultGraphicMaterial)
                {
                    _material = new Material(shared) { name = "CloudLayer (runtime)" };
                }
            }
            Build();
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>구름을 바람만큼 옮기고 빛을 맞춘다. 테스트는 직접 부른다.</summary>
        internal void Tick(float deltaTime)
        {
            if (weather == null || area == null) return;

            Quaternion camera = SceneMetrics.CameraRotation;

            // 바람의 화면 가로 성분. 화면 안쪽으로 부는 바람은 가로로는 거의 흐르지 않는다.
            float across = Vector3.Dot(weather.WindDirection, camera * Vector3.right);
            DriftSpeed = (across >= 0f ? baseSpeed : -baseSpeed) + across * weather.WindStrength * speedPerWind;

            ApplyLight(camera);

            float halfWidth = AreaWidth / 2f;
            float storm = weather.Storminess;
            float opacity = Mathf.Lerp(clearOpacity, stormOpacity, storm);
            CollectTextRects();
            ApplyClearRects(_textRects, new Vector2(Screen.width, Screen.height));
            foreach (Cloud cloud in _clouds)
            {
                Drift(cloud.Rect, cloud.Parallax, halfWidth, deltaTime);
                float alpha = (cloud.Extra ? opacity * storm : opacity) * (1f - Clearing(cloud.Rect, storm));
                cloud.Image.color = Params(0f, cumulusIridescence, alpha);
                cloud.Image.enabled = alpha > 0.001f;
            }

            TickTowers(deltaTime, halfWidth, storm);
        }

        // 탑을 목표 자람으로 천천히 키우거나 가라앉히고, 다 자라면 꼭대기에 채운 갓구름을 얹는다.
        private void TickTowers(float deltaTime, float halfWidth, float storm)
        {
            _cycleTime += deltaTime;
            TowerGrowth = Mathf.Lerp(TowerGrowth, GrowthTarget(storm), 1f - Mathf.Exp(-deltaTime / growthResponse));
            float cap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(capGrowth, capGrowth + CapFadeRange, TowerGrowth));
            CapVisibility = capOpacity * cap * (1f - storm);
            float alpha = Mathf.Lerp(towerOpacity, stormOpacity, storm);

            foreach (Tower tower in _towers)
            {
                Drift(tower.Rect, towerParallax, halfWidth, deltaTime);
                tower.Image.color = Params(TowerGrowth, towerIridescence, alpha);

                // 갓구름: 셰이더가 그리는 탑 꼭대기 바로 위, 탑이 기운 쪽으로.
                Rect frame = tower.Rect.rect;
                float top = Mathf.Lerp(TowerTopMin, TowerTopMax, TowerGrowth) / TowerHeightUnits * frame.height;
                Vector2 basePoint = tower.Rect.anchoredPosition;
                tower.CapRect.anchoredPosition = new Vector2(basePoint.x + tower.Peak * frame.width / 2f, basePoint.y + top + capLift);
                tower.Cap.color = Params(0f, capIridescence, CapVisibility);
                tower.Cap.enabled = CapVisibility > 0.001f;
            }
        }

        /// <summary>
        /// 적운이 탑 둘레의 하강 기류로 흩어진 정도(0~1). 탑과 가로로 겹칠수록, 탑이 자랄수록 세다.
        /// 탑 머리와 채운 갓구름이 작은 구름에 가려지지 않는다. 먹구름이면 모두 뒤섞이므로 흩어지지 않는다.
        /// </summary>
        internal float Clearing(RectTransform cloud, float storm)
        {
            float strongest = 0f;
            foreach (Tower tower in _towers)
            {
                float reach = (tower.Rect.rect.width + cloud.rect.width) / 2f;
                float distance = Mathf.Abs(cloud.anchoredPosition.x - tower.Rect.anchoredPosition.x);
                float overlap = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reach * ClearingCore, reach, distance));
                strongest = Mathf.Max(strongest, overlap);
            }
            return strongest * towerClearing * TowerGrowth * (1f - storm);
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

        // 바람만큼 옮기고, 화면을 완전히 벗어나면 반대편에서 다시 들어온다.
        private void Drift(RectTransform rect, float parallax, float halfWidth, float deltaTime)
        {
            Vector2 position = rect.anchoredPosition;
            position.x += DriftSpeed * parallax * deltaTime;
            float margin = halfWidth + rect.rect.width / 2f;
            if (position.x > margin) position.x -= margin * 2f;
            else if (position.x < -margin) position.x += margin * 2f;
            rect.anchoredPosition = position;
        }

        // 셰이더 매개변수를 꼭짓점 색에 싣는다: R 자람, G 채운 세기, A 불투명도.
        private static Color Params(float growth, float iridescence, float alpha)
        {
            return new Color(growth, iridescence, 1f, alpha);
        }

        // 글자가 실제로 그려진 범위를 화면 좌표로 모은다. 글자가 바뀌어도 따라간다.
        private void CollectTextRects()
        {
            _textRects.Clear();
            foreach (TMP_Text text in keepClear)
            {
                if (text == null || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
                Bounds bounds = text.textBounds;
                if (bounds.size.x <= 0f || bounds.size.y <= 0f) continue;
                Camera camera = CanvasCamera(text.rectTransform);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, text.rectTransform.TransformPoint(bounds.min));
                Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, text.rectTransform.TransformPoint(bounds.max));
                _textRects.Add(Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                    Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y)));
            }
        }

        /// <summary>글자 영역(화면 픽셀)을 화면 비율 좌표(xMin, yMin, xMax, yMax)로 셰이더에 넘긴다. 남는 칸은 비운다.</summary>
        internal void ApplyClearRects(IReadOnlyList<Rect> screenRects, Vector2 screenSize)
        {
            if (_material == null || screenSize.x <= 0f || screenSize.y <= 0f) return;
            _material.SetFloat(ClearOpacityId, behindTextOpacity);
            _material.SetFloat(ClearShadeId, behindTextShade);
            for (int i = 0; i < MaxClearRects; i++)
            {
                Vector4 rect = NoClearRect;
                if (i < screenRects.Count)
                {
                    Rect r = screenRects[i];
                    rect = new Vector4(r.xMin / screenSize.x, r.yMin / screenSize.y, r.xMax / screenSize.x, r.yMax / screenSize.y);
                }
                _material.SetVector(ClearRectIds[i], rect);
            }
        }

        // 오버레이 캔버스는 카메라 없이 월드 좌표가 곧 화면 좌표다.
        private static Camera CanvasCamera(Transform child)
        {
            Canvas canvas = child.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            canvas = canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        // 해의 색과 화면 속 방향을 셰이더에 넘긴다.
        private void ApplyLight(Quaternion camera)
        {
            Vector3 toSun = sun != null ? -sun.LightDirection : Vector3.up;
            var screen = new Vector2(Vector3.Dot(toSun, camera * Vector3.right), Vector3.Dot(toSun, camera * Vector3.up));
            SunScreenDirection = screen.sqrMagnitude > 0.0001f ? screen.normalized : Vector2.up;

            if (_material == null) return;
            Color light = (sun != null ? sun.SunColor : Color.white) * sunlight;
            light.a = 1f;
            _material.SetColor(SunColorId, light);
            _material.SetVector(SunDirId, SunScreenDirection);
            _material.SetFloat(StormId, weather.Storminess);
        }

        // 구름을 가로로 고르게 흩뿌린다. 위치·크기·모양은 무작위지만 한 번 정하면 흐르기만 한다.
        // 멀리 있는 탑을 먼저 만들어 적운 뒤에 그려지게 한다.
        private void Build()
        {
            if (cloudTemplate == null) return;

            BuildTowers();

            int total = density + stormExtra;
            float width = AreaWidth;
            for (int i = 0; i < total; i++)
            {
                RawImage image = NewPiece($"Cloud_{i}", KindCumulus, out _);

                // 먹구름용은 더 크고 낮게 깔린다.
                bool extra = i >= density;
                float cloudWidth = Random.Range(widthRange.x, widthRange.y) * (extra ? 1.25f : 1f);
                float depth = extra
                    ? Random.Range((depthRange.x + depthRange.y) / 2f, depthRange.y)
                    : Random.Range(depthRange.x, depthRange.y);

                var rect = image.rectTransform;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(cloudWidth, cloudWidth * CumulusAspect);
                rect.anchoredPosition = new Vector2(((i + Random.value) / total - 0.5f) * width, -depth);

                _clouds.Add(new Cloud
                {
                    Rect = rect,
                    Image = image,
                    Parallax = Random.Range(parallaxRange.x, parallaxRange.y),
                    Extra = extra,
                });
            }
        }

        // 탑은 밑면(피벗)을 지평선에 두고 위로 솟는다. 처음부터 지금 목표만큼 자란 채로 시작한다.
        private void BuildTowers()
        {
            float width = AreaWidth;
            TowerGrowth = GrowthTarget(weather != null ? weather.Storminess : 0f);
            for (int i = 0; i < towers; i++)
            {
                RawImage image = NewPiece($"Tower_{i}", KindCongestus, out int seed);
                float towerWidth = Random.Range(towerWidthRange.x, towerWidthRange.y);
                var rect = image.rectTransform;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(towerWidth, towerWidth * TowerAspect);
                float slot = TowerSlotMargin + Random.value * (1f - TowerSlotMargin * 2f);
                rect.anchoredPosition = new Vector2(((i + slot) / towers - 0.5f) * width, -towerBaseDepth);

                RawImage cap = NewPiece($"Cap_{i}", KindCap, out _);
                float capWidth = towerWidth * capWidthRatio;
                var capRect = cap.rectTransform;
                capRect.pivot = new Vector2(0.5f, 0.5f);
                capRect.sizeDelta = new Vector2(capWidth, capWidth * CapAspect);
                cap.enabled = false;

                _towers.Add(new Tower
                {
                    Rect = rect,
                    Image = image,
                    CapRect = capRect,
                    Cap = cap,
                    Peak = TallestTurret(seed),
                });
            }
        }

        // 구름 한 조각. uvRect의 x 정수부가 모양 시드, y 정수부가 종류다.
        private RawImage NewPiece(string pieceName, int kind, out int seed)
        {
            RawImage image = Instantiate(cloudTemplate, area);
            image.name = pieceName;
            image.gameObject.SetActive(true);
            image.raycastTarget = false;
            if (_material != null) image.material = _material;
            seed = Random.Range(1, MaxSeed);
            image.uvRect = new Rect(seed, kind, SeedUvWidth, SeedUvWidth);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            return image;
        }

        // 가장 높은 봉우리 머리의 가로 위치. 셰이더 Congestus와 같은 식이라 갓구름을 그 머리 위에 얹을 수 있다.
        internal static float TallestTurret(int seed)
        {
            int tallest = Mathf.FloorToInt(ShapeRandom(seed, SaltTallest) * TurretChoices);
            float jitter = (ShapeRandom(seed, SaltTurret + tallest) - 0.5f) * TurretJitter;
            float lean = (ShapeRandom(seed, SaltLean) - 0.5f) * TowerLeanRange;
            return ((tallest - 1f) * TurretSpacing + jitter) * TurretConverge + lean;
        }

        /// <summary>셰이더 Rand와 같은 식(정수 해시라 GPU와 값이 똑같다). 모양 시드와 번호로 0~1 값을 고른다.</summary>
        internal static float ShapeRandom(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * LatticePrimeX + (uint)salt * LatticePrimeY;
                h = (h ^ (h >> LatticeShiftA)) * LatticeMix;
                h ^= h >> LatticeShiftB;
                return (h & LatticeMask) / (float)LatticeMask;
            }
        }
    }
}
