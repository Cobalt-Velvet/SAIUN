using System;
using UnityEngine;

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// 세션 설정값 컨테이너.
    /// 순수 클래스이며 MonoBehaviour를 상속하지 않는다.
    /// 값은 프로퍼티 setter에서 즉시 경계값으로 보정되고,
    /// 인스펙터 편집처럼 setter를 거치지 않는 경로는 역직렬화 직후 다시 보정한다.
    /// </summary>
    [Serializable]
    public class SessionConfig : ISerializationCallbackReceiver
    {
        // ---- 확정값 (사양서 8장) ----
        public const int DefaultFocusMinutes = 25;
        public const int MinFocusMinutes = 5;
        public const int MaxFocusMinutes = 90;

        public const int DefaultShortBreakMinutes = 5;
        public const int MinShortBreakMinutes = 1;
        public const int MaxShortBreakMinutes = 30;

        public const int DefaultLongBreakMinutes = 15;
        public const int MinLongBreakMinutes = 5;
        public const int MaxLongBreakMinutes = 60;

        public const int DefaultTotalSets = 4;
        public const int MinTotalSets = 1;
        public const int MaxTotalSets = 12;

        public const string DefaultCropType = "rice";
        public const int MaxTaskTextLength = 40;

        // ---- 직렬화 필드 ----
        [SerializeField] private int focusMinutes = DefaultFocusMinutes;
        [SerializeField] private int shortBreakMinutes = DefaultShortBreakMinutes;
        [SerializeField] private int longBreakMinutes = DefaultLongBreakMinutes;
        [SerializeField] private int totalSets = DefaultTotalSets;
        [SerializeField] private string cropType = DefaultCropType;
        [SerializeField] private string taskText = string.Empty;

        // ---- 외부 노출 ----

        /// <summary>집중 구간 길이(분). 범위 밖 값은 경계값으로 보정된다.</summary>
        public int FocusMinutes
        {
            get => focusMinutes;
            set => focusMinutes = Mathf.Clamp(value, MinFocusMinutes, MaxFocusMinutes);
        }

        /// <summary>단기 휴식 길이(분).</summary>
        public int ShortBreakMinutes
        {
            get => shortBreakMinutes;
            set => shortBreakMinutes = Mathf.Clamp(value, MinShortBreakMinutes, MaxShortBreakMinutes);
        }

        /// <summary>장기 휴식 길이(분).</summary>
        public int LongBreakMinutes
        {
            get => longBreakMinutes;
            set => longBreakMinutes = Mathf.Clamp(value, MinLongBreakMinutes, MaxLongBreakMinutes);
        }

        /// <summary>세트 수.</summary>
        public int TotalSets
        {
            get => totalSets;
            set => totalSets = Mathf.Clamp(value, MinTotalSets, MaxTotalSets);
        }

        /// <summary>작물 종류 식별자. 비어 있으면 기본 작물로 대체된다.</summary>
        public string CropType
        {
            get => cropType;
            set => cropType = string.IsNullOrWhiteSpace(value) ? DefaultCropType : value.Trim();
        }

        /// <summary>태스크 텍스트. 최대 길이를 넘는 부분은 잘린다.</summary>
        public string TaskText
        {
            get => taskText;
            set => taskText = TruncateTaskText(value);
        }

        /// <summary>전체 집중 시간(분) = 집중 구간 × 세트 수.</summary>
        public int TotalFocusMinutes => focusMinutes * totalSets;

        /// <summary>현재 값 전체를 범위 안으로 보정한다.</summary>
        public void Validate()
        {
            FocusMinutes = focusMinutes;
            ShortBreakMinutes = shortBreakMinutes;
            LongBreakMinutes = longBreakMinutes;
            TotalSets = totalSets;
            CropType = cropType;
            TaskText = taskText;
        }

        /// <summary>값 복사본을 만든다. 세션 진행 중 원본이 바뀌어도 영향받지 않게 하기 위함.</summary>
        public SessionConfig Clone()
        {
            return (SessionConfig)MemberwiseClone();
        }

        public static string TruncateTaskText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= MaxTaskTextLength ? value : value.Substring(0, MaxTaskTextLength);
        }

        // ---- Unity 직렬화 훅 ----

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        // 인스펙터·JSON 등 setter를 거치지 않는 경로로 들어온 값도 범위 안으로 맞춘다.
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            Validate();
        }

        public override string ToString()
        {
            return $"Focus {focusMinutes}m / Short {shortBreakMinutes}m / Long {longBreakMinutes}m / Sets {totalSets} / Crop {cropType}";
        }
    }
}
