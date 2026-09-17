using System.IO;
using System.Text.RegularExpressions;
using _SAIUN.Scripts.Distraction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace _SAIUN.Tests
{
    /// <summary>P4-01 BlacklistStore: 기본 파일 생성, 파싱 실패 복구, 정규화 비교, 저장·복원.</summary>
    public class BlacklistStoreTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Application.temporaryCachePath, $"blacklist_{System.Guid.NewGuid():N}.json");
            BlacklistStore.PathOverride = _path;
        }

        [TearDown]
        public void TearDown()
        {
            BlacklistStore.PathOverride = null;
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Test]
        public void 기본_경로는_persistentDataPath_아래_blacklist_json이다()
        {
            BlacklistStore.PathOverride = null;
            Assert.AreEqual(Path.Combine(Application.persistentDataPath, "blacklist.json"), BlacklistStore.FilePath);
        }

        [Test]
        public void 파일이_없으면_기본_블랙리스트로_새로_만든다()
        {
            var store = new BlacklistStore();
            store.Load();

            Assert.IsTrue(File.Exists(_path));
            CollectionAssert.AreEquivalent(new[] { "chrome.exe", "steam.exe" }, store.Blacklist);
            Assert.AreEqual(0, store.Whitelist.Count);

            string json = File.ReadAllText(_path);
            StringAssert.Contains("\"blacklist\"", json);
            StringAssert.Contains("\"whitelist\"", json);
        }

        [Test]
        public void 사양서_형식의_파일을_읽는다()
        {
            File.WriteAllText(_path, "{\"blacklist\":[\"chrome.exe\",\"steam.exe\"],\"whitelist\":[\"Code.exe\"]}");
            var store = new BlacklistStore();
            store.Load();

            Assert.IsTrue(store.IsBlacklisted("chrome.exe"));
            Assert.IsTrue(store.IsWhitelisted("Code.exe"));
        }

        [Test]
        public void 파싱에_실패하면_경고를_남기고_기본값으로_되돌린다()
        {
            File.WriteAllText(_path, "{ this is not json");
            var store = new BlacklistStore();

            LogAssert.Expect(LogType.Warning, new Regex("파싱 실패"));
            store.Load();

            CollectionAssert.AreEquivalent(new[] { "chrome.exe", "steam.exe" }, store.Blacklist);
            StringAssert.Contains("chrome.exe", File.ReadAllText(_path), "복구된 기본값이 저장된다");
        }

        [Test]
        public void 비교는_대소문자와_exe_확장자를_무시한다()
        {
            var store = new BlacklistStore();
            store.Load();

            Assert.IsTrue(store.IsBlacklisted("chrome"));
            Assert.IsTrue(store.IsBlacklisted("Chrome.EXE"));
            Assert.IsTrue(store.IsBlacklisted(" steam "));
            Assert.IsFalse(store.IsBlacklisted("notepad"));
            Assert.IsFalse(store.IsBlacklisted(null));
            Assert.IsFalse(store.IsBlacklisted(""));
        }

        [Test]
        public void 화이트리스트가_블랙리스트보다_우선한다()
        {
            var store = new BlacklistStore();
            store.Load();
            store.AddToWhitelist("chrome.exe");

            Assert.IsTrue(store.IsBlacklisted("chrome"));
            Assert.IsFalse(store.IsDistracting("chrome"));
            Assert.IsTrue(store.IsDistracting("steam"));
        }

        [Test]
        public void 추가_삭제는_즉시_저장되고_다시_읽힌다()
        {
            var store = new BlacklistStore();
            store.Load();
            Assert.IsTrue(store.AddToBlacklist("Discord.exe"));
            Assert.IsFalse(store.AddToBlacklist("discord"), "중복은 추가하지 않는다");
            Assert.IsTrue(store.RemoveFromBlacklist("STEAM.exe"));
            Assert.IsFalse(store.RemoveFromBlacklist("steam"));
            Assert.IsTrue(store.AddToWhitelist("Code.exe"));

            var reloaded = new BlacklistStore();
            reloaded.Load();
            CollectionAssert.AreEquivalent(new[] { "chrome.exe", "Discord.exe" }, reloaded.Blacklist);
            CollectionAssert.AreEquivalent(new[] { "Code.exe" }, reloaded.Whitelist);
        }

        [Test]
        public void 정규화_규칙()
        {
            Assert.AreEqual("chrome", BlacklistStore.Normalize("Chrome.exe"));
            Assert.AreEqual("chrome", BlacklistStore.Normalize("  chrome  "));
            Assert.AreEqual("code", BlacklistStore.Normalize("Code"));
            Assert.AreEqual(string.Empty, BlacklistStore.Normalize(null));
        }
    }
}
