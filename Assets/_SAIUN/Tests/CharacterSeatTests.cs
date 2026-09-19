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
    /// P3-02·P3-04: 캐릭터는 화면을 정면으로 보고 엉덩이를 창 하단 베젤에 걸친 채 앉는다.
    /// 2026-09-19 참고 그림처럼 다리를 꼬아 창 밖으로 늘어뜨리고, 상체를 젖혀 두 손으로 베젤을 짚으며, 위에 올린 발을 까딱인다.
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
        public void 허벅지_아랫면이_창_하단_베젤_선에_얹힌다()
        {
            Assert.AreEqual(SceneMetrics.WindowHeight, _anchor.SeatPixel.y, 0.001f, "하단 베젤 = 카드 아래 모서리");
            Assert.IsTrue(_anchor.TryGetSeatSurface(out Vector3 seat));
            Vector2 contact = SceneMetrics.WorldToWindowPixels(seat);
            Assert.AreEqual(_anchor.SeatPixel.x, contact.x, 0.5f);
            Assert.AreEqual(_anchor.SeatPixel.y, contact.y, 0.5f);

            // 선이 허벅지를 가로지르지 않는다: 고관절과 엉덩이 뼈는 선보다 위(카드 안)에 있다.
            Assert.Less(Pixel(HumanBodyBones.LeftUpperLeg).y, SceneMetrics.WindowHeight);
            Assert.Less(Pixel(HumanBodyBones.RightUpperLeg).y, SceneMetrics.WindowHeight);
            Assert.Less(Pixel(HumanBodyBones.Hips).y, SceneMetrics.WindowHeight);
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
        public void 다리를_꼬아_위_무릎을_아래_무릎에_얹는다()
        {
            bool rightTop = _pose.RightOverLeft;
            Vector3 topHip = Local(rightTop ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg);
            Vector3 bottomHip = Local(rightTop ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            Vector3 topKnee = Local(rightTop ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg);
            Vector3 bottomKnee = Local(rightTop ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            float hipWidth = Mathf.Abs(topHip.x - bottomHip.x);

            // 위 무릎은 제 고관절보다 아래 다리 쪽으로 넘어가, 아래 무릎과 가로로 거의 겹친다.
            Assert.Greater((topKnee.x - topHip.x) * Mathf.Sign(bottomHip.x - topHip.x), 0f, "위 다리가 안쪽으로 넘어간다");
            Assert.Greater(Mathf.Abs(topKnee.x - topHip.x), hipWidth * 0.5f);
            Assert.Less(Mathf.Abs(topKnee.x - bottomKnee.x), hipWidth, "두 무릎이 겹친다");
            Assert.Greater(topKnee.y, bottomKnee.y, "위 무릎이 아래 무릎보다 높다");
        }

        [Test]
        public void 두_손으로_베젤을_짚는다()
        {
            // 목표점은 포즈를 입힐 때의 엉덩이 기준이다. 자리를 맞춘 뒤 한 번 더 입힌다(앱에서는 매 프레임).
            _pose.Apply(0f);
            bool rightTop = _pose.RightOverLeft;
            Transform nearWrist = _animator.GetBoneTransform(rightTop ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform propWrist = _animator.GetBoneTransform(rightTop ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            float armLength = ArmLength(rightTop);

            float nearMiss = Vector3.Distance(nearWrist.position, _pose.NearHandTarget);
            float propMiss = Vector3.Distance(propWrist.position, _pose.PropHandTarget);
            Assert.Less(nearMiss, armLength * 0.05f, $"near hand reaches its target (miss {nearMiss:F3}, arm {armLength:F3})");
            Assert.Less(propMiss, armLength * 0.05f, $"prop hand reaches its target (miss {propMiss:F3}, arm {armLength:F3})");

            // 화면에서: 두 손목이 베젤 높이에 있고, 기대는 손은 엉덩이에서 멀리, 가까운 손은 반대쪽 가까이 있다.
            Vector2 hip = Pixel(HumanBodyBones.Hips);
            Vector2 near = SceneMetrics.WorldToWindowPixels(nearWrist.position);
            Vector2 prop = SceneMetrics.WorldToWindowPixels(propWrist.position);
            Assert.AreEqual(SceneMetrics.WindowHeight, near.y, 15f, "가까운 손이 베젤을 짚는다");
            Assert.AreEqual(SceneMetrics.WindowHeight, prop.y, 15f, "기대는 손이 베젤을 짚는다");
            Assert.Greater(Mathf.Abs(prop.x - hip.x), Mathf.Abs(near.x - hip.x), "기대는 손이 더 멀리 짚는다");
            Assert.Less((prop.x - hip.x) * (near.x - hip.x), 0f, "두 손은 엉덩이 양옆에 있다");
        }

        [Test]
        public void 위에_올린_발만_까딱인다()
        {
            bool rightTop = _pose.RightOverLeft;
            HumanBodyBones topFoot = rightTop ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot;
            HumanBodyBones bottomFoot = rightTop ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;

            _pose.Apply(0f);
            Vector3 topStart = Local(topFoot);
            Vector3 bottomStart = Local(bottomFoot);

            // 까딱이는 주기의 1/4이 지나면 위 발이 가장 멀리 가 있다.
            float frequency = (float)typeof(CharacterPose)
                .GetField("bobFrequency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(_pose);
            _pose.Apply(0.25f / frequency);

            Assert.Greater(Vector3.Distance(Local(topFoot), topStart), 0.02f, "위 발이 움직인다");
            // 위 다리가 움직이면 몸 중심이 조금 옮겨 아래 발도 1픽셀 안쪽으로 따라 움직인다.
            Assert.Less(Vector3.Distance(Local(bottomFoot), bottomStart), SceneMetrics.PixelsToWorld(1f), "아래 발은 가만히 있다");
        }

        private float ArmLength(bool rightTop)
        {
            Transform upper = _animator.GetBoneTransform(rightTop ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            Transform lower = _animator.GetBoneTransform(rightTop ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            Transform hand = _animator.GetBoneTransform(rightTop ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            return Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
        }

        private Vector2 Pixel(HumanBodyBones bone)
        {
            return SceneMetrics.WorldToWindowPixels(_animator.GetBoneTransform(bone).position);
        }

        private Vector3 Local(HumanBodyBones bone)
        {
            return _go.transform.InverseTransformPoint(_animator.GetBoneTransform(bone).position);
        }
    }
}
