using UnityEngine;

namespace _SAIUN.Scripts.Core
{
    /// <summary>
    /// 확정된 색 팔레트. UI의 용도별 색은 모두 여기서 가져온다.
    /// 코드 곳곳에 색 리터럴을 두지 않기 위한 단일 출처다.
    ///
    /// "유목" 팔레트: 사양서 3-2의 민트·정글 5색이 바다·하늘·바랜 나무 데크와 따로 놀아,
    /// 바닷가에서 색을 새로 가져왔다. 파도에 씻긴 나무(숯빛 갈색·마른 나무빛), 모래, 바다 유리(청록), 산호.
    /// 작물의 잎·알곡·열매는 자연의 색이라 UI 팔레트와 떼어 따로 둔다(옛 5색 값 그대로).
    /// </summary>
    public static class SaiunPalette
    {
        // ---- 유목 팔레트 7색 ----

        /// <summary>바탕: 젖은 유목의 숯빛 갈색. 하단 바·패널·카드의 바탕이다.</summary>
        public static readonly Color Charcoal = Hex("2B2723");

        /// <summary>그늘진 유목. 패널 안 줄·작물 칸의 바탕으로 반투명하게 쓴다.</summary>
        public static readonly Color Driftwood = Hex("5A5048");

        /// <summary>마른 모래. 밝은 글자와 입력칸 바탕이다.</summary>
        public static readonly Color Sand = Hex("F3ECE0");

        /// <summary>마른 나무빛. 보조 설명 글자와 남은 세트 도트다.</summary>
        public static readonly Color DryWood = Hex("CFC4B4");

        /// <summary>세이지. 휴식 구간 강조다.</summary>
        public static readonly Color Sage = Hex("C7D3C4");

        /// <summary>파도에 닳은 바다 유리의 청록. 버튼·진행을 가리키는 포인트다.</summary>
        public static readonly Color SeaGlass = Hex("9CC3BC");

        /// <summary>
        /// 경고색 산호. 색상환에서 바다 유리와 161도 떨어져 색 차이로 읽히고,
        /// 숯빛 바탕 위 대비는 4.9로 본문 기준을 넘는다.
        /// </summary>
        public static readonly Color Coral = Hex("E8705C");

        // ---- 작물 (자연의 색, UI 팔레트와 따로) ----

        /// <summary>어린잎·새싹의 연두.</summary>
        public static readonly Color CropLeafLight = Hex("C8D5B9");

        /// <summary>잎의 초록.</summary>
        public static readonly Color CropLeaf = Hex("8FC0A9");

        /// <summary>짙은 잎.</summary>
        public static readonly Color CropLeafDark = Hex("4A7C59");

        /// <summary>씨앗·알곡·지지대·꽃의 상앗빛.</summary>
        public static readonly Color CropGrain = Hex("FAF3DD");

        /// <summary>익은 토마토.</summary>
        public static readonly Color CropFruit = Hex("FF6F61");

        // ---- 집중 작물 올리브와 토분 ----

        /// <summary>올리브 잎 윗면의 잿빛 초록.</summary>
        public static readonly Color OliveLeaf = Hex("7D8C6A");

        /// <summary>올리브 잎 뒷면의 은빛. 이 잎이 섞여 나무가 은빛으로 반짝인다.</summary>
        public static readonly Color OliveLeafSilver = Hex("B7C0A8");

        /// <summary>올리브 나무껍질의 잿빛 갈색.</summary>
        public static readonly Color OliveBark = Hex("6F655A");

        /// <summary>풋올리브.</summary>
        public static readonly Color OliveGreen = Hex("9AA65A");

        /// <summary>익은 올리브(검보라).</summary>
        public static readonly Color OliveRipe = Hex("4A2F45");

        /// <summary>햇볕과 바닷바람에 바랜 토분.</summary>
        public static readonly Color PotClay = Hex("C08F72");

        // ---- 용도별 색 ----

        /// <summary>메인 포인트 컬러: 바다 유리.</summary>
        public static readonly Color MainPoint = SeaGlass;

        /// <summary>경고 컬러: 산호.</summary>
        public static readonly Color Warning = Coral;

        /// <summary>수확 가능 컬러. 팔레트에서 가장 밝고 따뜻해 익은 느낌을 낸다.</summary>
        public static readonly Color Harvestable = Sand;

        /// <summary>HUD 텍스트 기본색.</summary>
        public static readonly Color HudText = Sand;

        /// <summary>보조 설명 글자(패널 소제목·힌트·취소 버튼).</summary>
        public static readonly Color SubtleText = DryWood;

        /// <summary>Bottom Bar 배경색. 바탕화면이 비쳐도 글자가 읽히도록 거의 불투명하게 둔다.</summary>
        public static readonly Color BottomBarBackground = WithAlpha(Charcoal, 0.92f);

        /// <summary>Bottom Bar 텍스트색.</summary>
        public static readonly Color BottomBarText = Sand;

        /// <summary>포인트 색 위에 올리는 글자색. 바다 유리 위에서 대비 7.7이다.</summary>
        public static readonly Color OnMainPoint = Charcoal;

        /// <summary>휴식 구간 강조색.</summary>
        public static readonly Color BreakAccent = Sage;

        /// <summary>완료한 세트 도트.</summary>
        public static readonly Color SetDotCompleted = SeaGlass;

        /// <summary>남은 세트 도트. 완료 도트보다 눈에 덜 띄게 반투명이다.</summary>
        public static readonly Color SetDotPending = WithAlpha(DryWood, 0.45f);

        /// <summary>태스크 입력 필드 배경.</summary>
        public static readonly Color InputBackground = Sand;

        /// <summary>태스크 입력 필드 글자. 밝은 배경 위에서 대비 12.6이다.</summary>
        public static readonly Color InputText = Charcoal;

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
