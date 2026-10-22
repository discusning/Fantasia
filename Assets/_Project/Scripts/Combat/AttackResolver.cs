using System;
using Fantasia.Data;
using Fantasia.Dice;
using UnityEngine;

namespace Fantasia.Combat
{
    public enum DamageType
    {
        Physical, // 물리 방어 참조
        Magic,    // 마법 방어 참조
    }

    // 공격 1회의 입력값. 아군은 AttackResolver.FromPartyMember, 적은 FromEnemy로 만든 뒤
    // 스킬이면 Coefficient/PierceRatio 등을 덮어쓴다 (예: 일점 돌파 1.3, 관통탄 0.5).
    public struct AttackRequest
    {
        public int SlotCount;     // 2/3/4
        public float SlotChance;  // 슬롯 1개 최종 확률 (5~95% 클램프 후)
        public bool UseFocus;     // 추가 집중 — 행동당 슬롯 1개만 확정 (집중력 차감은 호출부)
        public float Coefficient; // 기술 계수, 기본 공격 1.0
        public int AttackValue;   // 최종 공격력 A 또는 마력 M
        public DamageType Type;
        public float PierceRatio; // 일반 피해에서 무시하는 방어력 비율
    }

    // 피격자 쪽 값. 아군은 DefenseOf(PartyMember), 적은 DefenseOf(EnemyDefinition).
    public struct DefenseProfile
    {
        public int PhysicalDefense;
        public int MagicDefense;
        public float EvasionChance;  // 일반 회피 확률 (적은 별도 회피 스탯이 없으면 0)
        public bool HasEvasionState; // 적 전용 [회피 상태] — 크리티컬만 명중
        public int CurrentHP;

        public int DefenseAgainst(DamageType type) => type == DamageType.Magic ? MagicDefense : PhysicalDefense;
    }

    public readonly struct AttackOutcome
    {
        public readonly SlotCheck.Result Slots;
        public readonly bool IsCritical;
        public readonly bool Evaded;
        public readonly bool EvadedByState; // 회피 상태로 빗나감 (일반 회피와 UI 문구 구분용)
        public readonly int Damage;         // 계산된 피해
        public readonly int AppliedDamage;  // 현재 HP를 넘는 부분을 뺀 실제 차감량 — 흡수·회복 계산은 이 값

        public AttackOutcome(SlotCheck.Result slots, bool isCritical, bool evaded, bool evadedByState, int damage, int appliedDamage)
        {
            Slots = slots;
            IsCritical = isCritical;
            Evaded = evaded;
            EvadedByState = evadedByState;
            Damage = damage;
            AppliedDamage = appliedDamage;
        }
    }

    // D10 공통 전투 계산 (System.md "자원·행동·피해의 공통 정의" + "공격·조우·회피 확률표").
    // 기존 WeaponAttackResolver(구 프로토타입)는 전투 씬 교체(단계 3) 때 제거한다.
    public static class AttackResolver
    {
        public const int UnarmedSlotCount = 2;
        public const float BaseEvasion = 0.05f;
        public const float EvasionPerSpeed = 0.001f;
        public const float MaxEvasion = 0.40f;
        public const int DefendActionBonus = 8; // 방어 행동: 다음 개인 턴 전까지 물리·마법 방어 +8

        // 계수 × 공격력 곱이 13.0이 12.9999로 떨어지는 부동소수점 오차 방지.
        private const float FloorEpsilon = 0.0001f;

        // 일반 피해 = max(0, floor(계수 × A|M) − 대응 방어). 관통은 방어력 일부를 무시한다.
        // 가정: 관통 후 남는 방어력은 내림 (예: 방어 5, 관통 50% → 2). 기획 확인 전.
        public static int NormalDamage(float coefficient, int attackValue, int defense, float pierceRatio = 0f)
        {
            int raw = Mathf.FloorToInt(coefficient * attackValue + FloorEpsilon);
            int effectiveDefense = Mathf.FloorToInt(Mathf.Max(0, defense) * (1f - pierceRatio) + FloorEpsilon);
            return Mathf.Max(0, raw - effectiveDefense);
        }

        // 크리티컬 = floor(2 × 계수 × A|M), 방어 무시. 관통은 다시 적용하지 않는다.
        public static int CriticalDamage(float coefficient, int attackValue)
        {
            return Mathf.Max(0, Mathf.FloorToInt(2f * coefficient * attackValue + FloorEpsilon));
        }

        // 일반 회피 = 5% + 속도 × 0.1%p + 장비/스킬, 최대 40%.
        public static float EvasionChance(int speed, float bonus = 0f)
        {
            return Mathf.Clamp(BaseEvasion + speed * EvasionPerSpeed + bonus, 0f, MaxEvasion);
        }

        public static AttackRequest FromPartyMember(PartyMember member, float coefficient = 1f, bool useFocus = false, float slotBonus = 0f)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            var weapon = member.EquippedWeapon;
            var stats = member.FinalStats;

            // 맨손: 2슬롯·힘 판정·무기 보너스 0의 물리 공격.
            if (weapon == null)
            {
                return new AttackRequest
                {
                    SlotCount = UnarmedSlotCount,
                    SlotChance = SlotCheck.FinalSlotChance(UnarmedSlotCount, stats.Strength, slotBonus),
                    UseFocus = useFocus,
                    Coefficient = coefficient,
                    AttackValue = member.FinalPhysicalAttack,
                    Type = DamageType.Physical,
                    PierceRatio = 0f,
                };
            }

            bool magic = weapon.Category.IsMagic();
            int relevantStat = stats.Get(weapon.Category.RelevantStat());
            return new AttackRequest
            {
                SlotCount = weapon.SlotCount,
                SlotChance = SlotCheck.FinalSlotChance(weapon.SlotCount, relevantStat, slotBonus),
                UseFocus = useFocus,
                Coefficient = coefficient,
                AttackValue = magic ? member.FinalMagicAttack : member.FinalPhysicalAttack,
                Type = magic ? DamageType.Magic : DamageType.Physical,
                PierceRatio = weapon.Category.DefensePierceRatio(),
            };
        }

        // 적은 스탯 공식 없이 자신의 슬롯별 확률을 그대로 쓴다. 공격 종류를 안 주면
        // 공격력이 있으면 물리, 없으면 마법 (예: 연성 독거미 유체는 마력만 보유).
        public static AttackRequest FromEnemy(EnemyDefinition enemy, float coefficient = 1f, DamageType? type = null)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            var damageType = type ?? (enemy.PhysicalAttack > 0 ? DamageType.Physical : DamageType.Magic);
            return new AttackRequest
            {
                SlotCount = enemy.SlotCount,
                SlotChance = Mathf.Clamp(enemy.SlotSuccessRate, SlotCheck.MinChance, SlotCheck.MaxChance),
                UseFocus = false,
                Coefficient = coefficient,
                AttackValue = damageType == DamageType.Magic ? enemy.MagicAttack : enemy.PhysicalAttack,
                Type = damageType,
                PierceRatio = 0f,
            };
        }

        public static DefenseProfile DefenseOf(PartyMember member, bool defending = false, float evasionBonus = 0f)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            int defendBonus = defending ? DefendActionBonus : 0;
            return new DefenseProfile
            {
                PhysicalDefense = member.FinalPhysicalDefense + defendBonus,
                MagicDefense = member.FinalMagicDefense + defendBonus,
                EvasionChance = EvasionChance(member.FinalStats.Speed, evasionBonus),
                HasEvasionState = false,
                CurrentHP = member.CurrentHP,
            };
        }

        public static DefenseProfile DefenseOf(EnemyDefinition enemy, int currentHP, bool hasEvasionState, bool defending = false)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            int defendBonus = defending ? DefendActionBonus : 0;
            return new DefenseProfile
            {
                PhysicalDefense = enemy.PhysicalDefense + defendBonus,
                MagicDefense = enemy.MagicDefense + defendBonus,
                EvasionChance = 0f,
                HasEvasionState = hasEvasionState,
                CurrentHP = currentHP,
            };
        }

        public static AttackOutcome Resolve(AttackRequest attack, DefenseProfile defense)
        {
            var slots = SlotCheck.RollWithChance(attack.SlotCount, attack.SlotChance, attack.UseFocus ? 1 : 0);
            bool generalEvasionRolled = DiceRoller.RollChance(defense.EvasionChance);
            return Compute(attack, defense, slots, generalEvasionRolled);
        }

        // 판정 결과가 이미 정해졌을 때의 순수 계산 (셀프테스트에서 결정적으로 검증하려고 분리).
        // 처리 순서: 회피 → 크리티컬/일반 피해 → 방어 → HP 차감량 (상태 효과·파손은 D12/D18).
        public static AttackOutcome Compute(AttackRequest attack, DefenseProfile defense, SlotCheck.Result slots, bool generalEvasionRolled)
        {
            bool critical = slots.AllSucceeded;

            // 회피 상태 적: 비크리티컬은 무조건 회피, 크리티컬은 명중.
            if (defense.HasEvasionState && !critical)
                return new AttackOutcome(slots, false, true, true, 0, 0);

            // 일반 회피는 크리티컬에도 적용한다.
            if (generalEvasionRolled)
                return new AttackOutcome(slots, critical, true, false, 0, 0);

            int damage = critical
                ? CriticalDamage(attack.Coefficient, attack.AttackValue)
                : NormalDamage(attack.Coefficient, attack.AttackValue, defense.DefenseAgainst(attack.Type), attack.PierceRatio);
            int applied = Mathf.Min(damage, Mathf.Max(0, defense.CurrentHP));
            return new AttackOutcome(slots, critical, false, false, damage, applied);
        }
    }
}
