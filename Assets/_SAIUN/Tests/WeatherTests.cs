using System.Collections.Generic;
using System.Reflection;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Lighting;
using _SAIUN.Scripts.Timer;
using _SAIUN.Scripts.Weather;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P2-05: 바람은 늘 불고 30~120초마다 방향·세기(0~5)가 바뀐다.
    /// 비는 '랜덤'(세트마다)과 '집중 상태 연동'(유예 중 먹구름) 트리거가 정하고, 둘은 함께 켤 수 있다.
    /// 구름은 바람을 따라 흐르고 햇빛 색에 물들며, 비는 강도만큼 내리며 바람만큼 기울고 화단에 튄다.
    /// 풍향계·바람결·잎이 바람의 방향과 세기를 보여 주고, 비가 오면 흙이 젖었다가 그치면 천천히 마른다.
    /// </summary>
    public class WeatherTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private readonly Queue<float> _randoms = new Queue<float>();
        private PomodoroStateMachine _sm;
        private WeatherController _weather;
        private Material _cloudMaterial;
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

            RawImage extra = clouds.transform.Find("Cloud_4").GetComponent<RawImage>();
            Assert.IsFalse(extra.enabled, "맑을 때는 먹구름용 구름이 숨어 있다");

            Randoms(0.9f);
            _sm.ChangeState(PomodoroState.Focus);
            _sm.ChangeState(PomodoroState.Interrupted);
            Settle();
            clouds.Tick(0.1f);
            Assert.IsTrue(extra.enabled);
        }

        [Test]
        public void 구름은_햇빛의_색과_방향을_받고_원본_재질은_건드리지_않는다()
        {
            PomodoroTimer timer = _sm.gameObject.AddComponent<PomodoroTimer>();
            var sunGo = Own(new GameObject("Sun"));
            sunGo.SetActive(false);
            var light = sunGo.AddComponent<Light>();
            light.type = LightType.Directional;
            var orbit = sunGo.AddComponent<SunOrbitController>();
            Set(orbit, "sun", light);
            Set(orbit, "timer", timer);
            sunGo.SetActive(true);

            CloudLayer clouds = MakeClouds(density: 2, stormExtra: 0, sun: orbit);
            Color assetColor = _cloudMaterial.GetColor("_SunColor");
            RawImage first = clouds.transform.Find("Cloud_0").GetComponent<RawImage>();
            RawImage second = clouds.transform.Find("Cloud_1").GetComponent<RawImage>();
            Material runtime = first.material;
            Assert.AreNotSame(_cloudMaterial, runtime, "실행 중에는 복사본을 쓴다");
            Assert.AreNotEqual(Mathf.Floor(first.uvRect.x), Mathf.Floor(second.uvRect.x), "구름마다 모양 시드가 다르다");

            orbit.Apply(0f);
            clouds.Tick(0.1f);
            Color dawn = runtime.GetColor("_SunColor");
            Vector2 dawnDirection = clouds.SunScreenDirection;

            orbit.Apply(0.5f);
            clouds.Tick(0.1f);
            Color noon = runtime.GetColor("_SunColor");
            Vector2 noonDirection = clouds.SunScreenDirection;

            Assert.Greater(dawn.r - dawn.b, noon.r - noon.b + 0.1f, "아침 햇빛은 한낮보다 따뜻한 색이다");
            Assert.Greater(noonDirection.y, dawnDirection.y, "해가 높을수록 빛이 구름 위쪽에서 든다");
            Assert.AreEqual(assetColor, _cloudMaterial.GetColor("_SunColor"), "원본 재질은 그대로다");

            Randoms(0.9f);
            _sm.ChangeState(PomodoroState.Focus);
            _sm.ChangeState(PomodoroState.Interrupted);
            Settle();
            clouds.Tick(0.1f);
            Assert.Greater(runtime.GetFloat("_Storm"), 0.95f, "먹구름이면 셰이더도 어두워진다");
        }

        [Test]
        public void 글자_영역은_화면_비율로_셰이더에_넘기고_남는_칸은_비운다()
        {
            CloudLayer clouds = MakeClouds(density: 1, stormExtra: 0);
            Material runtime = clouds.transform.Find("Cloud_0").GetComponent<RawImage>().material;

            clouds.ApplyClearRects(new[] { new Rect(120f, 430f, 240f, 70f) }, new Vector2(480f, 860f));
            Vector4 first = runtime.GetVector("_ClearRect0");
            Assert.AreEqual(0.25f, first.x, 0.0001f);
            Assert.AreEqual(0.5f, first.y, 0.0001f);
            Assert.AreEqual(0.75f, first.z, 0.0001f);
            Assert.AreEqual(500f / 860f, first.w, 0.0001f);
            Assert.AreEqual(CloudLayer.NoClearRect, runtime.GetVector("_ClearRect1"), "글자가 하나면 둘째 칸은 비운다");

            clouds.ApplyClearRects(new Rect[0], new Vector2(480f, 860f));
            Assert.AreEqual(CloudLayer.NoClearRect, runtime.GetVector("_ClearRect0"));
            Assert.AreEqual(Get<float>(clouds, "behindTextOpacity"), runtime.GetFloat("_ClearOpacity"), 0.0001f);
        }

        // ---- 풍향계 ----

        [Test]
        public void 풍향계는_바람이_불어오는_쪽을_가리키고_천천히_돌아선다()
        {
            WeatherVane vane = MakeVane(out Transform arrow, out _);

            SetWind(Vector3.right, 3f);           // 서쪽(-X)에서 불어와 +X로 간다
            vane.Tick(0.02f, 0f);
            Assert.Less(Vector3.Angle(arrow.forward, Vector3.left), 0.5f, "화살촉은 바람이 오는 쪽");

            SetWind(Vector3.forward, 3f);         // 이제 남쪽(-Z)에서 불어온다
            vane.Tick(0.1f, 0f);
            Assert.Greater(Vector3.Angle(arrow.forward, Vector3.left), 0.1f, "돌기 시작한다");
            Assert.Greater(Vector3.Angle(arrow.forward, Vector3.back), 45f, "단번에 돌지는 않는다");

            for (int i = 0; i < 100; i++) vane.Tick(0.1f, 0f);
            Assert.Less(Vector3.Angle(arrow.forward, Vector3.back), 1f);
        }

        [Test]
        public void 풍속계_컵은_바람이_셀수록_빨리_돈다()
        {
            WeatherVane vane = MakeVane(out _, out Transform cups);

            SetWind(Vector3.right, 0f);
            vane.Tick(0.1f, 0f);
            float calm = vane.SpinSpeed;
            Assert.Greater(calm, 0f, "바람이 없어도 살짝 돈다");

            SetWind(Vector3.right, WeatherController.MaxWindStrength);
            Quaternion before = cups.localRotation;
            vane.Tick(0.1f, 0f);
            Assert.Greater(vane.SpinSpeed, calm * 10f);
            Assert.Greater(Quaternion.Angle(before, cups.localRotation), 1f);
        }

        // ---- 바람결·잎 ----

        [Test]
        public void 바람이_세질수록_바람결이_늘고_문턱을_넘으면_잎이_날린다()
        {
            WindEffect wind = MakeWind(out ParticleSystem streaks, out ParticleSystem leaves);
            Vector3 screenRight = SceneMetrics.CameraRotation * Vector3.right;

            SetWind(screenRight, 0.5f);
            wind.Tick();
            Assert.AreEqual(0f, wind.StreaksPerSecond, "산들바람은 보이지 않는다");
            Assert.AreEqual(0f, wind.LeavesPerSecond);

            SetWind(screenRight, 2f);
            wind.Tick();
            Assert.Greater(wind.StreaksPerSecond, 0f);
            Assert.AreEqual(0f, wind.LeavesPerSecond, "잎은 센 바람에서만 날린다");

            SetWind(screenRight, WeatherController.MaxWindStrength);
            wind.Tick();
            Assert.AreEqual(Get<float>(wind, "maxStreaksPerSecond"), wind.StreaksPerSecond, 0.01f);
            Assert.AreEqual(Get<float>(wind, "maxLeavesPerSecond"), wind.LeavesPerSecond, 0.01f);
            Assert.AreEqual(wind.StreaksPerSecond, streaks.emission.rateOverTime.constant, 0.01f);
            Assert.AreEqual(wind.LeavesPerSecond, leaves.emission.rateOverTime.constant, 0.01f);
            Assert.Greater(leaves.velocityOverLifetime.x.constant, 0f, "화면 오른쪽으로 날아간다");
            Assert.Less(leaves.shape.position.x, 0f, "잎은 바람이 오는 왼쪽 가장자리에서 들어온다");

            SetWind(screenRight, 1f);
            wind.Tick();
            Assert.AreEqual(0f, leaves.emission.rateOverTime.constant, 0.01f, "잦아들면 새 잎은 없다");
            Assert.Greater(leaves.velocityOverLifetime.x.constant, 0f, "떠 있던 잎은 허공에 멈추지 않고 마저 날아간다");

            SetWind(-screenRight, WeatherController.MaxWindStrength);
            wind.Tick();
            Assert.AreEqual(-1f, wind.ScreenDirection);
            Assert.Less(leaves.velocityOverLifetime.x.constant, 0f);
            Assert.Greater(leaves.shape.position.x, 0f);
            Assert.Less(streaks.velocityOverLifetime.x.constant, 0f);
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

            float maxRate = Get<float>(effect, "maxDropsPerSecond");
            Assert.AreEqual(_weather.RainIntensity * maxRate, effect.DropsPerSecond, 0.01f);
            Assert.Greater(effect.Slant, 0f, "화면 오른쪽 바람이면 오른쪽으로 기운다");
            Assert.AreEqual(effect.DropsPerSecond, rain.emission.rateOverTime.constant, 0.01f);
        }

        [Test]
        public void 비가_오면_화단_위에_빗방울이_튄다()
        {
            var rainGo = Own(new GameObject("Rain"));
            rainGo.SetActive(false);
            var rain = rainGo.AddComponent<ParticleSystem>();
            var effect = rainGo.AddComponent<RainEffect>();
            ParticleSystem splash = MakeParticles("Splash");
            Transform bed = Own(new GameObject("Bed")).transform;
            Set(effect, "weather", _weather);
            Set(effect, "rain", rain);
            Set(effect, "splash", splash);
            Set(effect, "splashArea", bed);
            rainGo.SetActive(true);

            effect.Splash(1f);
            Assert.AreEqual(0, effect.SplashCount, "비가 없으면 튀지 않는다");

            Randoms(0.1f, 1f, 1f);
            _sm.ChangeState(PomodoroState.Focus);
            Settle();
            effect.Splash(1f);

            float perSecond = _weather.RainIntensity * Get<float>(effect, "maxSplashesPerSecond");
            Assert.AreEqual(Mathf.Floor(perSecond), effect.SplashCount, 1f);
            Assert.AreEqual(effect.SplashCount * Get<int>(effect, "dropsPerSplash"), splash.particleCount);
        }

        // ---- 젖음 ----

        [Test]
        public void 비가_오면_젖고_그치면_천천히_마른다()
        {
            Assert.AreEqual(0f, _weather.Wetness);

            Randoms(0.1f, 1f, 1f);                // 강한 비, 가장 긴 지속
            _sm.ChangeState(PomodoroState.Focus);
            Settle();                             // 20초
            Assert.Greater(_weather.Wetness, 0.3f);
            Assert.Less(_weather.Wetness, 1f, "금세 흠뻑 젖지는 않는다");

            Settle();
            Settle();
            Assert.AreEqual(1f, _weather.Wetness, 0.001f);

            _now += 10000f;                       // 비가 그친다
            Settle();
            Assert.Less(_weather.RainIntensity, 0.01f);
            Assert.Greater(_weather.Wetness, 0.7f, "그쳐도 한동안 젖어 있다");

            for (int i = 0; i < 10; i++) Settle();
            Assert.AreEqual(0f, _weather.Wetness, 0.001f);
        }

        [Test]
        public void 젖은_흙은_짙어지고_반들거리며_마르면_원래대로_돌아간다()
        {
            var dryColor = new Color(0.5f, 0.4f, 0.3f, 1f);
            var material = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")));
            material.SetColor("_BaseColor", dryColor);
            material.SetFloat("_Smoothness", 0.15f);
            var soil = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var renderer = soil.GetComponent<Renderer>();
            renderer.sharedMaterial = material;

            var go = Own(new GameObject("Wetness"));
            go.SetActive(false);
            var wetness = go.AddComponent<SurfaceWetness>();
            Set(wetness, "weather", _weather);
            Set(wetness, "surfaces", new[] { renderer });
            go.SetActive(true);

            wetness.Apply(1f);
            Assert.IsTrue(renderer.HasPropertyBlock());
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.Less(block.GetColor("_BaseColor").grayscale, dryColor.grayscale, "젖으면 짙어진다");
            Assert.Greater(block.GetFloat("_Smoothness"), 0.6f, "젖으면 반들거린다");
            // 재질 색은 색 공간을 오가며 저장돼 비트 단위로는 같지 않을 수 있다.
            Assert.Less(Vector4.Distance(dryColor, material.GetColor("_BaseColor")), 0.0001f, "공유 재질은 그대로다");

            wetness.Apply(0f);
            Assert.IsFalse(renderer.HasPropertyBlock(), "마르면 원래 재질 그대로 그린다");
        }

        // ---- 도우미 ----

        private CloudLayer MakeClouds(int density, int stormExtra, SunOrbitController sun = null)
        {
            var canvasGo = Own(new GameObject("Canvas", typeof(Canvas)));
            var areaGo = new GameObject("Clouds", typeof(RectTransform));
            areaGo.transform.SetParent(canvasGo.transform, false);
            areaGo.SetActive(false);
            var area = (RectTransform)areaGo.transform;
            area.sizeDelta = new Vector2(SceneMetrics.WindowWidth, SceneMetrics.SkyLayerHeight);

            _cloudMaterial = Own(new Material(Shader.Find("SAIUN/UI/Cumulus")));
            var template = new GameObject("CloudTemplate", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            template.transform.SetParent(area, false);
            template.material = _cloudMaterial;

            var clouds = areaGo.AddComponent<CloudLayer>();
            Set(clouds, "weather", _weather);
            Set(clouds, "sun", sun);
            Set(clouds, "area", area);
            Set(clouds, "cloudTemplate", template);
            Set(clouds, "density", density);
            Set(clouds, "stormExtra", stormExtra);
            areaGo.SetActive(true);
            return clouds;
        }

        private WeatherVane MakeVane(out Transform arrow, out Transform cups)
        {
            var go = Own(new GameObject("WeatherVane"));
            go.SetActive(false);
            arrow = new GameObject("Vane").transform;
            arrow.SetParent(go.transform, false);
            cups = new GameObject("Cups").transform;
            cups.SetParent(go.transform, false);
            var vane = go.AddComponent<WeatherVane>();
            Set(vane, "weather", _weather);
            Set(vane, "vane", arrow);
            Set(vane, "cups", cups);
            go.SetActive(true);
            return vane;
        }

        private WindEffect MakeWind(out ParticleSystem streaks, out ParticleSystem leaves)
        {
            var go = Own(new GameObject("Wind"));
            go.SetActive(false);
            streaks = MakeParticles("Streaks");
            leaves = MakeParticles("Leaves");
            var wind = go.AddComponent<WindEffect>();
            Set(wind, "weather", _weather);
            Set(wind, "streaks", streaks);
            Set(wind, "leaves", leaves);
            go.SetActive(true);
            return wind;
        }

        // 기본 파티클은 스스로 뿜으므로 끈다. 수는 Emit이나 효과 컴포넌트가 정한다.
        private ParticleSystem MakeParticles(string name)
        {
            var system = Own(new GameObject(name)).AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            return system;
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

        private static T Get<T>(object target, string field)
        {
            return (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
        }

        private T Own<T>(T obj) where T : Object
        {
            _owned.Add(obj);
            return obj;
        }
    }
}
