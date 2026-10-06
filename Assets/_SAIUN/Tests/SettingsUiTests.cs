using System.Collections;
using System.Linq;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using _SAIUN.Scripts.UI;
using _SAIUN.Scripts.Weather;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P4-04: 시작을 누르면 세션 설정 패널이 뜨고 고른 값으로 시작한다.
    /// 기어로 여는 시스템 설정에서 방해 앱 목록·유예·창·사운드·데이터를 다룬다.
    /// </summary>
    public class SettingsUiTests : MainSceneTestBase
    {
        private BottomBarView _bar;
        private SessionPanelView _panel;
        private SettingsScreenView _settings;

        protected override void OnSceneLoaded()
        {
            _bar = Find<BottomBarView>();
            _panel = Find<SessionPanelView>();
            _settings = Find<SettingsScreenView>();
        }

        // ---- 세션 설정 패널 (12-1) ----

        [Test]
        public void 시작을_누르면_패널이_뜨고_한_번_더_누르면_고른_값으로_시작한다()
        {
            Assert.IsFalse(_panel.IsOpen);
            _bar.PrimaryButton.onClick.Invoke();

            Assert.IsTrue(_panel.IsOpen);
            Assert.AreEqual(PomodoroState.Idle, Gm.StateMachine.CurrentState, "패널만 열고 시작하지 않는다");
            Assert.AreEqual("집중 시작", _bar.CurrentButtonLabel);

            Field("focusField").Slider.value = 30;
            Field("setsField").Input.onEndEdit.Invoke("6");
            _bar.PrimaryButton.onClick.Invoke();

            Assert.IsFalse(_panel.IsOpen);
            Assert.AreEqual(PomodoroState.Focus, Gm.StateMachine.CurrentState);
            Assert.AreEqual(30, Gm.Timer.Config.FocusMinutes);
            Assert.AreEqual(6, Gm.Timer.Config.TotalSets);
            Assert.AreEqual(30, SettingsStore.LoadSessionConfig().FocusMinutes, "마지막 값을 저장한다");
        }

        [Test]
        public void 닫으면_시작하지_않고_라벨이_돌아온다()
        {
            _bar.PrimaryButton.onClick.Invoke();
            Button(_panel, "closeButton").onClick.Invoke();

            Assert.IsFalse(_panel.IsOpen);
            Assert.AreEqual("시작", _bar.CurrentButtonLabel);
            Assert.AreEqual(PomodoroState.Idle, Gm.StateMachine.CurrentState);
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
            Assert.AreEqual("wheat", Gm.Timer.Config.CropType);
        }

        [Test]
        public void 해금된_작물을_고르면_필요_설정까지_올리고_낮추면_대체_작물을_알린다()
        {
            Gm.Database.Unlock("crop.tomato");
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
            BlacklistStore lists = Gm.Watcher.Blacklist;

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
            Assert.IsFalse(Gm.Watcher.Blacklist.IsDistracting("chrome.exe"));
        }

        [Test]
        public void 유예_시간은_5에서_30초이고_감시자에_바로_반영된다()
        {
            OpenSettings();
            SliderField grace = Field(_settings, "graceField");
            Assert.AreEqual(SettingsStore.DefaultGraceSeconds, grace.Value);

            grace.Slider.value = 12;
            Assert.AreEqual(12, SettingsStore.GraceSeconds);
            Assert.AreEqual(12, Gm.Watcher.GraceSeconds);

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
        public void 사이드바를_켜면_저장되고_창이_세로로_길어지며_하늘과_카메라가_넓어진다()
        {
            OpenSettings();
            var window = Find<WindowController>();
            var sky = Find<SkyView>();
            var rig = Find<ViewRig>();
            Camera eye = Camera.main;
            float cardFov = eye.fieldOfView;
            Assert.IsFalse(Get<Toggle>(_settings, "windowSidebarToggle").isOn, "처음엔 떠 있는 카드다");
            Assert.IsFalse(window.Layout.Sidebar);

            Get<Toggle>(_settings, "windowSidebarToggle").isOn = true;
            Assert.IsTrue(SettingsStore.WindowSidebar);
            Assert.IsTrue(window.Layout.Sidebar);
            Vector2Int logical = window.Layout.Logical;
            Assert.Greater(logical.y, SceneMetrics.WindowHeight, "세로로 길다");
            Assert.Less(logical.x, SceneMetrics.WindowWidth, "폭은 좁다");

            var uiCard = (RectTransform)GameObject.Find("UICanvas").transform.Find("Card");
            Assert.AreEqual(logical.y, uiCard.sizeDelta.y, 0.01f, "UI 카드가 창 높이를 따른다");
            Assert.AreEqual(logical.y, sky.Target.height, "하늘도 창 높이로 그린다");
            Assert.Greater(eye.fieldOfView, cardFov + 10f, "화소당 각도를 지키며 화각이 넓어진다");
            Assert.AreEqual(rig.Current.Focal, sky.View.y, 0.0001f, "하늘과 정원 카메라가 한 눈이다");

            Get<Toggle>(_settings, "windowSidebarToggle").isOn = false;
            Assert.IsFalse(SettingsStore.WindowSidebar);
            Assert.IsFalse(window.Layout.Sidebar);
            Assert.AreEqual(SceneMetrics.WindowHeight, uiCard.sizeDelta.y, 0.01f);
            Assert.AreEqual(cardFov, eye.fieldOfView, 0.01f);
        }

        [Test]
        public void 데이터_항목에_누적_기록이_보인다()
        {
            Gm.Database.InsertSession(new SessionRecord
            {
                StartTime = SaiunDatabase.Now(), DurationMin = 25, SetsCompleted = 5,
                CropType = "rice", Result = SessionRecord.ResultHarvested,
            });
            Gm.Database.AddHarvest(1, "rice");
            Gm.Database.AddHarvest(1, "rice");

            OpenSettings();

            Assert.AreEqual("누적 집중 2시간 5분", Get<TMP_Text>(_settings, "focusTimeText").text);
            Assert.AreEqual("총 수확 2회", Get<TMP_Text>(_settings, "harvestCountText").text);
            Assert.AreEqual("쌀 2", Get<TMP_Text>(_settings, "inventoryText").text);
        }

        [Test]
        public void 초기화는_두_번_확인한_뒤에야_지운다()
        {
            Gm.Database.AddHarvest(0, "rice");
            OpenSettings();
            Button reset = Button(_settings, "resetDataButton");

            reset.onClick.Invoke();
            reset.onClick.Invoke();
            Assert.AreEqual(2, _settings.ResetStep);
            Assert.AreEqual(1, Gm.Database.GetHarvestCount(), "확인 중에는 지우지 않는다");

            reset.onClick.Invoke();
            Assert.AreEqual(0, Gm.Database.GetHarvestCount());
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
            Gm.Database.AddHarvest(0, "rice");
            Gm.RequestStart();
            OpenSettings();

            Button reset = Button(_settings, "resetDataButton");
            reset.onClick.Invoke();
            reset.onClick.Invoke();
            reset.onClick.Invoke();

            Assert.AreEqual(1, Gm.Database.GetHarvestCount());
            Assert.AreEqual("세션이 끝난 뒤에 할 수 있어요", Get<TMP_Text>(_settings, "resetDataLabel").text);
        }

        [Test]
        public void 튜토리얼_다시_보기는_완료_표시를_지우고_요청을_알린다()
        {
            SettingsStore.TutorialCompleted = true;
            bool requested = false;
            Gm.OnTutorialRequested += () => requested = true;
            OpenSettings();

            Button(_settings, "tutorialButton").onClick.Invoke();

            Assert.IsTrue(requested);
            Assert.IsFalse(SettingsStore.TutorialCompleted);
            Assert.IsFalse(_settings.IsOpen, "튜토리얼이 보이도록 설정을 닫는다");
        }

        [Test]
        public void 종료는_확인을_받고_진행_중인_세션을_정지로_기록한_뒤_끈다()
        {
            bool quit = false;
            Gm.OnQuitRequested += () => quit = true;
            Gm.RequestStart();
            OpenSettings();

            Button(_settings, "quitButton").onClick.Invoke();
            MessageDialogView dialog = Get<MessageDialogView>(_settings, "dialog");
            Assert.IsTrue(dialog.IsShowing, "바로 끄지 않고 확인을 받는다");
            Assert.IsFalse(quit);
            StringAssert.Contains("정지", dialog.Body);

            dialog.Close();
            Assert.IsFalse(quit, "취소하면 끄지 않는다");
        }

        [Test]
        public void 종료_요청은_세션을_기록한다()
        {
            // RequestQuit은 에디터 재생을 멈추므로, 기록 순서만 확인하려고 종료 이벤트에서 결과를 본다.
            int sessionsAtQuit = -1;
            Gm.OnQuitRequested += () => sessionsAtQuit = Gm.Database.GetSessionCount();
            Gm.RequestStart();

            QuitWithoutStopping();
            Assert.AreEqual(1, sessionsAtQuit, "끄기 전에 진행 중 세션을 기록한다");
            Assert.AreEqual(PomodoroState.Idle, Gm.StateMachine.CurrentState);
        }

        // RequestQuit 안의 에디터 정지를 피하려고 같은 정리 단계만 밟는다.
        private void QuitWithoutStopping()
        {
            System.Reflection.MethodInfo method = typeof(GameManager).GetMethod("PrepareQuit",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, "PrepareQuit이 없다");
            method.Invoke(Gm, null);
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
    }
}
