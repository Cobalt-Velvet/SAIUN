using System;
using System.Collections.Generic;

namespace _SAIUN.Scripts.Lighting
{
    /// <summary>하늘을 셈할 자리 하나: 이름, 위도·경도(도, 북위·동경 +), 그 도시가 쓰는 시간대 ID(윈도우·IANA).</summary>
    public readonly struct SkyPlace
    {
        public readonly string Name;
        public readonly float Latitude;
        public readonly float Longitude;
        public readonly string[] TimeZones;

        public SkyPlace(string name, float latitude, float longitude, params string[] timeZones)
        {
            Name = name;
            Latitude = latitude;
            Longitude = longitude;
            TimeZones = timeZones;
        }
    }

    /// <summary>
    /// 시계 하늘을 셈할 곳 목록. 설정에서 고르고, 고르지 않았으면
    /// 컴퓨터의 시간대를 쓰는 첫 도시(없으면 서울)로 본다. 인터넷 없이 되도록 도시를 앱에 담아 둔다.
    /// </summary>
    public static class SkyPlaces
    {
        private const string Korea = "Korea Standard Time";
        private const string Seoul = "Asia/Seoul";
        private const string Japan = "Tokyo Standard Time";
        private const string Tokyo = "Asia/Tokyo";

        public static readonly IReadOnlyList<SkyPlace> All = new[]
        {
            new SkyPlace("서울", 37.57f, 126.98f, Korea, Seoul),
            new SkyPlace("인천", 37.46f, 126.71f, Korea, Seoul),
            new SkyPlace("춘천", 37.88f, 127.73f, Korea, Seoul),
            new SkyPlace("강릉", 37.75f, 128.88f, Korea, Seoul),
            new SkyPlace("대전", 36.35f, 127.38f, Korea, Seoul),
            new SkyPlace("청주", 36.64f, 127.49f, Korea, Seoul),
            new SkyPlace("전주", 35.82f, 127.15f, Korea, Seoul),
            new SkyPlace("광주", 35.16f, 126.85f, Korea, Seoul),
            new SkyPlace("대구", 35.87f, 128.60f, Korea, Seoul),
            new SkyPlace("울산", 35.54f, 129.31f, Korea, Seoul),
            new SkyPlace("부산", 35.18f, 129.08f, Korea, Seoul),
            new SkyPlace("여수", 34.76f, 127.66f, Korea, Seoul),
            new SkyPlace("제주", 33.50f, 126.53f, Korea, Seoul),
            new SkyPlace("도쿄", 35.68f, 139.69f, Japan, Tokyo),
            new SkyPlace("오사카", 34.69f, 135.50f, Japan, Tokyo),
            new SkyPlace("삿포로", 43.06f, 141.35f, Japan, Tokyo),
            new SkyPlace("타이베이", 25.03f, 121.57f, "Taipei Standard Time", "Asia/Taipei"),
            new SkyPlace("베이징", 39.90f, 116.41f, "China Standard Time", "Asia/Shanghai"),
            new SkyPlace("상하이", 31.23f, 121.47f, "China Standard Time", "Asia/Shanghai"),
            new SkyPlace("홍콩", 22.32f, 114.17f, "China Standard Time", "Asia/Hong_Kong"),
            new SkyPlace("싱가포르", 1.35f, 103.82f, "Singapore Standard Time", "Asia/Singapore"),
            new SkyPlace("방콕", 13.76f, 100.50f, "SE Asia Standard Time", "Asia/Bangkok"),
            new SkyPlace("시드니", -33.87f, 151.21f, "AUS Eastern Standard Time", "Australia/Sydney"),
            new SkyPlace("밴쿠버", 49.28f, -123.12f, "Pacific Standard Time", "America/Vancouver"),
            new SkyPlace("로스앤젤레스", 34.05f, -118.24f, "Pacific Standard Time", "America/Los_Angeles"),
            new SkyPlace("뉴욕", 40.71f, -74.01f, "Eastern Standard Time", "America/New_York"),
            new SkyPlace("런던", 51.51f, -0.13f, "GMT Standard Time", "Europe/London"),
            new SkyPlace("파리", 48.86f, 2.35f, "Romance Standard Time", "Europe/Paris"),
            new SkyPlace("베를린", 52.52f, 13.40f, "W. Europe Standard Time", "Europe/Berlin"),
        };

        /// <summary>이름이 같은 곳의 번호. 없으면 -1.</summary>
        public static int IndexOf(string name)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Name == name) return i;
            }
            return -1;
        }

        /// <summary>
        /// 고른 곳. 이름이 비었거나 목록에 없으면 시간대 timeZoneId를 쓰는 첫 도시, 그것도 없으면 목록 첫 도시(서울)다.
        /// </summary>
        public static SkyPlace Resolve(string name, string timeZoneId)
        {
            int chosen = IndexOf(name);
            if (chosen >= 0) return All[chosen];
            foreach (SkyPlace place in All)
            {
                if (Array.IndexOf(place.TimeZones, timeZoneId) >= 0) return place;
            }
            return All[0];
        }
    }
}
