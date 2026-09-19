using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using _SAIUN.Scripts.Character;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Weather;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P3-05: 바람은 머리카락·옷자락(SpringBone)에 외력으로 들어가고, 비에 젖으면 옷과 머리카락이 짙어지며 물기가 돈다.
    /// 마르면 원래 색으로 돌아간다. 실제 VRM이 필요한 테스트는 로컬 모델이 있을 때만 돈다.
    /// </summary>
    public class CharacterWeatherTests
    {
        private const string LocalSamplePath = "Assets/_SAIUN/Art/VRM/HatsuneMikuNT.vrm";
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");

        private readonly List<Object> _owned = new List<Object>();
        private readonly Queue<float> _randoms = new Queue<float>();
        private WeatherController _weather;
        private VrmLoader _loader;
        private CharacterWeather _characterWeather;

        [SetUp]
        public void SetUp()
        {
            SettingsStore.DeleteAll();
            var systems = Own(new GameObject("Systems"));
            var stateMachine = systems.AddComponent<PomodoroStateMachine>();

            var weatherGo = Own(new GameObject("Weather"));
            weatherGo.SetActive(false);
            _weather = weatherGo.AddComponent<WeatherController>();
            Set(_weather, "stateMachine", stateMachine);
            weatherGo.SetActive(true);

            var characterGo = Own(new GameObject("Character"));
            characterGo.SetActive(false);
            _loader = characterGo.AddComponent<VrmLoader>();
            _characterWeather = characterGo.AddComponent<CharacterWeather>();
            Set(_characterWeather, "loader", _loader);
            Set(_characterWeather, "weather", _weather);
            characterGo.SetActive(true);
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

        [Test]
        public void 바람은_방향과_세기만큼_외력이_되고_돌풍처럼_출렁인다()
        {
            SetWind(Vector3.right, WeatherController.MaxWindStrength);
            float perWind = Get<float>(_characterWeather, "forcePerWind");
            float frequency = Get<float>(_characterWeather, "gustFrequency");

            _characterWeather.ApplyWind(0f);   // 돌풍 주기의 시작: 출렁임 0
            Assert.AreEqual(WeatherController.MaxWindStrength * perWind, _characterWeather.WindForce.magnitude, 0.0001f);
            Assert.Less(Vector3.Angle(Vector3.right, _characterWeather.WindForce), 0.1f, "바람이 가는 쪽으로 민다");

            _characterWeather.ApplyWind(0.25f / frequency);   // 4분의 1 주기: 가장 센 돌풍
            Assert.Greater(_characterWeather.WindForce.magnitude, WeatherController.MaxWindStrength * perWind);

            SetWind(Vector3.right, 0f);
            _characterWeather.ApplyWind(0.25f / frequency);
            Assert.AreEqual(Vector3.zero, _characterWeather.WindForce, "바람이 없으면 외력도 없다");
        }

        [UnityTest]
        public IEnumerator 비에_젖으면_옷과_머리카락이_짙어지고_마르면_원래_색으로_돌아간다()
        {
            string path = Path.GetFullPath(LocalSamplePath);
            if (!File.Exists(path)) Assert.Ignore("로컬 샘플 VRM이 없어 건너뛴다.");

            Task<bool> task = _loader.LoadAsync(path, persist: false);
            yield return new WaitUntil(() => task.IsCompleted);
            Assert.IsTrue(task.Result);
            Assert.Greater(_characterWeather.WettableMaterialCount, 0, "MToon 머티리얼을 찾아야 한다");

            Material material = FindBrightMToon(_loader.CurrentModel);
            Color dryColor = material.GetColor(ColorId);
            Color dryShade = material.GetColor(ShadeColorId);
            Color dryRim = material.GetColor(RimColorId);

            _characterWeather.ApplyWetness(1f);
            Assert.Less(material.GetColor(ColorId).grayscale, dryColor.grayscale, "젖으면 기본색이 짙어진다");
            Assert.Less(material.GetColor(ShadeColorId).grayscale, dryShade.grayscale + 0.0001f);
            Assert.Greater(material.GetColor(RimColorId).grayscale, dryRim.grayscale, "가장자리에 물기 광택이 돈다");
            Assert.AreEqual(dryColor.a, material.GetColor(ColorId).a, 0.0001f, "투명도는 그대로다");

            _characterWeather.ApplyWetness(0f);
            Assert.AreEqual(dryColor, material.GetColor(ColorId));
            Assert.AreEqual(dryShade, material.GetColor(ShadeColorId));
            Assert.AreEqual(dryRim, material.GetColor(RimColorId));
        }

        // 어두워지는 것을 확인할 수 있게 밝은 MToon 머티리얼을 고른다.
        private static Material FindBrightMToon(GameObject model)
        {
            Material best = null;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty(RimColorId)) continue;
                    if (best == null || material.GetColor(ColorId).grayscale > best.GetColor(ColorId).grayscale) best = material;
                }
            }
            Assert.IsNotNull(best);
            return best;
        }

        // 바람을 곧바로 원하는 값으로 둔다.
        private void SetWind(Vector3 direction, float strength)
        {
            float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            _randoms.Enqueue(strength / WeatherController.MaxWindStrength);
            _randoms.Enqueue(Mathf.Repeat(angle, 360f) / 360f);
            _randoms.Enqueue(1f);
            _weather.SetSources(() => 0f, () => _randoms.Count > 0 ? _randoms.Dequeue() : 0.5f);
        }

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
