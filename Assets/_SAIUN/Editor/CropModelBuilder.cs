using System.Collections.Generic;
using System.IO;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 작물 단계별 모델과 작물 정의·목록 에셋을 만든다 (P2-03).
    /// 기본 도형을 쌓던 자리표시자 대신 SculptMesh로 잎·줄기·열매를 빚는다.
    ///  - 벼: 휘어 퍼지는 풀잎 포기, 여물수록 고개를 숙이는 이삭(알곡이 번갈아 달린 가지).
    ///  - 밀: 풀잎 포기 위로 곧은 줄기와 알곡 두 줄의 이삭.
    ///  - 토마토: 지지대 옆 줄기에 나선으로 붙는 겹잎, 꼭지 홈과 골이 있는 열매와 꽃받침.
    ///  - 감자: 낮게 퍼지는 겹잎 무더기, 별 모양 꽃, 흙 위로 드러난 덩이줄기.
    /// 단계 모델마다 재질별 서브메시를 가진 메시 에셋 하나와 그것을 그리는 프리팹 하나다(부품 수와 무관하게 렌더러 1개).
    /// 같은 작물·단계는 늘 같은 난수로 빚어 다시 만들어도 모양이 같다. 치수는 칸 간격 0.4 기준이고 ModelScale로 키운다.
    /// 앞뒤 칸과 겹쳐 보여도 되지만 옆 칸을 넘지 않게(반경 0.095 × 2 &lt; 칸 절반 0.2) 한다.
    ///
    /// 눈높이 시점: 흙을 약 5°로 거의 옆에서 보므로 흙 위에 누운 것은 2화소 남짓으로 사라진다.
    /// 그래서 단계마다 흙 위로 선 윤곽으로 알아보게 한다. 앞줄 포기 기준(1유닛 ≈ 120화소)
    ///  - 씨앗: 부푼 씨앗에서 싹 끝이 막 올라온 모습(10화소 안팎). 흙 속 씨앗만으로는 보이지 않는다.
    ///  - 새싹: 20화소 남짓, 자람: 30화소 이상, 결실: 50화소 남짓. 단계마다 눈에 띄게 커진다.
    /// </summary>
    public static class CropModelBuilder
    {
        public const string CatalogPath = DataFolder + "/CropCatalog.asset";

        private const string PrefabRoot = "Assets/_SAIUN/Prefabs/Crops";
        private const string MeshRoot = "Assets/_SAIUN/Art/Meshes/Crops";
        private const string DataFolder = "Assets/_SAIUN/Data";
        private const string DefinitionFolder = DataFolder + "/Crops";
        private const string MaterialFolder = "Assets/_SAIUN/Art/Materials/Crops";
        private const string GradientPath = "Assets/_SAIUN/Art/Textures/leaf_gradient.png";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        // 창 100px = 1유닛이라 칸 기준 치수 그대로는 30px 남짓이다. 위젯에서 알아볼 크기로 키운다.
        // 가는 잎이 2픽셀 아래로 가늘어지지 않게 넉넉히 키우고, 부품도 굵게 빚는다.
        private const float ModelScale = 2f;

        // 잎 그러데이션: 밑동은 이만큼 어둡고 끝으로 갈수록 제 색이 된다.
        private const float LeafBaseShade = 0.66f;
        private const int GradientHeight = 64;
        private const int GradientWidth = 4;

        // 포기 안에서 잎·가지를 돌려 놓는 각. 황금각이라 개수와 무관하게 고르게 퍼진다.
        private const float GoldenAngleDegrees = 137.50776f;

        // 감자 포기에서 맨 위 잎이 맨 아래 잎보다 더 서는 각
        private const float UpperLeafLift = 25f;

        // 꽃 피는 감자 포기의 줄기 키. 꽃대가 그 8할 높이에서 갈라진다.
        private const float FruitingPotatoStem = 0.065f;

        // 떡잎줄기 갈고리가 굽는 각. 끝이 거의 아래를 본다.
        private const float HookArcDegrees = 160f;

        // 재질 이름
        private const string LeafLight = "Crop_LeafLight";
        private const string Leaf = "Crop_Leaf";
        private const string LeafDark = "Crop_LeafDark";
        private const string Grain = "Crop_Grain";
        private const string Fruit = "Crop_Fruit";

        private static readonly (string Name, Color Color, float Smoothness, bool Gradient)[] MaterialLooks =
        {
            (LeafLight, SaiunPalette.CropLeafLight, 0.3f, true),
            (Leaf, SaiunPalette.CropLeaf, 0.32f, true),
            (LeafDark, SaiunPalette.CropLeafDark, 0.34f, true),
            (Grain, SaiunPalette.CropGrain, 0.22f, false),
            (Fruit, SaiunPalette.CropFruit, 0.72f, false),
        };

        private static readonly CropStage[] ModelStages =
        {
            CropStage.Seed, CropStage.Sprout, CropStage.Growing, CropStage.Fruiting, CropStage.Harvestable,
        };

        [MenuItem("SAIUN/Build Crop Models (missing only)")]
        public static void BuildMissing()
        {
            Build(overwrite: false);
            AssetDatabase.SaveAssets();
        }

        // 메시 에셋은 제자리에 덮어써 GUID를 지키고, 프리팹도 같은 경로에 다시 저장한다.
        [MenuItem("SAIUN/Rebuild Crop Models (overwrite)")]
        public static void RebuildAll()
        {
            Build(overwrite: true);
            AssetDatabase.SaveAssets();
        }

        /// <summary>모델·정의·목록을 만들고 목록 에셋을 돌려준다.</summary>
        public static CropCatalog Build(bool overwrite)
        {
            Dictionary<string, Material> materials = EnsureMaterials();
            if (materials == null) return null;

            // 필요 설정과 해금 조건은 사양서 v1.1 7-3 MVP 표 그대로다.
            var definitions = new List<CropDefinition>
            {
                BuildCrop("rice", "쌀", materials, overwrite, RiceStage, 25, 4, UnlockCondition.Default, 0),
                BuildCrop("wheat", "밀", materials, overwrite, WheatStage, 25, 4, UnlockCondition.Default, 0),
                BuildCrop("tomato", "토마토", materials, overwrite, TomatoStage, 45, 4, UnlockCondition.TotalFocusHours, 10),
                BuildCrop("potato", "감자", materials, overwrite, PotatoStage, 45, 4, UnlockCondition.HarvestCount, 10),
            };

            var catalog = AssetDatabase.LoadAssetAtPath<CropCatalog>(CatalogPath);
            if (catalog == null)
            {
                EnsureFolder(DataFolder);
                catalog = ScriptableObject.CreateInstance<CropCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.Configure(definitions);
            EditorUtility.SetDirty(catalog);

            Debug.Log($"CropModelBuilder: 작물 {definitions.Count}종 준비 완료");
            return catalog;
        }

        // ---- 작물 ----

        private delegate void StageShape(CropStage stage, SculptMesh plant, Rng rng);

        private static CropDefinition BuildCrop(string id, string displayName, Dictionary<string, Material> materials,
            bool overwrite, StageShape describe,
            int requiredFocusMinutes, int requiredSets, UnlockCondition unlock, int unlockThreshold)
        {
            string prefabFolder = $"{PrefabRoot}/{id}";
            string meshFolder = $"{MeshRoot}/{id}";
            EnsureFolder(prefabFolder);
            EnsureFolder(meshFolder);

            var prefabs = new GameObject[CropDefinition.StageModelCount];
            for (int i = 0; i < ModelStages.Length; i++)
            {
                CropStage stage = ModelStages[i];
                string name = $"{id}_{stage}";
                string prefabPath = $"{prefabFolder}/{name}.prefab";

                if (overwrite || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                {
                    var plant = new SculptMesh();
                    describe(stage, plant, new Rng(StableSeed(name)));
                    Mesh mesh = SaveMesh(plant.Bake(name, ModelScale, out string[] parts), $"{meshFolder}/{name}.asset");

                    var root = new GameObject(name);
                    root.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = root.AddComponent<MeshRenderer>();
                    var shared = new Material[parts.Length];
                    for (int p = 0; p < parts.Length; p++) shared[p] = materials[parts[p]];
                    renderer.sharedMaterials = shared;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Object.DestroyImmediate(root);
                }

                prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }

            EnsureFolder(DefinitionFolder);
            string definitionPath = $"{DefinitionFolder}/{id}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<CropDefinition>(definitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<CropDefinition>();
                AssetDatabase.CreateAsset(definition, definitionPath);
            }
            definition.Configure(id, displayName, prefabs);
            definition.ConfigureRules(requiredFocusMinutes, requiredSets, unlock, unlockThreshold);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        // 있던 메시 에셋에는 내용만 덮어써 프리팹 참조(GUID)를 지킨다.
        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = Path.GetFileNameWithoutExtension(path);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ---- 벼 ----

        private static void RiceStage(CropStage stage, SculptMesh plant, Rng rng)
        {
            switch (stage)
            {
                case CropStage.Seed:
                    Shoots(plant, rng, Seeds(plant, rng, 3, new Vector3(0.0042f, 0.011f, 0.0035f), spread: 0.02f), 3,
                        length: (0.042f, 0.052f), width: 0.013f);
                    break;
                case CropStage.Sprout:
                    Tuft(plant, rng, i => i == 0 ? Leaf : LeafLight, 4, length: (0.085f, 0.11f), width: 0.019f, tilt: (8f, 20f), bend: (15f, 35f));
                    break;
                case CropStage.Growing:
                    Tuft(plant, rng, i => i % 3 == 0 ? LeafDark : Leaf, 8, length: (0.12f, 0.18f), width: 0.02f, tilt: (5f, 16f), bend: (30f, 70f));
                    break;
                case CropStage.Fruiting:
                    Tuft(plant, rng, i => i % 3 == 0 ? LeafDark : Leaf, 9, length: (0.14f, 0.2f), width: 0.02f, tilt: (5f, 16f), bend: (35f, 75f));
                    for (int i = 0; i < 4; i++)
                    {
                        Panicle(plant, rng, Leaf, LeafLight, i * GoldenAngleDegrees + rng.Range(-20f, 20f),
                            height: rng.Range(0.19f, 0.23f), droop: rng.Range(35f, 55f), kernels: 8);
                    }
                    break;
                case CropStage.Harvestable:
                    Tuft(plant, rng, i => i % 3 == 0 ? Leaf : LeafLight, 9, length: (0.14f, 0.2f), width: 0.02f, tilt: (6f, 18f), bend: (40f, 80f));
                    for (int i = 0; i < 5; i++)
                    {
                        Panicle(plant, rng, LeafLight, Grain, i * GoldenAngleDegrees + rng.Range(-20f, 20f),
                            height: rng.Range(0.19f, 0.23f), droop: rng.Range(95f, 125f), kernels: 9);
                    }
                    break;
            }
        }

        // 줄기가 살짝 기울며 오르다 끝에서 이삭 가지가 droop도만큼 굽어 내려온다. 알곡이 가지 양쪽에 번갈아 달린다.
        private static void Panicle(SculptMesh plant, Rng rng, string stalkMaterial, string grainMaterial, float yaw,
            float height, float droop, int kernels)
        {
            Vector3 outward = Direction(yaw);
            float lean = rng.Range(0.02f, 0.04f);
            var stalk = new List<Vector3>();
            const int stalkPoints = 6;
            for (int i = 0; i < stalkPoints; i++)
            {
                float t = i / (float)(stalkPoints - 1);
                stalk.Add(outward * (lean * t * t) + Vector3.up * (height * t));
            }
            plant.Tube(stalkMaterial, Matrix4x4.identity, stalk, 0.0036f, 0.0024f, 5);

            // 이삭 가지: 줄기 방향에서 시작해 바깥·아래로 굽는 호
            Vector3 start = (stalk[stalkPoints - 1] - stalk[stalkPoints - 2]).normalized;
            Vector3 hinge = Vector3.Cross(Vector3.up, outward).normalized;
            const float panicleLength = 0.07f;
            var axis = new List<Vector3> { stalk[stalkPoints - 1] };
            var tangents = new List<Vector3> { start };
            for (int k = 1; k <= kernels; k++)
            {
                float t = k / (float)kernels;
                Vector3 direction = Quaternion.AngleAxis(droop * t, hinge) * start;
                axis.Add(axis[k - 1] + direction * (panicleLength / kernels));
                tangents.Add(direction);
            }
            plant.Tube(stalkMaterial, Matrix4x4.identity, axis, 0.0022f, 0.0012f, 4);

            for (int k = 1; k <= kernels; k++)
            {
                Vector3 side = Vector3.Cross(tangents[k], outward).normalized * ((k % 2 == 0 ? 1f : -1f) * 0.0045f);
                float size = Mathf.Lerp(1f, 0.7f, k / (float)kernels);
                plant.Ellipsoid(grainMaterial, Frame(axis[k] + side, tangents[k] + side.normalized * 0.6f, outward),
                    new Vector3(0.0048f, 0.0105f, 0.0048f) * size, rings: 5, segments: 6);
            }
        }

        // ---- 밀 ----

        private static void WheatStage(CropStage stage, SculptMesh plant, Rng rng)
        {
            switch (stage)
            {
                case CropStage.Seed:
                    Shoots(plant, rng, Seeds(plant, rng, 2, new Vector3(0.0058f, 0.0095f, 0.005f), spread: 0.015f), 2,
                        length: (0.042f, 0.05f), width: 0.014f);
                    break;
                case CropStage.Sprout:
                    Tuft(plant, rng, i => i == 0 ? Leaf : LeafLight, 3, length: (0.09f, 0.11f), width: 0.021f, tilt: (6f, 16f), bend: (20f, 35f));
                    break;
                case CropStage.Growing:
                    Tuft(plant, rng, i => i % 3 == 0 ? LeafDark : Leaf, 7, length: (0.14f, 0.19f), width: 0.022f, tilt: (6f, 16f), bend: (30f, 60f));
                    break;
                case CropStage.Fruiting:
                    Tuft(plant, rng, i => i % 3 == 0 ? LeafDark : Leaf, 6, length: (0.09f, 0.13f), width: 0.022f, tilt: (10f, 22f), bend: (45f, 80f));
                    Stalks(plant, rng, Leaf, LeafLight, 5);
                    break;
                case CropStage.Harvestable:
                    Tuft(plant, rng, i => LeafLight, 6, length: (0.09f, 0.13f), width: 0.022f, tilt: (10f, 24f), bend: (50f, 85f));
                    Stalks(plant, rng, Grain, Grain, 6);
                    break;
            }
        }

        // 곧게 선 줄기 끝에 알곡 두 줄이 엇갈려 붙은 이삭
        private static void Stalks(SculptMesh plant, Rng rng, string stalkMaterial, string earMaterial, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 outward = Direction(i * GoldenAngleDegrees + rng.Range(-15f, 15f));
                float height = rng.Range(0.19f, 0.24f);
                float lean = rng.Range(0.015f, 0.035f);
                Vector3 basePoint = outward * rng.Range(0.004f, 0.012f);
                var stalk = new List<Vector3>();
                const int stalkPoints = 5;
                for (int p = 0; p < stalkPoints; p++)
                {
                    float t = p / (float)(stalkPoints - 1);
                    stalk.Add(basePoint + outward * (lean * t * t) + Vector3.up * (height * t));
                }
                plant.Tube(stalkMaterial, Matrix4x4.identity, stalk, 0.0038f, 0.0028f, 5);

                Vector3 top = stalk[stalkPoints - 1];
                Vector3 axis = (top - stalk[stalkPoints - 2]).normalized;
                Vector3 side = Vector3.Cross(axis, outward).normalized;
                const int perRow = 7;
                const float earLength = 0.06f;
                for (int k = 0; k < perRow * 2; k++)
                {
                    float t = (k / 2 + (k % 2) * 0.5f) / perRow;
                    float facing = k % 2 == 0 ? 1f : -1f;
                    Vector3 center = top - axis * (earLength * (1f - t)) + side * (facing * 0.0045f);
                    float size = Mathf.Lerp(1f, 0.75f, t);
                    plant.Ellipsoid(earMaterial, Frame(center, axis + side * (facing * 0.55f), outward),
                        new Vector3(0.0055f, 0.0095f, 0.006f) * size, rings: 5, segments: 6);
                }
            }
        }

        // ---- 토마토 ----

        private static void TomatoStage(CropStage stage, SculptMesh plant, Rng rng)
        {
            switch (stage)
            {
                case CropStage.Seed:
                    Seeds(plant, rng, 2, new Vector3(0.0055f, 0.0065f, 0.0016f), spread: 0.018f);
                    Hook(plant, height: 0.04f, yaw: rng.Range(0f, 360f));
                    break;
                case CropStage.Sprout:
                {
                    List<Vector3> stem = Stem(0.075f, 0.008f);
                    plant.Tube(Leaf, Matrix4x4.identity, stem, 0.0045f, 0.0032f, 5);
                    Vector3 top = stem[stem.Count - 1];
                    // 떡잎 한 쌍이 옆으로 펼쳐지고, 그 사이로 첫 본잎 한 쌍이 선다.
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 direction = Vector3.right * s + Vector3.up * 0.45f;
                        plant.Leaf(LeafLight, Frame(top, direction, Vector3.up), 0.046f, 0.02f, 0.2f, -12f);
                        Vector3 upright = Vector3.forward * (s * 0.5f) + Vector3.up;
                        plant.Leaf(Leaf, Frame(top, upright, Vector3.right), 0.03f, 0.02f, 0.3f, -18f);
                    }
                    break;
                }
                case CropStage.Growing:
                    TomatoBush(plant, rng, height: 0.15f, leaves: 4);
                    break;
                case CropStage.Fruiting:
                    TomatoBush(plant, rng, height: 0.19f, leaves: 5);
                    Truss(plant, rng, 0.085f, 40f, LeafLight, 0.014f, calyx: false);
                    Truss(plant, rng, 0.13f, 220f, LeafLight, 0.013f, calyx: false);
                    break;
                case CropStage.Harvestable:
                    TomatoBush(plant, rng, height: 0.19f, leaves: 5);
                    Truss(plant, rng, 0.08f, 40f, Fruit, 0.019f, calyx: true);
                    Truss(plant, rng, 0.125f, 220f, Fruit, 0.017f, calyx: true);
                    break;
            }
        }

        // 지지대 옆에서 살짝 굽이치며 선 줄기에 겹잎이 나선으로 붙는다.
        private static void TomatoBush(SculptMesh plant, Rng rng, float height, int leaves)
        {
            var stake = new List<Vector3> { new Vector3(0.04f, 0f, 0.03f), new Vector3(0.04f, height + 0.04f, 0.03f) };
            plant.Tube(Grain, Matrix4x4.identity, stake, 0.0048f, 0.0042f, 6);

            List<Vector3> stem = Stem(height, 0.01f);
            plant.Tube(Leaf, Matrix4x4.identity, stem, 0.0058f, 0.003f, 6);
            for (int i = 0; i < leaves; i++)
            {
                float t = 0.22f + 0.72f * i / Mathf.Max(1, leaves - 1);
                Vector3 attach = PointOnPath(stem, t);
                Vector3 outward = Direction(i * GoldenAngleDegrees + rng.Range(-12f, 12f));
                Vector3 direction = outward * Mathf.Cos(35f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(35f * Mathf.Deg2Rad);
                float length = Mathf.Lerp(0.06f, 0.045f, t);
                CompoundLeaf(plant, rng, Leaf, i % 3 == 0 ? LeafDark : Leaf, attach, direction, length, pairs: 2,
                    leafletLength: (0.026f, 0.034f), leafletWidth: 0.024f);
            }
        }

        // 줄기에서 바깥·아래로 휘는 꽃대 끝에 열매 세 알. 익은 열매에는 별 모양 꽃받침이 앉는다.
        private static void Truss(SculptMesh plant, Rng rng, float height, float yaw, string material, float radius, bool calyx)
        {
            Vector3 outward = Direction(yaw + rng.Range(-15f, 15f));
            Vector3 start = new Vector3(0f, height, 0f);
            var stalk = new List<Vector3> { start, start + outward * 0.02f + Vector3.up * 0.004f, start + outward * 0.035f - Vector3.up * 0.012f };
            plant.Tube(Leaf, Matrix4x4.identity, stalk, 0.0026f, 0.002f, 4);

            Vector3 end = stalk[stalk.Count - 1];
            Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
            var offsets = new[] { side * radius * 1.05f, -side * radius * 1.05f, outward * radius * 1.3f - Vector3.up * radius * 0.4f };
            foreach (Vector3 offset in offsets)
            {
                Vector3 center = end + offset - Vector3.up * radius * 0.9f;
                float size = rng.Range(0.85f, 1.1f);
                plant.Ellipsoid(material, Frame(center, Vector3.up, outward), new Vector3(radius, radius * 0.86f, radius) * size,
                    lobes: 0.045f, lobeCount: 5, dimple: 0.3f);
                if (calyx)
                {
                    plant.Star(LeafDark, Frame(center + Vector3.up * radius * size * 0.72f, Vector3.up, outward),
                        5, radius * 0.6f, 0.28f, 0.35f);
                }
            }
        }

        // ---- 감자 ----

        private static void PotatoStage(CropStage stage, SculptMesh plant, Rng rng)
        {
            switch (stage)
            {
                case CropStage.Seed:
                {
                    // 씨감자: 반쯤 묻힌 덩이줄기의 눈에서 굵고 짧은 싹이 오른다.
                    plant.Ellipsoid(Grain, Frame(new Vector3(0f, -0.004f, 0f), Vector3.up, Vector3.forward),
                        new Vector3(0.03f, 0.021f, 0.024f), lobes: 0.06f, lobeCount: 3);
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 outward = Direction(i * 120f + rng.Range(-25f, 25f));
                        Vector3 eye = outward * 0.012f + Vector3.up * 0.014f;
                        Vector3 growth = (Vector3.up + outward * 0.35f).normalized;
                        float length = rng.Range(0.018f, 0.026f);
                        var sprout = new List<Vector3> { eye, eye + growth * (length * 0.5f), eye + growth * length + outward * 0.003f };
                        plant.Tube(LeafLight, Matrix4x4.identity, sprout, 0.0042f, 0.0032f, 5);
                        plant.Ellipsoid(LeafLight, Frame(sprout[2], growth, outward), new Vector3(0.0045f, 0.006f, 0.0045f), rings: 4, segments: 6);
                    }
                    break;
                }
                case CropStage.Sprout:
                    PotatoLeaves(plant, rng, 4, i => i == 3 ? Leaf : LeafLight, length: (0.045f, 0.055f), up: 55f, pairs: 1, leaflet: (0.022f, 0.028f), rise: 0.02f);
                    break;
                case CropStage.Growing:
                    PotatoLeaves(plant, rng, 6, i => i % 3 == 0 ? LeafDark : Leaf, length: (0.06f, 0.075f), up: 40f, pairs: 2, leaflet: (0.024f, 0.031f), rise: 0.055f);
                    break;
                case CropStage.Fruiting:
                    PotatoLeaves(plant, rng, 7, i => i % 3 == 0 ? LeafDark : Leaf, length: (0.065f, 0.08f), up: 38f, pairs: 2, leaflet: (0.026f, 0.033f), rise: FruitingPotatoStem);
                    for (int i = 0; i < 3; i++) FlowerStalk(plant, rng, i * 120f + rng.Range(-20f, 20f), FruitingPotatoStem * 0.8f);
                    break;
                case CropStage.Harvestable:
                    PotatoLeaves(plant, rng, 7, i => i % 2 == 0 ? LeafLight : Leaf, length: (0.06f, 0.075f), up: 25f, pairs: 2, leaflet: (0.024f, 0.031f), rise: 0.03f);
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 outward = Direction(i * 120f + 30f + rng.Range(-20f, 20f));
                        plant.Ellipsoid(Grain, Frame(outward * rng.Range(0.06f, 0.075f) + Vector3.up * 0.002f, Vector3.up, outward),
                            new Vector3(0.024f, 0.016f, 0.019f) * rng.Range(0.85f, 1.1f), lobes: 0.07f, lobeCount: 3);
                    }
                    break;
            }
        }

        // 키 rise인 짧은 줄기를 따라 겹잎이 층층이 달린 포기. 아래 잎은 옆으로 눕고 위 잎일수록 선다.
        // 흙 위에 낮게 깔리면 눈높이에서 테두리 선과 겹쳐 보이지 않으므로 줄기로 무더기를 들어 올린다.
        private static void PotatoLeaves(SculptMesh plant, Rng rng, int count, System.Func<int, string> material,
            (float Min, float Max) length, float up, int pairs, (float Min, float Max) leaflet, float rise)
        {
            List<Vector3> stem = Stem(rise, 0.004f);
            plant.Tube(Leaf, Matrix4x4.identity, stem, 0.0042f, 0.003f, 5);
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                Vector3 outward = Direction(i * GoldenAngleDegrees + rng.Range(-12f, 12f));
                float lift = (up + UpperLeafLift * t + rng.Range(-8f, 8f)) * Mathf.Deg2Rad;
                Vector3 direction = outward * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift);
                CompoundLeaf(plant, rng, Leaf, material(i), PointOnPath(stem, t) + outward * 0.006f, direction,
                    rng.Range(length.Min, length.Max), pairs, leaflet, leafletWidth: 0.028f);
            }
        }

        // 잎 무더기 위로 오른 꽃대 끝의 별 모양 꽃 두세 송이. 줄기 baseHeight 높이에서 갈라져 나온다.
        private static void FlowerStalk(SculptMesh plant, Rng rng, float yaw, float baseHeight)
        {
            Vector3 outward = Direction(yaw);
            float height = rng.Range(0.075f, 0.09f);
            Vector3 root = Vector3.up * baseHeight;
            var stalk = new List<Vector3> { root, root + outward * 0.015f + Vector3.up * height * 0.6f, root + outward * 0.03f + Vector3.up * height };
            plant.Tube(Leaf, Matrix4x4.identity, stalk, 0.003f, 0.0022f, 4);
            Vector3 top = stalk[stalk.Count - 1];
            Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
            var offsets = new[] { Vector3.zero, side * 0.02f - Vector3.up * 0.006f, -side * 0.016f + outward * 0.01f - Vector3.up * 0.004f };
            foreach (Vector3 offset in offsets)
            {
                Vector3 center = top + offset;
                Vector3 face = (Vector3.up + outward * 0.5f + offset * 10f).normalized;
                plant.Star(Grain, Frame(center, face, outward), 5, 0.013f, 0.62f, 0.25f);
                plant.Ellipsoid(LeafLight, Frame(center + face * 0.002f, face, outward), Vector3.one * 0.004f, rings: 4, segments: 6);
            }
        }

        // ---- 공통 부품 ----

        // 흙 위에 누운 씨앗들. 싹을 올릴 자리로 씨앗 위치를 돌려준다.
        private static List<Vector3> Seeds(SculptMesh plant, Rng rng, int count, Vector3 radii, float spread)
        {
            var positions = new List<Vector3>();
            for (int i = 0; i < count; i++)
            {
                Vector3 position = Direction(i * GoldenAngleDegrees + rng.Range(0f, 40f)) * (spread * rng.Range(0.3f, 1f));
                Vector3 lying = Direction(rng.Range(0f, 360f));
                plant.Ellipsoid(Grain, Frame(position + Vector3.up * radii.z * 0.6f, lying, Vector3.up), radii, rings: 6, segments: 8);
                positions.Add(position);
            }
            return positions;
        }

        // 씨앗에서 막 올라온 곧은 싹 끝(볏과의 싹집과 첫 잎). 거의 곧게 서서 흙 위로 윤곽이 드러난다.
        // 한낮의 밝은 바다를 등지므로 연한 잎색이 아니라 짙은 잎색이라야 윤곽이 보인다.
        private static void Shoots(SculptMesh plant, Rng rng, List<Vector3> seeds, int count,
            (float Min, float Max) length, float width)
        {
            for (int i = 0; i < Mathf.Min(count, seeds.Count); i++)
            {
                Vector3 outward = seeds[i].sqrMagnitude > 1e-8f ? seeds[i].normalized : Direction(rng.Range(0f, 360f));
                float lean = rng.Range(2f, 10f) * Mathf.Deg2Rad;
                Vector3 growth = Vector3.up * Mathf.Cos(lean) + outward * Mathf.Sin(lean);
                plant.Blade(Leaf, Frame(seeds[i], growth, outward), rng.Range(length.Min, length.Max),
                    width * rng.Range(0.9f, 1.1f), rng.Range(4f, 14f), 0.35f);
            }
        }

        // 흙을 뚫고 고개 숙인 채 올라오는 떡잎줄기(쌍떡잎 싹의 갈고리). 끝에 씨껍질을 쓴 접힌 떡잎이 매달린다.
        private static void Hook(SculptMesh plant, float height, float yaw)
        {
            Vector3 outward = Direction(yaw);
            const float arc = 0.009f;
            const int arcPoints = 6;
            var path = new List<Vector3> { Vector3.zero, Vector3.up * ((height - arc) * 0.5f) };
            Vector3 center = outward * arc + Vector3.up * (height - arc);
            Vector3 tangent = Vector3.up;
            for (int i = 0; i < arcPoints; i++)
            {
                float angle = HookArcDegrees * i / (arcPoints - 1) * Mathf.Deg2Rad;
                path.Add(center - outward * (arc * Mathf.Cos(angle)) + Vector3.up * (arc * Mathf.Sin(angle)));
                tangent = outward * Mathf.Sin(angle) + Vector3.up * Mathf.Cos(angle);
            }
            plant.Tube(LeafLight, Matrix4x4.identity, path, 0.0048f, 0.004f, 6);

            Vector3 tip = path[path.Count - 1] + tangent * 0.006f;
            plant.Ellipsoid(LeafLight, Frame(tip, tangent, outward), new Vector3(0.0055f, 0.0085f, 0.004f), rings: 5, segments: 6);
            plant.Ellipsoid(Grain, Frame(tip + tangent * 0.006f, tangent, outward), new Vector3(0.005f, 0.0045f, 0.0032f),
                rings: 4, segments: 6);
        }

        // 뿌리 둘레에서 바깥으로 기울어 휘는 풀잎 포기
        private static void Tuft(SculptMesh plant, Rng rng, System.Func<int, string> material, int count,
            (float Min, float Max) length, float width, (float Min, float Max) tilt, (float Min, float Max) bend)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 outward = Direction(i * GoldenAngleDegrees + rng.Range(-15f, 15f));
                float lean = rng.Range(tilt.Min, tilt.Max) * Mathf.Deg2Rad;
                Vector3 growth = Vector3.up * Mathf.Cos(lean) + outward * Mathf.Sin(lean);
                Vector3 basePoint = outward * rng.Range(0.002f, 0.009f);
                plant.Blade(material(i), Frame(basePoint, growth, outward), rng.Range(length.Min, length.Max),
                    width * rng.Range(0.85f, 1.1f), rng.Range(bend.Min, bend.Max), 0.35f);
            }
        }

        // 잎자루를 따라 작은 잎이 마주나고 끝에 잎 하나가 달린 겹잎. 잎자루 끝은 조금 처진다.
        private static void CompoundLeaf(SculptMesh plant, Rng rng, string stalkMaterial, string leafMaterial, Vector3 attach,
            Vector3 direction, float length, int pairs, (float Min, float Max) leafletLength, float leafletWidth)
        {
            var stalk = new List<Vector3>();
            const int stalkPoints = 5;
            for (int i = 0; i < stalkPoints; i++)
            {
                float t = i / (float)(stalkPoints - 1);
                stalk.Add(attach + direction * (length * t) - Vector3.up * (length * 0.3f * t * t));
            }
            plant.Tube(stalkMaterial, Matrix4x4.identity, stalk, 0.0028f, 0.0016f, 4);

            for (int p = 1; p <= pairs; p++)
            {
                float t = p / (float)(pairs + 1);
                Vector3 point = PointOnPath(stalk, t);
                Vector3 tangent = TangentOnPath(stalk, t);
                Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 growth = side * s + tangent * 0.6f + Vector3.up * 0.15f;
                    plant.Leaf(leafMaterial, Frame(point, growth, Vector3.up), rng.Range(leafletLength.Min, leafletLength.Max),
                        leafletWidth * rng.Range(0.85f, 1.05f), 0.35f, rng.Range(-38f, -15f));
                }
            }
            Vector3 tip = stalk[stalkPoints - 1];
            plant.Leaf(leafMaterial, Frame(tip, TangentOnPath(stalk, 1f) + Vector3.up * 0.1f, Vector3.up),
                leafletLength.Max * 1.15f, leafletWidth * 1.1f, 0.35f, rng.Range(-30f, -12f));
        }

        // 밑동에서 곧게 오르며 옆으로 살짝 굽이치는 줄기
        private static List<Vector3> Stem(float height, float sway)
        {
            var path = new List<Vector3>();
            const int points = 7;
            for (int i = 0; i < points; i++)
            {
                float t = i / (float)(points - 1);
                path.Add(new Vector3(Mathf.Sin(t * Mathf.PI * 1.5f) * sway, height * t, Mathf.Sin(t * Mathf.PI) * sway * 0.5f));
            }
            return path;
        }

        private static Vector3 PointOnPath(List<Vector3> path, float t)
        {
            float scaled = Mathf.Clamp01(t) * (path.Count - 1);
            int i = Mathf.Min(Mathf.FloorToInt(scaled), path.Count - 2);
            return Vector3.Lerp(path[i], path[i + 1], scaled - i);
        }

        private static Vector3 TangentOnPath(List<Vector3> path, float t)
        {
            float scaled = Mathf.Clamp01(t) * (path.Count - 1);
            int i = Mathf.Min(Mathf.FloorToInt(scaled), path.Count - 2);
            return (path[i + 1] - path[i]).normalized;
        }

        private static Vector3 Direction(float yawDegrees)
        {
            float yaw = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        }

        private static Matrix4x4 Frame(Vector3 position, Vector3 growth, Vector3 face) => SculptMesh.Frame(position, growth, face);

        // 문자열 해시는 실행마다 달라질 수 있어 FNV-1a로 직접 센다.
        private static int StableSeed(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char ch in text) hash = (hash ^ ch) * 16777619u;
                return (int)hash;
            }
        }

        /// <summary>작물·단계마다 고정된 씨로 도는 난수. 다시 만들어도 모양이 같다.</summary>
        private sealed class Rng
        {
            private readonly System.Random _random;
            public Rng(int seed) { _random = new System.Random(seed); }
            public float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);
        }

        // ---- 재질 ----

        private static Dictionary<string, Material> EnsureMaterials()
        {
            Shader lit = Shader.Find(LitShaderName);
            if (lit == null)
            {
                Debug.LogError("CropModelBuilder: URP Lit 셰이더를 찾지 못했습니다.");
                return null;
            }

            Texture2D gradient = EnsureGradient();
            EnsureFolder(MaterialFolder);
            var result = new Dictionary<string, Material>();
            foreach ((string name, Color color, float smoothness, bool useGradient) in MaterialLooks)
            {
                string path = $"{MaterialFolder}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(lit) { name = name };
                    material.SetColor("_BaseColor", color);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetFloat("_Smoothness", smoothness);
                material.SetTexture("_BaseMap", useGradient ? gradient : null);
                EditorUtility.SetDirty(material);
                result[name] = material;
            }
            return result;
        }

        // 세로 그러데이션: 아래(밑동)가 어둡고 위(끝)가 흰색. 잎·줄기의 v를 따라 곱해진다.
        private static Texture2D EnsureGradient()
        {
            if (!File.Exists(GradientPath))
            {
                var texture = new Texture2D(GradientWidth, GradientHeight, TextureFormat.RGBA32, false);
                var pixels = new Color32[GradientWidth * GradientHeight];
                for (int y = 0; y < GradientHeight; y++)
                {
                    float t = Mathf.SmoothStep(0f, 1f, y / (float)(GradientHeight - 1));
                    float value = Mathf.Lerp(LeafBaseShade, 1f, t);
                    for (int x = 0; x < GradientWidth; x++) pixels[y * GradientWidth + x] = new Color(value, value, value, 1f);
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(GradientPath));
                File.WriteAllBytes(GradientPath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(GradientPath, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(GradientPath) is TextureImporter importer)
                {
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(GradientPath);
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }
    }
}
