using _SAIUN.Scripts.Core;

namespace _SAIUN.Scripts.Crop
{
    /// <summary>작물 등급. 세션 설정으로 시작 시 1회 결정된다.</summary>
    public enum CropGrade
    {
        Normal,
        Advanced,
        Rare,
        Legend,
    }

    /// <summary>
    /// 등급 판정 로직(사양서 v1.1 7-2).
    /// 판정은 포모도로 시작 시 1회만 하고, 진행 중 설정 변경은 반영하지 않는다.
    /// </summary>
    public static class CropGradeRule
    {
        // ---- 등급 임계값 (분 / 세트) ----
        public const int LegendFocusMinutes = 60;
        public const int LegendSets = 8;
        public const int RareFocusMinutes = 45;
        public const int RareSets = 4;
        public const int AdvancedFocusMinutes = 25;
        public const int AdvancedSets = 8;

        public static CropGrade GetCropGrade(int focusMinutes, int sets)
        {
            if (focusMinutes >= LegendFocusMinutes && sets >= LegendSets) return CropGrade.Legend;
            if (focusMinutes >= RareFocusMinutes && sets >= RareSets) return CropGrade.Rare;
            if (focusMinutes >= AdvancedFocusMinutes && sets >= AdvancedSets) return CropGrade.Advanced;
            return CropGrade.Normal;
        }

        public static CropGrade GetCropGrade(SessionConfig config)
        {
            return config == null
                ? CropGrade.Normal
                : GetCropGrade(config.FocusMinutes, config.TotalSets);
        }
    }
}
