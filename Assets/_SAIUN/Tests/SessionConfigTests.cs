using _SAIUN.Scripts.Core;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>P1-02 수용 조건: 범위 밖 값은 경계값으로 보정, MonoBehaviour 미상속.</summary>
    public class SessionConfigTests
    {
        [Test]
        public void MonoBehaviour를_상속하지_않는다()
        {
            Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(SessionConfig)));
        }

        [Test]
        public void 기본값은_사양서_8장을_따른다()
        {
            var config = new SessionConfig();
            Assert.AreEqual(25, config.FocusMinutes);
            Assert.AreEqual(5, config.ShortBreakMinutes);
            Assert.AreEqual(15, config.LongBreakMinutes);
            Assert.AreEqual(4, config.TotalSets);
            Assert.AreEqual(string.Empty, config.TaskText);
        }

        [Test]
        public void 범위_밖_값은_경계값으로_보정된다()
        {
            var config = new SessionConfig
            {
                FocusMinutes = 1,
                ShortBreakMinutes = 999,
                LongBreakMinutes = -5,
                TotalSets = 100,
            };

            Assert.AreEqual(SessionConfig.MinFocusMinutes, config.FocusMinutes);
            Assert.AreEqual(SessionConfig.MaxShortBreakMinutes, config.ShortBreakMinutes);
            Assert.AreEqual(SessionConfig.MinLongBreakMinutes, config.LongBreakMinutes);
            Assert.AreEqual(SessionConfig.MaxTotalSets, config.TotalSets);

            config.FocusMinutes = 200;
            config.ShortBreakMinutes = 0;
            config.LongBreakMinutes = 500;
            config.TotalSets = 0;

            Assert.AreEqual(SessionConfig.MaxFocusMinutes, config.FocusMinutes);
            Assert.AreEqual(SessionConfig.MinShortBreakMinutes, config.ShortBreakMinutes);
            Assert.AreEqual(SessionConfig.MaxLongBreakMinutes, config.LongBreakMinutes);
            Assert.AreEqual(SessionConfig.MinTotalSets, config.TotalSets);
        }

        [Test]
        public void 범위_안_값은_그대로_유지된다()
        {
            var config = new SessionConfig { FocusMinutes = 45, ShortBreakMinutes = 10, LongBreakMinutes = 30, TotalSets = 8 };
            Assert.AreEqual(45, config.FocusMinutes);
            Assert.AreEqual(10, config.ShortBreakMinutes);
            Assert.AreEqual(30, config.LongBreakMinutes);
            Assert.AreEqual(8, config.TotalSets);
        }

        [Test]
        public void 태스크_텍스트는_40자에서_잘린다()
        {
            string longText = new string('가', 60);
            var config = new SessionConfig { TaskText = longText };

            Assert.AreEqual(SessionConfig.MaxTaskTextLength, config.TaskText.Length);
            Assert.AreEqual(longText.Substring(0, SessionConfig.MaxTaskTextLength), config.TaskText);

            config.TaskText = null;
            Assert.AreEqual(string.Empty, config.TaskText);
        }

        [Test]
        public void 직렬화를_거친_범위_밖_값도_보정된다()
        {
            // 인스펙터·JSON처럼 setter를 우회하는 경로를 JsonUtility로 흉내 낸다.
            const string json = "{\"focusMinutes\":1,\"shortBreakMinutes\":99,\"longBreakMinutes\":2,\"totalSets\":50,\"taskText\":\"\"}";
            SessionConfig config = JsonUtility.FromJson<SessionConfig>(json);

            Assert.AreEqual(SessionConfig.MinFocusMinutes, config.FocusMinutes);
            Assert.AreEqual(SessionConfig.MaxShortBreakMinutes, config.ShortBreakMinutes);
            Assert.AreEqual(SessionConfig.MinLongBreakMinutes, config.LongBreakMinutes);
            Assert.AreEqual(SessionConfig.MaxTotalSets, config.TotalSets);
        }

        [Test]
        public void Clone은_독립된_복사본을_만든다()
        {
            var original = new SessionConfig { FocusMinutes = 30, TaskText = "원본" };
            SessionConfig copy = original.Clone();

            copy.FocusMinutes = 50;
            copy.TaskText = "복사본";

            Assert.AreEqual(30, original.FocusMinutes);
            Assert.AreEqual("원본", original.TaskText);
            Assert.AreEqual(50, copy.FocusMinutes);
        }

        [Test]
        public void 전체_집중_시간은_집중_곱하기_세트다()
        {
            var config = new SessionConfig { FocusMinutes = 25, TotalSets = 4 };
            Assert.AreEqual(100, config.TotalFocusMinutes);
        }
    }
}
