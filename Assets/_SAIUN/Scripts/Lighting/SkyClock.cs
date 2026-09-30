using System;
using UnityEngine;

namespace _SAIUN.Scripts.Lighting
{
    /// <summary>하늘의 해가 그리는 길(고도, 도). 정원 그림자를 만드는 광원의 고도와 따로다.</summary>
    [Serializable]
    public struct SkyArc
    {
        [Tooltip("하루가 시작할 때(진행률 0) 하늘의 해 고도. 앱을 켜면 보이는 시계 화면이라 금빛이 도는 맑은 아침 해로 둔다.")]
        public float Sunrise;

        [Tooltip("한낮 하늘의 해 고도")]
        public float Noon;

        [Tooltip("하루가 끝날 때(진행률 1) 하늘의 해 고도. 정원 빛은 그림자가 읽히게 높게 두지만, 하늘은 지평선에 닿아야 노을이 진다.")]
        public float Sunset;

        [Tooltip("박명이 가장 깊을 때(Twilight 1) 해가 저녁 해 고도에서 더 내려가는 각. 아침 쪽도 같은 바닥까지 내려간다.")]
        public float TwilightDepth;

        [Tooltip("밤이 가장 깊을 때(Night 1) 해가 박명 바닥에서 더 내려가는 각. 해가 지평선 약 18° 아래면 하늘빛이 다 사그라진다.")]
        public float NightDepth;

        public static SkyArc Default => new SkyArc { Sunrise = 6f, Noon = 62f, Sunset = 0.8f, TwilightDepth = 8.3f, NightDepth = 10.5f };

        /// <summary>박명이 가장 깊을 때의 해 고도(아침·저녁 같다).</summary>
        public float TwilightFloor => Sunset - TwilightDepth;

        /// <summary>진행률·박명·밤에 맞는 하늘의 해 고도.</summary>
        public float Elevation(float progress, float twilight, float night)
        {
            float low = progress < 0.5f ? Sunrise : Sunset;
            return Mathf.Lerp(low, Noon, Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI))
                   - (low - TwilightFloor) * twilight - NightDepth * night;
        }
    }

    /// <summary>시계 시각 하나가 만드는 하늘의 해·달 상태.</summary>
    public struct SkyClockState
    {
        /// <summary>낮의 진행률(0 아침 ~ 1 저녁). 해가 진 뒤는 1, 해 뜨기 전은 0이다.</summary>
        public float Progress;

        /// <summary>박명(0~1).</summary>
        public float Twilight;

        /// <summary>밤(0~1). 박명이 끝난 뒤 하늘빛이 사그라지는 정도.</summary>
        public float Night;

        /// <summary>달이 지평선 위에 있는지.</summary>
        public bool MoonUp;

        /// <summary>달이 뜬 동안의 진행률(0 뜸 ~ 1 짐).</summary>
        public float MoonProgress;

        /// <summary>달이 찬 정도(0 그믐 ~ 1 보름).</summary>
        public float MoonLit;
    }

    /// <summary>
    /// 시계 시각을 하늘의 해·달로 옮긴다 (2026-09-30, 사용자 "idle 상태에서도 미려하게" → 실제 시각·하루 순환 토글).
    /// 낮(해가 아침 해 고도를 지나 저녁 해 고도로 내려올 때까지)은 진행률로, 그 밖은 실제 해 고도에서 박명·밤을 읽는다.
    /// 그래서 하늘의 해는 해 진 뒤부터 해 뜰 때까지 실제 해와 같은 고도로 내려가고 올라온다.
    /// 달은 해의 하루 길을 나이만큼 늦게 따라간다(보름달은 해가 질 때 뜬다). 달의 적위·궤도 기울기는 셈하지 않는다.
    /// </summary>
    public class SkyClock
    {
        private const double HoursPerDay = 24.0;
        private const double MinutesPerHour = 60.0;

        public float Latitude { get; set; }
        public float Longitude { get; set; }
        public SkyArc Arc { get; set; }

        public SkyClock(float latitude, float longitude, SkyArc arc)
        {
            Latitude = latitude;
            Longitude = longitude;
            Arc = arc;
        }

        /// <summary>현지 시각의 하늘 상태. utcOffset은 그 시각의 표준시 차, moonAge는 달의 나이(일)다.</summary>
        public SkyClockState StateAt(DateTime local, TimeSpan utcOffset, double moonAge)
        {
            double hours = local.TimeOfDay.TotalHours;
            float elevation = SolarCalculator.Elevation(ToUtc(local, utcOffset), Latitude, Longitude);
            var state = new SkyClockState { MoonLit = SolarCalculator.MoonLitFraction(moonAge) };

            if (DayWindow(local.Date, utcOffset, Arc.Sunrise, Arc.Sunset, out double rise, out double set)
                && hours >= rise && hours <= set)
            {
                state.Progress = (float)((hours - rise) / Math.Max(set - rise, 1e-6));
            }
            else
            {
                // 해 진 뒤(저녁 쪽)와 해 뜨기 전(새벽 쪽). 박명·밤을 실제 고도에서 읽어 하늘의 해가 실제 해와 같은 고도가 된다.
                bool evening = hours > rise;
                float low = evening ? Arc.Sunset : Arc.Sunrise;
                state.Progress = evening ? 1f : 0f;
                state.Twilight = Mathf.Clamp01((low - elevation) / Mathf.Max(low - Arc.TwilightFloor, 1e-3f));
                state.Night = Mathf.Clamp01((Arc.TwilightFloor - elevation) / Mathf.Max(Arc.NightDepth, 1e-3f));
            }

            // 달: 나이만큼 늦은 시각의 해 길을 따른다.
            double lag = moonAge / SolarCalculator.SynodicMonthDays * HoursPerDay;
            double moonHours = Repeat(hours - lag, HoursPerDay);
            if (DayWindow(local.Date, utcOffset, 0f, 0f, out double moonRise, out double moonSet)
                && moonHours >= moonRise && moonHours <= moonSet)
            {
                state.MoonUp = true;
                state.MoonProgress = (float)((moonHours - moonRise) / Math.Max(moonSet - moonRise, 1e-6));
            }
            return state;
        }

        /// <summary>그날 하루가 시작하는 시각(현지 시, 아침 해가 하늘의 아침 해 고도를 지날 때).</summary>
        public double MorningHours(DateTime localDate, TimeSpan utcOffset)
        {
            return DayWindow(localDate, utcOffset, Arc.Sunrise, Arc.Sunset, out double rise, out _) ? rise : HoursPerDay / 4.0;
        }

        /// <summary>
        /// 세션의 하늘(진행률·박명)을 그날의 현지 시각으로 옮긴다. 세션이 끝나 시계 하늘로 돌아갈 때
        /// 여기서부터 시간을 앞으로 흘려 보낸다.
        /// </summary>
        public double HoursFor(float progress, float twilight, DateTime localDate, TimeSpan utcOffset)
        {
            bool haveDay = DayWindow(localDate, utcOffset, Arc.Sunrise, Arc.Sunset, out double rise, out double set);
            if (!haveDay) return HoursPerDay / 2.0;
            if (twilight <= 0f) return rise + Mathf.Clamp01(progress) * (set - rise);

            bool evening = progress >= 0.5f;
            float elevation = Arc.Elevation(evening ? 1f : 0f, twilight, 0f);
            if (!Crossing(localDate, utcOffset, elevation, out double up, out double down)) return evening ? set : rise;
            return evening ? down : up;
        }

        // 해가 아침에 morning 고도를 지나 저녁에 evening 고도로 내려오는 시각(현지 시).
        private bool DayWindow(DateTime localDate, TimeSpan utcOffset, float morning, float evening, out double rise, out double set)
        {
            bool a = Crossing(localDate, utcOffset, morning, out rise, out _);
            bool b = Crossing(localDate, utcOffset, evening, out _, out set);
            return a && b && set > rise;
        }

        private bool Crossing(DateTime localDate, TimeSpan utcOffset, float elevation, out double rising, out double setting)
        {
            bool ok = SolarCalculator.Crossing(localDate.Date, Latitude, Longitude, elevation, out double up, out double down);
            double offset = utcOffset.TotalHours;
            rising = Repeat(up / MinutesPerHour + offset, HoursPerDay);
            setting = Repeat(down / MinutesPerHour + offset, HoursPerDay);
            return ok;
        }

        private static DateTime ToUtc(DateTime local, TimeSpan utcOffset)
        {
            return DateTime.SpecifyKind(local - utcOffset, DateTimeKind.Utc);
        }

        internal static double Repeat(double value, double length)
        {
            double r = value % length;
            return r < 0 ? r + length : r;
        }
    }
}
