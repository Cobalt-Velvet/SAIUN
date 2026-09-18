using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 확인·경고 다이얼로그 (사양서 v1.1 8-2 "경고 다이얼로그", 12-2-1 "확인 다이얼로그").
    /// 뒤를 흐리게 덮고 클릭을 막는다. 취소 버튼 문구를 비우면 확인 버튼 하나만 보인다.
    /// </summary>
    public class MessageDialogView : MonoBehaviour
    {
        [Tooltip("열고 닫는 다이얼로그 본체")]
        [SerializeField] private GameObject dialog;

        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text cancelLabel;

        [SerializeField] private string defaultConfirm = "확인";

        public bool IsShowing => dialog != null && dialog.activeSelf;
        public string Title => titleText != null ? titleText.text : string.Empty;
        public string Body => bodyText != null ? bodyText.text : string.Empty;

        private Action _onConfirm;

        private void Awake()
        {
            if (dialog != null) dialog.SetActive(false);
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
        }

        /// <summary>다이얼로그를 띄운다. 이미 떠 있으면 내용을 바꾼다.</summary>
        /// <param name="onConfirm">확인을 눌렀을 때. 닫힌 뒤에 부른다.</param>
        /// <param name="cancel">취소 버튼 문구. 비우면 취소 버튼을 숨긴다.</param>
        public void Show(string title, string body, string confirm = null, Action onConfirm = null, string cancel = null)
        {
            if (dialog == null) return;
            if (titleText != null) titleText.text = title;
            if (bodyText != null) bodyText.text = body;
            if (confirmLabel != null) confirmLabel.text = string.IsNullOrEmpty(confirm) ? defaultConfirm : confirm;

            bool cancellable = !string.IsNullOrEmpty(cancel);
            if (cancelButton != null) cancelButton.gameObject.SetActive(cancellable);
            if (cancelLabel != null && cancellable) cancelLabel.text = cancel;

            _onConfirm = onConfirm;
            dialog.SetActive(true);
        }

        public void Confirm()
        {
            Action action = _onConfirm;
            Close();
            action?.Invoke();
        }

        public void Close()
        {
            _onConfirm = null;
            if (dialog != null) dialog.SetActive(false);
        }
    }
}
