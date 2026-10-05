using _SAIUN.Scripts.Lighting;
using UnityEngine;
using UnityEngine.Rendering;

namespace _SAIUN.Scripts.Weather
{
    /// <summary>
    /// 정원이 하늘의 빛을 받게 해 화분과 배경이 한 빛 아래 놓이게 한다.
    /// Unity 기본 하늘(고정된 회청색)을 주변광으로 쓰면 노을이 져도 화분이 차가운 회색이라 하늘에서 읽는다.
    ///  - 주변광: 그려진 하늘 장을 몇 초마다 4×4로 줄여 읽어(GPU 비동기 읽기) 위쪽 하늘·지평선 하늘·바다와 모래밭의
    ///    빛깔을 삼색 주변광(위·가운데·아래)으로 쓴다. 한낮엔 푸른 그늘, 노을엔 금빛·분홍, 박명엔 푸르게 가라앉는다.
    ///  - 햇빛: 하늘의 해 고도에서 대기를 지난 해 빛깔(바다에 쓰는 것과 같다)을 해에 넘긴다. 낮은 해는 붉고 어둡다.
    /// 창 전체 유리일 때는 하늘을 그리지 않으므로 주변광을 씬에 저장된 값으로 되돌리고, 햇빛 빛깔만 따른다.
    /// </summary>
    public class SkyLighting : MonoBehaviour
    {
        // 줄여 읽는 장: 64×64에서 밉맵 4단계(4×4)를 읽는다.
        private const int ProbeSize = 64;
        private const int ProbeMip = 4;
        private const int ProbeCells = 4;

        // 4×4의 줄(아래부터): 0 모래밭·바다, 1 지평선 하늘, 2~3 위쪽 하늘
        private const int GroundRow = 0;
        private const int EquatorRow = 1;

        // 해 원반이 지평선에 걸려 사라지는 고도 구간(도)
        private const float SunsetFadeLow = -0.6f;
        private const float SunsetFadeHigh = 0.4f;

        // 휘도 가중치(Rec. 709)
        private static readonly Vector3 Luma = new Vector3(0.2126f, 0.7152f, 0.0722f);

        [SerializeField] private SkyView sky;

        [Tooltip("햇빛 빛깔·세기를 넘길 해")]
        [SerializeField] private SunOrbitController sun;

        [Tooltip("하늘 장을 다시 읽는 간격(초)")]
        [SerializeField, Min(0.1f)] private float sampleInterval = 1f;

        [Tooltip("주변광이 새로 읽은 하늘빛을 따라가는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float response = 1.5f;

        [Tooltip("하늘 장의 밝기를 주변광 세기로 옮기는 배율. 크면 그늘이 옅어진다.")]
        [SerializeField, Min(0f)] private float ambientScale = 0.85f;

        [Tooltip("한낮 햇빛(해 고도 60°)을 1로 볼 때의 휘도. 낮은 해의 세기를 이 값에 견준다.")]
        [SerializeField, Min(0.01f)] private float noonLuminance = 0.85f;

        [Tooltip("해가 낮을 때 하늘 셰이더가 노출을 올리는 만큼 햇빛도 올린다(최대 배율).")]
        [SerializeField, Min(1f)] private float lowSunExposure = 1.7f;

        [Tooltip("노출을 다 올리는 해 고도(도). 이보다 높으면 조금씩 1로 돌아간다.")]
        [SerializeField] private float lowSunExposureElevation = 25f;

        /// <summary>지금 위쪽 하늘 주변광.</summary>
        public Color SkyColor { get; private set; }

        /// <summary>지금 지평선 주변광.</summary>
        public Color EquatorColor { get; private set; }

        /// <summary>지금 바다·모래밭에서 튀어 오는 주변광.</summary>
        public Color GroundColor { get; private set; }

        /// <summary>하늘 장을 한 번이라도 읽었는지.</summary>
        public bool HasSample { get; private set; }

        private RenderTexture _probe;
        private float _sinceSample;
        private bool _pending;
        private Color _skyTarget;
        private Color _equatorTarget;
        private Color _groundTarget;

        // 씬에 저장된 주변광(창 전체 유리일 때 되돌린다)
        private AmbientMode _savedMode;
        private Color _savedSky;
        private Color _savedEquator;
        private Color _savedGround;

        private void Awake()
        {
            if (sky == null) sky = FindFirstObjectByType<SkyView>();
            if (sun == null) sun = FindFirstObjectByType<SunOrbitController>();
            _savedMode = RenderSettings.ambientMode;
            _savedSky = RenderSettings.ambientSkyColor;
            _savedEquator = RenderSettings.ambientEquatorColor;
            _savedGround = RenderSettings.ambientGroundColor;
            SkyColor = _skyTarget = _savedSky;
            EquatorColor = _equatorTarget = _savedEquator;
            GroundColor = _groundTarget = _savedGround;
        }

        private void OnDestroy()
        {
            Restore();
            if (_probe == null) return;
            _probe.Release();
            Destroy(_probe);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>한 프레임 분량. 테스트는 직접 부른다.</summary>
        internal void Tick(float deltaTime)
        {
            if (sky == null) return;
            UpdateSunlight();

            if (sky.WindowGlass)
            {
                Restore();
                return;
            }

            _sinceSample += deltaTime;
            if (_sinceSample >= sampleInterval && !_pending)
            {
                _sinceSample = 0f;
                RequestSample();
            }

            if (!HasSample) return;
            float follow = 1f - Mathf.Exp(-deltaTime / response);
            SkyColor = Color.Lerp(SkyColor, _skyTarget, follow);
            EquatorColor = Color.Lerp(EquatorColor, _equatorTarget, follow);
            GroundColor = Color.Lerp(GroundColor, _groundTarget, follow);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = SkyColor;
            RenderSettings.ambientEquatorColor = EquatorColor;
            RenderSettings.ambientGroundColor = GroundColor;
        }

        /// <summary>하늘 장을 곧바로 읽어 주변광에 그대로 적용한다(처음, 테스트).</summary>
        internal void SampleNow()
        {
            if (sky == null || sky.Target == null) return;
            EnsureProbe();
            Graphics.Blit(sky.Target, _probe);
            _probe.GenerateMips();
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(_probe, ProbeMip);
            request.WaitForCompletion();
            OnReadback(request);
            SkyColor = _skyTarget;
            EquatorColor = _equatorTarget;
            GroundColor = _groundTarget;
            Tick(0f);
        }

        private void RequestSample()
        {
            if (sky.Target == null || !SystemInfo.supportsAsyncGPUReadback) return;
            EnsureProbe();
            Graphics.Blit(sky.Target, _probe);
            _probe.GenerateMips();
            _pending = true;
            AsyncGPUReadback.Request(_probe, ProbeMip, OnReadback);
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            _pending = false;
            if (request.hasError || this == null) return;
            var data = request.GetData<Color>();
            if (data.Length < ProbeCells * ProbeCells) return;

            // 읽은 줄은 아래부터다(텍스처 좌표와 같다).
            Color RowAverage(int row)
            {
                Color sum = Color.clear;
                for (int x = 0; x < ProbeCells; x++) sum += data[row * ProbeCells + x];
                return sum / ProbeCells;
            }

            Color upper = Color.clear;
            for (int row = EquatorRow + 1; row < ProbeCells; row++) upper += RowAverage(row);
            upper /= ProbeCells - EquatorRow - 1;
            _skyTarget = Opaque(upper * ambientScale);
            _equatorTarget = Opaque(RowAverage(EquatorRow) * ambientScale);
            _groundTarget = Opaque(RowAverage(GroundRow) * ambientScale);
            if (!HasSample)
            {
                SkyColor = _skyTarget;
                EquatorColor = _equatorTarget;
                GroundColor = _groundTarget;
            }
            HasSample = true;
        }

        // 하늘의 해 고도에서 대기를 지난 햇빛을 해에 넘긴다: 빛깔은 가장 밝은 성분을 1로, 세기는 한낮 대비 휘도.
        private void UpdateSunlight()
        {
            if (sun == null) return;
            float elevation = Mathf.Asin(Mathf.Clamp(sky.SunDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector4 t = SkyView.SunlightAtSea(elevation);
            float peak = Mathf.Max(t.x, Mathf.Max(t.y, t.z));
            var color = new Color(t.x / peak, t.y / peak, t.z / peak);
            float luminance = Vector3.Dot(new Vector3(t.x, t.y, t.z), Luma);
            float exposure = Mathf.Lerp(lowSunExposure, 1f, Mathf.Clamp01(elevation / lowSunExposureElevation));
            // 해가 지평선 아래로 내려가면 햇빛이 끊긴다.
            float above = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SunsetFadeLow, SunsetFadeHigh, elevation));
            float strength = Mathf.Clamp01(luminance / noonLuminance * exposure) * above;
            sun.SetSkyLight(color, strength);
        }

        private void Restore()
        {
            RenderSettings.ambientMode = _savedMode;
            RenderSettings.ambientSkyColor = _savedSky;
            RenderSettings.ambientEquatorColor = _savedEquator;
            RenderSettings.ambientGroundColor = _savedGround;
        }

        private void EnsureProbe()
        {
            if (_probe != null) return;
            _probe = new RenderTexture(ProbeSize, ProbeSize, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = "Sky Light Probe",
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
            };
            _probe.Create();
        }

        private static Color Opaque(Color c)
        {
            c.a = 1f;
            return c;
        }
    }
}
