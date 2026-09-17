using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Lighting;
using _SAIUN.Scripts.Timer;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>P2-01: 진행률 0/0.5/1이 수평각 +70/0/−70으로 매핑되고, FOCUS에서만 갱신된다.</summary>
    public class SunOrbitControllerTests
    {
        private GameObject _go;
        private GameObject _lightGo;
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private SunOrbitController _orbit;
        private Light _light;
        private double _now;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TimerHost");
            _sm = _go.AddComponent<PomodoroStateMachine>();
            _timer = _go.AddComponent<PomodoroTimer>();
            _now = 10d;
            _timer.SetClock(() => _now);

            _lightGo = new GameObject("Sun");
            _light = _lightGo.AddComponent<Light>();
            _light.type = LightType.Directional;
            _orbit = _lightGo.AddComponent<SunOrbitController>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_lightGo);
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void 수식은_사양서_4장과_같다()
        {
            Assert.AreEqual(70f, SunOrbitController.HorizontalAngleFor(0f), 0.001f);
            Assert.AreEqual(0f, SunOrbitController.HorizontalAngleFor(0.5f), 0.001f);
            Assert.AreEqual(-70f, SunOrbitController.HorizontalAngleFor(1f), 0.001f);
            Assert.AreEqual(70f, SunOrbitController.HorizontalAngleFor(-1f), 0.001f, "범위 밖은 잘린다");
            Assert.AreEqual(-70f, SunOrbitController.HorizontalAngleFor(2f), 0.001f);
        }

        [Test]
        public void 적용하면_광원_회전이_수직45도_수평각이_된다()
        {
            _orbit.Apply(0.25f);

            Assert.AreEqual(35f, _orbit.HorizontalAngle, 0.001f);
            float angle = Quaternion.Angle(_light.transform.rotation, Quaternion.Euler(45f, 35f, 0f));
            Assert.Less(angle, 0.01f);
        }

        [Test]
        public void 집중_진행률을_따라_각도가_움직인다()
        {
            _timer.StartSession(new SessionConfig { FocusMinutes = 10, TotalSets = 1 });
            _now += 300;   // 50%
            _timer.Tick();

            InvokeUpdate();
            Assert.AreEqual(0f, _orbit.HorizontalAngle, 0.01f);

            _now += 150;   // 75%
            _timer.Tick();
            InvokeUpdate();
            Assert.AreEqual(-35f, _orbit.HorizontalAngle, 0.01f);
        }

        [Test]
        public void 휴식_중에는_각도를_유지한다()
        {
            _timer.StartSession(new SessionConfig { FocusMinutes = 5, ShortBreakMinutes = 1, TotalSets = 2 });
            _now += 300;   // 집중 종료 → ShortBreak
            _timer.Tick();
            InvokeUpdate();
            float held = _orbit.HorizontalAngle;
            Assert.AreEqual(PomodoroState.ShortBreak, _sm.CurrentState);

            _now += 30;    // 휴식 50%
            _timer.Tick();
            InvokeUpdate();
            Assert.AreEqual(held, _orbit.HorizontalAngle, 0.001f);
        }

        // Update는 프레임을 기다려야 하므로 리플렉션으로 직접 부른다.
        private void InvokeUpdate()
        {
            typeof(SunOrbitController)
                .GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(_orbit, null);
        }
    }
}
