using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// 화면의 한 영역을 축소해서 읽어 온다.
    /// 축소 자체가 흐림 효과라, 따로 블러를 돌리지 않아도 유리처럼 보인다.
    /// GDI가 평균을 내며 줄여주므로 셰이더가 필요 없다.
    /// </summary>
    public sealed class DesktopCapture : IDisposable
    {
        /// <summary>무엇을 읽을지.</summary>
        public enum Source
        {
            /// <summary>합성된 화면 전체. 뒤에 있는 다른 창까지 비치지만 자기 자신도 찍힌다.</summary>
            Screen,
            /// <summary>바탕화면 레이어만. 다른 창은 안 비치는 대신 자기 자신도 안 찍혀 되먹임이 없다.</summary>
            WallpaperLayer,
        }

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode); // spellchecker:ignore StretchBlt
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
            IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines,
            byte[] bits, ref BITMAPINFO info, uint usage); // spellchecker:ignore BITMAPINFO DIBits

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER // spellchecker:ignore BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public int bmiColors;
        }

        const uint SRCCOPY = 0x00CC0020;            // spellchecker:ignore SRCCOPY
        const int HALFTONE = 4;                     // 축소할 때 평균을 내는 모드
        const uint DIB_RGB_COLORS = 0;              // spellchecker:ignore DIB
        const uint PW_RENDERFULLCONTENT = 0x0002;   // spellchecker:ignore RENDERFULLCONTENT
        const string DESKTOP_CLASS = "Progman";     // spellchecker:ignore Progman
        const string DESKTOP_TITLE = "Program Manager";

        // 가상 화면(모든 모니터를 합친 영역) 범위.
        const int SM_XVIRTUALSCREEN = 76;   // spellchecker:ignore XVIRTUALSCREEN
        const int SM_YVIRTUALSCREEN = 77;   // spellchecker:ignore YVIRTUALSCREEN
        const int SM_CXVIRTUALSCREEN = 78;  // spellchecker:ignore CXVIRTUALSCREEN
        const int SM_CYVIRTUALSCREEN = 79;  // spellchecker:ignore CYVIRTUALSCREEN

        // 주변 색을 잴 때 쓰는 띠의 개수. 위·아래·왼쪽·오른쪽.
        const int AmbientStripCount = 4;

        /// <summary>축소해서 담아 둔 화면. BGRA 순서다.</summary>
        public Texture2D Texture { get; }

        public int Width { get; }
        public int Height { get; }

        /// <summary>마지막 읽기에 걸린 시간(밀리초). 갱신 간격을 정할 때 참고한다.</summary>
        public double LastCaptureMilliseconds { get; private set; }

        private readonly byte[] _buffer;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private BITMAPINFO _info;
        private bool _disposed;

        // 바탕화면 레이어를 담아 두는 버퍼. 바탕화면 전체 크기로 한 번만 만들어 재사용한다.
        private IntPtr _layerDC;
        private IntPtr _layerBitmap;
        private int _layerWidth;
        private int _layerHeight;
        private bool _layerFilled;

        // 주변 색 측정용. 띠 하나를 1픽셀로 줄여 담으므로 4픽셀이면 충분하다.
        private IntPtr _ambientDC;
        private IntPtr _ambientBitmap;
        private readonly byte[] _ambientBuffer = new byte[AmbientStripCount * 4];
        private BITMAPINFO _ambientInfo;

        public DesktopCapture(int width, int height)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);

            Texture = new Texture2D(Width, Height, TextureFormat.BGRA32, false)
            {
                name = "DesktopGlass",
                filterMode = FilterMode.Bilinear,   // 확대할 때 부드럽게 퍼지며 흐려진다
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            _buffer = new byte[Width * Height * 4];
            _info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                    biWidth = Width,
                    // Unity 텍스처는 아래에서 위로 채워진다. DIB도 양수 높이로 두면 상향식이라 그대로 맞는다.
                    biHeight = Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };

            _ambientInfo = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                    biWidth = AmbientStripCount,
                    biHeight = 1,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };
        }

        /// <summary>
        /// 창 바로 바깥을 둘러싼 띠의 평균 색을 잰다.
        /// 창 안쪽이 아니라 바깥을 읽으므로 자기 자신이 섞이지 않는다.
        /// 창 뒤에 무엇이 깔려 있든 그 색이 잡히므로, 유리가 주변에 반응하게 만들 수 있다.
        /// </summary>
        /// <summary>주변 색 측정 결과의 순서. 배열 첨자로 쓴다.</summary>
        public const int AmbientTop = 0;
        public const int AmbientBottom = 1;
        public const int AmbientLeft = 2;
        public const int AmbientRight = 3;

        public bool SampleAmbient(int x, int y, int width, int height, int margin, int thickness, Color[] colors)
        {
            if (_disposed || colors == null || colors.Length < AmbientStripCount) return false;

            IntPtr screenDC = GetDC(IntPtr.Zero);
            if (screenDC == IntPtr.Zero) return false;

            try
            {
                if (!EnsureAmbientBuffer(screenDC)) return false;

                int screenX = GetSystemMetrics(SM_XVIRTUALSCREEN);
                int screenY = GetSystemMetrics(SM_YVIRTUALSCREEN);
                int screenRight = screenX + GetSystemMetrics(SM_CXVIRTUALSCREEN);
                int screenBottom = screenY + GetSystemMetrics(SM_CYVIRTUALSCREEN);

                // 위, 아래, 왼쪽, 오른쪽 순서로 띠를 하나씩 1픽셀로 줄여 담는다.
                var strips = new[]
                {
                    new RECT { left = x - margin, top = y - margin - thickness, right = x + width + margin, bottom = y - margin },
                    new RECT { left = x - margin, top = y + height + margin, right = x + width + margin, bottom = y + height + margin + thickness },
                    new RECT { left = x - margin - thickness, top = y - margin, right = x - margin, bottom = y + height + margin },
                    new RECT { left = x + width + margin, top = y - margin, right = x + width + margin + thickness, bottom = y + height + margin },
                };

                SetStretchBltMode(_ambientDC, HALFTONE);

                int sampled = 0;
                var valid = new bool[AmbientStripCount];
                for (int i = 0; i < strips.Length; i++)
                {
                    RECT strip = strips[i];
                    if (strip.left < screenX || strip.top < screenY) continue;
                    if (strip.right > screenRight || strip.bottom > screenBottom) continue;

                    int stripWidth = strip.right - strip.left;
                    int stripHeight = strip.bottom - strip.top;
                    if (stripWidth <= 0 || stripHeight <= 0) continue;

                    if (!StretchBlt(_ambientDC, i, 0, 1, 1, screenDC, strip.left, strip.top, stripWidth, stripHeight, SRCCOPY)) continue;
                    valid[i] = true;
                    sampled++;
                }

                if (sampled == 0) return false;
                if (GetDIBits(_ambientDC, _ambientBitmap, 0, 1, _ambientBuffer, ref _ambientInfo, DIB_RGB_COLORS) == 0) return false;

                // 읽히지 않은 방향은 읽힌 것들의 평균으로 메운다. 화면 가장자리에 붙었을 때를 위한 것이다.
                Color average = Color.black;
                for (int i = 0; i < AmbientStripCount; i++)
                {
                    if (!valid[i]) continue;
                    average.r += _ambientBuffer[i * 4 + 2] / 255f;
                    average.g += _ambientBuffer[i * 4 + 1] / 255f;
                    average.b += _ambientBuffer[i * 4 + 0] / 255f;
                }
                average /= sampled;
                average.a = 1f;

                for (int i = 0; i < AmbientStripCount; i++)
                {
                    colors[i] = valid[i]
                        ? new Color(_ambientBuffer[i * 4 + 2] / 255f, _ambientBuffer[i * 4 + 1] / 255f, _ambientBuffer[i * 4 + 0] / 255f, 1f)
                        : average;
                }
                return true;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        private bool EnsureAmbientBuffer(IntPtr screenDC)
        {
            if (_ambientDC != IntPtr.Zero) return true;

            _ambientDC = CreateCompatibleDC(screenDC);
            if (_ambientDC == IntPtr.Zero) return false;

            _ambientBitmap = CreateCompatibleBitmap(screenDC, AmbientStripCount, 1);
            if (_ambientBitmap == IntPtr.Zero)
            {
                DeleteDC(_ambientDC);
                _ambientDC = IntPtr.Zero;
                return false;
            }

            SelectObject(_ambientDC, _ambientBitmap);
            return true;
        }

        /// <summary>
        /// 지정한 화면 영역을 읽어 텍스처를 갱신한다.
        /// 바탕화면 레이어를 읽을 때 refreshSource가 false면 앞서 받아 둔 그림에서 잘라만 쓴다.
        /// 창을 옮길 때는 잘라내기만 하면 되므로 훨씬 싸다.
        /// </summary>
        public bool Capture(Source source, int x, int y, int width, int height, bool refreshSource = true)
        {
            if (_disposed || width <= 0 || height <= 0) return false;

            _stopwatch.Restart();
            bool result = source == Source.WallpaperLayer
                ? CaptureWallpaperLayer(x, y, width, height, refreshSource)
                : CaptureScreen(x, y, width, height);
            _stopwatch.Stop();
            LastCaptureMilliseconds = _stopwatch.Elapsed.TotalMilliseconds;

            return result;
        }

        // ---- 화면 전체에서 읽기 ----

        private bool CaptureScreen(int x, int y, int width, int height)
        {
            IntPtr screenDC = GetDC(IntPtr.Zero);
            if (screenDC == IntPtr.Zero) return false;

            try
            {
                return BlitAndRead(screenDC, x, y, width, height);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        // ---- 바탕화면 레이어에서 읽기 ----

        /// <summary>
        /// 바탕화면 창만 그려 받는다. 다른 프로그램 창은 물론 우리 창도 들어오지 않아 되먹임이 없다.
        /// Wallpaper Engine처럼 영상 벽지를 쓰는 경우에도 그 창이 여기에 그리므로 그대로 들어온다.
        /// </summary>
        private bool CaptureWallpaperLayer(int x, int y, int width, int height, bool refreshSource)
        {
            IntPtr desktop = FindWindow(DESKTOP_CLASS, DESKTOP_TITLE);
            if (desktop == IntPtr.Zero) return false;
            if (!GetWindowRect(desktop, out RECT desktopRect)) return false;

            int desktopWidth = desktopRect.right - desktopRect.left;
            int desktopHeight = desktopRect.bottom - desktopRect.top;
            if (!EnsureLayerBuffer(desktopWidth, desktopHeight)) return false;

            // PrintWindow는 DC의 원점 이동을 무시하고 언제나 (0,0)부터 그린다.
            // 그래서 바탕화면 전체를 받아 두고, 필요한 영역은 여기서 잘라낸다.
            if (refreshSource || !_layerFilled)
            {
                if (!PrintWindow(desktop, _layerDC, PW_RENDERFULLCONTENT)) return false;
                _layerFilled = true;
            }

            return BlitAndRead(_layerDC, x - desktopRect.left, y - desktopRect.top, width, height);
        }

        private bool EnsureLayerBuffer(int width, int height)
        {
            if (_layerDC != IntPtr.Zero && _layerWidth == width && _layerHeight == height) return true;

            ReleaseLayerBuffer();

            IntPtr screenDC = GetDC(IntPtr.Zero);
            if (screenDC == IntPtr.Zero) return false;

            try
            {
                _layerDC = CreateCompatibleDC(screenDC);
                if (_layerDC == IntPtr.Zero) return false;

                _layerBitmap = CreateCompatibleBitmap(screenDC, width, height);
                if (_layerBitmap == IntPtr.Zero)
                {
                    ReleaseLayerBuffer();
                    return false;
                }

                SelectObject(_layerDC, _layerBitmap);
                _layerWidth = width;
                _layerHeight = height;
                return true;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        private void ReleaseLayerBuffer()
        {
            if (_layerBitmap != IntPtr.Zero) { DeleteObject(_layerBitmap); _layerBitmap = IntPtr.Zero; }
            if (_layerDC != IntPtr.Zero) { DeleteDC(_layerDC); _layerDC = IntPtr.Zero; }
            _layerWidth = 0;
            _layerHeight = 0;
            _layerFilled = false;
        }

        // ---- 공용 ----

        /// <summary>원본 DC의 영역을 축소해 텍스처로 올린다.</summary>
        private bool BlitAndRead(IntPtr sourceDC, int x, int y, int width, int height)
        {
            IntPtr memoryDC = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;

            try
            {
                memoryDC = CreateCompatibleDC(sourceDC);
                if (memoryDC == IntPtr.Zero) return false;

                bitmap = CreateCompatibleBitmap(sourceDC, Width, Height);
                if (bitmap == IntPtr.Zero) return false;

                previous = SelectObject(memoryDC, bitmap);
                SetStretchBltMode(memoryDC, HALFTONE);

                if (!StretchBlt(memoryDC, 0, 0, Width, Height, sourceDC, x, y, width, height, SRCCOPY)) return false;

                // 선택을 풀어야 GetDIBits가 비트맵을 읽을 수 있다.
                SelectObject(memoryDC, previous);
                previous = IntPtr.Zero;

                if (GetDIBits(memoryDC, bitmap, 0, (uint)Height, _buffer, ref _info, DIB_RGB_COLORS) == 0) return false;

                // GDI는 알파를 채우지 않아 0이 들어온다. 불투명으로 덮어쓴다.
                for (int i = 3; i < _buffer.Length; i += 4) _buffer[i] = 255;

                Texture.LoadRawTextureData(_buffer);
                Texture.Apply(false, false);
                return true;
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memoryDC, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memoryDC != IntPtr.Zero) DeleteDC(memoryDC);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ReleaseLayerBuffer();

            if (_ambientBitmap != IntPtr.Zero) { DeleteObject(_ambientBitmap); _ambientBitmap = IntPtr.Zero; }
            if (_ambientDC != IntPtr.Zero) { DeleteDC(_ambientDC); _ambientDC = IntPtr.Zero; }

            if (Texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
            else UnityEngine.Object.DestroyImmediate(Texture);
        }
    }
}
