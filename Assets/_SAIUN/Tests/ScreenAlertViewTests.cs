using System.IO;
using System.Reflection;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Timer;
using _SAIUN.Scripts.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P5-02: 휴식 시작은 부드러운 발광, 포모도로 완료는 발광, 방해 앱 유예 동안 붉은 점멸,
    /// 작물 사망 시 화면이 잠시 어두워진다. 알림이 없으면 그래픽을 그리지 않는다.
    /// </summary>
    public class ScreenAlertViewTests
    {
        private string _dbPath;
        private GameObject _gmGo;
        private GameObject _alertGo;
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private GameManager _gm;
        private ScreenAlertView _view;
        private RawImage _glow;
        private Image _dim;
        private double _timerNow;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Application.temporaryCachePath, $"saiun_alert_{System.Guid.NewGuid():N}.db");
            SaiunDatabase.PathOverride = _dbPath;
            SettingsStore.DeleteAll();

            _gmGo = new GameObject("GameManager");
            _sm = _gmGo.AddComponent<PomodoroStateMachine>();
            _timer = _gmGo.AddComponent<PomodoroTimer>();
            _gmGo.AddComponent<SaiunDatabase>();
            _gm = _gmGo.AddComponent<GameManager>();
            _timerNow = 100d;
            _timer.SetClock(() => _timerNow);

            _alertGo = new GameObject("ScreenAlert", typeof(Canvas));
            _alertGo.SetActive(false);
            _view = _alertGo.AddComponent<ScreenAlertView>();
            _glow = new GameObject("EdgeGlow", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _dim = new GameObject("Dim", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _glow.transform.SetParent(_alertGo.transform, false);
            _dim.transform.SetParent(_alertGo.transform, false);
            Set("gameManager", _gm);
            Set("edgeGlow", _glow);
            Set("dimOverlay", _dim);
            _alertGo.SetActive(true);

            _now = 50f;
            _view.SetClock(() => _now);
            _view.Tick();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_alertGo);
            Object.DestroyImmediate(_gmGo);
            SaiunDatabase.PathOverride = null;
            SettingsStore.DeleteAll();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void 알림이_없으면_아무것도_그리지_않는다()
        {
            Assert.AreEqual(ScreenAlert.None, _view.ActiveGlow);
            Assert.IsFalse(_glow.enabled);
            Assert.IsFalse(_dim.enabled);
            Assert.IsNotNull(_glow.texture, "가장자리 빛 텍스처는 미리 만들어 둔다");
            Assert.IsFalse(_glow.raycastTarget || _dim.raycastTarget, "클릭을 가로채면 버튼과 창 드래그가 막힌다");
        }

        [Test]
        public void 휴식이_시작되면_부드럽게_빛났다가_사라진다()
        {
            StartSession(sets: 2);
            AdvanceTimer(300);   // 1세트 완료 → ShortBreak
            Assert.AreEqual(ScreenAlert.BreakStart, _view.ActiveGlow);
            Assert.AreEqual(SaiunPalette.BreakAccent, _view.GlowColor);

            float early = AlphaAt(0.1f);
            float peak = AlphaAt(1f);
            Assert.Greater(peak, early, "천천히 밝아진다");
            Assert.IsTrue(_glow.enabled);

            AlphaAt(10f);
            Assert.AreEqual(0f, _view.GlowAlpha);
            Assert.AreEqual(ScreenAlert.None, _view.ActiveGlow);
            Assert.IsFalse(_glow.enabled);
        }

        [Test]
        public void 포모도로를_마치면_수확색으로_빛난다()
        {
            StartSession(sets: 1);
            AdvanceTimer(300);   // → LongBreak

            Assert.AreEqual(ScreenAlert.SessionComplete, _view.ActiveGlow);
            Assert.AreEqual(SaiunPalette.Harvestable, _view.GlowColor);
            Assert.Greater(AlphaAt(1f), 0.9f);
        }

        [Test]
        public void 유예_동안_붉게_점멸하고_끝나면_꺼진다()
        {
            StartSession(sets: 2);
            _sm.ChangeState(PomodoroState.Interrupted);

            Assert.AreEqual(ScreenAlert.Distraction, _view.ActiveGlow);
            Assert.AreEqual(SaiunPalette.Warning, _view.GlowColor);

            // 반 주기마다 밝고 어두워지고, 다른 발광보다 오래 가도 계속된다.
            float period = (float)Get("blinkPeriod");
            float bright = AlphaAt(0f);
            float dark = AlphaAt(period / 2f);
            float brightAgain = AlphaAt(period * 20f);
            Assert.Greater(bright, dark + 0.5f);
            Assert.AreEqual(bright, brightAgain, 0.01f);

            _sm.ChangeState(PomodoroState.Focus);
            Assert.AreEqual(ScreenAlert.None, _view.ActiveGlow);
            float fading = AlphaAt(period * 20f + 0.05f);
            Assert.Greater(fading, 0f, "그 자리에서 뚝 끊지 않는다");
            Assert.AreEqual(0f, AlphaAt(period * 20f + 2f));
        }

        [Test]
        public void 작물이_죽으면_화면이_잠시_어두워진다()
        {
            StartSession(sets: 2);
            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);

            Assert.AreNotEqual(ScreenAlert.Distraction, _view.ActiveGlow, "실패하면 점멸을 멈춘다");

            _now += 0.8f;
            _view.Tick();
            Assert.Greater(_view.DimAlpha, 0.3f);
            Assert.IsTrue(_dim.enabled);

            _now += 10f;
            _view.Tick();
            Assert.AreEqual(0f, _view.DimAlpha);
            Assert.IsFalse(_dim.enabled);
        }

        [Test]
        public void 펄스_곡선은_시작과_끝이_0이고_사이에서_최고치에_닿는다()
        {
            var pulse = new AlertPulse(1f, 2f, 1f, 0.8f);
            Assert.AreEqual(4f, pulse.Duration);
            Assert.AreEqual(0f, pulse.Evaluate(-0.1f));
            Assert.AreEqual(0f, pulse.Evaluate(0f));
            Assert.AreEqual(0.8f, pulse.Evaluate(2f), 0.0001f);
            Assert.AreEqual(0f, pulse.Evaluate(4f));
            Assert.Less(pulse.Evaluate(3.5f), 0.8f);
        }

        // ---- 도우미 ----

        private void StartSession(int sets)
        {
            _gm.RequestStart(new SessionConfig { FocusMinutes = 5, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = sets });
        }

        private void AdvanceTimer(double seconds)
        {
            _timerNow += seconds;
            _timer.Tick();
        }

        private float _alertStart = float.NaN;

        // 알림이 걸린 시점으로부터 t초 뒤의 세기
        private float AlphaAt(float t)
        {
            if (float.IsNaN(_alertStart)) _alertStart = _now;
            _now = _alertStart + t;
            _view.Tick();
            return _view.GlowAlpha;
        }

        private void Set(string field, object value)
        {
            typeof(ScreenAlertView).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_view, value);
        }

        private object Get(string field)
        {
            return typeof(ScreenAlertView).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_view);
        }
    }
}
