using UnityEngine;

namespace Fantasia.Data
{
    public enum SkillType
    {
        Passive,
        Active,
        WorldActive, // 월드에서만 쓰는 액티브 (예: 연금술, 그림자 도약)
    }

    public enum SkillTarget
    {
        None,        // 패시브용
        Self,
        SingleAlly,
        SingleEnemy,
        AllAllies,
        AllEnemies,
    }

    // 스킬 정의 (System.md 각 직업 "스킬" 표). 비용은 집중력/HP/MP를 섞어 쓰는 직업이
    // 있어 FocusCost/HPCost/MPCost를 모두 두고, 안 쓰는 값은 0으로 둔다.
    [CreateAssetMenu(fileName = "Skill", menuName = "Fantasia/Data/Skill Definition")]
    public class SkillDefinition : ScriptableObject
    {
        [Header("식별")]
        public string Id;
        public string DisplayName;
        public JobId OwnerJob;
        public SkillType Type;
        public SkillTarget Target;

        [Header("비용·재사용")]
        public int FocusCost;
        public int HPCost;
        public int MPCost;
        public int CooldownTurns; // 0 = 쿨다운 없음(패시브·상시)

        [Header("효과")]
        [TextArea(3, 8)] public string EffectDescription; // System.md 원문 그대로 보관
    }
}
