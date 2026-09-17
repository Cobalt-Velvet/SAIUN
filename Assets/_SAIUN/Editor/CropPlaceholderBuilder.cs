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
    /// 작물 단계별 임시 모델과 작물 정의·목록 에셋을 만든다 (P2-03).
    /// 기본 도형을 팔레트 색으로 조합한 자리표시자다. 아트가 들어오면 CropDefinition의 프리팹만 바꾼다.
    /// 치수는 칸 간격 0.4(Flowerbed 기본값)를 기준으로 적고 ModelScale로 한꺼번에 키운다.
    /// 앞뒤 칸과 겹쳐 보여도 되지만 옆 칸을 넘지 않게 한다.
    /// </summary>
    public static class CropPlaceholderBuilder
    {
        public const string CatalogPath = DataFolder + "/CropCatalog.asset";

        private const string PrefabRoot = "Assets/_SAIUN/Prefabs/Crops";
        private const string DataFolder = "Assets/_SAIUN/Data";
        private const string DefinitionFolder = DataFolder + "/Crops";
        private const string MaterialFolder = "Assets/_SAIUN/Art/Materials/Crops";
        private const string LitShaderName = "Universal Render Pipeline/Lit";
        private const float Smoothness = 0.15f;

        // 창 100px = 1유닛이라 칸 기준 치수 그대로는 30px 남짓이다. 위젯에서 알아볼 크기로 키운다.
        private const float ModelScale = 1.5f;

        // 재질 이름
        private const string LeafLight = "Crop_LeafLight";
        private const string Leaf = "Crop_Leaf";
        private const string LeafDark = "Crop_LeafDark";
        private const string Grain = "Crop_Grain";
        private const string Fruit = "Crop_Fruit";
        private const string Mound = "Crop_Mound";

        private static readonly (string Name, Color Color)[] MaterialColors =
        {
            (LeafLight, SaiunPalette.TeaGreen),
            (Leaf, SaiunPalette.MutedTeal),
            (LeafDark, SaiunPalette.JungleTeal),
            (Grain, SaiunPalette.Eggshell),
            (Fruit, SaiunPalette.Warning),
            (Mound, SaiunPalette.DeepJungle),
        };

        private static readonly CropStage[] ModelStages =
        {
            CropStage.Seed, CropStage.Sprout, CropStage.Growing, CropStage.Fruiting, CropStage.Harvestable,
        };

        [MenuItem("SAIUN/Build Crop Placeholders (missing only)")]
        public static void BuildMissing()
        {
            Build(overwrite: false);
            AssetDatabase.SaveAssets();
        }

        // 프리팹을 다시 만들면 내부 fileID가 바뀐다. 작물 정의는 경로로 다시 연결하므로 끊기지 않는다.
        [MenuItem("SAIUN/Rebuild Crop Placeholders (overwrite)")]
        public static void RebuildAll()
        {
            Build(overwrite: true);
            AssetDatabase.SaveAssets();
        }

        /// <summary>임시 모델·정의·목록을 만들고 목록 에셋을 돌려준다.</summary>
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

            Debug.Log($"CropPlaceholderBuilder: 작물 {definitions.Count}종 준비 완료");
            return catalog;
        }

        // ---- 작물 ----

        private delegate void StageShape(CropStage stage, Shape shape);

        private static CropDefinition BuildCrop(string id, string displayName, Dictionary<string, Material> materials,
            bool overwrite, StageShape describe,
            int requiredFocusMinutes, int requiredSets, UnlockCondition unlock, int unlockThreshold)
        {
            string folder = $"{PrefabRoot}/{id}";
            EnsureFolder(folder);

            var prefabs = new GameObject[CropDefinition.StageModelCount];
            for (int i = 0; i < ModelStages.Length; i++)
            {
                CropStage stage = ModelStages[i];
                string path = $"{folder}/{id}_{stage}.prefab";

                if (overwrite || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    var shape = new Shape($"{id}_{stage}", materials);
                    describe(stage, shape);
                    PrefabUtility.SaveAsPrefabAsset(shape.Root, path);
                    Object.DestroyImmediate(shape.Root);
                }

                prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
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

        // 씨앗은 네 작물이 같은 모양이다. 흙 둔덕 위에 씨앗 하나.
        private static void Seed(Shape shape, float seedSize)
        {
            shape.Ball(Mound, new Vector3(0f, 0f, 0f), new Vector3(0.16f, 0.05f, 0.16f));
            shape.Ball(Grain, new Vector3(0f, 0.028f, 0f), new Vector3(seedSize, seedSize * 0.75f, seedSize));
        }

        // 벼: 가는 잎이 포기로 모여 나고, 익으면 이삭이 고개를 숙인다.
        private static void RiceStage(CropStage stage, Shape shape)
        {
            switch (stage)
            {
                case CropStage.Seed:
                    Seed(shape, 0.04f);
                    break;
                case CropStage.Sprout:
                    shape.Clump(LeafLight, 3, height: 0.09f, width: 0.012f, tilt: 14f);
                    break;
                case CropStage.Growing:
                    shape.Clump(Leaf, 6, height: 0.2f, width: 0.014f, tilt: 12f);
                    break;
                case CropStage.Fruiting:
                    shape.Clump(Leaf, 7, height: 0.25f, width: 0.014f, tilt: 11f);
                    shape.Heads(LeafLight, 3, reach: 0.25f, tilt: 11f, droop: 20f, new Vector3(0.024f, 0.05f, 0.024f));
                    break;
                case CropStage.Harvestable:
                    shape.Clump(LeafLight, 7, height: 0.26f, width: 0.014f, tilt: 11f);
                    shape.Heads(Grain, 4, reach: 0.26f, tilt: 11f, droop: 38f, new Vector3(0.03f, 0.062f, 0.03f));
                    break;
            }
        }

        // 밀: 곧은 줄기 끝에 이삭이 선다.
        private static void WheatStage(CropStage stage, Shape shape)
        {
            switch (stage)
            {
                case CropStage.Seed:
                    Seed(shape, 0.036f);
                    break;
                case CropStage.Sprout:
                    shape.Clump(LeafLight, 2, height: 0.08f, width: 0.013f, tilt: 16f);
                    break;
                case CropStage.Growing:
                    shape.Clump(Leaf, 5, height: 0.22f, width: 0.011f, tilt: 5f);
                    shape.Clump(LeafLight, 3, height: 0.1f, width: 0.016f, tilt: 30f);
                    break;
                case CropStage.Fruiting:
                    shape.Clump(Leaf, 5, height: 0.27f, width: 0.011f, tilt: 5f);
                    shape.Heads(LeafLight, 5, reach: 0.27f, tilt: 5f, droop: 0f, new Vector3(0.028f, 0.05f, 0.028f));
                    break;
                case CropStage.Harvestable:
                    shape.Clump(LeafLight, 5, height: 0.29f, width: 0.011f, tilt: 5f);
                    shape.Heads(Grain, 5, reach: 0.29f, tilt: 5f, droop: 6f, new Vector3(0.034f, 0.06f, 0.034f));
                    break;
            }
        }

        // 토마토: 지지대를 따라 덤불이 자라고, 초록 열매가 붉게 익는다.
        private static void TomatoStage(CropStage stage, Shape shape)
        {
            switch (stage)
            {
                case CropStage.Seed:
                    Seed(shape, 0.032f);
                    break;
                case CropStage.Sprout:
                    shape.Stick(LeafDark, Vector3.zero, 0.05f, 0.008f);
                    shape.Ball(LeafLight, new Vector3(0.022f, 0.052f, 0f), new Vector3(0.05f, 0.012f, 0.028f));
                    shape.Ball(LeafLight, new Vector3(-0.022f, 0.052f, 0f), new Vector3(0.05f, 0.012f, 0.028f));
                    break;
                case CropStage.Growing:
                    shape.Stick(Grain, new Vector3(0.04f, 0f, 0.03f), 0.28f, 0.008f);
                    shape.Stick(LeafDark, Vector3.zero, 0.18f, 0.012f);
                    shape.Ring(Leaf, 3, radius: 0.035f, y: 0.1f, rise: 0.03f, new Vector3(0.1f, 0.07f, 0.1f));
                    break;
                case CropStage.Fruiting:
                    shape.Stick(Grain, new Vector3(0.05f, 0f, 0.04f), 0.3f, 0.008f);
                    shape.Ring(LeafDark, 4, radius: 0.045f, y: 0.11f, rise: 0.035f, new Vector3(0.12f, 0.09f, 0.12f));
                    shape.Ring(LeafLight, 3, radius: 0.075f, y: 0.08f, rise: 0.04f, Vector3.one * 0.04f);
                    break;
                case CropStage.Harvestable:
                    shape.Stick(Grain, new Vector3(0.05f, 0f, 0.04f), 0.3f, 0.008f);
                    shape.Ring(LeafDark, 4, radius: 0.045f, y: 0.12f, rise: 0.035f, new Vector3(0.12f, 0.09f, 0.12f));
                    shape.Ring(Fruit, 4, radius: 0.08f, y: 0.08f, rise: 0.035f, Vector3.one * 0.052f);
                    break;
            }
        }

        // 감자: 낮게 퍼지는 잎 무더기에 꽃이 피고, 다 자라면 덩이줄기가 흙 위로 보인다.
        private static void PotatoStage(CropStage stage, Shape shape)
        {
            switch (stage)
            {
                case CropStage.Seed:
                    Seed(shape, 0.06f);
                    break;
                case CropStage.Sprout:
                    shape.Ring(LeafLight, 3, radius: 0.025f, y: 0.03f, rise: 0f, new Vector3(0.045f, 0.02f, 0.055f));
                    break;
                case CropStage.Growing:
                    shape.Ball(Leaf, new Vector3(0f, 0.09f, 0f), new Vector3(0.09f, 0.07f, 0.09f));
                    shape.Ring(Leaf, 4, radius: 0.06f, y: 0.05f, rise: 0f, new Vector3(0.09f, 0.06f, 0.09f));
                    break;
                case CropStage.Fruiting:
                    shape.Ball(Leaf, new Vector3(0f, 0.11f, 0f), new Vector3(0.11f, 0.09f, 0.11f));
                    shape.Ring(Leaf, 5, radius: 0.08f, y: 0.06f, rise: 0f, new Vector3(0.11f, 0.075f, 0.11f));
                    shape.Ring(Grain, 3, radius: 0.05f, y: 0.15f, rise: 0f, Vector3.one * 0.03f);
                    break;
                case CropStage.Harvestable:
                    shape.Ball(LeafDark, new Vector3(0f, 0.12f, 0f), new Vector3(0.12f, 0.1f, 0.12f));
                    shape.Ring(Leaf, 5, radius: 0.085f, y: 0.065f, rise: 0f, new Vector3(0.12f, 0.08f, 0.12f));
                    shape.Ring(Grain, 3, radius: 0.05f, y: 0.17f, rise: 0f, Vector3.one * 0.034f);
                    shape.Ring(Grain, 3, radius: 0.13f, y: 0.012f, rise: 0f, new Vector3(0.06f, 0.045f, 0.05f));
                    break;
            }
        }

        // ---- 도형 조립 ----

        /// <summary>기본 도형을 자식으로 붙여 한 모델을 만든다. 모델의 원점은 흙 윗면이다.</summary>
        private sealed class Shape
        {
            // 포기 안에서 잎·가지를 돌려 놓는 각. 황금각이라 개수와 무관하게 고르게 퍼진다.
            private const float GoldenAngleDegrees = 137.50776f;

            public GameObject Root { get; }
            private readonly Dictionary<string, Material> _materials;

            public Shape(string name, Dictionary<string, Material> materials)
            {
                Root = new GameObject(name);
                _materials = materials;
            }

            public void Ball(string material, Vector3 center, Vector3 size)
            {
                Add(PrimitiveType.Sphere, material, center, Quaternion.identity, size);
            }

            /// <summary>바닥에서 곧게 선 원기둥.</summary>
            public void Stick(string material, Vector3 basePoint, float height, float thickness)
            {
                // Unity 원기둥은 배율 1에서 높이가 2다.
                Add(PrimitiveType.Cylinder, material, basePoint + Vector3.up * (height / 2f), Quaternion.identity,
                    new Vector3(thickness, height / 2f, thickness));
            }

            /// <summary>뿌리에서 바깥으로 기울어 퍼지는 가는 잎 다발.</summary>
            public void Clump(string material, int count, float height, float width, float tilt)
            {
                for (int i = 0; i < count; i++)
                {
                    Quaternion rotation = BladeRotation(i, tilt);
                    Add(PrimitiveType.Cube, material, rotation * (Vector3.up * (height / 2f)), rotation,
                        new Vector3(width, height, width * 0.5f));
                }
            }

            /// <summary>잎 다발 끝에 달리는 이삭. droop만큼 바깥으로 숙인다.</summary>
            public void Heads(string material, int count, float reach, float tilt, float droop, Vector3 size)
            {
                for (int i = 0; i < count; i++)
                {
                    Quaternion stalk = BladeRotation(i, tilt);
                    Vector3 tip = stalk * (Vector3.up * reach);
                    Quaternion head = BladeRotation(i, tilt + droop);
                    // 캡슐은 배율 1에서 높이가 2다. 끝에 매달리게 절반만큼 올린다.
                    Add(PrimitiveType.Capsule, material, tip + head * (Vector3.up * size.y), head, size);
                }
            }

            /// <summary>중심 둘레에 같은 간격으로 놓는 구. rise만큼 번갈아 높낮이를 준다.</summary>
            public void Ring(string material, int count, float radius, float y, float rise, Vector3 size)
            {
                for (int i = 0; i < count; i++)
                {
                    Quaternion around = Quaternion.Euler(0f, i * 360f / count, 0f);
                    float height = y + (i % 2 == 0 ? rise : -rise * 0.5f);
                    Add(PrimitiveType.Sphere, material, around * new Vector3(radius, 0f, 0f) + Vector3.up * height,
                        around, size);
                }
            }

            private static Quaternion BladeRotation(int index, float tilt)
            {
                return Quaternion.Euler(0f, index * GoldenAngleDegrees, 0f) * Quaternion.Euler(0f, 0f, tilt);
            }

            private void Add(PrimitiveType type, string material, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                GameObject part = GameObject.CreatePrimitive(type);
                part.name = $"{type}_{Root.transform.childCount}";
                Object.DestroyImmediate(part.GetComponent<Collider>());
                part.transform.SetParent(Root.transform, false);
                part.transform.SetLocalPositionAndRotation(position * ModelScale, rotation);
                part.transform.localScale = scale * ModelScale;

                var renderer = part.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = _materials[material];
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        // ---- 재질 ----

        private static Dictionary<string, Material> EnsureMaterials()
        {
            Shader lit = Shader.Find(LitShaderName);
            if (lit == null)
            {
                Debug.LogError("CropPlaceholderBuilder: URP Lit 셰이더를 찾지 못했습니다.");
                return null;
            }

            EnsureFolder(MaterialFolder);
            var result = new Dictionary<string, Material>();
            foreach ((string name, Color color) in MaterialColors)
            {
                string path = $"{MaterialFolder}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(lit) { name = name };
                    material.SetColor("_BaseColor", color);
                    material.SetFloat("_Smoothness", Smoothness);
                    AssetDatabase.CreateAsset(material, path);
                }
                result[name] = material;
            }
            return result;
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
