using System;
using System.Runtime.InteropServices;
using UnityEngine;

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local
#pragma warning disable CS0414

namespace _SAIUN.Scripts.Core
{
    public class WindowController : MonoBehaviour
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("Dwmapi.dll")] static extern uint DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset); // spellchecker:ignore Dwmapi

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        const int  GWL_STYLE        = -16;
        const uint WS_POPUP         = 0x80000000;
        const uint WS_VISIBLE       = 0x10000000;
        const uint SWP_NOSIZE       = 0x0001; // spellchecker:ignore NOSIZE
        const uint SWP_NOMOVE       = 0x0002; // spellchecker:ignore NOMOVE
        const uint SWP_NOZORDER     = 0x0004; // spellchecker:ignore NOZORDER
        const uint WM_NCLBUTTONDOWN = 0xA1;   // spellchecker:ignore NCLBUTTONDOWN
        const int  HT_CAPTION       = 0x2;
        const int  VK_LBUTTON       = 0x01;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        IntPtr _hwnd;
        bool _wasDown;
        int _offsetX, _offsetY;

        void Start()
        {
#if !UNITY_EDITOR
            _hwnd = FindWindow(null, "SAIUN");
            SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
            var margins = new MARGINS { cxLeftWidth = -1 };
            DwmExtendFrameIntoClientArea(_hwnd, ref margins);
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
#endif
        }

        void Update()
        {
#if !UNITY_EDITOR
            bool isDown = GetAsyncKeyState(VK_LBUTTON);

            if (isDown && !_wasDown)
            {
                ReleaseCapture();
                SendMessage(_hwnd, WM_NCLBUTTONDOWN, new IntPtr(HT_CAPTION), IntPtr.Zero);
            }

            _wasDown = isDown;
#endif
        }
    }
}