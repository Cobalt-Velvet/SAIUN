using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using _SAIUN.Scripts.Timer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P2-03: 세션 진행에 따라 씨앗 → 발아 → 성장 → 결실 → 수확 가능으로 바뀌고,
    /// FAILED에서 시들어 사라진다. 화단의 모든 칸에 한 포기씩 심는다.
    /// </summary>
    public class CropGrowthTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private Flowerbed _bed;
        private CropGrowth _growth;
        private readonly List<CropStage> _events = new List<CropStage>();
        private double _now;

        [SetUp]
        public void SetUp()
        {
            var systems = Own(new GameObject("Systems"));
            _sm = systems.AddComponent<PomodoroStateMachine>();
            _timer = systems.AddComponent<PomodoroTimer>();
            _now = 100d;
            _timer.SetClock(() => _now);

            var catalog = Own(ScriptableObject.CreateInstance<CropCatalog>());
            catalog.Configure(new[] { Definition("rice"), Definition("wheat") });

            // Awake 전에 참조를 넣으려고 비활성 상태에서 붙인다.
            var bedGo = Own(new GameObject("Flowerbed"));
            bedGo.SetActive(false);
            _bed = bedGo.AddComponent<Flowerbed>();
            _growth = bedGo.AddComponent<CropGrowth>();
            Set("stateMachine", _sm);
            Set("timer", _timer);
            Set("flowerbed", _bed);
            Set("catalog", catalog);
            Set("growPopSeconds", 0f);
            Set("witherSeconds", 0f);
            Set("clearSeconds", 0f);
            bedGo.SetActive(true);

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

        // ---- 세션 연동 ----

        [Test]
        public void 세션을_시작하면_모든_칸에_씨앗을_심는다()
        {
            _timer.StartSession(Config(sets: 4, crop: "wheat"));

            Assert.AreEqual(CropStage.Seed, _growth.CurrentStage);
            Assert.AreEqual(12, _growth.PlantCount, "세트 수와 무관하게 칸마다 한 포기");
            Assert.AreEqual("wheat", _growth.CurrentCrop.Id);
            StringAssert.StartsWith("wheat_Seed", _growth.GetPlantModel(0).name);

            // 포기는 칸 중심에 있다. 0행 0열부터 행 우선 순서다.
            Assert.Less(Vector3.Distance(_bed.CellPosition(0, 0), _growth.GetPlantRoot(0).position), 0.0001f);
            Assert.Less(Vector3.Distance(_bed.CellPosition(5, 1), _growth.GetPlantRoot(11).position), 0.0001f);
        }

        [Test]
        public void 목록에_없는_작물은_첫_작물로_심고_경고한다()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("작물 'mango'"));
            _timer.StartSession(Config(sets: 2, crop: "mango"));

            Assert.AreEqual("rice", _growth.CurrentCrop.Id);
        }

        [Test]
        public void 세션_진행에_따라_다섯_단계를_한_번씩_거친다()
        {
            _timer.StartSession(Config(sets: 4, crop: "rice"));

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
            _timer.StartSession(Config(sets: 2, crop: "rice"));
            Advance(600);   // 1세트 완료 = 50% → 성장

            yield return null;   // Destroy는 프레임 끝에 반영된다

            Transform root = _growth.GetPlantRoot(0);
            Assert.AreEqual(1, root.childCount, "이전 단계 모델이 남아 있다");
            StringAssert.StartsWith("rice_Growing", root.GetChild(0).name);
        }

        [Test]
        public void 장기_휴식이_끝나면_거두고_비운다()
        {
            _timer.StartSession(Config(sets: 1, crop: "rice"));
            Advance(600);
            Assert.AreEqual(CropStage.Harvestable, _growth.CurrentStage);

            Advance(300);   // 장기 휴식 종료 → Idle
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.AreEqual(CropStage.None, _growth.CurrentStage);
            Assert.AreEqual(0, _growth.PlantCount);
            Assert.AreEqual(CropStage.None, _events.Last());
        }

        [Test]
        public void 실패하면_시들고_확인하면_비워진다()
        {
            _timer.StartSession(Config(sets: 3, crop: "rice"));
            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);

            Assert.AreEqual(CropStage.Dead, _growth.CurrentStage);
            Assert.AreEqual(0, _growth.PlantCount, "시드는 시간이 0이면 바로 사라진다");

            _sm.ChangeState(PomodoroState.Idle);
            Assert.AreEqual(CropStage.None, _growth.CurrentStage);
            CollectionAssert.AreEqual(new[] { CropStage.Seed, CropStage.Dead, CropStage.None }, _events);
        }

        [UnityTest]
        public IEnumerator 시드는_동안_포기가_쓰러지고_끝나면_사라진다()
        {
            Set("witherSeconds", 0.3f);
            _timer.StartSession(Config(sets: 2, crop: "rice"));
            Transform root = _growth.GetPlantRoot(0);
            Quaternion upright = root.rotation;

            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);
            yield return new WaitForSeconds(0.12f);

            Assert.AreEqual(12, _growth.PlantCount, "시드는 중에는 남아 있다");
            Assert.Greater(Quaternion.Angle(upright, root.rotation), 5f, "쓰러지고 있어야 한다");

            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(0, _growth.PlantCount);
            Assert.IsTrue(root == null, "포기 오브젝트가 파괴돼야 한다");
        }

        [Test]
        public void 취소하면_시들지_않고_비운다()
        {
            _timer.StartSession(Config(sets: 2, crop: "rice"));
            _timer.Cancel();

            Assert.AreEqual(CropStage.None, _growth.CurrentStage);
            Assert.AreEqual(0, _growth.PlantCount);
            CollectionAssert.DoesNotContain(_events, CropStage.Dead);
        }

        [Test]
        public void 다시_시작하면_새로_심는다()
        {
            _timer.StartSession(Config(sets: 2, crop: "rice"));
            _timer.Cancel();
            _timer.StartSession(Config(sets: 5, crop: "wheat"));

            Assert.AreEqual(12, _growth.PlantCount);
            Assert.AreEqual("wheat", _growth.CurrentCrop.Id);
            Assert.AreEqual(CropStage.Seed, _growth.CurrentStage);
        }

        [Test]
        public void 유예_중에는_더_크게_흔들린다()
        {
            Set("swayResponse", 1000f);
            _timer.StartSession(Config(sets: 1, crop: "rice"));

            float calm = MaxSwayAngle();
            _sm.ChangeState(PomodoroState.Interrupted);
            float alarmed = MaxSwayAngle();

            Assert.Greater(calm, 0.5f, "평소에도 조금은 흔들린다");
            Assert.Greater(alarmed, calm * 2f);
        }

        // ---- 도우미 ----

        private float MaxSwayAngle()
        {
            MethodInfo sway = typeof(CropGrowth).GetMethod("Sway", BindingFlags.NonPublic | BindingFlags.Instance);
            Transform root = _growth.GetPlantRoot(0);
            float max = 0f;
            for (int i = 0; i < 400; i++)
            {
                sway.Invoke(_growth, new object[] { 0.01f });
                max = Mathf.Max(max, Vector3.Angle(Vector3.up, root.rotation * Vector3.up));
            }
            return max;
        }

        private void Advance(double seconds)
        {
            _now += seconds;
            _timer.Tick();
        }

        private static SessionConfig Config(int sets, string crop)
        {
            return new SessionConfig
            {
                FocusMinutes = 10, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = sets, CropType = crop,
            };
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
