using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P2-02: 화단은 6×2 그리드이고, 칸 좌표가 화단 원점 기준으로 균일하게 놓인다.
    /// 외형은 둥근 테두리 화분과 둔덕·고랑이 있는 흙 면 메시다.
    /// </summary>
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
            Transform planter = MeshChild("Planter");
            Transform soil = MeshChild("Soil");
            Transform deck = MeshChild("Deck");
            SetField("planter", planter);
            SetField("soilRoot", soil);
            SetField("deck", deck);

            _bed.FitVisuals();

            Vector2 grid = _bed.GridSize;
            Bounds pot = planter.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(grid.x + _bed.RimWidth * 2f, pot.size.x, 0.001f, "화분 바깥 폭 = 흙 + 양쪽 테두리");
            Assert.AreEqual(grid.y + _bed.RimWidth * 2f, pot.size.z, 0.001f);
            Assert.AreEqual(_bed.RimHeight, pot.max.y, 0.0001f, "테두리 윗면 높이");
            Assert.AreEqual(0f, pot.min.y, 0.0001f, "화분은 바닥에 닿는다");

            // 데크: 윗면이 화분 바닥 높이이고, 화분 뒤·오른쪽 모서리에 맞추며(지평선 위를 덮지 않게) 보는 쪽으로 뻗는다.
            Bounds floor = deck.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(0f, floor.max.y, 0.0001f, "데크 윗면 = 화분 바닥");
            Assert.Less(floor.max.x - pot.max.x, 0.1f, "화분 오른쪽으로 거의 뻗지 않는다");
            Assert.Less(floor.max.z - pot.max.z, 0.1f, "화분 뒤로 거의 뻗지 않는다");
            Assert.Less(floor.min.x, pot.min.x - 0.3f, "화분 왼쪽으로 드러난다");
            Assert.Less(floor.min.z, pot.min.z - 1f, "보는 쪽으로 길게 뻗는다");
            Assert.Less(floor.min.y, -1f, "기둥이 아래로 내려간다(떠 있는 데크)");

            Bounds earth = soil.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(grid.x, earth.size.x, 0.0001f, "흙은 화분 안쪽을 꼭 채운다");
            Assert.AreEqual(grid.y, earth.size.z, 0.0001f);
            Assert.Less(earth.max.y, _bed.RimHeight, "흙은 테두리보다 낮다");
            Assert.AreEqual(Vector3.one, planter.localScale, "메시가 크기를 들고 있어 배율은 1이다");
        }

        [Test]
        public void 작물은_둔덕_꼭대기에_서고_칸_사이에는_고랑이_있다()
        {
            SoilShape shape = _bed.SoilShape;
            for (int row = 0; row < _bed.Rows; row++)
            {
                for (int column = 0; column < _bed.Columns; column++)
                {
                    Vector3 cell = _bed.CellPosition(column, row);
                    Assert.AreEqual(_bed.SurfaceHeight, FlowerbedMeshes.SoilHeight(cell.x, cell.z, shape), 0.0001f,
                        $"({column},{row}) 칸 가운데 둔덕 꼭대기가 흙 윗면 높이다");
                }
            }

            Vector3 first = _bed.CellPosition(0, 0);
            Vector3 second = _bed.CellPosition(1, 0);
            float groove = FlowerbedMeshes.SoilHeight((first.x + second.x) / 2f, first.z, shape);
            Assert.Less(groove, _bed.SurfaceHeight - shape.MoundHeight, "칸 사이 고랑은 둔덕 바닥보다 깊다");
            Assert.Greater(groove, shape.Floor - 0.0001f, "흙은 화분 안쪽 벽 아래로 꺼지지 않는다");
        }

        [Test]
        public void 풍향계_자리는_뒤_모서리_테두리_윗면이다()
        {
            Vector3 corner = _bed.RimCorner;
            Vector2 grid = _bed.GridSize;
            Assert.AreEqual(_bed.RimHeight, corner.y, 0.0001f);
            Assert.Greater(corner.x, grid.x / 2f, "흙 바깥, 테두리 위");
            Assert.Greater(corner.z, grid.y / 2f);
            Assert.Less(corner.x, grid.x / 2f + _bed.RimWidth);
            Assert.Less(corner.z, grid.y / 2f + _bed.RimWidth);
        }

        [Test]
        public void 눈높이_카메라에서_기본_크기_화단은_창_안_지평선_아래_하단_바_위에_앉는다()
        {
            // 흙 윗면 가운데를 씬 조립기와 같은 자리(290, 560)에 둔다. 흙 칸 네 귀퉁이가 창 안, 하늘의 지평선(약 501) 아래,
            // 하단 바(612) 위에 보여야 한다. 가운데는 창 가운데보다 오른쪽이다(사양서 2-3 "우측").
            var anchor = new Vector2(290f, 560f);
            Vector3 surface = SceneMetrics.WindowPixelsToGround(anchor, _bed.SurfaceHeight);
            _go.transform.position = new Vector3(surface.x, 0f, surface.z);
            Assert.Greater(anchor.x, SceneMetrics.WindowWidth / 2f);

            float horizon = SceneMetrics.WindowHeight / 2f
                            + SceneMetrics.CameraFocal * Mathf.Tan(SceneMetrics.CameraTiltUpDegrees * Mathf.Deg2Rad) * SceneMetrics.WindowHeight;
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
                Assert.That(pixel.x, Is.InRange(0f, SceneMetrics.WindowWidth), "창 안");
                Assert.That(pixel.y, Is.InRange(horizon, SceneMetrics.SceneLayerBottom), "지평선 아래, 하단 바 위");
            }
        }

        private Transform MeshChild(string name)
        {
            var child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)).transform;
            child.SetParent(_go.transform, false);
            return child;
        }

        private void SetField(string name, Object value)
        {
            typeof(Flowerbed)
                .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(_bed, value);
        }
    }
}
