using System.Collections;
using _SAIUN.Scripts.Core;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 창 뒤 화면을 읽어 흐리게 깔아 준다.
    /// Windows의 Acrylic은 광도 레이어가 색을 눌러버려 뒤에 무엇이 있는지 알아볼 수 없다.
    /// 여기서는 화면을 직접 축소해서 받아 오므로 뒷배경의 형태와 색이 그대로 남는다.
    /// </summary>
    public class DesktopGlassView : MonoBehaviour
    {
        [SerializeField] private RawImage backdrop;
        [SerializeField] private Image tintOverlay;
        [SerializeField] private WindowController windowController;

        [Header("흐림")]
        [Tooltip("몇 분의 1로 줄여서 읽을지. 클수록 더 흐려지고 더 가볍다.")]
        [Range(2, 16)]
        [SerializeField] private int downscale = 10;

        [Tooltip("화면을 다시 읽는 간격(초). 0.066이면 초당 15번이다.")]
        [Range(0.016f, 0.5f)]
        [SerializeField] private float refreshInterval = 0.066f;

        [Header("틴트")]
        [Tooltip("유리에 얹을 색.")]
        [SerializeField] private Color tint = SaiunPalette.DeepJungle;

        [Tooltip("틴트 농도. 낮출수록 뒷배경이 그대로 보인다.")]
        [Range(0f, 1f)]
        [SerializeField] private float tintStrength = 0.22f;

        /// <summary>마지막 읽기가 성공했는지.</summary>
        public bool IsCapturing { get; private set; }

        /// <summary>실제로 읽어 올 크기. 창 크기를 축소 배율로 나눈 값이다.</summary>
        public Vector2Int CaptureSize => new Vector2Int(
            Mathf.Max(1, SceneMetrics.WindowWidth / downscale),
            Mathf.Max(1, SceneMetrics.WindowHeight / downscale));

        private DesktopCapture _capture;
        private Coroutine _loop;

        private void Awake()
        {
            if (backdrop == null) backdrop = GetComponent<RawImage>();
            if (windowController == null) windowController = FindFirstObjectByType<WindowController>();

            if (backdrop != null) backdrop.raycastTarget = false;
            if (tintOverlay != null)
            {
                tintOverlay.raycastTarget = false;
                tintOverlay.color = SaiunPalette.WithAlpha(tint, tintStrength);
            }
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // 에디터에서는 화면을 읽어도 게임 뷰가 아니라 에디터 창이 잡혀 의미가 없다.
            if (backdrop != null) backdrop.enabled = false;
#else
            _capture = new DesktopCapture(CaptureSize.x, CaptureSize.y);

            if (backdrop != null) backdrop.texture = _capture.Texture;
            _loop = StartCoroutine(CaptureLoop());
#endif
        }

        private void OnDisable()
        {
            if (_loop != null)
            {
                StopCoroutine(_loop);
                _loop = null;
            }

            _capture?.Dispose();
            _capture = null;
        }

        /// <summary>틴트를 바꾼다.</summary>
        public void SetTint(Color color, float strength)
        {
            tint = color;
            tintStrength = Mathf.Clamp01(strength);
            if (tintOverlay != null) tintOverlay.color = SaiunPalette.WithAlpha(tint, tintStrength);
        }

        private IEnumerator CaptureLoop()
        {
            var wait = new WaitForSecondsRealtime(refreshInterval);

            // 창 설정이 끝나기 전에는 위치가 확정되지 않는다.
            while (windowController != null && !windowController.IsReady) yield return null;

            while (true)
            {
                CaptureOnce();
                yield return wait;
            }
        }

        private void CaptureOnce()
        {
            if (_capture == null) return;

            // 창이 옮겨졌을 수 있으므로 읽기 직전에 위치를 다시 확인한다.
            Vector2Int position = windowController != null ? windowController.RefreshAndGetPosition() : Vector2Int.zero;
            IsCapturing = _capture.Capture(position.x, position.y, SceneMetrics.WindowWidth, SceneMetrics.WindowHeight);

            if (backdrop != null) backdrop.enabled = IsCapturing;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (tintOverlay != null) tintOverlay.color = SaiunPalette.WithAlpha(tint, tintStrength);
        }
#endif
    }
}
