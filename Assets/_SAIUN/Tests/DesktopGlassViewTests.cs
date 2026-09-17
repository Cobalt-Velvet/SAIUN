using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Tests
{
    /// <summary>창 뒤 화면을 읽어 흐리게 까는 층의 크기 계산을 확인한다.</summary>
    public class DesktopGlassViewTests
    {
        private GameObject _go;
        private DesktopGlassView _view;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("DesktopGlass", typeof(RectTransform), typeof(RawImage));
            _view = _go.AddComponent<DesktopGlassView>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void 읽어올_크기는_창_크기를_축소_배율로_나눈_값이다()
        {
            Vector2Int size = _view.CaptureSize;

            Assert.Greater(size.x, 0);
            Assert.Greater(size.y, 0);
            Assert.Less(size.x, SceneMetrics.WindowWidth, "축소하지 않으면 흐려지지 않는다.");
            Assert.Less(size.y, SceneMetrics.WindowHeight);
        }

        [Test]
        public void 가로세로_비율이_창과_거의_같다()
        {
            Vector2Int size = _view.CaptureSize;
            float windowRatio = SceneMetrics.WindowWidth / (float)SceneMetrics.WindowHeight;
            float captureRatio = size.x / (float)size.y;

            // 정수 나눗셈이라 약간의 오차는 생긴다.
            Assert.AreEqual(windowRatio, captureRatio, 0.05f);
        }
    }
}
