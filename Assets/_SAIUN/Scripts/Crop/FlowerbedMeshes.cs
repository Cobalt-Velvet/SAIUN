using System.Collections.Generic;
using UnityEngine;

namespace _SAIUN.Scripts.Crop
{
    /// <summary>흙 면의 모양 값. 화단(Flowerbed)의 인스펙터 값에서 만든다.</summary>
    internal struct SoilShape
    {
        public int Columns;
        public int Rows;
        public float CellSize;
        public float Surface;          // 둔덕 꼭대기(칸 가운데) 높이 = 작물이 서는 높이
        public float MoundHeight;
        public float MoundRadius;      // 칸 크기 대비
        public float GrooveDepth;
        public float GrooveWidth;      // 고랑 반폭(월드)
        public float Grain;            // 알갱이 요철 높이
        public float GrainSize;        // 알갱이 크기(월드)
        public int Resolution;         // 칸 한 변을 나누는 수

        public float Width => Columns * CellSize;
        public float Depth => Rows * CellSize;

        /// <summary>흙이 가장 낮을 수 있는 높이. 화분 안쪽 벽은 이보다 아래까지 내려간다.</summary>
        public float Floor => Surface - MoundHeight - GrooveDepth - Grain;
    }

    /// <summary>
    /// 데크 모양 값(화단 로컬, 윗면 높이 0). 화분은 데크 뒤 모서리(+X, +Z)에 놓이고, 데크는 보는 쪽(−X, −Z)으로 뻗는다.
    /// 화분 뒤로 데크가 뻗으면 아이소메트릭 화면에서 지평선 위(하늘·바다)를 덮으므로 뒤 모서리는 화분에 맞춘다.
    /// </summary>
    internal struct DeckShape
    {
        public float XMin;
        public float XMax;
        public float ZMin;
        public float ZMax;
        public float PlankWidth;
        public float PlankGap;
        public float PlankThickness;
        public float JoistDepth;       // 널빤지 밑 장선 층 두께. 널빤지 틈으로 어둡게 보인다.
        public float FasciaThickness;  // 왼쪽 모서리를 덮는 옆판 두께
        public float PostSize;
        public float PostDepth;        // 기둥이 내려가는 깊이(화면 아래로 빠진다)
        public float PostSpacing;
        public float WoodRepeat;       // 나무 텍스처 한 장이 덮는 결 방향 길이(월드)
    }

    /// <summary>
    /// 화단 외형 메시를 코드로 만든다. 요소는 적게, 남은 것은 질감과 빛까지 다듬는다.
    /// 화분: 바닷바람에 바랜 나무 판자 상자. 벽마다 판자 두 장이 포개져 있고
    ///       앞뒤 벽이 모서리까지 뻗어 옆 벽을 막는다. 윗면은 벽 두께의 판자다.
    /// 데크: 화분이 놓인 바닷가 나무 데크. 틈을 두고 깐 널빤지, 틈으로 보이는 어두운 장선,
    ///       왼쪽 모서리를 덮는 옆판, 아래로 내려가는 기둥. 가까이 내려다보는 바닥이 생겨 먼 바다와 시점이 이어진다.
    /// 나무 UV: U는 결 방향 길이(WoodRepeat마다 한 장), V는 나무 텍스처의 판자 네 줄(WoodArtBuilder)이다.
    /// 흙: 칸마다 작물이 설 둔덕, 칸 사이 얕은 고랑, 잔 알갱이 요철이 있는 한 장의 면. 칸이 흙 모양으로 읽힌다.
    ///     UV는 칸 단위(칸 하나 = 0~1)라 칸 텍스처가 칸마다 한 장씩 깔린다.
    /// 좌표는 화단 로컬(바닥 중심 원점, 열 X·행 Z)이다.
    /// </summary>
    internal static class FlowerbedMeshes
    {
        // ---- 나무 텍스처 배치 (WoodArtBuilder와 맞춘다) ----
        // 판자 줄 수와 쓰임: 화분 벽은 0·1줄, 화분 윗면은 2줄, 데크 옆판은 3줄. 널빤지·기둥은 네 줄을 돌려 쓴다.
        private const int WoodRows = 4;
        private const int TopRow = 2;
        private const int FasciaRow = 3;
        private const float FasciaOffset = 0.29f;
        // 널빤지 옆면·끝면은 줄 가운데의 좁은 띠를 쓴다(판자 틈 그늘이 들지 않게).
        private const float EdgeStart = 0.4f;
        private const float EdgeBand = 0.2f;
        // 장선 그늘은 판자 틈의 가장 어두운 곳이다.
        private const float ShadowV = 0.0005f;
        // 그늘 면도 UV 폭이 0이면 접선을 셈할 수 없어 아주 좁게 편다.
        private static readonly Vector2 ShadowSpan = new Vector2(0.01f, 0f);
        private static readonly Vector2 ShadowSpanV = new Vector2(0f, 0.0002f);
        // 널빤지마다 결 위치를 흩뜨리는 폭(텍스처 장 수)과 해시 소금
        private const float PlankOffsetRange = 7f;
        private const int PlankSalt = 3;
        private const int PostSalt = 101;
        // 알갱이 요철을 이 거리(둔덕 반경 대비) 안쪽에서는 지워 작물이 정확히 둔덕 꼭대기에 선다.
        private const float GrainClearRatio = 0.35f;
        // 알갱이 노이즈는 두 겹이다. 둘째 겹은 더 잘고 어긋나게 둔다.
        private const float FirstOctaveWeight = 0.65f;
        private const float SecondOctaveScale = 2.3f;
        private static readonly Vector2 SecondOctaveOffset = new Vector2(17f, 5f);
        // 셰이더에서 흔히 쓰는 사인 해시 계수
        private const float HashX = 127.1f;
        private const float HashY = 311.7f;
        private const float HashScale = 43758.5453f;

        // ---- 화분 ----

        /// <summary>
        /// 흙(inner)을 둘러싸는 나무 판자 상자. 바깥 크기는 inner + 테두리 폭×2, 윗면 높이 rimHeight, 바닥(0)부터 선다.
        /// 벽 옆면은 판자 두 장(텍스처 0·1줄), 윗면은 셋째 줄이다.
        /// </summary>
        public static Mesh BuildPlanter(Vector2 inner, float rimWidth, float rimHeight, float woodRepeat)
        {
            var parts = new Parts();
            float repeat = Mathf.Max(0.01f, woodRepeat);
            float hx = inner.x / 2f;
            float hz = inner.y / 2f;
            float ox = hx + rimWidth;
            float oz = hz + rimWidth;

            // 앞뒤 벽(X 방향, 모서리까지)과 옆 벽(Z 방향, 앞뒤 벽 사이)
            WallAlongX(parts, -ox, ox, hz, oz, rimHeight, repeat, 0.13f);
            WallAlongX(parts, -ox, ox, -oz, -hz, rimHeight, repeat, 0.61f);
            WallAlongZ(parts, hx, ox, -hz, hz, rimHeight, repeat, 0.37f);
            WallAlongZ(parts, -ox, -hx, -hz, hz, rimHeight, repeat, 0.83f);
            return parts.Finish("Planter");
        }

        // X 방향으로 누운 벽: 옆면(±Z)은 판자 두 장, 윗면은 셋째 줄, 양 끝(±X)은 마구리.
        private static void WallAlongX(Parts parts, float x0, float x1, float z0, float z1, float height, float repeat, float offset)
        {
            float length = x1 - x0;
            var along = new Vector2(length / repeat, 0f);
            var tall = new Vector2(0f, 2f / WoodRows);
            Vector2 start = new Vector2(x0 / repeat + offset, 0f);
            parts.Face(new Vector3(x0, 0f, z1), new Vector3(length, 0f, 0f), new Vector3(0f, height, 0f), Vector3.forward, start, along, tall);
            parts.Face(new Vector3(x0, 0f, z0), new Vector3(length, 0f, 0f), new Vector3(0f, height, 0f), Vector3.back, start, along, tall);
            parts.Face(new Vector3(x0, height, z0), new Vector3(length, 0f, 0f), new Vector3(0f, 0f, z1 - z0), Vector3.up,
                new Vector2(start.x, TopRow / (float)WoodRows), along, new Vector2(0f, 1f / WoodRows));
            var across = new Vector2((z1 - z0) / repeat, 0f);
            parts.Face(new Vector3(x0, 0f, z0), new Vector3(0f, 0f, z1 - z0), new Vector3(0f, height, 0f), Vector3.left, Vector2.zero, across, tall);
            parts.Face(new Vector3(x1, 0f, z0), new Vector3(0f, 0f, z1 - z0), new Vector3(0f, height, 0f), Vector3.right, Vector2.zero, across, tall);
        }

        // Z 방향으로 누운 벽(앞뒤 벽 사이). 끝은 앞뒤 벽에 막혀 보이지 않는다.
        private static void WallAlongZ(Parts parts, float x0, float x1, float z0, float z1, float height, float repeat, float offset)
        {
            float length = z1 - z0;
            var along = new Vector2(length / repeat, 0f);
            var tall = new Vector2(0f, 2f / WoodRows);
            Vector2 start = new Vector2(z0 / repeat + offset, 0f);
            parts.Face(new Vector3(x1, 0f, z0), new Vector3(0f, 0f, length), new Vector3(0f, height, 0f), Vector3.right, start, along, tall);
            parts.Face(new Vector3(x0, 0f, z0), new Vector3(0f, 0f, length), new Vector3(0f, height, 0f), Vector3.left, start, along, tall);
            parts.Face(new Vector3(x0, height, z0), new Vector3(0f, 0f, length), new Vector3(x1 - x0, 0f, 0f), Vector3.up,
                new Vector2(start.x, TopRow / (float)WoodRows), along, new Vector2(0f, 1f / WoodRows));
        }

        // ---- 데크 ----

        /// <summary>화분이 놓인 나무 데크. 윗면 높이는 0이다.</summary>
        public static Mesh BuildDeck(DeckShape shape)
        {
            var parts = new Parts();
            float repeat = Mathf.Max(0.01f, shape.WoodRepeat);
            float t = shape.PlankThickness;
            float length = shape.ZMax - shape.ZMin;
            float pitch = Mathf.Max(0.001f, shape.PlankWidth + shape.PlankGap);
            var along = new Vector2(length / repeat, 0f);

            // 널빤지: Z 방향으로 길게, 왼쪽(XMin)부터 틈을 두고 깐다. 널빤지마다 텍스처 줄과 결 위치가 다르다.
            int index = 0;
            for (float x0 = shape.XMin; x0 < shape.XMax - 0.001f; x0 += pitch, index++)
            {
                float x1 = Mathf.Min(x0 + shape.PlankWidth, shape.XMax);
                float width = x1 - x0;
                int row = index % WoodRows;
                var start = new Vector2(shape.ZMin / repeat + PlankHash(index) * PlankOffsetRange, row / (float)WoodRows);
                var edgeV = new Vector2(0f, EdgeBand / WoodRows);
                var edgeStart = new Vector2(start.x, (row + EdgeStart) / WoodRows);
                parts.Face(new Vector3(x0, 0f, shape.ZMin), new Vector3(0f, 0f, length), new Vector3(width, 0f, 0f), Vector3.up,
                    start, along, new Vector2(0f, 1f / WoodRows));
                parts.Face(new Vector3(x0, -t, shape.ZMin), new Vector3(0f, 0f, length), new Vector3(0f, t, 0f), Vector3.left,
                    edgeStart, along, edgeV);
                parts.Face(new Vector3(x1, -t, shape.ZMin), new Vector3(0f, 0f, length), new Vector3(0f, t, 0f), Vector3.right,
                    edgeStart, along, edgeV);
                parts.Face(new Vector3(x0, -t, shape.ZMax), new Vector3(width, 0f, 0f), new Vector3(0f, t, 0f), Vector3.forward,
                    edgeStart, new Vector2(width / repeat, 0f), edgeV);
                parts.Face(new Vector3(x0, -t, shape.ZMin), new Vector3(width, 0f, 0f), new Vector3(0f, t, 0f), Vector3.back,
                    edgeStart, new Vector2(width / repeat, 0f), edgeV);
            }

            // 장선 층: 널빤지 틈으로 보이는 그늘. 텍스처의 판자 틈(가장 어두운 곳)을 쓴다.
            float joistTop = -t;
            float joistBottom = -t - shape.JoistDepth;
            var shadow = new Vector2(0.5f, ShadowV);
            parts.Face(new Vector3(shape.XMin, joistTop, shape.ZMin), new Vector3(0f, 0f, length), new Vector3(shape.XMax - shape.XMin, 0f, 0f),
                Vector3.up, shadow, ShadowSpan, ShadowSpanV);
            parts.Face(new Vector3(shape.XMin, joistBottom, shape.ZMax), new Vector3(shape.XMax - shape.XMin, 0f, 0f),
                new Vector3(0f, shape.JoistDepth, 0f), Vector3.forward, shadow, ShadowSpan, ShadowSpanV);

            // 왼쪽 옆판: 데크 모서리를 덮는 판자 한 장(넷째 줄). 널빤지 윗면과 높이를 맞춘다.
            float fx0 = shape.XMin - shape.FasciaThickness;
            float fasciaHeight = t + shape.JoistDepth;
            var fasciaStart = new Vector2(shape.ZMin / repeat + FasciaOffset, FasciaRow / (float)WoodRows);
            var fasciaTall = new Vector2(0f, 1f / WoodRows);
            parts.Face(new Vector3(fx0, joistBottom, shape.ZMin), new Vector3(0f, 0f, length), new Vector3(0f, fasciaHeight, 0f), Vector3.left,
                fasciaStart, along, fasciaTall);
            parts.Face(new Vector3(fx0, 0f, shape.ZMin), new Vector3(0f, 0f, length), new Vector3(shape.FasciaThickness, 0f, 0f), Vector3.up,
                fasciaStart, along, new Vector2(0f, EdgeBand / WoodRows));
            parts.Face(new Vector3(fx0, joistBottom, shape.ZMax), new Vector3(shape.FasciaThickness, 0f, 0f), new Vector3(0f, fasciaHeight, 0f),
                Vector3.forward, fasciaStart, new Vector2(shape.FasciaThickness / repeat, 0f), fasciaTall);

            // 기둥: 왼쪽 모서리 아래로 내려가 데크가 떠 있는 바닥임을 알린다. 결은 세로다.
            float half = shape.PostSize / 2f;
            float px = fx0 + half;
            int post = 0;
            for (float pz = shape.ZMax - half; pz > shape.ZMin; pz -= Mathf.Max(0.01f, shape.PostSpacing), post++)
            {
                float y0 = joistBottom - shape.PostDepth;
                var up = new Vector3(0f, shape.PostDepth, 0f);
                var grain = new Vector2(shape.PostDepth / repeat, 0f);
                var postStart = new Vector2(PlankHash(post + PostSalt), (post % WoodRows) / (float)WoodRows);
                var postAcross = new Vector2(0f, 1f / WoodRows);
                // 결이 세로라 U를 높이 쪽 변에 둔다.
                parts.Face(new Vector3(px - half, y0, pz - half), up, new Vector3(0f, 0f, shape.PostSize), Vector3.left, postStart, grain, postAcross);
                parts.Face(new Vector3(px + half, y0, pz - half), up, new Vector3(0f, 0f, shape.PostSize), Vector3.right, postStart, grain, postAcross);
                parts.Face(new Vector3(px - half, y0, pz + half), up, new Vector3(shape.PostSize, 0f, 0f), Vector3.forward, postStart, grain, postAcross);
                parts.Face(new Vector3(px - half, y0, pz - half), up, new Vector3(shape.PostSize, 0f, 0f), Vector3.back, postStart, grain, postAcross);
            }

            return parts.Finish("Deck");
        }

        private static float PlankHash(int index)
        {
            float n = Mathf.Sin(index * HashX + PlankSalt * HashY) * HashScale;
            return n - Mathf.Floor(n);
        }

        // ---- 흙 ----

        /// <summary>흙 면 메시. 가로 Width, 세로 Depth, 칸 가운데 둔덕 꼭대기가 Surface 높이다.</summary>
        public static Mesh BuildSoil(SoilShape shape)
        {
            int resolution = Mathf.Max(2, shape.Resolution);
            int xCount = shape.Columns * resolution + 1;
            int zCount = shape.Rows * resolution + 1;
            float halfWidth = shape.Width / 2f;
            float halfDepth = shape.Depth / 2f;
            // UV는 칸 단위다. 칸 텍스처 한 장이 칸 하나에 꼭 맞는다.
            float tile = Mathf.Max(0.001f, shape.CellSize);

            var vertices = new List<Vector3>(xCount * zCount);
            var uvs = new List<Vector2>(xCount * zCount);
            for (int z = 0; z < zCount; z++)
            {
                for (int x = 0; x < xCount; x++)
                {
                    float px = -halfWidth + shape.Width * x / (xCount - 1);
                    float pz = -halfDepth + shape.Depth * z / (zCount - 1);
                    vertices.Add(new Vector3(px, SoilHeight(px, pz, shape), pz));
                    uvs.Add(new Vector2((px + halfWidth) / tile, (pz + halfDepth) / tile));
                }
            }

            var triangles = new List<int>();
            for (int z = 0; z < zCount - 1; z++)
            {
                for (int x = 0; x < xCount - 1; x++)
                {
                    int a = z * xCount + x;
                    // 위에서 봐서 앞면이 되게 감는다.
                    triangles.Add(a);
                    triangles.Add(a + xCount);
                    triangles.Add(a + xCount + 1);
                    triangles.Add(a);
                    triangles.Add(a + xCount + 1);
                    triangles.Add(a + 1);
                }
            }

            return Finish("Soil", vertices, null, uvs, triangles, recalculateNormals: true);
        }

        /// <summary>흙 면의 높이. 칸 가운데 둔덕, 칸 사이 고랑, 알갱이 요철을 더한다.</summary>
        public static float SoilHeight(float x, float z, SoilShape shape)
        {
            float cell = shape.CellSize;
            float u = (x + shape.Width / 2f) / cell;     // 0 ~ Columns
            float v = (z + shape.Depth / 2f) / cell;     // 0 ~ Rows
            int column = Mathf.Clamp(Mathf.FloorToInt(u), 0, shape.Columns - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(v), 0, shape.Rows - 1);
            float fu = u - column - 0.5f;
            float fv = v - row - 0.5f;

            // 둔덕: 칸 가운데가 가장 높고 반경 밖에서 0이 되는 코사인 언덕.
            float moundRadius = Mathf.Max(0.0001f, shape.MoundRadius * cell);
            float fromCenter = Mathf.Sqrt(fu * fu + fv * fv) * cell;
            float mound = shape.MoundHeight * (Mathf.Cos(Mathf.PI * Mathf.Clamp01(fromCenter / moundRadius)) * 0.5f + 0.5f);

            // 고랑: 이웃 칸과 맞닿은 경계에만 판다. 화분 벽 쪽은 파지 않는다.
            float border = float.MaxValue;
            if (fu > 0f && column < shape.Columns - 1) border = Mathf.Min(border, (0.5f - fu) * cell);
            if (fu < 0f && column > 0) border = Mathf.Min(border, (0.5f + fu) * cell);
            if (fv > 0f && row < shape.Rows - 1) border = Mathf.Min(border, (0.5f - fv) * cell);
            if (fv < 0f && row > 0) border = Mathf.Min(border, (0.5f + fv) * cell);
            float groove = border < float.MaxValue
                ? -shape.GrooveDepth * (1f - Mathf.SmoothStep(0f, 1f, border / Mathf.Max(0.0001f, shape.GrooveWidth)))
                : 0f;

            // 알갱이: 두 겹 값 노이즈. 둔덕 꼭대기 가까이에서는 지운다.
            float size = Mathf.Max(0.0001f, shape.GrainSize);
            float noise = ValueNoise(x / size, z / size) * FirstOctaveWeight
                          + ValueNoise(x / size * SecondOctaveScale + SecondOctaveOffset.x, z / size * SecondOctaveScale + SecondOctaveOffset.y)
                          * (1f - FirstOctaveWeight);
            float clear = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(moundRadius * GrainClearRatio, moundRadius, fromCenter));
            float grain = shape.Grain * (noise * 2f - 1f) * clear;

            return shape.Surface - shape.MoundHeight + mound + groove + grain;
        }

        // ---- 도우미 ----

        // 네 점으로 두 삼각형을 만들고, 꼭짓점 법선 쪽이 앞면이 되게 감는다.
        private static void Quad(List<int> triangles, List<Vector3> vertices, List<Vector3> normals, int a, int b, int c, int d)
        {
            Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            bool flip = Vector3.Dot(face, normals[a] + normals[c]) < 0f;
            if (flip)
            {
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(a); triangles.Add(d); triangles.Add(c);
            }
            else
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }

        // 판자 상자의 면들을 모은다. 면마다 꼭짓점을 따로 두어 모서리가 또렷하다.
        private sealed class Parts
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();

            // corner에서 edgeU·edgeV로 펼친 사각 면. UV는 uv0에서 uvU·uvV만큼 같은 방향으로 늘어난다.
            public void Face(Vector3 corner, Vector3 edgeU, Vector3 edgeV, Vector3 normal, Vector2 uv0, Vector2 uvU, Vector2 uvV)
            {
                int first = _vertices.Count;
                _vertices.Add(corner);
                _vertices.Add(corner + edgeU);
                _vertices.Add(corner + edgeU + edgeV);
                _vertices.Add(corner + edgeV);
                _uvs.Add(uv0);
                _uvs.Add(uv0 + uvU);
                _uvs.Add(uv0 + uvU + uvV);
                _uvs.Add(uv0 + uvV);
                for (int i = 0; i < 4; i++) _normals.Add(normal);
                Quad(_triangles, _vertices, _normals, first, first + 1, first + 2, first + 3);
            }

            public Mesh Finish(string name) => FlowerbedMeshes.Finish(name, _vertices, _normals, _uvs, _triangles, recalculateNormals: false);
        }

        private static Mesh Finish(string name, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles, bool recalculateNormals)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            if (recalculateNormals) mesh.RecalculateNormals();
            else mesh.SetNormals(normals);
            if (uvs != null) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // 격자점마다 고정된 난수를 두고 부드럽게 잇는 값 노이즈(0~1).
        private static float ValueNoise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy);
            float b = Hash(ix + 1, iy);
            float c = Hash(ix, iy + 1);
            float d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        private static float Hash(int x, int y)
        {
            float n = Mathf.Sin(x * HashX + y * HashY) * HashScale;
            return n - Mathf.Floor(n);
        }
    }
}
