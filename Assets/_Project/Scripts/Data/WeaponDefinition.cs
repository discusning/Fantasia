using UnityEngine;

namespace Fantasia.Data
{
    // 무기 정의 (System.md "무기" 절). 슬롯 수·기본 보너스 B·착용 보정은 계열의
    // 고정값이고, 등급·착용 레벨은 개별 무기 자산이 갖는다.
    [CreateAssetMenu(fileName = "Weapon", menuName = "Fantasia/Data/Weapon Definition")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("식별")]
        public string Id;
        public string DisplayName;
        public WeaponCategory Category;

        [Header("계열 고정값 (System.md 무기 계열 표)")]
        public int SlotCount;           // 2/3/4
        public int BaseBonusB;          // 기본 보너스 B (등급 공식에 쓰임)
        public int BaseAttackMPCost;    // 기본 공격 MP 비용 (물리 계열은 0)
        [TextArea] public string WearStatBonusDescription; // 예: "힘 +2, 속도 +3"
        public StatBlock WearStatBonus;  // 위 설명의 계산용 값 (슬롯 확률·속도에 반영)
        // 스태프 +8 / 마법서 +12. 아직 PartyMember.MaxMP에 더하지 않는다 — 직업 초기 MP 표(역병 의사 132)가
        // 시작 무기 보정을 포함한 값인지 기획 확인 전 (Docs/DevPlan/WorkPlan_M0-M1.md §6).
        public int MaxMPBonus;
        [TextArea] public string CritEffectDescription;    // 예: "크리티컬 시 약한 출혈 3턴"
        // 기본 무기 공격 크리티컬 시 대상에게 거는 상태 (예: 단검 약한 출혈 3턴). 직업 스킬 공격에는 적용 안 함.
        public StatusEffectDefinition[] OnCritStatuses = System.Array.Empty<StatusEffectDefinition>();
        // 기본 무기 공격 크리티컬 시 대상의 턴을 민다 (양손검·대검 계열: 방해함).
        public TurnShift OnCritTurnShift;

        [Header("개별 자산 값")]
        public EquipmentGrade Grade = EquipmentGrade.Normal;
        public int WearLevel = 1;
        public int Price;

        public JobId[] AllowedJobs;

        // 무기 공격 보너스 = ceil((B + 8 × floor((착용레벨-1)/3)) × R) — System.md "등급·착용 레벨·가격 계산"
        public int ComputeAttackBonus()
        {
            var row = EquipmentGradeTable.Get(Grade);
            int levelStep = Mathf.FloorToInt((WearLevel - 1) / 3f);
            float raw = (BaseBonusB + 8 * levelStep) * row.AttackBonusMultiplier;
            return Mathf.CeilToInt(raw);
        }
    }
}
