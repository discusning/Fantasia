using System.Collections.Generic;
using System.Linq;
using Fantasia.Combat;
using Fantasia.Data;
using Fantasia.Items;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D17·D19 검증: 장비 등급 환산·착용·인벤토리 상한·소모품 효과·전리품 분포를 System.md 수치와 대조.
    // 먼저 Fantasia/Data/Seed Sample Game Data (ItemDataSeed 포함) 실행.
    public static class ItemSelfTest
    {
        [MenuItem("Fantasia/Data/Run Item Self-Test")]
        public static void Run()
        {
            var catalog = ItemCatalog.Current;
            var detectiveJob = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_명탐정.asset");
            var doctorJob = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_역병의사.asset");
            if (catalog == null || detectiveJob == null || doctorJob == null)
            {
                Debug.LogError("[ItemSelfTest] 카탈로그·직업 자산 없음 — 먼저 Seed Sample Game Data 실행.");
                return;
            }

            int failures = 0;
            failures += CheckGearGrades(catalog);
            failures += CheckEquip(catalog, detectiveJob);
            failures += CheckInventoryLimits(catalog);
            failures += CheckConsumables(catalog, detectiveJob, doctorJob);
            failures += CheckLoot(catalog, detectiveJob, doctorJob);

            if (failures == 0) Debug.Log("[ItemSelfTest] 전부 통과.");
            else Debug.LogError($"[ItemSelfTest] {failures}건 실패. 위 로그 확인할 것.");
        }

        private static WeaponDefinition W(ItemCatalog c, string name) => c.Weapons.First(w => w.DisplayName == name);
        private static ArmorDefinition A(ItemCatalog c, string name) => c.Armors.First(a => a.DisplayName == name);
        private static ConsumableDefinition C(ItemCatalog c, string id) => c.Consumable(id);

        // 무기: ceil((B + 8×floor((Lv−1)/3)) × R) / 방어구: 방어·HP만 R 비율로 올림 / 가격 × Q 비율 5 Cent 반올림
        private static int CheckGearGrades(ItemCatalog c)
        {
            int e = 0;
            var dagger = W(c, "탐정의 접이식 단검");
            e += Expect("단검 노말 공격 보너스", new GearItem(dagger).AttackBonus, 5);
            e += Expect("단검 희귀(전리품) 공격 보너스 ceil(5×1.15)", new GearItem(dagger, EquipmentGrade.Rare).AttackBonus, 6);
            e += Expect("단검 전설 공격 보너스 ceil(5×1.9)", new GearItem(dagger, EquipmentGrade.Legendary).AttackBonus, 10);
            e += Expect("단검 희귀 착용 보정 힘(등급으로 안 곱함)", new GearItem(dagger, EquipmentGrade.Rare).StatBonus.Strength, 2);
            e += Expect("단검 희귀 가격 70×1.5=105", new GearItem(dagger, EquipmentGrade.Rare).Price, 105);

            var coat = A(c, "인부의 두꺼운 코트");
            var rareCoat = new GearItem(coat, EquipmentGrade.Rare);
            e += Expect("코트 희귀 물리 방어 ceil(2×1.15)", rareCoat.PhysicalDefense, 3);
            e += Expect("코트 희귀 최대 HP ceil(10×1.15)", rareCoat.MaxHPBonus, 12);
            e += Expect("코트 희귀 가격 55×1.5=82.5→85", rareCoat.Price, 85);
            e += Expect("코트 되팔기 40% 버림", rareCoat.SellPrice, 34);
            return e;
        }

        private static int CheckEquip(ItemCatalog c, JobDefinition detective)
        {
            int e = 0;
            var party = new Party();
            var m = new PartyMember(detective);
            party.AddMember(m);
            var inv = party.Inventory;
            int baseHp = m.MaxHP, baseDef = m.FinalPhysicalDefense, baseStr = m.FinalStats.Strength;

            var coat = new GearItem(A(c, "인부의 두꺼운 코트"));
            inv.AddGear(coat);
            e += ExpectTrue("코트 착용", m.TryEquip(coat, inv, out _));
            e += Expect("코트 착용 후 최대 HP +10", m.MaxHP, baseHp + 10);
            e += Expect("코트 착용 후 물리 방어 +2", m.FinalPhysicalDefense, baseDef + 2);
            e += Expect("착용하면 인벤토리에서 빠짐", inv.Gear.Count, 0);
            e += Expect("최대 HP가 늘어도 현재 HP는 그대로", m.CurrentHP, baseHp);

            var ring = new GearItem(A(c, "구리 힘의 반지"));
            inv.AddGear(ring);
            m.TryEquip(ring, inv, out _);
            e += Expect("반지 힘 +3", m.FinalStats.Strength, baseStr + 3);

            var saber = new GearItem(W(c, "야경대 세이버"));
            inv.AddGear(saber);
            e += ExpectTrue("명탐정은 검·세이버 착용 가능", m.TryEquip(saber, inv, out _));
            e += ExpectTrue("바꾼 단검은 인벤토리로", inv.Gear.Any(g => g.BaseName == "탐정의 접이식 단검"));

            var staff = new GearItem(W(c, "검역소 지팡이"));
            e += ExpectTrue("명탐정은 스태프 착용 불가", !staff.CanEquip(m, out _));
            var rareDagger = new GearItem(W(c, "은도금 해부단검"));
            e += ExpectTrue("레벨 4 장비는 레벨 1이 착용 불가", !rareDagger.CanEquip(m, out _));

            e += ExpectTrue("반지 해제", m.TryUnequip(GearSlot.Ring, inv, out _));
            e += Expect("반지 해제 후 힘 = 기본 − 단검 2 + 세이버 4", m.FinalStats.Strength, baseStr + 2);
            return e;
        }

        private static int CheckInventoryLimits(ItemCatalog c)
        {
            int e = 0;
            var inv = new PartyInventory();
            var bandage = C(c, ConsumableIds.Bandage);
            e += Expect("붕대 12개 넣으면 10개만", inv.AddConsumable(bandage, 12), 10);
            e += Expect("붕대 더 안 들어감", inv.AddConsumable(bandage), 0);
            var reagent = C(c, ConsumableIds.Reagent);
            e += Expect("시약은 30개까지", inv.AddConsumable(reagent, 40), 30);
            var coat = A(c, "인부의 두꺼운 코트");
            for (int i = 0; i < PartyInventory.MaxGear; i++) inv.AddGear(new GearItem(coat));
            e += ExpectTrue("장비 20개면 가득", !inv.AddGear(new GearItem(coat)));
            return e;
        }

        private static int CheckConsumables(ItemCatalog c, JobDefinition detective, JobDefinition doctor)
        {
            int e = 0;
            var party = new Party();
            var m = new PartyMember(detective);
            var d = new PartyMember(doctor);
            party.AddMember(m);
            party.AddMember(d);
            party.GiveStartingItems(c);
            var bandage = C(c, ConsumableIds.Bandage);
            e += Expect("시작 붕대 = 인원수", party.Inventory.CountOf(bandage), 2);
            e += ExpectTrue("시작 붕대는 퀵 슬롯 1번", m.QuickSlots[0] == bandage);

            e += ExpectTrue("HP 가득이면 붕대 못 씀", !ConsumableRules.CanUseOn(bandage, m, out _));
            m.TakeDamage(30);
            var used = new HashSet<PartyMember>();
            int ambush = 0;
            var r = ConsumableRules.UseInWorld(bandage, m, party, used, ref ambush);
            e += ExpectTrue("월드 붕대 사용", r.Used);
            e += Expect("붕대 HP +20", m.CurrentHP, m.MaxHP - 10);
            e += Expect("붕대 1개 차감", party.Inventory.CountOf(bandage), 1);
            var again = ConsumableRules.UseInWorld(bandage, m, party, used, ref ambush);
            e += ExpectTrue("같은 아군 턴에 같은 사람 두 번째 사용 불가", !again.Used);

            var mana = C(c, ConsumableIds.ManaElixir);
            e += ExpectTrue("MP 없는 직업엔 마력 영약 불가", !ConsumableRules.CanUseOn(mana, m, out _));

            var smoke = c.Consumables.First(x => x.Effect == ConsumableEffect.AmbushBonus);
            party.Inventory.AddConsumable(smoke);
            var s = ConsumableRules.UseInWorld(smoke, m, party, used, ref ambush);
            e += ExpectTrue("연막병 사용", s.Used);
            e += Expect("다음 기습 +15%p", ambush, 15);
            return e;
        }

        private static int CheckLoot(ItemCatalog c, JobDefinition detective, JobDefinition doctor)
        {
            int e = 0;
            var party = new Party();
            party.AddMember(new PartyMember(detective));
            party.AddMember(new PartyMember(doctor));
            var small = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_역병쥐.asset");
            if (small == null) { Debug.LogError("[ItemSelfTest] 역병 쥐 자산 없음"); return 1; }
            var rng = new System.Random(1234);

            var slotCounts = new int[5];
            int minCent = int.MaxValue, maxCent = 0, items = 0, gear = 0;
            const int runs = 20000;
            var enemies = new List<EnemyDefinition> { small, small };
            for (int i = 0; i < runs; i++)
            {
                var rw = LootRules.Roll(enemies, party, c, rng);
                slotCounts[rw.SlotCount]++;
                minCent = Mathf.Min(minCent, rw.Cent);
                maxCent = Mathf.Max(maxCent, rw.Cent);
                items += rw.Items.Count;
                gear += rw.Items.Count(x => x.Gear != null);
                if (i == 0) e += Expect("소형 2마리 경험치 25×2", rw.Experience, 50);
            }
            e += ExpectNear("슬롯 1칸 15%", slotCounts[1] / (float)runs, 0.15f, 0.015f);
            e += ExpectNear("슬롯 2칸 50%", slotCounts[2] / (float)runs, 0.50f, 0.015f);
            e += ExpectNear("슬롯 3칸 25%", slotCounts[3] / (float)runs, 0.25f, 0.015f);
            e += ExpectNear("슬롯 4칸 10%", slotCounts[4] / (float)runs, 0.10f, 0.015f);
            e += ExpectTrue($"소형 2마리 Cent 40~70 (실제 {minCent}~{maxCent})", minCent >= 40 && maxCent <= 70);
            e += ExpectNear("추가 아이템 기대 1.30개", items / (float)runs, 1.30f, 0.04f);
            e += ExpectNear("장비 비율 44% (무기20·방어구10·반지7·목걸이7)", gear / (float)items, 0.44f, 0.02f);

            var boss = ScriptableObject.CreateInstance<EnemyDefinition>();
            boss.Size = EnemySize.Boss;
            boss.FixedRewardCent = 180;
            boss.FixedRewardExp = 100;
            var br = LootRules.Roll(new List<EnemyDefinition> { boss }, party, c, rng);
            e += Expect("보스 4칸 고정", br.SlotCount, 4);
            e += Expect("보스 고정 Cent", br.Cent, 180);
            Object.DestroyImmediate(boss);

            // 무기 기본형: 구역 레벨 1의 노말 기본형만
            for (int i = 0; i < 200; i++)
            {
                var w = LootRules.RollWeaponBase(false, party, c, rng);
                if (w.WearLevel != 1 || w.ExcludeFromLoot) { e += Fail($"구역 1 무기 기본형이 아님: {w.DisplayName}"); break; }
            }

            // 지급: Cent·경험치·인벤토리
            int centBefore = party.Cent;
            var reward = new BattleRewards { Cent = 30, Experience = 25 };
            reward.Items.Add(new LootDrop(C(c, ConsumableIds.LifeElixir)));
            LootRules.Apply(reward, party);
            e += Expect("Cent 지급", party.Cent, centBefore + 30);
            e += Expect("경험치 지급", party.Members[0].Experience, 25);
            e += Expect("영약 지급", party.Inventory.CountOf(C(c, ConsumableIds.LifeElixir)), 1);
            return e;
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[ItemSelfTest] {label}: 기대 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectTrue(string label, bool condition) => condition ? 0 : Fail(label);

        private static int ExpectNear(string label, float actual, float expected, float tolerance)
        {
            if (Mathf.Abs(actual - expected) <= tolerance) return 0;
            Debug.LogError($"[ItemSelfTest] {label}: 기대 {expected:0.000}±{tolerance}, 실제 {actual:0.000}");
            return 1;
        }

        private static int Fail(string label)
        {
            Debug.LogError($"[ItemSelfTest] 실패: {label}");
            return 1;
        }
    }
}
