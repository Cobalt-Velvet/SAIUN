using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using NUnit.Framework;

namespace _SAIUN.Tests
{
    /// <summary>P1-04 SettingsStore: 키 상수와 기본값, 저장·복원.</summary>
    public class SettingsStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            SettingsStore.DeleteAll();
        }

        [TearDown]
        public void TearDown()
        {
            SettingsStore.DeleteAll();
        }

        [Test]
        public void 저장값이_없으면_기본값을_돌려준다()
        {
            SessionConfig config = SettingsStore.LoadSessionConfig();
            Assert.AreEqual(SessionConfig.DefaultFocusMinutes, config.FocusMinutes);
            Assert.AreEqual(SessionConfig.DefaultShortBreakMinutes, config.ShortBreakMinutes);
            Assert.AreEqual(SessionConfig.DefaultLongBreakMinutes, config.LongBreakMinutes);
            Assert.AreEqual(SessionConfig.DefaultTotalSets, config.TotalSets);

            Assert.IsTrue(SettingsStore.AlwaysOnTop);
            Assert.IsTrue(SettingsStore.SoundEnabled);
            Assert.AreEqual(0.7f, SettingsStore.SoundVolume, 0.0001f);
            Assert.AreEqual(7, SettingsStore.GraceSeconds);
            Assert.IsFalse(SettingsStore.TutorialCompleted);
            Assert.IsFalse(SettingsStore.HasWindowPosition);
        }

        [Test]
        public void 세션_설정은_저장_후_그대로_복원된다()
        {
            var config = new SessionConfig
            {
                FocusMinutes = 45, ShortBreakMinutes = 10, LongBreakMinutes = 20, TotalSets = 6,
                TaskText = "저장되지 않는 값",
            };
            SettingsStore.SaveSessionConfig(config);

            SessionConfig loaded = SettingsStore.LoadSessionConfig();
            Assert.AreEqual(45, loaded.FocusMinutes);
            Assert.AreEqual(10, loaded.ShortBreakMinutes);
            Assert.AreEqual(20, loaded.LongBreakMinutes);
            Assert.AreEqual(6, loaded.TotalSets);
            Assert.AreEqual(string.Empty, loaded.TaskText, "태스크 텍스트는 세션마다 새로 쓴다");
        }

        [Test]
        public void 시스템_설정은_저장_후_복원되고_범위가_보정된다()
        {
            SettingsStore.AlwaysOnTop = false;
            SettingsStore.SoundEnabled = false;
            SettingsStore.SoundVolume = 1.5f;
            SettingsStore.GraceSeconds = 100;
            SettingsStore.TutorialCompleted = true;
            SettingsStore.SaveWindowPosition(120, 40);

            Assert.IsFalse(SettingsStore.AlwaysOnTop);
            Assert.IsFalse(SettingsStore.SoundEnabled);
            Assert.AreEqual(1f, SettingsStore.SoundVolume, 0.0001f);
            Assert.AreEqual(SettingsStore.MaxGraceSeconds, SettingsStore.GraceSeconds);
            Assert.IsTrue(SettingsStore.TutorialCompleted);
            Assert.IsTrue(SettingsStore.HasWindowPosition);
            Assert.AreEqual(new UnityEngine.Vector2Int(120, 40), SettingsStore.LoadWindowPosition());

            SettingsStore.ClearWindowPosition();
            Assert.IsFalse(SettingsStore.HasWindowPosition);
        }

        [Test]
        public void 키_문자열은_사양서_7장과_같다()
        {
            Assert.AreEqual("session.focusMinutes", SettingsStore.Keys.FocusMinutes);
            Assert.AreEqual("session.shortBreakMinutes", SettingsStore.Keys.ShortBreakMinutes);
            Assert.AreEqual("session.longBreakMinutes", SettingsStore.Keys.LongBreakMinutes);
            Assert.AreEqual("session.totalSets", SettingsStore.Keys.TotalSets);
            Assert.AreEqual("window.posX", SettingsStore.Keys.WindowPosX);
            Assert.AreEqual("window.posY", SettingsStore.Keys.WindowPosY);
            Assert.AreEqual("window.alwaysOnTop", SettingsStore.Keys.WindowAlwaysOnTop);
            Assert.AreEqual("sound.enabled", SettingsStore.Keys.SoundEnabled);
            Assert.AreEqual("sound.volume", SettingsStore.Keys.SoundVolume);
            Assert.AreEqual("distraction.graceSeconds", SettingsStore.Keys.DistractionGraceSeconds);
            Assert.AreEqual("tutorial.completed", SettingsStore.Keys.TutorialCompleted);
        }
    }
}
