using UnityEngine;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 비에 젖은 흙·화분.
    /// 젖을수록 색이 짙어지고 반들반들해진다(URP Lit의 기본색·Smoothness).
    /// 공유 머티리얼 에셋을 건드리지 않도록 MaterialPropertyBlock을 쓰고, 다 마르면 블록을 떼어 원래대로 둔다.
    /// </summary>
    public class SurfaceWetness : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        [SerializeField] private WeatherController weather;

        [Tooltip("젖을 면들(흙 칸, 화분 등)")]
        [SerializeField] private Renderer[] surfaces = new Renderer[0];

        [Tooltip("흠뻑 젖었을 때 어두워지는 정도")]
        [SerializeField, Range(0f, 1f)] private float darkening = 0.35f;

        [Tooltip("흠뻑 젖었을 때의 매끈함")]
        [SerializeField, Range(0f, 1f)] private float wetSmoothness = 0.78f;

        /// <summary>지금 입힌 젖음 정도.</summary>
        public float AppliedWetness { get; private set; }

        private MaterialPropertyBlock _block;
        private const float Epsilon = 0.002f;

        private void Awake()
        {
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
            _block = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            Apply(0f);
        }

        private void Update()
        {
            if (weather == null) return;
            float wetness = weather.Wetness;
            if (Mathf.Abs(wetness - AppliedWetness) > Epsilon || (wetness == 0f && AppliedWetness != 0f)) Apply(wetness);
        }

        /// <summary>젖음 정도를 면에 입힌다. 테스트는 직접 부른다.</summary>
        internal void Apply(float wetness)
        {
            AppliedWetness = Mathf.Clamp01(wetness);
            foreach (Renderer surface in surfaces)
            {
                if (surface == null) continue;
                Material material = surface.sharedMaterial;
                if (AppliedWetness <= 0f || material == null || !material.HasProperty(BaseColorId))
                {
                    // 마르면 블록을 떼어 SRP Batcher가 다시 묶을 수 있게 한다.
                    surface.SetPropertyBlock(null);
                    continue;
                }

                Color dry = material.GetColor(BaseColorId);
                Color wet = dry * (1f - darkening * AppliedWetness);
                wet.a = dry.a;
                float drySmoothness = material.HasProperty(SmoothnessId) ? material.GetFloat(SmoothnessId) : 0f;

                _block ??= new MaterialPropertyBlock();
                _block.Clear();
                _block.SetColor(BaseColorId, wet);
                _block.SetFloat(SmoothnessId, Mathf.Lerp(drySmoothness, wetSmoothness, AppliedWetness));
                surface.SetPropertyBlock(_block);
            }
        }
    }
}
