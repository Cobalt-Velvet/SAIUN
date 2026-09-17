using System.Collections.Generic;
using System.IO;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using _SAIUN.Scripts.Timer;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>P4-01·P4-02: 1초 폴링으로 INTERRUPTED 전이, 유예 복귀·초과, 세션 예외.</summary>
    public class ForegroundWatcherTests
    {
        private string _dbPath;
        private string _blacklistPath;
        private GameObject _go;
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private SaiunDatabase _db;
        private ForegroundWatcher _watcher;
        private GameManager _gm;
        private string _foreground;
        private double _now;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Application.temporaryCachePath, $"saiun_fw_{System.Guid.NewGuid():N}.db");
            _blacklistPath = Path.Combine(Application.temporaryCachePath, $"blacklist_fw_{System.Guid.NewGuid():N}.json");
            SaiunDatabase.PathOverride = _dbPath;
            BlacklistStore.PathOverride = _blacklistPath;
            SettingsStore.DeleteAll();

            _go = new GameObject("WatcherTest");
            _sm = _go.AddComponent<PomodoroStateMachine>();
            _timer = _go.AddComponent<PomodoroTimer>();
            _db = _go.AddComponent<SaiunDatabase>();
            _watcher = _go.AddComponent<ForegroundWatcher>();
            _gm = _go.AddComponent<GameManager>();

            _now = 0d;
            _timer.SetClock(() => _now);
            _foreground = "SAIUN";
            _watcher.ProcessNameProvider = () => _foreground;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            SaiunDatabase.PathOverride = null;
            BlacklistStore.PathOverride = null;
            SettingsStore.DeleteAll();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_blacklistPath)) File.Delete(_blacklistPath);
        }

        private void StartFocus()
        {
            _gm.RequestStart(new SessionConfig { FocusMinutes = 5, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = 2 });
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
        }

        [Test]
        public void 유예_시간은_설정값으로_초기화되고_범위가_보정된다()
        {
            Assert.AreEqual(SettingsStore.DefaultGraceSeconds, _watcher.GraceSeconds);
            _watcher.GraceSeconds = 100;
            Assert.AreEqual(SettingsStore.MaxGraceSeconds, _watcher.GraceSeconds);
            _watcher.GraceSeconds = 1;
            Assert.AreEqual(SettingsStore.MinGraceSeconds, _watcher.GraceSeconds);
        }

        [Test]
        public void Idle에서는_감시하지_않는다()
        {
            _foreground = "chrome";
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.IsFalse(_watcher.IsWatching);
        }

        [Test]
        public void 집중_중_블랙리스트_앱이_앞에_오면_Interrupted로_간다()
        {
            string detected = null;
            _watcher.OnDistractionDetected += p => detected = p;

            StartFocus();
            Assert.IsTrue(_watcher.IsWatching);

            _foreground = "notepad";
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);

            _foreground = "chrome";
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.Interrupted, _sm.CurrentState);
            Assert.AreEqual("chrome", detected);
            Assert.AreEqual("chrome", _watcher.DetectedProcess);
            Assert.AreEqual(7f, _watcher.GraceRemainingSeconds, 0.001f);
            Assert.IsTrue(_timer.IsPaused, "유예 중에는 타이머가 멈춘다");
        }

        [Test]
        public void 유예_안에_돌아오면_Focus로_복귀한다()
        {
            int recovered = 0;
            var ticks = new List<float>();
            _watcher.OnRecovered += () => recovered++;
            _watcher.OnGraceTick += t => ticks.Add(t);

            StartFocus();
            _foreground = "chrome";
            _watcher.Poll();
            _watcher.Poll();
            _watcher.Poll();
            Assert.AreEqual(5f, _watcher.GraceRemainingSeconds, 0.001f);

            _foreground = "SAIUN";
            _watcher.Poll();

            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
            Assert.AreEqual(1, recovered);
            Assert.IsNull(_watcher.DetectedProcess);
            Assert.IsFalse(_timer.IsPaused);
            CollectionAssert.AreEqual(new[] { 7f, 6f, 5f }, ticks);
        }

        [Test]
        public void 유예를_넘기면_Failed로_가고_세션이_FAILED로_기록된다()
        {
            int expired = 0;
            _watcher.OnGraceExpired += () => expired++;

            StartFocus();
            _foreground = "steam.exe";
            _watcher.Poll();                                   // 감지, 7초
            for (int i = 0; i < 6; i++) _watcher.Poll();       // 6 → 1초
            Assert.AreEqual(PomodoroState.Interrupted, _sm.CurrentState);
            Assert.AreEqual(1f, _watcher.GraceRemainingSeconds, 0.001f);

            _watcher.Poll();                                   // 0초 → Failed
            Assert.AreEqual(PomodoroState.Failed, _sm.CurrentState);
            Assert.AreEqual(1, expired);
            Assert.IsFalse(_watcher.IsWatching);
            Assert.IsNull(_watcher.DetectedProcess);

            List<SessionRecord> sessions = _db.GetRecentSessions(1);
            Assert.AreEqual(1, sessions.Count);
            Assert.AreEqual(SessionRecord.ResultFailed, sessions[0].Result);
            Assert.AreEqual(0, sessions[0].SetsCompleted);
        }

        [Test]
        public void 세션_예외를_주면_그_세션에서는_같은_앱을_무시한다()
        {
            StartFocus();
            _foreground = "chrome";
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.Interrupted, _sm.CurrentState);

            _gm.RequestExcuseDistraction();
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
            Assert.IsTrue(_watcher.IsExcusedThisSession("Chrome.exe"));

            _watcher.Poll();
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState, "예외 처리된 앱은 다시 감지하지 않는다");

            _foreground = "steam";
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.Interrupted, _sm.CurrentState, "다른 블랙리스트 앱은 여전히 감지한다");
        }

        [Test]
        public void 세션_예외는_다음_세션에서_초기화된다()
        {
            StartFocus();
            _foreground = "chrome";
            _watcher.Poll();
            _gm.RequestExcuseDistraction();
            _gm.RequestCancel();
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);

            StartFocus();
            Assert.IsFalse(_watcher.IsExcusedThisSession("chrome"));
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.Interrupted, _sm.CurrentState);
        }

        [Test]
        public void 영구_화이트리스트_앱은_감지하지_않는다()
        {
            _watcher.Blacklist.AddToWhitelist("chrome.exe");
            StartFocus();
            _foreground = "chrome";
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
        }

        [Test]
        public void 휴식_중에는_감시하지_않는다()
        {
            StartFocus();
            _now += 300;
            _timer.Tick();
            Assert.AreEqual(PomodoroState.ShortBreak, _sm.CurrentState);
            Assert.IsFalse(_watcher.IsWatching);

            _foreground = "chrome";
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.ShortBreak, _sm.CurrentState);
        }

        [Test]
        public void 유예_밖에서의_예외_요청은_무시된다()
        {
            StartFocus();
            _gm.RequestExcuseDistraction();
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
        }
    }
}
