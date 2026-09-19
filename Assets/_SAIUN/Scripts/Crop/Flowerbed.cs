using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Crop
{
    /// <summary>
    /// 아이소메트릭 화단의 그리드 좌표계 (사양서 v1.1 2-5, 7-4).
    /// 트랜스폼 위치가 화단 바닥 중심이자 월드 원점이다. 열은 로컬 X, 행은 로컬 Z 방향으로 늘어난다.
    /// 칸 간격·윗면 높이·작물 Y 오프셋은 실측 대기값이라 인스펙터에 둔다.
    /// 외형(화분·흙 칸·그림자 받이)이 연결돼 있으면 값을 바꿀 때 크기를 따라 맞춘다.
    /// </summary>
    public class Flowerbed : MonoBehaviour
    {
        [Header("그리드 (실측 대기)")]
        [Tooltip("칸 한 변의 월드 길이")]
        [SerializeField, Min(0.05f)] private float cellSize = 0.4f;

        [Tooltip("바닥에서 흙 윗면까지의 높이")]
        [SerializeField, Min(0f)] private float surfaceHeight = 0.24f;

        [Tooltip("흙 윗면에서 작물 기준점까지 띄우는 높이")]
        [SerializeField] private float cropYOffset;

        [Header("임시 외형 (아트가 들어오면 비워 둔다)")]
        [SerializeField] private Transform planter;

        [Tooltip("칸마다 흙 타일을 자식으로 둔다. 0행 0~5열, 1행 0~5열 순서.")]
        [SerializeField] private Transform soilRoot;

        [Tooltip("흙 칸 바깥으로 두르는 화분 테두리 폭")]
        [SerializeField, Min(0f)] private float rimWidth = 0.08f;

        [Tooltip("칸 크기 대비 흙 타일 크기. 1보다 작으면 칸 사이에 틈이 보인다.")]
        [SerializeField, Range(0.5f, 1f)] private float soilFill = 0.86f;

        [Tooltip("흙 타일이 화분 테두리보다 솟은 높이")]
        [SerializeField, Min(0.001f)] private float soilRaise = 0.03f;

        [Header("그림자 받이")]
        [Tooltip("그림자만 그리는 바닥. 화단보다 이만큼 넓게 깐다.")]
        [SerializeField] private Transform shadowGround;

        [SerializeField, Min(0f)] private float shadowMargin = 1f;

        public int Columns => SceneMetrics.FlowerbedColumns;
        public int Rows => SceneMetrics.FlowerbedRows;
        public int CellCount => Columns * Rows;
        public float CellSize => cellSize;
        public float SurfaceHeight => surfaceHeight;
        public float CropYOffset => cropYOffset;

        /// <summary>화분 테두리 폭.</summary>
        public float RimWidth => rimWidth;

        /// <summary>화분 윗면(테두리) 높이. 흙은 이보다 조금 솟아 있다.</summary>
        public float RimHeight => Mathf.Max(0f, surfaceHeight - soilRaise);

        /// <summary>흙 윗면의 가로(열 방향)·세로(행 방향) 월드 길이. 테두리는 뺀다.</summary>
        public Vector2 GridSize => new Vector2(Columns * cellSize, Rows * cellSize);

        /// <summary>
        /// 그리드 좌표를 작물 기준점의 월드 좌표로 바꾼다. 칸 중심이 정수 좌표다.
        /// 소수 좌표도 받으므로 칸 사이에 놓을 때도 쓴다. 트랜스폼 배율은 무시한다.
        /// </summary>
        public Vector3 GridToWorld(float column, float row)
        {
            return transform.position + transform.rotation * GridToLocal(column, row, surfaceHeight + cropYOffset);
        }

        /// <summary>칸 중심의 작물 기준점. 범위를 벗어나면 가장자리 칸으로 자른다.</summary>
        public Vector3 CellPosition(int column, int row)
        {
            return GridToWorld(Mathf.Clamp(column, 0, Columns - 1), Mathf.Clamp(row, 0, Rows - 1));
        }

        /// <summary>외형을 현재 그리드 값에 맞춘다. 연결되지 않은 외형은 건너뛴다.</summary>
        public void FitVisuals()
        {
            Vector2 grid = GridSize;
            float planterHeight = RimHeight;

            if (planter != null)
            {
                SetLocal(planter,
                    new Vector3(0f, planterHeight / 2f, 0f),
                    new Vector3(grid.x + rimWidth * 2f, planterHeight, grid.y + rimWidth * 2f));
            }

            if (soilRoot != null)
            {
                SetLocal(soilRoot, Vector3.zero, Vector3.one);
                int count = Mathf.Min(soilRoot.childCount, CellCount);
                for (int i = 0; i < count; i++)
                {
                    // 타일 윗면이 흙 윗면과 맞고, 아랫부분은 화분 속에 묻힌다.
                    Vector3 center = GridToLocal(i % Columns, i / Columns, surfaceHeight - soilRaise);
                    SetLocal(soilRoot.GetChild(i), center,
                        new Vector3(cellSize * soilFill, soilRaise * 2f, cellSize * soilFill));
                }
            }

            if (shadowGround != null)
            {
                // Quad는 XY 평면이라 눕혀서 깐다. 배율의 y가 월드 Z 길이가 된다.
                float border = (rimWidth + shadowMargin) * 2f;
                shadowGround.localPosition = Vector3.zero;
                shadowGround.localRotation = Quaternion.Euler(90f, 0f, 0f);
                shadowGround.localScale = new Vector3(grid.x + border, grid.y + border, 1f);
            }
        }

        private Vector3 GridToLocal(float column, float row, float height)
        {
            return new Vector3(
                (column - (Columns - 1) / 2f) * cellSize,
                height,
                (row - (Rows - 1) / 2f) * cellSize);
        }

        private static void SetLocal(Transform target, Vector3 position, Vector3 scale)
        {
            target.localPosition = position;
            target.localRotation = Quaternion.identity;
            target.localScale = scale;
        }

#if UNITY_EDITOR
        // 인스펙터에서 칸 간격 등을 바꾸면 외형이 바로 따라오게 한다. 실측용.
        // OnValidate 안에서 트랜스폼을 바꾸면 경고가 나므로 다음 에디터 틱으로 미룬다.
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) FitVisuals();
            };
        }

        // 선택했을 때 칸 경계를 그려 작물 위치를 가늠할 수 있게 한다.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = SaiunPalette.MainPoint;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            var size = new Vector3(cellSize, 0f, cellSize);
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    Gizmos.DrawWireCube(GridToLocal(column, row, surfaceHeight), size);
                }
            }
        }
#endif
    }
}
