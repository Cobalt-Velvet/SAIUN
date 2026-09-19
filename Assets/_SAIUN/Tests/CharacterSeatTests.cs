using System.Collections;
using System.IO;
using System.Threading.Tasks;
using _SAIUN.Scripts.Character;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace _SAIUN.Tests
{
    /// <summary>
    /// P3-02·P3-04: 캐릭터는 화면을 정면으로 보고 엉덩이를 창 하단 베젤에 걸친 채 앉아, 다리를 창 밖으로 늘어뜨려 흔든다.
    /// 실제 VRM이 필요해 저장소 밖(gitignore)의 로컬 모델이 있을 때만 돈다.
    /// </summary>
    public class CharacterSeatTests
    {
        private const string LocalSamplePath = "Assets/_SAIUN/Art/VRM/HatsuneMikuNT.vrm";

        private GameObject _go;
        private VrmLoader _loader;
        private BezelAnchor _anchor;
        private CharacterPose _pose;
        private Animator _animator;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            string path = Path.GetFullPath(LocalSamplePath);
            if (!File.Exists(path)) Assert.Ignore("로컬 샘플 VRM이 없어 건너뛴다.");
            SettingsStore.DeleteAll();

            _go = new GameObject("Character");
            _loader = _go.AddComponent<VrmLoader>();
            _anchor = _go.AddComponent<BezelAnchor>();
            _pose = _go.AddComponent<CharacterPose>();

            Task<bool> task = _loader.LoadAsync(path, persist: false);
            yield return new WaitUntil(() => task.IsCompleted);
            Assert.IsTrue(task.Result);
            _animator = _loader.CurrentModel.GetComponent<Animator>();

            _pose.Apply(0f);
            _anchor.Align();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            SettingsStore.DeleteAll();
        }

        [Test]
        public void 엉덩이는_창_하단_베젤의_자리_픽셀에_있다()
        {
            Vector2 hip = Pixel(HumanBodyBones.Hips);
            Assert.AreEqual(SceneMetrics.WindowHeight, _anchor.SeatPixel.y, 0.001f, "하단 베젤 = 카드 아래 모서리");
            Assert.AreEqual(_anchor.SeatPixel.x, hip.x, 0.5f);
            Assert.AreEqual(_anchor.SeatPixel.y, hip.y, 0.5f);
        }

        [Test]
        public void 화면을_정면으로_본다()
        {
            Vector3 cameraForward = SceneMetrics.CameraRotation * Vector3.forward;
            Assert.Greater(Vector3.Dot(_go.transform.forward, -cameraForward), 0.99f);
        }

        [Test]
        public void 상반신은_하단_바_위로_나오고_다리는_창_밖으로_늘어진다()
        {
            Vector2 head = Pixel(HumanBodyBones.Head);
            Vector2 leftFoot = Pixel(HumanBodyBones.LeftFoot);
            Vector2 rightFoot = Pixel(HumanBodyBones.RightFoot);

            Assert.Less(head.y, SceneMetrics.SceneLayerBottom, "머리가 하단 바보다 위, Scene Layer 안에 보인다");
            Assert.Greater(leftFoot.y, SceneMetrics.WindowHeight, "발은 카드 아래 다리 영역에 있다");
            Assert.Greater(rightFoot.y, SceneMetrics.WindowHeight);
            Assert.Less(Mathf.Max(leftFoot.y, rightFoot.y), SceneMetrics.FrameHeight, "발이 창 아래로 잘리지 않는다");
        }

        [Test]
        public void 걸터앉아_무릎이_앞으로_나온다()
        {
            // 고관절에서 무릎으로 가는 허벅지 방향. 캐릭터 기준 +Z가 앞이다.
            Transform hipJoint = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform knee = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Vector3 thigh = _go.transform.InverseTransformDirection(knee.position - hipJoint.position);
            float pitch = Mathf.Atan2(thigh.y, thigh.z) * Mathf.Rad2Deg;

            Assert.Greater(thigh.z, 0f, "무릎이 몸 앞(+Z)으로 나온다");
            Assert.Less(Mathf.Abs(pitch), 20f, $"허벅지가 거의 수평이다 (기울기 {pitch:F1}도)");
            Assert.IsTrue(_pose.IsPosing);
        }

        [Test]
        public void 다리를_번갈아_흔든다()
        {
            _pose.Apply(0f);
            Vector3 leftStart = FootLocal(HumanBodyBones.LeftFoot);
            Vector3 rightStart = FootLocal(HumanBodyBones.RightFoot);

            // 흔들기 주기의 1/4이 지나면 발이 앞뒤로 움직여 있다.
            float frequency = (float)typeof(CharacterPose)
                .GetField("swingFrequency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(_pose);
            _pose.Apply(0.25f / frequency);
            Vector3 leftLater = FootLocal(HumanBodyBones.LeftFoot);
            Vector3 rightLater = FootLocal(HumanBodyBones.RightFoot);

            float leftMove = leftLater.z - leftStart.z;
            float rightMove = rightLater.z - rightStart.z;
            Assert.Greater(Mathf.Abs(leftMove), 0.03f, "왼발이 앞뒤로 움직인다");
            Assert.Greater(Mathf.Abs(rightMove), 0.03f, "오른발이 앞뒤로 움직인다");
            Assert.Less(leftMove * rightMove, 0f, "두 발이 반대로 움직인다");
        }

        private Vector2 Pixel(HumanBodyBones bone)
        {
            return SceneMetrics.WorldToWindowPixels(_animator.GetBoneTransform(bone).position);
        }

        private Vector3 FootLocal(HumanBodyBones bone)
        {
            return _go.transform.InverseTransformPoint(_animator.GetBoneTransform(bone).position);
        }
    }
}
