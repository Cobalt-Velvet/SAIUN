using UnityEngine;

namespace _SAIUN.Scripts.Crop
{
    /// <summary>
    /// 작물 한 종류의 데이터. 작물 종류는 코드에 박지 않고 이 에셋으로 늘린다.
    /// 성장 단계마다 보여 줄 모델을 갖는다.
    /// </summary>
    [CreateAssetMenu(menuName = "SAIUN/Crop Definition", fileName = "Crop")]
    public class CropDefinition : ScriptableObject
    {
        /// <summary>성장 단계 중 모델이 있는 단계 수(씨앗~수확 가능).</summary>
        public const int StageModelCount = 5;

        [Tooltip("DB의 수확·보유 기록에 남는 값. 소문자 영문.")]
        [SerializeField] private string id = "rice";

        [SerializeField] private string displayName = "쌀";

        [Tooltip("씨앗, 발아, 성장, 결실, 수확 가능 순서의 모델")]
        [SerializeField] private GameObject[] stagePrefabs = new GameObject[StageModelCount];

        public string Id => id;
        public string DisplayName => displayName;

        /// <summary>단계에 맞는 모델. 모델이 없는 단계(None·Dead)나 비어 있는 칸이면 null.</summary>
        public GameObject GetStagePrefab(CropStage stage)
        {
            int index = (int)stage - (int)CropStage.Seed;
            if (stagePrefabs == null || index < 0 || index >= stagePrefabs.Length) return null;
            return stagePrefabs[index];
        }

        /// <summary>에디터 생성기와 테스트가 데이터를 채울 때 쓴다.</summary>
        public void Configure(string cropId, string name, GameObject[] prefabs)
        {
            id = cropId;
            displayName = name;
            stagePrefabs = prefabs;
        }
    }
}
