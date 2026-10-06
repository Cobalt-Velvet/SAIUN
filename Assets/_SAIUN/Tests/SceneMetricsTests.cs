using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Weather;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>레이아웃 확정값과 그로부터 계산되는 파생값이 사양서와 맞는지 확인한다.</summary>
    public class SceneMetricsTests
    {
        [Test]
        public void 창과_레이어_높이는_사양서_8장과_같다()
        {
            Assert.AreEqual(480, SceneMetrics.WindowWidth);
            Assert.AreEqual(680, SceneMetrics.WindowHeight);
            Assert.AreEqual(340, SceneMetrics.SkyLayerHeight);
            Assert.AreEqual(272, SceneMetrics.SceneLayerHeight);
            Assert.AreEqual(68, SceneMetrics.BottomBarHeight);
        }

        [Test]
        public void 레이어_높이의_합은_창_높이와_같다()
        {
            Assert.AreEqual(
                SceneMetrics.WindowHeight,
                SceneMetrics.SkyLayerHeight + SceneMetrics.SceneLayerHeight + SceneMetrics.BottomBarHeight);
        }

        [Test]
        public void 픽셀과_월드_환산은_서로_역연산이다()
        {
            Assert.AreEqual(3.4f, SceneMetrics.PixelsToWorld(340f), 0.0001f);
            Assert.AreEqual(340f, SceneMetrics.WorldToPixels(3.4f), 0.0001f);
            Assert.AreEqual(123.4f, SceneMetrics.WorldToPixels(SceneMetrics.PixelsToWorld(123.4f)), 0.0001f);
        }

        [Test]
        public void 카메라는_하늘과_같은_눈이다()
        {
            // 하늘 셰이더(view_common): 14° 올려다보고 초점 0.95. 세로 화각은 약 55.5°다.
            Assert.AreEqual(14f, SceneMetrics.CameraTiltUpDegrees, 0.0001f);
            Assert.AreEqual(0.95f, SceneMetrics.CameraFocal, 0.0001f);
            Assert.AreEqual(55.51f, SceneMetrics.CameraFieldOfView, 0.01f);
            Assert.Greater(SceneMetrics.CameraEyeHeight, 0f, "데크 위 눈높이");
        }

        [Test]
        public void 눈높이의_먼_점은_하늘의_수평선에_그려진다()
        {
            // 하늘 셰이더의 지평선: 화면 가운데에서 0.95 × tan 14° × 680 ≈ 161 화소 아래.
            Vector3 flat = Quaternion.Euler(0f, SceneMetrics.CameraYawDegrees, 0f) * Vector3.forward;
            Vector2 horizon = SceneMetrics.WorldToWindowPixels(SceneMetrics.CameraPosition + flat * 10000f);
            float expected = SceneMetrics.WindowHeight / 2f
                             + SceneMetrics.CameraFocal * Mathf.Tan(SceneMetrics.CameraTiltUpDegrees * Mathf.Deg2Rad) * SceneMetrics.WindowHeight;
            Assert.AreEqual(expected, horizon.y, 0.05f);
            Assert.AreEqual(SceneMetrics.WindowWidth / 2f, horizon.x, 0.05f);
        }

        [Test]
        public void 한_유닛_깊이에서는_1유닛이_100픽셀이다()
        {
            Vector3 center = SceneMetrics.CameraPosition + SceneMetrics.CameraRotation * Vector3.forward * SceneMetrics.UnitDepth;
            Vector3 up = SceneMetrics.CameraRotation * Vector3.up;
            Vector2 a = SceneMetrics.WorldToWindowPixels(center);
            Vector2 b = SceneMetrics.WorldToWindowPixels(center + up);
            Assert.AreEqual(SceneMetrics.WindowHeight / 2f, a.y, 0.001f, "시선 가운데는 창 가운데");
            Assert.AreEqual(SceneMetrics.PixelsPerUnit, a.y - b.y, 0.01f);
        }

        [Test]
        public void 창_픽셀에서_수평면으로의_역산은_투영의_역연산이다()
        {
            // 지평선(약 501) 아래 픽셀만 눈보다 낮은 면과 만난다.
            foreach (var pixel in new[] { new Vector2(290f, 575f), new Vector2(10f, 600f), new Vector2(470f, 520f) })
            {
                foreach (float height in new[] { 0f, 0.24f, -0.5f })
                {
                    Vector3 ground = SceneMetrics.WindowPixelsToGround(pixel, height);
                    Assert.AreEqual(height, ground.y, 0.0001f);

                    Vector2 back = SceneMetrics.WorldToWindowPixels(ground);
                    Assert.AreEqual(pixel.x, back.x, 0.01f);
                    Assert.AreEqual(pixel.y, back.y, 0.01f);
                }
            }
            Assert.IsTrue(float.IsNaN(SceneMetrics.WindowPixelsToGround(new Vector2(240f, 200f)).x), "지평선 위 픽셀은 바닥과 만나지 않는다");
        }

        [Test]
        public void 유리_배경은_Far_클립_안쪽_화단과_효과보다_먼_곳에_있다()
        {
            Assert.Less(SceneMetrics.BackdropPlaneDistance, SceneMetrics.CameraFarClip);
            Assert.Greater(SceneMetrics.BackdropPlaneDistance, SceneMetrics.UnitDepth * 3f,
                "씬 오브젝트와 비·바람보다 충분히 뒤에 있어야 깊이 테스트로 가려진다.");
            Assert.LessOrEqual(SceneMetrics.UnitDepth, SceneMetrics.ShadowDistance);
        }

        [Test]
        public void 카드의_눈은_사양_그대로고_사이드바는_화소당_각도와_지평선_높이를_지킨다()
        {
            EyeView card = ViewRig.ViewFor(new Vector2Int(SceneMetrics.WindowWidth, SceneMetrics.WindowHeight));
            Assert.AreEqual(SceneMetrics.CameraTiltUpDegrees, card.TiltUp, 0.001f);
            Assert.AreEqual(SceneMetrics.CameraFocal, card.Focal, 0.0001f);
            Assert.AreEqual(0f, card.Yaw, 0.0001f);

            var tall = new Vector2Int(380, 1040);
            EyeView bar = ViewRig.ViewFor(tall);
            Assert.AreEqual(SceneMetrics.CameraFocal * SceneMetrics.WindowHeight, bar.Focal * tall.y, 0.01f, "초점(화소)이 같다");
            float Horizon(EyeView view, int height) => (0.5f - view.Focal * Mathf.Tan(view.TiltUp * Mathf.Deg2Rad)) * height;
            Assert.AreEqual(Horizon(card, SceneMetrics.WindowHeight), Horizon(bar, tall.y), 0.05f, "지평선은 창 아래에서 같은 높이");
            Assert.Greater(bar.Yaw, 0f, "좁아진 만큼 오른쪽으로 돌아 오른쪽 가장자리와의 거리를 지킨다");
            Assert.AreEqual(50f, Mathf.Tan(bar.Yaw * Mathf.Deg2Rad) * SceneMetrics.CameraFocal * SceneMetrics.WindowHeight, 0.01f);
        }

        [Test]
        public void 창_모양은_배율만큼_크고_사이드바는_작업_영역_세로_전체를_채운다()
        {
            WindowLayout card = WindowLayout.Card(1.5f);
            Assert.IsFalse(card.Sidebar);
            Assert.AreEqual(new Vector2Int(480, 680), card.Logical);
            Assert.AreEqual(720, card.Physical.width);
            Assert.AreEqual(1020, card.Physical.height);

            WindowLayout bar = WindowLayout.Dock(1.5f, 2560, 0, 1392, 380);
            Assert.IsTrue(bar.Sidebar);
            Assert.AreEqual(570, bar.Physical.width, "폭 380 × 배율 1.5");
            Assert.AreEqual(2560 - 570, bar.Physical.x, "오른쪽 가장자리에 붙는다");
            Assert.AreEqual(0, bar.Physical.y);
            Assert.AreEqual(1392, bar.Physical.height, "작업 영역 세로 전체");
            Assert.AreEqual(928, bar.Logical.y, "논리 높이 = 실제 높이 / 배율");
            // 아주 높은 모니터(작업 영역 1872)에서는 논리 높이 1100을 넘지 않게 사이드바 전체를 키운다.
            float tall = WindowLayout.SidebarScale(1f, 1872, 1100);
            Assert.AreEqual(1872f / 1100f, tall, 0.0001f);
            WindowLayout big = WindowLayout.Dock(tall, 3000, 0, 1872, 380);
            Assert.AreEqual(1100, big.Logical.y);
            Assert.AreEqual(Mathf.RoundToInt(380 * tall), big.Physical.width, "폭도 같은 비율로 커진다");
            Assert.AreEqual(1.5f, WindowLayout.SidebarScale(1.5f, 1392, 1100), 0.0001f, "보통 모니터는 화면 배율 그대로");
        }

        [Test]
        public void 넓고_큰_창은_초광각처럼_양옆과_위로_더_넓게_본다()
        {
            EyeView card = ViewRig.ViewFor(new Vector2Int(SceneMetrics.WindowWidth, SceneMetrics.WindowHeight));
            var size = new Vector2Int(1200, 900);
            EyeView wide = ViewRig.ViewFor(size);
            Assert.AreEqual(0f, wide.Yaw, 0.0001f, "넓어질 때는 돌지 않아 양옆으로 고르게 열린다");
            Assert.AreEqual(card.Focal * SceneMetrics.WindowHeight, wide.Focal * size.y, 0.01f, "화소당 각도가 같다(그림을 늘리지 않는다)");
            float Horizon(EyeView view, int height) => (0.5f - view.Focal * Mathf.Tan(view.TiltUp * Mathf.Deg2Rad)) * height;
            Assert.AreEqual(Horizon(card, SceneMetrics.WindowHeight), Horizon(wide, size.y), 0.05f, "지평선은 창 아래에서 같은 높이");

            float HorizontalFov(EyeView view, Vector2Int s) => 2f * Mathf.Atan(0.5f * s.x / s.y / view.Focal) * Mathf.Rad2Deg;
            var cardSize = new Vector2Int(SceneMetrics.WindowWidth, SceneMetrics.WindowHeight);
            Assert.Greater(HorizontalFov(wide, size), HorizontalFov(card, cardSize) * 2f, "가로 화각이 두 배 넘게 넓다");

            WindowLayout layout = WindowLayout.Card(1.25f, size);
            Assert.AreEqual(size, layout.Logical);
            Assert.AreEqual(1500, layout.Physical.width);
            Assert.AreEqual(1125, layout.Physical.height);
        }

        [Test]
        public void 가장자리를_끌면_반대쪽은_제자리에_남고_크기는_범위로_잘린다()
        {
            var start = new RectInt(1000, 200, 480, 680);
            var min = new Vector2Int(340, 480);
            var max = new Vector2Int(2560, 1392);

            RectInt right = WindowController.ResizedRect(start, WindowController.ResizeEdge.Right, new Vector2Int(300, 50), min, max);
            Assert.AreEqual(new RectInt(1000, 200, 780, 680), right, "오른쪽만 늘어난다");

            var leftTop = WindowController.ResizeEdge.Left | WindowController.ResizeEdge.Top;
            RectInt corner = WindowController.ResizedRect(start, leftTop, new Vector2Int(-200, -100), min, max);
            Assert.AreEqual(new RectInt(800, 100, 680, 780), corner, "왼쪽 위를 끌면 오른쪽 아래가 제자리다");

            RectInt tiny = WindowController.ResizedRect(start, leftTop, new Vector2Int(400, 400), min, max);
            Assert.AreEqual(min.x, tiny.width);
            Assert.AreEqual(min.y, tiny.height);
            Assert.AreEqual(start.xMax, tiny.xMax, "줄여도 반대쪽은 그대로다");
            Assert.AreEqual(start.yMax, tiny.yMax);

            RectInt huge = WindowController.ResizedRect(start, WindowController.ResizeEdge.Bottom, new Vector2Int(0, 5000), min, max);
            Assert.AreEqual(max.y, huge.height, "작업 영역보다 커지지 않는다");
        }

        [Test]
        public void 가장자리_띠와_모서리를_알아본다()
        {
            var rect = new RectInt(100, 100, 480, 680);
            const int band = 7;
            WindowController.ResizeEdge At(int x, int y, bool leftOnly = false) =>
                WindowController.EdgeAt(rect, new Vector2Int(x, y), band, leftOnly);

            Assert.AreEqual(WindowController.ResizeEdge.None, At(340, 400), "가운데는 끌어 옮긴다");
            Assert.AreEqual(WindowController.ResizeEdge.Left, At(102, 400));
            Assert.AreEqual(WindowController.ResizeEdge.Right, At(578, 400));
            Assert.AreEqual(WindowController.ResizeEdge.Top, At(340, 103));
            Assert.AreEqual(WindowController.ResizeEdge.Bottom, At(340, 777));
            Assert.AreEqual(WindowController.ResizeEdge.Right | WindowController.ResizeEdge.Bottom, At(570, 770), "모서리는 두 배 폭으로 잡힌다");
            Assert.AreEqual(WindowController.ResizeEdge.None, At(50, 400), "창 밖");
            Assert.AreEqual(WindowController.ResizeEdge.Left, At(102, 103, leftOnly: true), "사이드바는 왼쪽만");
            Assert.AreEqual(WindowController.ResizeEdge.None, At(578, 400, leftOnly: true));
        }
    }
}
