using System;
using System.Collections;
using System.Collections.Generic;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Timer;
using UnityEngine;

namespace _SAIUN.Scripts.Crop
{
    /// <summary>작물 성장 단계 (사양서 v1.1 7-1). 순서가 곧 성장 순서다.</summary>
    public enum CropStage
    {
        None,
        Seed,
        Sprout,
        Growing,
        Fruiting,
        Harvestable,
        Dead,
    }

    /// <summary>
    /// 세션 진행에 맞춰 화단에 작물을 심고, 키우고, 치운다 (P2-03).
    /// 상태머신과 타이머를 구독만 하고 상태는 바꾸지 않는다.
    /// 세션마다 화단의 모든 칸에 한 포기씩 심고, 모든 포기가 같은 단계로 함께 자란다.
    /// </summary>
    public class CropGrowth : MonoBehaviour
    {
        // ---- 확정값 (사양서 8장 작물 성장 임계: 1세트 / 50% / 80% / 100%) ----
        public const int SproutSets = 1;
        public const float GrowingProgress = 0.5f;
        public const float FruitingProgress = 0.8f;

        // 포기마다 방향을 어긋나게 돌리는 각. 황금각이라 몇 포기를 심어도 겹치는 방향이 없다.
        private const float GoldenAngleDegrees = 137.50776f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("참조")]
        [SerializeField] private PomodoroStateMachine stateMachine;
        [SerializeField] private PomodoroTimer timer;
        [SerializeField] private Flowerbed flowerbed;
        [SerializeField] private CropCatalog catalog;

        [Header("성장 연출")]
        [Tooltip("단계가 바뀔 때 새 모델이 커지는 시간(초)")]
        [SerializeField, Min(0f)] private float growPopSeconds = 0.35f;

        [Tooltip("새 모델이 이 배율에서 1까지 커진다")]
        [SerializeField, Range(0f, 1f)] private float growPopStartScale = 0.6f;

        [Tooltip("발아할 때 포기마다 튀는 파티클 (사양서 7-1: 새싹 메시 + 파티클)")]
        [SerializeField] private ParticleSystem sproutBurst;

        [SerializeField, Min(0)] private int sproutBurstCount = 8;

        [Tooltip("수확 가능 상태에서 계속 피어오르는 빛 (사양서 7-1: 빛 VFX)")]
        [SerializeField] private ParticleSystem harvestGlow;

        [Tooltip("거둘 때 포기마다 튀는 파티클")]
        [SerializeField] private ParticleSystem harvestBurst;

        [SerializeField, Min(0)] private int harvestBurstCount = 14;

        [Header("흔들림")]
        [SerializeField, Min(0f)] private float swayDegrees = 2f;

        [Tooltip("유예 중 흔들림 (사양서 v1.1 6장: 작물 흔들림 강도 증가)")]
        [SerializeField, Min(0f)] private float interruptedSwayDegrees = 9f;

        [Tooltip("초당 흔들림 횟수")]
        [SerializeField, Min(0f)] private float swayFrequency = 0.5f;

        [SerializeField, Min(0f)] private float interruptedSwayFrequency = 2.2f;

        [Tooltip("열 하나를 건널 때 어긋나는 위상(라디안). 바람이 화단을 훑고 지나가는 것처럼 보인다.")]
        [SerializeField] private float swayWavePerColumn = 0.7f;

        [Tooltip("흔들림 세기가 목표로 따라붙는 빠르기")]
        [SerializeField, Min(0f)] private float swayResponse = 4f;

        [Header("사망")]
        [SerializeField, Min(0f)] private float witherSeconds = 2.4f;

        [SerializeField, Range(0f, 90f)] private float witherDroopDegrees = 55f;

        [SerializeField, Range(0f, 1f)] private float witherDesaturation = 0.85f;

        [SerializeField, Range(0f, 1f)] private float witherDarkening = 0.55f;

        [Tooltip("시드는 시간 중 이 지점부터 작아져 사라진다")]
        [SerializeField, Range(0.01f, 0.99f)] private float witherShrinkStart = 0.55f;

        [Tooltip("수확·취소로 치울 때 작아지는 시간(초)")]
        [SerializeField, Min(0f)] private float clearSeconds = 0.4f;

        /// <summary>현재 단계. 심은 작물이 없으면 None.</summary>
        public CropStage CurrentStage { get; private set; } = CropStage.None;

        /// <summary>지금 심겨 있는 작물. 없으면 null.</summary>
        public CropDefinition CurrentCrop { get; private set; }

        /// <summary>화단에 있는 포기 수. 심겨 있으면 화단 칸 수와 같다.</summary>
        public int PlantCount => _plants.Count;

        /// <summary>단계가 바뀔 때마다 1회 발행. 치워져 None이 될 때도 발행한다.</summary>
        public event Action<CropStage> OnStageChanged;

        private sealed class Plant
        {
            public Transform Root;
            public GameObject Model;
            public Quaternion BaseRotation;
            public Vector3 DroopAxis;
            public float Phase;
        }

        private readonly List<Plant> _plants = new List<Plant>();
        private Transform _container;
        private Coroutine _pop;
        private Coroutine _removal;
        private bool _removing;   // 코루틴이 첫 프레임 전에 끝나도 올바르게 판단하려고 따로 둔다
        private float _swayAmplitude;
        private float _swayFrequency;
        private float _swayAngle;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (flowerbed == null) flowerbed = GetComponent<Flowerbed>();
            if (stateMachine == null) stateMachine = FindFirstObjectByType<PomodoroStateMachine>();
            if (timer == null) timer = FindFirstObjectByType<PomodoroTimer>();

            if (stateMachine == null || timer == null || flowerbed == null || catalog == null)
            {
                Debug.LogError("CropGrowth: 상태머신·타이머·화단·작물 목록 참조가 모두 필요합니다.");
            }

            _swayAmplitude = swayDegrees;
            _swayFrequency = swayFrequency;
        }

        private void OnEnable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged += HandleStateChanged;
            if (timer != null) timer.OnTick += HandleTick;
        }

        private void OnDisable()
        {
            if (stateMachine != null) stateMachine.OnStateChanged -= HandleStateChanged;
            if (timer != null) timer.OnTick -= HandleTick;
        }

        private void Update()
        {
            if (_plants.Count == 0 || _removing) return;
            Sway(Time.deltaTime);
        }

        // ---- 판정 ----

        /// <summary>
        /// 사양서 8장 성장 임계. 완료 세트와 진행 중인 집중 구간의 진행률로 전체 진행률을 구한다.
        /// 세트 단위로만 세면 4세트 세션에서 80%(3.2세트)에 닿지 못해 결실 단계가 빠지기 때문이다.
        /// </summary>
        public static CropStage StageFor(int completedSets, float focusProgress, int totalSets)
        {
            if (totalSets <= 0) return CropStage.Seed;
            if (completedSets >= totalSets) return CropStage.Harvestable;

            float overall = (Mathf.Max(0, completedSets) + Mathf.Clamp01(focusProgress)) / totalSets;
            if (overall >= FruitingProgress) return CropStage.Fruiting;
            if (overall >= GrowingProgress) return CropStage.Growing;
            if (completedSets >= SproutSets) return CropStage.Sprout;
            return CropStage.Seed;
        }

        /// <summary>포기의 현재 모델. 테스트와 연출 확인용.</summary>
        public GameObject GetPlantModel(int index)
        {
            return index >= 0 && index < _plants.Count ? _plants[index].Model : null;
        }

        /// <summary>포기의 기준점(화단 위 위치).</summary>
        public Transform GetPlantRoot(int index)
        {
            return index >= 0 && index < _plants.Count ? _plants[index].Root : null;
        }

        // ---- 이벤트 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            switch (to)
            {
                case PomodoroState.Focus:
                    if (from == PomodoroState.Idle) PlantSession();
                    else Evaluate();
                    break;

                case PomodoroState.ShortBreak:
                case PomodoroState.LongBreak:
                    Evaluate();
                    break;

                case PomodoroState.Failed:
                    Wither();
                    break;

                case PomodoroState.Idle:
                    // Failed를 거쳐 왔다면 이미 시들고 있다. 그 외에는 수확(장기 휴식 종료)이거나 취소다.
                    if (from == PomodoroState.Failed)
                    {
                        if (!_removing) ResetToNone();
                    }
                    else
                    {
                        Clear(harvested: from == PomodoroState.LongBreak);
                    }
                    break;
            }
        }

        private void HandleTick()
        {
            if (stateMachine != null && stateMachine.CurrentState == PomodoroState.Focus) Evaluate();
        }

        // ---- 심기·성장 ----

        private void PlantSession()
        {
            RemoveImmediately();

            SessionConfig config = timer != null ? timer.Config : null;
            if (config == null || flowerbed == null)
            {
                Debug.LogWarning("CropGrowth: 세션 설정이나 화단이 없어 작물을 심지 않습니다.");
                return;
            }

            CurrentCrop = ResolveCrop(config.CropType);
            if (CurrentCrop == null) return;

            // 세트 수와 무관하게 칸마다 한 포기씩 심는다. 기본 4세트에서도 화단이 비어 보이지 않게 한다.
            for (int i = 0; i < flowerbed.CellCount; i++)
            {
                int column = i % flowerbed.Columns;
                int row = i / flowerbed.Columns;

                var root = new GameObject($"Plant_{row}_{column}").transform;
                root.SetParent(Container, false);
                Quaternion rotation = Quaternion.Euler(0f, i * GoldenAngleDegrees, 0f);
                root.SetPositionAndRotation(flowerbed.CellPosition(column, row), rotation);

                _plants.Add(new Plant
                {
                    Root = root,
                    BaseRotation = root.localRotation,
                    // 시들 때 포기마다 다른 쪽으로 쓰러진다.
                    DroopAxis = rotation * Vector3.right,
                    Phase = column * swayWavePerColumn,
                });
            }

            SetStage(CropStage.Seed);
        }

        private CropDefinition ResolveCrop(string cropType)
        {
            if (catalog == null) return null;

            CropDefinition crop = catalog.Find(cropType);
            if (crop != null) return crop;

            CropDefinition fallback = catalog.Crops.Count > 0 ? catalog.Crops[0] : null;
            Debug.LogWarning($"CropGrowth: 작물 '{cropType}'이(가) 목록에 없어 " +
                             $"{(fallback != null ? fallback.Id : "아무것도")}을(를) 심습니다.");
            return fallback;
        }

        private void Evaluate()
        {
            if (_plants.Count == 0 || CurrentStage == CropStage.Dead || timer == null || timer.Config == null) return;

            PomodoroState state = stateMachine.CurrentState;
            float focusProgress = state == PomodoroState.Focus || state == PomodoroState.Interrupted ? timer.Progress : 0f;
            CropStage next = StageFor(timer.CompletedSets, focusProgress, timer.Config.TotalSets);

            // 되돌아가지 않는다. 한 번에 여러 단계를 건너뛰면 마지막 단계만 보여 준다.
            if (next > CurrentStage) SetStage(next);
        }

        private void SetStage(CropStage stage)
        {
            CurrentStage = stage;

            GameObject prefab = CurrentCrop != null ? CurrentCrop.GetStagePrefab(stage) : null;
            foreach (Plant plant in _plants)
            {
                if (plant.Model != null) Destroy(plant.Model);
                plant.Model = prefab != null ? Instantiate(prefab, plant.Root, false) : null;
            }

            if (_pop != null) StopCoroutine(_pop);
            _pop = growPopSeconds > 0f ? StartCoroutine(Pop()) : null;

            if (stage == CropStage.Sprout) EmitAtPlants(sproutBurst, sproutBurstCount);
            SetHarvestGlow(stage == CropStage.Harvestable);

            OnStageChanged?.Invoke(stage);
        }

        private IEnumerator Pop()
        {
            for (float elapsed = 0f; elapsed < growPopSeconds; elapsed += Time.deltaTime)
            {
                // 끝에서 살짝 느려지는 곡선
                float t = 1f - Mathf.Pow(1f - elapsed / growPopSeconds, 3f);
                SetModelScale(Mathf.LerpUnclamped(growPopStartScale, 1f, t));
                yield return null;
            }
            SetModelScale(1f);
            _pop = null;
        }

        private void SetModelScale(float scale)
        {
            foreach (Plant plant in _plants)
            {
                if (plant.Model != null) plant.Model.transform.localScale = Vector3.one * scale;
            }
        }

        private void Sway(float deltaTime)
        {
            bool alarmed = stateMachine != null && stateMachine.CurrentState == PomodoroState.Interrupted;
            float blend = 1f - Mathf.Exp(-swayResponse * deltaTime);
            _swayAmplitude = Mathf.Lerp(_swayAmplitude, alarmed ? interruptedSwayDegrees : swayDegrees, blend);
            _swayFrequency = Mathf.Lerp(_swayFrequency, alarmed ? interruptedSwayFrequency : swayFrequency, blend);

            // 주파수가 바뀌어도 튀지 않도록 각을 누적한다.
            _swayAngle = Mathf.Repeat(_swayAngle + deltaTime * _swayFrequency * Mathf.PI * 2f, Mathf.PI * 2f);

            foreach (Plant plant in _plants)
            {
                float degrees = Mathf.Sin(_swayAngle + plant.Phase) * _swayAmplitude;
                plant.Root.localRotation = Quaternion.AngleAxis(degrees, Vector3.forward) * plant.BaseRotation;
            }
        }

        // ---- 치우기 ----

        private void Wither()
        {
            if (_plants.Count == 0) return;

            CurrentStage = CropStage.Dead;
            SetHarvestGlow(false);
            OnStageChanged?.Invoke(CropStage.Dead);

            StopRemoval();
            _removing = true;
            _removal = StartCoroutine(WitherRoutine());
        }

        private IEnumerator WitherRoutine()
        {
            var renderers = new List<Renderer>();
            var originals = new List<Color>();
            foreach (Plant plant in _plants)
            {
                if (plant.Model == null) continue;
                foreach (Renderer renderer in plant.Model.GetComponentsInChildren<Renderer>())
                {
                    Material material = renderer.sharedMaterial;
                    if (material == null || !material.HasProperty(BaseColorId)) continue;
                    renderers.Add(renderer);
                    originals.Add(material.GetColor(BaseColorId));
                }
            }

            var block = new MaterialPropertyBlock();
            for (float elapsed = 0f; elapsed < witherSeconds; elapsed += Time.deltaTime)
            {
                float t = elapsed / witherSeconds;
                float wilt = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / witherShrinkStart));
                float shrink = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - witherShrinkStart) / (1f - witherShrinkStart)));

                foreach (Plant plant in _plants)
                {
                    plant.Root.localRotation = Quaternion.AngleAxis(wilt * witherDroopDegrees, plant.DroopAxis) * plant.BaseRotation;
                    plant.Root.localScale = Vector3.one * shrink;
                }

                for (int i = 0; i < renderers.Count; i++)
                {
                    if (renderers[i] == null) continue;
                    block.SetColor(BaseColorId, WitheredColor(originals[i], wilt));
                    renderers[i].SetPropertyBlock(block);
                }

                yield return null;
            }

            DestroyPlants();
            _removing = false;

            // 확인을 눌러 이미 Idle로 돌아왔다면 여기서 비운다.
            if (stateMachine != null && stateMachine.CurrentState == PomodoroState.Idle) ResetToNone();
        }

        private Color WitheredColor(Color original, float amount)
        {
            float gray = original.grayscale;
            Color dull = Color.Lerp(original, new Color(gray, gray, gray), witherDesaturation) * (1f - witherDarkening);
            Color result = Color.Lerp(original, dull, amount);
            result.a = original.a;
            return result;
        }

        private void Clear(bool harvested)
        {
            if (_plants.Count == 0)
            {
                ResetToNone();
                return;
            }

            SetHarvestGlow(false);
            if (harvested) EmitAtPlants(harvestBurst, harvestBurstCount);

            StopRemoval();
            _removing = true;
            _removal = StartCoroutine(ClearRoutine());
        }

        private IEnumerator ClearRoutine()
        {
            for (float elapsed = 0f; elapsed < clearSeconds; elapsed += Time.deltaTime)
            {
                float scale = 1f - Mathf.SmoothStep(0f, 1f, elapsed / clearSeconds);
                foreach (Plant plant in _plants) plant.Root.localScale = Vector3.one * scale;
                yield return null;
            }

            DestroyPlants();
            _removing = false;
            ResetToNone();
        }

        private void RemoveImmediately()
        {
            StopRemoval();
            if (_pop != null)
            {
                StopCoroutine(_pop);
                _pop = null;
            }
            SetHarvestGlow(false);
            DestroyPlants();
            CurrentStage = CropStage.None;
            CurrentCrop = null;
        }

        private void StopRemoval()
        {
            if (_removal != null) StopCoroutine(_removal);
            _removal = null;
            _removing = false;
        }

        private void DestroyPlants()
        {
            foreach (Plant plant in _plants)
            {
                if (plant.Root != null) Destroy(plant.Root.gameObject);
            }
            _plants.Clear();
        }

        private void ResetToNone()
        {
            if (CurrentStage == CropStage.None) return;
            CurrentStage = CropStage.None;
            CurrentCrop = null;
            OnStageChanged?.Invoke(CropStage.None);
        }

        // ---- 파티클 ----

        private Transform Container
        {
            get
            {
                if (_container != null) return _container;
                _container = new GameObject("Plants").transform;
                _container.SetParent(transform, false);
                return _container;
            }
        }

        private void EmitAtPlants(ParticleSystem system, int count)
        {
            if (system == null || count <= 0) return;

            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            foreach (Plant plant in _plants)
            {
                emit.position = plant.Root.position;
                system.Emit(emit, count);
            }
        }

        private void SetHarvestGlow(bool on)
        {
            if (harvestGlow == null) return;

            if (!on)
            {
                harvestGlow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            // 칸 간격을 바꿔도 빛이 화단을 덮도록 켤 때마다 맞춘다.
            if (flowerbed != null)
            {
                harvestGlow.transform.position = flowerbed.GridToWorld((flowerbed.Columns - 1) / 2f, (flowerbed.Rows - 1) / 2f);
                ParticleSystem.ShapeModule shape = harvestGlow.shape;
                Vector2 grid = flowerbed.GridSize;
                shape.scale = new Vector3(grid.x, shape.scale.y, grid.y);
            }
            harvestGlow.Play(true);
        }
    }
}
