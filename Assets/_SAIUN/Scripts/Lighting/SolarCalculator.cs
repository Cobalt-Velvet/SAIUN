using System;

namespace _SAIUN.Scripts.Lighting
{
    /// <summary>
    /// 시각과 자리로 실제 해의 고도, 해가 어떤 고도를 지나는 시각, 달의 나이를 셈한다 (2026-09-30, 시계 화면 하늘).
    /// 해는 미국 해양대기청(NOAA)의 근사식(연중 날짜의 푸리에 급수로 적위·균시차)을 쓴다. 오차는 1분 안팎이다.
    /// 달은 평균 삭망월로 나이만 센다. 하늘에서 달의 자리는 해의 하루 길을 나이만큼 늦춰 따라가게 근사한다(SunOrbitController).
    /// </summary>
    public static class SolarCalculator
    {
        /// <summary>평균 삭망월(일).</summary>
        public const double SynodicMonthDays = 29.530588853;

        // 기준 삭: 2000-01-06 18:14 UTC
        private static readonly DateTime ReferenceNewMoonUtc = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

        private const double MinutesPerDay = 1440.0;
        private const double DegreesPerHourAngleMinute = 0.25;   // 지구는 4분에 1° 돈다
        private const double NoonMinutesUtc = 720.0;

        /// <summary>그 순간(UTC) 위도·경도(도, 동경 +)에서 해의 고도(도). 대기 굴절은 넣지 않는다.</summary>
        public static float Elevation(DateTime utc, float latitude, float longitude)
        {
            SunAngles(utc, out double declination, out double equationOfTime);
            double minutes = utc.TimeOfDay.TotalMinutes;
            double trueSolar = minutes + equationOfTime + 4.0 * longitude;
            double hourAngle = Deg2Rad(trueSolar * DegreesPerHourAngleMinute - 180.0);
            double lat = Deg2Rad(latitude);
            double sinElevation = Math.Sin(lat) * Math.Sin(declination) + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hourAngle);
            return (float)Rad2Deg(Math.Asin(Clamp(sinElevation, -1.0, 1.0)));
        }

        /// <summary>
        /// 그날(UTC 날짜) 해가 elevation 도를 오전에 오르며, 오후에 내려가며 지나는 시각(UTC, 자정부터 분).
        /// 해가 그 고도에 닿지 않거나(늘 아래) 늘 위에 있으면 false다.
        /// </summary>
        public static bool Crossing(DateTime utcDate, float latitude, float longitude, float elevation,
            out double risingMinutes, out double settingMinutes)
        {
            DateTime noon = utcDate.Date.AddMinutes(NoonMinutesUtc - 4.0 * longitude);
            SunAngles(noon, out double declination, out double equationOfTime);
            double lat = Deg2Rad(latitude);
            double cosHour = (Math.Sin(Deg2Rad(elevation)) - Math.Sin(lat) * Math.Sin(declination))
                             / (Math.Cos(lat) * Math.Cos(declination));
            double solarNoon = NoonMinutesUtc - 4.0 * longitude - equationOfTime;
            if (cosHour > 1.0 || cosHour < -1.0)
            {
                risingMinutes = settingMinutes = solarNoon;
                return false;
            }
            double half = Rad2Deg(Math.Acos(cosHour)) / DegreesPerHourAngleMinute;
            risingMinutes = solarNoon - half;
            settingMinutes = solarNoon + half;
            return true;
        }

        /// <summary>달의 나이(일, 0 삭 ~ 29.53).</summary>
        public static double MoonAge(DateTime utc)
        {
            double days = (utc - ReferenceNewMoonUtc).TotalDays;
            double age = days % SynodicMonthDays;
            return age < 0 ? age + SynodicMonthDays : age;
        }

        /// <summary>달이 찬 정도(0 그믐 ~ 1 보름). 나이에서 해·달이 벌어진 각으로 셈한다.</summary>
        public static float MoonLitFraction(double ageDays)
        {
            double elongation = 2.0 * Math.PI * ageDays / SynodicMonthDays;
            return (float)((1.0 - Math.Cos(elongation)) / 2.0);
        }

        // NOAA 근사식: 연중 날짜 각(gamma)으로 적위(라디안)와 균시차(분)
        private static void SunAngles(DateTime utc, out double declination, out double equationOfTime)
        {
            int daysInYear = DateTime.IsLeapYear(utc.Year) ? 366 : 365;
            double gamma = 2.0 * Math.PI / daysInYear * (utc.DayOfYear - 1 + (utc.TimeOfDay.TotalHours - 12.0) / 24.0);
            equationOfTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                                       - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
            declination = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                          - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                          - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
        }

        private static double Deg2Rad(double degrees) => degrees * Math.PI / 180.0;
        private static double Rad2Deg(double radians) => radians * 180.0 / Math.PI;
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
