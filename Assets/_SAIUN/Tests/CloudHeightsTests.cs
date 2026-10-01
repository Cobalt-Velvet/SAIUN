using System.Collections.Generic;
using _SAIUN.Scripts.Weather;
using NUnit.Framework;

namespace _SAIUN.Tests
{
    /// <summary>
    /// 구름층 높이(2026-10-01 "다양화"): 같은 종류도 나타날 때마다 다른 높이에 뜨고, 보이는 동안은 높이가 그대로이며,
    /// 적운 밑면은 하루 동안 올라간다.
    /// </summary>
    public class CloudHeightsTests
    {
        private readonly Queue<float> _randoms = new Queue<float>();
        private readonly Dictionary<CloudKind, float> _cover = new Dictionary<CloudKind, float>();

        private float NextRandom() => _randoms.Count > 0 ? _randoms.Dequeue() : 0.5f;
        private float Cover(CloudKind kind) => _cover.TryGetValue(kind, out float c) ? c : 0f;

        [Test]
        public void 층마다_제_범위_안에서_높이를_고른다()
        {
            var heights = new CloudHeights();
            for (int i = 0; i < 6; i++) _randoms.Enqueue(0f);
            heights.Reset(0.5f, NextRandom);
            float lowCirrus = heights.Altitude(CloudKind.Cirrus);
            float lowAltocumulus = heights.Altitude(CloudKind.Altocumulus);

            for (int i = 0; i < 6; i++) _randoms.Enqueue(1f);
            heights.Reset(0.5f, NextRandom);
            Assert.Greater(heights.Altitude(CloudKind.Cirrus), lowCirrus);
            Assert.Greater(heights.Altitude(CloudKind.Altocumulus), lowAltocumulus);
            Assert.Greater(lowCirrus, heights.Altitude(CloudKind.Altocumulus), "높은 구름은 중층 구름보다 늘 높다");
            Assert.Greater(heights.Altitude(CloudKind.Stratocumulus), 1f);
            Assert.AreEqual(0f, heights.Altitude(CloudKind.Stratus), "안개구름은 높이를 바꾸지 않는다");
        }

        [Test]
        public void 보이는_동안은_그대로이고_사라지면_다음에_뜰_높이를_새로_고른다()
        {
            var heights = new CloudHeights();
            heights.Reset(0.5f, NextRandom);
            float first = heights.Altitude(CloudKind.Cirrocumulus);

            _cover[CloudKind.Cirrocumulus] = 0.6f;
            for (int i = 0; i < 100; i++)
            {
                _randoms.Enqueue(0.9f);
                heights.Tick(1f, 0.5f, Cover, NextRandom);
            }
            Assert.AreEqual(first, heights.Altitude(CloudKind.Cirrocumulus), "보이는 동안 높이가 튀지 않는다");

            _cover[CloudKind.Cirrocumulus] = 0f;
            _randoms.Clear();
            _randoms.Enqueue(0.95f);
            heights.Tick(1f, 0.5f, Cover, NextRandom);
            Assert.Greater(heights.Altitude(CloudKind.Cirrocumulus), first, "사라진 뒤 새 높이");
        }

        [Test]
        public void 적운_밑면은_아침에_낮고_오후에_높으며_천천히_따라간다()
        {
            var heights = new CloudHeights();
            heights.Reset(0.05f, NextRandom);
            float morning = heights.CumulusBase;

            heights.Tick(1f, 0.8f, Cover, NextRandom);
            Assert.Less(heights.CumulusBase - morning, 0.05f, "한 번에 오르지 않는다");

            for (int i = 0; i < 1000; i++) heights.Tick(1f, 0.8f, Cover, NextRandom);
            Assert.Greater(heights.CumulusBase - morning, 0.5f, "오후엔 밑면이 높다");
        }
    }
}
