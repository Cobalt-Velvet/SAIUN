using System;
using System.Collections.Generic;
using UnityEngine;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 모델 하나를 코드로 빚는 도구 (2026-09-27). 부품을 붙여 가다가 재질별 서브메시 한 장으로 굽는다.
    /// 작물(CropModelBuilder)과 풍향계(VaneModelBuilder)가 쓴다.
    /// 부품: 휘고 가운데가 접힌 풀잎·넓은 잎, 휘는 관(줄기·막대), 골과 꼭지 홈이 있는 타원체, 꽃잎·꽃받침 별,
    ///       두께 있는 판(화살촉·꼬리 날개), 속이 빈 반구(풍속계 컵).
    /// 잎처럼 얇은 부품은 앞뒷면을 따로 만들어 어느 쪽에서 봐도 빛을 받는다(재질은 뒷면을 버리는 기본 그대로).
    /// UV의 v는 잎·줄기의 밑동 0에서 끝 1로 가서, 세로 그러데이션 텍스처로 밑동을 어둡게 할 수 있다.
    /// </summary>
    internal sealed class SculptMesh
    {
        // 풀잎 폭이 끝으로 줄어드는 정도. 1보다 크면 끝 가까이에서 급히 뾰족해진다.
        private const float BladeTaper = 1.4f;
        // 넓은 잎 윤곽: 밑동이 좁고 1/3쯤에서 가장 넓으며 끝이 뾰족하다.
        private const float LeafBaseCurve = 0.7f;
        private const float LeafTipNarrowing = 0.35f;
        private const float LeafFullness = 0.8f;       // 1보다 작으면 잎 어깨가 통통하다
        // 접힌 잎맥에서 두 쪽 면이 기우는 정도
        private const float FoldTilt = 0.5f;
        // 꼭지 홈이 극 둘레로만 좁게 파이게 하는 지수
        private const float DimpleSharpness = 6f;
        // 관의 첫 고리 방향을 잡을 때 접선과 거의 나란하다고 보는 내적
        private const float NearlyParallel = 0.9f;

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<string> _materials = new List<string>();
        private readonly Dictionary<string, List<int>> _triangles = new Dictionary<string, List<int>>();

        /// <summary>부품의 자리: 로컬 +Y가 growth(정확히), +Z가 face 쪽(growth에 수직으로 맞춤)이 되게 놓는다.</summary>
        public static Matrix4x4 Frame(Vector3 position, Vector3 growth, Vector3 face)
        {
            Vector3 y = growth.normalized;
            Vector3 z = face - y * Vector3.Dot(face, y);
            if (z.sqrMagnitude < 1e-8f) z = Vector3.Cross(y, Vector3.right);
            return Matrix4x4.TRS(position, Quaternion.LookRotation(z.normalized, y), Vector3.one);
        }

        // ---- 얇은 부품 ----

        /// <summary>풀잎: 밑동에서 +Y로 자라 +Z 쪽으로 bend도만큼 휜다. 밑동이 가장 넓고 끝이 뾰족하다.</summary>
        public void Blade(string material, Matrix4x4 at, float length, float width, float bendDegrees, float fold, int segments = 6)
        {
            Strip(material, at, length, t => width * (1f - Mathf.Pow(t, BladeTaper)), bendDegrees, fold, segments);
        }

        /// <summary>넓은 잎: 밑동(잎자루 끝)에서 +Y로 뻗고 면은 +Z를 본다. curl이 음수면 끝이 아래로 처진다.</summary>
        public void Leaf(string material, Matrix4x4 at, float length, float width, float fold, float curlDegrees, int segments = 7)
        {
            Strip(material, at, length,
                // 끝(t=1)에서 sin(π)가 부동소수로 아주 작은 음수가 되면 거듭제곱이 NaN이 되므로 0으로 막는다.
                t => width * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(t, LeafBaseCurve))), LeafFullness) * (1f - LeafTipNarrowing * t),
                curlDegrees, fold, segments);
        }

        // 중심선을 호로 휘며 폭을 따라 네 줄(왼쪽 끝, 잎맥 왼쪽, 잎맥 오른쪽, 오른쪽 끝)로 세운 띠. 앞뒤 두 벌.
        private void Strip(string material, Matrix4x4 at, float length, Func<float, float> widthAt, float bendDegrees,
            float fold, int segments)
        {
            segments = Mathf.Max(1, segments);
            float bend = bendDegrees * Mathf.Deg2Rad;
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = bend * t;
                Vector3 center;
                if (Mathf.Abs(bend) < 1e-4f)
                {
                    center = new Vector3(0f, length * t, 0f);
                }
                else
                {
                    float radius = length / bend;
                    center = new Vector3(0f, radius * Mathf.Sin(angle), radius * (1f - Mathf.Cos(angle)));
                }
                var tangent = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                Vector3 side = Vector3.right;
                Vector3 face = Vector3.Cross(side, tangent);
                float half = Mathf.Max(0f, widthAt(t)) / 2f;
                Vector3 crease = center - face * (fold * half);

                positions.Add(center - side * half);
                positions.Add(crease);
                positions.Add(crease);
                positions.Add(center + side * half);
                normals.Add((face + side * FoldTilt * fold).normalized);
                normals.Add((face + side * FoldTilt * fold).normalized);
                normals.Add((face - side * FoldTilt * fold).normalized);
                normals.Add((face - side * FoldTilt * fold).normalized);
                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(0.5f, t));
                uvs.Add(new Vector2(0.5f, t));
                uvs.Add(new Vector2(1f, t));
            }

            var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                int row = i * 4;
                int next = row + 4;
                // 왼쪽 반(0-1), 오른쪽 반(2-3)
                Quad(triangles, row, next, next + 1, row + 1);
                Quad(triangles, row + 2, next + 2, next + 3, row + 3);
            }

            AddDoubleSided(material, at, positions, normals, uvs, triangles);
        }

        /// <summary>꽃잎·꽃받침 별: +Y를 보는 원판에 points개 끝. cup이 양수면 끝이 위로 들린다(오목).</summary>
        public void Star(string material, Matrix4x4 at, int points, float radius, float innerRatio, float cup)
        {
            var positions = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0f) };
            int corners = points * 2;
            for (int k = 0; k < corners; k++)
            {
                float angle = k * Mathf.PI * 2f / corners;
                float r = k % 2 == 0 ? radius : radius * innerRatio;
                positions.Add(new Vector3(Mathf.Cos(angle) * r, cup * r, Mathf.Sin(angle) * r));
                uvs.Add(new Vector2(0.5f, r / radius));
            }
            var triangles = new List<int>();
            for (int k = 0; k < corners; k++)
            {
                triangles.Add(0);
                triangles.Add(1 + (k + 1) % corners);
                triangles.Add(1 + k);
            }
            List<Vector3> normals = SmoothNormals(positions, triangles);
            // 별은 위(+Y)를 앞면으로 삼는다.
            for (int i = 0; i < normals.Count; i++) if (normals[i].y < 0f) normals[i] = -normals[i];
            AddDoubleSided(material, at, positions, normals, uvs, triangles);
        }

        // ---- 입체 부품 ----

        /// <summary>경로를 따라 굵기가 변하는 관(줄기·잎자루). 끝은 뾰족하게 닫는다.</summary>
        public void Tube(string material, Matrix4x4 at, IList<Vector3> path, float radiusStart, float radiusEnd, int sides = 6)
        {
            if (path.Count < 2) return;
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            Vector3 previousTangent = (path[1] - path[0]).normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(previousTangent, Vector3.forward)) < NearlyParallel ? Vector3.forward : Vector3.right;
            Vector3 normal = Vector3.Cross(previousTangent, up).normalized;

            for (int i = 0; i < path.Count; i++)
            {
                Vector3 tangent = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                // 앞 고리의 방향을 새 접선에 맞춰 돌려 관이 꼬이지 않게 한다.
                normal = Quaternion.FromToRotation(previousTangent, tangent) * normal;
                previousTangent = tangent;
                Vector3 binormal = Vector3.Cross(tangent, normal);
                float t = i / (float)(path.Count - 1);
                float radius = Mathf.Lerp(radiusStart, radiusEnd, t);
                for (int s = 0; s < sides; s++)
                {
                    float angle = s * Mathf.PI * 2f / sides;
                    positions.Add(path[i] + (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radius);
                    uvs.Add(new Vector2(s / (float)sides, t));
                }
            }
            int tip = positions.Count;
            Vector3 last = path[path.Count - 1];
            positions.Add(last + previousTangent * radiusEnd);
            uvs.Add(new Vector2(0.5f, 1f));

            var triangles = new List<int>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = i * sides + s;
                    int b = i * sides + (s + 1) % sides;
                    Quad(triangles, a, a + sides, b + sides, b);
                }
            }
            int lastRing = (path.Count - 1) * sides;
            for (int s = 0; s < sides; s++)
            {
                triangles.Add(lastRing + s);
                triangles.Add(tip);
                triangles.Add(lastRing + (s + 1) % sides);
            }
            AddSolid(material, at, positions, uvs, triangles);
        }

        /// <summary>
        /// 타원체(열매·알곡·덩이줄기·씨앗). lobes만큼 세로 골이 지고(lobeCount개), dimple만큼 꼭지 쪽이 파인다.
        /// </summary>
        public void Ellipsoid(string material, Matrix4x4 at, Vector3 radii, float lobes = 0f, int lobeCount = 5,
            float dimple = 0f, int rings = 8, int segments = 12)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int r = 0; r <= rings; r++)
            {
                float latitude = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, r / (float)rings);
                float y = Mathf.Sin(latitude);
                float ring = Mathf.Cos(latitude);
                // 꼭지 홈: 위쪽 극에 가까울수록 아래로 당긴다.
                y -= dimple * Mathf.Pow(Mathf.Max(0f, y), DimpleSharpness);
                for (int s = 0; s <= segments; s++)
                {
                    float longitude = s * Mathf.PI * 2f / segments;
                    float lobe = 1f + lobes * Mathf.Cos(lobeCount * longitude);
                    positions.Add(new Vector3(Mathf.Cos(longitude) * ring * lobe * radii.x, y * radii.y,
                        Mathf.Sin(longitude) * ring * lobe * radii.z));
                    uvs.Add(new Vector2(s / (float)segments, r / (float)rings));
                }
            }
            var triangles = new List<int>();
            int stride = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * stride + s;
                    Quad(triangles, a, a + 1, a + stride + 1, a + stride);
                }
            }
            AddSolid(material, at, positions, uvs, triangles);
        }

        /// <summary>
        /// 두께 있는 판: 로컬 XY의 볼록 다각형 윤곽을 Z로 thickness만큼 두껍게 한다. 앞뒤 면과 옆면이 있다.
        /// </summary>
        public void Plate(string material, Matrix4x4 at, IList<Vector2> outline, float thickness)
        {
            int count = outline.Count;
            if (count < 3) return;
            float half = thickness / 2f;
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // 앞뒤 면: 볼록이라 첫 꼭짓점에서 부채꼴로 나눈다.
            for (int side = -1; side <= 1; side += 2)
            {
                int start = positions.Count;
                foreach (Vector2 point in outline)
                {
                    positions.Add(new Vector3(point.x, point.y, half * side));
                    normals.Add(new Vector3(0f, 0f, side));
                    uvs.Add(point);
                }
                for (int i = 1; i < count - 1; i++)
                {
                    triangles.Add(start);
                    triangles.Add(start + i);
                    triangles.Add(start + i + 1);
                }
            }

            // 옆면: 모서리마다 바깥을 보는 사각형. 모서리가 또렷하게 꼭짓점을 따로 둔다.
            Vector2 center = Vector2.zero;
            foreach (Vector2 point in outline) center += point;
            center /= count;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[(i + 1) % count];
                Vector2 edge = (b - a).normalized;
                var outward = new Vector3(edge.y, -edge.x, 0f);
                if (Vector2.Dot(new Vector2(outward.x, outward.y), (a + b) / 2f - center) < 0f) outward = -outward;
                int start = positions.Count;
                positions.Add(new Vector3(a.x, a.y, -half));
                positions.Add(new Vector3(b.x, b.y, -half));
                positions.Add(new Vector3(b.x, b.y, half));
                positions.Add(new Vector3(a.x, a.y, half));
                for (int k = 0; k < 4; k++)
                {
                    normals.Add(outward);
                    uvs.Add(Vector2.zero);
                }
                Quad(triangles, start, start + 1, start + 2, start + 3);
            }

            AppendOriented(material, at, positions, normals, uvs, triangles);
        }

        /// <summary>속이 빈 반구(그릇): 입구가 로컬 +Y를 본다. 바깥·안쪽 면과 입구 테두리가 있다.</summary>
        public void Bowl(string material, Matrix4x4 at, float radius, float wall, int rings = 5, int segments = 12)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float inner = Mathf.Max(0f, radius - wall);

            // 바깥면(법선 바깥)과 안쪽면(법선 안쪽)
            for (int surface = 0; surface < 2; surface++)
            {
                float r = surface == 0 ? radius : inner;
                float facing = surface == 0 ? 1f : -1f;
                int start = positions.Count;
                for (int ring = 0; ring <= rings; ring++)
                {
                    float latitude = Mathf.Lerp(-Mathf.PI / 2f, 0f, ring / (float)rings);
                    for (int s = 0; s <= segments; s++)
                    {
                        float longitude = s * Mathf.PI * 2f / segments;
                        var direction = new Vector3(Mathf.Cos(latitude) * Mathf.Cos(longitude), Mathf.Sin(latitude),
                            Mathf.Cos(latitude) * Mathf.Sin(longitude));
                        positions.Add(direction * r);
                        normals.Add(direction * facing);
                        uvs.Add(new Vector2(s / (float)segments, ring / (float)rings));
                    }
                }
                int stride = segments + 1;
                for (int ring = 0; ring < rings; ring++)
                {
                    for (int s = 0; s < segments; s++)
                    {
                        int a = start + ring * stride + s;
                        Quad(triangles, a, a + 1, a + stride + 1, a + stride);
                    }
                }
            }

            // 입구 테두리: 바깥과 안쪽 가장자리를 잇는 위를 보는 고리
            int rimStart = positions.Count;
            for (int s = 0; s <= segments; s++)
            {
                float longitude = s * Mathf.PI * 2f / segments;
                var around = new Vector3(Mathf.Cos(longitude), 0f, Mathf.Sin(longitude));
                positions.Add(around * radius);
                positions.Add(around * inner);
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(s / (float)segments, 1f));
                uvs.Add(new Vector2(s / (float)segments, 1f));
            }
            for (int s = 0; s < segments; s++)
            {
                int a = rimStart + s * 2;
                Quad(triangles, a, a + 2, a + 3, a + 1);
            }

            AppendOriented(material, at, positions, normals, uvs, triangles);
        }

        // ---- 굽기 ----

        /// <summary>모은 부품을 재질별 서브메시로 굽는다. materials는 서브메시 순서다. scale만큼 전체를 키운다.</summary>
        public Mesh Bake(string name, float scale, out string[] materials)
        {
            var mesh = new Mesh { name = name };
            var scaled = new List<Vector3>(_vertices.Count);
            foreach (Vector3 v in _vertices) scaled.Add(v * scale);
            mesh.SetVertices(scaled);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _materials.Count;
            for (int i = 0; i < _materials.Count; i++) mesh.SetTriangles(_triangles[_materials[i]], i);
            // 작물 재질은 노멀 맵을 쓰지 않아 탄젠트를 굽지 않는다(에셋 크기를 줄인다).
            mesh.RecalculateBounds();
            materials = _materials.ToArray();
            return mesh;
        }

        // ---- 내부 ----

        private void AddSolid(string material, Matrix4x4 at, List<Vector3> positions, List<Vector2> uvs, List<int> triangles)
        {
            // 법선이 바깥(부품 중심에서 먼 쪽)을 보게 감겼는지 모든 삼각형으로 따져 보고, 반대면 뒤집는다.
            Vector3 center = Vector3.zero;
            foreach (Vector3 p in positions) center += p;
            center /= positions.Count;
            float agreement = 0f;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = positions[triangles[i]], b = positions[triangles[i + 1]], c = positions[triangles[i + 2]];
                agreement += Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center);
            }
            if (agreement < 0f) Flip(triangles);
            Append(material, at, positions, SmoothNormals(positions, triangles), uvs, triangles);
        }

        private void AddDoubleSided(string material, Matrix4x4 at, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> uvs, List<int> triangles)
        {
            // 앞면: 감긴 방향이 법선과 맞게 한다. 끝이 뾰족해 넓이가 0인 삼각형이 있어 모두 더해 따진다.
            var front = new List<int>(triangles);
            float agreement = 0f;
            for (int i = 0; i < front.Count; i += 3)
            {
                Vector3 a = positions[front[i]], b = positions[front[i + 1]], c = positions[front[i + 2]];
                agreement += Vector3.Dot(Vector3.Cross(b - a, c - a), normals[front[i]] + normals[front[i + 1]] + normals[front[i + 2]]);
            }
            if (agreement < 0f) Flip(front);
            Append(material, at, positions, normals, uvs, front);

            var back = new List<int>(front);
            Flip(back);
            var flipped = new List<Vector3>(normals.Count);
            foreach (Vector3 n in normals) flipped.Add(-n);
            Append(material, at, positions, flipped, uvs, back);
        }

        // 삼각형마다 감긴 방향을 그 꼭짓점 법선 쪽에 맞춘다. 면마다 법선을 직접 준 부품(판·그릇)에 쓴다.
        private void AppendOriented(string material, Matrix4x4 at, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> uvs, List<int> triangles)
        {
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = positions[triangles[i]], b = positions[triangles[i + 1]], c = positions[triangles[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                Vector3 normal = normals[triangles[i]] + normals[triangles[i + 1]] + normals[triangles[i + 2]];
                if (Vector3.Dot(face, normal) < 0f) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            }
            Append(material, at, positions, normals, uvs, triangles);
        }

        private void Append(string material, Matrix4x4 at, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> uvs, List<int> triangles)
        {
            if (!_triangles.TryGetValue(material, out List<int> list))
            {
                list = new List<int>();
                _triangles[material] = list;
                _materials.Add(material);
            }
            Matrix4x4 normalMatrix = at.inverse.transpose;
            int offset = _vertices.Count;
            for (int i = 0; i < positions.Count; i++)
            {
                _vertices.Add(at.MultiplyPoint3x4(positions[i]));
                _normals.Add(normalMatrix.MultiplyVector(normals[i]).normalized);
                _uvs.Add(uvs[i]);
            }
            foreach (int index in triangles) list.Add(offset + index);
        }

        private static List<Vector3> SmoothNormals(List<Vector3> positions, List<int> triangles)
        {
            var normals = new Vector3[positions.Count];
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 face = Vector3.Cross(positions[triangles[i + 1]] - positions[triangles[i]],
                    positions[triangles[i + 2]] - positions[triangles[i]]);
                normals[triangles[i]] += face;
                normals[triangles[i + 1]] += face;
                normals[triangles[i + 2]] += face;
            }
            var result = new List<Vector3>(normals.Length);
            foreach (Vector3 n in normals) result.Add(n.sqrMagnitude > 0f ? n.normalized : Vector3.up);
            return result;
        }

        private static void Quad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
        }

        private static void Flip(List<int> triangles)
        {
            for (int i = 0; i < triangles.Count; i += 3)
            {
                (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            }
        }
    }
}
