using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Lighting;
using _SAIUN.Scripts.Timer;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P2-01: 진행률 0/0.5/1이 수평각 +70/0/−70으로 매핑되고, FOCUS에서만 갱신된다.
    /// 고도는 해 뜰 때·질 때 낮고 한낮에 높아 그림자가 길어졌다 짧아진다(2026-09-19 사용자 지시).
    /// </summary>
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
        public void 수평각은_진행률을_따라_선형으로_돈다()
        {
            Assert.AreEqual(45f, SunOrbitController.HorizontalAngleFor(0f), 0.001f);
            Assert.AreEqual(130f, SunOrbitController.HorizontalAngleFor(0.5f), 0.001f);
            Assert.AreEqual(215f, SunOrbitController.HorizontalAngleFor(1f), 0.001f);
            Assert.AreEqual(45f, SunOrbitController.HorizontalAngleFor(-1f), 0.001f, "범위 밖은 잘린다");
            Assert.AreEqual(215f, SunOrbitController.HorizontalAngleFor(2f), 0.001f);
        }

        [Test]
        public void 적용하면_광원이_그_진행률의_고도와_수평각이_된다()
        {
            _orbit.Apply(0.25f);

            Assert.AreEqual(87.5f, _orbit.HorizontalAngle, 0.001f);
            float angle = Quaternion.Angle(_light.transform.rotation, Quaternion.Euler(_orbit.Elevation, 87.5f, 0f));
            Assert.Less(angle, 0.01f);
        }

        [Test]
        public void 고도는_양_끝에서_낮고_한낮에_가장_높다()
        {
            Assert.AreEqual(22f, SunOrbitController.ElevationFor(0f, 22f, 68f), 0.001f);
            Assert.AreEqual(68f, SunOrbitController.ElevationFor(0.5f, 22f, 68f), 0.001f);
            Assert.AreEqual(22f, SunOrbitController.ElevationFor(1f, 22f, 68f), 0.001f);
            Assert.AreEqual(
                SunOrbitController.ElevationFor(0.2f, 22f, 68f),
                SunOrbitController.ElevationFor(0.8f, 22f, 68f), 0.001f, "아침과 저녁이 대칭이다");
        }

        [Test]
        public void 아침_그림자는_한낮보다_길다()
        {
            _orbit.Apply(0f);
            float morning = ShadowLength();
            _orbit.Apply(0.5f);
            float noon = ShadowLength();

            Assert.Greater(morning, noon * 3f, "높이 1인 물체의 그림자 길이");
        }

        [Test]
        public void 고도_변화를_끄면_사양서의_45도_고정이다()
        {
            typeof(SunOrbitController)
                .GetField("varyElevation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(_orbit, false);

            _orbit.Apply(0f);
            Assert.AreEqual(SunOrbitController.VerticalAngle, _orbit.Elevation, 0.001f);
            _orbit.Apply(0.5f);
            Assert.AreEqual(SunOrbitController.VerticalAngle, _orbit.Elevation, 0.001f);
        }

        [Test]
        public void 빛은_아침에_따뜻하고_한낮에_희며_약간_약하다()
        {
            _orbit.Apply(0f);
            Color morning = _light.color;
            float morningIntensity = _light.intensity;
            _orbit.Apply(0.5f);

            Assert.Greater(morning.r - morning.b, 0.1f, "아침 빛은 붉은 쪽으로 기운다");
            Assert.AreEqual(Color.white, _light.color);
            Assert.Less(morningIntensity, _light.intensity);
            Assert.AreEqual(_light.color, _orbit.SunColor);
        }

        // 높이 1인 막대의 그림자가 바닥에 드리우는 길이.
        private float ShadowLength()
        {
            return 1f / Mathf.Tan(_orbit.Elevation * Mathf.Deg2Rad);
        }

        [Test]
        public void 집중_진행률을_따라_각도가_움직인다()
        {
            _timer.StartSession(new SessionConfig { FocusMinutes = 10, TotalSets = 1 });
            _now += 300;   // 50%
            _timer.Tick();

            InvokeUpdate();
            Assert.AreEqual(130f, _orbit.HorizontalAngle, 0.01f);

            _now += 150;   // 75%
            _timer.Tick();
            InvokeUpdate();
            Assert.AreEqual(172.5f, _orbit.HorizontalAngle, 0.01f);
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

        [Test]
        public void 쉬는_동안_해는_휴식_시간을_따라_지고_짧은_휴식은_박명광까지_긴_휴식은_박명까지_간다()
        {
            System.Reflection.MethodInfo goal = typeof(SunOrbitController)
                .GetMethod("TwilightGoal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            float Goal(PomodoroState phase, float progress) => (float)goal.Invoke(_orbit, new object[] { phase, progress });

            Assert.AreEqual(0f, Goal(PomodoroState.ShortBreak, 0f), 0.0001f, "휴식이 막 시작하면 해는 아직 지평선에 걸려 있다");
            float middle = Goal(PomodoroState.ShortBreak, 0.5f);
            float end = Goal(PomodoroState.ShortBreak, 1f);
            Assert.Greater(middle, 0f, "시간이 지나며 해가 진다");
            Assert.Greater(end, middle, "노을이 끝까지 깊어진다");
            Assert.Less(end, 0.5f, "짧은 휴식은 구름을 볼 수 있을 만큼만 진다");

            Assert.Less(Goal(PomodoroState.LongBreak, 0.3f), 1f);
            Assert.AreEqual(1f, Goal(PomodoroState.LongBreak, 0.9f), 0.0001f, "긴 휴식은 푸른 박명까지 간다");
            Assert.AreEqual(0f, Goal(PomodoroState.Idle, 0.5f), 0.0001f);
        }

        [Test]
        public void 쉬는_동안_해가_지면_빛이_어둡고_푸르게_가라앉고_돌아오면_되살아난다()
        {
            _orbit.Apply(1f);
            float dusk = _light.intensity;
            Color duskColor = _light.color;

            _orbit.StepTwilight(1f, 60f);
            Assert.Greater(_orbit.Twilight, 0f);
            Assert.Less(_orbit.Twilight, 1f, "한 번에 지지 않는다");

            _orbit.StepTwilight(1f, 10000f);
            Assert.AreEqual(1f, _orbit.Twilight, 0.0001f);
            Assert.Less(_light.intensity, dusk * 0.5f, "빛이 어두워진다");
            Assert.Greater(_light.color.b - _light.color.r, duskColor.b - duskColor.r, "빛이 푸르러진다");
            Assert.AreEqual(1f, _orbit.Progress, 0.0001f, "궤도는 그대로다");

            _orbit.StepTwilight(0f, 10000f);
            Assert.AreEqual(0f, _orbit.Twilight, 0.0001f);
            Assert.AreEqual(dusk, _light.intensity, 0.0001f);
        }
    }
}
