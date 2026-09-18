using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// Win32 투명 창·유리 배경·항상 위·드래그 이동.
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
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hWnd, ref WINDOWCOMPOSITIONATTRIBDATA data); // spellchecker:ignore WINDOWCOMPOSITIONATTRIBDATA
        [DllImport("Dwmapi.dll")] static extern uint DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset); // spellchecker:ignore Dwmapi
        [DllImport("Dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetOpenFileName(ref OPENFILENAME lpofn); // spellchecker:ignore comdlg OPENFILENAME

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        // DWM 합성 속성. 문서화되지 않았지만 Windows 10 1803 이후로 형태가 바뀌지 않았다.
        [StructLayout(LayoutKind.Sequential)]
        struct ACCENT_POLICY // spellchecker:ignore ACCENT
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;   // 0xAABBGGRR
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWCOMPOSITIONATTRIBDATA
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        // 파일 열기 대화상자 (comdlg32).
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OPENFILENAME
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

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

        const int OFN_NOCHANGEDIR    = 0x00000008; // spellchecker:ignore NOCHANGEDIR
        const int OFN_PATHMUSTEXIST  = 0x00000800; // spellchecker:ignore PATHMUSTEXIST
        const int OFN_FILEMUSTEXIST  = 0x00001000; // spellchecker:ignore FILEMUSTEXIST
        const int OFN_EXPLORER       = 0x00080000;
        const int MaxPathChars       = 4096;

        // 화면 캡처에서 이 창만 빼는 속성. 직접 흐림을 쓸 때 자기 자신을 다시 찍지 않으려면 필요하다.
        const uint WDA_NONE = 0x0000;                  // spellchecker:ignore WDA
        const uint WDA_EXCLUDEFROMCAPTURE = 0x0011;    // spellchecker:ignore EXCLUDEFROMCAPTURE

        // Windows 11 빌드 22621 이상에서 지원하는 창 속성.
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;   // spellchecker:ignore SYSTEMBACKDROP

        // 문서화되지 않은 합성 속성 상수.
        const int WCA_ACCENT_POLICY = 19;           // spellchecker:ignore WCA
        const int ACCENT_DISABLED = 0;
        const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;     // spellchecker:ignore TRANSPARENTGRADIENT
        const int ACCENT_ENABLE_BLURBEHIND = 3;              // spellchecker:ignore BLURBEHIND
        const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;       // spellchecker:ignore ACRYLICBLURBEHIND

        // 창 모서리를 둥글게. 2는 기본 반경, 3은 작은 반경이다.
        const int DWMWCP_DONOTROUND = 1;   // spellchecker:ignore DWMWCP DONOTROUND
        const int DWMWCP_ROUND = 2;
        const int DWMWCP_ROUNDSMALL = 3;   // spellchecker:ignore ROUNDSMALL

        // 창 크기는 SceneMetrics가 단일 출처다. 플레이어가 레지스트리에 남긴 이전 해상도를 덮어쓴다.
        const int WindowWidth  = SceneMetrics.WindowWidth;
        const int WindowHeight = SceneMetrics.WindowHeight;
        const string UNITY_WND_CLASS = "UnityWndClass";
        static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2); // spellchecker:ignore NOTOPMOST

        // 해상도 변경이 적용되기를 기다리는 최대 시간(초).
        const float ResolutionTimeoutSeconds = 2f;

        /// <summary>창 뒤에 깔리는 유리 배경의 종류.</summary>
        public enum GlassMode
        {
            /// <summary>배경 없음. 바탕화면이 그대로 비친다.</summary>
            None,
            /// <summary>DWM Mica. 바탕화면 색만 크게 흐린다. 투명도를 조절할 수 없다.</summary>
            SystemMica,
            /// <summary>DWM Acrylic. 투명도가 고정이라 뒷배경 색이 거의 묻힌다.</summary>
            SystemAcrylic,
            /// <summary>틴트 색과 농도를 직접 지정하는 Acrylic. 농도를 낮추면 뒷배경 색이 배어 나온다.</summary>
            TintedAcrylic,
            /// <summary>틴트 Acrylic보다 가벼운 단순 블러. 뒷배경이 더 선명하게 비친다.</summary>
            TintedBlur,
            /// <summary>블러 없이 색만 얇게 덮는다. 뒷배경이 또렷하게 그대로 비친다.</summary>
            TransparentTint,
            /// <summary>화면을 직접 읽어 흐린다. 뒷배경의 형태와 색이 그대로 남는 유일한 방식이다.</summary>
            DesktopBlur,
            /// <summary>바탕화면 레이어만 읽어 흐린다. 다른 창은 안 비치지만 녹화에도 정상으로 나온다.</summary>
            WallpaperBlur,
        }

        [Header("유리 배경")]
        [Tooltip("창 뒤를 흐리는 방식. Tinted 계열만 농도를 조절할 수 있다.")]
        [SerializeField] private GlassMode glass = GlassMode.DesktopBlur;

        [Tooltip("유리에 섞을 틴트 색. 팔레트의 어두운 톤을 기본으로 쓴다.")]
        [SerializeField] private Color glassTint = SaiunPalette.DeepJungle;

        [Tooltip("틴트 농도. 낮출수록 뒷배경 색이 그대로 배어 나온다.")]
        [Range(0f, 1f)]
        [SerializeField] private float glassTintStrength = 0.15f;

        [Tooltip("창 모서리를 둥글게 깎는다. Windows 11에서만 동작한다.")]
        [SerializeField] private bool roundedCorners = true;

        [Tooltip("DWM이 그리는 1픽셀 테두리 색. 유리 가장자리를 또렷하게 만든다.")]
        [SerializeField] private Color borderColor = SaiunPalette.Eggshell;

        [Tooltip("테두리를 그릴지 여부. 끄면 DWM 기본 테두리를 쓴다.")]
        [SerializeField] private bool customBorder = true;

        [Tooltip("DWM 프레임을 클라이언트 영역까지 확장한다. 합성 블러를 쓸 때는 꺼야 배경이 제대로 비친다.")]
        [SerializeField] private bool extendFrame;

        [Tooltip("화면 녹화와 스크린샷에서 이 창을 제외한다. DesktopBlur가 자기 자신을 다시 찍는 것을 막는다.")]
        [SerializeField] private bool excludeFromCapture = true;

        /// <summary>창 좌상단 스크린 좌표. 에디터에서는 (0,0).</summary>
        public Vector2Int Position { get; private set; }

        /// <summary>투명 창 설정이 끝나 MoveTo 등을 호출해도 되는 상태인지.</summary>
        public bool IsReady { get; private set; }

        /// <summary>현재 적용된 유리 배경.</summary>
        public GlassMode CurrentGlass => glass;

        /// <summary>창을 끌고 있는 중인지. 에디터에서는 드래그 경로가 없어 항상 false다.</summary>
        public bool IsDragging => _dragging;

        /// <summary>투명 창 설정이 끝났을 때 1회 발행.</summary>
        public event Action OnReady;

        /// <summary>드래그가 끝나 창 위치가 바뀌었을 때 발행. 에디터 컴파일에서는 Win32 경로가 빠져 발행 지점이 없다.</summary>
#pragma warning disable CS0067
        public event Action<Vector2Int> OnMoved;
#pragma warning restore CS0067

        IntPtr _hwnd;
        bool _wasDown;
        bool _dragging;
        Vector2Int _dragCursorStart;
        Vector2Int _dragWindowStart;

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

            ReadOverrides();

            if (extendFrame)
            {
                var margins = new MARGINS { cxLeftWidth = -1 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);
            }

            ApplyGlass();
            ApplyCaptureExclusion();
            ApplyCorners();
            ApplyBorder();
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
        // 그동안 Unity가 통째로 멈춰서 시계도 유리 배경도 얼어붙는다.
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

        /// <summary>
        /// 파일 열기 대화상자를 띄우고 고른 파일 경로를 돌려준다. 취소하면 null.
        /// 대화상자가 떠 있는 동안 이 창은 멈춘다(모달).
        /// </summary>
        /// <param name="title">대화상자 제목</param>
        /// <param name="filterName">형식 이름(예: "VRM 파일")</param>
        /// <param name="extension">점 없는 확장자(예: "vrm")</param>
        public string ShowOpenFileDialog(string title, string filterName, string extension)
        {
#if !UNITY_EDITOR
            IntPtr buffer = Marshal.AllocHGlobal(MaxPathChars * sizeof(char));
            try
            {
                // 빈 문자열로 시작해야 대화상자가 초기 파일 이름을 쓰지 않는다.
                Marshal.WriteInt16(buffer, 0);
                var ofn = new OPENFILENAME
                {
                    lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                    hwndOwner = _hwnd,
                    // 이름\0패턴\0 쌍을 \0 하나로 끝낸다.
                    lpstrFilter = $"{filterName} (*.{extension})\0*.{extension}\0\0",
                    nFilterIndex = 1,
                    lpstrFile = buffer,
                    nMaxFile = MaxPathChars,
                    lpstrTitle = title,
                    lpstrDefExt = extension,
                    Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
                };
                return GetOpenFileName(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
#else
            string path = UnityEditor.EditorUtility.OpenFilePanel(title, string.Empty, extension);
            return string.IsNullOrEmpty(path) ? null : path;
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

        /// <summary>유리 배경을 바꾼다. 틴트 색과 농도는 현재 설정값을 쓴다.</summary>
        public void SetGlass(GlassMode mode)
        {
            glass = mode;
            ApplyGlass();
        }

        /// <summary>틴트 색과 농도를 바꾼다. Tinted 계열에서만 효과가 있다.</summary>
        public void SetGlassTint(Color tint, float strength)
        {
            glassTint = tint;
            glassTintStrength = Mathf.Clamp01(strength);
            ApplyGlass();
        }

        // ---- 유리 배경 ----

        void ApplyGlass()
        {
#if !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero) return;

            // 어두운 틴트를 쓰므로 다크 모드로 둬야 시스템 배경 색조가 맞는다.
            int dark = 1;
            DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            switch (glass)
            {
                case GlassMode.SystemMica:
                    SetAccent(ACCENT_DISABLED, 0);
                    SetSystemBackdrop(2);
                    break;

                case GlassMode.SystemAcrylic:
                    SetAccent(ACCENT_DISABLED, 0);
                    SetSystemBackdrop(3);
                    break;

                case GlassMode.TintedAcrylic:
                    // 시스템 배경과 합성 속성을 같이 켜면 시스템 쪽이 이겨서 농도 조절이 먹지 않는다.
                    SetSystemBackdrop(1);
                    SetAccent(ACCENT_ENABLE_ACRYLICBLURBEHIND, ToAbgr(glassTint, glassTintStrength));
                    break;

                case GlassMode.TintedBlur:
                    SetSystemBackdrop(1);
                    SetAccent(ACCENT_ENABLE_BLURBEHIND, ToAbgr(glassTint, glassTintStrength));
                    break;

                case GlassMode.TransparentTint:
                    SetSystemBackdrop(1);
                    SetAccent(ACCENT_ENABLE_TRANSPARENTGRADIENT, ToAbgr(glassTint, glassTintStrength));
                    break;

                case GlassMode.DesktopBlur:
                case GlassMode.WallpaperBlur:
                    // 창을 완전히 투명하게 두고, 흐림은 DesktopGlassView가 직접 그린다.
                    SetSystemBackdrop(1);
                    SetAccent(ACCENT_DISABLED, 0);
                    break;

                default:
                    SetAccent(ACCENT_DISABLED, 0);
                    SetSystemBackdrop(1);
                    break;
            }

            Debug.Log($"WindowController: 유리 배경 {glass}, 틴트 #{ColorUtility.ToHtmlStringRGB(glassTint)} 농도 {glassTintStrength:F2}");
#endif
        }

        void SetSystemBackdrop(int type)
        {
#if !UNITY_EDITOR
            int value = type;
            DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int));
#endif
        }

        void SetAccent(int state, int gradientColor)
        {
#if !UNITY_EDITOR
            var policy = new ACCENT_POLICY
            {
                AccentState = state,
                // 2는 네 변을 모두 그리라는 뜻이다. 틴트를 쓸 때만 의미가 있다.
                AccentFlags = state == ACCENT_DISABLED ? 0 : 2,
                GradientColor = gradientColor,
                AnimationId = 0,
            };

            int size = Marshal.SizeOf(policy);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, buffer, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = buffer,
                    SizeOfData = size,
                };
                SetWindowCompositionAttribute(_hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
#endif
        }

        /// <summary>
        /// 화면 캡처에서 이 창을 뺀다. DesktopBlur는 화면을 그대로 읽으므로,
        /// 제외하지 않으면 자기가 그린 유리를 다시 찍어 무한히 겹친다.
        /// 끄면 OBS 같은 녹화 도구에도 보이지만 DesktopBlur는 쓸 수 없다.
        /// </summary>
        void ApplyCaptureExclusion()
        {
#if !UNITY_EDITOR
            // 바탕화면 레이어만 읽는 모드는 자기 자신이 안 찍히므로 제외할 이유가 없다.
            bool exclude = excludeFromCapture && glass == GlassMode.DesktopBlur;
            bool ok = SetWindowDisplayAffinity(_hwnd, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            Debug.Log($"WindowController: 캡처 제외 {exclude} 적용 {(ok ? "성공" : "실패")} err={Marshal.GetLastWin32Error()}");
#endif
        }

        void ApplyCorners()
        {
#if !UNITY_EDITOR
            int preference = roundedCorners ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
#endif
        }

        void ApplyBorder()
        {
#if !UNITY_EDITOR
            if (!customBorder) return;
            int colorRef = ToColorRef(borderColor);
            DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
#endif
        }

        /// <summary>합성 속성이 쓰는 0xAABBGGRR 형식으로 바꾼다.</summary>
        static int ToAbgr(Color color, float alpha)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f), 0, 255);
            return (a << 24) | (b << 16) | (g << 8) | r;
        }

        /// <summary>DWM 테두리가 쓰는 0x00BBGGRR 형식으로 바꾼다.</summary>
        static int ToColorRef(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return (b << 16) | (g << 8) | r;
        }

        // 실험 중에는 실행 인자로 값을 바꿔 한 번의 빌드로 여러 조합을 본다.
        // 예: SAIUN.exe -glass tintedacrylic -tint 0.2 -corners off
        void ReadOverrides()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                string value = args[i + 1];
                switch (args[i])
                {
                    case "-glass":
                        if (Enum.TryParse(value, true, out GlassMode parsed)) glass = parsed;
                        else Debug.LogWarning($"WindowController: 알 수 없는 -glass 값 {value}");
                        break;

                    case "-tint":
                        if (float.TryParse(value, out float strength)) glassTintStrength = Mathf.Clamp01(strength);
                        break;

                    case "-corners":
                        roundedCorners = value != "off";
                        break;

                    case "-border":
                        customBorder = value != "off";
                        break;

                    case "-frame":
                        extendFrame = value != "off";
                        break;

                    case "-capture":
                        excludeFromCapture = value != "on";
                        break;
                }
            }
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
