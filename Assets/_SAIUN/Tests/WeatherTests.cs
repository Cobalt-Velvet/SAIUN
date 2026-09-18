using System.Collections.Generic;
using System.Reflection;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Weather;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P2-05: 바람은 늘 불고 30~120초마다 방향·세기(0~5)가 바뀐다.
    /// 비는 '랜덤'(세트마다)과 '집중 상태 연동'(유예 중 먹구름) 트리거가 정하고, 둘은 함께 켤 수 있다.
    /// 구름은 바람을 따라 흐르고, 비는 강도만큼 내리며 바람만큼 기운다.
    /// </summary>
    public class WeatherTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private readonly Queue<float> _randoms = new Queue<float>();
        private PomodoroStateMachine _sm;
        private WeatherController _weather;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            SettingsStore.DeleteAll();
            var systems = Own(new GameObject("Systems"));
            _sm = systems.AddComponent<PomodoroStateMachine>();

            var weatherGo = Own(new GameObject("Weather"));
            weatherGo.SetActive(false);
            _weather = weatherGo.AddComponent<WeatherController>();
            Set(_weather, "stateMachine", _sm);
            weatherGo.SetActive(true);

            _now = 100f;
            _randoms.Clear();
            _weather.SetSources(() => _now, NextRandom);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _owned)
            {
                if (owned != null) Object.DestroyImmediate(owned);
            }
            _owned.Clear();
            SettingsStore.DeleteAll();
        }

        // ---- 바람 ----

        [Test]
        public void 바람은_처음부터_불고_세기는_0에서_5_사이다()
        {
            Randoms(1f, 0.25f, 0f);
            _weather.SetSources(() => _now, NextRandom);

            Assert.AreEqual(WeatherController.MaxWindStrength, _weather.WindStrength, 0.0001f);
            Assert.AreEqual(1f, _weather.WindAmount, 0.0001f);
            Assert.Less(Vector3.Distance(Vector3.right, _weather.WindDirection), 0.001f, "90도 = 월드 +X");
            Assert.AreEqual(WeatherController.MinWindChangeSeconds, _weather.SecondsUntilWindChange, 0.001f);
        }

        [Test]
        public void 바람은_정해진_시간이_지나면_새_목표로_옮겨_간다()
        {
            Randoms(0f, 0f, 1f);                  // 첫 바람: 세기 0, 120초 뒤 변화
            _weather.SetSources(() => _now, NextRandom);
            Assert.AreEqual(WeatherController.MaxWindChangeSeconds, _weather.SecondsUntilWindChange, 0.001f);

            Randoms(1f, 0.5f, 0.5f);              // 다음 바람: 세기 5
            _now += WeatherController.MaxWindChangeSeconds;
            for (int i = 0; i < 600; i++) _weather.Tick(0.1f);

            Assert.AreEqual(WeatherController.MaxWindStrength, _weather.WindStrength, 0.05f);
            Assert.That(_weather.SecondsUntilWindChange,
                Is.InRange(WeatherController.MinWindChangeSeconds, WeatherController.MaxWindChangeSeconds));
        }

        // ---- 비: 랜덤 ----

        [Test]
        public void 랜덤이_켜져_있으면_세트가_시작될_때_비를_굴린다()
        {
            Randoms(0.1f, 1f, 0f);                // 비 온다, 강도 최대, 최단 지속
            _sm.ChangeState(PomodoroState.Focus);
            Settle();

            Assert.Greater(_weather.RainIntensity, 0.9f);
        }

        [Test]
        public void 확률에_걸리지_않으면_비가_오지_않는다()
        {
            Randoms(0.9f);
            _sm.ChangeState(PomodoroState.Focus);
            Settle();

            Assert.AreEqual(0f, _weather.RainIntensity, 0.001f);
        }

        [Test]
        public void 랜덤을_끄면_굴리지_않는다()
        {
            SettingsStore.WeatherRandom = false;
            Randoms(0.1f, 1f, 0f);
            _sm.ChangeState(PomodoroState.Focus);
            Settle();

            Assert.AreEqual(0f, _weather.RainIntensity, 0.001f);
        }

        [Test]
        public void 비는_지속시간이_지나면_그친다()
        {
            Randoms(0.1f, 1f, 0f);
            _sm.ChangeState(PomodoroState.Focus);
            Settle();
            Assert.Greater(_weather.RainIntensity, 0.9f);

            _now += 10000f;
            Settle();
            Assert.AreEqual(0f, _weather.RainIntensity, 0.01f);
        }

        // ---- 집중 상태 연동 ----

        [Test]
        public void 유예에_들어가면_먹구름과_비가_몰려오고_돌아오면_걷힌다()
        {
            Randoms(0.9f);                        // 랜덤 비는 없다
            _sm.ChangeState(PomodoroState.Focus);
            _sm.ChangeState(PomodoroState.Interrupted);
            Settle();

            Assert.Greater(_weather.Storminess, 0.95f);
            Assert.Greater(_weather.RainIntensity, 0.7f);

            _sm.ChangeState(PomodoroState.Focus);
            Settle();
            Assert.Less(_weather.Storminess, 0.05f);
            Assert.Less(_weather.RainIntensity, 0.05f);
        }

        [Test]
        public void 집중_상태_연동을_끄면_유예에도_맑다()
        {
            SettingsStore.WeatherFocusLinked = false;
            Randoms(0.9f);
            _sm.ChangeState(PomodoroState.Focus);
            _sm.ChangeState(PomodoroState.Interrupted);
            Settle();

            Assert.AreEqual(0f, _weather.Storminess, 0.001f);
        }

        [Test]
        public void 세션이_끝나면_하늘이_갠다()
        {
            Randoms(0.1f, 1f, 1f);
            _sm.ChangeState(PomodoroState.Focus);
            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);
            Settle();
            Assert.Greater(_weather.Storminess, 0.95f, "실패해도 세션이 끝날 때까지 남는다");

            _sm.ChangeState(PomodoroState.Idle);
            Settle();
            Assert.Less(_weather.RainIntensity, 0.01f);
            Assert.Less(_weather.Storminess, 0.01f);
        }

        // ---- 구름 ----

        [Test]
        public void 구름은_바람의_화면_가로_방향으로_흐르고_먹구름이면_더_몰려온다()
        {
            CloudLayer clouds = MakeClouds(density: 3, stormExtra: 2);
            Assert.AreEqual(5, clouds.CloudCount);

            Vector3 screenRight = SceneMetrics.CameraRotation * Vector3.right;
            SetWind(screenRight, 5f);
            clouds.Tick(0.1f);
            Assert.Greater(clouds.DriftSpeed, 0f, "화면 오른쪽으로 부는 바람");

            SetWind(-screenRight, 5f);
            clouds.Tick(0.1f);
            Assert.Less(clouds.DriftSpeed, 0f);

            Image extra = clouds.transform.Find("Cloud_4").GetComponent<Image>();
            Assert.IsFalse(extra.enabled, "맑을 때는 먹구름용 구름이 숨어 있다");

            Randoms(0.9f);
            _sm.ChangeState(PomodoroState.Focus);
            _sm.ChangeState(PomodoroState.Interrupted);
            Settle();
            clouds.Tick(0.1f);
            Assert.IsTrue(extra.enabled);
        }

        // ---- 비 ----

        [Test]
        public void 빗줄기는_강도만큼_내리고_바람만큼_기운다()
        {
            var rainGo = Own(new GameObject("Rain"));
            rainGo.SetActive(false);
            var rain = rainGo.AddComponent<ParticleSystem>();
            var effect = rainGo.AddComponent<RainEffect>();
            Set(effect, "weather", _weather);
            Set(effect, "rain", rain);
            rainGo.SetActive(true);

            Randoms(0.1f, 1f, 1f);
            _sm.ChangeState(PomodoroState.Focus);
            Settle();
            SetWind(SceneMetrics.CameraRotation * Vector3.right, 4f);
            effect.Tick();

            float maxRate = (float)typeof(RainEffect).GetField("maxDropsPerSecond", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(effect);
            Assert.AreEqual(_weather.RainIntensity * maxRate, effect.DropsPerSecond, 0.01f);
            Assert.Greater(effect.Slant, 0f, "화면 오른쪽 바람이면 오른쪽으로 기운다");
            Assert.AreEqual(effect.DropsPerSecond, rain.emission.rateOverTime.constant, 0.01f);
        }

        // ---- 도우미 ----

        private CloudLayer MakeClouds(int density, int stormExtra)
        {
            var canvasGo = Own(new GameObject("Canvas", typeof(Canvas)));
            var areaGo = new GameObject("Clouds", typeof(RectTransform));
            areaGo.transform.SetParent(canvasGo.transform, false);
            areaGo.SetActive(false);
            var area = (RectTransform)areaGo.transform;
            area.sizeDelta = new Vector2(SceneMetrics.WindowWidth, SceneMetrics.SkyLayerHeight);

            var template = new GameObject("CloudTemplate", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            template.transform.SetParent(area, false);

            var texture = Own(new Texture2D(8, 4));
            Sprite sprite = Own(Sprite.Create(texture, new Rect(0, 0, 8, 4), Vector2.one / 2f));

            var clouds = areaGo.AddComponent<CloudLayer>();
            Set(clouds, "weather", _weather);
            Set(clouds, "area", area);
            Set(clouds, "cloudTemplate", template);
            Set(clouds, "sprites", new[] { sprite });
            Set(clouds, "density", density);
            Set(clouds, "stormExtra", stormExtra);
            areaGo.SetActive(true);
            return clouds;
        }

        // 바람을 곧바로 원하는 값으로 둔다.
        private void SetWind(Vector3 direction, float strength)
        {
            float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            Randoms(strength / WeatherController.MaxWindStrength, Mathf.Repeat(angle, 360f) / 360f, 1f);
            _weather.SetSources(() => _now, NextRandom);
        }

        private void Settle()
        {
            for (int i = 0; i < 200; i++) _weather.Tick(0.1f);
        }

        private void Randoms(params float[] values)
        {
            foreach (float value in values) _randoms.Enqueue(value);
        }

        private float NextRandom() => _randoms.Count > 0 ? _randoms.Dequeue() : 0.5f;

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private T Own<T>(T obj) where T : Object
        {
            _owned.Add(obj);
            return obj;
        }
    }
}
