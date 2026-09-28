using System;
using System.Collections.Generic;
using System.Reflection;
using _SAIUN.Scripts.Weather;
using NUnit.Framework;

namespace _SAIUN.Tests
{
    /// <summary>
    /// 구름 예보(2026-09-28): 맑은 날 장면은 몇 분마다 때·바람에 따라 무작위로 바뀌고,
    /// 비는 권층운 → 고층운 → 난층운, 뇌우는 모루·아치구름·유방운, 드문 구름은 정해진 조건에서만 뜬다.
    /// </summary>
    public class CloudForecastTests
    {
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
        private const int Rolls = 4000;

        private static SkyInputs Day(float progress, float wind = 0f)
        {
            return new SkyInputs { Progress = progress, Wind = wind };
        }

        private static CloudForecast Make(Func<float> random)
        {
            var forecast = new CloudForecast();
            forecast.SetRandom(random);
            return forecast;
        }

        private static Func<float> Seeded(int seed)
        {
            var rng = new Random(seed);
            return () => (float)rng.NextDouble();
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, Flags).SetValue(target, value);
        }

        private static float Get(object target, string field)
        {
            return (float)target.GetType().GetField(field, Flags).GetValue(target);
        }

        // 장면을 여러 번 고른 횟수
        private static Dictionary<SkyScene, int> CountScenes(CloudForecast forecast, SkyInputs inputs)
        {
            var counts = new Dictionary<SkyScene, int>();
            foreach (SkyScene scene in Enum.GetValues(typeof(SkyScene))) counts[scene] = 0;
            for (int i = 0; i < Rolls; i++)
            {
                forecast.ChooseScene(inputs);
                counts[forecast.Scene]++;
            }
            return counts;
        }

        [Test]
        public void 장면은_몇_분마다_바뀌고_같은_장면이_이어지지_않는다()
        {
            CloudForecast forecast = Make(Seeded(1));
            forecast.Tick(0f, Day(0.5f));
            Assert.GreaterOrEqual(forecast.SecondsUntilChange, 5f * 60f);
            Assert.LessOrEqual(forecast.SecondsUntilChange, 10f * 60f);

            SkyScene first = forecast.Scene;
            forecast.Tick(forecast.SecondsUntilChange + 0.1f, Day(0.5f));
            Assert.AreNotEqual(first, forecast.Scene, "시간이 다 되면 다른 장면으로 바뀐다");

            for (int i = 0; i < 300; i++)
            {
                SkyScene before = forecast.Scene;
                forecast.ChooseScene(Day(0.5f));
                Assert.AreNotEqual(before, forecast.Scene);
            }
        }

        [Test]
        public void 아침엔_안개와_양떼구름이_한낮엔_뭉게구름_떼가_잦다()
        {
            Dictionary<SkyScene, int> morning = CountScenes(Make(Seeded(2)), Day(0.05f));
            Dictionary<SkyScene, int> noon = CountScenes(Make(Seeded(3)), Day(0.5f));

            Assert.Greater(morning[SkyScene.MorningFog], noon[SkyScene.MorningFog] * 5, "안개는 아침에 낀다");
            Assert.Greater(morning[SkyScene.Altocumulus], noon[SkyScene.Altocumulus], "양떼구름은 아침에 잦다");
            foreach (KeyValuePair<SkyScene, int> pair in noon)
            {
                if (pair.Key != SkyScene.FairCumulus) Assert.Greater(noon[SkyScene.FairCumulus], pair.Value, "한낮엔 뭉게구름 떼가 가장 잦다");
            }
        }

        [Test]
        public void 바람이_세면_새털구름과_두루마리구름이_잦다()
        {
            Dictionary<SkyScene, int> calm = CountScenes(Make(Seeded(4)), Day(0.5f, 0f));
            Dictionary<SkyScene, int> windy = CountScenes(Make(Seeded(5)), Day(0.5f, 1f));
            Assert.Greater(windy[SkyScene.Cirrus], calm[SkyScene.Cirrus] * 1.4f);
            Assert.Greater(windy[SkyScene.Stratocumulus], calm[SkyScene.Stratocumulus] * 1.4f);
        }

        [Test]
        public void 비가_다가오면_권층운_고층운_난층운_순으로_두꺼워지고_맑은_날_구름과_탑이_가려진다()
        {
            CloudForecast forecast = Make(Seeded(6));
            var inputs = Day(0.5f);

            inputs.Rain = 0.06f;   // 전선이 막 다가온다
            forecast.Tick(0.1f, inputs);
            Assert.Greater(forecast.Target(CloudKind.Cirrostratus), 0.2f, "먼저 햇무리구름이 덮는다");
            Assert.Less(forecast.Target(CloudKind.Altostratus), 0.01f);
            Assert.AreEqual(0f, forecast.Target(CloudKind.Nimbostratus), 0.0001f);

            inputs.Rain = 0.24f;
            forecast.Tick(0.1f, inputs);
            Assert.Greater(forecast.Target(CloudKind.Altostratus), 0.4f, "다음은 차일구름");
            Assert.Less(forecast.Target(CloudKind.Nimbostratus), 0.05f);

            inputs.Rain = 0.6f;
            forecast.Tick(0.1f, inputs);
            Assert.Greater(forecast.Target(CloudKind.Nimbostratus), 0.85f, "비가 한창이면 비구름이 덮는다");
            Assert.Less(forecast.TowerPresence, 0.3f, "탑이 가려진다");
            foreach (CloudKind fair in new[] { CloudKind.Cumulus, CloudKind.Cirrus, CloudKind.Cirrocumulus, CloudKind.Stratocumulus, CloudKind.Stratus, CloudKind.Lenticular })
            {
                Assert.AreEqual(0f, forecast.Target(fair), 0.0001f, fair + "는 전선 비에 가려진다");
            }
        }

        [Test]
        public void 뇌우가_몰려오면_아치구름이_잠깐_서고_모루가_서며_탑이_뚜렷하다()
        {
            CloudForecast forecast = Make(Seeded(7));
            var inputs = Day(0.5f);
            forecast.Tick(0.1f, inputs);
            Assert.AreEqual(0f, forecast.Target(CloudKind.Arcus), 0.0001f);

            inputs.Storm = 0.5f;
            inputs.Rain = 0.4f;
            forecast.Tick(0.1f, inputs);
            Assert.AreEqual(1f, forecast.Target(CloudKind.Arcus), 0.0001f, "먹구름이 몰려오는 순간 선다");
            Assert.Greater(forecast.Target(CloudKind.Anvil), 0.8f);
            Assert.Less(forecast.Target(CloudKind.Cirrostratus), 0.01f, "뇌우의 비는 전선 비가 아니다");

            inputs.Storm = 1f;
            inputs.Rain = 0.8f;
            forecast.Tick(Get(forecast, "arcusSeconds") + 1f, inputs);
            Assert.AreEqual(0f, forecast.Target(CloudKind.Arcus), 0.0001f, "아치구름은 잠깐이다");
            Assert.AreEqual(Get(forecast, "stormDeck"), forecast.Target(CloudKind.Nimbostratus), 0.001f, "먹구름 층은 틈을 남긴다");
            Assert.AreEqual(1f, forecast.TowerPresence, 0.0001f, "적란운은 뚜렷하다");
            Assert.AreEqual(0f, forecast.Target(CloudKind.Lenticular), 0.0001f, "폭풍이면 렌즈구름은 숨는다");
        }

        [Test]
        public void 짙은_폭풍이_걷히면_가끔_유방운이_남는다()
        {
            foreach (bool lucky in new[] { true, false })
            {
                CloudForecast forecast = Make(() => lucky ? 0f : 0.999f);
                var inputs = Day(0.8f);
                forecast.Tick(0.1f, inputs);
                inputs.Storm = 1f;
                forecast.Tick(0.1f, inputs);
                inputs.Storm = 0.3f;
                forecast.Tick(0.1f, inputs);
                Assert.AreEqual(lucky ? 1f : 0f, forecast.Target(CloudKind.Mammatus), 0.0001f);

                inputs.Storm = 0f;
                forecast.Tick(Get(forecast, "mammatusSeconds") + 1f, inputs);
                Assert.AreEqual(0f, forecast.Target(CloudKind.Mammatus), 0.0001f, "잠깐 남았다 사라진다");
            }
        }

        [Test]
        public void 해가_질_때마다_한_번_야광운을_굴리고_해가_지면_뭉게구름이_스러진다()
        {
            float next = 0f;
            CloudForecast forecast = Make(() => next);
            var inputs = Day(1f);
            forecast.Tick(0.1f, inputs);
            Assert.AreEqual(0f, forecast.Target(CloudKind.Noctilucent), 0.0001f, "해가 지기 전에는 없다");

            inputs.Twilight = 0.5f;
            forecast.Tick(0.1f, inputs);
            Assert.IsTrue(forecast.NoctilucentTonight);
            Assert.AreEqual(1f, forecast.Target(CloudKind.Noctilucent), 0.0001f);

            next = 0.999f;
            forecast.Tick(0.1f, inputs);
            Assert.IsTrue(forecast.NoctilucentTonight, "한 번 굴린 박명에는 그대로다");

            inputs.Twilight = 0f;
            forecast.Tick(0.1f, inputs);
            inputs.Twilight = 0.5f;
            forecast.Tick(0.1f, inputs);
            Assert.IsFalse(forecast.NoctilucentTonight, "다음 박명에 다시 굴린다");

            inputs.Twilight = 1f;
            forecast.Tick(0.1f, inputs);
            Assert.AreEqual(0f, forecast.Target(CloudKind.Cumulus), 0.0001f, "해가 지면 뭉게구름이 스러진다");
            Assert.AreEqual(0f, forecast.Target(CloudKind.Lenticular), 0.0001f, "빛을 잃은 렌즈구름도 스러진다");
        }

        [Test]
        public void 구멍구름은_조개구름과_양떼구름_장면에서만_뚫린다()
        {
            CloudForecast forecast = Make(Seeded(8));
            int holes = 0;
            for (int i = 0; i < Rolls; i++)
            {
                forecast.ChooseScene(Day(0.4f));
                forecast.Tick(0.01f, Day(0.4f));
                if (forecast.Target(CloudKind.FallstreakHole) <= 0f) continue;
                holes++;
                Assert.IsTrue(forecast.Scene == SkyScene.Cirrocumulus || forecast.Scene == SkyScene.Altocumulus, forecast.Scene.ToString());
            }
            Assert.Greater(holes, 0, "드물지만 뚫린다");
            Assert.Less(holes, Rolls / 10, "드물다");
        }

        [Test]
        public void 채운_렌즈구름은_주기의_대부분_떠_있고_층구름이_덮으면_숨는다()
        {
            // 난수 0이면 맑음 장면(렌즈구름을 가리는 층이 없다)
            CloudForecast forecast = Make(() => 0f);
            Set(forecast, "lensCycleMinutes", 1f);
            forecast.Tick(0.01f, Day(0.3f));
            Assert.AreEqual(SkyScene.Clear, forecast.Scene);
            Assert.Greater(forecast.Target(CloudKind.Lenticular), 0.99f, "켜자마자 떠 있다");

            int shown = 0;
            float lowest = float.MaxValue;
            const int Samples = 120;
            for (int i = 0; i < Samples; i++)
            {
                forecast.Tick(0.5f, Day(0.3f));
                lowest = Math.Min(lowest, forecast.Target(CloudKind.Lenticular));
                if (forecast.Target(CloudKind.Lenticular) > 0.5f) shown++;
            }
            Assert.AreEqual(0f, lowest, 0.0001f, "주기마다 한 번은 사라진다");
            // 피어나고 사라지는 시간의 절반씩을 빼면 온전히 떠 있는 비율이다.
            float expected = Get(forecast, "lensPresence") - Get(forecast, "lensFade");
            Assert.AreEqual(expected, shown / (float)Samples, 0.05f);

            Set(forecast, "_lensTime", 0.25f * 60f);
            var rainy = Day(0.3f);
            rainy.Rain = 0.6f;
            forecast.Tick(0.01f, rainy);
            Assert.AreEqual(0f, forecast.Target(CloudKind.Lenticular), 0.0001f, "비구름이 덮으면 숨는다");
        }
    }
}
