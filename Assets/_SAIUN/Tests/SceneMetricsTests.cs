using _SAIUN.Scripts.Core;
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
        public void Orthographic_Size는_창_전체에서_pixelsPerUnit_100이_되도록_계산된다()
        {
            Assert.AreEqual(4.3f, SceneMetrics.CameraOrthographicSize, 0.0001f);

            // OS 창 높이 = 2 × Size × pixelsPerUnit 이어야 한다.
            float screenHeight = 2f * SceneMetrics.CameraOrthographicSize * SceneMetrics.PixelsPerUnit;
            Assert.AreEqual(SceneMetrics.FrameHeight, screenHeight, 0.0001f);
        }

        [Test]
        public void 창은_카드_아래로_다리_영역만큼_늘어난다()
        {
            Assert.AreEqual(SceneMetrics.WindowHeight + SceneMetrics.LegRoomHeight, SceneMetrics.FrameHeight);
            Assert.Greater(SceneMetrics.LegRoomHeight, 0);

            // 카메라를 다리 영역의 절반만큼 내리면 창 한가운데가 카드 한가운데보다 그만큼 아래가 된다.
            float frameCenter = SceneMetrics.FrameHeight / 2f;
            float cardCenter = SceneMetrics.WindowHeight / 2f;
            Assert.AreEqual(frameCenter - cardCenter, SceneMetrics.WorldToPixels(SceneMetrics.CameraDownShift), 0.001f);
        }

        [Test]
        public void 화면_평면_위의_점은_그_픽셀에_그려진다()
        {
            var pixel = new Vector2(64f, 680f);
            Vector3 point = SceneMetrics.WindowPixelsToScreenPlane(pixel);
            Vector2 back = SceneMetrics.WorldToWindowPixels(point);
            Assert.AreEqual(pixel.x, back.x, 0.01f);
            Assert.AreEqual(pixel.y, back.y, 0.01f);
            Assert.AreEqual(0f, Vector3.Dot(point, SceneMetrics.CameraRotation * Vector3.forward), 0.0001f);
        }

        [Test]
        public void 픽셀과_월드_환산은_서로_역연산이다()
        {
            Assert.AreEqual(3.4f, SceneMetrics.PixelsToWorld(340f), 0.0001f);
            Assert.AreEqual(340f, SceneMetrics.WorldToPixels(3.4f), 0.0001f);
            Assert.AreEqual(123.4f, SceneMetrics.WorldToPixels(SceneMetrics.PixelsToWorld(123.4f)), 0.0001f);
        }

        [Test]
        public void 카메라_시점은_위_45도_측면_45도다()
        {
            Assert.AreEqual(45f, SceneMetrics.CameraPitchDegrees, 0.0001f);
            Assert.AreEqual(45f, SceneMetrics.CameraYawDegrees, 0.0001f);
        }

        [Test]
        public void 클리핑_범위가_카메라_거리를_감싼다()
        {
            Assert.Less(SceneMetrics.CameraNearClip, SceneMetrics.CameraDistance);
            Assert.Greater(SceneMetrics.CameraFarClip, SceneMetrics.CameraDistance);
        }

        [Test]
        public void 씬_원점은_창_한가운데에_그려진다()
        {
            Vector2 pixel = SceneMetrics.WorldToWindowPixels(Vector3.zero);
            Assert.AreEqual(SceneMetrics.WindowWidth / 2f, pixel.x, 0.0001f);
            Assert.AreEqual(SceneMetrics.WindowHeight / 2f, pixel.y, 0.0001f);
        }

        [Test]
        public void 카메라_위쪽으로_1유닛은_100픽셀_위다()
        {
            Vector3 up = SceneMetrics.CameraRotation * Vector3.up;
            Vector2 pixel = SceneMetrics.WorldToWindowPixels(up);
            Assert.AreEqual(SceneMetrics.WindowHeight / 2f - SceneMetrics.PixelsPerUnit, pixel.y, 0.001f);
        }

        [Test]
        public void 시선_방향으로_움직여도_같은_픽셀이다()
        {
            Vector3 point = new Vector3(0.3f, 0.2f, -1.1f);
            Vector3 forward = SceneMetrics.CameraRotation * Vector3.forward;
            Vector2 a = SceneMetrics.WorldToWindowPixels(point);
            Vector2 b = SceneMetrics.WorldToWindowPixels(point + forward * 4f);
            Assert.AreEqual(a.x, b.x, 0.001f);
            Assert.AreEqual(a.y, b.y, 0.001f);
        }

        [Test]
        public void 창_픽셀에서_수평면으로의_역산은_투영의_역연산이다()
        {
            foreach (var pixel in new[] { new Vector2(352f, 490f), new Vector2(10f, 600f), new Vector2(240f, 340f) })
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
        }

        [Test]
        public void 유리_배경은_Far_클립_안쪽_카메라보다_먼_곳에_있다()
        {
            Assert.Less(SceneMetrics.BackdropPlaneDistance, SceneMetrics.CameraFarClip);
            Assert.Greater(SceneMetrics.BackdropPlaneDistance, SceneMetrics.CameraDistance * 2f,
                "씬 오브젝트보다 충분히 뒤에 있어야 깊이 테스트로 가려진다.");
        }

        [Test]
        public void 씬_원점이_그림자_거리_안에_들어온다()
        {
            // URP의 Shadow Distance는 카메라 기준이다. 원점이 경계에 걸리면 그림자가 사라진다.
            Assert.Less(SceneMetrics.CameraDistance, SceneMetrics.ShadowDistance,
                "카메라가 그림자 범위 경계에 있으면 씬의 그림자가 잘린다.");
        }
    }
}
