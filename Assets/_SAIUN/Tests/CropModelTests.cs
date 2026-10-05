using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>
    /// 눈높이 시점: 흙을 거의 옆에서 보므로 작물은 흙 위로 선 키로만 보인다.
    /// 앞줄 포기가 창에서 차지하는 키(화소)로 단계마다 알아볼 수 있는지, 단계마다 커지는지 본다.
    /// </summary>
    public class CropModelTests : MainSceneTestBase
    {
        // 씨앗 단계는 싹 끝이 막 올라온 모습이라도 흙 위로 이만큼은 서야 화분 테두리 선과 구별된다.
        private const float SeedMinPixels = 8f;

        // 새싹은 씨앗보다 확실히 커 보여야 발아가 눈에 띈다.
        private const float SproutMinPixels = 18f;

        // 앞줄 가운데 칸
        private const int FrontColumn = 2;
        private const int FrontRow = 0;

        private static readonly CropStage[] GrowingOrder =
        {
            CropStage.Seed, CropStage.Sprout, CropStage.Growing, CropStage.Fruiting,
        };

        private Flowerbed _bed;

        protected override void OnSceneLoaded()
        {
            _bed = Find<Flowerbed>();
        }

        [Test]
        public void 씨앗과_새싹도_눈높이에서_알아볼_만큼_흙_위로_선다()
        {
            foreach (CropDefinition crop in Gm.CropCatalog.Crops)
            {
                Assert.GreaterOrEqual(StandingPixels(crop, CropStage.Seed), SeedMinPixels, $"{crop.Id} 씨앗");
                Assert.GreaterOrEqual(StandingPixels(crop, CropStage.Sprout), SproutMinPixels, $"{crop.Id} 새싹");
            }
        }

        [Test]
        public void 결실까지는_단계마다_키가_커진다()
        {
            foreach (CropDefinition crop in Gm.CropCatalog.Crops)
            {
                for (int i = 1; i < GrowingOrder.Length; i++)
                {
                    Assert.Greater(StandingPixels(crop, GrowingOrder[i]), StandingPixels(crop, GrowingOrder[i - 1]),
                        $"{crop.Id} {GrowingOrder[i - 1]} → {GrowingOrder[i]}");
                }
            }
        }

        // 앞줄 가운데 칸에 심은 모델이 흙(뿌리 높이) 위로 창에서 차지하는 키. 흙 속 부분은 보이지 않으므로 뺀다.
        private float StandingPixels(CropDefinition crop, CropStage stage)
        {
            GameObject prefab = crop.GetStagePrefab(stage);
            Assert.IsNotNull(prefab, $"{crop.Id} {stage} 모델이 없다");
            Bounds bounds = prefab.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 root = _bed.CellPosition(FrontColumn, FrontRow);
            Vector2 top = SceneMetrics.WorldToWindowPixels(root + Vector3.up * bounds.max.y);
            Vector2 bottom = SceneMetrics.WorldToWindowPixels(root + Vector3.up * Mathf.Max(0f, bounds.min.y));
            return bottom.y - top.y;
        }
    }
}
