using System;
using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 구름층이 뜨는 높이 (2026-10-01 사용자 "같은 종류의 구름은 항상 모두 같은 높이에 있어야 하나?" → "다양화").
    /// 같은 종류라도 날마다, 공기마다 뜨는 높이가 다르다. 층이 보이지 않게 된 사이에 범위 안에서 새 높이를 골라,
    /// 다음에 그 층이 나타날 때는 다른 높이(높으면 결이 잘고 느리게, 낮으면 크고 빠르게 보인다)로 뜬다.
    /// 보이는 동안에는 높이를 바꾸지 않아 구름이 튀지 않는다. 층의 굽이와 기울기는 셰이더(LayerLift)가 높이에서 짓는다.
    /// 적운 밑면은 하루 동안 올라간다: 땅이 데워지고 마를수록 응결 높이가 오른다. 밑면은 늘 평평하게 한 줄로 늘어선다.
    /// </summary>
    [Serializable]
    public class CloudHeights
    {
        // 이만큼 옅으면 보이지 않는 것으로 본다.
        private const float InvisibleCover = 0.005f;

        [Tooltip("권운이 뜨는 높이 범위(km)")]
        [SerializeField] private Vector2 cirrus = new Vector2(8f, 11f);

        [Tooltip("권적운이 뜨는 높이 범위(km)")]
        [SerializeField] private Vector2 cirrocumulus = new Vector2(6.5f, 9f);

        [Tooltip("권층운이 뜨는 높이 범위(km)")]
        [SerializeField] private Vector2 cirrostratus = new Vector2(7.5f, 10f);

        [Tooltip("고적운이 뜨는 높이 범위(km)")]
        [SerializeField] private Vector2 altocumulus = new Vector2(3f, 5.5f);

        [Tooltip("고층운이 뜨는 높이 범위(km)")]
        [SerializeField] private Vector2 altostratus = new Vector2(3.8f, 6f);

        [Tooltip("층적운이 뜨는 높이 범위(km)")]
        [SerializeField] private Vector2 stratocumulus = new Vector2(1.2f, 2.2f);

        [Tooltip("적운 밑면 높이(km): x 아침, y 오후. 땅이 데워지며 응결 높이가 오른다.")]
        [SerializeField] private Vector2 cumulusBase = new Vector2(0.9f, 1.6f);

        [Tooltip("적운 밑면이 날마다 달라지는 폭(km, ±). 습한 날은 낮고 마른 날은 높다.")]
        [SerializeField, Min(0f)] private float cumulusBaseSpread = 0.2f;

        [Tooltip("적운 밑면이 목표 높이를 따라가는 시간(초). 세션이 새 아침으로 넘어가도 밑면이 한 번에 내려앉지 않는다.")]
        [SerializeField, Min(0.01f)] private float cumulusBaseLag = 90f;

        // 높이를 바꾸는 층(셰이더 _AltHigh·_AltLow 순서)
        private static readonly CloudKind[] Layers =
        {
            CloudKind.Cirrus, CloudKind.Cirrocumulus, CloudKind.Cirrostratus,
            CloudKind.Altocumulus, CloudKind.Altostratus, CloudKind.Stratocumulus,
        };

        private readonly float[] _altitude = new float[Layers.Length];
        private readonly bool[] _visible = new bool[Layers.Length];
        private bool _cumulusVisible;
        private float _cumulusOffset;

        /// <summary>적운 밑면 높이(km).</summary>
        public float CumulusBase { get; private set; } = 1.3f;

        /// <summary>층의 높이(km). 높이를 바꾸지 않는 층은 0이다.</summary>
        public float Altitude(CloudKind kind)
        {
            int i = Array.IndexOf(Layers, kind);
            return i >= 0 ? _altitude[i] : 0f;
        }

        /// <summary>모든 층의 높이를 새로 고르고 적운 밑면을 지금 진행률의 높이에 둔다. random은 0~1을 돌려준다.</summary>
        public void Reset(float progress, Func<float> random)
        {
            for (int i = 0; i < Layers.Length; i++)
            {
                _altitude[i] = Pick(Range(Layers[i]), random);
                _visible[i] = false;
            }
            _cumulusOffset = (random() * 2f - 1f) * cumulusBaseSpread;
            _cumulusVisible = false;
            CumulusBase = CumulusTarget(progress);
        }

        /// <summary>
        /// 한 걸음 진행한다. coverage는 층마다 지금 보이는 양이다. 보이던 층이 사라지면 다음에 뜰 높이를 새로 고르고,
        /// 적운 밑면은 진행률을 따라 천천히 오르내린다.
        /// </summary>
        public void Tick(float deltaTime, float progress, Func<CloudKind, float> coverage, Func<float> random)
        {
            for (int i = 0; i < Layers.Length; i++)
            {
                bool visible = coverage(Layers[i]) > InvisibleCover;
                if (_visible[i] && !visible) _altitude[i] = Pick(Range(Layers[i]), random);
                _visible[i] = visible;
            }

            bool cumulus = coverage(CloudKind.Cumulus) > InvisibleCover;
            if (_cumulusVisible && !cumulus) _cumulusOffset = (random() * 2f - 1f) * cumulusBaseSpread;
            _cumulusVisible = cumulus;
            CumulusBase = Mathf.Lerp(CumulusBase, CumulusTarget(progress), 1f - Mathf.Exp(-deltaTime / cumulusBaseLag));
        }

        private float CumulusTarget(float progress)
        {
            return Mathf.Lerp(cumulusBase.x, cumulusBase.y, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.75f, progress)))
                   + _cumulusOffset;
        }

        private Vector2 Range(CloudKind kind)
        {
            switch (kind)
            {
                case CloudKind.Cirrus: return cirrus;
                case CloudKind.Cirrocumulus: return cirrocumulus;
                case CloudKind.Cirrostratus: return cirrostratus;
                case CloudKind.Altocumulus: return altocumulus;
                case CloudKind.Altostratus: return altostratus;
                default: return stratocumulus;
            }
        }

        private static float Pick(Vector2 range, Func<float> random) => Mathf.Lerp(range.x, range.y, random());
    }
}
