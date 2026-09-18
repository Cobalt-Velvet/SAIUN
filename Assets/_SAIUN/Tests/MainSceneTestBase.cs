using System;
using System.Collections;
using System.IO;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using _SAIUN.Scripts.Distraction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace _SAIUN.Tests
{
    /// <summary>
    /// SaiunSceneBuilder가 조립한 메인 씬을 불러와 실제 UI를 누르는 테스트의 공통 준비.
    /// DB·방해 앱 파일은 임시 경로로 돌리고, PlayerPrefs는 앱 키만 지운다.
    /// 타이머에는 가짜 시계를 넣어 Now를 움직이면 시간이 흐른다.
    /// </summary>
    public abstract class MainSceneTestBase
    {
        private const string SceneName = "Main";

        private string _dbPath;
        private string _blacklistPath;

        protected Scene Scene { get; private set; }
        protected GameManager Gm { get; private set; }
        protected double Now { get; set; }

        [UnitySetUp]
        public IEnumerator LoadMainScene()
        {
            _dbPath = Path.Combine(Application.temporaryCachePath, $"saiun_scene_{Guid.NewGuid():N}.db");
            _blacklistPath = Path.Combine(Application.temporaryCachePath, $"blacklist_scene_{Guid.NewGuid():N}.json");
            SaiunDatabase.PathOverride = _dbPath;
            BlacklistStore.PathOverride = _blacklistPath;
            SettingsStore.DeleteAll();
            BeforeSceneLoad();

            yield return Load();
        }

        [UnityTearDown]
        public IEnumerator UnloadMainScene()
        {
            yield return Unload();
            SaiunDatabase.PathOverride = null;
            BlacklistStore.PathOverride = null;
            SettingsStore.DeleteAll();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_blacklistPath)) File.Delete(_blacklistPath);
        }

        /// <summary>씬을 닫았다가 다시 연다. 앱 재시작을 흉내 낸다.</summary>
        protected IEnumerator Reload()
        {
            yield return Unload();
            yield return Load();
        }

        /// <summary>씬을 불러오기 직전. PlayerPrefs 등 시작 조건을 바꿀 때 쓴다.</summary>
        protected virtual void BeforeSceneLoad()
        {
        }

        /// <summary>씬을 불러온 뒤 한 프레임이 지난 시점. 뷰 참조를 찾을 때 쓴다.</summary>
        protected virtual void OnSceneLoaded()
        {
        }

        protected T Find<T>() where T : Component
        {
            foreach (GameObject root in Scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            Assert.Fail($"메인 씬에 {typeof(T).Name}이(가) 없다");
            return null;
        }

        /// <summary>뷰의 직렬화 필드를 읽는다. 연결이 빠졌으면 실패한다.</summary>
        protected static T Get<T>(object owner, string field) where T : class
        {
            var value = owner.GetType()
                .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(owner) as T;
            Assert.IsNotNull(value, $"{owner.GetType().Name}.{field}이(가) 연결되지 않았다. Setup Main Scene을 다시 실행하세요.");
            return value;
        }

        private IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
            Scene = SceneManager.GetSceneByName(SceneName);
            yield return null;   // Start까지 돌게 한 프레임 더 기다린다

            Gm = Find<GameManager>();
            Now = 1000d;
            Gm.Timer.SetClock(() => Now);
            OnSceneLoaded();
        }

        private IEnumerator Unload()
        {
            if (Scene.IsValid() && Scene.isLoaded) yield return SceneManager.UnloadSceneAsync(Scene);
        }
    }
}
