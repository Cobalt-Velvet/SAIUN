using SQLite;

// 사양서 7장 SQLite 스키마의 4개 테이블 모델.
// 파일당 공개 클래스 1개 규칙의 예외로, 사양서 3장이 이 파일에 4개 모델을 함께 두도록 지정했다.
// 시각 컬럼은 모두 DateTime.Now.ToString("o") 형식의 문자열이다.
namespace _SAIUN.Scripts.Data
{
    /// <summary>세션 1건의 기록. 세션 종료 시 1행을 넣는다.</summary>
    [Table("sessions")]
    public class SessionRecord
    {
        /// <summary>Result 컬럼에 허용되는 값. 이 두 값만 쓴다.</summary>
        public const string ResultHarvested = "HARVESTED";
        public const string ResultFailed = "FAILED";

        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>세션 시작 시각(ISO8601).</summary>
        public string StartTime { get; set; }

        /// <summary>집중 구간 길이(분).</summary>
        public int DurationMin { get; set; }

        /// <summary>완료한 집중 세트 수.</summary>
        public int SetsCompleted { get; set; }

        public string CropType { get; set; }

        /// <summary>HARVESTED 또는 FAILED.</summary>
        public string Result { get; set; }

        /// <summary>이 세션에서 실제로 집중한 시간(분).</summary>
        public int FocusMinutes => DurationMin * SetsCompleted;
    }

    /// <summary>수확 1건의 기록.</summary>
    [Table("harvests")]
    public class HarvestRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>sessions.Id 참조.</summary>
        [Indexed]
        public int SessionId { get; set; }

        public string CropType { get; set; }

        /// <summary>수확 시각(ISO8601).</summary>
        public string HarvestedAt { get; set; }
    }

    /// <summary>작물별 보유 수량.</summary>
    [Table("inventory")]
    public class InventoryRecord
    {
        [PrimaryKey]
        public string CropType { get; set; }

        public int Quantity { get; set; }
    }

    /// <summary>해금된 아이템.</summary>
    [Table("unlocks")]
    public class UnlockRecord
    {
        [PrimaryKey]
        public string ItemId { get; set; }

        /// <summary>해금 시각(ISO8601).</summary>
        public string UnlockedAt { get; set; }
    }
}
