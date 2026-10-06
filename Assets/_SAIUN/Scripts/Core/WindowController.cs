using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// Win32 테두리 없는 창·항상 위·드래그 이동·창 모양(떠 있는 카드 / 오른쪽 세로 전체 사이드바).
    /// 화면 녹화·스크린샷(OBS 등)에 그대로 잡힌다.
    /// Win32 의존부는 이 클래스에만 둔다. 창 위치 저장·복원은 GameManager가 이 클래스의 API로 배선한다.
    /// 사이드바는 창이 있는 모니터의 오른쪽 가장자리에 붙어 작업 영역 세로 전체를 채운다. 다른 창 위에 떠 있을 뿐
    /// 화면 공간을 예약하지는 않는다. 화면 배율(DPI)만큼 창을 키우고 UI·하늘은 논리 크기로 짠다.
    /// 모니터 구성이나 배율이 바뀌면 몇 초 안에 다시 붙는다. 사이드바에서는 끌어 옮기지 않는다.
    /// GameManager.Start가 위치를 복원하기 전에 창 핸들이 준비돼야 하므로 실행 순서를 앞당긴다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WindowController : MonoBehaviour
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);
        [DllImport("Dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute); // spellchecker:ignore Dwmapi
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

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
        const int  VK_LBUTTON        = 0x01;
        const uint SWP_FRAMECHANGED  = 0x0020; // spellchecker:ignore FRAMECHANGED
        const uint SPI_GETWORKAREA   = 0x0030; // spellchecker:ignore GETWORKAREA
        const uint MONITOR_DEFAULTTONEAREST = 2; // spellchecker:ignore DEFAULTTONEAREST
        const float BaseDpi     = 96f;

        // 에디터에는 모니터가 없으므로 사이드바 높이를 개발 PC 작업 영역쯤으로 둔다.
        const int EditorSidebarHeight = 1040;

        // Windows 11 빌드 22621 이상에서 지원하는 창 속성.
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;

        // 창 모서리를 둥글게(기본 반경) 또는 깎지 않게.
        const int DWMWCP_DONOTROUND = 1;   // spellchecker:ignore DWMWCP DONOTROUND
        const int DWMWCP_ROUND = 2;

        const string UNITY_WND_CLASS = "UnityWndClass";
        static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2); // spellchecker:ignore NOTOPMOST

        // 해상도 변경이 적용되기를 기다리는 최대 시간(초).
        const float ResolutionTimeoutSeconds = 2f;

        // 창 크기를 바꾼 뒤 창 안쪽 크기가 맞을 때까지 다시 맞춰 보는 프레임 수
        const int SettleFrames = 30;


        [Header("창 테두리")]
        [Tooltip("창 모서리를 둥글게 깎는다. Windows 11에서만 동작한다.")]
        [SerializeField] private bool roundedCorners = true;

        [Tooltip("DWM이 그리는 1픽셀 테두리 색. 카드 가장자리를 또렷하게 만든다.")]
        [SerializeField] private Color borderColor = SaiunPalette.Sand;

        [Header("사이드바")]
        [Tooltip("사이드바 폭(화면 배율 100% 기준 화소)")]
        [SerializeField, Min(200)] private int sidebarWidth = 380;

        [Tooltip("사이드바 논리 높이 상한. 이보다 높은 모니터에서는 사이드바 전체를 키운다(하늘 구도가 무너지지 않게).")]
        [SerializeField, Min(680)] private int maxSidebarHeight = 1100;

        [Tooltip("사이드바일 때 모니터 구성·배율이 바뀌었는지 살피는 간격(초)")]
#pragma warning disable CS0414   // 에디터 컴파일에서는 Win32 경로가 빠져 읽는 곳이 없다.
        [SerializeField, Min(0.2f)] private float displayPollSeconds = 2f;
#pragma warning restore CS0414

        /// <summary>창 좌상단 스크린 좌표. 에디터에서는 (0,0).</summary>
        public Vector2Int Position { get; private set; }

        /// <summary>지금 창 모양(카드·사이드바, 논리 크기, 배율).</summary>
        public WindowLayout Layout { get; private set; } = WindowLayout.Card(1f);

        /// <summary>창 모양이 바뀌었을 때(처음 준비됐을 때 포함) 발행.</summary>
        public event Action<WindowLayout> OnLayoutChanged;

        /// <summary>창 설정이 끝나 MoveTo 등을 호출해도 되는 상태인지.</summary>
        public bool IsReady { get; private set; }

        /// <summary>창 설정이 끝났을 때 1회 발행.</summary>
        public event Action OnReady;

        /// <summary>드래그가 끝나 창 위치가 바뀌었을 때 발행. 에디터 컴파일에서는 Win32 경로가 빠져 발행 지점이 없다.</summary>
#pragma warning disable CS0067
        public event Action<Vector2Int> OnMoved;
#pragma warning restore CS0067

        IntPtr _hwnd;
        bool _wasDown;
        bool _dragging;
        bool _changing;
        Vector2Int _cardPosition;
#if !UNITY_EDITOR
        bool _alwaysOnTop = true;
        float _sincePoll;
        Vector4 _dockedDisplay;
#endif
        Vector2Int _dragCursorStart;
        Vector2Int _dragWindowStart;

        void Start()
        {
#if !UNITY_EDITOR
            StartCoroutine(InitializeWindow());
#else
            IsReady = true;
            OnReady?.Invoke();
            OnLayoutChanged?.Invoke(Layout);
#endif
        }

#if !UNITY_EDITOR
        // Unity는 마지막 창 해상도를 레지스트리에서 복원한다(사이드바로 끝냈으면 그 크기다).
        // 창 핸들을 잡은 뒤 테두리를 떼고 창 크기를 카드로 맞춘다. 해상도를 먼저 바꾸면 테두리 있는 크기로
        // 뒤늦게 다시 적용돼 테두리를 뗀 창이 그만큼 커지므로, 창 크기부터 바꾸고 Unity가 따라오게 한다(ApplyPhysical).
        System.Collections.IEnumerator InitializeWindow()
        {
            yield return null;   // 첫 프레임: 창이 만들어진 뒤

            _hwnd = FindWindow(UNITY_WND_CLASS, Application.productName);
            if (_hwnd == IntPtr.Zero) _hwnd = GetActiveWindow();
            if (_hwnd == IntPtr.Zero)
            {
                Debug.LogWarning("WindowController: 창 핸들을 찾지 못해 창 설정을 건너뜁니다.");
                yield break;
            }

            // 화면 배율만큼 카드를 키운다(배율 100%면 480×680 그대로다).
            WindowLayout card = WindowLayout.Card(DpiScale());
            yield return ApplyPhysical(card.Physical, move: false);
            Layout = card;

            IsReady = true;
            OnReady?.Invoke();
            OnLayoutChanged?.Invoke(Layout);
        }

        // 창을 rect 크기로 바꾸고 스타일·테두리를 다시 입힌다. 먼저 창 크기만 바꿔 Unity가 따라오게 하고(시작할 때와 같은 길),
        // 따라오지 않을 때만 해상도를 바꾼다. 실행 중 해상도 변경은 테두리 있는 창 크기로 뒤늦게 다시 적용되며
        // 테두리를 뺀 창을 그만큼 키우기 때문이다.
        System.Collections.IEnumerator ApplyPhysical(RectInt rect, bool move)
        {
            uint flags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | (move ? 0u : SWP_NOMOVE);
            SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
            SetWindowPos(_hwnd, IntPtr.Zero, rect.x, rect.y, rect.width, rect.height, flags);
            for (int i = 0; i < SettleFrames && (Screen.width != rect.width || Screen.height != rect.height); i++)
            {
                yield return null;
            }

            if (Screen.width != rect.width || Screen.height != rect.height)
            {
                Debug.Log($"WindowController: 창 크기만으로는 {Screen.width}x{Screen.height}라 해상도를 {rect.width}x{rect.height}로 바꿉니다.");
                Screen.SetResolution(rect.width, rect.height, FullScreenMode.Windowed);
                float deadline = Time.realtimeSinceStartup + ResolutionTimeoutSeconds;
                while ((Screen.width != rect.width || Screen.height != rect.height) && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                yield return null;
                SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
                SetWindowPos(_hwnd, IntPtr.Zero, rect.x, rect.y, rect.width, rect.height, flags);
                for (int i = 0; i < SettleFrames && (Screen.width != rect.width || Screen.height != rect.height); i++)
                {
                    yield return null;
                    SetWindowPos(_hwnd, IntPtr.Zero, rect.x, rect.y, rect.width, rect.height, flags);
                }
            }
            Debug.Log($"WindowController: 창 {rect.width}x{rect.height} → 화면 {Screen.width}x{Screen.height}");

            ApplyDarkMode();
            ApplyCorners();
            ApplyBorder();
            SetAlwaysOnTop(_alwaysOnTop);
            RefreshPosition();
        }

        // 창이 있는 모니터 작업 영역의 오른쪽 가장자리에 붙는다.
        System.Collections.IEnumerator Dock()
        {
            _changing = true;
            if (!Layout.Sidebar) _cardPosition = Position;
            if (!ReadDisplay(out RECT monitor, out RECT work, out float scale))
            {
                _changing = false;
                yield break;
            }

            float dpiScale = scale;
            scale = WindowLayout.SidebarScale(dpiScale, work.bottom - work.top, maxSidebarHeight);
            // 작업 표시줄을 오른쪽에 둔 경우에도 그 왼쪽에 붙도록 작업 영역의 오른쪽을 쓴다.
            WindowLayout layout = WindowLayout.Dock(scale, work.right, work.top, work.bottom, sidebarWidth);
            yield return ApplyPhysical(layout.Physical, move: true);
            _dockedDisplay = new Vector4(monitor.left, monitor.right, work.top * 10000f + work.bottom, dpiScale);
            Layout = layout;
            _changing = false;
            OnLayoutChanged?.Invoke(Layout);
        }

        // 카드로 돌아간다. 마지막으로 끌어 둔 자리로 돌아간다.
        System.Collections.IEnumerator Undock()
        {
            _changing = true;
            WindowLayout card = WindowLayout.Card(DpiScale());
            yield return ApplyPhysical(card.Physical, move: false);
            Layout = card;
            SetWindowPos(_hwnd, IntPtr.Zero, _cardPosition.x, _cardPosition.y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            RefreshPosition();
            _changing = false;
            OnLayoutChanged?.Invoke(Layout);
        }

        bool ReadDisplay(out RECT monitor, out RECT work, out float scale)
        {
            monitor = default;
            work = default;
            scale = DpiScale();
            IntPtr handle = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (handle == IntPtr.Zero || !GetMonitorInfo(handle, ref info)) return false;
            monitor = info.rcMonitor;
            work = info.rcWork;
            return true;
        }

        float DpiScale()
        {
            uint dpi = _hwnd != IntPtr.Zero ? GetDpiForWindow(_hwnd) : 0;
            return dpi > 0 ? dpi / BaseDpi : 1f;
        }

        // 사이드바일 때 모니터 크기·작업 영역·배율이 바뀌었으면 다시 붙는다.
        void PollDisplay()
        {
            _sincePoll += Time.unscaledDeltaTime;
            if (_sincePoll < displayPollSeconds) return;
            _sincePoll = 0f;
            if (!ReadDisplay(out RECT monitor, out RECT work, out float scale)) return;
            var now = new Vector4(monitor.left, monitor.right, work.top * 10000f + work.bottom, scale);
            if (now != _dockedDisplay) StartCoroutine(Dock());
        }
#endif

        /// <summary>
        /// 사이드바(화면 오른쪽 세로 전체)로 붙이거나 떠 있는 카드로 돌아간다. 바꾸는 중에 다시 부르면 무시한다.
        /// </summary>
        public void SetSidebar(bool on)
        {
            if (on == Layout.Sidebar || _changing) return;
#if !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero) return;
            StartCoroutine(on ? Dock() : Undock());
#else
            Layout = on ? WindowLayout.Dock(1f, 0, 0, EditorSidebarHeight, sidebarWidth) : WindowLayout.Card(1f);
            OnLayoutChanged?.Invoke(Layout);
#endif
        }

        void Update()
        {
#if !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero) return;

            if (Layout.Sidebar && !_changing) PollDisplay();

            // GetAsyncKeyState는 최상위 비트가 눌림 상태다.
            bool isDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

            // 사이드바는 화면 가장자리에 붙어 있으므로 끌어 옮기지 않는다.
            if (isDown && !_wasDown && !Layout.Sidebar && IsCursorInsideWindow() && !IsPointerOverUI())
            {
                BeginDrag();
            }
            else if (_dragging && isDown)
            {
                ContinueDrag();
            }
            else if (_dragging)
            {
                EndDrag();
            }

            _wasDown = isDown;
#endif
        }

        // ---- 드래그 ----
        //
        // OS에 캡션 드래그를 넘기면(WM_NCLBUTTONDOWN) 그 호출이 드래그가 끝날 때까지 돌아오지 않는다.
        // 그동안 Unity가 통째로 멈춰서 시계도 하늘도 얼어붙는다.
        // 그래서 위치를 직접 옮긴다. 매 프레임 그리므로 배경이 창을 따라온다.

        void BeginDrag()
        {
#if !UNITY_EDITOR
            if (!GetCursorPos(out POINT cursor) || !GetWindowRect(_hwnd, out RECT rect)) return;
            _dragging = true;
            _dragCursorStart = new Vector2Int(cursor.x, cursor.y);
            _dragWindowStart = new Vector2Int(rect.left, rect.top);
#endif
        }

        void ContinueDrag()
        {
#if !UNITY_EDITOR
            if (!GetCursorPos(out POINT cursor)) return;
            int x = _dragWindowStart.x + (cursor.x - _dragCursorStart.x);
            int y = _dragWindowStart.y + (cursor.y - _dragCursorStart.y);
            SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            RefreshPosition();
#endif
        }

        void EndDrag()
        {
            _dragging = false;
            RefreshPosition();
            OnMoved?.Invoke(Position);
        }

        // ---- 공개 API ----

        /// <summary>창을 스크린 좌표 (x, y)로 옮긴다. 사이드바일 때는 카드로 돌아갈 자리만 기억한다.</summary>
        public void MoveTo(int x, int y)
        {
            if (Layout.Sidebar)
            {
                _cardPosition = new Vector2Int(x, y);
                return;
            }
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
            // 사이드바로 붙어 있어도 기억해 둘 자리는 떠 있는 카드의 자리다(사이드바 폭으로 셈하면 카드가 화면 밖으로 삐져나간다).
            MoveTo(workArea.right - WindowLayout.Card(DpiScale()).Physical.width, workArea.top);
#endif
        }

        public void SetAlwaysOnTop(bool alwaysOnTop)
        {
#if !UNITY_EDITOR
            _alwaysOnTop = alwaysOnTop;
            if (_hwnd == IntPtr.Zero) return;
            SetWindowPos(_hwnd, alwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
#endif
        }

        /// <summary>
        /// 창 위치를 지금 다시 읽어 돌려준다.
        /// 다른 프로그램이 창을 옮겼을 수도 있어, 화면을 읽기 전에는 이 값을 써야 한다.
        /// </summary>
        public Vector2Int RefreshAndGetPosition()
        {
            RefreshPosition();
            return Position;
        }

        // ---- 창 테두리 ----

        // 어두운 카드이므로 다크 모드로 둬야 DWM이 그리는 테두리·그림자 색조가 맞는다.
        void ApplyDarkMode()
        {
#if !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero) return;
            int dark = 1;
            DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
#endif
        }

        void ApplyCorners()
        {
#if !UNITY_EDITOR
            // 사이드바는 화면 가장자리에 붙으므로 모서리를 깎지 않는다.
            int preference = roundedCorners && !Layout.Sidebar ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
#endif
        }

        void ApplyBorder()
        {
#if !UNITY_EDITOR
            int colorRef = ToColorRef(borderColor);
            DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
#endif
        }

        /// <summary>DWM 테두리가 쓰는 0x00BBGGRR 형식으로 바꾼다.</summary>
        static int ToColorRef(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return (b << 16) | (g << 8) | r;
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
