using System.IO;
using System.Reflection;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Timer;
using _SAIUN.Scripts.UI;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>P5-01: 다섯 이벤트에 맞는 효과음을 시스템 설정의 켜기·볼륨으로 재생한다.</summary>
    public class SoundViewTests
    {
        private string _dbPath;
        private GameObject _gmGo;
        private GameObject _soundGo;
        private PomodoroStateMachine _sm;
        private PomodoroTimer _timer;
        private GameManager _gm;
        private SoundView _view;
        private AudioClip _complete, _transition, _harvest, _warning, _death;
        private CropCatalog _catalog;
        private CropDefinition _rice;
        private double _now;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Application.temporaryCachePath, $"saiun_sound_{System.Guid.NewGuid():N}.db");
            SaiunDatabase.PathOverride = _dbPath;
            SettingsStore.DeleteAll();

            _gmGo = new GameObject("GameManager");
            _sm = _gmGo.AddComponent<PomodoroStateMachine>();
            _timer = _gmGo.AddComponent<PomodoroTimer>();
            _gmGo.AddComponent<SaiunDatabase>();
            _gm = _gmGo.AddComponent<GameManager>();
            _now = 100d;
            _timer.SetClock(() => _now);

            // 수확 이벤트는 작물 목록이 있어야 기록된다.
            _rice = ScriptableObject.CreateInstance<CropDefinition>();
            _rice.Configure("rice", "rice", new GameObject[CropDefinition.StageModelCount]);
            _catalog = ScriptableObject.CreateInstance<CropCatalog>();
            _catalog.Configure(new[] { _rice });
            SetField(typeof(GameManager), _gm, "cropCatalog", _catalog);

            _soundGo = new GameObject("Sound");
            _soundGo.SetActive(false);
            _soundGo.AddComponent<AudioSource>();
            _view = _soundGo.AddComponent<SoundView>();
            SetField(typeof(SoundView), _view, "gameManager", _gm);
            _complete = Clip("complete", "completeClip");
            _transition = Clip("transition", "transitionClip");
            _harvest = Clip("harvest", "harvestClip");
            _warning = Clip("warning", "warningClip");
            _death = Clip("death", "deathClip");
            _soundGo.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_soundGo);
            Object.DestroyImmediate(_gmGo);
            foreach (Object owned in new Object[] { _complete, _transition, _harvest, _warning, _death, _catalog, _rice })
            {
                Object.DestroyImmediate(owned);
            }
            SaiunDatabase.PathOverride = null;
            SettingsStore.DeleteAll();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void 세트_전환과_포모도로_완료와_수확에_맞는_소리를_낸다()
        {
            Start(sets: 2);
            Assert.AreEqual(0, _view.PlayCount, "집중 시작에는 소리가 없다");

            Advance(300);   // → ShortBreak
            Assert.AreSame(_transition, _view.LastPlayedClip);

            Advance(60);    // → Focus
            Advance(300);   // → LongBreak
            Assert.AreSame(_complete, _view.LastPlayedClip);

            Advance(300);   // → Idle, 수확
            Assert.AreSame(_harvest, _view.LastPlayedClip);
            Assert.AreEqual(3, _view.PlayCount);
        }

        [Test]
        public void 방해_앱_경고와_작물_사망에_맞는_소리를_낸다()
        {
            Start(sets: 2);
            _sm.ChangeState(PomodoroState.Interrupted);
            Assert.AreSame(_warning, _view.LastPlayedClip);

            _sm.ChangeState(PomodoroState.Failed);
            Assert.AreSame(_death, _view.LastPlayedClip);
        }

        [Test]
        public void 기본_볼륨은_70퍼센트다()
        {
            Start(sets: 1);
            Advance(300);
            Assert.AreEqual(0.7f, _view.LastPlayedVolume, 0.0001f);
        }

        [Test]
        public void 설정한_볼륨으로_재생한다()
        {
            SettingsStore.SoundVolume = 0.25f;
            Start(sets: 1);
            Advance(300);
            Assert.AreEqual(0.25f, _view.LastPlayedVolume, 0.0001f);
        }

        [Test]
        public void 효과음을_끄면_재생하지_않는다()
        {
            SettingsStore.SoundEnabled = false;
            Start(sets: 1);
            Advance(300);
            _sm.ChangeState(PomodoroState.Idle);

            Assert.AreEqual(0, _view.PlayCount);
            Assert.IsNull(_view.LastPlayedClip);
        }

        // ---- 도우미 ----

        private void Start(int sets)
        {
            _gm.RequestStart(new SessionConfig { FocusMinutes = 5, ShortBreakMinutes = 1, LongBreakMinutes = 5, TotalSets = sets });
        }

        private void Advance(double seconds)
        {
            _now += seconds;
            _timer.Tick();
        }

        private AudioClip Clip(string name, string field)
        {
            AudioClip clip = AudioClip.Create(name, 441, 1, 44100, false);
            SetField(typeof(SoundView), _view, field, clip);
            return clip;
        }

        private static void SetField(System.Type type, object target, string field, object value)
        {
            type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }
    }
}
