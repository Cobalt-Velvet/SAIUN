using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Timer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P1-03 수용 조건: 실제 경과와 오차 1초 이내, 세트가 자동으로 끝까지 진행.
    /// 로직 검증은 가짜 시계로, 정확도 검증은 실시간으로 한다.
    /// </summary>
    public class PomodoroTimerTests
    {
        private GameObject _go;
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private double _now;
        private List<PomodoroState> _history;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TimerTest");
            _sm = _go.AddComponent<PomodoroStateMachine>();
            _timer = _go.AddComponent<PomodoroTimer>();

            _now = 1000d;
            _timer.SetClock(() => _now);

            _history = new List<PomodoroState>();
            _sm.OnStateChanged += (_, to) => _history.Add(to);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        private static SessionConfig ShortConfig(int sets = 2)
        {
            // 최소 범위값: 집중 5분 / 단기 1분 / 장기 5분
            return new SessionConfig { FocusMinutes = 5, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = sets };
        }

        private void Advance(double seconds)
        {
            _now += seconds;
            _timer.Tick();
        }

        [Test]
        public void 시작하면_Focus로_전이하고_첫_세트_카운트다운이_선다()
        {
            _timer.StartSession(ShortConfig());

            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
            Assert.AreEqual(1, _timer.CurrentSet);
            Assert.AreEqual(0, _timer.CompletedSets);
            Assert.AreEqual(300f, _timer.RemainingSeconds, 0.001f);
            Assert.AreEqual(0f, _timer.Progress, 0.001f);
            Assert.IsTrue(_timer.IsRunning);
            Assert.IsFalse(_timer.IsPaused);
        }

        [Test]
        public void Idle이_아니면_시작을_거부한다()
        {
            _timer.StartSession(ShortConfig());
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("시작할 수 없습니다"));
            _timer.StartSession(ShortConfig());

            Assert.AreEqual(1, _history.Count);
        }

        [Test]
        public void 남은_시간과_진행률이_시계를_따라간다()
        {
            _timer.StartSession(ShortConfig());

            Advance(120);
            Assert.AreEqual(180f, _timer.RemainingSeconds, 0.001f);
            Assert.AreEqual(0.4f, _timer.Progress, 0.001f);

            Advance(179.5);
            Assert.AreEqual(0.5f, _timer.RemainingSeconds, 0.001f);
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
        }

        [Test]
        public void 세트가_자동으로_끝까지_진행된다()
        {
            int phaseCompleted = 0;
            _timer.OnPhaseCompleted += () => phaseCompleted++;

            _timer.StartSession(ShortConfig(sets: 2));

            // 1세트 집중 종료 → 단기 휴식
            Advance(300);
            Assert.AreEqual(PomodoroState.ShortBreak, _sm.CurrentState);
            Assert.AreEqual(1, _timer.CompletedSets);
            Assert.AreEqual(60f, _timer.RemainingSeconds, 0.001f);

            // 휴식 종료 → 2세트 집중
            Advance(60);
            Assert.AreEqual(PomodoroState.Focus, _sm.CurrentState);
            Assert.AreEqual(2, _timer.CurrentSet);

            // 마지막 세트 집중 종료 → 장기 휴식
            Advance(300);
            Assert.AreEqual(PomodoroState.LongBreak, _sm.CurrentState);
            Assert.AreEqual(2, _timer.CompletedSets);

            // 장기 휴식 종료 → Idle
            Advance(300);
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.IsFalse(_timer.IsRunning);

            Assert.AreEqual(4, phaseCompleted);
            CollectionAssert.AreEqual(
                new[]
                {
                    PomodoroState.Focus, PomodoroState.ShortBreak, PomodoroState.Focus,
                    PomodoroState.LongBreak, PomodoroState.Idle,
                },
                _history);
        }

        [Test]
        public void 네_세트_구성도_끝까지_진행된다()
        {
            _timer.StartSession(ShortConfig(sets: 4));

            for (int set = 1; set <= 4; set++)
            {
                Assert.AreEqual(set, _timer.CurrentSet);
                Advance(300);
                if (set < 4)
                {
                    Assert.AreEqual(PomodoroState.ShortBreak, _sm.CurrentState);
                    Advance(60);
                }
            }

            Assert.AreEqual(PomodoroState.LongBreak, _sm.CurrentState);
            Assert.AreEqual(4, _timer.CompletedSets);
            Advance(300);
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
        }

        [Test]
        public void 일시정지_중에는_시간이_흐르지_않는다()
        {
            _timer.StartSession(ShortConfig());
            Advance(100);

            _timer.Pause();
            Assert.IsTrue(_timer.IsPaused);
            _now += 500;
            _timer.Tick();
            Assert.AreEqual(200f, _timer.RemainingSeconds, 0.001f);

            _timer.Resume();
            Assert.IsFalse(_timer.IsPaused);
            Advance(50);
            Assert.AreEqual(150f, _timer.RemainingSeconds, 0.001f);
        }

        [Test]
        public void 유예_상태에서는_멈추고_복귀하면_이어간다()
        {
            _timer.StartSession(ShortConfig());
            Advance(100);

            _sm.ChangeState(PomodoroState.Interrupted);
            Assert.IsTrue(_timer.IsPaused);
            _now += 7;

            _sm.ChangeState(PomodoroState.Focus);
            Assert.IsFalse(_timer.IsPaused);
            Advance(0);
            Assert.AreEqual(200f, _timer.RemainingSeconds, 0.001f);
            Assert.AreEqual(1, _timer.CurrentSet);
        }

        [Test]
        public void 실패하면_카운트다운이_멈춘다()
        {
            _timer.StartSession(ShortConfig());
            _sm.ChangeState(PomodoroState.Interrupted);
            _sm.ChangeState(PomodoroState.Failed);

            Assert.IsFalse(_timer.IsRunning);
            Assert.AreEqual(0f, _timer.RemainingSeconds);
        }

        [Test]
        public void 취소하면_Idle로_돌아간다()
        {
            _timer.StartSession(ShortConfig());
            Advance(10);
            _timer.Cancel();

            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.IsFalse(_timer.IsRunning);
            Assert.AreEqual(0f, _timer.RemainingSeconds);
        }

        [Test]
        public void 틱은_남은_정수_초가_바뀔_때마다_1회_발행된다()
        {
            int ticks = 0;
            _timer.OnTick += () => ticks++;

            _timer.StartSession(ShortConfig());
            Assert.AreEqual(1, ticks, "시작 직후 첫 틱");

            Advance(0.3);
            Advance(0.3);
            Assert.AreEqual(1, ticks, "같은 초 안에서는 발행하지 않는다");

            Advance(0.5);
            Assert.AreEqual(2, ticks);

            for (int i = 0; i < 10; i++) Advance(1);
            Assert.AreEqual(12, ticks);
        }

        [UnityTest]
        public IEnumerator 실시간으로_5초_돌리면_오차가_1초_이내다()
        {
            _timer.SetClock(null);   // 실시간 시계로 복귀
            _timer.StartSession(ShortConfig());

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            yield return new WaitForSecondsRealtime(5f);
            stopwatch.Stop();

            float expected = 300f - (float)stopwatch.Elapsed.TotalSeconds;
            Assert.AreEqual(expected, _timer.RemainingSeconds, 1f);
            // 실제로는 프레임 간격 수준의 오차만 허용한다.
            Assert.AreEqual(expected, _timer.RemainingSeconds, 0.1f);
        }

        [UnityTest]
        public IEnumerator 배속으로_2세트_전체가_실시간_흐름에_따라_끝난다()
        {
            // 집중 5분 + 휴식 1분 + 집중 5분 + 장기 5분 = 16분 = 960초. 100배속이면 9.6초.
            const float scale = 100f;
            SetTimeScale(scale);
            _timer.SetClock(null);

            _timer.StartSession(ShortConfig(sets: 2));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (_sm.CurrentState != PomodoroState.Idle && stopwatch.Elapsed.TotalSeconds < 20)
            {
                yield return null;
            }

            stopwatch.Stop();
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.AreEqual(2, _timer.CompletedSets);
            Assert.AreEqual(960f / scale, (float)stopwatch.Elapsed.TotalSeconds, 1f);
        }

        private void SetTimeScale(float value)
        {
            FieldInfo field = typeof(PomodoroTimer).GetField("timeScale", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "timeScale 필드를 찾지 못했다.");
            field.SetValue(_timer, value);
        }
    }
}
