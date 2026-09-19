using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Character
{
    /// <summary>
    /// 캐릭터를 창 하단 베젤에 걸터앉힌다 (P3-02, 사양서 v1.1 2-2·8-3·8-4).
    /// 엉덩이(Hips 본)가 유리 카드 아래 모서리(= 창 하단 베젤)의 지정 픽셀에 오도록 캐릭터 자리를 옮긴다.
    /// 상반신은 카드 안에, 다리는 카드 아래 다리 영역(바탕화면 위)에 그려진다.
    /// 2026-09-19 사용자 지시: 베젤은 "하단"이다. 캐릭터는 정면(화면)을 본다.
    /// 카메라가 고정 Orthographic이라 픽셀 차이를 카메라 축 방향 이동으로 바꾸면 된다(8-4의 수식을 일반화).
    /// 창을 물리 픽셀로 다루므로 DPI 배율 보정(8-5)은 필요 없다. OS 배율이 바뀌어도 창 픽셀과 Unity 화면 픽셀이 같다.
    /// </summary>
    public class BezelAnchor : MonoBehaviour
    {
        [SerializeField] private VrmLoader loader;

        [Tooltip("옮길 캐릭터 자리. 보통 VrmLoader가 모델을 붙이는 트랜스폼이다.")]
        [SerializeField] private Transform characterRoot;

        [Header("자리 (실측 대기)")]
        [Tooltip("엉덩이를 둘 창 픽셀 x. 사양서 2-3 '캐릭터 좌측'.")]
        [SerializeField] private float seatX = 84f;

        [Tooltip("하단 베젤에서 엉덩이를 내리는 픽셀. 양수면 아래(창 밖)로 내려 앉는다.")]
        [SerializeField] private float seatDrop;

        [Tooltip("화면을 정면으로 보게 돌린다")]
        [SerializeField] private bool faceViewer = true;

        /// <summary>엉덩이를 둘 창 픽셀(카드 기준, y는 아래로).</summary>
        public Vector2 SeatPixel => new Vector2(seatX, SceneMetrics.WindowHeight + seatDrop);

        /// <summary>마지막으로 잰 엉덩이 픽셀.</summary>
        public Vector2 HipPixel { get; private set; }

        private Animator _animator;

        private void Awake()
        {
            if (loader == null) loader = GetComponent<VrmLoader>();
            if (characterRoot == null) characterRoot = transform;
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
        }

        // 포즈(Update) 뒤에 맞춘다. 엉덩이 높이는 다리를 흔들어도 변하지 않아, 처음 한 번 옮긴 뒤로는 거의 움직이지 않는다.
        private void LateUpdate()
        {
            Align();
        }

        /// <summary>엉덩이를 자리 픽셀에 맞춘다. 테스트는 직접 부른다.</summary>
        internal void Align()
        {
            if (_animator == null) return;
            Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return;

            Quaternion camera = SceneMetrics.CameraRotation;
            // 카메라를 마주 보게: 모델 앞(+Z)이 카메라 쪽, 위가 화면 위.
            if (faceViewer) characterRoot.rotation = Quaternion.LookRotation(-(camera * Vector3.forward), camera * Vector3.up);

            HipPixel = SceneMetrics.WorldToWindowPixels(hips.position);
            Vector2 delta = SeatPixel - HipPixel;
            if (delta.sqrMagnitude < 0.0001f) return;

            // 화면 y는 아래로 늘어나므로 위쪽 축에는 부호를 뒤집어 준다.
            characterRoot.position += camera * Vector3.right * SceneMetrics.PixelsToWorld(delta.x)
                                      - camera * Vector3.up * SceneMetrics.PixelsToWorld(delta.y);
            HipPixel = SeatPixel;
        }

        private void Bind(GameObject model)
        {
            _animator = model != null ? model.GetComponent<Animator>() : null;
            if (_animator != null && !_animator.isHuman) _animator = null;
            Align();
        }
    }
}
