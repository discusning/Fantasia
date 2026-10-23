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

    // 액티브 스킬이 하는 일 (D11). 같은 종류는 아래 수치 칸만 바꿔 새 스킬을 만들 수 있다.
    public enum SkillEffect
    {
        None,
        Attack,      // 계수 × A|M 피해 (단일 또는 전체), 크리티컬 시 상태·턴 밀기
        Heal,        // 고정 + 마력 비율 회복(확정), 판정 전부 성공 시 상태 제거(선택)
        Cleanse,     // 지정 상태 제거(확정)
        ApplyStatus, // 지정 상태 부여(확정) — 적 약화·자기 강화
    }

    // 패시브가 하는 일 — 전투·조우 코드가 이 값으로 찾아 쓴다.
    public enum PassiveType
    {
        None,
        AmbushSlotBonus,     // 현장 검증: 이 캐릭터가 조우를 시작하면 기습 슬롯 성공률 +PassiveValue
        ConsumableHealBonus, // 조제 숙련: 자신이 쓰는 HP 회복 소모품 +PassiveValue (소모품 D17)
        FirstGunShotBonus,   // 첫 탄의 침묵: 전투 중 첫 활·총 공격 피해 +PassiveValue
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
        // 전투 행동 메뉴의 스킬 버튼 그림. 비우면 이름 글자 버튼.
        public Sprite Icon;

        [Header("비용·재사용")]
        public int FocusCost;
        public int HPCost;
        public int MPCost;
        public int CooldownTurns; // 0 = 쿨다운 없음. N이면 사용 뒤 자신의 다음 N개 개인 턴 동안 재사용 불가

        [Header("액티브 효과 (D11)")]
        public SkillEffect Effect;
        public float Coefficient = 1f;     // 공격 계수 (1.3A 등)
        public bool UsesMagic;             // true면 마력 M·마법 방어 (검은 연무 0.9M)
        public int SlotCount;              // 0 = 현재 무기 슬롯·무기 참조 스탯 / N = 이 스킬 고유 N슬롯 판정
        public StatKind RollStat = StatKind.Intelligence; // SlotCount > 0일 때 판정 스탯
        [Range(0f, 1f)] public float PierceRatio;          // 일반 피해에서 무시할 방어 비율 (관통탄 0.5)
        public int HealFlat;               // 회복 고정량 (응급 봉합 30)
        public float HealMagicRatio;       // 회복 마력 비율 (응급 봉합 0.5 → floor(0.5M))
        public StatusEffectDefinition[] AppliedStatuses = System.Array.Empty<StatusEffectDefinition>(); // 확정 부여
        public StatusEffectDefinition[] OnCritStatuses = System.Array.Empty<StatusEffectDefinition>();  // 크리티컬 명중 시
        public TurnShift OnCritTurnShift;
        public StatusEffectType[] CleansedStatuses = System.Array.Empty<StatusEffectType>();
        public bool CleanseNeedsFullSuccess; // true면 판정 슬롯 전부 성공 시에만 제거 (응급 봉합의 출혈 제거)

        [Header("패시브 효과")]
        public PassiveType Passive;
        public float PassiveValue;

        [Header("설명")]
        [TextArea(3, 8)] public string EffectDescription; // System.md 원문 그대로 보관

        // 이 스킬이 슬롯 판정을 하는지 (추가 집중 사용 가능 여부).
        public bool RollsSlots => Effect == SkillEffect.Attack || (Effect == SkillEffect.Heal && CleanseNeedsFullSuccess);
    }
}
