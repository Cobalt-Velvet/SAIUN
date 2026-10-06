using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using _SAIUN.Scripts.Timer;
using _SAIUN.Scripts.Weather;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace _SAIUN.Tests
{
    /// <summary>
    /// 쉬는 동안에는 상자 칸마다 텃밭 작물이 실제 시각을 따라 저절로 자라고(한 시간 주기, 칸마다 어긋남),
    /// 집중하면 상자를 토분으로 바꿔 올리브 한 그루를 심는다. 올리브는 세션 진행에 따라
    /// 씨앗 → 발아 → 성장 → 결실 → 수확 가능으로 자라고, FAILED에서 시들어 사라진다. 세션이 끝나면 상자로 돌아간다.
    /// </summary>
    public class CropGrowthTests
    {
        private static readonly DateTime Noon = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

        private readonly List<Object> _owned = new List<Object>();
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private Flowerbed _bed;
        private CropGrowth _growth;
        private CropCatalog _catalog;
        private readonly List<CropStage> _events = new List<CropStage>();
        private double _now;
        private DateTime _clock;

        [SetUp]
        public void SetUp()
        {
            var systems = Own(new GameObject("Systems"));
            _sm = systems.AddComponent<PomodoroStateMachine>();
            _timer = systems.AddComponent<PomodoroTimer>();
            _now = 100d;
            _timer.SetClock(() => _now);

            _catalog = Own(ScriptableObject.CreateInstance<CropCatalog>());
            _catalog.Configure(new[] { Definition("rice"), Definition("wheat"), Definition("tomato"), Definition("potato") },
                Definition("olive"));

            // Awake 전에 참조를 넣으려고 비활성 상태에서 붙인다.
            var bedGo = Own(new GameObject("Flowerbed"));
            bedGo.SetActive(false);
            _bed = bedGo.AddComponent<Flowerbed>();
            _growth = bedGo.AddComponent<CropGrowth>();
            Set("stateMachine", _sm);
            Set("timer", _timer);
            Set("flowerbed", _bed);
            Set("catalog", _catalog);
            Set("growPopSeconds", 0f);
            Set("witherSeconds", 0f);
            Set("clearSeconds", 0f);
            Set("swapSeconds", 0f);
            _clock = Noon;
            _growth.Clock = () => _clock;
            bedGo.SetActive(true);
            Invoke("Start");   // 동기 테스트에서는 Start가 불리지 않는다

            _events.Clear();
            _growth.OnStageChanged += _events.Add;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _owned)
            {
                if (owned != null) Object.DestroyImmediate(owned);
            }
            _owned.Clear();
        }

        // ---- 판정 ----

        [Test]
        public void 성장_임계는_1세트_50퍼센트_80퍼센트_100퍼센트다()
        {
            Assert.AreEqual(CropStage.Seed, CropGrowth.StageFor(0, 0.99f, 4));
            Assert.AreEqual(CropStage.Sprout, CropGrowth.StageFor(1, 0f, 4));
            Assert.AreEqual(CropStage.Sprout, CropGrowth.StageFor(1, 0.9f, 4), "47.5%");
            Assert.AreEqual(CropStage.Growing, CropGrowth.StageFor(2, 0f, 4), "50%");
            Assert.AreEqual(CropStage.Growing, CropGrowth.StageFor(3, 0.1f, 4), "77.5%");
            Assert.AreEqual(CropStage.Fruiting, CropGrowth.StageFor(3, 0.25f, 4), "81.25%");
            Assert.AreEqual(CropStage.Harvestable, CropGrowth.StageFor(4, 0f, 4));
        }

        [Test]
        public void 한_세트_세션도_집중_진행률로_결실까지_간다()
        {
            Assert.AreEqual(CropStage.Seed, CropGrowth.StageFor(0, 0.4f, 1));
            Assert.AreEqual(CropStage.Growing, CropGrowth.StageFor(0, 0.6f, 1));
            Assert.AreEqual(CropStage.Fruiting, CropGrowth.StageFor(0, 0.9f, 1));
            Assert.AreEqual(CropStage.Harvestable, CropGrowth.StageFor(1, 0f, 1));
        }

        [Test]
        public void 텃밭_주기는_씨앗에서_익을_때까지_차례로_간다()
        {
            Assert.AreEqual(CropStage.Seed, CropGrowth.GardenStageFor(0f));
            Assert.AreEqual(CropStage.Sprout, CropGrowth.GardenStageFor(CropGrowth.GardenSproutAt));
            Assert.AreEqual(CropStage.Growing, CropGrowth.GardenStageFor(CropGrowth.GardenGrowingAt));
            Assert.AreEqual(CropStage.Fruiting, CropGrowth.GardenStageFor(CropGrowth.GardenFruitingAt));
            Assert.AreEqual(CropStage.Harvestable, CropGrowth.GardenStageFor(0.99f));
        }

        // ---- 텃밭 (쉬는 동안) ----

        [Test]
        public void 쉬는_동안_상자_칸마다_텃밭_작물이_서고_단계가_여럿_섞여_있다()
        {
            Assert.IsFalse(_growth.PotShown);
            Assert.AreEqual(12, _growth.GardenCount);

            var stages = new HashSet<CropStage>();
            for (int i = 0; i < _growth.GardenCount; i++)
            {
                Assert.IsNotNull(_growth.GetGardenCrop(i));
                CollectionAssert.Contains(_catalog.Crops, _growth.GetGardenCrop(i), "텃밭에는 올리브를 심지 않는다");
                StringAssert.StartsWith($"{_growth.GetGardenCrop(i).Id}_{_growth.GetGardenStage(i)}", _growth.GetGardenModel(i).name);
                stages.Add(_growth.GetGardenStage(i));
            }
            Assert.GreaterOrEqual(stages.Count, 4, "칸마다 주기가 어긋나 늘 여러 단계가 보인다");
        }

        [Test]
        public void 같은_시각이면_다시_켜도_같은_텃밭이다()
        {
            List<(string, CropStage)> before = Garden();
            Invoke("PlantGarden");
            CollectionAssert.AreEqual(before, Garden());
        }

        [Test]
        public void 한_시간이_지나면_칸마다_한_주기를_돌아_같은_단계가_된다()
        {
            List<(string, CropStage)> before = Garden();
            _clock = Noon.AddMinutes(60);
            UpdateGarden();
            CollectionAssert.AreEqual(before.Select(p => p.Item2), Garden().Select(p => p.Item2));
        }

        [Test]
        public void 시간이_흐르면_자라고_다_익은_칸은_거둬_새로_심는다()
        {
            int ripe = Enumerable.Range(0, _growth.GardenCount).First(i => _growth.GetGardenStage(i) == CropStage.Harvestable);
            int seed = Enumerable.Range(0, _growth.GardenCount).First(i => _growth.GetGardenStage(i) == CropStage.Seed);

            // 한 주기의 1/4쯤 흐르면 익은 칸은 거둬 새로 심겨 있고, 씨앗 칸은 자라 있다.
            _clock = Noon.AddMinutes(15);
            UpdateGarden();
            Assert.That(_growth.GetGardenStage(ripe), Is.LessThanOrEqualTo(CropStage.Sprout), "거두고 새로 심었다");
            Assert.That(_growth.GetGardenStage(seed), Is.GreaterThan(CropStage.Seed));
        }

        // ---- 집중 작물 ----

        [Test]
        public void 집중을_시작하면_상자가_토분으로_바뀌고_올리브를_심는다()
        {
            _timer.StartSession(Config(sets: 4));

            Assert.IsTrue(_growth.PotShown);
            Assert.AreEqual("olive", _growth.CurrentCrop.Id);
            Assert.AreEqual(CropStage.Seed, _growth.CurrentStage);
            StringAssert.StartsWith("olive_Seed", _growth.FocusModel.name);
            Assert.Less(Vector3.Distance(_bed.PotPlantPosition, _growth.FocusRoot.position), 0.0001f, "토분 흙 가운데에 선다");
        }

        [Test]
        public void 세션_진행에_따라_다섯_단계를_한_번씩_거친다()
        {
            _timer.StartSession(Config(sets: 4));

            Advance(600);   // 1세트 완료 → 단기 휴식
            Assert.AreEqual(PomodoroState.ShortBreak, _sm.CurrentState);
            Assert.AreEqual(CropStage.Sprout, _growth.CurrentStage);

            Advance(60);    // 2세트 집중
            Advance(600);   // 2세트 완료 = 50%
            Assert.AreEqual(CropStage.Growing, _growth.CurrentStage);

            Advance(60);
            Advance(600);   // 3세트 완료 = 75%
            Assert.AreEqual(CropStage.Growing, _growth.CurrentStage);

            Advance(60);    // 4세트 집중 시작
            Advance(150);   // 4세트 25% → 전체 81.25%
            Assert.AreEqual(CropStage.Fruiting, _growth.CurrentStage);

            Advance(450);   // 4세트 완료 → 장기 휴식
            Assert.AreEqual(PomodoroState.LongBreak, _sm.CurrentState);
            Assert.AreEqual(CropStage.Harvestable, _growth.CurrentStage);

            CollectionAssert.AreEqual(
                new[] { CropStage.Seed, CropStage.Sprout, CropStage.Growing, CropStage.Fruiting, CropStage.Harvestable },
                _events);
        }

        [UnityTest]
        public IEnumerator 단계가_바뀌면_모델이_새_단계로_교체된다()
        {
            _timer.StartSession(Config(sets: 2));
            Advance(600);   // 1세트 완료 = 50% → 성장

            yield return null;   // Destroy는 프레임 끝에 반영된다

            Transform root = _growth.FocusRoot;
            Assert.AreEqual(1, root.childCount, "이전 단계 모델이 남아 있다");
            StringAssert.StartsWith("olive_Growing", root.GetChild(0).name);
        }

        [Test]
        public void 장기_휴식이_끝나면_거두고_상자로_돌아간다()
        {
            _timer.StartSession(Config(sets: 1));
            Advance(600);
            Assert.AreEqual(CropStage.Harvestable, _growth.CurrentStage);

            Advance(300);   // 장기 휴식 종료 → Idle
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.AreEqual(CropStage.None, _growth.CurrentStage);
            Assert.IsNull(_growth.FocusRoot);
            Assert.IsFalse(_growth.PotShown);
            Assert.AreEqual(CropStage.None, _events.Last());
        }

        [Test]
        public void 실패하면_시들고_확인하면_상자로_돌아간다()
        {
            _timer.StartSession(Config(sets: 3));
            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);

            Assert.AreEqual(CropStage.Dead, _growth.CurrentStage);
            Assert.IsNull(_growth.FocusRoot, "시드는 시간이 0이면 바로 사라진다");
            Assert.IsTrue(_growth.PotShown, "확인 전까지는 빈 토분이 남아 있다");

            _sm.ChangeState(PomodoroState.Idle);
            Assert.AreEqual(CropStage.None, _growth.CurrentStage);
            Assert.IsFalse(_growth.PotShown);
            CollectionAssert.AreEqual(new[] { CropStage.Seed, CropStage.Dead, CropStage.None }, _events);
        }

        [UnityTest]
        public IEnumerator 시드는_동안_나무가_쓰러지고_끝나면_사라진다()
        {
            Set("witherSeconds", 0.3f);
            _timer.StartSession(Config(sets: 2));
            Transform root = _growth.FocusRoot;
            Quaternion upright = root.rotation;

            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);
            yield return new WaitForSeconds(0.12f);

            Assert.IsNotNull(_growth.FocusRoot, "시드는 중에는 남아 있다");
            Assert.Greater(Quaternion.Angle(upright, root.rotation), 5f, "쓰러지고 있어야 한다");

            yield return new WaitForSeconds(0.4f);
            Assert.IsNull(_growth.FocusRoot);
            Assert.IsTrue(root == null, "나무 오브젝트가 파괴돼야 한다");
        }

        [UnityTest]
        public IEnumerator 상자와_토분은_가라앉고_솟으며_바뀐다()
        {
            Set("swapSeconds", 0.2f);
            var planter = new GameObject("Planter").transform;
            var pot = new GameObject("Pot").transform;
            Own(planter.gameObject);
            Own(pot.gameObject);
            typeof(Flowerbed).GetField("planter", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_bed, planter);
            typeof(Flowerbed).GetField("pot", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_bed, pot);

            _timer.StartSession(Config(sets: 2));
            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(planter.gameObject.activeSelf, "상자가 가라앉는 중");
            Assert.Less(planter.localScale.y, 1f);

            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(planter.gameObject.activeSelf, "상자는 치워졌다");
            Assert.IsTrue(pot.gameObject.activeSelf);
            Assert.AreEqual(1f, pot.localScale.y, 0.0001f, "토분이 다 솟았다");
        }

        [Test]
        public void 취소하면_시들지_않고_상자로_돌아간다()
        {
            _timer.StartSession(Config(sets: 2));
            _timer.Cancel();

            Assert.AreEqual(CropStage.None, _growth.CurrentStage);
            Assert.IsNull(_growth.FocusRoot);
            Assert.IsFalse(_growth.PotShown);
            CollectionAssert.DoesNotContain(_events, CropStage.Dead);
        }

        [Test]
        public void 다시_시작하면_새로_심는다()
        {
            _timer.StartSession(Config(sets: 2));
            _timer.Cancel();
            _timer.StartSession(Config(sets: 5));

            Assert.IsNotNull(_growth.FocusRoot);
            Assert.IsTrue(_growth.PotShown);
            Assert.AreEqual(CropStage.Seed, _growth.CurrentStage);
        }

        [Test]
        public void 유예_중에는_더_크게_흔들린다()
        {
            Set("swayResponse", 1000f);
            _timer.StartSession(Config(sets: 1));

            float calm = MaxSwayAngle();
            _sm.ChangeState(PomodoroState.Interrupted);
            float alarmed = MaxSwayAngle();

            Assert.Greater(calm, 0.5f, "평소에도 조금은 흔들린다");
            Assert.Greater(alarmed, calm * 2f);
        }

        [Test]
        public void 바람이_불면_바람_쪽으로_눕는다()
        {
            var weatherGo = Own(new GameObject("Weather"));
            weatherGo.SetActive(false);
            var weather = weatherGo.AddComponent<WeatherController>();
            typeof(WeatherController).GetField("stateMachine", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(weather, _sm);
            weatherGo.SetActive(true);
            var randoms = new Queue<float>(new[] { 1f, 0.25f, 1f });   // 세기 5, 방향 +X
            weather.SetSources(() => 0f, () => randoms.Count > 0 ? randoms.Dequeue() : 0.5f);

            Set("weather", weather);
            Set("swayDegrees", 0f);
            Set("swayResponse", 1000f);
            _timer.StartSession(Config(sets: 1));

            Invoke("Sway", 0.01f);
            Vector3 up = _growth.FocusRoot.rotation * Vector3.up;
            Assert.Greater(up.x, 0.05f, "바람이 불어 가는 +X 쪽으로 기운다");
        }

        // ---- 도우미 ----

        private List<(string, CropStage)> Garden()
        {
            return Enumerable.Range(0, _growth.GardenCount)
                .Select(i => (_growth.GetGardenCrop(i).Id, _growth.GetGardenStage(i)))
                .ToList();
        }

        private void UpdateGarden() => Invoke("UpdateGarden", false);

        private float MaxSwayAngle()
        {
            Transform root = _growth.FocusRoot;
            float max = 0f;
            for (int i = 0; i < 400; i++)
            {
                Invoke("Sway", 0.01f);
                max = Mathf.Max(max, Vector3.Angle(Vector3.up, root.rotation * Vector3.up));
            }
            return max;
        }

        private void Invoke(string method, params object[] args)
        {
            typeof(CropGrowth).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_growth, args);
        }

        private void Advance(double seconds)
        {
            _now += seconds;
            _timer.Tick();
        }

        private static SessionConfig Config(int sets)
        {
            return new SessionConfig { FocusMinutes = 10, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = sets };
        }

        private CropDefinition Definition(string id)
        {
            var prefabs = new GameObject[CropDefinition.StageModelCount];
            for (int i = 0; i < prefabs.Length; i++)
            {
                prefabs[i] = Own(new GameObject($"{id}_{(CropStage)((int)CropStage.Seed + i)}"));
            }

            var definition = Own(ScriptableObject.CreateInstance<CropDefinition>());
            definition.Configure(id, id, prefabs);
            return definition;
        }

        private void Set(string field, object value)
        {
            typeof(CropGrowth)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_growth, value);
        }

        private T Own<T>(T obj) where T : Object
        {
            _owned.Add(obj);
            return obj;
        }
    }
}
