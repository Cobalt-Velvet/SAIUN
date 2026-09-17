using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>P2-02: 화단은 6×2 그리드이고, 칸 좌표가 화단 원점 기준으로 균일하게 놓인다.</summary>
    public class FlowerbedTests
    {
        private GameObject _go;
        private Flowerbed _bed;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Flowerbed");
            _bed = _go.AddComponent<Flowerbed>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void 그리드는_사양서_8장의_6칸_2칸이다()
        {
            Assert.AreEqual(6, _bed.Columns);
            Assert.AreEqual(2, _bed.Rows);
            Assert.AreEqual(12, _bed.CellCount);
        }

        [Test]
        public void 칸_중심은_칸_간격만큼_떨어져_있다()
        {
            float cell = _bed.CellSize;
            Vector3 first = _bed.CellPosition(0, 0);

            Assert.AreEqual(cell, Vector3.Distance(first, _bed.CellPosition(1, 0)), 0.0001f);
            Assert.AreEqual(cell, Vector3.Distance(first, _bed.CellPosition(0, 1)), 0.0001f);
            Assert.AreEqual(cell * 5f, Vector3.Distance(first, _bed.CellPosition(5, 0)), 0.0001f);
        }

        [Test]
        public void 그리드_중심은_화단_원점_위에_있다()
        {
            _go.transform.position = new Vector3(-1f, 0f, -2f);

            Vector3 center = (_bed.CellPosition(0, 0) + _bed.CellPosition(5, 1)) / 2f;
            Assert.AreEqual(-1f, center.x, 0.0001f);
            Assert.AreEqual(-2f, center.z, 0.0001f);
            Assert.AreEqual(_bed.SurfaceHeight + _bed.CropYOffset, center.y, 0.0001f);
        }

        [Test]
        public void 열은_X_행은_Z_방향으로_늘어난다()
        {
            Vector3 origin = _bed.CellPosition(0, 0);
            Assert.Greater(_bed.CellPosition(1, 0).x, origin.x);
            Assert.Greater(_bed.CellPosition(0, 1).z, origin.z);
        }

        [Test]
        public void 범위_밖_칸은_가장자리로_잘린다()
        {
            Assert.AreEqual(_bed.CellPosition(5, 1), _bed.CellPosition(9, 7));
            Assert.AreEqual(_bed.CellPosition(0, 0), _bed.CellPosition(-3, -1));
        }

        [Test]
        public void 외형은_그리드_크기에_맞춰진다()
        {
            var planter = new GameObject("Planter").transform;
            var soil = new GameObject("Soil").transform;
            var ground = new GameObject("Ground").transform;
            planter.SetParent(_go.transform, false);
            soil.SetParent(_go.transform, false);
            ground.SetParent(_go.transform, false);
            for (int i = 0; i < _bed.CellCount; i++) new GameObject($"Tile{i}").transform.SetParent(soil, false);
            SetField("planter", planter);
            SetField("soilRoot", soil);
            SetField("shadowGround", ground);

            _bed.FitVisuals();

            Vector2 grid = _bed.GridSize;
            Assert.Greater(planter.localScale.x, grid.x, "화분은 흙 칸보다 넓다");
            Assert.Greater(planter.localScale.z, grid.y);
            Assert.Greater(ground.localScale.x, planter.localScale.x, "그림자 받이는 화분보다 넓다");

            // 마지막 타일(1행 5열)은 그 칸의 중심에 있어야 한다.
            Vector3 lastTile = soil.GetChild(_bed.CellCount - 1).position;
            Vector3 lastCell = _bed.CellPosition(5, 1);
            Assert.AreEqual(lastCell.x, lastTile.x, 0.0001f);
            Assert.AreEqual(lastCell.z, lastTile.z, 0.0001f);

            // 타일 윗면은 흙 윗면과 같은 높이다.
            float tileTop = lastTile.y + soil.GetChild(0).localScale.y / 2f;
            Assert.AreEqual(_bed.SurfaceHeight, tileTop, 0.0001f);
        }

        [Test]
        public void 화면_오른쪽에_둔_기본_크기_화단은_Scene_Layer_안에_들어간다()
        {
            // 사양서 2-3: 화단은 우측, Scene Layer 중앙 하단. 기본 칸 간격이 그 영역에 맞는지 본다.
            var anchor = new Vector2(
                SceneMetrics.WindowWidth * 0.75f,
                (SceneMetrics.SceneLayerTop + SceneMetrics.SceneLayerBottom) / 2f);
            Vector3 surface = SceneMetrics.WindowPixelsToGround(anchor, _bed.SurfaceHeight);
            _go.transform.position = new Vector3(surface.x, 0f, surface.z);

            float half = _bed.CellSize / 2f;
            foreach (Vector3 corner in new[]
                     {
                         _bed.CellPosition(0, 0) + new Vector3(-half, 0f, -half),
                         _bed.CellPosition(5, 0) + new Vector3(half, 0f, -half),
                         _bed.CellPosition(0, 1) + new Vector3(-half, 0f, half),
                         _bed.CellPosition(5, 1) + new Vector3(half, 0f, half),
                     })
            {
                Vector2 pixel = SceneMetrics.WorldToWindowPixels(corner);
                Assert.That(pixel.x, Is.InRange(SceneMetrics.WindowWidth / 2f, SceneMetrics.WindowWidth), "오른쪽 절반");
                Assert.That(pixel.y, Is.InRange(SceneMetrics.SceneLayerTop, SceneMetrics.SceneLayerBottom), "Scene Layer");
            }
        }

        private void SetField(string name, Object value)
        {
            typeof(Flowerbed)
                .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(_bed, value);
        }
    }
}
