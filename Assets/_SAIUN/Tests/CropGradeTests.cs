using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using NUnit.Framework;

namespace _SAIUN.Tests
{
    /// <summary>P2-04: 사양서 v1.1 7-2 등급 표대로 판정된다.</summary>
    public class CropGradeTests
    {
        [TestCase(25, 4, CropGrade.Normal)]
        [TestCase(45, 4, CropGrade.Rare)]
        [TestCase(25, 8, CropGrade.Advanced)]
        [TestCase(45, 8, CropGrade.Rare)]
        [TestCase(60, 8, CropGrade.Legend)]
        [TestCase(90, 12, CropGrade.Legend)]
        [TestCase(60, 6, CropGrade.Rare)]
        [TestCase(5, 12, CropGrade.Normal)]
        [TestCase(59, 8, CropGrade.Rare)]
        public void 집중시간과_세트수로_등급이_정해진다(int focusMinutes, int sets, CropGrade expected)
        {
            Assert.AreEqual(expected, CropGradeRule.GetCropGrade(focusMinutes, sets));
        }

        [Test]
        public void 세션_설정으로도_판정된다()
        {
            var config = new SessionConfig { FocusMinutes = 60, TotalSets = 8 };
            Assert.AreEqual(CropGrade.Legend, CropGradeRule.GetCropGrade(config));
            Assert.AreEqual(CropGrade.Normal, CropGradeRule.GetCropGrade((SessionConfig)null));
        }
    }
}
