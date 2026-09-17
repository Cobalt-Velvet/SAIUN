using _SAIUN.Scripts.Core;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>
    /// 팔레트가 지정된 색과 같은지, 그리고 실제로 겹쳐 쓰는 조합의 대비가 충분한지 확인한다.
    /// 대비는 WCAG 상대 휘도 공식을 쓴다. 본문 기준은 4.5, 큰 글자 기준은 3.0이다.
    /// </summary>
    public class SaiunPaletteTests
    {
        private const float BodyTextMinimum = 4.5f;

        [Test]
        public void 팔레트_5색이_지정된_값과_같다()
        {
            AssertHex("FAF3DD", SaiunPalette.Eggshell);
            AssertHex("C8D5B9", SaiunPalette.TeaGreen);
            AssertHex("8FC0A9", SaiunPalette.MutedTeal);
            AssertHex("68B0AB", SaiunPalette.TropicalTeal);
            AssertHex("4A7C59", SaiunPalette.JungleTeal);
        }

        [Test]
        public void 메인_포인트는_사양서가_지정한_민트_계열이다()
        {
            Assert.AreEqual(SaiunPalette.TropicalTeal, SaiunPalette.MainPoint);
        }

        [Test]
        public void 하단_바_글자가_배경_위에서_읽힌다()
        {
            Assert.GreaterOrEqual(
                Contrast(SaiunPalette.BottomBarText, SaiunPalette.DeepJungle), BodyTextMinimum);
        }

        [Test]
        public void 버튼_라벨이_포인트_색_위에서_읽힌다()
        {
            Assert.GreaterOrEqual(
                Contrast(SaiunPalette.OnMainPoint, SaiunPalette.MainPoint), BodyTextMinimum);
        }

        [Test]
        public void 입력_글자가_입력_배경_위에서_읽힌다()
        {
            Assert.GreaterOrEqual(
                Contrast(SaiunPalette.InputText, SaiunPalette.InputBackground), BodyTextMinimum);
        }

        [Test]
        public void 버튼이_하단_바_배경과_구분된다()
        {
            // 도형이라 큰 글자 기준 3.0이면 충분하다.
            Assert.GreaterOrEqual(Contrast(SaiunPalette.MainPoint, SaiunPalette.DeepJungle), 3f);
        }

        [Test]
        public void 남은_도트가_완료_도트보다_눈에_덜_띈다()
        {
            Assert.Less(SaiunPalette.SetDotPending.a, SaiunPalette.SetDotCompleted.a);
        }

        [Test]
        public void 하단_바_배경은_거의_불투명하다()
        {
            // 바탕화면이 비쳐 보이면 글자 대비가 무너진다.
            Assert.GreaterOrEqual(SaiunPalette.BottomBarBackground.a, 0.9f);
        }

        // ---- 도우미 ----

        private static void AssertHex(string expected, Color actual)
        {
            Assert.IsTrue(ColorUtility.TryParseHtmlString("#" + expected, out Color parsed));
            Assert.AreEqual(parsed.r, actual.r, 0.001f, expected);
            Assert.AreEqual(parsed.g, actual.g, 0.001f, expected);
            Assert.AreEqual(parsed.b, actual.b, 0.001f, expected);
        }

        /// <summary>WCAG 대비비. 1.0에서 21.0 사이다.</summary>
        private static float Contrast(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float high = Mathf.Max(la, lb);
            float low = Mathf.Min(la, lb);
            return (high + 0.05f) / (low + 0.05f);
        }

        private static float RelativeLuminance(Color color)
        {
            return 0.2126f * Linear(color.r) + 0.7152f * Linear(color.g) + 0.0722f * Linear(color.b);
        }

        private static float Linear(float channel)
        {
            return channel <= 0.03928f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }
    }
}
