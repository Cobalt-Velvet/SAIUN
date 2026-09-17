using System;
using System.Runtime.InteropServices;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// 화면의 한 영역을 축소해서 읽어 온다.
    /// 축소 자체가 흐림 효과라, 따로 블러를 돌리지 않아도 유리처럼 보인다.
    /// GDI가 평균을 내며 줄여주므로 비용이 거의 들지 않는다.
    /// </summary>
    public sealed class DesktopCapture : IDisposable
    {
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
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

        const uint SRCCOPY = 0x00CC0020;   // spellchecker:ignore SRCCOPY
        const int HALFTONE = 4;            // 축소할 때 평균을 내는 모드
        const uint DIB_RGB_COLORS = 0;     // spellchecker:ignore DIB

        /// <summary>축소해서 담아 둔 화면. BGRA 순서다.</summary>
        public Texture2D Texture { get; }

        public int Width { get; }
        public int Height { get; }

        private readonly byte[] _buffer;
        private BITMAPINFO _info;
        private bool _disposed;

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
        }

        /// <summary>화면의 (x, y, width, height) 영역을 읽어 텍스처를 갱신한다.</summary>
        public bool Capture(int x, int y, int width, int height)
        {
            if (_disposed || width <= 0 || height <= 0) return false;

            IntPtr screenDC = GetDC(IntPtr.Zero);
            if (screenDC == IntPtr.Zero) return false;

            IntPtr memoryDC = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;

            try
            {
                memoryDC = CreateCompatibleDC(screenDC);
                if (memoryDC == IntPtr.Zero) return false;

                bitmap = CreateCompatibleBitmap(screenDC, Width, Height);
                if (bitmap == IntPtr.Zero) return false;

                previous = SelectObject(memoryDC, bitmap);
                SetStretchBltMode(memoryDC, HALFTONE);

                if (!StretchBlt(memoryDC, 0, 0, Width, Height, screenDC, x, y, width, height, SRCCOPY)) return false;

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
                ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
            else UnityEngine.Object.DestroyImmediate(Texture);
        }
    }
}
