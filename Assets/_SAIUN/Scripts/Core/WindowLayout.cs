using UnityEngine;

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// 창 모양: 떠 있는 카드, 또는 화면을 감지해 오른쪽 세로 전체를 채우는 사이드바.
    ///  - 떠 있는 카드: 기본은 사양서 8장 480×680. 끌어 옮기고, 가장자리를 끌어 크기를 바꾼다.
    ///  - 사이드바: 창이 있는 모니터의 오른쪽 가장자리에 붙어 작업 영역(작업 표시줄 제외) 세로 전체를 채운다.
    ///    왼쪽 가장자리를 끌어 폭을 바꾼다.
    /// 창이 커지면 그림을 늘리지 않고 더 넓게 본다(화소당 각도를 지키는 초광각, ViewRig).
    /// 논리 크기는 화면 배율 100% 기준 화소이고, 실제 창은 배율(DPI/96)만큼 크다. UI와 하늘은 논리 크기로 짠다.
    /// </summary>
    public struct WindowLayout
    {
        /// <summary>사이드바인지(아니면 떠 있는 카드).</summary>
        public bool Sidebar;

        /// <summary>논리 크기(배율 100% 기준 화소).</summary>
        public Vector2Int Logical;

        /// <summary>화면 배율(DPI / 96).</summary>
        public float Scale;

        /// <summary>실제 창 자리(스크린 화소). 카드는 크기만 쓴다.</summary>
        public RectInt Physical;

        /// <summary>기본 크기(480×680)의 떠 있는 카드.</summary>
        public static WindowLayout Card(float scale)
        {
            return Card(scale, new Vector2Int(SceneMetrics.WindowWidth, SceneMetrics.WindowHeight));
        }

        /// <summary>떠 있는 카드. 자리는 끌어 옮긴 곳을 지키므로 크기만 정한다.</summary>
        public static WindowLayout Card(float scale, Vector2Int logical)
        {
            logical = Vector2Int.Max(logical, Vector2Int.one);
            return new WindowLayout
            {
                Sidebar = false,
                Logical = logical,
                Scale = scale,
                Physical = new RectInt(0, 0, ToPhysical(logical.x, scale), ToPhysical(logical.y, scale)),
            };
        }

        /// <summary>
        /// 사이드바. right는 붙일 오른쪽 가장자리(모니터 오른쪽, 다른 앱바가 있으면 그 왼쪽), top·bottom은 작업 영역이다.
        /// </summary>
        public static WindowLayout Dock(float scale, int right, int top, int bottom, int logicalWidth)
        {
            int width = ToPhysical(logicalWidth, scale);
            int height = Mathf.Max(1, bottom - top);
            return new WindowLayout
            {
                Sidebar = true,
                Logical = new Vector2Int(logicalWidth, Mathf.Max(1, Mathf.RoundToInt(height / scale))),
                Scale = scale,
                Physical = new RectInt(right - width, top, width, height),
            };
        }

        /// <summary>
        /// 사이드바 배율: 화면 배율과, 논리 높이가 maxLogicalHeight를 넘지 않게 하는 배율 중 큰 쪽.
        /// 아주 높은 모니터에서 논리 높이를 그대로 늘리면 하늘 화각이 천정을 넘어 구도가 무너지므로,
        /// 대신 사이드바 전체를 키워 어느 모니터에서나 같은 구도·같은 화면 비율을 지킨다.
        /// </summary>
        public static float SidebarScale(float dpiScale, int physicalHeight, int maxLogicalHeight)
        {
            return Mathf.Max(dpiScale, physicalHeight / (float)Mathf.Max(1, maxLogicalHeight));
        }

        private static int ToPhysical(int logical, float scale) => Mathf.Max(1, Mathf.RoundToInt(logical * scale));
    }
}
