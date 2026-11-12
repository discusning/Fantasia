using System.Collections.Generic;
using Fantasia.Combat;
using Fantasia.Data;
using UnityEngine;

namespace Fantasia.UI
{
    // 11/12 팀장 지시: 아이템을 얻을 때·상점·인벤토리에서 무슨 아이템인지 한눈에.
    //  ① 그림: 아이템 자체 그림이 있으면 그것, 없으면 종류 아이콘 (Resources/ItemCategory/Cat_*, Editor/ItemCategoryIconSetup)
    //  ② 등급 색: 노말 흰 · 희귀 파랑 · 에픽 보라 · 유니크 금 · 전설 빨강 · 스페셜 청록 (Docs/Art/AssetList "등급 테두리 6색")
    //  ③ 전투 승리 결과 창: 얻은 아이템 그림 + 등급 색 이름 (BattleHud.ResultItems) — 하나씩 뜨는 알림은 안 씀 (팀장 11/12)
    public static class ItemLook
    {
        public static Color GradeColor(EquipmentGrade g) => g switch
        {
            EquipmentGrade.Rare => new Color(0.42f, 0.7f, 1f),
            EquipmentGrade.Epic => new Color(0.78f, 0.5f, 1f),
            EquipmentGrade.Unique => new Color(1f, 0.82f, 0.32f),
            EquipmentGrade.Legendary => new Color(1f, 0.33f, 0.28f),
            EquipmentGrade.Special => new Color(0.36f, 0.92f, 0.78f),
            _ => new Color(0.93f, 0.91f, 0.86f),
        };

        public static readonly Color ConsumableColor = new Color(0.93f, 0.91f, 0.86f);

        public static EquipmentGrade? GradeOf(object item) => item switch
        {
            GearItem g => g.Grade,
            WeaponDefinition w => w.Grade,
            ArmorDefinition a => a.Grade,
            LootDrop d when d.Gear != null => d.Gear.Grade,
            _ => null,
        };

        public static Color NameColor(object item) => GradeOf(item) is EquipmentGrade g ? GradeColor(g) : ConsumableColor;

        private static readonly Dictionary<string, Sprite> Cache = new();
        public static Sprite Category(string key)
        {
            if (!Cache.TryGetValue(key, out var s)) Cache[key] = s = Resources.Load<Sprite>($"ItemCategory/Cat_{key}");
            return s;
        }

        public static string CategoryKey(object item) => item switch
        {
            GearItem g => g.IsWeapon ? g.Weapon.Category.ToString() : g.Armor.Slot.ToString(),
            WeaponDefinition w => w.Category.ToString(),
            ArmorDefinition a => a.Slot.ToString(),
            ConsumableDefinition c => c.Effect switch
            {
                ConsumableEffect.Escape or ConsumableEffect.Teleport or ConsumableEffect.RuneTicket or ConsumableEffect.WeaponUpgrade => "Scroll",
                ConsumableEffect.None or ConsumableEffect.RepairWeapon or ConsumableEffect.RepairGear or ConsumableEffect.CampRest => "Pouch",
                _ => "Potion",
            },
            LootDrop d => d.Gear != null ? CategoryKey(d.Gear) : CategoryKey(d.Consumable),
            _ => null,
        };

        // 자체 그림 → 없으면 종류 아이콘
        public static Sprite IconOf(object item)
        {
            Sprite own = item switch
            {
                GearItem g => g.Icon,
                WeaponDefinition w => w.Icon,
                ArmorDefinition a => a.Icon,
                ConsumableDefinition c => c.Icon,
                LootDrop d => d.Gear != null ? d.Gear.Icon : d.Consumable != null ? d.Consumable.Icon : null,
                _ => null,
            };
            if (own != null) return own;
            string key = CategoryKey(item);
            return key != null ? Category(key) : null;
        }

        // 등급이 있으면 "[전설] 이름"으로 보이게 (GearItem.DisplayName은 기본 등급이면 이름만)
        public static string NameWithGrade(object item)
        {
            switch (item)
            {
                case GearItem g: return $"[{g.Grade.Label()}] {g.BaseName}";
                case LootDrop d when d.Gear != null: return NameWithGrade(d.Gear);
                case LootDrop d: return d.Name;
                case ConsumableDefinition c: return c.DisplayName;
                default: return item?.ToString() ?? "";
            }
        }

        // 결과 창 등 여러 줄 글 속 이름에 등급 색
        public static string Rich(object item) => $"<color=#{ColorUtility.ToHtmlStringRGB(NameColor(item))}>{NameWithGrade(item)}</color>";
    }
}
