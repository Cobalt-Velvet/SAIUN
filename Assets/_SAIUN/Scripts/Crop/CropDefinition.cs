using _SAIUN.Scripts.Core;
using UnityEngine;

namespace _SAIUN.Scripts.Crop
{
    /// <summary>작물 해금 조건 종류 (사양서 v1.1 7-3). 랜덤 요소는 두지 않는다.</summary>
    public enum UnlockCondition
    {
        /// <summary>처음부터 심을 수 있다.</summary>
        Default,

        /// <summary>누적 집중 시간(시간)이 기준 이상.</summary>
        TotalFocusHours,

        /// <summary>누적 수확 횟수가 기준 이상.</summary>
        HarvestCount,
    }

    /// <summary>
    /// 작물 한 종류의 데이터 (사양서 v1.1 7장). 작물 종류는 코드에 박지 않고 이 에셋으로 늘린다.
    /// 성장 단계마다 보여 줄 모델, 심는 데 필요한 세션 설정, 해금 조건을 갖는다.
    /// </summary>
    [CreateAssetMenu(menuName = "SAIUN/Crop Definition", fileName = "Crop")]
    public class CropDefinition : ScriptableObject
    {
        /// <summary>성장 단계 중 모델이 있는 단계 수(씨앗~수확 가능).</summary>
        public const int StageModelCount = 5;

        /// <summary>unlocks 테이블의 ItemId 앞에 붙인다. 코스튬 등 다른 해금 항목과 구분한다.</summary>
        public const string UnlockItemPrefix = "crop.";

        private const int MinutesPerHour = 60;

        [Tooltip("SessionConfig.CropType과 DB에 저장되는 값. 소문자 영문.")]
        [SerializeField] private string id = SessionConfig.DefaultCropType;

        [SerializeField] private string displayName = "쌀";

        [Tooltip("씨앗, 발아, 성장, 결실, 수확 가능 순서의 모델")]
        [SerializeField] private GameObject[] stagePrefabs = new GameObject[StageModelCount];

        [Header("필요 포모도로 설정 (사양서 7-3)")]
        [SerializeField, Min(0)] private int requiredFocusMinutes = SessionConfig.DefaultFocusMinutes;

        [SerializeField, Min(0)] private int requiredSets = SessionConfig.DefaultTotalSets;

        [Header("해금 조건 (사양서 7-3)")]
        [SerializeField] private UnlockCondition unlockCondition = UnlockCondition.Default;

        [Tooltip("누적 집중 시간이면 시간 단위, 수확 횟수면 회 단위")]
        [SerializeField, Min(0)] private int unlockThreshold;

        public string Id => id;
        public string DisplayName => displayName;
        public int RequiredFocusMinutes => requiredFocusMinutes;
        public int RequiredSets => requiredSets;
        public UnlockCondition UnlockCondition => unlockCondition;
        public int UnlockThreshold => unlockThreshold;

        /// <summary>처음부터 심을 수 있는 기본 작물인지.</summary>
        public bool IsDefault => unlockCondition == UnlockCondition.Default;

        /// <summary>unlocks 테이블에 기록하는 아이템 id.</summary>
        public string UnlockItemId => UnlockItemPrefix + id;

        /// <summary>단계에 맞는 모델. 모델이 없는 단계(None·Dead)나 비어 있는 칸이면 null.</summary>
        public GameObject GetStagePrefab(CropStage stage)
        {
            int index = (int)stage - (int)CropStage.Seed;
            if (stagePrefabs == null || index < 0 || index >= stagePrefabs.Length) return null;
            return stagePrefabs[index];
        }

        /// <summary>세션 설정이 이 작물의 필요 집중 시간과 세트 수를 채우는지.</summary>
        public bool MeetsRequirement(SessionConfig config)
        {
            return config != null
                   && config.FocusMinutes >= requiredFocusMinutes
                   && config.TotalSets >= requiredSets;
        }

        /// <summary>누적 기록이 해금 조건을 채우는지. 기본 작물은 항상 참이다.</summary>
        public bool IsUnlockedBy(int totalFocusMinutes, int harvestCount)
        {
            switch (unlockCondition)
            {
                case UnlockCondition.TotalFocusHours:
                    return totalFocusMinutes >= unlockThreshold * MinutesPerHour;
                case UnlockCondition.HarvestCount:
                    return harvestCount >= unlockThreshold;
                default:
                    return true;
            }
        }

        /// <summary>에디터 생성기와 테스트가 데이터를 채울 때 쓴다.</summary>
        public void Configure(string cropId, string name, GameObject[] prefabs)
        {
            id = cropId;
            displayName = name;
            stagePrefabs = prefabs;
        }

        /// <summary>에디터 생성기와 테스트가 필요 설정과 해금 조건을 채울 때 쓴다.</summary>
        public void ConfigureRules(int focusMinutes, int sets, UnlockCondition condition, int threshold)
        {
            requiredFocusMinutes = focusMinutes;
            requiredSets = sets;
            unlockCondition = condition;
            unlockThreshold = threshold;
        }
    }
}
