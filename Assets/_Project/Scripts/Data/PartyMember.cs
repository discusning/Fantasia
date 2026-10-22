using System;
using UnityEngine;

namespace Fantasia.Data
{
    // 런타임 파티원 상태 (System.md "레벨·기본 자원" + "능력치" 공식).
    // JobDefinition은 불변 원본 데이터, PartyMember가 레벨·경험치·현재 자원·장비를 갖는다.
    public class PartyMember
    {
        public const int MaxLevel = 10;
        public const int BaseMaxFocus = 3;
        public const int AbsoluteMaxFocus = 6; // 장비·기도 포함 상한 (보정원은 아직 미구현)
        public const int MaxLP = 5;

        public JobDefinition Job { get; }
        public int Level { get; private set; } = 1;
        public int Experience { get; private set; }
        public WeaponDefinition EquippedWeapon { get; private set; }

        public int CurrentHP { get; private set; }
        public int CurrentMP { get; private set; }
        public int CurrentFocus { get; private set; }
        public int CurrentLP { get; private set; }

        public PartyMember(JobDefinition job)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
            EquippedWeapon = job.StartingWeapon;
            CurrentHP = MaxHP;
            CurrentMP = MaxMP;
            CurrentFocus = Mathf.Min(job.StartingFocus, MaxFocus);
            CurrentLP = Mathf.Min(job.StartingLP, MaxLP);
        }

        private int LevelBonusSteps => Level - 1;

        // 최대 HP = 활력×2 + 레벨당 +8. 레벨 1에서는 System.md 직업별 초기 HP 표와 같은 값이 나온다
        // (예: 명탐정 활력54 → 108, 표의 초기 HP 108과 일치).
        public int MaxHP => Job.Stats.Vitality * 2 + LevelBonusSteps * 8;

        // MP를 쓰는 직업(역병 의사·연금술사·지휘자·도박가)만 활력×2 + 레벨당 +6, 나머지는 0 고정.
        public int MaxMP => Job.UsesMP ? Job.Stats.Vitality * 2 + LevelBonusSteps * 6 : 0;

        // 집중력 최대는 기본 3, 장비·기도 포함 최대 6 — 보정원(장비 스탯·석상 기도)은
        // 구조화된 수치가 아직 없어 D18(장비 고유효과)/D23(보드 이벤트) 때 반영한다.
        public int MaxFocus => BaseMaxFocus;

        // 직업 스탯 + 착용 무기 보정, 0~99로 제한. 슬롯 확률·속도·회피는 이 값을 쓴다.
        // 최대 HP·MP는 아직 직업 활력 기준 (무기 계열 중 활력 보정이 있는 것이 없음).
        public StatBlock FinalStats
        {
            get
            {
                var stats = Job.Stats;
                if (EquippedWeapon != null) stats += EquippedWeapon.WearStatBonus;
                return stats.ClampToCap();
            }
        }

        public int FinalPhysicalAttack
        {
            get
            {
                int weaponBonus = EquippedWeapon != null && !EquippedWeapon.Category.IsMagic()
                    ? EquippedWeapon.ComputeAttackBonus()
                    : 0;
                return Job.BasePhysicalAttack + LevelBonusSteps * 2 + weaponBonus;
            }
        }

        public int FinalMagicAttack
        {
            get
            {
                int weaponBonus = EquippedWeapon != null && EquippedWeapon.Category.IsMagic()
                    ? EquippedWeapon.ComputeAttackBonus()
                    : 0;
                return Job.BaseMagicAttack + LevelBonusSteps * 2 + weaponBonus;
            }
        }

        // 방어력은 1차 구현 범위에서 장비 방어구 보정이 아직 없어 직업 기본값 그대로 노출한다 (D17/D18에서 확장).
        public int FinalPhysicalDefense => Job.BasePhysicalDefense;
        public int FinalMagicDefense => Job.BaseMagicDefense;

        public bool IsAlive => CurrentHP > 0;

        public int ExperienceToNextLevel => Level * 60;

        public void EquipWeapon(WeaponDefinition weapon)
        {
            EquippedWeapon = weapon;
            // 장비 교체로 최대치가 늘어도 현재 자원은 무료로 채우지 않는다 (System.md) —
            // 넘친 경우에만 상한으로 깎는다.
            CurrentHP = Mathf.Min(CurrentHP, MaxHP);
            CurrentMP = Mathf.Min(CurrentMP, MaxMP);
        }

        public void GrantExperience(int amount)
        {
            if (amount <= 0 || Level >= MaxLevel) return;
            Experience += amount;
            while (Level < MaxLevel && Experience >= ExperienceToNextLevel)
            {
                Experience -= ExperienceToNextLevel;
                LevelUp();
            }
        }

        private void LevelUp()
        {
            int hpBefore = MaxHP;
            int mpBefore = MaxMP;
            Level++;
            // 레벨업 시 "증가한" 최대 HP/MP만큼만 현재 자원을 회복한다 (전체 리필 아님).
            CurrentHP += MaxHP - hpBefore;
            CurrentMP += MaxMP - mpBefore;
            CurrentFocus = Mathf.Min(CurrentFocus + 1, MaxFocus);
            // 부활 횟수(LP)는 레벨업으로 회복하지 않는다.
        }

        // 스킬 비용·추가 집중(슬롯 확정) 공용. 부족하면 아무것도 쓰지 않고 false.
        public bool TrySpendFocus(int amount)
        {
            if (amount < 0 || amount > CurrentFocus) return false;
            CurrentFocus -= amount;
            return true;
        }

        public void TakeDamage(int amount)
        {
            CurrentHP = Mathf.Max(0, CurrentHP - amount);
        }

        public void Heal(int amount)
        {
            CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        }
    }
}
