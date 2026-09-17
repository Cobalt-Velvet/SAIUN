using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// Win32 투명 창·항상 위·드래그 이동.
    /// Win32 의존부는 이 클래스에만 둔다. 창 위치 저장·복원은 GameManager가 이 클래스의 API로 배선한다.
    /// GameManager.Start가 위치를 복원하기 전에 창 핸들이 준비돼야 하므로 실행 순서를 앞당긴다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WindowController : MonoBehaviour
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);
        [DllImport("Dwmapi.dll")] static extern uint DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset); // spellchecker:ignore Dwmapi

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        const int  GWL_STYLE         = -16;
        const uint WS_POPUP          = 0x80000000;
        const uint WS_VISIBLE        = 0x10000000;
        const uint SWP_NOSIZE        = 0x0001; // spellchecker:ignore NOSIZE
        const uint SWP_NOMOVE        = 0x0002; // spellchecker:ignore NOMOVE
        const uint SWP_NOZORDER      = 0x0004; // spellchecker:ignore NOZORDER
        const uint SWP_NOACTIVATE    = 0x0010; // spellchecker:ignore NOACTIVATE
        const uint WM_NCLBUTTONDOWN  = 0xA1;   // spellchecker:ignore NCLBUTTONDOWN
        const int  HT_CAPTION        = 0x2;
        const int  VK_LBUTTON        = 0x01;
        const uint SWP_FRAMECHANGED  = 0x0020; // spellchecker:ignore FRAMECHANGED
        const uint SPI_GETWORKAREA   = 0x0030; // spellchecker:ignore GETWORKAREA

        // 창 크기 확정값 (사양서 8장). 플레이어가 레지스트리에 남긴 이전 해상도를 덮어쓴다.
        public const int WindowWidth  = 480;
        public const int WindowHeight = 680;
        const string UNITY_WND_CLASS = "UnityWndClass";
        static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2); // spellchecker:ignore NOTOPMOST

        // 해상도 변경이 적용되기를 기다리는 최대 시간(초).
        const float ResolutionTimeoutSeconds = 2f;

        /// <summary>창 좌상단 스크린 좌표. 에디터에서는 (0,0).</summary>
        public Vector2Int Position { get; private set; }

        /// <summary>투명 창 설정이 끝나 MoveTo 등을 호출해도 되는 상태인지.</summary>
        public bool IsReady { get; private set; }

        /// <summary>투명 창 설정이 끝났을 때 1회 발행.</summary>
        public event Action OnReady;

        /// <summary>드래그가 끝나 창 위치가 바뀌었을 때 발행. 에디터 컴파일에서는 Win32 경로가 빠져 발행 지점이 없다.</summary>
#pragma warning disable CS0067
        public event Action<Vector2Int> OnMoved;
#pragma warning restore CS0067

        IntPtr _hwnd;
        bool _wasDown;

        void Start()
        {
#if !UNITY_EDITOR
            StartCoroutine(InitializeWindow());
#else
            IsReady = true;
            OnReady?.Invoke();
#endif
        }

#if !UNITY_EDITOR
        // Unity는 마지막 창 해상도를 레지스트리에서 복원하고, 해상도 변경은 프레임 끝에 적용되면서
        // 창 스타일을 초기화한다. 그래서 고정 크기가 적용된 뒤에 Win32 스타일을 입힌다.
        System.Collections.IEnumerator InitializeWindow()
        {
            if (Screen.width != WindowWidth || Screen.height != WindowHeight)
            {
                Screen.SetResolution(WindowWidth, WindowHeight, FullScreenMode.Windowed);
                float deadline = Time.realtimeSinceStartup + ResolutionTimeoutSeconds;
                while ((Screen.width != WindowWidth || Screen.height != WindowHeight)
                       && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                yield return null;   // 스타일 초기화가 끝난 다음 프레임
            }

            _hwnd = FindWindow(UNITY_WND_CLASS, Application.productName);
            if (_hwnd == IntPtr.Zero) _hwnd = GetActiveWindow();
            if (_hwnd == IntPtr.Zero)
            {
                Debug.LogWarning("WindowController: 창 핸들을 찾지 못해 투명 창 설정을 건너뜁니다.");
                yield break;
            }

            SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, WindowWidth, WindowHeight, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            var margins = new MARGINS { cxLeftWidth = -1 };
            DwmExtendFrameIntoClientArea(_hwnd, ref margins);
            SetAlwaysOnTop(true);
            RefreshPosition();

            IsReady = true;
            OnReady?.Invoke();
        }
#endif

        void Update()
        {
#if !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero) return;

            // GetAsyncKeyState는 최상위 비트가 눌림 상태다.
            bool isDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

            if (isDown && !_wasDown && IsCursorInsideWindow() && !IsPointerOverUI())
            {
                // 테두리 없는 창은 Unity 입력이 마우스 다운을 잡지 못해 OS에 캡션 드래그를 위임한다.
                // SendMessage는 드래그가 끝날 때까지 돌아오지 않는다.
                ReleaseCapture();
                SendMessage(_hwnd, WM_NCLBUTTONDOWN, new IntPtr(HT_CAPTION), IntPtr.Zero);
                RefreshPosition();
                OnMoved?.Invoke(Position);
            }

            _wasDown = isDown;
#endif
        }

        // ---- 공개 API ----

        /// <summary>창을 스크린 좌표 (x, y)로 옮긴다.</summary>
        public void MoveTo(int x, int y)
        {
#if !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero) return;
            SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            RefreshPosition();
#else
            Position = new Vector2Int(x, y);
#endif
        }

        /// <summary>작업 영역(작업 표시줄 제외) 우측 상단으로 옮긴다. 첫 실행 기본 위치.</summary>
        public void MoveToDefaultPosition()
        {
#if !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero) return;
            var workArea = new RECT();
            if (!SystemParametersInfo(SPI_GETWORKAREA, 0, ref workArea, 0)) return;
            GetWindowRect(_hwnd, out RECT rect);
            int width = rect.right - rect.left;
            MoveTo(workArea.right - width, workArea.top);
#endif
        }

        public void SetAlwaysOnTop(bool alwaysOnTop)
        {
#if !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero) return;
            SetWindowPos(_hwnd, alwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
#endif
        }

        // ---- 내부 ----

        void RefreshPosition()
        {
#if !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero) return;
            if (GetWindowRect(_hwnd, out RECT rect)) Position = new Vector2Int(rect.left, rect.top);
#endif
        }

        bool IsCursorInsideWindow()
        {
#if !UNITY_EDITOR
            if (!GetCursorPos(out POINT p) || !GetWindowRect(_hwnd, out RECT r)) return false;
            return p.x >= r.left && p.x < r.right && p.y >= r.top && p.y < r.bottom;
#else
            return false;
#endif
        }

        // 버튼·입력 필드 위에서는 드래그를 시작하지 않는다.
        static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
