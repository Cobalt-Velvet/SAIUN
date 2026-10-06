using System;
using System.Collections;
using System.Collections.Generic;
using _SAIUN.Scripts.Core;
using _SAIUN.Scripts.Timer;
using _SAIUN.Scripts.Weather;
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
    /// 화단의 작물을 심고, 키우고, 거둔다.
    ///  - 쉬는 동안(Idle): 나무 상자의 칸마다 텃밭 작물이 실제 시각을 따라 저절로 자란다. 한 포기가 싹터 거둘 때까지
    ///    gardenCycleMinutes가 걸리고, 거두면 그 칸에 무작위 작물이 새로 심긴다. 칸마다 주기가 어긋나 늘 여러 단계가 섞여 있다.
    ///    모습은 시각만으로 정해지므로 앱을 껐다 켜도 꺼져 있던 동안 자란 모습 그대로다(따로 저장하지 않는다).
    ///  - 집중할 때: 상자가 가라앉고 그 자리에 토분이 솟아 집중 작물 한 그루를 새로 심는다. 세션 진행만큼 자라고,
    ///    모든 세트를 마치면 열매가 익어 거두고, 실패하면 시든다. 세션이 끝나면 토분이 가라앉고 상자가 돌아온다.
    /// 상태머신과 타이머를 구독만 하고 상태는 바꾸지 않는다.
    /// </summary>
    public class CropGrowth : MonoBehaviour
    {
        // ---- 확정값 (사양서 8장 작물 성장 임계: 1세트 / 50% / 80% / 100%) ----
        public const int SproutSets = 1;
        public const float GrowingProgress = 0.5f;
        public const float FruitingProgress = 0.8f;

        // ---- 텃밭 주기 안에서 단계가 바뀌는 자리(주기 비율). 열매 맺고 익은 모습을 오래 본다. ----
        public const float GardenSproutAt = 0.1f;
        public const float GardenGrowingAt = 0.3f;
        public const float GardenFruitingAt = 0.55f;
        public const float GardenHarvestableAt = 0.8f;

        // 포기마다 방향을 어긋나게 돌리는 각. 황금각이라 몇 포기를 심어도 겹치는 방향이 없다.
        private const float GoldenAngleDegrees = 137.50776f;

        // 칸마다 주기를 어긋나게 하는 비율. 황금비라 칸 수와 무관하게 주기 안에 고르게 흩어진다.
        private const double GoldenRatioFraction = 0.6180339887;

        private const double SecondsPerMinute = 60.0;

        // 텃밭 시각을 재는 기준점. 어느 날이든 상관없고 바뀌지만 않으면 된다.
        private static readonly DateTime GardenEpoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("참조")]
        [SerializeField] private PomodoroStateMachine stateMachine;
        [SerializeField] private PomodoroTimer timer;
        [SerializeField] private Flowerbed flowerbed;
        [SerializeField] private CropCatalog catalog;

        [Tooltip("바람. 없으면 바람 없이 제자리에서 흔들린다.")]
        [SerializeField] private WeatherController weather;

        [Header("텃밭 (쉬는 동안)")]
        [Tooltip("텃밭 작물 한 포기가 싹터 거둘 때까지 걸리는 시간(분)")]
        [SerializeField, Min(1f)] private float gardenCycleMinutes = 60f;

        [Tooltip("텃밭 한 포기를 거둘 때 튀는 알갱이 수(집중 작물보다 조용하게)")]
        [SerializeField, Min(0)] private int gardenHarvestBurstCount = 4;

        [Header("상자 ↔ 토분")]
        [Tooltip("상자나 토분이 가라앉거나 솟는 시간(초, 한쪽)")]
        [SerializeField, Min(0f)] private float swapSeconds = 0.35f;

        [Header("성장 연출")]
        [Tooltip("단계가 바뀔 때 새 모델이 커지는 시간(초)")]
        [SerializeField, Min(0f)] private float growPopSeconds = 0.35f;

        [Tooltip("새 모델이 이 배율에서 1까지 커진다")]
        [SerializeField, Range(0f, 1f)] private float growPopStartScale = 0.6f;

        [Tooltip("집중 작물이 발아할 때 튀는 파티클 (사양서 7-1: 새싹 메시 + 파티클)")]
        [SerializeField] private ParticleSystem sproutBurst;

        [SerializeField, Min(0)] private int sproutBurstCount = 8;

        [Tooltip("집중 작물이 익었을 때 계속 피어오르는 빛 (사양서 7-1: 빛 VFX)")]
        [SerializeField] private ParticleSystem harvestGlow;

        [Tooltip("거둘 때 튀는 파티클")]
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

        [Tooltip("바람 세기 1당 더해지는 흔들림 폭(도)")]
        [SerializeField, Min(0f)] private float swayPerWind = 1.2f;

        [Tooltip("바람 세기 1당 바람 쪽으로 눕는 각(도)")]
        [SerializeField, Min(0f)] private float leanPerWind = 2.5f;

        [Tooltip("나무는 풀보다 덜 흔들린다(집중 작물의 흔들림 배율)")]
        [SerializeField, Range(0f, 1f)] private float focusSwayScale = 0.45f;

        [Header("사망")]
        [SerializeField, Min(0f)] private float witherSeconds = 2.4f;

        [SerializeField, Range(0f, 90f)] private float witherDroopDegrees = 55f;

        [SerializeField, Range(0f, 1f)] private float witherDesaturation = 0.85f;

        [SerializeField, Range(0f, 1f)] private float witherDarkening = 0.55f;

        [Tooltip("시드는 시간 중 이 지점부터 작아져 사라진다")]
        [SerializeField, Range(0.01f, 0.99f)] private float witherShrinkStart = 0.55f;

        [Tooltip("수확·취소로 치울 때 작아지는 시간(초)")]
        [SerializeField, Min(0f)] private float clearSeconds = 0.4f;

        /// <summary>집중 작물의 현재 단계. 심은 작물이 없으면 None.</summary>
        public CropStage CurrentStage { get; private set; } = CropStage.None;

        /// <summary>지금 토분에 심겨 있는 작물. 없으면 null.</summary>
        public CropDefinition CurrentCrop { get; private set; }

        /// <summary>토분이 나와 있는지(바꾸는 중이면 바뀔 쪽). 아니면 상자가 나와 있다.</summary>
        public bool PotShown { get; private set; }

        /// <summary>상자 텃밭의 포기 수(칸 수와 같다).</summary>
        public int GardenCount => _garden.Count;

        /// <summary>집중 작물의 단계가 바뀔 때마다 1회 발행. 치워져 None이 될 때도 발행한다.</summary>
        public event Action<CropStage> OnStageChanged;

        /// <summary>텃밭이 따르는 시계. 테스트가 시각을 바꿀 때 쓴다.</summary>
        internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        private sealed class Plant
        {
            public Transform Root;
            public GameObject Model;
            public Quaternion BaseRotation;
            public Vector3 DroopAxis;
            public float Phase;
            public CropDefinition Crop;
            public CropStage Stage;
            public long Cycle = long.MinValue;
            public float PopStart = -1f;
        }

        private readonly List<Plant> _garden = new List<Plant>();
        private Plant _focus;
        private Transform _gardenContainer;
        private Transform _focusContainer;
        private Coroutine _removal;
        private Coroutine _swap;
        private bool _removing;   // 코루틴이 첫 프레임 전에 끝나도 올바르게 판단하려고 따로 둔다
        private bool _returnAfterRemoval;
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

        private void Start()
        {
            // 처음에는 상자가 나와 있고 텃밭은 지금 시각의 모습이다. 이미 심었으면(다시 불려도) 그대로 둔다.
            if (_garden.Count > 0) return;
            PlantGarden();
            ShowImmediately(pot: false);
        }

        private void Update()
        {
            if (!PotShown && _swap == null) UpdateGarden(effects: true);
            Sway(Time.deltaTime);
            foreach (Plant plant in _garden) UpdatePop(plant);
            if (_focus != null && !_removing) UpdatePop(_focus);
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

        /// <summary>텃밭 주기 안의 자리(0~1)에 맞는 단계.</summary>
        public static CropStage GardenStageFor(float cyclePhase)
        {
            if (cyclePhase >= GardenHarvestableAt) return CropStage.Harvestable;
            if (cyclePhase >= GardenFruitingAt) return CropStage.Fruiting;
            if (cyclePhase >= GardenGrowingAt) return CropStage.Growing;
            if (cyclePhase >= GardenSproutAt) return CropStage.Sprout;
            return CropStage.Seed;
        }

        /// <summary>텃밭 칸의 작물. 테스트와 연출 확인용.</summary>
        public CropDefinition GetGardenCrop(int index) => index >= 0 && index < _garden.Count ? _garden[index].Crop : null;

        /// <summary>텃밭 칸의 단계.</summary>
        public CropStage GetGardenStage(int index) => index >= 0 && index < _garden.Count ? _garden[index].Stage : CropStage.None;

        /// <summary>텃밭 칸의 현재 모델.</summary>
        public GameObject GetGardenModel(int index) => index >= 0 && index < _garden.Count ? _garden[index].Model : null;

        /// <summary>집중 작물의 현재 모델. 없으면 null.</summary>
        public GameObject FocusModel => _focus != null ? _focus.Model : null;

        /// <summary>집중 작물의 기준점(토분 흙 위). 없으면 null.</summary>
        public Transform FocusRoot => _focus != null ? _focus.Root : null;

        // ---- 이벤트 ----

        private void HandleStateChanged(PomodoroState from, PomodoroState to)
        {
            switch (to)
            {
                case PomodoroState.Focus:
                    if (from == PomodoroState.Idle) StartFocus();
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
                        if (_removing) _returnAfterRemoval = true;
                        else ReturnToGarden();
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

        // ---- 텃밭 ----

        // 칸마다 한 포기씩 자리를 만들고 지금 시각의 모습으로 채운다(연출 없이).
        private void PlantGarden()
        {
            foreach (Plant plant in _garden)
            {
                if (plant.Root != null) Destroy(plant.Root.gameObject);
            }
            _garden.Clear();
            if (flowerbed == null) return;

            for (int i = 0; i < flowerbed.CellCount; i++)
            {
                int column = i % flowerbed.Columns;
                int row = i / flowerbed.Columns;

                Quaternion rotation = Quaternion.Euler(0f, i * GoldenAngleDegrees, 0f);
                Transform root = NewPlantRoot($"Plant_{row}_{column}", GardenContainer, flowerbed.CellPosition(column, row), rotation);
                _garden.Add(new Plant
                {
                    Root = root,
                    BaseRotation = root.localRotation,
                    DroopAxis = rotation * Vector3.right,
                    Phase = column * swayWavePerColumn,
                });
            }
            UpdateGarden(effects: false);
        }

        // 칸마다 지금 시각의 주기·단계를 셈해, 바뀐 칸만 모델을 바꾼다. 주기가 넘어가면 거두고 새 작물을 심는다.
        private void UpdateGarden(bool effects)
        {
            if (catalog == null || catalog.Crops.Count == 0) return;

            double cycleSeconds = gardenCycleMinutes * SecondsPerMinute;
            double now = (Clock() - GardenEpoch).TotalSeconds / cycleSeconds;
            for (int i = 0; i < _garden.Count; i++)
            {
                Plant plant = _garden[i];
                double position = now + i * GoldenRatioFraction;
                long cycle = (long)Math.Floor(position);
                CropStage stage = GardenStageFor((float)(position - cycle));

                if (cycle != plant.Cycle)
                {
                    if (effects && plant.Stage == CropStage.Harvestable) Emit(harvestBurst, plant.Root.position, gardenHarvestBurstCount);
                    plant.Cycle = cycle;
                    plant.Crop = GardenCrop(i, cycle);
                    plant.Stage = CropStage.None;
                }
                if (stage == plant.Stage) continue;

                plant.Stage = stage;
                SetModel(plant, plant.Crop != null ? plant.Crop.GetStagePrefab(stage) : null, effects);
            }
        }

        // 칸과 주기로 정해지는 무작위 작물. 같은 시각이면 언제 다시 켜도 같은 작물이다.
        private CropDefinition GardenCrop(int cell, long cycle)
        {
            unchecked
            {
                uint hash = (uint)cell * 0x9E3779B1u ^ (uint)cycle * 0x85EBCA77u ^ (uint)(cycle >> 32) * 0xC2B2AE3Du;
                hash ^= hash >> 15;
                hash *= 0x2C1B3C6Du;
                hash ^= hash >> 12;
                return catalog.Crops[(int)(hash % (uint)catalog.Crops.Count)];
            }
        }

        // ---- 집중 작물 ----

        private void StartFocus()
        {
            StopRemoval();
            DestroyFocus();
            CurrentStage = CropStage.None;
            CurrentCrop = catalog != null ? catalog.FocusCrop : null;

            if (CurrentCrop == null || flowerbed == null)
            {
                Debug.LogWarning("CropGrowth: 집중 작물이나 화단이 없어 토분에 심지 않습니다.");
                return;
            }

            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            Transform root = NewPlantRoot("FocusPlant", FocusContainer, flowerbed.PotPlantPosition, rotation);
            _focus = new Plant
            {
                Root = root,
                BaseRotation = root.localRotation,
                DroopAxis = rotation * Vector3.right,
                Crop = CurrentCrop,
            };

            SetFocusStage(CropStage.Seed);
            SwapTo(pot: true);
        }

        private void Evaluate()
        {
            if (_focus == null || CurrentStage == CropStage.Dead || timer == null || timer.Config == null) return;

            PomodoroState state = stateMachine.CurrentState;
            float focusProgress = state == PomodoroState.Focus || state == PomodoroState.Interrupted ? timer.Progress : 0f;
            CropStage next = StageFor(timer.CompletedSets, focusProgress, timer.Config.TotalSets);

            // 되돌아가지 않는다. 한 번에 여러 단계를 건너뛰면 마지막 단계만 보여 준다.
            if (next > CurrentStage) SetFocusStage(next);
        }

        private void SetFocusStage(CropStage stage)
        {
            CurrentStage = stage;
            _focus.Stage = stage;
            SetModel(_focus, CurrentCrop.GetStagePrefab(stage), pop: true);

            if (stage == CropStage.Sprout) Emit(sproutBurst, _focus.Root.position, sproutBurstCount);
            SetHarvestGlow(stage == CropStage.Harvestable);

            OnStageChanged?.Invoke(stage);
        }

        // ---- 모델 ----

        private void SetModel(Plant plant, GameObject prefab, bool pop)
        {
            if (plant.Model != null) Destroy(plant.Model);
            plant.Model = prefab != null ? Instantiate(prefab, plant.Root, false) : null;
            plant.PopStart = pop && growPopSeconds > 0f && plant.Model != null ? Time.time : -1f;
            if (plant.PopStart >= 0f) plant.Model.transform.localScale = Vector3.one * growPopStartScale;
        }

        private void UpdatePop(Plant plant)
        {
            if (plant.PopStart < 0f || plant.Model == null) return;

            float elapsed = Time.time - plant.PopStart;
            if (elapsed >= growPopSeconds)
            {
                plant.Model.transform.localScale = Vector3.one;
                plant.PopStart = -1f;
                return;
            }
            // 끝에서 살짝 느려지는 곡선
            float t = 1f - Mathf.Pow(1f - elapsed / growPopSeconds, 3f);
            plant.Model.transform.localScale = Vector3.one * Mathf.LerpUnclamped(growPopStartScale, 1f, t);
        }

        private void Sway(float deltaTime)
        {
            bool alarmed = stateMachine != null && stateMachine.CurrentState == PomodoroState.Interrupted;
            float wind = weather != null ? weather.WindStrength : 0f;
            float blend = 1f - Mathf.Exp(-swayResponse * deltaTime);
            float amplitude = (alarmed ? interruptedSwayDegrees : swayDegrees) + wind * swayPerWind;
            _swayAmplitude = Mathf.Lerp(_swayAmplitude, amplitude, blend);
            _swayFrequency = Mathf.Lerp(_swayFrequency, alarmed ? interruptedSwayFrequency : swayFrequency, blend);

            // 주파수가 바뀌어도 튀지 않도록 각을 누적한다.
            _swayAngle = Mathf.Repeat(_swayAngle + deltaTime * _swayFrequency * Mathf.PI * 2f, Mathf.PI * 2f);

            // 바람이 불어 가는 쪽으로 눕고, 그 둘레로 흔들린다. 이 축으로 양의 각을 주면 윗부분이 바람 쪽으로 기운다.
            Vector3 axis = weather != null ? Vector3.Cross(Vector3.up, weather.WindDirection) : Vector3.forward;
            float lean = wind * leanPerWind;
            foreach (Plant plant in _garden) SwayPlant(plant, axis, lean, 1f);
            if (_focus != null && !_removing) SwayPlant(_focus, axis, lean, focusSwayScale);
        }

        private void SwayPlant(Plant plant, Vector3 axis, float lean, float scale)
        {
            float degrees = (lean + Mathf.Sin(_swayAngle + plant.Phase) * _swayAmplitude) * scale;
            plant.Root.localRotation = Quaternion.AngleAxis(degrees, axis) * plant.BaseRotation;
        }

        // ---- 치우기 ----

        private void Wither()
        {
            if (_focus == null) return;

            CurrentStage = CropStage.Dead;
            SetHarvestGlow(false);
            OnStageChanged?.Invoke(CropStage.Dead);

            StopRemoval();
            _removing = true;
            _removal = StartCoroutine(WitherRoutine());
        }

        private IEnumerator WitherRoutine()
        {
            // 한 모델이 재질을 여럿(잎·줄기·열매) 가질 수 있어 재질 칸마다 원래 색을 기억해 따로 시든다.
            var renderers = new List<Renderer>();
            var slots = new List<int>();
            var originals = new List<Color>();
            if (_focus.Model != null)
            {
                foreach (Renderer renderer in _focus.Model.GetComponentsInChildren<Renderer>())
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int slot = 0; slot < materials.Length; slot++)
                    {
                        if (materials[slot] == null || !materials[slot].HasProperty(BaseColorId)) continue;
                        renderers.Add(renderer);
                        slots.Add(slot);
                        originals.Add(materials[slot].GetColor(BaseColorId));
                    }
                }
            }

            var block = new MaterialPropertyBlock();
            for (float elapsed = 0f; elapsed < witherSeconds; elapsed += Time.deltaTime)
            {
                float t = elapsed / witherSeconds;
                float wilt = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / witherShrinkStart));
                float shrink = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - witherShrinkStart) / (1f - witherShrinkStart)));

                _focus.Root.localRotation = Quaternion.AngleAxis(wilt * witherDroopDegrees, _focus.DroopAxis) * _focus.BaseRotation;
                _focus.Root.localScale = Vector3.one * shrink;

                for (int i = 0; i < renderers.Count; i++)
                {
                    if (renderers[i] == null) continue;
                    block.SetColor(BaseColorId, WitheredColor(originals[i], wilt));
                    renderers[i].SetPropertyBlock(block, slots[i]);
                }

                yield return null;
            }

            FinishRemoval();
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
            SetHarvestGlow(false);
            if (_focus == null)
            {
                ReturnToGarden();
                return;
            }

            if (harvested) Emit(harvestBurst, _focus.Root.position, harvestBurstCount);

            StopRemoval();
            _removing = true;
            _returnAfterRemoval = true;
            _removal = StartCoroutine(ClearRoutine());
        }

        private IEnumerator ClearRoutine()
        {
            for (float elapsed = 0f; elapsed < clearSeconds; elapsed += Time.deltaTime)
            {
                _focus.Root.localScale = Vector3.one * (1f - Mathf.SmoothStep(0f, 1f, elapsed / clearSeconds));
                yield return null;
            }
            FinishRemoval();
        }

        // 집중 작물을 치운 뒤, Idle로 돌아왔으면 상자로 돌아간다.
        private void FinishRemoval()
        {
            DestroyFocus();
            _removing = false;
            _removal = null;
            bool idle = stateMachine != null && stateMachine.CurrentState == PomodoroState.Idle;
            if (_returnAfterRemoval || idle) ReturnToGarden();
        }

        private void ReturnToGarden()
        {
            _returnAfterRemoval = false;
            ResetToNone();
            if (PotShown) SwapTo(pot: false);
        }

        private void StopRemoval()
        {
            if (_removal != null) StopCoroutine(_removal);
            _removal = null;
            _removing = false;
            _returnAfterRemoval = false;
        }

        private void DestroyFocus()
        {
            SetHarvestGlow(false);
            if (_focus != null && _focus.Root != null) Destroy(_focus.Root.gameObject);
            _focus = null;
        }

        private void ResetToNone()
        {
            if (CurrentStage == CropStage.None) return;
            CurrentStage = CropStage.None;
            CurrentCrop = null;
            OnStageChanged?.Invoke(CropStage.None);
        }

        // ---- 상자 ↔ 토분 ----

        private void SwapTo(bool pot)
        {
            if (_swap != null) StopCoroutine(_swap);
            _swap = null;
            ShowImmediately(PotShown);   // 바꾸던 중이면 지금 나와 있어야 할 쪽으로 맞추고 시작한다
            PotShown = pot;
            if (swapSeconds <= 0f || !isActiveAndEnabled)
            {
                if (!pot) UpdateGarden(effects: false);
                ShowImmediately(pot);
                return;
            }
            _swap = StartCoroutine(SwapRoutine(pot));
        }

        // 나와 있던 쪽이 가라앉고, 다른 쪽이 솟는다. 둘 다 데크 위 가운데를 축으로 줄었다 커진다.
        private IEnumerator SwapRoutine(bool pot)
        {
            Transform[] outgoing = pot ? BoxParts : PotParts;
            Transform[] incoming = pot ? PotParts : BoxParts;

            for (float elapsed = 0f; elapsed < swapSeconds; elapsed += Time.deltaTime)
            {
                float t = elapsed / swapSeconds;
                SetScale(outgoing, 1f - t * t);
                yield return null;
            }
            SetVisible(outgoing, false);

            // 상자가 돌아올 때 텃밭은 그동안 흐른 시각의 모습이다.
            if (!pot) UpdateGarden(effects: false);
            SetVisible(incoming, true);
            for (float elapsed = 0f; elapsed < swapSeconds; elapsed += Time.deltaTime)
            {
                float t = 1f - elapsed / swapSeconds;
                SetScale(incoming, 1f - t * t);
                yield return null;
            }
            SetScale(incoming, 1f);
            _swap = null;
        }

        private void ShowImmediately(bool pot)
        {
            SetVisible(BoxParts, !pot);
            SetVisible(PotParts, pot);
            SetScale(BoxParts, 1f);
            SetScale(PotParts, 1f);
        }

        private Transform[] BoxParts => flowerbed != null
            ? new[] { flowerbed.Planter, flowerbed.Soil, GardenContainer }
            : new[] { GardenContainer };

        private Transform[] PotParts => flowerbed != null
            ? new[] { flowerbed.Pot, flowerbed.PotSoil, FocusContainer }
            : new[] { FocusContainer };

        private static void SetVisible(Transform[] parts, bool visible)
        {
            foreach (Transform part in parts)
            {
                if (part != null) part.gameObject.SetActive(visible);
            }
        }

        private static void SetScale(Transform[] parts, float scale)
        {
            foreach (Transform part in parts)
            {
                if (part != null) part.localScale = Vector3.one * Mathf.Max(0f, scale);
            }
        }

        // ---- 그릇·파티클 ----

        // 작물 그릇은 화단 원점(데크 위 가운데)에 두어, 상자·토분과 같은 축으로 줄었다 커진다.
        private Transform GardenContainer => _gardenContainer != null ? _gardenContainer : _gardenContainer = NewContainer("Garden");

        private Transform FocusContainer => _focusContainer != null ? _focusContainer : _focusContainer = NewContainer("FocusPot");

        private Transform NewContainer(string containerName)
        {
            var container = new GameObject(containerName).transform;
            container.SetParent(transform, false);
            return container;
        }

        // 그릇이 줄어든 채여도 제자리에 서도록 화단 기준 로컬 좌표로 놓는다(그릇은 화단 원점에 회전 없이 있다).
        private Transform NewPlantRoot(string rootName, Transform container, Vector3 worldPosition, Quaternion rotation)
        {
            var root = new GameObject(rootName).transform;
            root.SetParent(container, false);
            root.localPosition = transform.InverseTransformPoint(worldPosition);
            root.localRotation = rotation;
            return root;
        }

        private static void Emit(ParticleSystem system, Vector3 position, int count)
        {
            if (system == null || count <= 0) return;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true, position = position };
            system.Emit(emit, count);
        }

        private void SetHarvestGlow(bool on)
        {
            if (harvestGlow == null) return;

            if (!on || _focus == null)
            {
                harvestGlow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            // 빛이 익은 나무의 수관을 감싸도록 켤 때마다 모델 크기에 맞춘다.
            Renderer tree = _focus.Model != null ? _focus.Model.GetComponentInChildren<Renderer>() : null;
            Bounds bounds = tree != null ? tree.bounds : new Bounds(_focus.Root.position, Vector3.zero);
            harvestGlow.transform.position = bounds.center;
            ParticleSystem.ShapeModule shape = harvestGlow.shape;
            shape.scale = new Vector3(bounds.size.x, bounds.size.y * 0.5f, bounds.size.z);
            harvestGlow.Play(true);
        }
    }
}
