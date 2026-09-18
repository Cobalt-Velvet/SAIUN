using System;
using System.Collections;
using System.IO;
using System.Linq;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using _SAIUN.Scripts.Timer;
using _SAIUN.Scripts.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P4-04: 시작을 누르면 세션 설정 패널이 뜨고 고른 값으로 시작한다.
    /// 기어로 여는 시스템 설정에서 방해 앱 목록·유예·창·사운드·데이터를 다룬다.
    /// SaiunSceneBuilder가 조립한 메인 씬을 그대로 불러와 실제 버튼을 누른다.
    /// DB·방해 앱 파일은 임시 경로로 돌리고, PlayerPrefs는 앱 키만 지운다.
    /// </summary>
    public class SettingsUiTests
    {
        private const string SceneName = "Main";

        private string _dbPath;
        private string _blacklistPath;
        private Scene _scene;
        private GameManager _gm;
        private BottomBarView _bar;
        private SessionPanelView _panel;
        private SettingsScreenView _settings;
        private double _now;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dbPath = Path.Combine(Application.temporaryCachePath, $"saiun_settings_{Guid.NewGuid():N}.db");
            _blacklistPath = Path.Combine(Application.temporaryCachePath, $"blacklist_settings_{Guid.NewGuid():N}.json");
            SaiunDatabase.PathOverride = _dbPath;
            BlacklistStore.PathOverride = _blacklistPath;
            SettingsStore.DeleteAll();

            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByName(SceneName);
            yield return null;   // Start까지 돌게 한 프레임 더 기다린다

            _gm = Find<GameManager>();
            _bar = Find<BottomBarView>();
            _panel = Find<SessionPanelView>();
            _settings = Find<SettingsScreenView>();
            _now = 1000d;
            _gm.Timer.SetClock(() => _now);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid()) yield return SceneManager.UnloadSceneAsync(_scene);
            SaiunDatabase.PathOverride = null;
            BlacklistStore.PathOverride = null;
            SettingsStore.DeleteAll();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_blacklistPath)) File.Delete(_blacklistPath);
        }

        // ---- 세션 설정 패널 (12-1) ----

        [Test]
        public void 시작을_누르면_패널이_뜨고_한_번_더_누르면_고른_값으로_시작한다()
        {
            Assert.IsFalse(_panel.IsOpen);
            _bar.PrimaryButton.onClick.Invoke();

            Assert.IsTrue(_panel.IsOpen);
            Assert.AreEqual(PomodoroState.Idle, _gm.StateMachine.CurrentState, "패널만 열고 시작하지 않는다");
            Assert.AreEqual("집중 시작", _bar.CurrentButtonLabel);

            Field("focusField").Slider.value = 30;
            Field("setsField").Input.onEndEdit.Invoke("6");
            _bar.PrimaryButton.onClick.Invoke();

            Assert.IsFalse(_panel.IsOpen);
            Assert.AreEqual(PomodoroState.Focus, _gm.StateMachine.CurrentState);
            Assert.AreEqual(30, _gm.Timer.Config.FocusMinutes);
            Assert.AreEqual(6, _gm.Timer.Config.TotalSets);
            Assert.AreEqual(30, SettingsStore.LoadSessionConfig().FocusMinutes, "마지막 값을 저장한다");
        }

        [Test]
        public void 닫으면_시작하지_않고_라벨이_돌아온다()
        {
            _bar.PrimaryButton.onClick.Invoke();
            Button(_panel, "closeButton").onClick.Invoke();

            Assert.IsFalse(_panel.IsOpen);
            Assert.AreEqual("시작", _bar.CurrentButtonLabel);
            Assert.AreEqual(PomodoroState.Idle, _gm.StateMachine.CurrentState);
        }

        [Test]
        public void 범위_밖_숫자는_경계로_잘린다()
        {
            _bar.PrimaryButton.onClick.Invoke();
            SliderField focus = Field("focusField");

            focus.Input.onEndEdit.Invoke("500");
            Assert.AreEqual(SessionConfig.MaxFocusMinutes, _panel.Draft.FocusMinutes);
            Assert.AreEqual("90", focus.Input.text);

            focus.Input.onEndEdit.Invoke("abc");
            Assert.AreEqual(SessionConfig.MaxFocusMinutes, focus.Value, "숫자가 아니면 원래 값을 지킨다");
        }

        [Test]
        public void 잠긴_작물은_누를_수_없고_기본_작물은_골라서_심는다()
        {
            _bar.PrimaryButton.onClick.Invoke();

            Assert.IsTrue(CropButton("rice").interactable);
            Assert.IsTrue(CropButton("wheat").interactable);
            Assert.IsFalse(CropButton("tomato").interactable, "누적 집중 10시간 전에는 잠겨 있다");
            Assert.IsFalse(CropButton("potato").interactable);

            CropButton("wheat").onClick.Invoke();
            _bar.PrimaryButton.onClick.Invoke();
            Assert.AreEqual("wheat", _gm.Timer.Config.CropType);
        }

        [Test]
        public void 해금된_작물을_고르면_필요_설정까지_올리고_낮추면_대체_작물을_알린다()
        {
            _gm.Database.Unlock("crop.tomato");
            _bar.PrimaryButton.onClick.Invoke();

            CropButton("tomato").onClick.Invoke();
            Assert.AreEqual(45, _panel.Draft.FocusMinutes, "토마토는 45분 × 4세트부터");
            Assert.AreEqual(45, Field("focusField").Value);

            Field("focusField").Slider.value = 30;
            StringAssert.Contains("쌀", HintText(), "설정이 모자라면 쌀을 심는다고 알린다");
        }

        // ---- 시스템 설정 (12-2) ----

        [Test]
        public void 기어를_누르면_설정이_열리고_닫기로_닫힌다()
        {
            Button(_settings, "openButton").onClick.Invoke();
            Assert.IsTrue(_settings.IsOpen);

            Button(_settings, "closeButton").onClick.Invoke();
            Assert.IsFalse(_settings.IsOpen);
        }

        [UnityTest]
        public IEnumerator 블랙리스트에_추가하고_지울_수_있다()
        {
            OpenSettings();
            BlacklistStore lists = _gm.Watcher.Blacklist;

            TMP_InputField input = Get<TMP_InputField>(_settings, "blacklistInput");
            input.text = "Discord";
            Button(_settings, "blacklistAddButton").onClick.Invoke();

            Assert.IsTrue(lists.IsBlacklisted("discord.exe"));
            Assert.Contains("Discord.exe", lists.Blacklist.ToList(), "실행 파일 이름으로 남긴다");
            Assert.AreEqual(string.Empty, input.text, "추가하면 입력칸을 비운다");
            yield return null;

            Transform row = Get<RectTransform>(_settings, "blacklistRows").Find("Row_Discord.exe");
            Assert.IsNotNull(row);
            row.Find("Remove").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(lists.IsBlacklisted("discord.exe"));
        }

        [Test]
        public void 화이트리스트에_추가하면_방해_앱에서_빠진다()
        {
            OpenSettings();
            _settings.AddToWhitelist("chrome");
            Assert.IsFalse(_gm.Watcher.Blacklist.IsDistracting("chrome.exe"));
        }

        [Test]
        public void 유예_시간은_5에서_30초이고_감시자에_바로_반영된다()
        {
            OpenSettings();
            SliderField grace = Field(_settings, "graceField");
            Assert.AreEqual(SettingsStore.DefaultGraceSeconds, grace.Value);

            grace.Slider.value = 12;
            Assert.AreEqual(12, SettingsStore.GraceSeconds);
            Assert.AreEqual(12, _gm.Watcher.GraceSeconds);

            grace.Input.onEndEdit.Invoke("99");
            Assert.AreEqual(SettingsStore.MaxGraceSeconds, SettingsStore.GraceSeconds);
        }

        [Test]
        public void 창과_사운드_설정이_저장된다()
        {
            OpenSettings();

            Get<Toggle>(_settings, "alwaysOnTopToggle").isOn = false;
            Assert.IsFalse(SettingsStore.AlwaysOnTop);

            Get<Toggle>(_settings, "soundToggle").isOn = false;
            Assert.IsFalse(SettingsStore.SoundEnabled);

            Field(_settings, "volumeField").Slider.value = 30;
            Assert.AreEqual(0.3f, SettingsStore.SoundVolume, 0.0001f);
        }

        [Test]
        public void 데이터_항목에_누적_기록이_보인다()
        {
            _gm.Database.InsertSession(new SessionRecord
            {
                StartTime = SaiunDatabase.Now(), DurationMin = 25, SetsCompleted = 5,
                CropType = "rice", Result = SessionRecord.ResultHarvested,
            });
            _gm.Database.AddHarvest(1, "rice");
            _gm.Database.AddHarvest(1, "rice");

            OpenSettings();

            Assert.AreEqual("누적 집중 2시간 5분", Get<TMP_Text>(_settings, "focusTimeText").text);
            Assert.AreEqual("총 수확 2회", Get<TMP_Text>(_settings, "harvestCountText").text);
            Assert.AreEqual("쌀 2", Get<TMP_Text>(_settings, "inventoryText").text);
        }

        [Test]
        public void 초기화는_두_번_확인한_뒤에야_지운다()
        {
            _gm.Database.AddHarvest(0, "rice");
            OpenSettings();
            Button reset = Button(_settings, "resetDataButton");

            reset.onClick.Invoke();
            reset.onClick.Invoke();
            Assert.AreEqual(2, _settings.ResetStep);
            Assert.AreEqual(1, _gm.Database.GetHarvestCount(), "확인 중에는 지우지 않는다");

            reset.onClick.Invoke();
            Assert.AreEqual(0, _gm.Database.GetHarvestCount());
            Assert.AreEqual("초기화했어요", Get<TMP_Text>(_settings, "resetDataLabel").text);
            Assert.AreEqual("보유한 수확물이 없어요", Get<TMP_Text>(_settings, "inventoryText").text);
        }

        [UnityTest]
        public IEnumerator 확인_단계는_시간이_지나면_처음으로_돌아간다()
        {
            OpenSettings();
            float clock = 100f;
            _settings.SetClock(() => clock);

            Button(_settings, "resetDataButton").onClick.Invoke();
            Assert.AreEqual(1, _settings.ResetStep);

            clock += 10f;
            yield return null;   // Update에서 되돌린다
            Assert.AreEqual(0, _settings.ResetStep);
        }

        [Test]
        public void 세션_중에는_초기화하지_않는다()
        {
            _gm.Database.AddHarvest(0, "rice");
            _gm.RequestStart();
            OpenSettings();

            Button reset = Button(_settings, "resetDataButton");
            reset.onClick.Invoke();
            reset.onClick.Invoke();
            reset.onClick.Invoke();

            Assert.AreEqual(1, _gm.Database.GetHarvestCount());
            Assert.AreEqual("세션이 끝난 뒤에 할 수 있어요", Get<TMP_Text>(_settings, "resetDataLabel").text);
        }

        [Test]
        public void 튜토리얼_다시_보기는_완료_표시를_지우고_요청을_알린다()
        {
            SettingsStore.TutorialCompleted = true;
            bool requested = false;
            _gm.OnTutorialRequested += () => requested = true;
            OpenSettings();

            Button(_settings, "tutorialButton").onClick.Invoke();

            Assert.IsTrue(requested);
            Assert.IsFalse(SettingsStore.TutorialCompleted);
            Assert.IsFalse(_settings.IsOpen, "튜토리얼이 보이도록 설정을 닫는다");
        }

        // ---- 도우미 ----

        private void OpenSettings()
        {
            Button(_settings, "openButton").onClick.Invoke();
            Assert.IsTrue(_settings.IsOpen);
        }

        private Button CropButton(string id)
        {
            Transform grid = Get<RectTransform>(_panel, "cropGrid");
            Transform child = grid.Find($"Crop_{id}");
            Assert.IsNotNull(child, $"작물 버튼 {id}이(가) 없다");
            return child.GetComponent<Button>();
        }

        private string HintText() => Get<TMP_Text>(_panel, "hintText").text;

        private SliderField Field(string name) => Get<SliderField>(_panel, name);

        private static SliderField Field(object owner, string name) => Get<SliderField>(owner, name);

        private static Button Button(object owner, string name) => Get<Button>(owner, name);

        private static T Get<T>(object owner, string field) where T : class
        {
            var value = owner.GetType()
                .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(owner) as T;
            Assert.IsNotNull(value, $"{owner.GetType().Name}.{field}이(가) 연결되지 않았다. Setup Main Scene을 다시 실행하세요.");
            return value;
        }

        private T Find<T>() where T : Component
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            Assert.Fail($"메인 씬에 {typeof(T).Name}이(가) 없다");
            return null;
        }
    }
}
