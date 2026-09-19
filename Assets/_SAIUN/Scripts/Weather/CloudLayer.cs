using System.Collections.Generic;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Lighting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 적운(뭉게구름): 늘 떠 있고 바람을 따라 흐른다 (사양서 v1.1 10장 "기본 항상 표시, 속도·밀도 설정 가능").
    /// 모양은 셰이더(SAIUN/UI/Cumulus)가 노이즈로 그려 구름마다 다르고 시간에 따라 천천히 변한다.
    /// 해 방향에서 빛을 받아 명암이 지고, 광원 색(아침·저녁의 따뜻한 빛)에 물든다.
    /// 먹구름 정도가 오르면 어두워지고, 평소에는 숨어 있던 구름이 더 몰려온다.
    /// 밝은 구름이 밝은 시계 글자 뒤로 지나가면 글자가 묻히므로, 글자 바로 뒤에서만 옅어진다(셰이더가 화소별로 처리).
    /// 유리 배경 바로 위, 3D 씬 뒤에 그려지도록 배경 캔버스의 Sky Layer 영역에 둔다.
    /// </summary>
    public class CloudLayer : MonoBehaviour
    {
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int StormId = Shader.PropertyToID("_Storm");
        private static readonly int[] ClearRectIds = { Shader.PropertyToID("_ClearRect0"), Shader.PropertyToID("_ClearRect1") };
        private static readonly int ClearOpacityId = Shader.PropertyToID("_ClearOpacity");

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

        [Header("빛")]
        [Tooltip("구름에 비치는 햇빛의 밝기 배율. 해 색에 곱한다.")]
        [SerializeField, Min(0f)] private float sunlight = 1.05f;

        [Tooltip("맑을 때 구름 불투명도. 시계 글자 뒤로 지나가도 읽히도록 옅게 둔다.")]
        [SerializeField, Range(0f, 1f)] private float clearOpacity = 0.7f;

        [Tooltip("먹구름일 때 불투명도")]
        [SerializeField, Range(0f, 1f)] private float stormOpacity = 0.9f;

        [Header("글자 가림 방지")]
        [Tooltip("구름이 뒤로 지나갈 때 옅어질 글자(시계 등). 앞의 두 개까지 쓴다.")]
        [SerializeField] private TMP_Text[] keepClear = new TMP_Text[0];

        [Tooltip("글자 바로 뒤에서 남기는 불투명도 비율")]
        [SerializeField, Range(0f, 1f)] private float behindTextOpacity = 0.35f;

        /// <summary>지금 흐르는 속도(픽셀/초). 양수면 오른쪽.</summary>
        public float DriftSpeed { get; private set; }

        public int CloudCount => _clouds.Count;

        /// <summary>구름을 비추는 해의 화면 방향(구름에서 해 쪽, 위가 +y).</summary>
        public Vector2 SunScreenDirection { get; private set; } = Vector2.up;

        private sealed class Cloud
        {
            public RectTransform Rect;
            public RawImage Image;
            public float Parallax;
            public bool Extra;
        }

        // 오른쪽 끝 픽셀이 다음 정수(다른 시드)로 넘어가지 않게 폭을 1보다 살짝 작게 둔다.
        private const float SeedUvWidth = 0.999f;
        private const int MaxSeed = 997;

        private readonly List<Cloud> _clouds = new List<Cloud>();
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
                Vector2 position = cloud.Rect.anchoredPosition;
                position.x += DriftSpeed * cloud.Parallax * deltaTime;

                // 화면을 완전히 벗어나면 반대편에서 다시 들어온다.
                float margin = halfWidth + cloud.Rect.rect.width / 2f;
                if (position.x > margin) position.x -= margin * 2f;
                else if (position.x < -margin) position.x += margin * 2f;
                cloud.Rect.anchoredPosition = position;

                float alpha = cloud.Extra ? opacity * storm : opacity;
                cloud.Image.color = new Color(1f, 1f, 1f, alpha);
                cloud.Image.enabled = alpha > 0.001f;
            }
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
        private void Build()
        {
            if (cloudTemplate == null) return;

            int total = density + stormExtra;
            float width = AreaWidth;
            for (int i = 0; i < total; i++)
            {
                RawImage image = Instantiate(cloudTemplate, area);
                image.name = $"Cloud_{i}";
                image.gameObject.SetActive(true);
                image.raycastTarget = false;
                if (_material != null) image.material = _material;
                // uvRect의 x 정수부가 셰이더의 모양 시드다.
                image.uvRect = new Rect(Random.Range(1, MaxSeed), 0f, SeedUvWidth, 1f);

                // 먹구름용은 더 크고 낮게 깔린다.
                bool extra = i >= density;
                float cloudWidth = Random.Range(widthRange.x, widthRange.y) * (extra ? 1.25f : 1f);
                float depth = extra
                    ? Random.Range((depthRange.x + depthRange.y) / 2f, depthRange.y)
                    : Random.Range(depthRange.x, depthRange.y);

                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(cloudWidth, cloudWidth / 2f);
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
    }
}
