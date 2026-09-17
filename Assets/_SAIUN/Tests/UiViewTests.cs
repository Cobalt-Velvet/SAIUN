using System;
using System.IO;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using _SAIUN.Scripts.Timer;
using _SAIUN.Scripts.UI;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P1-06·P1-07 수용 조건: 상태에 따라 버튼 라벨이 바뀌고 40자 초과가 잘린다.
    /// IDLE·FOCUS 표시가 바뀌고 세트 도트가 진행에 맞게 채워진다.
    /// 프리팹은 SaiunSceneBuilder가 만든 것을 그대로 쓴다.
    /// </summary>
    public class UiViewTests
    {
        private const string BottomBarPrefabPath = "Assets/_SAIUN/Prefabs/UI/BottomBar.prefab";
        private const string TimerHudPrefabPath = "Assets/_SAIUN/Prefabs/UI/TimerHud.prefab";

        private string _dbPath;
        private string _blacklistPath;
        private ForegroundWatcher _watcher;
        private string _foreground = "SAIUN";
        private GameObject _gmGo;
        private GameObject _canvasGo;
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private GameManager _gm;
        private BottomBarView _bar;
        private TimerHudView _hud;
        private double _now;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Application.temporaryCachePath, $"saiun_ui_{Guid.NewGuid():N}.db");
            SaiunDatabase.PathOverride = _dbPath;
            _blacklistPath = Path.Combine(Application.temporaryCachePath, $"blacklist_ui_{Guid.NewGuid():N}.json");
            BlacklistStore.PathOverride = _blacklistPath;
            SettingsStore.DeleteAll();

            _gmGo = new GameObject("GameManager");
            _sm = _gmGo.AddComponent<PomodoroStateMachine>();
            _timer = _gmGo.AddComponent<PomodoroTimer>();
            _gmGo.AddComponent<SaiunDatabase>();
            _watcher = _gmGo.AddComponent<ForegroundWatcher>();
            _watcher.ProcessNameProvider = () => _foreground;
            _gm = _gmGo.AddComponent<GameManager>();

            _now = 100d;
            _timer.SetClock(() => _now);

            _canvasGo = new GameObject("Canvas", typeof(Canvas));
            _bar = Instantiate<BottomBarView>(BottomBarPrefabPath);
            _hud = Instantiate<TimerHudView>(TimerHudPrefabPath);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_canvasGo);
            UnityEngine.Object.DestroyImmediate(_gmGo);
            SaiunDatabase.PathOverride = null;
            BlacklistStore.PathOverride = null;
            SettingsStore.DeleteAll();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_blacklistPath)) File.Delete(_blacklistPath);
        }

        private T Instantiate<T>(string prefabPath) where T : Component
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, $"프리팹이 없습니다. SAIUN/Build All을 먼저 실행하세요: {prefabPath}");
            GameObject instance = UnityEngine.Object.Instantiate(prefab, _canvasGo.transform);
            return instance.GetComponent<T>();
#else
            Assert.Ignore("에디터에서만 프리팹을 읽는다.");
            return null;
#endif
        }

        private static SessionConfig ShortConfig(int sets = 3)
        {
            return new SessionConfig { FocusMinutes = 5, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = sets };
        }

        private void Advance(double seconds)
        {
            _now += seconds;
            _timer.Tick();
        }

        // ---- Bottom Bar ----

        [Test]
        public void 바_Idle에서는_시작_라벨이고_입력이_가능하다()
        {
            Assert.AreEqual("시작", _bar.CurrentButtonLabel);
            Assert.IsTrue(_bar.TaskInput.interactable);
            Assert.IsTrue(_bar.PrimaryButton.interactable);
        }

        [Test]
        public void 바_시작을_누르면_세션이_시작되고_정지_라벨로_바뀐다()
        {
            _bar.TaskInput.text = "사양서 읽기";
            _bar.PrimaryButton.onClick.Invoke();

            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
            Assert.AreEqual("정지", _bar.CurrentButtonLabel);
            Assert.IsFalse(_bar.TaskInput.interactable);
            Assert.AreEqual("사양서 읽기", _timer.Config.TaskText);
        }

        [Test]
        public void 바_정지를_누르면_Idle로_돌아가고_시작_라벨이_된다()
        {
            _bar.PrimaryButton.onClick.Invoke();
            Advance(10);
            _bar.PrimaryButton.onClick.Invoke();

            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.AreEqual("시작", _bar.CurrentButtonLabel);
            Assert.IsTrue(_bar.TaskInput.interactable);
        }

        [Test]
        public void 바_휴식_중에도_정지_라벨이다()
        {
            _gm.RequestStart(ShortConfig());
            Advance(300);
            Assert.AreEqual(PomodoroState.ShortBreak, _sm.CurrentState);
            Assert.AreEqual("정지", _bar.CurrentButtonLabel);
        }

        [Test]
        public void 바_유예_중에는_괜찮아요_라벨이고_누르면_Focus로_복귀한다()
        {
            _gm.RequestStart(ShortConfig());
            _foreground = "chrome";
            _watcher.Poll();
            Assert.AreEqual(PomodoroState.Interrupted, _sm.CurrentState);
            Assert.AreEqual("괜찮아요", _bar.CurrentButtonLabel);
            Assert.IsTrue(_bar.PrimaryButton.interactable);
            Assert.AreEqual("INTERRUPTED 7", _hud.PhaseText);

            _bar.PrimaryButton.onClick.Invoke();
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
            Assert.AreEqual("정지", _bar.CurrentButtonLabel);
            Assert.AreEqual("POMODORO", _hud.PhaseText);
        }

        [Test]
        public void 바_실패_상태에서는_확인_라벨이고_누르면_Idle이다()
        {
            _gm.RequestStart(ShortConfig());
            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);

            Assert.AreEqual("확인", _bar.CurrentButtonLabel);
            Assert.IsTrue(_bar.PrimaryButton.interactable);

            _bar.PrimaryButton.onClick.Invoke();
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.AreEqual("시작", _bar.CurrentButtonLabel);
        }

        [Test]
        public void 바_40자_초과_입력은_잘린다()
        {
            Assert.AreEqual(SessionConfig.MaxTaskTextLength, _bar.TaskInput.characterLimit);

            _bar.TaskInput.text = new string('가', 70);
            _bar.PrimaryButton.onClick.Invoke();

            Assert.AreEqual(SessionConfig.MaxTaskTextLength, _timer.Config.TaskText.Length);
            Assert.AreEqual(SessionConfig.MaxTaskTextLength, _gm.CurrentConfig.TaskText.Length);
        }

        // ---- Timer HUD ----

        [Test]
        public void HUD_Idle에서는_현재_시각을_대형으로_보이고_보조_표시는_숨긴다()
        {
            string before = DateTime.Now.ToString("HH:mm");
            string shown = _hud.PrimaryText;
            string after = DateTime.Now.ToString("HH:mm");

            Assert.IsTrue(shown == before || shown == after, $"시계 표시가 현재 시각과 다르다: {shown}");
            Assert.IsFalse(_hud.IsSecondaryVisible);
            Assert.IsFalse(_hud.AreDotsVisible);
        }

        [Test]
        public void HUD_Focus에서는_타이머_대형_시각_소형_2단이다()
        {
            _gm.RequestStart(ShortConfig());

            Assert.AreEqual("05:00", _hud.PrimaryText);
            Assert.IsTrue(_hud.IsSecondaryVisible);
            string clock = DateTime.Now.ToString("HH:mm");
            Assert.IsTrue(_hud.SecondaryText == clock || Math.Abs((DateTime.Now - DateTime.ParseExact(_hud.SecondaryText, "HH:mm", null)).TotalMinutes) <= 1);
            Assert.AreEqual("POMODORO", _hud.PhaseText);

            Advance(61);
            Assert.AreEqual("03:59", _hud.PrimaryText);
        }

        [Test]
        public void HUD_세트_도트가_진행에_맞게_채워진다()
        {
            _gm.RequestStart(ShortConfig(sets: 3));

            Assert.IsTrue(_hud.AreDotsVisible);
            Assert.AreEqual(3, _hud.DotCount);
            Assert.AreEqual(0, _hud.FilledDotCount);

            Advance(300);   // 1세트 완료 → ShortBreak
            Assert.AreEqual(1, _hud.FilledDotCount);
            Assert.AreEqual("SHORT BREAK", _hud.PhaseText);
            Assert.AreEqual("01:00", _hud.PrimaryText);

            Advance(60);
            Advance(300);   // 2세트 완료
            Assert.AreEqual(2, _hud.FilledDotCount);

            Advance(60);
            Advance(300);   // 3세트 완료 → LongBreak
            Assert.AreEqual(3, _hud.FilledDotCount);
            Assert.AreEqual("LONG BREAK", _hud.PhaseText);

            Advance(300);   // → Idle
            Assert.IsFalse(_hud.AreDotsVisible);
            Assert.IsFalse(_hud.IsSecondaryVisible);
        }

        [Test]
        public void HUD_세트_수가_바뀌면_도트_수도_바뀐다()
        {
            _gm.RequestStart(ShortConfig(sets: 2));
            Assert.AreEqual(2, _hud.DotCount);
            _gm.RequestCancel();

            _gm.RequestStart(ShortConfig(sets: 5));
            Assert.AreEqual(5, _hud.DotCount);
        }

        [Test]
        public void HUD_남은_시간_형식은_올림_mm_ss다()
        {
            Assert.AreEqual("00:00", TimerHudView.FormatRemaining(0f));
            Assert.AreEqual("01:00", TimerHudView.FormatRemaining(59.2f));
            Assert.AreEqual("25:00", TimerHudView.FormatRemaining(1500f));
            Assert.AreEqual("90:00", TimerHudView.FormatRemaining(5400f));
        }
    }
}
