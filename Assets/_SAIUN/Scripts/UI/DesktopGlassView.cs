using System.Collections;
using System.Threading;
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
        [SerializeField] private RawImage tintOverlay;
        [SerializeField] private WindowController windowController;

        [Header("흐림")]
        [Tooltip("몇 분의 1로 줄여서 읽을지. 너무 크게 잡으면 확대할 때 계단이 보인다.")]
        [Range(2, 16)]
        [SerializeField] private int downscale = 3;

        [Tooltip("GPU에서 부드럽게 뭉개는 횟수. 줄였다 키우기를 반복해 계단을 없앤다.")]
        [Range(0, 4)]
        [SerializeField] private int blurPasses = 3;

        [Tooltip("뒷배경이 움직일 때 다시 읽는 간격(초).")]
        [Range(0.016f, 0.3f)]
        [SerializeField] private float refreshInterval = 0.06f;

        [Tooltip("뒷배경이 멈춰 있을 때 늦추는 간격(초). 가만히 둔 위젯이 CPU를 계속 먹지 않게 한다.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float idleInterval = 0.6f;

        [Tooltip("이보다 적게 바뀌면 멈춘 것으로 본다.")]
        [Range(0f, 0.05f)]
        [SerializeField] private float changeThreshold = 0.004f;

        [Tooltip("바탕화면 그림을 새로 받는 간격(초). 영상 벽지의 움직임이 이 주기로 갱신된다.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float wallpaperInterval = 0.3f;

        [Header("틴트")]
        [Tooltip("유리에 얹을 색.")]
        [SerializeField] private Color tint = SaiunPalette.Charcoal;

        [Tooltip("틴트 농도. 낮출수록 뒷배경이 그대로 보인다.")]
        [Range(0f, 1f)]
        [SerializeField] private float tintStrength = 0.22f;

        /// <summary>마지막 읽기가 성공했는지.</summary>
        public bool IsCapturing { get; private set; }

        /// <summary>마지막 읽기에 걸린 시간(밀리초).</summary>
        public double LastCaptureMilliseconds => _capture?.LastCaptureMilliseconds ?? 0d;

        /// <summary>실제로 읽어 올 크기. 창 크기를 축소 배율로 나눈 값이다.</summary>
        public Vector2Int CaptureSize => new Vector2Int(
            Mathf.Max(1, _windowSize.x / downscale),
            Mathf.Max(1, _windowSize.y / downscale));

        // 읽을 창 크기(실제 화소). 켜질 때 창 크기를 읽는다(창 모양이 바뀌면 WindowLayoutView가 다시 켠다).
        private Vector2Int _windowSize = new Vector2Int(SceneMetrics.WindowWidth, SceneMetrics.WindowHeight);

        // 이만큼 연달아 변화가 없으면 느린 간격으로 넘어간다.
        private const int IdleFramesBeforeSlowing = 8;

        private DesktopCapture _capture;
        private Coroutine _loop;
        private Thread _worker;
        private volatile bool _stopWorker;
        private int _captureCount;

        // 작업 스레드와 주고받는 값들.
        private readonly object _shared = new object();
        private Vector2Int _sharedPosition;
        private DesktopCapture.Source _sharedSource;
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
                ApplyTint();
            }
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // 에디터에서는 화면을 읽어도 게임 뷰가 아니라 에디터 창이 잡혀 의미가 없다.
            if (backdrop != null) backdrop.enabled = false;
#else
            _windowSize = new Vector2Int(Screen.width, Screen.height);
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

            StopWorker();
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

        // 유리 위에 팔레트 색을 얇게 깐다. 흐린 뒷배경이 하늘 색과 따로 놀지 않게 묶어 준다.
        private void ApplyTint()
        {
            if (tintOverlay == null) return;
            tintOverlay.texture = null;
            tintOverlay.color = SaiunPalette.WithAlpha(tint, tintStrength);
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
            // 창 설정이 끝나기 전에는 위치가 확정되지 않는다.
            while (windowController != null && !windowController.IsReady) yield return null;

            PublishPosition();
            StartWorker();

            while (true)
            {
                PublishPosition();

                if (_capture.ApplyToTexture())
                {
                    IsCapturing = true;
                    if (backdrop != null)
                    {
                        backdrop.enabled = true;
                        backdrop.texture = Smooth(_capture.Texture);
                    }

                    if (++_captureCount == 30)
                    {
                        Debug.Log($"DesktopGlassView: {_sharedSource} 읽기 {_capture.LastCaptureMilliseconds:F1}ms (작업 스레드)");
                    }
                }

                yield return null;
            }
        }

        /// <summary>창 위치와 읽기 대상을 작업 스레드에 넘긴다.</summary>
        private void PublishPosition()
        {
            Vector2Int position = windowController != null ? windowController.RefreshAndGetPosition() : Vector2Int.zero;
            DesktopCapture.Source source = ResolveSource();

            lock (_shared)
            {
                _sharedPosition = position;
                _sharedSource = source;
            }
        }

        private void StartWorker()
        {
            if (_worker != null) return;

            _stopWorker = false;
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "SaiunDesktopGlass",
            };
            _worker.Start();
        }

        private void StopWorker()
        {
            if (_worker == null) return;

            _stopWorker = true;
            if (!_worker.Join(1000)) Debug.LogWarning("DesktopGlassView: 작업 스레드가 제때 끝나지 않았습니다.");
            _worker = null;
        }

        private void WorkerLoop()
        {
            DesktopCapture capture = _capture;
            Vector2Int windowSize = _windowSize;
            float nextWallpaper = 0f;
            int idleFrames = 0;
            var lastPosition = new Vector2Int(int.MinValue, int.MinValue);
            var clock = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                while (!_stopWorker)
                {
                    Vector2Int position;
                    DesktopCapture.Source source;
                    lock (_shared)
                    {
                        position = _sharedPosition;
                        source = _sharedSource;
                    }

                    // 벽지 그림은 드물게 새로 받고, 창을 따라 잘라내는 일은 자주 한다.
                    float now = (float)clock.Elapsed.TotalSeconds;
                    bool refreshSource = now >= nextWallpaper;
                    if (refreshSource) nextWallpaper = now + wallpaperInterval;

                    capture.Capture(source, position.x, position.y,
                        windowSize.x, windowSize.y, refreshSource);

                    // 창이 움직였거나 뒷배경이 바뀌었으면 빠르게, 아니면 느긋하게 돈다.
                    bool moved = position != lastPosition;
                    lastPosition = position;
                    if (moved || capture.LastChangeAmount > changeThreshold) idleFrames = 0;
                    else idleFrames++;

                    // 몇 번 연달아 그대로면 느린 쪽으로 넘어간다.
                    float interval = idleFrames > IdleFramesBeforeSlowing ? idleInterval : refreshInterval;
                    Thread.Sleep(Mathf.Max(1, Mathf.RoundToInt(interval * 1000f)));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"DesktopGlassView: 작업 스레드가 멈췄습니다. {e}");
            }
            finally
            {
                // GDI 자원은 만든 스레드에서 놓는다.
                capture.ReleaseGdiResources();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyTint();
        }
#endif
    }
}
