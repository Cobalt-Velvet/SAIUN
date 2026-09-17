using System.Collections.Generic;
using System.IO;
using _SAIUN.Scripts.Data;
using NUnit.Framework;
using UnityEngine;

namespace _SAIUN.Tests
{
    /// <summary>P1-04 수용 조건: 재시작 후에도 세션 기록 조회, DB 파일이 지정 경로에 생성.</summary>
    public class SaiunDatabaseTests
    {
        private string _dbPath;
        private GameObject _go;
        private SaiunDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Application.temporaryCachePath, $"saiun_test_{System.Guid.NewGuid():N}.db");
            SaiunDatabase.PathOverride = _dbPath;
            _go = new GameObject("DatabaseTest");
            _db = _go.AddComponent<SaiunDatabase>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            SaiunDatabase.PathOverride = null;
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void 기본_경로는_persistentDataPath_아래_saiun_db다()
        {
            SaiunDatabase.PathOverride = null;
            string expected = Path.Combine(Application.persistentDataPath, SaiunDatabase.FileName);
            Assert.AreEqual(expected, SaiunDatabase.DatabasePath);
            Assert.AreEqual("saiun.db", SaiunDatabase.FileName);
        }

        [Test]
        public void 열면_지정_경로에_파일이_생긴다()
        {
            Assert.IsTrue(_db.IsOpen);
            Assert.IsTrue(File.Exists(_dbPath));
        }

        [Test]
        public void 세션을_기록하면_Id가_부여된다()
        {
            int id = _db.InsertSession(NewSession(SessionRecord.ResultHarvested));
            Assert.Greater(id, 0);
            Assert.AreEqual(1, _db.GetSessionCount());
        }

        [Test]
        public void 닫았다가_다시_열어도_이전_세션이_조회된다()
        {
            _db.InsertSession(NewSession(SessionRecord.ResultHarvested, setsCompleted: 4));
            _db.InsertSession(NewSession(SessionRecord.ResultFailed, setsCompleted: 1));
            _db.Close();
            Assert.IsFalse(_db.IsOpen);

            // 앱 재시작을 흉내 낸다: 새 컴포넌트가 같은 파일을 연다.
            Object.DestroyImmediate(_go);
            _go = new GameObject("DatabaseTest2");
            _db = _go.AddComponent<SaiunDatabase>();

            List<SessionRecord> sessions = _db.GetRecentSessions(10);
            Assert.AreEqual(2, sessions.Count);
            Assert.AreEqual(SessionRecord.ResultFailed, sessions[0].Result, "최근 것이 먼저");
            Assert.AreEqual(SessionRecord.ResultHarvested, sessions[1].Result);
            Assert.AreEqual(4, sessions[1].SetsCompleted);
        }

        [Test]
        public void 누적_집중_시간은_완료_세트만_센다()
        {
            _db.InsertSession(NewSession(SessionRecord.ResultHarvested, durationMin: 25, setsCompleted: 4));
            _db.InsertSession(NewSession(SessionRecord.ResultFailed, durationMin: 45, setsCompleted: 1));

            Assert.AreEqual(25 * 4 + 45 * 1, _db.GetTotalFocusMinutes());
        }

        [Test]
        public void 수확하면_기록과_보유량이_함께_늘어난다()
        {
            int sessionId = _db.InsertSession(NewSession(SessionRecord.ResultHarvested));
            _db.AddHarvest(sessionId, "rice");
            _db.AddHarvest(sessionId, "rice");
            _db.AddHarvest(sessionId, "wheat");

            Assert.AreEqual(3, _db.GetHarvestCount());

            List<InventoryRecord> inventory = _db.GetInventory();
            Assert.AreEqual(2, inventory.Count);
            Assert.AreEqual(2, inventory.Find(i => i.CropType == "rice").Quantity);
            Assert.AreEqual(1, inventory.Find(i => i.CropType == "wheat").Quantity);
        }

        [Test]
        public void 해금은_한_번만_기록된다()
        {
            Assert.IsFalse(_db.IsUnlocked("tomato"));
            _db.Unlock("tomato");
            _db.Unlock("tomato");

            Assert.IsTrue(_db.IsUnlocked("tomato"));
            Assert.AreEqual(1, _db.GetUnlocks().Count);
        }

        [Test]
        public void 전체_삭제_후에는_모든_테이블이_비어_있다()
        {
            int sessionId = _db.InsertSession(NewSession(SessionRecord.ResultHarvested));
            _db.AddHarvest(sessionId, "rice");
            _db.Unlock("potato");

            _db.DeleteAllRows();

            Assert.AreEqual(0, _db.GetSessionCount());
            Assert.AreEqual(0, _db.GetHarvestCount());
            Assert.AreEqual(0, _db.GetInventory().Count);
            Assert.AreEqual(0, _db.GetUnlocks().Count);
        }

        [Test]
        public void 시각_형식은_ISO8601_라운드트립이다()
        {
            string now = SaiunDatabase.Now();
            Assert.DoesNotThrow(() => System.DateTime.Parse(now, null, System.Globalization.DateTimeStyles.RoundtripKind));
        }

        private static SessionRecord NewSession(string result, int durationMin = 25, int setsCompleted = 4)
        {
            return new SessionRecord
            {
                StartTime = SaiunDatabase.Now(),
                DurationMin = durationMin,
                SetsCompleted = setsCompleted,
                CropType = "rice",
                Result = result,
            };
        }
    }
}
