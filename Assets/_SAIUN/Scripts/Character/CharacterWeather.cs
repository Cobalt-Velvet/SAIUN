using System.Collections.Generic;
using _SAIUN.Scripts.Weather;
using UniGLTF.SpringBoneJobs.Blittables;
using UniVRM10;
using Unity.Mathematics;
using UnityEngine;

namespace _SAIUN.Scripts.Character
{
    /// <summary>
    /// 캐릭터의 환경 반응 (P3-05, 사양서 v1.1 8-7).
    /// 바람: 머리카락·옷자락(VRM SpringBone)에 바람 방향·세기만큼 외력을 더한다. 돌풍처럼 세기가 출렁인다.
    /// 비: MToon에는 사양서가 말한 wetness 파라미터가 없어서, 젖은 천·머리카락을 흉내 낸다.
    /// 기본색·그늘색을 짙게 하고, 가장자리에 물기 어린 광택(파라메트릭 림)을 얹는다.
    /// 머티리얼은 VRM을 불러올 때 새로 만든 것이라 직접 고쳐도 에셋이 바뀌지 않으며, 끌 때 원래 값으로 되돌린다.
    /// </summary>
    public class CharacterWeather : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int RimFresnelPowerId = Shader.PropertyToID("_RimFresnelPower");
        private static readonly int RimLiftId = Shader.PropertyToID("_RimLift");

        [SerializeField] private VrmLoader loader;
        [SerializeField] private WeatherController weather;

        [Header("바람 → SpringBone")]
        [Tooltip("바람 세기 1당 외력. SpringBone의 중력(gravityPower)과 같은 단위다.")]
        [SerializeField, Min(0f)] private float forcePerWind = 0.06f;

        [Tooltip("돌풍: 세기가 이만큼 출렁인다(비율)")]
        [SerializeField, Range(0f, 1f)] private float gustiness = 0.35f;

        [Tooltip("돌풍 빠르기(초당 횟수)")]
        [SerializeField, Min(0f)] private float gustFrequency = 0.45f;

        [Header("비 → 젖은 옷·머리카락 (MToon)")]
        [Tooltip("흠뻑 젖었을 때 기본색이 어두워지는 정도")]
        [SerializeField, Range(0f, 1f)] private float colorDarkening = 0.25f;

        [Tooltip("흠뻑 젖었을 때 그늘색이 어두워지는 정도")]
        [SerializeField, Range(0f, 1f)] private float shadeDarkening = 0.35f;

        [Tooltip("젖은 면 가장자리에 도는 물기 광택 색")]
        [SerializeField] private Color wetSheen = new Color(0.5f, 0.56f, 0.6f, 1f);

        [Tooltip("광택이 가장자리에 몰리는 정도. 작을수록 넓게 번진다.")]
        [SerializeField, Min(0.01f)] private float wetFresnelPower = 3f;

        [Tooltip("광택을 안쪽까지 끌어올리는 정도")]
        [SerializeField, Range(0f, 1f)] private float wetRimLift = 0.06f;

        /// <summary>지금 SpringBone에 준 외력.</summary>
        public Vector3 WindForce { get; private set; }

        /// <summary>지금 입힌 젖음 정도.</summary>
        public float AppliedWetness { get; private set; }

        private struct Dry
        {
            public Material Material;
            public Color Color;
            public Color Shade;
            public Color Rim;
            public float FresnelPower;
            public float RimLift;
        }

        private readonly List<Dry> _materials = new List<Dry>();
        private Vrm10Instance _instance;
        private const float Epsilon = 0.002f;

        private void Awake()
        {
            if (loader == null) loader = GetComponent<VrmLoader>();
            if (weather == null) weather = FindFirstObjectByType<WeatherController>();
        }

        private void OnEnable()
        {
            if (loader == null) return;
            loader.OnCharacterLoaded += Bind;
            if (loader.CurrentModel != null) Bind(loader.CurrentModel);
        }

        private void OnDisable()
        {
            if (loader != null) loader.OnCharacterLoaded -= Bind;
            ApplyWetness(0f);
        }

        private void Update()
        {
            if (weather == null) return;
            ApplyWind(Time.time);
            float wetness = weather.Wetness;
            if (Mathf.Abs(wetness - AppliedWetness) > Epsilon || (wetness == 0f && AppliedWetness != 0f)) ApplyWetness(wetness);
        }

        /// <summary>바람을 SpringBone 외력으로 넣는다. 테스트는 시간을 정해 부른다.</summary>
        internal void ApplyWind(float time)
        {
            float gust = 1f + Mathf.Sin(time * gustFrequency * Mathf.PI * 2f) * gustiness;
            WindForce = weather.WindDirection * (weather.WindStrength * forcePerWind * gust);

            if (_instance == null) return;
            // 배율(모델 크기 2.4배)에 맞춰 SpringBone 반응 속도를 보정하게 한다.
            _instance.Runtime.SpringBone.SetModelLevel(_instance.transform,
                new BlittableModelLevel(externalForce: (float3)WindForce, supportsScalingAtRuntime: true));
        }

        /// <summary>젖음 정도를 머티리얼에 입힌다. 0이면 원래대로.</summary>
        internal void ApplyWetness(float wetness)
        {
            AppliedWetness = Mathf.Clamp01(wetness);
            foreach (Dry dry in _materials)
            {
                if (dry.Material == null) continue;
                float w = AppliedWetness;
                dry.Material.SetColor(ColorId, Darken(dry.Color, colorDarkening * w));
                dry.Material.SetColor(ShadeColorId, Darken(dry.Shade, shadeDarkening * w));
                dry.Material.SetColor(RimColorId, Color.Lerp(dry.Rim, dry.Rim + wetSheen, w));
                dry.Material.SetFloat(RimFresnelPowerId, Mathf.Lerp(dry.FresnelPower, wetFresnelPower, w));
                dry.Material.SetFloat(RimLiftId, Mathf.Lerp(dry.RimLift, wetRimLift, w));
            }
        }

        private static Color Darken(Color color, float amount)
        {
            Color result = color * (1f - amount);
            result.a = color.a;
            return result;
        }

        private void Bind(GameObject model)
        {
            ApplyWetness(0f);
            _materials.Clear();
            _instance = model != null ? model.GetComponent<Vrm10Instance>() : null;
            if (model == null) return;

            var seen = new HashSet<Material>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !seen.Add(material) || !material.HasProperty(RimColorId)) continue;
                    _materials.Add(new Dry
                    {
                        Material = material,
                        Color = material.GetColor(ColorId),
                        Shade = material.GetColor(ShadeColorId),
                        Rim = material.GetColor(RimColorId),
                        FresnelPower = material.GetFloat(RimFresnelPowerId),
                        RimLift = material.GetFloat(RimLiftId),
                    });
                }
            }
            if (weather != null) ApplyWetness(weather.Wetness);
        }

        /// <summary>젖음을 입힐 수 있는 MToon 머티리얼 수. 테스트용.</summary>
        public int WettableMaterialCount => _materials.Count;
    }
}
