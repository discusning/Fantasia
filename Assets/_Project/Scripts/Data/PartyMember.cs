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

        // 착용 슬롯 5칸 (GearSlot 순서). 예비 장비는 파티 인벤토리에 — 능력치 합산 안 함.
        private readonly GearItem[] _equipped = new GearItem[5];
        public GearItem GetEquipped(GearSlot slot) => _equipped[(int)slot];
        public GearItem EquippedWeaponItem => _equipped[(int)GearSlot.Weapon];
        public WeaponDefinition EquippedWeapon => EquippedWeaponItem?.Weapon;

        // 퀵 슬롯 4칸 — 소모품 종류만 가리키고 수량은 파티 인벤토리를 본다 (System.md "퀵 슬롯").
        public const int QuickSlotCount = 4;
        public ConsumableDefinition[] QuickSlots { get; } = new ConsumableDefinition[QuickSlotCount];

        public int CurrentHP { get; private set; }
        public int CurrentMP { get; private set; }
        public int CurrentFocus { get; private set; }
        public int CurrentLP { get; private set; }

        // 걸린 상태 효과 (D12). 중독처럼 전투 뒤에도 남는 것이 있어 파티원이 직접 갖는다.
        public StatusSet Statuses { get; } = new();

        // 십자가 석상의 활성 기도 (캐릭터당 1개, 이번 회차 동안). 바꾸면 SetPrayer — 최대 HP·집중력이 줄면 현재 값도 맞춘다.
        public PrayerKind Prayer { get; private set; }

        public void SetPrayer(PrayerKind prayer)
        {
            Prayer = prayer;
            Statuses.ImmuneToConfusion = prayer == PrayerKind.Focus;
            if (prayer == PrayerKind.Focus) Statuses.Remove(StatusEffectType.Confusion);
            CurrentHP = Mathf.Min(CurrentHP, MaxHP);
            CurrentFocus = Mathf.Min(CurrentFocus, MaxFocus);
        }

        public PartyMember(JobDefinition job)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
            if (job.StartingWeapon != null)
                _equipped[(int)GearSlot.Weapon] = new GearItem(job.StartingWeapon) { IsStartingGear = true };
            CurrentHP = MaxHP;
            CurrentMP = MaxMP;
            CurrentFocus = Mathf.Min(job.StartingFocus, MaxFocus);
            CurrentLP = Mathf.Min(job.StartingLP, MaxLP);
        }

        private int LevelBonusSteps => Level - 1;

        // 최대 HP = 활력×2 + 레벨당 +8. 레벨 1에서는 System.md 직업별 초기 HP 표와 같은 값이 나온다
        // (예: 명탐정 활력54 → 108, 표의 초기 HP 108과 일치).
        // + 방어구·장신구 최대 HP 보정 (예: 인부의 두꺼운 코트 +10).
        // + 생명의 기도 활력 +5 → HP +10 (활력으로만, 따로 더하지 않음)
        public int MaxHP => (Job.Stats.Vitality + PrayerRules.StatBonus(Prayer).Vitality) * 2 + LevelBonusSteps * 8 + GearSum(g => g.MaxHPBonus);

        // MP를 쓰는 직업(역병 의사·연금술사·지휘자·도박가)만 활력×2 + 레벨당 +6 (+ 장신구 최대 MP), 나머지는 0 고정.
        public int MaxMP => Job.UsesMP ? Job.Stats.Vitality * 2 + LevelBonusSteps * 6 + GearSum(g => g.MaxMPBonus) : 0;

        // 집중력 최대는 기본 3 + 장비 + 집중의 기도 +2, 최대 6.
        public int MaxFocus => Mathf.Min(AbsoluteMaxFocus, BaseMaxFocus + GearSum(g => g.MaxFocusBonus) + (Prayer == PrayerKind.Focus ? PrayerRules.FocusMaxBonus : 0));

        private int GearSum(Func<GearItem, int> value)
        {
            int sum = 0;
            foreach (var g in _equipped) if (g != null) sum += value(g);
            return sum;
        }

        // 직업 스탯 + 착용 무기 보정 − 상태 효과(중독 전 스탯·속박 속도), 0~99로 제한. 슬롯 확률·속도·회피는 이 값을 쓴다.
        // 최대 HP·MP는 아직 직업 활력 기준 — 중독의 활력 감소를 최대 자원에 반영하는 건 D12 2차에서.
        public StatBlock FinalStats
        {
            get
            {
                var stats = Job.Stats;
                foreach (var g in _equipped) if (g != null) stats += g.StatBonus;
                stats += PrayerRules.StatBonus(Prayer); // 석상 기도
                int all = Statuses.AllStatPenalty;
                stats += new StatBlock(-all, -all - Statuses.SpeedPenalty, -all, -all, -all, -all);
                return stats.ClampToCap();
            }
        }

        public int FinalPhysicalAttack
        {
            get
            {
                var w = EquippedWeaponItem;
                int weaponBonus = w != null && !w.IsMagicWeapon ? w.AttackBonus : 0;
                return Mathf.Max(0, Job.BasePhysicalAttack + LevelBonusSteps * 2 + weaponBonus - Statuses.AttackPenalty);
            }
        }

        public int FinalMagicAttack
        {
            get
            {
                var w = EquippedWeaponItem;
                int weaponBonus = w != null && w.IsMagicWeapon ? w.AttackBonus : 0;
                return Mathf.Max(0, Job.BaseMagicAttack + LevelBonusSteps * 2 + weaponBonus - Statuses.AttackPenalty);
            }
        }

        // 직업 기본 + 방어구·장신구 − 상태 효과
        public int FinalPhysicalDefense => Mathf.Max(0, Job.BasePhysicalDefense + GearSum(g => g.PhysicalDefense) - Statuses.DefensePenalty);
        public int FinalMagicDefense => Mathf.Max(0, Job.BaseMagicDefense + GearSum(g => g.MagicDefense) - Statuses.DefensePenalty);

        // 이 직업이 가진 패시브 값 합 (없으면 0) — 예: 현장 검증 기습 +0.1.
        public float PassiveValue(PassiveType type)
        {
            float sum = 0f;
            if (Job.Skills == null) return sum;
            foreach (var s in Job.Skills) if (s != null && s.Type == SkillType.Passive && s.Passive == type) sum += s.PassiveValue;
            return sum;
        }

        public bool IsAlive => CurrentHP > 0;

        public int ExperienceToNextLevel => Level * 60;

        // 인벤토리 없이 무기를 바로 바꾼다 (셀프 테스트·시작 장비용). null이면 맨손.
        public void EquipWeapon(WeaponDefinition weapon)
        {
            _equipped[(int)GearSlot.Weapon] = weapon != null ? new GearItem(weapon) : null;
            ClampResourcesToMax();
        }

        // 인벤토리의 장비를 착용한다. 같은 슬롯에 있던 장비는 인벤토리로 돌아간다.
        public bool TryEquip(GearItem item, PartyInventory inventory, out string reason)
        {
            if (item == null) { reason = "장비 없음"; return false; }
            if (!item.CanEquip(this, out reason)) return false;
            var previous = _equipped[(int)item.Slot];
            _equipped[(int)item.Slot] = item;
            inventory?.SwapGear(item, previous);
            ClampResourcesToMax();
            return true;
        }

        // 슬롯을 비우고 인벤토리로. 인벤토리가 꽉 차면 실패.
        public bool TryUnequip(GearSlot slot, PartyInventory inventory, out string reason)
        {
            reason = null;
            var current = _equipped[(int)slot];
            if (current == null) { reason = "빈 슬롯"; return false; }
            if (inventory != null && inventory.IsGearFull) { reason = "인벤토리가 가득 참"; return false; }
            _equipped[(int)slot] = null;
            inventory?.SwapGear(null, current);
            ClampResourcesToMax();
            return true;
        }

        // 장비 교체로 최대치가 늘어도 현재 자원은 무료로 채우지 않는다 (System.md) — 넘친 경우에만 상한으로 깎는다.
        private void ClampResourcesToMax()
        {
            CurrentHP = Mathf.Min(CurrentHP, MaxHP);
            CurrentMP = Mathf.Min(CurrentMP, MaxMP);
            CurrentFocus = Mathf.Min(CurrentFocus, MaxFocus);
        }

        // 소모품 회복 (영약·향초 등). 최대치를 넘지 않는다. 회복량을 돌려준다.
        public int RestoreMP(int amount)
        {
            int before = CurrentMP;
            CurrentMP = Mathf.Min(MaxMP, CurrentMP + Mathf.Max(0, amount));
            return CurrentMP - before;
        }

        public int RestoreFocus(int amount)
        {
            int before = CurrentFocus;
            CurrentFocus = Mathf.Min(MaxFocus, CurrentFocus + Mathf.Max(0, amount));
            return CurrentFocus - before;
        }

        public bool GainLP()
        {
            if (IsPermanentlyDead || CurrentLP >= MaxLP) return false;
            CurrentLP++;
            return true;
        }

        public void GrantExperience(int amount)
        {
            if (amount <= 0 || Level >= MaxLevel) return;
            if (Prayer == PrayerKind.Wisdom) amount += Mathf.FloorToInt(amount * PrayerRules.WisdomExpBonus); // 지혜의 기도 (버림)
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

        // 마법 무기 기본 공격·스킬 MP. 부족하면 쓰지 않고 false.
        public bool TrySpendMP(int amount)
        {
            if (amount < 0 || amount > CurrentMP) return false;
            CurrentMP -= amount;
            return true;
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
            if (IsPermanentlyDead) return;
            CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        }

        // ── D21 쓰러짐·부활·영구 사망 (System.md "레벨·기본 자원" + "패배 시 약탈") ──
        // HP 0 = 쓰러짐. LP가 있으면 1을 써서 부활할 수 있고, 없으면 영구 사망. LP 0이어도 HP가 남으면 생존.
        public bool IsPermanentlyDead { get; private set; }
        public bool IsDowned => CurrentHP <= 0 && !IsPermanentlyDead;

        public bool TrySpendLP()
        {
            if (CurrentLP <= 0) return false;
            CurrentLP--;
            return true;
        }

        // 부활: 최대 HP의 ratio만큼(최소 1)으로 복귀. 출혈·화상·중독은 제거, 저주 등은 유지.
        // MP·집중력·다른 상태는 자동으로 초기화하지 않는다. LP 차감은 호출부(TrySpendLP)에서.
        public void Revive(float hpRatio)
        {
            if (IsPermanentlyDead) return;
            CurrentHP = Mathf.Max(1, Mathf.FloorToInt(MaxHP * hpRatio));
            Statuses.Remove(StatusEffectType.WeakBleed);
            Statuses.Remove(StatusEffectType.Poison);
            // 화상은 상태 2차 구현 때 여기 추가
        }

        public void MarkPermanentlyDead()
        {
            CurrentHP = 0;
            IsPermanentlyDead = true;
        }

        // ── D27 불러오기: 저장된 값을 그대로 (장비를 먼저 입힌 뒤 자원 — 최대치 계산이 장비에 따라 달라짐) ──
        public void RestoreEquipped(GearSlot slot, GearItem item) => _equipped[(int)slot] = item;

        public void RestoreState(int level, int experience, int hp, int mp, int focus, int lp, bool permanentlyDead)
        {
            Level = Mathf.Clamp(level, 1, MaxLevel);
            Experience = Mathf.Max(0, experience);
            CurrentHP = Mathf.Clamp(hp, 0, MaxHP);
            CurrentMP = Mathf.Clamp(mp, 0, MaxMP);
            CurrentFocus = Mathf.Clamp(focus, 0, MaxFocus);
            CurrentLP = Mathf.Clamp(lp, 0, MaxLP);
            IsPermanentlyDead = permanentlyDead;
        }
    }
}
