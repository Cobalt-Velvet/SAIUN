using System.Collections;
using _SAIUN.Scripts.Core;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 창 뒤를 읽어 흐리게 깔아 준다.
    /// Windows의 Acrylic은 광도 레이어가 색을 눌러버려 뒤에 무엇이 있는지 알아볼 수 없다.
    /// 여기서는 직접 축소해서 받아 오므로 뒷배경의 형태와 색이 그대로 남는다.
    /// 무엇을 읽을지는 WindowController의 유리 모드가 정한다.
    /// </summary>
    public class DesktopGlassView : MonoBehaviour
    {
        [SerializeField] private RawImage backdrop;
        [SerializeField] private Image tintOverlay;
        [SerializeField] private WindowController windowController;

        [Header("흐림")]
        [Tooltip("몇 분의 1로 줄여서 읽을지. 너무 크게 잡으면 확대할 때 계단이 보인다.")]
        [Range(2, 16)]
        [SerializeField] private int downscale = 3;

        [Tooltip("GPU에서 부드럽게 뭉개는 횟수. 줄였다 키우기를 반복해 계단을 없앤다.")]
        [Range(0, 4)]
        [SerializeField] private int blurPasses = 3;

        [Tooltip("창 위치를 따라 배경을 다시 잘라내는 간격(초). 잘라내기만 하므로 싸다.")]
        [Range(0.016f, 0.3f)]
        [SerializeField] private float refreshInterval = 0.05f;

        [Tooltip("바탕화면 그림을 새로 받는 간격(초). 영상 벽지의 움직임이 이 주기로 갱신된다.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float wallpaperInterval = 0.3f;

        [Header("틴트")]
        [Tooltip("유리에 얹을 색.")]
        [SerializeField] private Color tint = SaiunPalette.DeepJungle;

        [Tooltip("틴트 농도. 낮출수록 뒷배경이 그대로 보인다.")]
        [Range(0f, 1f)]
        [SerializeField] private float tintStrength = 0.22f;

        /// <summary>마지막 읽기가 성공했는지.</summary>
        public bool IsCapturing { get; private set; }

        /// <summary>마지막 읽기에 걸린 시간(밀리초).</summary>
        public double LastCaptureMilliseconds => _capture?.LastCaptureMilliseconds ?? 0d;

        /// <summary>실제로 읽어 올 크기. 창 크기를 축소 배율로 나눈 값이다.</summary>
        public Vector2Int CaptureSize => new Vector2Int(
            Mathf.Max(1, SceneMetrics.WindowWidth / downscale),
            Mathf.Max(1, SceneMetrics.WindowHeight / downscale));

        private DesktopCapture _capture;
        private Coroutine _loop;
        private int _captureCount;
        private float _nextWallpaperRefresh;
        private RenderTexture _blurA;
        private RenderTexture _blurB;

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

            ReleaseRenderTexture(ref _blurA);
            ReleaseRenderTexture(ref _blurB);
        }

        /// <summary>
        /// 줄였다 키우기를 반복해 계단을 없앤다.
        /// 텍스처가 작아서 GPU 비용은 사실상 없다.
        /// </summary>
        private Texture Smooth(Texture source)
        {
            if (blurPasses <= 0) return source;

            // 같은 크기끼리 옮기면 아무 일도 일어나지 않는다. 반드시 줄였다 키워야 뭉개진다.
            int width = Mathf.Max(2, source.width / 2);
            int height = Mathf.Max(2, source.height / 2);
            EnsureRenderTexture(ref _blurA, width, height);
            EnsureRenderTexture(ref _blurB, Mathf.Max(2, width / 2), Mathf.Max(2, height / 2));

            Graphics.Blit(source, _blurA);
            for (int i = 0; i < blurPasses; i++)
            {
                Graphics.Blit(_blurA, _blurB);   // 절반으로 줄이며 평균
                Graphics.Blit(_blurB, _blurA);   // 다시 키우며 보간
            }
            return _blurA;
        }

        private static void EnsureRenderTexture(ref RenderTexture texture, int width, int height)
        {
            if (texture != null && texture.width == width && texture.height == height) return;

            ReleaseRenderTexture(ref texture);
            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "GlassBlur",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.Create();
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
            texture = null;
        }

        /// <summary>틴트를 바꾼다.</summary>
        public void SetTint(Color color, float strength)
        {
            tint = color;
            tintStrength = Mathf.Clamp01(strength);
            if (tintOverlay != null) tintOverlay.color = SaiunPalette.WithAlpha(tint, tintStrength);
        }

        /// <summary>유리 모드에 맞는 읽기 대상을 고른다.</summary>
        private DesktopCapture.Source ResolveSource()
        {
            return windowController != null && windowController.CurrentGlass == WindowController.GlassMode.DesktopBlur
                ? DesktopCapture.Source.Screen
                : DesktopCapture.Source.WallpaperLayer;
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
            // 벽지 그림은 드물게 새로 받고, 창을 따라 잘라내는 일은 자주 한다.
            bool refreshSource = Time.realtimeSinceStartup >= _nextWallpaperRefresh;
            if (refreshSource) _nextWallpaperRefresh = Time.realtimeSinceStartup + wallpaperInterval;

            IsCapturing = _capture.Capture(
                ResolveSource(), position.x, position.y,
                SceneMetrics.WindowWidth, SceneMetrics.WindowHeight, refreshSource);

            if (backdrop != null)
            {
                backdrop.enabled = IsCapturing;
                if (IsCapturing) backdrop.texture = Smooth(_capture.Texture);
            }

            // 실제 비용을 한 번은 남겨 둔다. 간격을 조절할 때 근거가 된다.
            if (++_captureCount == 30)
            {
                Debug.Log($"DesktopGlassView: {ResolveSource()} 읽기 {_capture.LastCaptureMilliseconds:F1}ms, 간격 {refreshInterval:F3}s");
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (tintOverlay != null) tintOverlay.color = SaiunPalette.WithAlpha(tint, tintStrength);
        }
#endif
    }
}
