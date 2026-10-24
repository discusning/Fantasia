using System;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Data;

namespace Fantasia.Combat
{
    // 전리품 한 칸: 장비 또는 소모품 1개
    public readonly struct LootDrop
    {
        public readonly GearItem Gear;
        public readonly ConsumableDefinition Consumable;
        public LootDrop(GearItem gear) { Gear = gear; Consumable = null; }
        public LootDrop(ConsumableDefinition consumable) { Gear = null; Consumable = consumable; }
        public string Name => Gear != null ? Gear.DisplayName : Consumable != null ? Consumable.DisplayName : "?";
    }

    public class BattleRewards
    {
        public int Cent;
        public int Experience;                       // 생존 캐릭터 1명당
        public int SlotCount;                        // Cent 슬롯 포함 1~4
        public List<LootDrop> Items = new();         // Cent 외 추가 칸
        public List<LootDrop> Overflow = new();      // 인벤토리가 차서 못 넣은 것 — 전리품 창이 버리기·교체를 묻는다
    }

    // D19 승리 보상 (System.md "승리 보상·아이템 종류·패배 약탈 확률표" + "수입·가격 책정 근거").
    // - 총 슬롯 1~4 (15/50/25/10), 첫 칸은 항상 Cent. 보스가 있으면 4칸 고정.
    // - 추가 칸은 칸마다 독립으로 종류표(합 100)에서 뽑는다. 행운은 영향 없음.
    // - 장비는 종류 → 기본형 → 등급(50/30/15/2/1/2) 순서. 구역 착용 레벨 유지.
    public static class LootRules
    {
        // 지금 구역의 기본 장비 착용 레벨 (1막 1, 2막 4, 고성 7). 지역 설계가 오면 보드가 정한다.
        public static int CurrentZoneWearLevel = 1;

        public static readonly (int slots, int weight)[] SlotTable = { (1, 15), (2, 50), (3, 25), (4, 10) };

        public enum DropKind { PhysicalWeapon, MagicWeapon, Armor, Ring, Necklace, Consumable }

        // 추가 칸 종류표 — 소모품 행은 ConsumableIds
        public static readonly (DropKind kind, string consumableId, int weight)[] KindTable =
        {
            (DropKind.PhysicalWeapon, null, 10),
            (DropKind.MagicWeapon, null, 10),
            (DropKind.Armor, null, 10),
            (DropKind.Ring, null, 7),
            (DropKind.Necklace, null, 7),
            (DropKind.Consumable, ConsumableIds.RuneTicket, 3),
            (DropKind.Consumable, ConsumableIds.Teleport, 5),
            (DropKind.Consumable, ConsumableIds.Escape, 5),
            (DropKind.Consumable, ConsumableIds.LifeElixir, 10),
            (DropKind.Consumable, ConsumableIds.ManaElixir, 10),
            (DropKind.Consumable, ConsumableIds.MarieDeed, 1),
            (DropKind.Consumable, ConsumableIds.FocusCandle, 6),
            (DropKind.Consumable, ConsumableIds.Antidote, 5),
            (DropKind.Consumable, ConsumableIds.Bandage, 4),
            (DropKind.Consumable, ConsumableIds.RepairKit, 3),
            (DropKind.Consumable, ConsumableIds.Reagent, 4),
        };

        // 적 1마리 Cent·경험치 — 소형 20~35/25, 중형 50~75/55, 대형 100~150/100, 고정값이 있으면 그 값.
        public static int RollEnemyCent(EnemyDefinition enemy, Random rng)
        {
            if (enemy.FixedRewardCent > 0) return enemy.FixedRewardCent;
            return enemy.Size switch
            {
                EnemySize.Small => rng.Next(20, 36),
                EnemySize.Medium => rng.Next(50, 76),
                _ => rng.Next(100, 151),
            };
        }

        public static int EnemyExp(EnemyDefinition enemy)
        {
            if (enemy.FixedRewardExp > 0) return enemy.FixedRewardExp;
            return enemy.Size switch
            {
                EnemySize.Small => 25,
                EnemySize.Medium => 55,
                _ => 100,
            };
        }

        public static int RollSlotCount(bool hasBoss, Random rng) => hasBoss ? 4 : Pick(SlotTable, e => e.weight, rng).slots;

        // 보상을 굴리기만 한다 (지급은 Apply)
        public static BattleRewards Roll(IReadOnlyList<EnemyDefinition> enemies, Party party, ItemCatalog catalog, Random rng)
        {
            var rewards = new BattleRewards();
            foreach (var e in enemies)
            {
                rewards.Cent += RollEnemyCent(e, rng);
                rewards.Experience += EnemyExp(e);
            }
            rewards.SlotCount = RollSlotCount(enemies.Any(e => e.Size == EnemySize.Boss), rng);
            if (catalog == null) return rewards;
            for (int i = 1; i < rewards.SlotCount; i++)
            {
                var drop = RollDrop(party, catalog, rng);
                if (drop.HasValue) rewards.Items.Add(drop.Value);
            }
            return rewards;
        }

        // Cent·경험치(살아 있는 파티원)·아이템을 지급. 인벤토리에 못 넣은 아이템은 Overflow로.
        public static void Apply(BattleRewards rewards, Party party)
        {
            party.AddCent(rewards.Cent);
            party.GrantExperienceToSurvivors(rewards.Experience);
            rewards.Overflow.Clear();
            foreach (var item in rewards.Items)
                if (!TryAdd(party.Inventory, item)) rewards.Overflow.Add(item);
        }

        public static bool TryAdd(PartyInventory inventory, LootDrop drop) =>
            drop.Gear != null ? inventory.AddGear(drop.Gear) : inventory.AddConsumable(drop.Consumable) == 1;

        public static LootDrop? RollDrop(Party party, ItemCatalog catalog, Random rng)
        {
            var row = Pick(KindTable, e => e.weight, rng);
            switch (row.kind)
            {
                case DropKind.Consumable:
                    var c = catalog.Consumable(row.consumableId);
                    return c != null ? new LootDrop(c) : (LootDrop?)null;
                case DropKind.PhysicalWeapon:
                case DropKind.MagicWeapon:
                    var w = RollWeaponBase(row.kind == DropKind.MagicWeapon, party, catalog, rng);
                    return w != null ? new LootDrop(new GearItem(w, RollGrade(rng))) : (LootDrop?)null;
                default:
                    var a = RollArmorBase(row.kind, catalog, rng);
                    return a != null ? new LootDrop(new GearItem(a, RollGrade(rng))) : (LootDrop?)null;
            }
        }

        // 구역에 나온 기본형 중 70%는 생존 파티가 쓸 수 있는 계열, 30%는 같은 피해 분류 전체에서 균등.
        // 그 분류를 쓰는 캐릭터가 없으면 전체에서.
        public static WeaponDefinition RollWeaponBase(bool magic, Party party, ItemCatalog catalog, Random rng)
        {
            var pool = catalog.Weapons.Where(w => w != null && !w.ExcludeFromLoot && w.Category.IsMagic() == magic
                                                  && w.WearLevel == CurrentZoneWearLevel).ToList();
            if (pool.Count == 0) return null;
            var usable = party == null ? new HashSet<WeaponCategory>() : new HashSet<WeaponCategory>(
                party.LivingMembers.SelectMany(m => m.Job.AllowedWeaponCategories ?? Array.Empty<WeaponCategory>()));
            var usablePool = pool.Where(w => usable.Contains(w.Category)).ToList();
            var from = usablePool.Count > 0 && rng.NextDouble() < 0.7 ? usablePool : pool;
            return from[rng.Next(from.Count)];
        }

        // 해당 구역 이하 착용 레벨의 기본형에서 균등 (방어구 = 몸통·머리 중 균등 → 그 부위 목록)
        public static ArmorDefinition RollArmorBase(DropKind kind, ItemCatalog catalog, Random rng)
        {
            GearSlot slot = kind switch
            {
                DropKind.Ring => GearSlot.Ring,
                DropKind.Necklace => GearSlot.Necklace,
                _ => rng.Next(2) == 0 ? GearSlot.Body : GearSlot.Head,
            };
            var pool = catalog.Armors.Where(a => a != null && !a.ExcludeFromLoot && a.Slot == slot && a.WearLevel <= CurrentZoneWearLevel).ToList();
            return pool.Count > 0 ? pool[rng.Next(pool.Count)] : null;
        }

        public static EquipmentGrade RollGrade(Random rng)
        {
            var grades = (EquipmentGrade[])Enum.GetValues(typeof(EquipmentGrade));
            return Pick(grades, g => EquipmentGradeTable.Get(g).DrawWeight, rng);
        }

        private static T Pick<T>(IReadOnlyList<T> rows, Func<T, int> weight, Random rng)
        {
            int total = 0;
            foreach (var r in rows) total += weight(r);
            int roll = rng.Next(total);
            foreach (var r in rows)
            {
                if (roll < weight(r)) return r;
                roll -= weight(r);
            }
            return rows[rows.Count - 1];
        }
    }
}
