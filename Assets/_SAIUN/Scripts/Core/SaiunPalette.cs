using UnityEngine;

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// 확정된 색 팔레트. 사양서 3-2의 용도별 색은 모두 여기서 가져온다.
    /// 코드 곳곳에 색 리터럴을 두지 않기 위한 단일 출처다.
    /// </summary>
    public static class SaiunPalette
    {
        // ---- 원본 팔레트 5색 ----
        public static readonly Color Eggshell = Hex("FAF3DD");
        public static readonly Color TeaGreen = Hex("C8D5B9");
        public static readonly Color MutedTeal = Hex("8FC0A9");
        public static readonly Color TropicalTeal = Hex("68B0AB");
        public static readonly Color JungleTeal = Hex("4A7C59");

        // ---- 파생색 ----

        /// <summary>
        /// Jungle Teal을 약 0.48배로 어둡게 한 값.
        /// 팔레트 5색끼리는 가장 밝은 쌍도 대비가 4.38뿐이라 본문 기준 4.5에 못 미친다.
        /// 이 배경 위에서는 5색이 모두 5.0 이상으로 읽힌다.
        /// </summary>
        public static readonly Color DeepJungle = Hex("24382C");

        /// <summary>
        /// 경고색 Living Coral. 팔레트에 붉은 계열이 없어 따로 지정받았다.
        /// 색상환에서 Tropical Teal과 171도 떨어져 사실상 보색이고,
        /// 하단 바 배경 위 대비는 4.59로 본문 기준을 넘는다.
        /// </summary>
        public static readonly Color Warning = Hex("FF6F61");

        // ---- 용도별 색 (사양서 3-2) ----

        /// <summary>메인 포인트 컬러. 사양서가 지정한 민트 계열이다.</summary>
        public static readonly Color MainPoint = TropicalTeal;

        /// <summary>수확 가능 컬러. 팔레트에서 가장 밝고 따뜻해 익은 느낌을 낸다.</summary>
        public static readonly Color Harvestable = Eggshell;

        /// <summary>HUD 텍스트 기본색.</summary>
        public static readonly Color HudText = Eggshell;

        /// <summary>Bottom Bar 배경색. 바탕화면이 비쳐도 글자가 읽히도록 거의 불투명하게 둔다.</summary>
        public static readonly Color BottomBarBackground = WithAlpha(DeepJungle, 0.92f);

        /// <summary>Bottom Bar 텍스트색.</summary>
        public static readonly Color BottomBarText = Eggshell;

        /// <summary>포인트 색 위에 올리는 글자색. 민트 위에서 대비 5.0이다.</summary>
        public static readonly Color OnMainPoint = DeepJungle;

        /// <summary>집중 구간 강조색.</summary>
        public static readonly Color FocusAccent = TropicalTeal;

        /// <summary>휴식 구간 강조색.</summary>
        public static readonly Color BreakAccent = MutedTeal;

        /// <summary>완료한 세트 도트.</summary>
        public static readonly Color SetDotCompleted = TropicalTeal;

        /// <summary>남은 세트 도트. 완료 도트보다 눈에 덜 띄게 반투명이다.</summary>
        public static readonly Color SetDotPending = WithAlpha(TeaGreen, 0.45f);

        /// <summary>태스크 입력 필드 배경.</summary>
        public static readonly Color InputBackground = Eggshell;

        /// <summary>태스크 입력 필드 글자. 밝은 배경 위에서 대비 11.3이다.</summary>
        public static readonly Color InputText = DeepJungle;

        // ---- 도우미 ----

        public static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        private static Color Hex(string rgb)
        {
            // 팔레트 상수는 코드에 박힌 값이라 실패할 일이 없지만, 오타를 눈에 띄게 만든다.
            return ColorUtility.TryParseHtmlString("#" + rgb, out Color color) ? color : Color.magenta;
        }
    }
}
