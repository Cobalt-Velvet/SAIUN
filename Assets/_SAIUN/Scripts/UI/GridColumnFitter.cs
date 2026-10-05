using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 고정 열 수 격자의 칸 폭을 격자 폭에 맞춘다(사이드바처럼 폭이 바뀔 때).
    /// 카드(폭 480)에 맞춰 칸 폭을 박아 두면 사이드바(폭 380)에서 마지막 칸이 잘리므로, 폭이 바뀔 때마다 나눠 맞춘다.
    /// </summary>
    [RequireComponent(typeof(GridLayoutGroup))]
    public class GridColumnFitter : MonoBehaviour
    {
        private GridLayoutGroup _grid;
        private RectTransform _rect;

        private void Awake()
        {
            _grid = GetComponent<GridLayoutGroup>();
            _rect = (RectTransform)transform;
            Fit();
        }

        // 숨어 있는 동안 창 모양이 바뀌면 크기 변화 알림을 못 받으므로 보일 때 다시 맞춘다.
        private void OnEnable()
        {
            Fit();
        }

        private void OnRectTransformDimensionsChange()
        {
            Fit();
        }

        private void OnTransformParentChanged()
        {
            Fit();
        }

        /// <summary>
        /// 칸 폭 = (부모 안쪽 폭 − 칸 사이 간격 합) / 열 수.
        /// 격자 자신의 폭은 부모 레이아웃이 칸 크기에서 되돌려 정하므로(되먹임) 부모 폭을 잰다.
        /// </summary>
        internal void Fit()
        {
            if (_grid == null || _rect == null || !(_rect.parent is RectTransform parent)) return;
            int columns = Mathf.Max(1, _grid.constraintCount);
            float inner = parent.rect.width;
            if (parent.TryGetComponent(out LayoutGroup layout)) inner -= layout.padding.horizontal;
            float width = inner - _grid.padding.horizontal - _grid.spacing.x * (columns - 1);
            if (width <= 0f) return;
            float cell = width / columns;
            if (Mathf.Abs(_grid.cellSize.x - cell) < 0.01f) return;
            _grid.cellSize = new Vector2(cell, _grid.cellSize.y);
        }
    }
}
