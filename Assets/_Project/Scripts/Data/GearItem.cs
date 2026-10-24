using UnityEngine;

namespace Fantasia.Data
{
    // 실제로 가진 장비 1개. 같은 기본형이라도 전리품 등급이 다를 수 있어 정의(ScriptableObject)와 분리한다.
    // 무기: 공격 보너스 = ceil((B + 8 × floor((레벨−1)/3)) × R) — 착용 보정 스탯은 등급으로 다시 곱하지 않음.
    // 방어구·장신구: 방어·최대 HP·MP만 새 R / 표 R을 곱해 올림 — 다른 스탯·최대 집중력은 그대로.
    // 가격: 표 가격 × 새 Q / 표 Q, 5 Cent 단위 반올림. 되팔기는 40% 버림 (System.md).
    public class GearItem
    {
        public WeaponDefinition Weapon { get; }
        public ArmorDefinition Armor { get; }
        public EquipmentGrade Grade { get; }

        public GearItem(WeaponDefinition weapon, EquipmentGrade? grade = null)
        {
            Weapon = weapon;
            Grade = grade ?? weapon.Grade;
        }

        public GearItem(ArmorDefinition armor, EquipmentGrade? grade = null)
        {
            Armor = armor;
            Grade = grade ?? armor.Grade;
        }

        public bool IsWeapon => Weapon != null;
        public GearSlot Slot => IsWeapon ? GearSlot.Weapon : Armor.Slot;
        public string BaseName => IsWeapon ? Weapon.DisplayName : Armor.DisplayName;
        public int WearLevel => IsWeapon ? Weapon.WearLevel : Armor.WearLevel;
        public Sprite Icon => IsWeapon ? Weapon.Icon : Armor.Icon;
        public bool IsStartingGear { get; set; } // 시작 무기 — 되팔기 0

        // 표 등급과 다르면 이름 앞에 등급 표시
        public string DisplayName => Grade == (IsWeapon ? Weapon.Grade : Armor.Grade) ? BaseName : $"[{Grade.Label()}] {BaseName}";

        private float RatioToTable => EquipmentGradeTable.Get(Grade).AttackBonusMultiplier
                                      / EquipmentGradeTable.Get(IsWeapon ? Weapon.Grade : Armor.Grade).AttackBonusMultiplier;

        private int Scale(int tableValue) => tableValue <= 0 ? tableValue : Mathf.CeilToInt(tableValue * RatioToTable - 0.0001f);

        public int AttackBonus => IsWeapon ? Weapon.ComputeAttackBonus(Grade) : 0;
        public bool IsMagicWeapon => IsWeapon && Weapon.Category.IsMagic();

        public StatBlock StatBonus => IsWeapon ? Weapon.WearStatBonus : Armor.StatBonus;
        public int PhysicalDefense => IsWeapon ? 0 : Scale(Armor.PhysicalDefense);
        public int MagicDefense => IsWeapon ? 0 : Scale(Armor.MagicDefense);
        public int MaxHPBonus => IsWeapon ? 0 : Scale(Armor.MaxHPBonus);
        public int MaxMPBonus => IsWeapon ? 0 : Scale(Armor.MaxMPBonus);
        public int MaxFocusBonus => IsWeapon ? 0 : Armor.MaxFocusBonus;

        public int Price
        {
            get
            {
                int table = IsWeapon ? Weapon.Price : Armor.Price;
                var tableGrade = IsWeapon ? Weapon.Grade : Armor.Grade;
                float raw = table * EquipmentGradeTable.Get(Grade).PriceMultiplier / EquipmentGradeTable.Get(tableGrade).PriceMultiplier;
                return Mathf.FloorToInt(raw / 5f + 0.5f) * 5; // 가장 가까운 5 Cent, .5는 올림
            }
        }

        public int SellPrice => IsStartingGear ? 0 : Price * 40 / 100;

        // 이 캐릭터가 지금 착용할 수 있는지 (무기 계열 제한 + 착용 레벨). 이유는 UI 표시용.
        public bool CanEquip(PartyMember member, out string reason)
        {
            reason = null;
            if (member.Level < WearLevel) { reason = $"레벨 {WearLevel} 필요"; return false; }
            if (IsWeapon && member.Job.AllowedWeaponCategories != null
                && System.Array.IndexOf(member.Job.AllowedWeaponCategories, Weapon.Category) < 0)
            {
                reason = $"{member.Job.DisplayName}은(는) {Weapon.Category.Label()} 사용 불가";
                return false;
            }
            return true;
        }
    }
}
