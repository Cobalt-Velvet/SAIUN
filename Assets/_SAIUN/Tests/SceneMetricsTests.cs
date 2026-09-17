using _SAIUN.Scripts.Core;
using NUnit.Framework;

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
        public void Orthographic_Size는_pixelsPerUnit_100이_되도록_계산된다()
        {
            Assert.AreEqual(3.4f, SceneMetrics.CameraOrthographicSize, 0.0001f);

            // 화면 높이 = 2 × Size × pixelsPerUnit 이어야 한다.
            float screenHeight = 2f * SceneMetrics.CameraOrthographicSize * SceneMetrics.PixelsPerUnit;
            Assert.AreEqual(SceneMetrics.WindowHeight, screenHeight, 0.0001f);
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
        public void 씬_원점이_그림자_거리_안에_들어온다()
        {
            // URP의 Shadow Distance는 카메라 기준이다. 원점이 경계에 걸리면 그림자가 사라진다.
            Assert.Less(SceneMetrics.CameraDistance, SceneMetrics.ShadowDistance,
                "카메라가 그림자 범위 경계에 있으면 씬의 그림자가 잘린다.");
        }
    }
}
