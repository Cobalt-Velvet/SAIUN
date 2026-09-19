using UnityEngine;

namespace _SAIUN.Scripts.Character
{
    /// <summary>
    /// 캐릭터 포즈 (P3-04). 2026-09-19 사용자가 보낸 참고 그림처럼 한 가지로 고정한다:
    /// 하단 베젤에 걸터앉아 다리를 꼬고, 상체를 조금 젖혀 두 손으로 베젤을 짚는다. 위에 올린 발을 가볍게 까딱인다.
    /// 상태별 포즈(사양서 v1.1 8-6)는 클립이 생기면 여기에 붙인다.
    /// 몸통·다리는 휴머노이드 근육값으로 만들어 모델마다 뼈 축이 달라도 같게 먹는다.
    /// 손은 근육값으로는 모델 체형마다 닿는 곳이 달라, 엉덩이 기준 목표점에 두 관절 IK로 짚게 한다.
    /// 근육값 기준(실측): 허벅지 Front-Back −0.67이면 수평, −1이면 30° 더 들린다. 정강이 Stretch 1이 폄, −0.12면 수평 허벅지에서 수직.
    /// 단, 허리를 젖히면 유니티가 몸 중심 방향을 지키려고 골반을 뒤로 눕혀(젖힘 −0.6·−0.3에서 약 23°) 허벅지가 그만큼 들린다.
    /// 그래서 허벅지 값은 젖힌 상태에서 수평이 되도록 잡았다.
    /// </summary>
    public class CharacterPose : MonoBehaviour
    {
        [SerializeField] private VrmLoader loader;

        [Header("다리 꼬기 (근육값 −1~1, 실측 대기)")]
        [Tooltip("오른 다리를 위로 꼰다. 끄면 왼 다리가 위다. 베젤을 짚고 기대는 팔은 위 다리의 반대쪽이다.")]
        [SerializeField] private bool rightOverLeft = true;

        [Tooltip("아래 다리 허벅지(Upper Leg Front-Back). 젖히지 않았다면 −0.67이 수평이고, 지금 젖힘에서는 −0.42가 수평이다.")]
        [SerializeField, Range(-1f, 1f)] private float bottomThighForward = -0.42f;

        [Tooltip("아래 다리 정강이(Lower Leg Stretch). −0.12면 수직으로 내려온다.")]
        [SerializeField, Range(-1f, 1f)] private float bottomKneeBend = -0.12f;

        [Tooltip("아래 다리를 안으로 모으는 정도(Upper Leg In-Out, 음수가 안쪽)")]
        [SerializeField, Range(-1f, 1f)] private float bottomLegIn = 0f;

        [Tooltip("위 다리 허벅지. 아래 무릎에 얹히도록 조금 더 든다.")]
        [SerializeField, Range(-1f, 1f)] private float topThighForward = -0.58f;

        [Tooltip("위 다리 정강이. 허벅지가 든 만큼 더 굽혀야 수직으로 늘어진다.")]
        [SerializeField, Range(-1f, 1f)] private float topKneeBend = -0.3f;

        [Tooltip("위 다리를 가로질러 넘기는 정도(Upper Leg In-Out, 음수가 안쪽)")]
        [SerializeField, Range(-1f, 1f)] private float topLegIn = -0.45f;

        [Tooltip("발끝을 내리는 정도(Foot Up-Down, 음수가 발끝 아래)")]
        [SerializeField, Range(-1f, 1f)] private float footPoint = -0.5f;

        [Header("상체")]
        [Tooltip("허리를 뒤로 젖히는 정도(Spine Front-Back, 음수가 뒤)")]
        [SerializeField, Range(-1f, 1f)] private float recline = -0.6f;

        [Tooltip("가슴을 뒤로 젖히는 정도(Chest Front-Back, 음수가 뒤)")]
        [SerializeField, Range(-1f, 1f)] private float chestRecline = -0.3f;

        [Tooltip("기대는 팔 쪽으로 허리를 기울이는 정도(Spine Left-Right)")]
        [SerializeField, Range(-1f, 1f)] private float leanToProp = 0.2f;

        [Header("손 짚기 (엉덩이 폭 단위: x 바깥쪽, y 위, z 앞)")]
        // 엉덩이 뼈가 베젤 선에 맞춰져 있어, 화면에서 베젤을 짚으려면 손목이 엉덩이 높이 근처에 와야 한다.
        [Tooltip("위 다리 쪽 손. 엉덩이 옆 조금 뒤를 짚는다.")]
        [SerializeField] private Vector3 nearHand = new Vector3(1.4f, -0.1f, -1.2f);

        [Tooltip("반대쪽 손. 옆으로 멀리, 뒤를 짚어 몸을 기댄다.")]
        [SerializeField] private Vector3 propHand = new Vector3(2.6f, -0.1f, -1f);

        [Tooltip("팔꿈치가 향할 쪽(같은 단위 방향). 뒤로 굽혀야 정면에서 팔이 곧게 뻗어 보인다.")]
        [SerializeField] private Vector3 elbowHint = new Vector3(0.15f, 0f, -1f);

        [Tooltip("IK 전에 팔을 내려 두는 정도(Arm Down-Up). 팔이 비틀리는 기준이 된다.")]
        [SerializeField, Range(-1f, 1f)] private float armsDown = -0.62f;

        [Tooltip("손목을 젖혀 손바닥을 바닥에 붙이는 정도(Hand Down-Up)")]
        [SerializeField, Range(-1f, 1f)] private float wristBend = 0.5f;

        [Header("발 까딱이기")]
        [Tooltip("위 다리 정강이가 오가는 폭(근육값)")]
        [SerializeField, Range(0f, 1f)] private float bobAmount = 0.1f;

        [Tooltip("초당 까딱이는 횟수")]
        [SerializeField, Min(0f)] private float bobFrequency = 0.6f;

        /// <summary>지금 포즈를 입히고 있는지. 휴머노이드가 아니면 T 포즈로 둔다(사양서 8-2).</summary>
        public bool IsPosing => _handler != null;

        /// <summary>오른 다리가 위인지.</summary>
        public bool RightOverLeft => rightOverLeft;

        /// <summary>위 다리 쪽 손의 목표점(월드).</summary>
        public Vector3 NearHandTarget { get; private set; }

        /// <summary>기대는 손의 목표점(월드).</summary>
        public Vector3 PropHandTarget { get; private set; }

        // 팔을 끝까지 펴면 팔꿈치 방향이 정해지지 않아 떨린다. 이만큼만 편다.
        private const float MaxReach = 0.995f;
        private const float MinReach = 0.05f;

        private enum Part { UpperLegFrontBack, UpperLegInOut, LowerLegStretch, FootUpDown, ArmDownUp, HandDownUp }

        private static readonly string[] PartNames =
        {
            "Upper Leg Front-Back", "Upper Leg In-Out", "Lower Leg Stretch", "Foot Up-Down", "Arm Down-Up", "Hand Down-Up",
        };

        private const int Left = 0;
        private const int Right = 1;
        private static readonly string[] SideNames = { "Left ", "Right " };

        private static int[,] s_parts;
        private static int s_spine = -1;
        private static int s_chest = -1;
        private static int s_spineSide = -1;

        private HumanPoseHandler _handler;
        private HumanPose _pose;
        private Animator _animator;
        private float _time;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (loader == null) loader = GetComponent<VrmLoader>();
            if (s_parts == null) FindMuscles();
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

            int top = rightOverLeft ? Right : Left;
            int bottom = 1 - top;
            float bob = Mathf.Sin(time * bobFrequency * Mathf.PI * 2f) * bobAmount;

            Set(bottom, Part.UpperLegFrontBack, bottomThighForward);
            Set(bottom, Part.LowerLegStretch, bottomKneeBend);
            Set(bottom, Part.UpperLegInOut, bottomLegIn);
            Set(top, Part.UpperLegFrontBack, topThighForward);
            Set(top, Part.LowerLegStretch, topKneeBend + bob);
            Set(top, Part.UpperLegInOut, topLegIn);
            for (int side = Left; side <= Right; side++)
            {
                Set(side, Part.FootUpDown, footPoint);
                Set(side, Part.ArmDownUp, armsDown);
                Set(side, Part.HandDownUp, wristBend);
            }
            SetMuscle(s_spine, recline);
            SetMuscle(s_chest, chestRecline);
            // Spine Left-Right는 양수가 캐릭터 오른쪽으로 기운다(실측). 기대는 팔은 아래 다리 쪽이다.
            SetMuscle(s_spineSide, bottom == Right ? leanToProp : -leanToProp);
            _handler.SetHumanPose(ref _pose);

            PlaceHands(top);
        }

        // 엉덩이 폭을 잣대로 두 손의 목표점을 정하고 팔을 뻗는다. 위 다리 쪽 손은 가까이, 반대쪽은 멀리 짚어 기댄다.
        private void PlaceHands(int top)
        {
            Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform leftLeg = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform rightLeg = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (hips == null || leftLeg == null || rightLeg == null) return;

            float unit = Vector3.Distance(leftLeg.position, rightLeg.position);
            Transform body = _animator.transform;

            NearHandTarget = BodyPoint(hips.position, body, unit, nearHand, top);
            PropHandTarget = BodyPoint(hips.position, body, unit, propHand, 1 - top);
            Reach(top, NearHandTarget, BodyDirection(body, elbowHint, top));
            Reach(1 - top, PropHandTarget, BodyDirection(body, elbowHint, 1 - top));
        }

        // 몸 기준 좌표(x는 그쪽 바깥)를 월드 점으로. 캐릭터는 +Z를 보고, 오른쪽이 +X다.
        private static Vector3 BodyPoint(Vector3 origin, Transform body, float unit, Vector3 offset, int side)
        {
            return origin + BodyDirection(body, offset, side) * unit;
        }

        private static Vector3 BodyDirection(Transform body, Vector3 offset, int side)
        {
            float outward = side == Right ? 1f : -1f;
            return body.right * (offset.x * outward) + body.up * offset.y + body.forward * offset.z;
        }

        // 두 관절 IK: 어깨-팔꿈치-손목이 이루는 삼각형을 코사인 법칙으로 풀고, 팔꿈치는 힌트 쪽으로 굽힌다.
        private void Reach(int side, Vector3 target, Vector3 hint)
        {
            Transform upper = _animator.GetBoneTransform(side == Right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            Transform lower = _animator.GetBoneTransform(side == Right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            Transform hand = _animator.GetBoneTransform(side == Right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (upper == null || lower == null || hand == null) return;

            Vector3 shoulder = upper.position;
            float upperLength = Vector3.Distance(shoulder, lower.position);
            float lowerLength = Vector3.Distance(lower.position, hand.position);
            Vector3 toTarget = target - shoulder;
            if (toTarget.sqrMagnitude < 0.000001f || upperLength <= 0f || lowerLength <= 0f) return;

            float reach = Mathf.Clamp(toTarget.magnitude,
                Mathf.Abs(upperLength - lowerLength) + MinReach * lowerLength, (upperLength + lowerLength) * MaxReach);
            Vector3 direction = toTarget.normalized;
            float cos = Mathf.Clamp((upperLength * upperLength + reach * reach - lowerLength * lowerLength)
                                    / (2f * upperLength * reach), -1f, 1f);
            Vector3 bend = Vector3.ProjectOnPlane(hint, direction);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(-_animator.transform.forward, direction);
            bend.Normalize();

            Vector3 elbow = shoulder + direction * (upperLength * cos) + bend * (upperLength * Mathf.Sqrt(1f - cos * cos));
            upper.rotation = Quaternion.FromToRotation(lower.position - shoulder, elbow - shoulder) * upper.rotation;
            Vector3 wrist = shoulder + direction * reach;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, wrist - lower.position) * lower.rotation;
        }

        private void Set(int side, Part part, float value)
        {
            SetMuscle(s_parts[side, (int)part], value);
        }

        private void SetMuscle(int index, float value)
        {
            if (index >= 0) _pose.muscles[index] = Mathf.Clamp(value, -1f, 1f);
        }

        private void Bind(GameObject model)
        {
            Release();
            var animator = model != null ? model.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman) return;

            _animator = animator;
            _handler = new HumanPoseHandler(animator.avatar, animator.transform);
            _pose = new HumanPose();
            // 몸통 위치·회전과 여기서 다루지 않는 근육은 불러온 자세 그대로 둔다.
            _handler.GetHumanPose(ref _pose);
            Apply(_time);
        }

        private void Release()
        {
            _handler?.Dispose();
            _handler = null;
            _animator = null;
        }

        private static void FindMuscles()
        {
            s_parts = new int[2, PartNames.Length];
            for (int side = Left; side <= Right; side++)
            {
                for (int part = 0; part < PartNames.Length; part++) s_parts[side, part] = Find(SideNames[side] + PartNames[part]);
            }
            s_spine = Find("Spine Front-Back");
            s_chest = Find("Chest Front-Back");
            s_spineSide = Find("Spine Left-Right");
        }

        private static int Find(string muscle)
        {
            int index = System.Array.IndexOf(HumanTrait.MuscleName, muscle);
            if (index < 0) Debug.LogWarning($"CharacterPose: 근육 '{muscle}'을 찾지 못했습니다.");
            return index;
        }
    }
}
