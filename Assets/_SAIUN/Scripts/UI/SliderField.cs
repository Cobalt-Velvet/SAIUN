using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace _SAIUN.Scripts.UI
{
    /// <summary>
    /// 슬라이더와 숫자 입력칸이 같은 정수를 가리키는 한 줄 (사양서 v1.1 12-1 "슬라이더 + 숫자 입력").
    /// 어느 쪽을 바꿔도 다른 쪽이 따라오고, 범위 밖 입력은 경계값으로 자른다.
    /// </summary>
    public class SliderField : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Slider slider;
        [SerializeField] private TMP_InputField input;

        public int Value { get; private set; }
        public int Min { get; private set; }
        public int Max { get; private set; }

        /// <summary>사용자가 값을 바꿨을 때 발행. SetValue로 바꾼 것은 알리지 않는다.</summary>
        public event Action<int> OnValueChanged;

        public Slider Slider => slider;
        public TMP_InputField Input => input;

        private bool _listening;

        /// <summary>범위와 현재 값을 정한다. 알림은 보내지 않는다.</summary>
        public void Setup(string text, int min, int max, int value)
        {
            if (label != null) label.text = text;
            Min = Mathf.Min(min, max);
            Max = Mathf.Max(min, max);

            if (slider != null)
            {
                slider.wholeNumbers = true;
                slider.minValue = Min;
                slider.maxValue = Max;
            }
            if (input != null)
            {
                input.contentType = TMP_InputField.ContentType.IntegerNumber;
                input.characterLimit = Max.ToString().Length;
            }

            Listen();
            SetValue(value);
        }

        /// <summary>값을 바꾼다. 알림은 보내지 않는다.</summary>
        public void SetValue(int value)
        {
            Value = Mathf.Clamp(value, Min, Max);
            if (slider != null) slider.SetValueWithoutNotify(Value);
            if (input != null) input.SetTextWithoutNotify(Value.ToString());
        }

        private void Listen()
        {
            if (_listening) return;
            _listening = true;
            if (slider != null) slider.onValueChanged.AddListener(v => Commit(Mathf.RoundToInt(v)));
            // 입력 중간값(빈 칸 등)으로 흔들리지 않도록 입력을 마쳤을 때 반영한다.
            if (input != null) input.onEndEdit.AddListener(HandleInput);
        }

        private void HandleInput(string text)
        {
            if (int.TryParse(text, out int parsed)) Commit(parsed);
            else SetValue(Value);   // 숫자가 아니면 원래 값으로 되돌린다
        }

        private void Commit(int value)
        {
            int previous = Value;
            SetValue(value);
            if (Value != previous) OnValueChanged?.Invoke(Value);
        }
    }
}
