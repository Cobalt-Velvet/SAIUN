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
    /// 화단 외형 메시를 코드로 만든다 (2026-09-27, "요소는 적게, 비주얼은 훌륭하게").
    /// 화분: 흙 둘레(모서리가 살짝 둥근 사각형)를 따라 테두리 단면을 쓸어 만든 여물통.
    ///       안쪽 벽 → 둥근 안쪽 입술 → 평평한 윗면 → 둥근 바깥 입술 → 바깥 벽 → 둥근 밑동. 둥근 면이 빛을 받아 반짝인다.
    /// 흙: 칸마다 작물이 설 둔덕, 칸 사이 얕은 고랑, 잔 알갱이 요철이 있는 한 장의 면. 칸이 흙 모양으로 읽힌다.
    ///     UV는 칸 단위(칸 하나 = 0~1)라 칸 텍스처가 칸마다 한 장씩 깔린다.
    /// 좌표는 화단 로컬(바닥 중심 원점, 열 X·행 Z)이다.
    /// </summary>
    internal static class FlowerbedMeshes
    {
        // 밑동의 둥근 반경은 입술 반경의 이만큼. 바닥에 닿는 선을 부드럽게만 한다.
        private const float FootRadiusRatio = 0.5f;
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
        /// 흙 가장자리(inner, 모서리 반경 cornerRadius)를 둘러싸는 화분 메시.
        /// 바깥 크기는 inner + 테두리 폭×2, 윗면 높이는 rimHeight, 바깥 벽은 바닥(0)까지 내려간다.
        /// </summary>
        public static Mesh BuildPlanter(Vector2 inner, float rimWidth, float rimHeight, float lipRadius,
            float cornerRadius, float wallBottom, int arcSteps)
        {
            arcSteps = Mathf.Max(1, arcSteps);
            float lip = Mathf.Clamp(lipRadius, 0.0001f, Mathf.Min(rimWidth / 2f, rimHeight / 2f));
            float foot = lip * FootRadiusRatio;

            // 단면: x = 흙 가장자리에서 바깥으로 잰 거리, y = 높이. 법선도 같은 평면에 둔다.
            var profile = new List<Vector2>();
            var profileNormals = new List<Vector2>();
            profile.Add(new Vector2(0f, wallBottom));
            profileNormals.Add(Vector2.left);
            Arc(profile, profileNormals, new Vector2(lip, rimHeight - lip), lip, 180f, 90f, arcSteps);
            Arc(profile, profileNormals, new Vector2(rimWidth - lip, rimHeight - lip), lip, 90f, 0f, arcSteps);
            Arc(profile, profileNormals, new Vector2(rimWidth - foot, foot), foot, 0f, -90f, arcSteps);

            // 경로: 흙 가장자리를 도는 둥근 사각형. 반시계 방향으로 네 모서리 호를 잇는다.
            float corner = Mathf.Clamp(cornerRadius, 0.0001f, Mathf.Min(inner.x, inner.y) / 2f);
            var centers = new[]
            {
                new Vector2(inner.x / 2f - corner, inner.y / 2f - corner),
                new Vector2(-(inner.x / 2f - corner), inner.y / 2f - corner),
                new Vector2(-(inner.x / 2f - corner), -(inner.y / 2f - corner)),
                new Vector2(inner.x / 2f - corner, -(inner.y / 2f - corner)),
            };
            var path = new List<Vector3>();
            var pathNormals = new List<Vector3>();
            for (int c = 0; c < centers.Length; c++)
            {
                for (int s = 0; s <= arcSteps; s++)
                {
                    float angle = (90f * c + 90f * s / arcSteps) * Mathf.Deg2Rad;
                    var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    path.Add(new Vector3(centers[c].x, 0f, centers[c].y) + direction * corner);
                    pathNormals.Add(direction);
                }
            }

            var vertices = new List<Vector3>(path.Count * profile.Count);
            var normals = new List<Vector3>(path.Count * profile.Count);
            for (int i = 0; i < path.Count; i++)
            {
                for (int j = 0; j < profile.Count; j++)
                {
                    vertices.Add(path[i] + pathNormals[i] * profile[j].x + Vector3.up * profile[j].y);
                    normals.Add((pathNormals[i] * profileNormals[j].x + Vector3.up * profileNormals[j].y).normalized);
                }
            }

            var triangles = new List<int>();
            int columns = profile.Count;
            for (int i = 0; i < path.Count; i++)
            {
                int next = (i + 1) % path.Count;
                for (int j = 0; j < columns - 1; j++)
                {
                    Quad(triangles, vertices, normals, i * columns + j, next * columns + j, next * columns + j + 1, i * columns + j + 1);
                }
            }

            return Finish("Planter", vertices, normals, null, triangles, recalculateNormals: false);
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

        // 호를 시작각에서 끝각까지 steps 조각으로 나눠 단면에 잇는다. 시작점이 앞 점과 같으면 겹치지 않게 건너뛴다.
        private static void Arc(List<Vector2> points, List<Vector2> normals, Vector2 center, float radius,
            float fromDegrees, float toDegrees, int steps)
        {
            for (int s = 0; s <= steps; s++)
            {
                float angle = Mathf.Lerp(fromDegrees, toDegrees, s / (float)steps) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 point = center + direction * radius;
                if (points.Count > 0 && (points[points.Count - 1] - point).sqrMagnitude < 1e-10f) continue;
                points.Add(point);
                normals.Add(direction);
            }
        }

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
