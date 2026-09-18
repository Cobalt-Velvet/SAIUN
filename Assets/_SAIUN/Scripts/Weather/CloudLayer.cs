using System.Collections.Generic;
using _SAIUN.Scripts.Core;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 적운(뭉게구름): 늘 떠 있고 바람을 따라 흐른다 (사양서 v1.1 10장 "기본 항상 표시, 속도·밀도 설정 가능").
    /// 유리 배경 바로 위, 3D 씬 뒤에 그려지도록 배경 캔버스의 Sky Layer 영역에 둔다.
    /// 먹구름 정도가 오르면 구름이 어두워지고, 평소에는 숨어 있던 구름이 더 몰려온다.
    /// </summary>
    public class CloudLayer : MonoBehaviour
    {
        [SerializeField] private WeatherController weather;

        [Tooltip("구름이 흐르는 영역. Sky Layer에 맞춘다.")]
        [SerializeField] private RectTransform area;

        [Tooltip("구름 한 조각의 틀")]
        [SerializeField] private Image cloudTemplate;

        [SerializeField] private Sprite[] sprites;

        [Header("적운")]
        [Tooltip("평소 떠 있는 구름 수(밀도)")]
        [SerializeField, Range(0, 16)] private int density = 5;

        [Tooltip("먹구름일 때 더 몰려오는 구름 수")]
        [SerializeField, Range(0, 16)] private int stormExtra = 4;

        [Tooltip("바람이 없어도 흐르는 속도(픽셀/초)")]
        [SerializeField, Min(0f)] private float baseSpeed = 4f;

        [Tooltip("바람 세기 1당 더해지는 속도(픽셀/초)")]
        [SerializeField, Min(0f)] private float speedPerWind = 6f;

        [Tooltip("구름 폭 범위(픽셀)")]
        [SerializeField] private Vector2 widthRange = new Vector2(130f, 230f);

        [Tooltip("영역 위에서부터 잰 구름 높이 범위(픽셀)")]
        [SerializeField] private Vector2 depthRange = new Vector2(20f, 230f);

        [Tooltip("멀리 있는 구름일수록 느리게 흐르는 정도")]
        [SerializeField] private Vector2 parallaxRange = new Vector2(0.6f, 1.2f);

        [Tooltip("맑을 때 구름 색. 시계 글자 뒤로 지나가도 읽히도록 옅게 둔다.")]
        [SerializeField] private Color clearColor = SaiunPalette.WithAlpha(SaiunPalette.Eggshell, 0.32f);
        [SerializeField] private Color stormColor = SaiunPalette.WithAlpha(SaiunPalette.DeepJungle, 0.85f);

        /// <summary>지금 흐르는 속도(픽셀/초). 양수면 오른쪽.</summary>
        public float DriftSpeed { get; private set; }

        public int CloudCount => _clouds.Count;

        private sealed class Cloud
        {
            public RectTransform Rect;
            public Image Image;
            public float Parallax;
            public bool Extra;
        }

        private readonly List<Cloud> _clouds = new List<Cloud>();

        // 캔버스 배치가 끝나기 전(Awake)에는 폭이 0일 수 있다. 캔버스 기준 해상도가 창 크기라 그 폭을 대신 쓴다.
        private float AreaWidth => area.rect.width > 0f ? area.rect.width : SceneMetrics.WindowWidth;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
            if (area == null) area = (RectTransform)transform;
            if (cloudTemplate != null) cloudTemplate.gameObject.SetActive(false);
            Build();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>구름을 바람만큼 옮기고 색을 맞춘다. 테스트는 직접 부른다.</summary>
        internal void Tick(float deltaTime)
        {
            if (weather == null || area == null) return;

            // 바람의 화면 가로 성분. 화면 안쪽으로 부는 바람은 가로로는 거의 흐르지 않는다.
            float across = Vector3.Dot(weather.WindDirection, SceneMetrics.CameraRotation * Vector3.right);
            DriftSpeed = (across >= 0f ? baseSpeed : -baseSpeed) + across * weather.WindStrength * speedPerWind;

            float halfWidth = AreaWidth / 2f;
            Color color = Color.Lerp(clearColor, stormColor, weather.Storminess);

            foreach (Cloud cloud in _clouds)
            {
                Vector2 position = cloud.Rect.anchoredPosition;
                position.x += DriftSpeed * cloud.Parallax * deltaTime;

                // 화면을 완전히 벗어나면 반대편에서 다시 들어온다.
                float margin = halfWidth + cloud.Rect.rect.width / 2f;
                if (position.x > margin) position.x -= margin * 2f;
                else if (position.x < -margin) position.x += margin * 2f;
                cloud.Rect.anchoredPosition = position;

                Color tinted = color;
                if (cloud.Extra) tinted.a *= weather.Storminess;
                cloud.Image.color = tinted;
                cloud.Image.enabled = tinted.a > 0f;
            }
        }

        // 구름을 가로로 고르게 흩뿌린다. 위치·크기는 무작위지만 한 번 정하면 흐르기만 한다.
        private void Build()
        {
            if (cloudTemplate == null || sprites == null || sprites.Length == 0) return;

            int total = density + stormExtra;
            float width = AreaWidth;
            for (int i = 0; i < total; i++)
            {
                Image image = Instantiate(cloudTemplate, area);
                image.name = $"Cloud_{i}";
                image.gameObject.SetActive(true);
                image.raycastTarget = false;
                image.sprite = sprites[i % sprites.Length];

                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                float cloudWidth = Random.Range(widthRange.x, widthRange.y);
                float aspect = image.sprite.rect.height / image.sprite.rect.width;
                rect.sizeDelta = new Vector2(cloudWidth, cloudWidth * aspect);
                float x = ((i + Random.value) / total - 0.5f) * width;
                rect.anchoredPosition = new Vector2(x, -Random.Range(depthRange.x, depthRange.y));

                _clouds.Add(new Cloud
                {
                    Rect = rect,
                    Image = image,
                    Parallax = Random.Range(parallaxRange.x, parallaxRange.y),
                    Extra = i >= density,
                });
            }
        }
    }
}
