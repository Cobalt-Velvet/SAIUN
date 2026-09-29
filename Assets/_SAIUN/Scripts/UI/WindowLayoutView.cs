using _SAIUN.Scripts.Core;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 창 모양에 맞춰 UI를 맞춘다 (2026-09-29, 사이드바).
    /// 캔버스는 화면 배율만큼 키우고(논리 화소로 짠 UI가 배율 125~150% 화면에서도 같은 크기로 보인다),
    /// 카드 높이를 창의 논리 높이로 늘리며, 창 크기로 구운 테두리·알림 빛을 다시 굽는다.
    /// 바탕화면을 읽는 유리는 창 크기로 읽으므로 켜져 있으면 다시 켠다.
    /// </summary>
    public class WindowLayoutView : MonoBehaviour
    {
        [SerializeField] private WindowController window;

        [Tooltip("화면 배율을 입힐 캔버스들")]
        [SerializeField] private CanvasScaler[] scalers;

        [Tooltip("높이를 창의 논리 높이로 맞출 카드들(UI·배경)")]
        [SerializeField] private RectTransform[] cards;

        [SerializeField] private GlassRimView rim;
        [SerializeField] private ScreenAlertView alert;
        [SerializeField] private GameObject desktopGlass;

        private void Awake()
        {
            if (window == null) window = FindFirstObjectByType<WindowController>();
            if (window != null) window.OnLayoutChanged += Apply;
        }

        private void OnDestroy()
        {
            if (window != null) window.OnLayoutChanged -= Apply;
        }

        /// <summary>창 모양에 맞춘다. 창이 바뀔 때 WindowController가 부른다.</summary>
        internal void Apply(WindowLayout layout)
        {
            if (scalers != null)
            {
                foreach (CanvasScaler scaler in scalers)
                {
                    if (scaler != null) scaler.scaleFactor = layout.Scale;
                }
            }

            if (cards != null)
            {
                foreach (RectTransform card in cards)
                {
                    if (card != null) card.sizeDelta = new Vector2(card.sizeDelta.x, layout.Logical.y);
                }
            }

            if (rim != null) rim.Rebuild(layout.Logical, rounded: !layout.Sidebar);
            if (alert != null) alert.Resize(layout.Logical);

            if (desktopGlass != null && desktopGlass.activeSelf)
            {
                desktopGlass.SetActive(false);
                desktopGlass.SetActive(true);
            }
        }
    }
}
