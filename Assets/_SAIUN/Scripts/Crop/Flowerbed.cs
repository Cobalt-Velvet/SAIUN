using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Crop
{
    /// <summary>
    /// 아이소메트릭 화단의 그리드 좌표계 (사양서 v1.1 2-5, 7-4).
    /// 트랜스폼 위치가 화단 바닥 중심이자 월드 원점이다. 열은 로컬 X, 행은 로컬 Z 방향으로 늘어난다.
    /// 칸 간격·윗면 높이·작물 Y 오프셋은 실측 대기값이라 인스펙터에 둔다.
    /// 외형(화분·흙·데크)이 연결돼 있으면 값을 바꿀 때 따라 맞춘다.
    /// 화분·흙·데크는 기본 도형 대신 FlowerbedMeshes가 만드는 메시다(바랜 나무 판자 상자, 둔덕·고랑이 있는 흙 면,
    /// 화분이 놓인 바닷가 나무 데크). 데크가 그림자를 받으므로 그림자만 그리는 바닥은 두지 않는다(2026-09-29).
    /// 메시는 실행할 때(에디터에서는 값을 바꿀 때) 새로 만들고 씬에는 저장하지 않는다.
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

        [Header("화분 (외형)")]
        [Tooltip("MeshFilter가 있으면 화분 메시를 채운다")]
        [SerializeField] private Transform planter;

        [Tooltip("흙 칸 바깥으로 두르는 화분 테두리 폭")]
        [SerializeField, Min(0.01f)] private float rimWidth = 0.08f;

        [Tooltip("테두리 윗면이 흙 둔덕 꼭대기보다 높은 정도")]
        [SerializeField, Min(0f)] private float rimLip = 0.025f;

        [Tooltip("나무 텍스처 한 장이 덮는 결 방향 길이(월드). 화분과 데크가 같이 쓴다.")]
        [SerializeField, Min(0.01f)] private float woodRepeat = 1.3f;

        [Header("흙 (외형)")]
        [Tooltip("MeshFilter가 있으면 흙 면 메시를 채운다")]
        [SerializeField] private Transform soilRoot;

        [Tooltip("칸 가운데 둔덕 높이. 꼭대기가 흙 윗면 높이다.")]
        [SerializeField, Min(0f)] private float moundHeight = 0.035f;

        [Tooltip("둔덕 반경(칸 크기 대비)")]
        [SerializeField, Range(0.1f, 0.7f)] private float moundRadius = 0.42f;

        [Tooltip("칸 사이 고랑 깊이(둔덕 바닥에서 더 파는 깊이)")]
        [SerializeField, Min(0f)] private float grooveDepth = 0.02f;

        [Tooltip("고랑 반폭(월드)")]
        [SerializeField, Min(0.001f)] private float grooveWidth = 0.05f;

        [Tooltip("흙 알갱이 요철 높이")]
        [SerializeField, Min(0f)] private float grain = 0.004f;

        [Tooltip("흙 알갱이 크기(월드)")]
        [SerializeField, Min(0.005f)] private float grainSize = 0.05f;

        [Tooltip("칸 한 변을 나누는 수. 클수록 둔덕·고랑이 매끈하다.")]
        [SerializeField, Range(2, 32)] private int soilResolution = 16;

        [Header("데크 (외형)")]
        [Tooltip("MeshFilter가 있으면 데크 메시를 채운다. 화분은 데크 뒤 모서리에 놓이고 데크는 보는 쪽으로 뻗는다.")]
        [SerializeField] private Transform deck;

        [Tooltip("화분 왼쪽(−X)으로 드러나는 데크 폭(월드)")]
        [SerializeField, Min(0f)] private float deckSideMargin = 0.62f;

        [Tooltip("화분 앞(−Z, 보는 쪽)으로 뻗는 데크 길이. 화면 아래로 빠질 만큼 길게 둔다.")]
        [SerializeField, Min(0f)] private float deckReach = 3.2f;

        [Tooltip("화분 뒤·오른쪽으로 삐져나오는 데크 턱. 크면 데크가 지평선 위(하늘·바다)를 덮는다.")]
        [SerializeField, Min(0f)] private float deckLip = 0.05f;

        [Tooltip("널빤지 폭")]
        [SerializeField, Min(0.01f)] private float plankWidth = 0.14f;

        [Tooltip("널빤지 사이 틈")]
        [SerializeField, Min(0f)] private float plankGap = 0.012f;

        [Tooltip("널빤지 두께")]
        [SerializeField, Min(0.005f)] private float plankThickness = 0.04f;

        [Tooltip("널빤지 밑 장선 층 두께(틈으로 어둡게 보인다)")]
        [SerializeField, Min(0.005f)] private float joistDepth = 0.09f;

        [Tooltip("왼쪽 모서리를 덮는 옆판 두께")]
        [SerializeField, Min(0.005f)] private float fasciaThickness = 0.03f;

        [Tooltip("기둥 굵기")]
        [SerializeField, Min(0.01f)] private float postSize = 0.1f;

        [Tooltip("기둥이 내려가는 깊이(화면 아래로 빠진다)")]
        [SerializeField, Min(0f)] private float postDepth = 3f;

        [Tooltip("기둥 사이 간격")]
        [SerializeField, Min(0.05f)] private float postSpacing = 1.2f;

        public int Columns => SceneMetrics.FlowerbedColumns;
        public int Rows => SceneMetrics.FlowerbedRows;
        public int CellCount => Columns * Rows;
        public float CellSize => cellSize;
        public float SurfaceHeight => surfaceHeight;
        public float CropYOffset => cropYOffset;

        /// <summary>화분 테두리 폭.</summary>
        public float RimWidth => rimWidth;

        /// <summary>화분 윗면(테두리) 높이. 흙 둔덕 꼭대기보다 조금 높다.</summary>
        public float RimHeight => surfaceHeight + rimLip;

        /// <summary>화분 뒤 모서리(+X,+Z)의 판자 윗면 가운데 점(로컬). 풍향계를 세운다.</summary>
        public Vector3 RimCorner
        {
            get
            {
                Vector2 grid = GridSize;
                return new Vector3(grid.x / 2f + rimWidth / 2f, RimHeight, grid.y / 2f + rimWidth / 2f);
            }
        }

        /// <summary>데크 모양(로컬, 윗면 높이 0).</summary>
        internal DeckShape DeckShape
        {
            get
            {
                Vector2 grid = GridSize;
                float x = grid.x / 2f + rimWidth;
                float z = grid.y / 2f + rimWidth;
                return new DeckShape
                {
                    XMin = -x - deckSideMargin,
                    XMax = x + deckLip,
                    ZMin = -z - deckReach,
                    ZMax = z + deckLip,
                    PlankWidth = plankWidth,
                    PlankGap = plankGap,
                    PlankThickness = plankThickness,
                    JoistDepth = joistDepth,
                    FasciaThickness = fasciaThickness,
                    PostSize = postSize,
                    PostDepth = postDepth,
                    PostSpacing = postSpacing,
                    WoodRepeat = woodRepeat,
                };
            }
        }

        internal SoilShape SoilShape => new SoilShape
        {
            Columns = Columns,
            Rows = Rows,
            CellSize = cellSize,
            Surface = surfaceHeight,
            MoundHeight = moundHeight,
            MoundRadius = moundRadius,
            GrooveDepth = grooveDepth,
            GrooveWidth = grooveWidth,
            Grain = grain,
            GrainSize = grainSize,
            Resolution = soilResolution,
        };

        private Mesh _planterMesh;
        private Mesh _soilMesh;
        private Mesh _deckMesh;

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

        private void Awake()
        {
            // 메시는 씬에 저장하지 않으므로 실행할 때 만든다.
            FitVisuals();
        }

        private void OnDestroy()
        {
            Release(ref _planterMesh);
            Release(ref _soilMesh);
            Release(ref _deckMesh);
        }

        /// <summary>외형을 현재 그리드 값에 맞춘다. 연결되지 않은 외형은 건너뛴다.</summary>
        public void FitVisuals()
        {
            Vector2 grid = GridSize;

            if (planter != null && planter.TryGetComponent(out MeshFilter planterFilter))
            {
                SetLocal(planter, Vector3.zero, Vector3.one);
                Release(ref _planterMesh);
                _planterMesh = FlowerbedMeshes.BuildPlanter(grid, rimWidth, RimHeight, woodRepeat);
                planterFilter.sharedMesh = _planterMesh;
            }

            if (soilRoot != null && soilRoot.TryGetComponent(out MeshFilter soilFilter))
            {
                SetLocal(soilRoot, Vector3.zero, Vector3.one);
                Release(ref _soilMesh);
                _soilMesh = FlowerbedMeshes.BuildSoil(SoilShape);
                soilFilter.sharedMesh = _soilMesh;
            }

            if (deck != null && deck.TryGetComponent(out MeshFilter deckFilter))
            {
                SetLocal(deck, Vector3.zero, Vector3.one);
                Release(ref _deckMesh);
                _deckMesh = FlowerbedMeshes.BuildDeck(DeckShape);
                deckFilter.sharedMesh = _deckMesh;
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

        private static void Release(ref Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
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
