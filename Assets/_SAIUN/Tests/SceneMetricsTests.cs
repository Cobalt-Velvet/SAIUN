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
    }
}
