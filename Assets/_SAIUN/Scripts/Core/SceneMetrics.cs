using UnityEngine;

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// 창·레이어·카메라의 확정 수치를 한곳에 모은다.
    /// 사양서 8장 확정값과, 그로부터 계산으로 정해지는 파생값만 둔다.
    /// 취향으로 정하는 색·폰트는 여기 두지 않고 각 뷰의 [SerializeField]로 남긴다.
    /// </summary>
    public static class SceneMetrics
    {
        // ---- 창 (사양서 8장 확정값) ----
        public const int WindowWidth = 480;
        public const int WindowHeight = 680;

        // ---- 레이어 높이 (사양서 2-1 확정값) ----
        public const int SkyLayerHeight = 340;
        public const int SceneLayerHeight = 272;
        public const int BottomBarHeight = 68;

        /// <summary>
        /// 월드 1유닛이 차지하는 화면 픽셀 수.
        /// Canvas의 Reference Pixels Per Unit과 같은 100으로 맞춰서,
        /// UI 좌표와 월드 좌표의 환산이 1:1이 되게 한다.
        /// </summary>
        public const float PixelsPerUnit = 100f;

        /// <summary>
        /// 카메라 Orthographic Size. 화면 높이 절반에 해당하는 월드 단위다.
        /// PixelsPerUnit을 100으로 고정하기 위해 창 높이에서 계산한다.
        /// 680 / (2 × 100) = 3.4
        /// </summary>
        public const float CameraOrthographicSize = WindowHeight / (2f * PixelsPerUnit);

        // ---- 아이소메트릭 시점 (사양서 v1.1 7-4: 위 45도 / 측면 45도) ----
        public const float CameraPitchDegrees = 45f;
        public const float CameraYawDegrees = 45f;

        /// <summary>
        /// 카메라와 씬 원점 사이의 거리. Orthographic이라 화면 크기에는 영향이 없고 클리핑과 그림자에만 쓰인다.
        /// URP의 Shadow Distance는 카메라에서부터 재므로, 그 값보다 확실히 가깝게 둬야 씬이 그림자 범위 안에 들어온다.
        /// </summary>
        public const float CameraDistance = 10f;

        public const float CameraNearClip = 0.1f;
        public const float CameraFarClip = CameraDistance * 3f;

        /// <summary>URP 에셋에 설정한 Shadow Distance. 여기서는 검증용으로만 쓴다.</summary>
        public const float ShadowDistance = 20f;

        /// <summary>화면 픽셀 길이를 월드 단위로 바꾼다. 사양서 8-4의 베젤 좌표 계산에 쓴다.</summary>
        public static float PixelsToWorld(float pixels) => pixels / PixelsPerUnit;

        /// <summary>월드 길이를 화면 픽셀로 바꾼다.</summary>
        public static float WorldToPixels(float worldUnits) => worldUnits * PixelsPerUnit;
    }
}
