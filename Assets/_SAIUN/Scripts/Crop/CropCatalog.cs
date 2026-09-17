using System.Collections.Generic;
using UnityEngine;

namespace _SAIUN.Scripts.Crop
{
    /// <summary>게임에 있는 작물 정의 목록. 작물 종류 문자열로 정의를 찾는다.</summary>
    [CreateAssetMenu(menuName = "SAIUN/Crop Catalog", fileName = "CropCatalog")]
    public class CropCatalog : ScriptableObject
    {
        [SerializeField] private List<CropDefinition> crops = new List<CropDefinition>();

        public IReadOnlyList<CropDefinition> Crops => crops;

        /// <summary>id가 같은 정의. 없으면 null.</summary>
        public CropDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (CropDefinition crop in crops)
            {
                if (crop != null && crop.Id == id) return crop;
            }
            return null;
        }

        /// <summary>에디터 생성기와 테스트가 목록을 채울 때 쓴다.</summary>
        public void Configure(IEnumerable<CropDefinition> definitions)
        {
            crops = new List<CropDefinition>(definitions);
        }
    }
}
