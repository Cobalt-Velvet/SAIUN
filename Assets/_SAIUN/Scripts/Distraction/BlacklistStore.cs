using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace _SAIUN.Scripts.Distraction
{
    /// <summary>
    /// 방해 앱 블랙·화이트리스트. Application.persistentDataPath/blacklist.json 에 저장한다.
    /// 파일이 없으면 기본 블랙리스트로 새로 만들고, 파싱에 실패하면 기본값으로 되돌리고 경고를 남긴다.
    /// 프로세스 이름 비교는 대소문자와 .exe 확장자를 무시한다.
    /// </summary>
    public class BlacklistStore
    {
        public const string FileName = "blacklist.json";
        public static readonly string[] DefaultBlacklist = { "chrome.exe", "steam.exe" };

        private const string ExeSuffix = ".exe";

        /// <summary>파일 전체 경로. 테스트가 PathOverride를 지정하면 그 경로를 쓴다.</summary>
        public static string FilePath =>
            string.IsNullOrEmpty(PathOverride) ? Path.Combine(Application.persistentDataPath, FileName) : PathOverride;

        /// <summary>테스트 전용 경로 덮어쓰기. null이면 기본 경로.</summary>
        internal static string PathOverride;

        [Serializable]
        private class Data
        {
            public List<string> blacklist = new List<string>();
            public List<string> whitelist = new List<string>();
        }

        private Data _data = new Data();

        public IReadOnlyList<string> Blacklist => _data.blacklist;
        public IReadOnlyList<string> Whitelist => _data.whitelist;

        // ---- 파일 ----

        public void Load()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                ResetToDefault();
                Save();
                return;
            }

            try
            {
                Data loaded = JsonUtility.FromJson<Data>(File.ReadAllText(path));
                if (loaded == null) throw new InvalidDataException("빈 JSON");
                loaded.blacklist ??= new List<string>();
                loaded.whitelist ??= new List<string>();
                _data = loaded;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"BlacklistStore: {path} 파싱 실패로 기본값으로 되돌립니다. ({e.Message})");
                ResetToDefault();
                Save();
            }
        }

        public void Save()
        {
            string path = FilePath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(_data, true));
        }

        public void ResetToDefault()
        {
            _data = new Data
            {
                blacklist = new List<string>(DefaultBlacklist),
                whitelist = new List<string>(),
            };
        }

        // ---- 판정 ----

        public bool IsBlacklisted(string processName) => Contains(_data.blacklist, processName);

        public bool IsWhitelisted(string processName) => Contains(_data.whitelist, processName);

        /// <summary>블랙리스트에 있고 영구 화이트리스트에는 없는 프로세스인지.</summary>
        public bool IsDistracting(string processName) => IsBlacklisted(processName) && !IsWhitelisted(processName);

        // ---- 편집 (변경 즉시 저장) ----

        public bool AddToBlacklist(string processName) => Add(_data.blacklist, processName);
        public bool RemoveFromBlacklist(string processName) => Remove(_data.blacklist, processName);
        public bool AddToWhitelist(string processName) => Add(_data.whitelist, processName);
        public bool RemoveFromWhitelist(string processName) => Remove(_data.whitelist, processName);

        /// <summary>비교용 정규화: 공백 제거, 소문자, 끝의 .exe 제거.</summary>
        public static string Normalize(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return string.Empty;
            string name = processName.Trim().ToLowerInvariant();
            return name.EndsWith(ExeSuffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - ExeSuffix.Length)
                : name;
        }

        // ---- 내부 ----

        private static bool Contains(List<string> list, string processName)
        {
            string target = Normalize(processName);
            if (target.Length == 0) return false;
            foreach (string entry in list)
            {
                if (Normalize(entry) == target) return true;
            }
            return false;
        }

        private bool Add(List<string> list, string processName)
        {
            if (string.IsNullOrWhiteSpace(processName) || Contains(list, processName)) return false;
            list.Add(processName.Trim());
            Save();
            return true;
        }

        private bool Remove(List<string> list, string processName)
        {
            string target = Normalize(processName);
            int removed = list.RemoveAll(entry => Normalize(entry) == target);
            if (removed == 0) return false;
            Save();
            return true;
        }
    }
}
