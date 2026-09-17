using System.Collections.Generic;
using System.IO;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using _SAIUN.Scripts.Timer;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>P1-05 수용 조건: GameManager만 서로를 알고, 세션 종료 시 sessions 행 1건이 기록된다.</summary>
    public class GameManagerTests
    {
        private string _dbPath;
        private GameObject _go;
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private SaiunDatabase _db;
        private GameManager _gm;
        private double _now;
        private readonly List<Object> _owned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Application.temporaryCachePath, $"saiun_gm_{System.Guid.NewGuid():N}.db");
            SaiunDatabase.PathOverride = _dbPath;
            SettingsStore.DeleteAll();

            // 씬 배치와 같은 순서로 한 오브젝트에 컴포넌트를 붙인다.
            _go = new GameObject("GameManagerTest");
            _sm = _go.AddComponent<PomodoroStateMachine>();
            _timer = _go.AddComponent<PomodoroTimer>();
            _db = _go.AddComponent<SaiunDatabase>();
            _gm = _go.AddComponent<GameManager>();

            _now = 5000d;
            _timer.SetClock(() => _now);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _owned) Object.DestroyImmediate(owned);
            _owned.Clear();
            Object.DestroyImmediate(_go);
            SaiunDatabase.PathOverride = null;
            SettingsStore.DeleteAll();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        private static SessionConfig ShortConfig(int sets = 2)
        {
            return new SessionConfig { FocusMinutes = 5, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = sets, CropType = "wheat" };
        }

        private void Advance(double seconds)
        {
            _now += seconds;
            _timer.Tick();
        }

        [Test]
        public void 하위_시스템은_GameManager를_참조하지_않는다()
        {
            // 각 시스템 어셈블리 타입이 GameManager 타입의 필드를 갖지 않는지 리플렉션으로 확인한다.
            AssertNoFieldOfType(typeof(PomodoroStateMachine), typeof(GameManager));
            AssertNoFieldOfType(typeof(PomodoroTimer), typeof(GameManager));
            AssertNoFieldOfType(typeof(SaiunDatabase), typeof(GameManager));
            AssertNoFieldOfType(typeof(WindowController), typeof(GameManager));
            AssertNoFieldOfType(typeof(ForegroundWatcher), typeof(GameManager));

            // 상태머신은 아무도 참조하지 않는다(다른 시스템 타입의 필드가 없다).
            AssertNoFieldOfType(typeof(PomodoroStateMachine), typeof(PomodoroTimer));
            AssertNoFieldOfType(typeof(PomodoroStateMachine), typeof(SaiunDatabase));
        }

        [Test]
        public void 시작_요청은_설정을_저장하고_Focus로_보낸다()
        {
            _gm.RequestStart(ShortConfig());

            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
            Assert.AreEqual(5, SettingsStore.LoadSessionConfig().FocusMinutes);
            Assert.AreEqual("wheat", _gm.CurrentConfig.CropType);
        }

        [Test]
        public void 전_세트_완료_시_HARVESTED_세션이_1건_기록된다()
        {
            SessionRecord recorded = null;
            _gm.OnSessionRecorded += r => recorded = r;

            _gm.RequestStart(ShortConfig(sets: 2));
            Advance(300);   // Focus 1 → ShortBreak
            Advance(60);    // → Focus 2
            Advance(300);   // → LongBreak
            Assert.AreEqual(0, _db.GetSessionCount(), "장기 휴식 중에는 아직 기록하지 않는다");
            Advance(300);   // → Idle

            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.AreEqual(1, _db.GetSessionCount());
            Assert.IsNotNull(recorded);
            Assert.AreEqual(SessionRecord.ResultHarvested, recorded.Result);
            Assert.AreEqual(2, recorded.SetsCompleted);
            Assert.AreEqual(5, recorded.DurationMin);
            Assert.AreEqual("wheat", recorded.CropType);
            Assert.IsFalse(string.IsNullOrEmpty(recorded.StartTime));
        }

        [Test]
        public void 중간_취소는_완료_세트를_보존한_FAILED로_기록된다()
        {
            _gm.RequestStart(ShortConfig(sets: 3));
            Advance(300);   // 1세트 완료 → ShortBreak
            Advance(60);    // → Focus 2
            Advance(100);
            _gm.RequestCancel();

            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            List<SessionRecord> sessions = _db.GetRecentSessions(1);
            Assert.AreEqual(1, sessions.Count);
            Assert.AreEqual(SessionRecord.ResultFailed, sessions[0].Result);
            Assert.AreEqual(1, sessions[0].SetsCompleted);
        }

        [Test]
        public void 유예_초과_실패는_FAILED로_1건만_기록된다()
        {
            _gm.RequestStart(ShortConfig());
            Advance(30);
            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);

            Assert.AreEqual(1, _db.GetSessionCount());
            Assert.AreEqual(SessionRecord.ResultFailed, _db.GetRecentSessions(1)[0].Result);

            _gm.RequestAcknowledgeFailure();
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.AreEqual(1, _db.GetSessionCount(), "Failed→Idle에서 중복 기록하지 않는다");
        }

        [Test]
        public void 장기_휴식을_건너뛰어도_HARVESTED다()
        {
            _gm.RequestStart(ShortConfig(sets: 1));
            Advance(300);   // → LongBreak (1세트 구성)
            _gm.RequestCancel();

            Assert.AreEqual(SessionRecord.ResultHarvested, _db.GetRecentSessions(1)[0].Result);
            Assert.AreEqual(1, _db.GetRecentSessions(1)[0].SetsCompleted);
        }

        [Test]
        public void 일시정지와_재개_요청이_타이머에_전달된다()
        {
            _gm.RequestStart(ShortConfig());
            _gm.RequestPause();
            Assert.IsTrue(_timer.IsPaused);
            _gm.RequestResume();
            Assert.IsFalse(_timer.IsPaused);
        }

        [Test]
        public void 상태에_따라_프레임레이트가_한_번씩_바뀐다()
        {
            // 배치 모드에서는 창 포커스가 없을 수 있어 포커스 상태를 고정한다.
            typeof(GameManager)
                .GetField("_hasFocus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(_gm, true);

            // Start()가 아직 안 돌았을 수 있으므로 전이로 확인한다.
            _gm.RequestStart(ShortConfig());
            Assert.AreEqual(144, Application.targetFrameRate);

            Advance(300);   // → ShortBreak
            Assert.AreEqual(60, Application.targetFrameRate);

            _gm.RequestCancel();
            Assert.AreEqual(60, Application.targetFrameRate);
        }

        [Test]
        public void 작물_등급은_시작_시점_설정으로_1회_판정된다()
        {
            Assert.AreEqual(_SAIUN.Scripts.Crop.CropGrade.Normal, _gm.CurrentGrade);
            _gm.RequestStart(new SessionConfig { FocusMinutes = 60, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = 8 });
            Assert.AreEqual(_SAIUN.Scripts.Crop.CropGrade.Legend, _gm.CurrentGrade);

            // 진행 중 설정을 바꿔도 등급은 그대로다.
            _gm.SetTaskText("x");
            Assert.AreEqual(_SAIUN.Scripts.Crop.CropGrade.Legend, _gm.CurrentGrade);
        }

        [Test]
        public void 태스크_텍스트는_40자로_잘려_보관된다()
        {
            _gm.SetTaskText(new string('a', 100));
            Assert.AreEqual(SessionConfig.MaxTaskTextLength, _gm.CurrentConfig.TaskText.Length);
        }

        // ---- 수확·해금 (P4-03) ----

        [Test]
        public void 수확하면_수확_기록과_보유량이_1씩_오른다()
        {
            UseCatalog();
            string harvested = null;
            _gm.OnHarvested += crop => harvested = crop;

            CompleteOneSetSession("wheat");

            Assert.AreEqual(1, _db.GetHarvestCount());
            List<InventoryRecord> inventory = _db.GetInventory();
            Assert.AreEqual(1, inventory.Count);
            Assert.AreEqual("wheat", inventory[0].CropType);
            Assert.AreEqual(1, inventory[0].Quantity);
            Assert.AreEqual("wheat", harvested);
        }

        [Test]
        public void 실패하거나_취소한_세션은_수확하지_않는다()
        {
            UseCatalog();
            _gm.RequestStart(ShortConfig());
            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);
            _gm.RequestAcknowledgeFailure();

            _gm.RequestStart(ShortConfig());
            _gm.RequestCancel();

            Assert.AreEqual(2, _db.GetSessionCount());
            Assert.AreEqual(0, _db.GetHarvestCount());
            Assert.AreEqual(0, _db.GetInventory().Count);
        }

        [Test]
        public void 장기_휴식_중_수확_요청은_휴식을_끝내고_거둔다()
        {
            UseCatalog();
            _gm.RequestStart(ShortConfig(sets: 1));
            _gm.RequestHarvest();
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState, "집중 중에는 무시한다");

            Advance(300);   // → LongBreak
            _gm.RequestHarvest();

            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.AreEqual(1, _db.GetHarvestCount());
        }

        [Test]
        public void 누적_집중_10시간을_채우면_토마토가_한_번_해금된다()
        {
            UseCatalog();
            var unlocked = new List<string>();
            _gm.OnCropUnlocked += crop => unlocked.Add(crop.Id);

            // 9시간 55분을 미리 쌓아 두고 5분짜리 세션을 마친다.
            InsertPastFocus(595);
            CompleteOneSetSession("rice");

            Assert.IsTrue(_db.IsUnlocked("crop.tomato"));
            Assert.IsFalse(_db.IsUnlocked("crop.potato"));
            CollectionAssert.AreEqual(new[] { "tomato" }, unlocked);

            CompleteOneSetSession("rice");
            CollectionAssert.AreEqual(new[] { "tomato" }, unlocked, "이미 해금된 작물은 다시 발행하지 않는다");
        }

        [Test]
        public void 열_번째_수확에서_감자가_해금된다()
        {
            UseCatalog();
            for (int i = 0; i < 9; i++) _db.AddHarvest(0, "rice");

            CompleteOneSetSession("rice");

            Assert.IsTrue(_db.IsUnlocked("crop.potato"));
            Assert.IsTrue(_gm.IsCropUnlocked(_gm.CropCatalog.Find("potato")));
        }

        [Test]
        public void 실패한_세션의_집중_시간도_해금에_쌓인다()
        {
            UseCatalog();
            InsertPastFocus(595);

            _gm.RequestStart(ShortConfig(sets: 2));
            Advance(300);   // 1세트 완료 = 5분
            _gm.RequestCancel();

            Assert.IsTrue(_db.IsUnlocked("crop.tomato"));
        }

        [Test]
        public void 잠긴_작물로_시작하면_기본_작물을_심고_고른_작물은_저장해_둔다()
        {
            UseCatalog();
            _gm.RequestStart(LongConfig("tomato"));

            Assert.AreEqual("rice", _timer.Config.CropType, "잠긴 토마토 대신 목록의 첫 기본 작물");
            Assert.AreEqual("tomato", _gm.CurrentConfig.CropType);
            Assert.AreEqual("tomato", SettingsStore.LoadSessionConfig().CropType);
        }

        [Test]
        public void 해금된_작물은_필요_설정을_채우면_그대로_심는다()
        {
            UseCatalog();
            _db.Unlock("crop.tomato");

            _gm.RequestStart(LongConfig("tomato"));

            Assert.AreEqual("tomato", _timer.Config.CropType);
        }

        [Test]
        public void 수확_기록은_실제로_심은_작물로_남는다()
        {
            UseCatalog();
            CompleteOneSetSession("potato");   // 잠긴 감자 대신 쌀을 심는다

            Assert.AreEqual("rice", _db.GetRecentSessions(1)[0].CropType);
            Assert.AreEqual("rice", _db.GetInventory()[0].CropType);
        }

        private void UseCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<CropCatalog>();
            _owned.Add(catalog);
            catalog.Configure(new[]
            {
                Crop("rice", 25, 4, UnlockCondition.Default, 0),
                Crop("wheat", 25, 4, UnlockCondition.Default, 0),
                Crop("tomato", 45, 4, UnlockCondition.TotalFocusHours, 10),
                Crop("potato", 45, 4, UnlockCondition.HarvestCount, 10),
            });
            typeof(GameManager)
                .GetField("cropCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(_gm, catalog);
        }

        private CropDefinition Crop(string id, int focus, int sets, UnlockCondition condition, int threshold)
        {
            var crop = ScriptableObject.CreateInstance<CropDefinition>();
            _owned.Add(crop);
            crop.Configure(id, id, new GameObject[CropDefinition.StageModelCount]);
            crop.ConfigureRules(focus, sets, condition, threshold);
            return crop;
        }

        private void InsertPastFocus(int minutes)
        {
            _db.InsertSession(new SessionRecord
            {
                StartTime = SaiunDatabase.Now(), DurationMin = minutes, SetsCompleted = 1,
                CropType = "rice", Result = SessionRecord.ResultFailed,
            });
        }

        private void CompleteOneSetSession(string crop)
        {
            _gm.RequestStart(new SessionConfig
            {
                FocusMinutes = 5, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = 1, CropType = crop,
            });
            Advance(300);   // → LongBreak
            Advance(300);   // → Idle
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
        }

        private static SessionConfig LongConfig(string crop)
        {
            return new SessionConfig
            {
                FocusMinutes = 45, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = 4, CropType = crop,
            };
        }

        private static void AssertNoFieldOfType(System.Type owner, System.Type forbidden)
        {
            foreach (System.Reflection.FieldInfo field in owner.GetFields(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                         System.Reflection.BindingFlags.NonPublic))
            {
                Assert.IsFalse(forbidden.IsAssignableFrom(field.FieldType),
                    $"{owner.Name}이(가) {forbidden.Name}을(를) 참조합니다: {field.Name}");
            }
        }
    }
}
