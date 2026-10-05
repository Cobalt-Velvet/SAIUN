using System;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Lighting;
using _SAIUN.Scripts.Timer;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace _SAIUN.Tests
{
    /// <summary>
    /// 시계 화면 하늘: 실제 시각의 해·달을 셈하고, 세션 밖에서는 하늘이 시계를 따르며,
    /// 세션이 시작·끝날 때 시간이 앞으로만 흘러 새 아침·지금 시각으로 넘어간다. 자리는 서울, 표준시 +9다.
    /// </summary>
    public class SkyClockTests
    {
        private const float SeoulLatitude = 37.57f;
        private const float SeoulLongitude = 126.98f;
        private static readonly TimeSpan Kst = TimeSpan.FromHours(9);
        private static readonly DateTime Day = new DateTime(2026, 9, 30);

        // 알려진 보름·삭(UTC)
        private static readonly DateTime FullMoon = new DateTime(2024, 1, 25, 17, 54, 0, DateTimeKind.Utc);
        private static readonly DateTime NewMoon = new DateTime(2024, 1, 11, 11, 57, 0, DateTimeKind.Utc);
        private const double FullMoonAge = SolarCalculator.SynodicMonthDays / 2.0;

        private GameObject _timerGo;
        private GameObject _sunGo;
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private SunOrbitController _orbit;
        private double _now;
        private DateTime _local;

        [SetUp]
        public void SetUp()
        {
            _timerGo = new GameObject("TimerHost");
            _sm = _timerGo.AddComponent<PomodoroStateMachine>();
            _timer = _timerGo.AddComponent<PomodoroTimer>();
            _now = 10d;
            _timer.SetClock(() => _now);

            _sunGo = new GameObject("Sun");
            _sunGo.AddComponent<Light>().type = LightType.Directional;
            _orbit = _sunGo.AddComponent<SunOrbitController>();
            _orbit.Clock = () => _local;
            _orbit.UtcOffset = _ => Kst;
            _orbit.MoonAge = () => FullMoonAge;
            _orbit.SetPlace(SkyPlaces.Resolve("서울", string.Empty));
            _orbit.SetIdleCycle(false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_sunGo);
            Object.DestroyImmediate(_timerGo);
        }

        // ---- 해·달 셈 ----

        [Test]
        public void 해의_고도와_뜨고_지는_시각은_근사식을_따른다()
        {
            // 하지 한낮(서울 남중 12:28 = 03:28 UTC)의 해는 90 − 위도 + 23.44 ≈ 75.9°
            float solstice = SolarCalculator.Elevation(new DateTime(2026, 6, 21, 3, 28, 0, DateTimeKind.Utc), SeoulLatitude, SeoulLongitude);
            Assert.AreEqual(75.9f, solstice, 0.3f);

            // 2026-09-30 서울: 해 뜸 06:25, 해 짐 18:19(대기 굴절을 넣은 지평선 −0.833°)
            Assert.IsTrue(SolarCalculator.Crossing(Day, SeoulLatitude, SeoulLongitude, -0.833f, out double rise, out double set));
            Assert.AreEqual(6 * 60 + 25, rise + Kst.TotalMinutes, 3.0);
            Assert.AreEqual(18 * 60 + 19, set + Kst.TotalMinutes, 3.0);
        }

        [Test]
        public void 달은_보름에_가득_차고_삭에_보이지_않는다()
        {
            Assert.Greater(SolarCalculator.MoonLitFraction(SolarCalculator.MoonAge(FullMoon)), 0.99f);
            Assert.Less(SolarCalculator.MoonLitFraction(SolarCalculator.MoonAge(NewMoon)), 0.01f);
        }

        [Test]
        public void 낮은_진행률로_해가_진_뒤는_실제_해_고도로_박명과_밤이_온다()
        {
            var clock = new SkyClock(SeoulLatitude, SeoulLongitude, SkyArc.Default);

            SkyClockState noon = clock.StateAt(Day.AddHours(12.5), Kst, FullMoonAge);
            Assert.That(noon.Progress, Is.InRange(0.4f, 0.6f));
            Assert.AreEqual(0f, noon.Twilight);
            Assert.AreEqual(0f, noon.Night);

            DateTime dusk = Day.AddHours(18 + 40 / 60.0);
            SkyClockState afterSunset = clock.StateAt(dusk, Kst, FullMoonAge);
            Assert.AreEqual(1f, afterSunset.Progress);
            Assert.That(afterSunset.Twilight, Is.InRange(0.5f, 0.9f), "해가 지평선 5° 아래");
            Assert.AreEqual(0f, afterSunset.Night);
            Assert.AreEqual(RealElevation(dusk), SkyArc.Default.Elevation(1f, afterSunset.Twilight, afterSunset.Night), 0.05f,
                "해 진 뒤 하늘의 해는 실제 해와 같은 고도다");

            SkyClockState night = clock.StateAt(Day.AddHours(21), Kst, FullMoonAge);
            Assert.AreEqual(1f, night.Night, "밤 9시는 깊은 밤이다");

            DateTime dawn = Day.AddHours(5 + 40 / 60.0);
            SkyClockState beforeSunrise = clock.StateAt(dawn, Kst, FullMoonAge);
            Assert.AreEqual(0f, beforeSunrise.Progress);
            Assert.AreEqual(1f, beforeSunrise.Twilight);
            Assert.That(beforeSunrise.Night, Is.InRange(0.1f, 0.4f), "새벽 박명이 막 시작했다");
            Assert.AreEqual(RealElevation(dawn), SkyArc.Default.Elevation(0f, beforeSunrise.Twilight, beforeSunrise.Night), 0.05f);
        }

        [Test]
        public void 보름달은_한밤에_높이_뜨고_삭에는_밤하늘에_없다()
        {
            var clock = new SkyClock(SeoulLatitude, SeoulLongitude, SkyArc.Default);
            SkyClockState full = clock.StateAt(Day.AddHours(0.5), Kst, FullMoonAge);
            Assert.IsTrue(full.MoonUp);
            Assert.That(full.MoonProgress, Is.InRange(0.35f, 0.65f));
            Assert.Greater(full.MoonLit, 0.99f);

            SkyClockState dark = clock.StateAt(Day.AddHours(0.5), Kst, 0.0);
            Assert.IsFalse(dark.MoonUp, "삭의 달은 해와 함께 낮에 떠 있다");
        }

        // ---- 시계 하늘 ----

        [Test]
        public void 세션_밖의_하늘은_실제_시각을_따른다()
        {
            _local = Day.AddHours(21);
            _orbit.Step(0f);

            Assert.IsTrue(_orbit.ClockActive);
            Assert.AreEqual(1f, _orbit.Night, 0.001f);
            Assert.AreEqual(1f, _orbit.Progress, 0.001f);
            Assert.Less(_orbit.SkyElevation, -15f, "해가 깊이 졌다");
            Assert.IsTrue(_orbit.MoonUp, "보름달이 떠 있다");
        }

        [Test]
        public void 세션이_시작하면_밤을_지나_새_아침까지_앞으로_감고_세션에_넘긴다()
        {
            _local = Day.AddHours(14);
            _orbit.Step(0f);
            Assert.That(_orbit.Progress, Is.InRange(0.5f, 0.8f), "오후 2시");

            _timer.StartSession(new SessionConfig { FocusMinutes = 25, TotalSets = 1 });
            float deepest = 0f;
            double previous = _orbit.ClockHours;
            int steps = 0;
            while (_orbit.ClockActive && steps < 400)
            {
                _orbit.Step(0.1f);
                deepest = Mathf.Max(deepest, _orbit.Night);
                Assert.Less(SkyClock.Repeat(_orbit.ClockHours - previous, 24.0), 12.0, "시간은 거꾸로 흐르지 않는다");
                previous = _orbit.ClockHours;
                steps++;
            }

            Assert.IsFalse(_orbit.ClockActive, "몇 초 안에 세션에 넘긴다");
            Assert.Less(steps * 0.1f, 20f);
            Assert.Greater(deepest, 0.9f, "저녁과 밤을 지나 왔다");
            _orbit.Step(0.1f);
            Assert.AreEqual(0f, _orbit.Night);
            Assert.AreEqual(0f, _orbit.Twilight);
            Assert.AreEqual(_timer.Progress, _orbit.Progress, 0.001f, "이제 세션이 해를 옮긴다");
            Assert.IsFalse(_orbit.MoonUp, "세션의 하늘에는 달이 없다");
        }

        [Test]
        public void 세션이_끝나면_세션의_노을에서_지금_시각까지_앞으로_흘린다()
        {
            _local = Day.AddHours(14);
            _orbit.Step(0f);
            _timer.StartSession(new SessionConfig { FocusMinutes = 25, TotalSets = 1 });
            for (int i = 0; i < 400 && _orbit.ClockActive; i++) _orbit.Step(0.1f);

            // 긴 휴식 끝의 푸른 박명에서 세션이 끝난다.
            _orbit.Apply(1f);
            _orbit.StepTwilight(1f, 100000f);
            _timer.Cancel();

            _orbit.Step(0.1f);
            Assert.IsTrue(_orbit.ClockActive);
            Assert.AreEqual(1f, _orbit.Progress, 0.001f, "세션의 저녁에서 시작한다");
            float deepest = 0f;
            for (int i = 0; i < 400; i++)
            {
                _orbit.Step(0.1f);
                deepest = Mathf.Max(deepest, _orbit.Night);
            }
            Assert.Greater(deepest, 0.9f, "밤을 지나 왔다");
            Assert.AreEqual(14.0, _orbit.ClockHours, 1.0 / 60.0, "지금 시각에 닿는다");
            Assert.AreEqual(0f, _orbit.Night);
        }

        [Test]
        public void 하루_순환을_켜면_지금_하늘에서_이어_천천히_돈다()
        {
            _local = Day.AddHours(12);
            _orbit.Step(0f);
            _orbit.SetIdleCycle(true);

            // 기본 24분에 하루: 1분에 한 시간
            _orbit.Step(60f);
            Assert.AreEqual(13.0, _orbit.ClockHours, 0.05);
            Assert.AreEqual(12.0, _local.TimeOfDay.TotalHours, 0.0001, "실제 시각과 따로 돈다");

            _orbit.SetIdleCycle(false);
            for (int i = 0; i < 400; i++) _orbit.Step(0.1f);
            Assert.AreEqual(12.0, _orbit.ClockHours, 1.0 / 60.0, "끄면 앞으로 돌아 실제 시각에 닿는다");
        }

        [Test]
        public void 사는_곳은_고른_도시이고_고르지_않았으면_컴퓨터_시간대의_도시다()
        {
            Assert.AreEqual("부산", SkyPlaces.Resolve("부산", "Tokyo Standard Time").Name);
            Assert.AreEqual("서울", SkyPlaces.Resolve(string.Empty, "Korea Standard Time").Name);
            Assert.AreEqual("도쿄", SkyPlaces.Resolve(string.Empty, "Asia/Tokyo").Name);
            Assert.AreEqual("서울", SkyPlaces.Resolve("없는 곳", "Mars Standard Time").Name, "모르면 서울");
        }

        [Test]
        public void 사는_곳을_바꾸면_그곳의_지금_하늘이_된다()
        {
            _local = Day.AddHours(18 + 40 / 60.0);
            _orbit.Step(0f);
            Assert.Greater(_orbit.Twilight, 0f, "서울은 해가 막 졌다");

            // 같은 순간 런던은 오전 10시 40분이다.
            _orbit.SetPlace(SkyPlaces.Resolve("런던", string.Empty));
            Assert.AreEqual(0f, _orbit.Twilight);
            Assert.That(_orbit.Progress, Is.InRange(0.15f, 0.5f));
        }

        private static float RealElevation(DateTime local)
        {
            return SolarCalculator.Elevation(DateTime.SpecifyKind(local - Kst, DateTimeKind.Utc), SeoulLatitude, SeoulLongitude);
        }
    }
}
