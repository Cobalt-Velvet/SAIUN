using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Character
{
    /// <summary>
    /// 캐릭터 포즈 (P3-04). 지금은 2026-09-19 사용자 지시대로 한 가지로 고정한다:
    /// 정면을 보고 하단 베젤에 걸터앉아 다리를 흔든다. 상태별 포즈(사양서 v1.1 8-6)는 클립이 생기면 여기에 붙인다.
    /// 휴머노이드 근육값으로 자세를 만들어, 모델마다 뼈 축이 달라도 같게 먹는다.
    /// 근육값은 -1~1이며 기본 범위(허벅지 −90°~+50°, 정강이 −80°~+80°)와 선 자세 값(허벅지 0.6, 정강이 1)을 실측해 정했다.
    /// 허벅지를 선 자세에서 90° 들면 수평(−0.67), 그때 무릎을 90° 굽히면 정강이가 수직(−0.12)이다.
    /// </summary>
    public class CharacterPose : MonoBehaviour
    {
        [SerializeField] private VrmLoader loader;

        [Header("앉은 자세 (근육값 −1~1, 실측 대기)")]
        [Tooltip("Upper Leg Front-Back. −0.67이면 허벅지가 앞으로 수평이 된다. −1은 30° 더 들린다.")]
        [SerializeField, Range(-1f, 1f)] private float thighForward = -0.67f;

        [Tooltip("Lower Leg Stretch. 허벅지가 수평일 때 −0.12면 정강이가 수직으로 내려온다. 흔들기는 이 값을 중심으로 오간다.")]
        [SerializeField, Range(-1f, 1f)] private float kneeBend = -0.12f;

        [Tooltip("Upper Leg In-Out. 양수면 다리를 벌린다.")]
        [SerializeField, Range(-1f, 1f)] private float legsApart = 0.08f;

        [Tooltip("Arm Down-Up. 음수면 팔을 내린다.")]
        [SerializeField, Range(-1f, 1f)] private float armsDown = -0.62f;

        [Tooltip("Arm Front-Back. 양수면 손이 앞으로 나와 무릎 옆 베젤을 짚는다.")]
        [SerializeField, Range(-1f, 1f)] private float armsForward = 0.3f;

        [Tooltip("Forearm Stretch. 1이면 팔꿈치를 편다.")]
        [SerializeField, Range(-1f, 1f)] private float elbowStretch = 0.75f;

        [Tooltip("Spine Front-Back. 양수면 앞으로 조금 숙인다.")]
        [SerializeField, Range(-1f, 1f)] private float leanForward = 0.08f;

        [Header("다리 흔들기")]
        [Tooltip("정강이가 앞뒤로 오가는 폭(근육값)")]
        [SerializeField, Range(0f, 1f)] private float swingAmount = 0.32f;

        [Tooltip("초당 흔드는 횟수")]
        [SerializeField, Min(0f)] private float swingFrequency = 0.7f;

        [Tooltip("두 다리가 어긋나는 정도(주기 비율). 0.5면 번갈아 흔든다.")]
        [SerializeField, Range(0f, 1f)] private float swingOffset = 0.5f;

        /// <summary>지금 포즈를 입히고 있는지. 휴머노이드가 아니면 T 포즈로 둔다(사양서 8-2).</summary>
        public bool IsPosing => _handler != null;

        private static readonly string[] MuscleNames =
        {
            "Left Upper Leg Front-Back", "Right Upper Leg Front-Back",
            "Left Lower Leg Stretch", "Right Lower Leg Stretch",
            "Left Upper Leg In-Out", "Right Upper Leg In-Out",
            "Left Arm Down-Up", "Right Arm Down-Up",
            "Left Arm Front-Back", "Right Arm Front-Back",
            "Left Forearm Stretch", "Right Forearm Stretch",
            "Spine Front-Back",
        };

        private enum Muscle
        {
            LeftThigh, RightThigh, LeftKnee, RightKnee, LeftApart, RightApart,
            LeftArmDown, RightArmDown, LeftArmForward, RightArmForward, LeftElbow, RightElbow, Spine,
        }

        private static int[] s_indices;
        private HumanPoseHandler _handler;
        private HumanPose _pose;
        private float _time;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (loader == null) loader = GetComponent<VrmLoader>();
            s_indices ??= FindMuscles();
        }

        private void OnEnable()
        {
            if (loader == null) return;
            loader.OnCharacterLoaded += Bind;
            if (loader.CurrentModel != null) Bind(loader.CurrentModel);
        }

        private void OnDisable()
        {
            if (loader != null) loader.OnCharacterLoaded -= Bind;
            Release();
        }

        private void Update()
        {
            if (_handler == null) return;
            _time += Time.deltaTime;
            Apply(_time);
        }

        // ---- 포즈 ----

        /// <summary>시각 time(초)의 포즈를 입힌다. 테스트는 시간을 정해 직접 부른다.</summary>
        internal void Apply(float time)
        {
            if (_handler == null) return;

            float phase = time * swingFrequency * Mathf.PI * 2f;
            float leftSwing = Mathf.Sin(phase) * swingAmount;
            float rightSwing = Mathf.Sin(phase + swingOffset * Mathf.PI * 2f) * swingAmount;

            Set(Muscle.LeftThigh, thighForward);
            Set(Muscle.RightThigh, thighForward);
            Set(Muscle.LeftKnee, kneeBend + leftSwing);
            Set(Muscle.RightKnee, kneeBend + rightSwing);
            Set(Muscle.LeftApart, legsApart);
            Set(Muscle.RightApart, legsApart);
            Set(Muscle.LeftArmDown, armsDown);
            Set(Muscle.RightArmDown, armsDown);
            Set(Muscle.LeftArmForward, armsForward);
            Set(Muscle.RightArmForward, armsForward);
            Set(Muscle.LeftElbow, elbowStretch);
            Set(Muscle.RightElbow, elbowStretch);
            Set(Muscle.Spine, leanForward);

            _handler.SetHumanPose(ref _pose);
        }

        private void Set(Muscle muscle, float value)
        {
            int index = s_indices[(int)muscle];
            if (index >= 0) _pose.muscles[index] = Mathf.Clamp(value, -1f, 1f);
        }

        private void Bind(GameObject model)
        {
            Release();
            var animator = model != null ? model.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman) return;

            _handler = new HumanPoseHandler(animator.avatar, animator.transform);
            _pose = new HumanPose();
            // 몸통 위치·회전은 불러온 자세 그대로 두고 근육만 바꾼다.
            _handler.GetHumanPose(ref _pose);
            Apply(_time);
        }

        private void Release()
        {
            _handler?.Dispose();
            _handler = null;
        }

        private static int[] FindMuscles()
        {
            var indices = new int[MuscleNames.Length];
            for (int i = 0; i < MuscleNames.Length; i++)
            {
                indices[i] = System.Array.IndexOf(HumanTrait.MuscleName, MuscleNames[i]);
                if (indices[i] < 0) Debug.LogWarning($"CharacterPose: 근육 '{MuscleNames[i]}'을 찾지 못했습니다.");
            }
            return indices;
        }
    }
}
