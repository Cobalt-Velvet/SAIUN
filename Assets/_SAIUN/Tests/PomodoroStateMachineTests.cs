using System.Text.RegularExpressions;
using _SAIUN.Scripts.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace _SAIUN.Tests
{
    /// <summary>P1-01 수용 조건: 유효 전이 통과, 무효 전이 경고와 함께 차단, 전이마다 이벤트 1회.</summary>
    public class PomodoroStateMachineTests
    {
        private GameObject _go;
        private PomodoroStateMachine _sm;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("StateMachineTest");
            _sm = _go.AddComponent<PomodoroStateMachine>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void 초기_상태는_Idle이다()
        {
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
        }

        [Test]
        public void 유효_전이_전체_경로가_통과한다()
        {
            // Idle → Focus → ShortBreak → Focus → Interrupted → Focus → LongBreak → Idle
            AssertTransition(PomodoroState.Focus);
            AssertTransition(PomodoroState.ShortBreak);
            AssertTransition(PomodoroState.Focus);
            AssertTransition(PomodoroState.Interrupted);
            AssertTransition(PomodoroState.Focus);
            AssertTransition(PomodoroState.LongBreak);
            AssertTransition(PomodoroState.Idle);

            // Idle → Focus → Interrupted → Failed → Idle
            AssertTransition(PomodoroState.Focus);
            AssertTransition(PomodoroState.Interrupted);
            AssertTransition(PomodoroState.Failed);
            AssertTransition(PomodoroState.Idle);

            // Focus → Idle(취소), ShortBreak → Idle(취소)
            AssertTransition(PomodoroState.Focus);
            AssertTransition(PomodoroState.Idle);
            AssertTransition(PomodoroState.Focus);
            AssertTransition(PomodoroState.ShortBreak);
            AssertTransition(PomodoroState.Idle);
        }

        [Test]
        public void 무효_전이는_경고와_함께_차단된다()
        {
            AssertBlocked(PomodoroState.ShortBreak);   // Idle → ShortBreak
            AssertBlocked(PomodoroState.LongBreak);    // Idle → LongBreak
            AssertBlocked(PomodoroState.Interrupted);  // Idle → Interrupted
            AssertBlocked(PomodoroState.Failed);       // Idle → Failed

            _sm.ChangeState(PomodoroState.Focus);
            AssertBlocked(PomodoroState.Failed);       // Focus → Failed

            _sm.ChangeState(PomodoroState.ShortBreak);
            AssertBlocked(PomodoroState.LongBreak);    // ShortBreak → LongBreak
            AssertBlocked(PomodoroState.Interrupted);  // ShortBreak → Interrupted

            _sm.ChangeState(PomodoroState.Focus);
            _sm.ChangeState(PomodoroState.Interrupted);
            AssertBlocked(PomodoroState.Idle);         // Interrupted → Idle
            AssertBlocked(PomodoroState.ShortBreak);   // Interrupted → ShortBreak

            _sm.ChangeState(PomodoroState.Failed);
            AssertBlocked(PomodoroState.Focus);        // Failed → Focus
        }

        [Test]
        public void 전이마다_이벤트가_정확히_1회_발행된다()
        {
            int count = 0;
            PomodoroState lastFrom = PomodoroState.Idle;
            PomodoroState lastTo = PomodoroState.Idle;
            _sm.OnStateChanged += (from, to) =>
            {
                count++;
                lastFrom = from;
                lastTo = to;
            };

            _sm.ChangeState(PomodoroState.Focus);
            Assert.AreEqual(1, count);
            Assert.AreEqual(PomodoroState.Idle, lastFrom);
            Assert.AreEqual(PomodoroState.Focus, lastTo);

            _sm.ChangeState(PomodoroState.ShortBreak);
            Assert.AreEqual(2, count);
            Assert.AreEqual(PomodoroState.Focus, lastFrom);
            Assert.AreEqual(PomodoroState.ShortBreak, lastTo);
        }

        [Test]
        public void 같은_상태로의_전이는_무시되고_이벤트도_없다()
        {
            int count = 0;
            _sm.OnStateChanged += (_, _) => count++;

            _sm.ChangeState(PomodoroState.Idle);
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void 무효_전이는_이벤트를_발행하지_않는다()
        {
            int count = 0;
            _sm.OnStateChanged += (_, _) => count++;

            LogAssert.Expect(LogType.Warning, new Regex("Invalid transition"));
            _sm.ChangeState(PomodoroState.LongBreak);

            Assert.AreEqual(0, count);
            Assert.AreEqual(PomodoroState.Idle, _sm.CurrentState);
        }

        private void AssertTransition(PomodoroState to)
        {
            PomodoroState before = _sm.CurrentState;
            _sm.ChangeState(to);
            Assert.AreEqual(to, _sm.CurrentState, $"{before} → {to} 전이가 통과해야 한다.");
        }

        private void AssertBlocked(PomodoroState to)
        {
            PomodoroState before = _sm.CurrentState;
            LogAssert.Expect(LogType.Warning, new Regex("Invalid transition"));
            _sm.ChangeState(to);
            Assert.AreEqual(before, _sm.CurrentState, $"{before} → {to} 전이는 차단돼야 한다.");
        }
    }
}
