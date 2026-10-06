using UnityEngine;

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// 창 가장자리에서 크기를 바꿀 수 있음을 알리는 양쪽 화살표 커서. 테두리 없는 창이라 운영체제 커서가 바뀌지 않으므로
    /// 흰 화살표에 검은 테를 두른 그림을 코드로 그려 Unity 커서로 쓴다.
    /// </summary>
    internal static class ResizeCursors
    {
        private const int Size = 32;

        // 화살표 반길이, 화살촉이 시작하는 자리, 화살대 반폭, 화살촉 기울기(화소)
        private const float Reach = 12f;
        private const float HeadStart = 6.5f;
        private const float ShaftHalfWidth = 1.5f;
        private const float HeadSlope = 1.1f;

        private static Texture2D _horizontal;
        private static Texture2D _vertical;
        private static Texture2D _falling;
        private static Texture2D _rising;

        /// <summary>커서의 가리키는 점(그림 가운데).</summary>
        public static Vector2 Hotspot => new Vector2(Size / 2f, Size / 2f);

        /// <summary>
        /// 잡은 가장자리에 맞는 커서. 좌우는 ↔, 위아래는 ↕, 왼쪽 위·오른쪽 아래 모서리는 ↘↖, 나머지 모서리는 ↗↙.
        /// </summary>
        public static Texture2D For(bool horizontal, bool vertical, bool falling)
        {
            if (horizontal && vertical)
            {
                return falling
                    ? _falling != null ? _falling : _falling = Draw(new Vector2(1f, 1f))
                    : _rising != null ? _rising : _rising = Draw(new Vector2(1f, -1f));
            }
            if (horizontal) return _horizontal != null ? _horizontal : _horizontal = Draw(new Vector2(1f, 0f));
            return _vertical != null ? _vertical : _vertical = Draw(new Vector2(0f, 1f));
        }

        // axis는 화면 좌표(아래가 +y) 방향이다.
        private static Texture2D Draw(Vector2 axis)
        {
            axis.Normalize();
            var across = new Vector2(-axis.y, axis.x);
            var inside = new bool[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2(x + 0.5f - Size / 2f, y + 0.5f - Size / 2f);
                    float u = Mathf.Abs(Vector2.Dot(p, axis));
                    float v = Mathf.Abs(Vector2.Dot(p, across));
                    bool shaft = u <= HeadStart && v <= ShaftHalfWidth;
                    bool head = u >= HeadStart && u <= Reach && v <= (Reach - u) * HeadSlope;
                    inside[y * Size + x] = shaft || head;
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "ResizeCursor",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[Size * Size];
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // 텍스처는 아래 줄부터 채우므로 화면 좌표의 줄을 뒤집어 쓴다.
                    int target = (Size - 1 - y) * Size + x;
                    if (inside[y * Size + x]) pixels[target] = white;
                    else if (Touches(inside, x, y)) pixels[target] = black;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // 둘레 여덟 칸 중 하나라도 화살표면 테두리다.
        private static bool Touches(bool[] inside, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx >= 0 && nx < Size && ny >= 0 && ny < Size && inside[ny * Size + nx]) return true;
                }
            }
            return false;
        }
    }
}
