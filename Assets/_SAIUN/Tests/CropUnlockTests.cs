using System.Collections.Generic;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Crop;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>P4-03: 작물의 필요 설정·해금 조건 판정과, 세션에 심을 작물 고르기.</summary>
    public class CropUnlockTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private CropDefinition _rice;
        private CropDefinition _wheat;
        private CropDefinition _tomato;
        private CropDefinition _potato;
        private CropCatalog _catalog;
        private readonly HashSet<string> _unlocked = new HashSet<string>();

        [SetUp]
        public void SetUp()
        {
            // 사양서 v1.1 7-3 MVP 표
            _rice = Crop("rice", 25, 4, UnlockCondition.Default, 0);
            _wheat = Crop("wheat", 25, 4, UnlockCondition.Default, 0);
            _tomato = Crop("tomato", 45, 4, UnlockCondition.TotalFocusHours, 10);
            _potato = Crop("potato", 45, 4, UnlockCondition.HarvestCount, 10);

            _catalog = ScriptableObject.CreateInstance<CropCatalog>();
            _owned.Add(_catalog);
            _catalog.Configure(new[] { _rice, _wheat, _tomato, _potato });
            _unlocked.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _owned) Object.DestroyImmediate(owned);
            _owned.Clear();
        }

        // ---- 해금 조건 ----

        [Test]
        public void 기본_작물은_기록이_없어도_해금돼_있다()
        {
            Assert.IsTrue(_rice.IsDefault);
            Assert.IsTrue(_rice.IsUnlockedBy(0, 0));
        }

        [Test]
        public void 토마토는_누적_집중_10시간에_해금된다()
        {
            Assert.IsFalse(_tomato.IsUnlockedBy(599, 1000), "수확 횟수와는 무관하다");
            Assert.IsTrue(_tomato.IsUnlockedBy(600, 0));
        }

        [Test]
        public void 감자는_수확_10회에_해금된다()
        {
            Assert.IsFalse(_potato.IsUnlockedBy(100000, 9), "집중 시간과는 무관하다");
            Assert.IsTrue(_potato.IsUnlockedBy(0, 10));
        }

        [Test]
        public void 해금_아이템_id는_작물_접두어가_붙는다()
        {
            Assert.AreEqual("crop.tomato", _tomato.UnlockItemId);
        }

        [Test]
        public void 필요_설정은_집중_시간과_세트_수를_모두_채워야_한다()
        {
            Assert.IsTrue(_tomato.MeetsRequirement(Config(45, 4, "tomato")));
            Assert.IsTrue(_tomato.MeetsRequirement(Config(60, 8, "tomato")));
            Assert.IsFalse(_tomato.MeetsRequirement(Config(44, 4, "tomato")));
            Assert.IsFalse(_tomato.MeetsRequirement(Config(45, 3, "tomato")));
            Assert.IsFalse(_tomato.MeetsRequirement(null));
        }

        // ---- 작물 고르기 ----

        [Test]
        public void 해금되고_설정을_채운_작물은_그대로_심는다()
        {
            _unlocked.Add("tomato");
            Assert.AreSame(_tomato, _catalog.ChooseFor(Config(45, 4, "tomato"), IsUnlocked));
        }

        [Test]
        public void 잠긴_작물을_고르면_목록의_첫_기본_작물을_심는다()
        {
            Assert.AreSame(_rice, _catalog.ChooseFor(Config(45, 4, "tomato"), IsUnlocked));
        }

        [Test]
        public void 설정이_모자라면_해금된_작물이라도_다른_작물을_심는다()
        {
            _unlocked.Add("tomato");
            Assert.AreSame(_rice, _catalog.ChooseFor(Config(25, 4, "tomato"), IsUnlocked));
        }

        [Test]
        public void 기본_작물은_짧은_세션에도_고른_그대로_심는다()
        {
            Assert.AreSame(_wheat, _catalog.ChooseFor(Config(5, 1, "wheat"), IsUnlocked));
        }

        [Test]
        public void 목록에_없는_작물이면_조건에_맞는_첫_작물이다()
        {
            Assert.AreSame(_rice, _catalog.ChooseFor(Config(25, 4, "mango"), IsUnlocked));
        }

        [Test]
        public void 빈_목록이면_null이다()
        {
            var empty = ScriptableObject.CreateInstance<CropCatalog>();
            _owned.Add(empty);
            Assert.IsNull(empty.ChooseFor(Config(25, 4, "rice"), IsUnlocked));
        }

        // ---- 도우미 ----

        private bool IsUnlocked(CropDefinition crop) => _unlocked.Contains(crop.Id);

        private static SessionConfig Config(int focus, int sets, string crop)
        {
            return new SessionConfig { FocusMinutes = focus, TotalSets = sets, CropType = crop };
        }

        private CropDefinition Crop(string id, int focus, int sets, UnlockCondition condition, int threshold)
        {
            var crop = ScriptableObject.CreateInstance<CropDefinition>();
            _owned.Add(crop);
            crop.Configure(id, id, new GameObject[CropDefinition.StageModelCount]);
            crop.ConfigureRules(focus, sets, condition, threshold);
            return crop;
        }
    }
}
