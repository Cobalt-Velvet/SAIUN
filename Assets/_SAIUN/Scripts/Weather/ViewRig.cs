using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>하늘·정원을 보는 눈: 올려다보는 각, 초점(화면 높이 1 기준), 오른쪽으로 돈 각.</summary>
    public struct EyeView
    {
        public float TiltUp;
        public float Focal;
        public float Yaw;
    }

    /// <summary>
    /// 창 모양에 맞춰 정원 카메라와 하늘의 눈을 함께 맞춘다(카드·사이드바).
    ///  - 화소당 각도는 카드와 같게 두고(초점 약 646 논리 화소), 지평선은 창 아래에서 같은 높이(약 179 논리 화소)에 둔다.
    ///    창이 길어지면 하늘이 위로 더 열리고, 바다·정원·하단 바는 아래에서 카드와 같은 자리다.
    ///  - 폭이 좁아지면 좁아진 절반만큼 오른쪽으로 돌아, 탑·윤슬·화분이 창 오른쪽 가장자리에서 카드와 같은 거리에 선다.
    ///    하늘과 정원 카메라를 같은 각만큼 돌리므로 둘은 여전히 한 눈이다.
    ///  - 하늘 텍스처는 논리 크기로 그리되 화소 수에 상한을 둔다(큰 화면·높은 배율에서 GPU를 아낀다).
    /// </summary>
    public class ViewRig : MonoBehaviour
    {
        [SerializeField] private WindowController window;
        [SerializeField] private Camera eye;
        [SerializeField] private SkyView sky;

        [Tooltip("카메라에 붙은 비. 화각이 넓어지면 더 위에서 떨어지게 맞춘다.")]
        [SerializeField] private RainEffect rain;

        [Tooltip("하늘 텍스처 화소 수 상한")]
        [SerializeField, Min(10000)] private int maxSkyPixels = 450000;

        /// <summary>지금 눈.</summary>
        public EyeView Current { get; private set; } = ViewFor(new Vector2Int(SceneMetrics.WindowWidth, SceneMetrics.WindowHeight));

        private void Awake()
        {
            if (window == null) window = FindFirstObjectByType<WindowController>();
            if (eye == null) eye = Camera.main;
            if (sky == null) sky = FindFirstObjectByType<SkyView>();
            if (window != null) window.OnLayoutChanged += Apply;
        }

        private void OnDestroy()
        {
            if (window != null) window.OnLayoutChanged -= Apply;
        }

        /// <summary>논리 창 크기에 맞는 눈. 카드(480×680)면 사양 그대로(14°, 0.95, 0°)다.</summary>
        public static EyeView ViewFor(Vector2Int logical)
        {
            float focalPixels = SceneMetrics.CameraFocal * SceneMetrics.WindowHeight;
            float horizonFromBottom = SceneMetrics.WindowHeight / 2f
                                      - SceneMetrics.CameraFocal * Mathf.Tan(SceneMetrics.CameraTiltUpDegrees * Mathf.Deg2Rad) * SceneMetrics.WindowHeight;
            float height = Mathf.Max(1, logical.y);
            float focal = focalPixels / height;
            float shift = (SceneMetrics.WindowWidth - logical.x) / 2f;
            return new EyeView
            {
                TiltUp = Mathf.Atan((0.5f - horizonFromBottom / height) / focal) * Mathf.Rad2Deg,
                Focal = focal,
                Yaw = Mathf.Atan(shift / focalPixels) * Mathf.Rad2Deg,
            };
        }

        /// <summary>창 모양에 맞춘다. 창이 바뀔 때 WindowController가 부른다.</summary>
        internal void Apply(WindowLayout layout)
        {
            EyeView view = ViewFor(layout.Logical);
            Current = view;

            if (eye != null)
            {
                eye.orthographic = false;
                eye.fieldOfView = 2f * Mathf.Atan(0.5f / view.Focal) * Mathf.Rad2Deg;
                eye.transform.SetPositionAndRotation(SceneMetrics.CameraPosition,
                    Quaternion.Euler(-view.TiltUp, SceneMetrics.CameraYawDegrees + view.Yaw, 0f));
            }

            if (sky != null)
            {
                sky.Resize(SkySize(layout.Logical));
                sky.SetView(view.TiltUp, view.Focal, view.Yaw);
            }

            if (rain != null) rain.FitView(SceneMetrics.CameraFocal / view.Focal);
        }

        // 논리 크기 그대로 그리되 상한을 넘으면 같은 비율로 줄인다.
        private Vector2Int SkySize(Vector2Int logical)
        {
            float pixels = (float)logical.x * logical.y;
            float scale = pixels > maxSkyPixels ? Mathf.Sqrt(maxSkyPixels / pixels) : 1f;
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(logical.x * scale)), Mathf.Max(1, Mathf.RoundToInt(logical.y * scale)));
        }
    }
}
