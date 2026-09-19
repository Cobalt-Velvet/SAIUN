using System.Collections;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P5-03: 첫 실행에만 3단계 튜토리얼이 뜨고, 단계마다 다음·건너뛰기가 있다.
    /// 끝내거나 건너뛰면 다시 띄우지 않으며, 설정의 '다시 보기'로 처음부터 볼 수 있다.
    /// </summary>
    public class TutorialTests : MainSceneTestBase
    {
        private TutorialView _tutorial;

        protected override void OnSceneLoaded()
        {
            _tutorial = Find<TutorialView>();
        }

        [Test]
        public void 첫_실행에는_첫_단계가_뜬다()
        {
            Assert.IsTrue(_tutorial.IsShowing);
            Assert.AreEqual(0, _tutorial.StepIndex);
            Assert.AreEqual(3, _tutorial.StepCount, "사양서 14장은 세 단계다");
            Assert.AreEqual("1 / 3", Get<TMP_Text>(_tutorial, "stepText").text);
            Assert.AreEqual("캐릭터 설정", Get<TMP_Text>(_tutorial, "titleText").text);
        }

        [Test]
        public void 다음으로_세_단계를_지나면_끝나고_완료로_남는다()
        {
            Button next = Get<Button>(_tutorial, "nextButton");
            TMP_Text label = Get<TMP_Text>(_tutorial, "nextLabel");

            Assert.AreEqual("다음", label.text);
            next.onClick.Invoke();
            Assert.AreEqual("작물 심기", Get<TMP_Text>(_tutorial, "titleText").text);
            next.onClick.Invoke();
            Assert.AreEqual("포모도로 시작", Get<TMP_Text>(_tutorial, "titleText").text);
            Assert.AreEqual("시작하기", label.text, "마지막 단계에서는 시작하기");

            next.onClick.Invoke();
            Assert.IsFalse(_tutorial.IsShowing);
            Assert.IsTrue(SettingsStore.TutorialCompleted);
        }

        [Test]
        public void 건너뛰어도_완료로_남는다()
        {
            Get<Button>(_tutorial, "skipButton").onClick.Invoke();

            Assert.IsFalse(_tutorial.IsShowing);
            Assert.IsTrue(SettingsStore.TutorialCompleted);
        }

        [UnityTest]
        public IEnumerator 완료한_뒤_다시_켜면_뜨지_않는다()
        {
            Get<Button>(_tutorial, "skipButton").onClick.Invoke();
            yield return Reload();

            Assert.IsFalse(_tutorial.IsShowing);
        }

        [UnityTest]
        public IEnumerator 다시_보기를_요청하면_처음부터_뜬다()
        {
            Get<Button>(_tutorial, "skipButton").onClick.Invoke();
            yield return Reload();

            Gm.RequestReplayTutorial();
            Assert.IsTrue(_tutorial.IsShowing);
            Assert.AreEqual(0, _tutorial.StepIndex);
        }

        [UnityTest]
        public IEnumerator 단계마다_안내하는_버튼을_테두리로_가리킨다()
        {
            yield return null;   // 레이아웃이 자리 잡도록
            RectTransform highlight = Get<RectTransform>(_tutorial, "highlight");
            RectTransform gear = Find<SettingsScreenView>().transform.Find("Gear") as RectTransform;

            Assert.IsTrue(highlight.gameObject.activeSelf);
            Assert.Less(Vector3.Distance(Center(gear), Center(highlight)), 1f, "1단계는 기어를 가리킨다");

            Get<Button>(_tutorial, "nextButton").onClick.Invoke();
            yield return null;
            var start = (RectTransform)Find<BottomBarView>().PrimaryButton.transform;
            Assert.Less(Vector3.Distance(Center(start), Center(highlight)), 1f, "2단계는 시작 버튼을 가리킨다");
            Assert.Greater(highlight.rect.width, start.rect.width, "버튼보다 조금 크게 두른다(두 캔버스 모두 픽셀 1:1)");
        }

        [Test]
        public void 떠_있는_동안_뒤의_클릭을_막는다()
        {
            GameObject overlay = Get<GameObject>(_tutorial, "overlay");
            Assert.IsTrue(overlay.GetComponent<Image>().raycastTarget);
        }

        // 캔버스가 달라도 비교할 수 있게 화면 좌표로 잰다.
        private static Vector3 Center(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Canvas canvas = rt.GetComponentInParent<Canvas>().rootCanvas;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return (min + max) / 2f;
        }
    }
}
