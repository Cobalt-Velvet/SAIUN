using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 유리 카드의 아래 두 모서리를 둥글게 깎는다.
    /// 창이 카드 아래로 길어져(다리 영역) DWM은 창 위 모서리만 둥글게 깎으므로, 카드 아래 모서리는 직접 지운다.
    /// 모서리 바깥쪽 1/4 원 밖을 알파 1로 칠한 작은 마스크를 지우개 셰이더(SAIUN/UI/EraseAlpha)로 그린다.
    /// </summary>
    public class CardCornerMask : MonoBehaviour
    {
        [Tooltip("왼쪽 아래 모서리를 지울 이미지")]
        [SerializeField] private RawImage bottomLeft;

        [Tooltip("오른쪽 아래 모서리를 지울 이미지")]
        [SerializeField] private RawImage bottomRight;

        [Tooltip("모서리 반경(픽셀). Windows 11 기본 둥근 모서리(8px)와 유리 테두리에 맞춘다.")]
        [SerializeField, Range(1f, 24f)] private float radius = 8f;

        private const int Supersample = 4;
        private Texture2D _mask;

        private void Awake()
        {
            Build();
        }

        private void OnDestroy()
        {
            if (_mask != null) Destroy(_mask);
        }

        /// <summary>마스크를 만들어 두 모서리에 입힌다.</summary>
        public void Build()
        {
            int size = Mathf.CeilToInt(radius);
            if (_mask != null) Destroy(_mask);
            _mask = new Texture2D(size, size, TextureFormat.Alpha8, false)
            {
                name = "CardCornerMask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            // 왼쪽 아래 모서리 기준. 원의 중심은 (radius, radius)이고, 원 밖(모서리 쪽)을 지운다.
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int outside = 0;
                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            float px = x + (sx + 0.5f) / Supersample;
                            float py = y + (sy + 0.5f) / Supersample;
                            float dx = radius - px;
                            float dy = radius - py;
                            if (dx > 0f && dy > 0f && dx * dx + dy * dy > radius * radius) outside++;
                        }
                    }
                    byte alpha = (byte)Mathf.RoundToInt(outside * 255f / (Supersample * Supersample));
                    pixels[y * size + x] = new Color32(0, 0, 0, alpha);
                }
            }
            _mask.SetPixels32(pixels);
            _mask.Apply(false, true);

            Place(bottomLeft, new Vector2(0f, 0f), new Rect(0f, 0f, 1f, 1f), size);
            // 오른쪽은 좌우를 뒤집어 쓴다.
            Place(bottomRight, new Vector2(1f, 0f), new Rect(1f, 0f, -1f, 1f), size);
        }

        private void Place(RawImage image, Vector2 corner, Rect uv, int size)
        {
            if (image == null) return;
            image.texture = _mask;
            image.uvRect = uv;
            image.raycastTarget = false;
            RectTransform rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = corner;
            rt.pivot = corner;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
        }
    }
}
