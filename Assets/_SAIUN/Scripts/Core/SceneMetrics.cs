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

        /// <summary>Scene Layer의 위·아래 경계. 창 왼쪽 위를 원점으로 아래로 잰 픽셀이다.</summary>
        public const int SceneLayerTop = SkyLayerHeight;
        public const int SceneLayerBottom = SkyLayerHeight + SceneLayerHeight;

        // ---- 화단 (사양서 8장 확정값) ----
        public const int FlowerbedColumns = 6;
        public const int FlowerbedRows = 2;

        /// <summary>
        /// 월드 1유닛이 차지하는 화면 픽셀 수.
        /// Canvas의 Reference Pixels Per Unit과 같은 100으로 맞춰서,
        /// UI 좌표와 월드 좌표의 환산이 1:1이 되게 한다.
        /// </summary>
        public const float PixelsPerUnit = 100f;

        // ---- 눈 (2026-09-29 사용자 선택 "수평선과 같은 눈높이") ----
        // 사양서 v1.1 7-4의 아이소메트릭(위 45°·옆 45° 정사영)을 바꿨다. 정원을 하늘과 같은 카메라로 본다:
        // 원근, 지평선에서 14° 올려다보고 세로 화각도 같다. 그래서 데크 선이 바다 수평선으로 모이고,
        // 화분은 앉은 눈높이에서 살짝 내려다보여 작물이 바다와 노을을 배경으로 선다.

        /// <summary>카메라가 지평선에서 올려다보는 각(도). 하늘 셰이더의 VIEW_PITCH와 같아야 한다.</summary>
        public const float CameraTiltUpDegrees = 14f;

        /// <summary>카메라가 옆으로 돈 각(도). 화분 긴 변이 수평선과 거의 나란하게 보인다.</summary>
        public const float CameraYawDegrees = 20f;

        /// <summary>초점 거리(화면 높이를 1로 잰 값). 하늘 셰이더의 VIEW_FOCAL과 같아야 한다.</summary>
        public const float CameraFocal = 0.95f;

        /// <summary>데크 윗면(월드 y = 0)에서 눈까지의 높이. 앉은 눈높이쯤이라 화분 흙이 살짝 보인다.</summary>
        public const float CameraEyeHeight = 0.8f;

        public const float CameraNearClip = 0.1f;
        public const float CameraFarClip = 40f;

        /// <summary>URP 에셋에 설정한 Shadow Distance. 여기서는 검증용으로만 쓴다.</summary>
        public const float ShadowDistance = 20f;

        /// <summary>
        /// 유리 배경(하늘) 캔버스를 카메라에서 떨어뜨리는 거리.
        /// 씬의 어떤 오브젝트보다 뒤에 있어야 깊이 테스트로 가려지고, Far 클립 안에 있어야 그려진다.
        /// </summary>
        public const float BackdropPlaneDistance = CameraFarClip - 1f;

        /// <summary>
        /// 1 월드 유닛이 PixelsPerUnit 화소로 보이는 깊이. 카메라에 붙인 비·바람 효과를 여기 두면 화소 단위로 잰 크기가 그대로 맞는다.
        /// 0.95 × 680 / 100 = 6.46
        /// </summary>
        public const float UnitDepth = CameraFocal * WindowHeight / PixelsPerUnit;

        /// <summary>세로 화각(도). 2·atan(0.5 / 초점) ≈ 55.5°.</summary>
        public static float CameraFieldOfView => 2f * Mathf.Atan(0.5f / CameraFocal) * Mathf.Rad2Deg;

        /// <summary>카메라 자리(월드). 데크 윗면 위 눈높이다.</summary>
        public static Vector3 CameraPosition => new Vector3(0f, CameraEyeHeight, 0f);

        /// <summary>카메라 회전. 투영 계산에 쓴다.</summary>
        public static Quaternion CameraRotation => Quaternion.Euler(-CameraTiltUpDegrees, CameraYawDegrees, 0f);

        /// <summary>화면 픽셀 길이를 월드 단위로 바꾼다.</summary>
        public static float PixelsToWorld(float pixels) => pixels / PixelsPerUnit;

        /// <summary>월드 길이를 화면 픽셀로 바꾼다.</summary>
        public static float WorldToPixels(float worldUnits) => worldUnits * PixelsPerUnit;

        /// <summary>
        /// 월드 좌표가 창의 어느 픽셀에 그려지는지 계산한다(원근).
        /// 원점은 창 왼쪽 위이고 y는 아래로 늘어난다(사양서 2-1 레이아웃 표기와 같다). 카메라 뒤의 점은 NaN이다.
        /// </summary>
        public static Vector2 WorldToWindowPixels(Vector3 world)
        {
            Vector3 view = Quaternion.Inverse(CameraRotation) * (world - CameraPosition);
            if (view.z <= 1e-5f) return new Vector2(float.NaN, float.NaN);
            float scale = CameraFocal * WindowHeight / view.z;
            return new Vector2(WindowWidth / 2f + view.x * scale, WindowHeight / 2f - view.y * scale);
        }

        /// <summary>창 픽셀을 지나는 시선 방향(월드, 길이 1이 아님: 카메라 앞 깊이 1까지의 벡터).</summary>
        public static Vector3 WindowPixelRay(Vector2 pixels)
        {
            float span = CameraFocal * WindowHeight;
            var view = new Vector3((pixels.x - WindowWidth / 2f) / span, (WindowHeight / 2f - pixels.y) / span, 1f);
            return CameraRotation * view;
        }

        /// <summary>
        /// 창 픽셀 위치에 보이는, 높이 <paramref name="height"/>인 수평면 위의 월드 점.
        /// <see cref="WorldToWindowPixels"/>의 역연산이다. 화단 원점처럼 화면 배치로 정한 값을 월드로 옮길 때 쓴다.
        /// 눈보다 낮은 면은 지평선 아래 픽셀에서만 만난다(만나지 않으면 NaN).
        /// </summary>
        public static Vector3 WindowPixelsToGround(Vector2 pixels, float height = 0f)
        {
            Vector3 ray = WindowPixelRay(pixels);
            float along = (height - CameraEyeHeight) / ray.y;
            if (float.IsInfinity(along) || along <= 0f) return new Vector3(float.NaN, float.NaN, float.NaN);
            return CameraPosition + ray * along;
        }
    }
}
