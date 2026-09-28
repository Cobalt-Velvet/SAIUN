using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Data
{
    /// <summary>
    /// PlayerPrefs 래퍼. 키 문자열은 이 클래스의 상수로만 접근한다.
    /// 기본값은 사양서 7장 PlayerPrefs 키 표를 따른다.
    /// </summary>
    public static class SettingsStore
    {
        /// <summary>PlayerPrefs 키 상수. 코드 다른 곳에서 키 문자열을 직접 쓰지 않는다.</summary>
        public static class Keys
        {
            public const string FocusMinutes = "session.focusMinutes";
            public const string ShortBreakMinutes = "session.shortBreakMinutes";
            public const string LongBreakMinutes = "session.longBreakMinutes";
            public const string TotalSets = "session.totalSets";
            public const string CropType = "session.cropType";
            public const string WindowPosX = "window.posX";
            public const string WindowPosY = "window.posY";
            public const string WindowAlwaysOnTop = "window.alwaysOnTop";
            public const string SoundEnabled = "sound.enabled";
            public const string SoundVolume = "sound.volume";
            public const string DistractionGraceSeconds = "distraction.graceSeconds";
            public const string TutorialCompleted = "tutorial.completed";
            public const string WeatherRandom = "weather.random";
            public const string WeatherFocusLinked = "weather.focusLinked";
            public const string BackgroundGlass = "display.backgroundGlass";
        }

        // ---- 기본값 (사양서 7장·8장) ----
        public const bool DefaultAlwaysOnTop = true;
        public const bool DefaultSoundEnabled = true;
        public const float DefaultSoundVolume = 0.7f;
        public const int DefaultGraceSeconds = 7;
        public const int MinGraceSeconds = 5;
        public const int MaxGraceSeconds = 30;
        public const bool DefaultTutorialCompleted = false;
        public const bool DefaultWeatherRandom = true;
        public const bool DefaultWeatherFocusLinked = true;
        public const bool DefaultBackgroundGlass = true;

        private const int True = 1;
        private const int False = 0;

        // ---- 세션 설정 ----

        /// <summary>마지막으로 저장한 세션 설정을 읽는다. 저장값이 없으면 기본값이다.</summary>
        public static SessionConfig LoadSessionConfig()
        {
            return new SessionConfig
            {
                FocusMinutes = PlayerPrefs.GetInt(Keys.FocusMinutes, SessionConfig.DefaultFocusMinutes),
                ShortBreakMinutes = PlayerPrefs.GetInt(Keys.ShortBreakMinutes, SessionConfig.DefaultShortBreakMinutes),
                LongBreakMinutes = PlayerPrefs.GetInt(Keys.LongBreakMinutes, SessionConfig.DefaultLongBreakMinutes),
                TotalSets = PlayerPrefs.GetInt(Keys.TotalSets, SessionConfig.DefaultTotalSets),
                CropType = PlayerPrefs.GetString(Keys.CropType, SessionConfig.DefaultCropType),
            };
        }

        /// <summary>세션 설정을 저장한다. 태스크 텍스트는 세션마다 새로 쓰므로 저장하지 않는다.</summary>
        public static void SaveSessionConfig(SessionConfig config)
        {
            if (config == null) return;
            PlayerPrefs.SetInt(Keys.FocusMinutes, config.FocusMinutes);
            PlayerPrefs.SetInt(Keys.ShortBreakMinutes, config.ShortBreakMinutes);
            PlayerPrefs.SetInt(Keys.LongBreakMinutes, config.LongBreakMinutes);
            PlayerPrefs.SetInt(Keys.TotalSets, config.TotalSets);
            PlayerPrefs.SetString(Keys.CropType, config.CropType);
            PlayerPrefs.Save();
        }

        // ---- 창 ----

        /// <summary>저장된 창 위치가 있는지. 없으면 호출 측이 우측 상단 기본 위치를 계산한다.</summary>
        public static bool HasWindowPosition =>
            PlayerPrefs.HasKey(Keys.WindowPosX) && PlayerPrefs.HasKey(Keys.WindowPosY);

        public static Vector2Int LoadWindowPosition()
        {
            return new Vector2Int(PlayerPrefs.GetInt(Keys.WindowPosX), PlayerPrefs.GetInt(Keys.WindowPosY));
        }

        public static void SaveWindowPosition(int x, int y)
        {
            PlayerPrefs.SetInt(Keys.WindowPosX, x);
            PlayerPrefs.SetInt(Keys.WindowPosY, y);
            PlayerPrefs.Save();
        }

        public static void ClearWindowPosition()
        {
            PlayerPrefs.DeleteKey(Keys.WindowPosX);
            PlayerPrefs.DeleteKey(Keys.WindowPosY);
            PlayerPrefs.Save();
        }

        public static bool AlwaysOnTop
        {
            get => GetBool(Keys.WindowAlwaysOnTop, DefaultAlwaysOnTop);
            set => SetBool(Keys.WindowAlwaysOnTop, value);
        }

        // ---- 사운드 ----

        public static bool SoundEnabled
        {
            get => GetBool(Keys.SoundEnabled, DefaultSoundEnabled);
            set => SetBool(Keys.SoundEnabled, value);
        }

        public static float SoundVolume
        {
            get => PlayerPrefs.GetFloat(Keys.SoundVolume, DefaultSoundVolume);
            set
            {
                PlayerPrefs.SetFloat(Keys.SoundVolume, Mathf.Clamp01(value));
                PlayerPrefs.Save();
            }
        }

        // ---- 방해 앱 ----

        public static int GraceSeconds
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(Keys.DistractionGraceSeconds, DefaultGraceSeconds), MinGraceSeconds, MaxGraceSeconds);
            set
            {
                PlayerPrefs.SetInt(Keys.DistractionGraceSeconds, Mathf.Clamp(value, MinGraceSeconds, MaxGraceSeconds));
                PlayerPrefs.Save();
            }
        }

        // ---- 튜토리얼 ----

        public static bool TutorialCompleted
        {
            get => GetBool(Keys.TutorialCompleted, DefaultTutorialCompleted);
            set => SetBool(Keys.TutorialCompleted, value);
        }

        // ---- 날씨 트리거 (사양서 v1.1 10-1, 중복 선택 가능) ----

        /// <summary>세트마다 날씨가 무작위로 바뀐다.</summary>
        public static bool WeatherRandom
        {
            get => GetBool(Keys.WeatherRandom, DefaultWeatherRandom);
            set => SetBool(Keys.WeatherRandom, value);
        }

        /// <summary>방해 앱을 감지하면 먹구름·비로 바뀐다.</summary>
        public static bool WeatherFocusLinked
        {
            get => GetBool(Keys.WeatherFocusLinked, DefaultWeatherFocusLinked);
            set => SetBool(Keys.WeatherFocusLinked, value);
        }

        // ---- 화면 ----

        /// <summary>배경 유리: 켜면 지평선 아래로 바탕화면이 흐리게 비치고, 끄면 하늘과 땅이 카드를 다 채운다.</summary>
        public static bool BackgroundGlass
        {
            get => GetBool(Keys.BackgroundGlass, DefaultBackgroundGlass);
            set => SetBool(Keys.BackgroundGlass, value);
        }

        // ---- 초기화 ----

        /// <summary>이 앱이 쓰는 키만 지운다. PlayerPrefs.DeleteAll은 쓰지 않는다.</summary>
        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(Keys.FocusMinutes);
            PlayerPrefs.DeleteKey(Keys.ShortBreakMinutes);
            PlayerPrefs.DeleteKey(Keys.LongBreakMinutes);
            PlayerPrefs.DeleteKey(Keys.TotalSets);
            PlayerPrefs.DeleteKey(Keys.CropType);
            PlayerPrefs.DeleteKey(Keys.WindowPosX);
            PlayerPrefs.DeleteKey(Keys.WindowPosY);
            PlayerPrefs.DeleteKey(Keys.WindowAlwaysOnTop);
            PlayerPrefs.DeleteKey(Keys.SoundEnabled);
            PlayerPrefs.DeleteKey(Keys.SoundVolume);
            PlayerPrefs.DeleteKey(Keys.DistractionGraceSeconds);
            PlayerPrefs.DeleteKey(Keys.TutorialCompleted);
            PlayerPrefs.DeleteKey(Keys.WeatherRandom);
            PlayerPrefs.DeleteKey(Keys.WeatherFocusLinked);
            PlayerPrefs.DeleteKey(Keys.BackgroundGlass);
            PlayerPrefs.Save();
        }

        // ---- 내부 ----

        private static bool GetBool(string key, bool defaultValue)
        {
            return PlayerPrefs.GetInt(key, defaultValue ? True : False) == True;
        }

        private static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? True : False);
            PlayerPrefs.Save();
        }
    }
}
