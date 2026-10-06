using System.Collections.Generic;
using UnityEngine;

namespace _SAIUN.Scripts.Crop
{
    /// <summary>
    /// 게임에 있는 작물 정의 목록.
    /// 쉬는 동안 나무 상자에서 저절로 자라는 텃밭 작물들과, 집중할 때 화분에 하나만 심는 집중 작물로 나뉜다.
    /// </summary>
    [CreateAssetMenu(menuName = "SAIUN/Crop Catalog", fileName = "CropCatalog")]
    public class CropCatalog : ScriptableObject
    {
        [Tooltip("쉬는 동안 상자 칸마다 무작위로 자라는 작물")]
        [SerializeField] private List<CropDefinition> crops = new List<CropDefinition>();

        [Tooltip("집중할 때 화분에 심는 작물")]
        [SerializeField] private CropDefinition focusCrop;

        /// <summary>텃밭 작물 목록.</summary>
        public IReadOnlyList<CropDefinition> Crops => crops;

        /// <summary>집중 작물. 없으면 null.</summary>
        public CropDefinition FocusCrop => focusCrop;

        /// <summary>id가 같은 정의(텃밭 작물과 집중 작물 모두에서 찾는다). 없으면 null.</summary>
        public CropDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (focusCrop != null && focusCrop.Id == id) return focusCrop;
            foreach (CropDefinition crop in crops)
            {
                if (crop != null && crop.Id == id) return crop;
            }
            return null;
        }

        /// <summary>에디터 생성기와 테스트가 목록을 채울 때 쓴다.</summary>
        public void Configure(IEnumerable<CropDefinition> gardenCrops, CropDefinition focus)
        {
            crops = new List<CropDefinition>(gardenCrops);
            focusCrop = focus;
        }
    }
}
