using System;
using System.Collections.Generic;
using _SAIUN.Scripts.Core;
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

        /// <summary>
        /// 이번 세션에 심을 작물을 고른다.
        /// 고른 작물이 해금돼 있고 필요 설정을 채우면 그대로 쓰고, 아니면 목록 순서대로 조건에 맞는 첫 작물을 쓴다.
        /// 기본 작물은 필요 설정과 무관하게 심을 수 있다. 짧은 세션에도 화단이 비지 않게 하기 위해서다.
        /// </summary>
        /// <param name="config">시작할 세션 설정</param>
        /// <param name="isUnlocked">작물이 해금됐는지 묻는 함수. 기본 작물에는 호출하지 않는다.</param>
        /// <returns>심을 작물. 목록이 비어 있으면 null.</returns>
        public CropDefinition ChooseFor(SessionConfig config, Func<CropDefinition, bool> isUnlocked)
        {
            bool CanPlant(CropDefinition crop)
            {
                if (crop == null) return false;
                if (crop.IsDefault) return true;
                return crop.MeetsRequirement(config) && isUnlocked != null && isUnlocked(crop);
            }

            CropDefinition preferred = Find(config != null ? config.CropType : null);
            if (CanPlant(preferred)) return preferred;

            foreach (CropDefinition crop in crops)
            {
                if (CanPlant(crop)) return crop;
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
