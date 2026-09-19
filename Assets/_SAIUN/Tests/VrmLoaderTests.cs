using System.Collections;
using System.IO;
using System.Threading.Tasks;
using _SAIUN.Scripts.Character;
using _SAIUN.Scripts.Data;
using NUnit.Framework;
using UniVRM10;
using UnityEngine;
using UnityEngine.TestTools;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P3-01: VRM을 런타임에 불러오고, 실패하면 이유를 알리며 지금 캐릭터를 유지한다(사양서 v1.1 8-2).
    /// 실제 VRM이 필요한 테스트는 저장소 밖(gitignore)의 로컬 모델이 있을 때만 돈다.
    /// </summary>
    public class VrmLoaderTests
    {
        // 서드파티 모델이라 저장소에 없다. 있으면 실제 로드까지 확인한다.
        private const string LocalSamplePath = "Assets/_SAIUN/Art/VRM/HatsuneMikuNT.vrm";
        private const string MToonUrpShader = "VRM10/Universal Render Pipeline/MToon10";

        private GameObject _go;
        private VrmLoader _loader;
        private string _tempDirectory;
        private VrmLoadError _lastError;
        private int _failures;

        [SetUp]
        public void SetUp()
        {
            SettingsStore.DeleteAll();
            _tempDirectory = Path.Combine(Application.temporaryCachePath, $"vrm_{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectory);

            _go = new GameObject("Character");
            _loader = _go.AddComponent<VrmLoader>();
            _lastError = VrmLoadError.None;
            _failures = 0;
            _loader.OnLoadFailed += (error, _) =>
            {
                _lastError = error;
                _failures++;
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            SettingsStore.DeleteAll();
            if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
        }

        [Test]
        public void 없는_파일은_찾을_수_없다고_알린다()
        {
            Assert.AreEqual(VrmLoadError.FileNotFound, VrmLoader.Validate(Path.Combine(_tempDirectory, "none.vrm"), out _));
            Assert.AreEqual(VrmLoadError.FileNotFound, VrmLoader.Validate(null, out _));
        }

        [Test]
        public void 용량이_200MB를_넘으면_열기_전에_막는다()
        {
            string path = Path.Combine(_tempDirectory, "huge.vrm");
            using (var stream = new FileStream(path, FileMode.Create)) stream.SetLength(VrmLoader.MaxFileBytes + 1);

            Assert.AreEqual(VrmLoadError.TooLarge, VrmLoader.Validate(path, out string detail));
            StringAssert.Contains("MB", detail);

            string exact = Path.Combine(_tempDirectory, "exact.vrm");
            using (var stream = new FileStream(exact, FileMode.Create)) stream.SetLength(VrmLoader.MaxFileBytes);
            Assert.AreEqual(VrmLoadError.None, VrmLoader.Validate(exact, out _), "200MB 딱 맞으면 허용한다");
        }

        [UnityTest]
        public IEnumerator 손상된_파일은_읽지_못했다고_알리고_경로를_저장하지_않는다()
        {
            string path = Path.Combine(_tempDirectory, "broken.vrm");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            LogAssert.ignoreFailingMessages = true;   // UniVRM이 파싱 실패를 로그로도 남긴다

            Task<bool> task = _loader.ImportAsync(path);
            yield return new WaitUntil(() => task.IsCompleted);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(task.Result);
            Assert.AreEqual(VrmLoadError.Unreadable, _lastError);
            Assert.IsNull(_loader.CurrentModel);
            Assert.AreEqual(string.Empty, SettingsStore.VrmPath, "실패하면 경로를 저장하지 않는다");
        }

        [UnityTest]
        public IEnumerator 실제_VRM을_불러오면_MToon_URP로_그리고_그림자를_드리우지_않는다()
        {
            string path = Path.GetFullPath(LocalSamplePath);
            if (!File.Exists(path)) Assert.Ignore("로컬 샘플 VRM이 없어 건너뛴다.");

            Task<bool> task = _loader.ImportAsync(path);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsTrue(task.Result, $"불러오기 실패: {_lastError}");
            GameObject model = _loader.CurrentModel;
            Assert.IsNotNull(model);
            Assert.AreSame(_go.transform, model.transform.parent);
            Assert.AreEqual(path, SettingsStore.VrmPath, "성공하면 경로를 저장한다");
            Assert.IsTrue(_loader.PoseSupported);
            Assert.IsNotNull(model.GetComponent<Vrm10Instance>());

            // 사양서 8-8: MToon이 URP에서 분홍으로 깨지지 않도록 URP용 MToon10으로 만든다.
            bool anyMToon = false;
            var shaders = new System.Collections.Generic.HashSet<string>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    shaders.Add(material.shader.name);
                    if (material.shader.name == MToonUrpShader) anyMToon = true;
                }
            }
            Assert.IsTrue(anyMToon, "MToon 머티리얼이 URP 셰이더를 써야 한다: " + string.Join(", ", shaders));

            // 창 베젤에 앉아 있어 화단 바닥에 그림자를 드리우지 않는다.
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, renderer.shadowCastingMode);
            }
        }

        [UnityTest]
        public IEnumerator 불러온_뒤_실패하면_지금_캐릭터를_유지한다()
        {
            string path = Path.GetFullPath(LocalSamplePath);
            if (!File.Exists(path)) Assert.Ignore("로컬 샘플 VRM이 없어 건너뛴다.");

            Task<bool> first = _loader.ImportAsync(path);
            yield return new WaitUntil(() => first.IsCompleted);
            GameObject kept = _loader.CurrentModel;

            Task<bool> second = _loader.ImportAsync(Path.Combine(_tempDirectory, "missing.vrm"));
            yield return new WaitUntil(() => second.IsCompleted);

            Assert.IsFalse(second.Result);
            Assert.AreEqual(VrmLoadError.FileNotFound, _lastError);
            Assert.AreSame(kept, _loader.CurrentModel);
            Assert.AreEqual(path, SettingsStore.VrmPath);
        }
    }
}
