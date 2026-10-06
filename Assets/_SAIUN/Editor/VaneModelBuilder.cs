using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace _SAIUN.Editor
{
    /// <summary>풍향계 메시 세 벌과 각 서브메시의 재질 이름. 몸체는 서 있고, 화살표와 풍속계 컵은 따로 돈다.</summary>
    public struct VaneMeshes
    {
        public Mesh Body;
        public string[] BodyParts;
        public Mesh Arrow;
        public string[] ArrowParts;
        public Mesh Cups;
        public string[] CupsParts;
    }

    /// <summary>
    /// 풍향계·풍속계 모델을 코드로 빚는다.
    /// 몸체: 둥근 받침, 위로 가늘어지는 기둥, 동서남북 막대 끝의 구슬, 화살표 축 고리, 풍속계 대.
    /// 화살표: 둥근 축, 판으로 된 연 모양 화살촉, 부채꼴 꼬리 날개. 앞(+Z)이 화살촉이다.
    /// 풍속계: 가운데 통과 꼭지, 세 팔 끝에서 접선 쪽으로 입을 벌린 반구 컵.
    /// 화살표·컵 메시는 각자의 회전축(원점)을 기준으로 빚어 WeatherVane이 돌린다.
    /// 메시 에셋은 제자리에 덮어써 GUID를 지킨다.
    /// </summary>
    public static class VaneModelBuilder
    {
        public const string Metal = "Metal";
        public const string Accent = "Accent";

        /// <summary>
        /// 받침(데크)에서 화살표 축까지 높이(월드). 기둥은 화분 상자 뒤 모서리를 꿰뚫고 데크에 박혀,
        /// 집중하는 동안 상자를 토분으로 바꿔도 그대로 선다.
        /// </summary>
        public const float PoleHeight = 1.42f;

        /// <summary>화살표 축에서 풍속계 중심까지 높이(월드).</summary>
        public const float CupsLift = 0.2f;

        private const string MeshFolder = "Assets/_SAIUN/Art/Meshes/Vane";

        // 몸체
        private const float FootRadius = 0.045f;
        private const float FootHeight = 0.014f;
        private const float PoleBaseRadius = 0.022f;
        private const float PoleTopRadius = 0.015f;
        private const float CompassHeightRatio = 0.72f;
        private const float CompassArm = 0.17f;
        private const float CompassArmRadius = 0.008f;
        private const float CompassBall = 0.017f;
        private const float StemRadius = 0.011f;

        // 화살표
        private const float ShaftFront = 0.24f;
        private const float ShaftBack = 0.34f;
        private const float ShaftRadius = 0.011f;
        private const float PlateThickness = 0.014f;
        private const float FinThickness = 0.01f;
        private const float HeadLength = 0.13f;
        private const float HeadHalfWidth = 0.065f;
        private const float FeatherLength = 0.26f;
        private const float FeatherRoot = 0.17f;
        private const float FeatherHeight = 0.105f;
        private const float FeatherGap = 0.004f;

        // 풍속계
        private const int CupCount = 3;
        private const float CupArm = 0.19f;
        private const float CupArmRadius = 0.006f;
        private const float CupRadius = 0.038f;
        private const float CupWall = 0.005f;

        private const int RoundSides = 10;

        /// <summary>세 메시를 만들어(있으면 덮어써) 돌려준다.</summary>
        public static VaneMeshes Build()
        {
            return new VaneMeshes
            {
                Body = Save(BuildBody(), "Body", out string[] body),
                BodyParts = body,
                Arrow = Save(BuildArrow(), "Arrow", out string[] arrow),
                ArrowParts = arrow,
                Cups = Save(BuildCups(), "Cups", out string[] cups),
                CupsParts = cups,
            };
        }

        private static SculptMesh BuildBody()
        {
            var body = new SculptMesh();
            body.Ellipsoid(Metal, SculptMesh.Frame(Vector3.up * (FootHeight * 0.4f), Vector3.up, Vector3.forward),
                new Vector3(FootRadius, FootHeight, FootRadius), rings: 6, segments: 16);
            body.Tube(Metal, Matrix4x4.identity, new[] { Vector3.zero, Vector3.up * PoleHeight }, PoleBaseRadius, PoleTopRadius, RoundSides);

            // 동서남북 막대: 가운데 고리에서 네 방향으로 뻗고 끝에 구슬
            float compassY = PoleHeight * CompassHeightRatio;
            var center = new Vector3(0f, compassY, 0f);
            body.Ellipsoid(Metal, SculptMesh.Frame(center, Vector3.up, Vector3.forward),
                new Vector3(PoleBaseRadius * 1.4f, CompassBall, PoleBaseRadius * 1.4f), rings: 6, segments: 12);
            foreach (Vector3 direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                body.Tube(Metal, Matrix4x4.identity, new[] { center, center + direction * CompassArm },
                    CompassArmRadius, CompassArmRadius * 0.75f, 6);
                body.Ellipsoid(Metal, SculptMesh.Frame(center + direction * CompassArm, Vector3.up, direction),
                    Vector3.one * CompassBall, rings: 6, segments: 10);
            }

            // 화살표 축 고리와 풍속계 대
            var pivot = new Vector3(0f, PoleHeight, 0f);
            body.Ellipsoid(Metal, SculptMesh.Frame(pivot, Vector3.up, Vector3.forward),
                new Vector3(PoleTopRadius * 1.5f, PoleTopRadius * 2f, PoleTopRadius * 1.5f), rings: 6, segments: 12);
            body.Tube(Metal, Matrix4x4.identity, new[] { pivot, pivot + Vector3.up * CupsLift }, StemRadius, StemRadius * 0.9f, 8);
            return body;
        }

        private static SculptMesh BuildArrow()
        {
            var arrow = new SculptMesh();
            arrow.Tube(Metal, Matrix4x4.identity, new[] { Vector3.back * ShaftBack, Vector3.forward * ShaftFront },
                ShaftRadius, ShaftRadius * 0.8f, 8);

            // 화살촉: 수직으로 선 삼각 판. 비스듬히 내려다보는 카메라에서도 뾰족한 촉이 또렷하다.
            // 판의 로컬 Y가 앞(+Z), 두께가 옆(+X)이다.
            var head = new[]
            {
                new Vector2(0f, HeadLength), new Vector2(HeadHalfWidth, 0f), new Vector2(-HeadHalfWidth, 0f),
            };
            arrow.Plate(Accent, SculptMesh.Frame(Vector3.forward * (ShaftFront - 0.01f), Vector3.forward, Vector3.right), head, PlateThickness);

            // 꼬리 깃: 뒤로 젖힌 깃 두 장(위·아래)이 뒤끝에 V자 홈을 남긴다. 화살 깃처럼 읽히고, 넓이가 화살촉보다 훨씬 커
            // 바람에 밀려 바람 아래쪽으로 돈다(그래서 촉이 바람이 불어오는 쪽을 본다). 판의 로컬 Y가 뒤(-Z), X가 위아래다.
            foreach (float side in new[] { 1f, -1f })
            {
                var feather = new[]
                {
                    new Vector2(FeatherGap * side, 0f), new Vector2(FeatherGap * side, FeatherRoot),
                    new Vector2(FeatherHeight * side, FeatherLength), new Vector2(FeatherHeight * side, FeatherLength - FeatherRoot),
                };
                arrow.Plate(Accent, SculptMesh.Frame(Vector3.back * (ShaftBack - FeatherLength), Vector3.back, Vector3.right),
                    feather, FinThickness);
            }
            arrow.Ellipsoid(Metal, SculptMesh.Frame(Vector3.back * ShaftBack, Vector3.back, Vector3.up),
                Vector3.one * ShaftRadius * 1.6f, rings: 5, segments: 8);
            return arrow;
        }

        private static SculptMesh BuildCups()
        {
            var cups = new SculptMesh();
            cups.Ellipsoid(Metal, SculptMesh.Frame(Vector3.zero, Vector3.up, Vector3.forward),
                new Vector3(0.026f, 0.02f, 0.026f), rings: 6, segments: 12);
            cups.Ellipsoid(Metal, SculptMesh.Frame(Vector3.up * 0.026f, Vector3.up, Vector3.forward),
                new Vector3(0.011f, 0.018f, 0.011f), rings: 5, segments: 8);
            for (int i = 0; i < CupCount; i++)
            {
                float angle = i * Mathf.PI * 2f / CupCount;
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Vector3 tangent = Vector3.Cross(Vector3.up, outward);
                cups.Tube(Metal, Matrix4x4.identity, new[] { Vector3.zero, outward * CupArm }, CupArmRadius, CupArmRadius * 0.8f, 6);
                // 컵은 입을 접선 쪽으로 벌린다. 바람이 오목한 쪽을 밀어 돌린다.
                cups.Bowl(Accent, SculptMesh.Frame(outward * (CupArm + CupRadius * 0.4f), tangent, Vector3.up), CupRadius, CupWall);
            }
            return cups;
        }

        private static Mesh Save(SculptMesh sculpt, string name, out string[] parts)
        {
            Mesh mesh = sculpt.Bake($"Vane_{name}", 1f, out parts);
            Directory.CreateDirectory(MeshFolder);
            string path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            // 에셋의 주 오브젝트 이름은 파일 이름과 같아야 한다(CreateAsset이 그렇게 맞추므로 다시 구울 때도 같게).
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
