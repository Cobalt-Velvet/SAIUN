using System;
using System.Collections.Generic;
using System.IO;
using SQLite;
using UnityEngine;

namespace _SAIUN.Scripts.Data
{
    /// <summary>
    /// SQLite 연결과 스키마 관리.
    /// 연결은 하나만 유지하고 OnDestroy에서 닫는다.
    /// 파일 경로는 Application.persistentDataPath/saiun.db 로 고정한다.
    /// </summary>
    public class SaiunDatabase : MonoBehaviour
    {
        public const string FileName = "saiun.db";

        /// <summary>DB 파일 전체 경로. 테스트가 PathOverride를 지정하면 그 경로를 쓴다.</summary>
        public static string DatabasePath =>
            string.IsNullOrEmpty(PathOverride) ? Path.Combine(Application.persistentDataPath, FileName) : PathOverride;

        /// <summary>테스트 전용 경로 덮어쓰기. null이면 기본 경로.</summary>
        internal static string PathOverride;

        public bool IsOpen => _connection != null;

        private SQLiteConnection _connection;

        // ---- 수명 주기 ----

        private void Awake()
        {
            Open();
        }

        private void OnDestroy()
        {
            Close();
        }

        // ---- 연결 ----

        /// <summary>연결을 열고 테이블이 없으면 만든다. 이미 열려 있으면 아무 것도 하지 않는다.</summary>
        public void Open()
        {
            if (IsOpen) return;

            try
            {
                _connection = new SQLiteConnection(
                    DatabasePath,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);

                _connection.CreateTable<SessionRecord>();
                _connection.CreateTable<HarvestRecord>();
                _connection.CreateTable<InventoryRecord>();
            }
            catch (Exception e)
            {
                Debug.LogError($"SaiunDatabase: 연결 실패 ({DatabasePath})\n{e}");
                _connection = null;
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            _connection.Close();
            _connection.Dispose();
            _connection = null;
        }

        // ---- sessions ----

        /// <summary>세션 1건을 기록하고 부여된 Id를 돌려준다. 실패하면 0.</summary>
        public int InsertSession(SessionRecord record)
        {
            if (!EnsureOpen() || record == null) return 0;
            _connection.Insert(record);
            return record.Id;
        }

        /// <summary>최근 세션을 시작 시각 내림차순으로 가져온다.</summary>
        public List<SessionRecord> GetRecentSessions(int limit)
        {
            if (!EnsureOpen()) return new List<SessionRecord>();
            return _connection.Table<SessionRecord>()
                .OrderByDescending(s => s.Id)
                .Take(limit)
                .ToList();
        }

        public int GetSessionCount()
        {
            if (!EnsureOpen()) return 0;
            return _connection.Table<SessionRecord>().Count();
        }

        /// <summary>누적 집중 시간(분). 결과와 무관하게 완료한 세트만 센다.</summary>
        public int GetTotalFocusMinutes()
        {
            if (!EnsureOpen()) return 0;
            return _connection.ExecuteScalar<int>(
                "SELECT COALESCE(SUM(DurationMin * SetsCompleted), 0) FROM sessions");
        }

        // ---- harvests / inventory ----

        /// <summary>수확 1건을 기록하고 보유량을 1 올린다. 두 작업은 한 트랜잭션이다.</summary>
        public void AddHarvest(int sessionId, string cropType)
        {
            if (!EnsureOpen() || string.IsNullOrEmpty(cropType)) return;

            _connection.RunInTransaction(() =>
            {
                _connection.Insert(new HarvestRecord
                {
                    SessionId = sessionId,
                    CropType = cropType,
                    HarvestedAt = Now(),
                });

                InventoryRecord item = _connection.Find<InventoryRecord>(cropType)
                                       ?? new InventoryRecord { CropType = cropType, Quantity = 0 };
                item.Quantity++;
                _connection.InsertOrReplace(item);
            });
        }

        public int GetHarvestCount()
        {
            if (!EnsureOpen()) return 0;
            return _connection.Table<HarvestRecord>().Count();
        }

        public List<InventoryRecord> GetInventory()
        {
            if (!EnsureOpen()) return new List<InventoryRecord>();
            return _connection.Table<InventoryRecord>().ToList();
        }

        // ---- 초기화 ----

        /// <summary>모든 테이블의 행을 지운다. 파일은 그대로 둔다.</summary>
        public void DeleteAllRows()
        {
            if (!EnsureOpen()) return;
            _connection.RunInTransaction(() =>
            {
                _connection.DeleteAll<HarvestRecord>();
                _connection.DeleteAll<SessionRecord>();
                _connection.DeleteAll<InventoryRecord>();
            });
        }

        // ---- 내부 ----

        /// <summary>사양서 7장 시각 형식.</summary>
        public static string Now()
        {
            return DateTime.Now.ToString("o");
        }

        private bool EnsureOpen()
        {
            if (!IsOpen) Open();
            if (!IsOpen) Debug.LogWarning("SaiunDatabase: 연결이 닫혀 있어 요청을 무시합니다.");
            return IsOpen;
        }
    }
}
