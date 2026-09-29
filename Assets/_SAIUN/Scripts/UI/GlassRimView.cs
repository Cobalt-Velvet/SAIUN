using _SAIUN.Scripts.Core;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 유리판 가장자리의 하이라이트를 그린다.
    /// Windows는 1픽셀 단색 테두리까지만 제공하므로, 굵기와 밝기 기울기가 있는 테두리는 직접 만든다.
    /// 텍스처는 시작할 때와 창 모양이 바뀔 때(카드 ↔ 사이드바) 창의 논리 크기로 만든다. 사이드바는 모서리를 깎지 않는다.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class GlassRimView : MonoBehaviour
    {
        [SerializeField] private RawImage target;

        [Header("테두리")]
        [Tooltip("하이라이트 색. 알파는 전체 세기로 쓰인다.")]
        [SerializeField] private Color rimColor = SaiunPalette.Eggshell;

        [Tooltip("테두리 굵기(픽셀).")]
        [Range(0.5f, 8f)]
        [SerializeField] private float thickness = 1.5f;

        [Tooltip("모서리 반경(픽셀). Windows 11의 둥근 모서리와 맞춘다.")]
        [Range(0f, 24f)]
        [SerializeField] private float cornerRadius = 8f;

        [Header("밝기 기울기")]
        [Tooltip("위쪽 테두리 세기. 빛이 위에서 온다고 보고 위를 밝게 둔다.")]
        [Range(0f, 1f)]
        [SerializeField] private float topIntensity = 0.85f;

        [Tooltip("아래쪽 테두리 세기.")]
        [Range(0f, 1f)]
        [SerializeField] private float bottomIntensity = 0.18f;

        private Texture2D _texture;
        private Vector2Int _size = new Vector2Int(SceneMetrics.WindowWidth, SceneMetrics.WindowHeight);
        private bool _rounded = true;

        private void Awake()
        {
            if (target == null) target = GetComponent<RawImage>();
            target.raycastTarget = false;   // 테두리가 클릭을 가로채면 창 드래그가 막힌다
            Rebuild();
        }

        private void OnDestroy()
        {
            DestroyTexture();
        }

        /// <summary>창 크기(논리 화소)와 모서리 둥글기를 바꿔 다시 만든다.</summary>
        public void Rebuild(Vector2Int size, bool rounded)
        {
            _size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
            _rounded = rounded;
            Rebuild();
        }

        /// <summary>테두리 텍스처를 다시 만든다. 값이 바뀌면 호출한다.</summary>
        public void Rebuild()
        {
            if (target == null) return;

            DestroyTexture();

            int width = _size.x;
            int height = _size.y;

            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "GlassRim",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[width * height];
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float radius = _rounded ? Mathf.Min(cornerRadius, Mathf.Min(halfWidth, halfHeight)) : 0f;
            byte r = (byte)Mathf.RoundToInt(rimColor.r * 255f);
            byte g = (byte)Mathf.RoundToInt(rimColor.g * 255f);
            byte b = (byte)Mathf.RoundToInt(rimColor.b * 255f);

            for (int y = 0; y < height; y++)
            {
                // 아래에서 위로 갈수록 밝아지는 기울기.
                float vertical = height > 1 ? y / (float)(height - 1) : 1f;
                float gradient = Mathf.Lerp(bottomIntensity, topIntensity, vertical) * rimColor.a;

                for (int x = 0; x < width; x++)
                {
                    float distance = RoundedBoxDistance(
                        x + 0.5f - halfWidth, y + 0.5f - halfHeight, halfWidth, halfHeight, radius);

                    // 경계 안쪽으로 들어온 거리. 0이 경계, 양수가 창 안쪽이다.
                    float inset = -distance;

                    // 바깥쪽 1픽셀은 계단이 보이지 않게 부드럽게 끊는다.
                    float outer = Mathf.Clamp01(inset + 0.5f);
                    float inner = Mathf.Clamp01((thickness - inset) / thickness);
                    float alpha = outer * inner * gradient;

                    pixels[y * width + x] = new Color32(r, g, b, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            _texture.SetPixels32(pixels);
            _texture.Apply(false, false);
            target.texture = _texture;
            target.color = Color.white;
        }

        /// <summary>둥근 사각형의 부호 있는 거리. 안쪽이 음수, 경계가 0이다.</summary>
        private static float RoundedBoxDistance(float px, float py, float halfWidth, float halfHeight, float radius)
        {
            float qx = Mathf.Abs(px) - (halfWidth - radius);
            float qy = Mathf.Abs(py) - (halfHeight - radius);
            float outsideX = Mathf.Max(qx, 0f);
            float outsideY = Mathf.Max(qy, 0f);
            float outside = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }

        private void DestroyTexture()
        {
            if (_texture == null) return;
            if (Application.isPlaying) Destroy(_texture);
            else DestroyImmediate(_texture);
            _texture = null;
        }

#if UNITY_EDITOR
        // 인스펙터에서 값을 움직이면 바로 보이게 한다. 실측용이다.
        private void OnValidate()
        {
            if (target == null) target = GetComponent<RawImage>();

            // OnValidate 도중에는 에셋을 만들 수 없어 한 틱 미룬다.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || target == null) return;
                Rebuild();
            };
        }
#endif
    }
}
