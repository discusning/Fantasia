using System.Collections.Generic;
using System.IO;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // System.md 아이템 표 → ScriptableObject 자산.
    // 무기: "무기 > 일반 무기 목록 · 30종"·"스토리 고유 무기 · 6종" (계열 고정값은 무기 계열 표)
    // 방어구: "방어구와 장신구 · 17종", 소모품: "소모품·이동권·재료 · 29종".
    // 결과는 Resources/ItemCatalog.asset에 표 순서대로 모은다.
    public static class ItemDataSeed
    {
        private const string WeaponDir = "Assets/_Project/Data/Weapons";
        private const string ArmorDir = "Assets/_Project/Data/Armor";
        private const string ConsumableDir = "Assets/_Project/Data/Items/Consumables";
        private const string CatalogDir = "Assets/_Project/Resources";
        private const string WeakBleedPath = "Assets/_Project/Data/StatusEffects/Status_약한출혈.asset";

        [MenuItem("Fantasia/Data/Seed Item Data")]
        public static void SeedAll()
        {
            var weapons = SeedWeapons();
            var armors = SeedArmors();
            var consumables = SeedConsumables();

            var catalog = LoadOrCreate<ItemCatalog>(CatalogDir, "ItemCatalog");
            catalog.Weapons = weapons.ToArray();
            catalog.Armors = armors.ToArray();
            catalog.Consumables = consumables.ToArray();
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            Debug.Log($"[ItemDataSeed] 아이템 자산 생성/갱신 완료 — 무기 {weapons.Count} · 방어구/장신구 {armors.Count} · 소모품 {consumables.Count}");
        }

        // 새 폴더(Armor·Items/Consumables·Resources)는 AssetDatabase에도 등록해야 CreateAsset이 실패하지 않는다.
        private static void EnsureFolder(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
        }

        private static T LoadOrCreate<T>(string dir, string name) where T : ScriptableObject
        {
            EnsureFolder(dir);
            string path = $"{dir}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static StatBlock Stats(int str = 0, int spd = 0, int intel = 0, int luck = 0, int perc = 0)
            => new StatBlock(strength: str, speed: spd, vitality: 0, intelligence: intel, luck: luck, perception: perc);

        // ───────────── 무기 ─────────────

        // 무기 계열 표 한 행 (슬롯·B·착용 보정·기본 공격 MP·크리티컬 효과·사용 직업)
        private sealed class Family
        {
            public int Slots;
            public int B;
            public StatBlock Wear;
            public string WearDesc;
            public int MaxMP;
            public int MPCost;
            public string Crit;
            public JobId[] Jobs;
            public StatusEffectDefinition[] CritStatuses = System.Array.Empty<StatusEffectDefinition>();
            public TurnShift CritShift = TurnShift.None;
        }

        private static Dictionary<WeaponCategory, Family> BuildFamilies()
        {
            var weakBleed = AssetDatabase.LoadAssetAtPath<StatusEffectDefinition>(WeakBleedPath);
            if (weakBleed == null)
                Debug.LogWarning($"[ItemDataSeed] {WeakBleedPath} 없음 — 단검 크리티컬 출혈 연결을 건너뛴다 (GameDataSeed 먼저 실행).");

            return new Dictionary<WeaponCategory, Family>
            {
                [WeaponCategory.Dagger] = new Family
                {
                    Slots = 2, B = 5, Wear = Stats(str: 2, spd: 3), WearDesc = "힘 +2, 속도 +3", MPCost = 0,
                    Crit = "크리티컬 시 약한 출혈 3턴",
                    Jobs = new[] { JobId.Detective, JobId.Assassin, JobId.Gambler },
                    CritStatuses = weakBleed != null ? new[] { weakBleed } : System.Array.Empty<StatusEffectDefinition>(),
                },
                [WeaponCategory.SwordSaber] = new Family
                {
                    Slots = 3, B = 6, Wear = Stats(str: 4), WearDesc = "힘 +4", MPCost = 0,
                    Crit = "크리티컬 시 물리 방어 −3, 2턴", // 상태 자산 없음 — 설명만
                    Jobs = new[] { JobId.Detective, JobId.Gambler },
                },
                [WeaponCategory.Greatsword] = new Family
                {
                    Slots = 4, B = 10, Wear = Stats(str: 5, spd: -4), WearDesc = "힘 +5, 속도 −4", MPCost = 0,
                    Crit = "크리티컬 시 방해함",
                    Jobs = new[] { JobId.Detective, JobId.Inquisitor, JobId.Gambler },
                    CritShift = TurnShift.Delay, // [방해함]
                },
                [WeaponCategory.Polearm] = new Family
                {
                    Slots = 3, B = 8, Wear = Stats(perc: 4), WearDesc = "인지 +4", MPCost = 0,
                    Crit = "일반 공격에서 방어력 25% 관통", // 크리티컬 효과가 아니라 상시 관통 — 설명 칸에만 둔다
                    Jobs = new[] { JobId.Gambler }, // "도박가; 확장 직업용"
                },
                [WeaponCategory.Bow] = new Family
                {
                    Slots = 3, B = 6, Wear = Stats(perc: 5, spd: 2), WearDesc = "인지 +5, 속도 +2", MPCost = 0,
                    Crit = "크리티컬 시 속도 −10, 2턴",
                    Jobs = new[] { JobId.Sniper, JobId.Gambler },
                },
                [WeaponCategory.Pistol] = new Family
                {
                    Slots = 2, B = 7, Wear = Stats(perc: 4), WearDesc = "인지 +4", MPCost = 0,
                    Crit = "기본 공격은 추가 상태 효과 없음; 높은 크리티컬 빈도",
                    Jobs = new[] { JobId.Sniper, JobId.Gambler },
                },
                [WeaponCategory.Musket] = new Family
                {
                    Slots = 4, B = 10, Wear = Stats(perc: 6, spd: -5), WearDesc = "인지 +6, 속도 −5", MPCost = 0,
                    Crit = "일반 공격에서 방어력 50% 관통",
                    Jobs = new[] { JobId.Sniper, JobId.Gambler },
                },
                [WeaponCategory.Staff] = new Family
                {
                    Slots = 3, B = 6, Wear = Stats(intel: 4), WearDesc = "지력 +4, 최대 MP +8", MaxMP = 8, MPCost = 4,
                    Crit = "크리티컬 시 마법 방어 −3, 2턴",
                    Jobs = new[] { JobId.PlagueDoctor, JobId.Alchemist, JobId.Gambler },
                },
                [WeaponCategory.Grimoire] = new Family
                {
                    Slots = 4, B = 8, Wear = Stats(intel: 5), WearDesc = "지력 +5, 최대 MP +12", MaxMP = 12, MPCost = 6,
                    Crit = "크리티컬 시 화상 2턴", // 화상 상태 자산 없음
                    Jobs = new[] { JobId.PlagueDoctor, JobId.Alchemist, JobId.Gambler },
                },
                [WeaponCategory.Instrument] = new Family
                {
                    Slots = 2, B = 4, Wear = Stats(intel: 3, spd: 3), WearDesc = "지력 +3, 속도 +3", MPCost = 3,
                    Crit = "크리티컬 시 자신의 HP 5 회복",
                    Jobs = new[] { JobId.Conductor, JobId.Gambler },
                },
            };
        }

        private static List<WeaponDefinition> SeedWeapons()
        {
            var fam = BuildFamilies();
            var list = new List<WeaponDefinition>();
            const EquipmentGrade N = EquipmentGrade.Normal, R = EquipmentGrade.Rare, E = EquipmentGrade.Epic;

            void W(string id, string name, WeaponCategory cat, EquipmentGrade grade, int level, int tableBonus, int price,
                string unique = null)
            {
                var f = fam[cat];
                var w = LoadOrCreate<WeaponDefinition>(WeaponDir, "Weapon_" + name.Replace(" ", ""));
                w.Id = id;
                w.DisplayName = name;
                w.Category = cat;
                w.SlotCount = f.Slots;
                w.BaseBonusB = f.B;
                w.BaseAttackMPCost = f.MPCost;
                w.WearStatBonus = f.Wear;
                w.WearStatBonusDescription = f.WearDesc;
                w.MaxMPBonus = f.MaxMP;
                // 고유 효과는 계열 효과 뒤에 "고유: " 줄로 붙인다 (계열 크리티컬 효과도 그대로 적용되므로)
                w.CritEffectDescription = unique == null ? f.Crit : $"{f.Crit}\n고유: {unique}";
                w.OnCritStatuses = f.CritStatuses;
                w.OnCritTurnShift = f.CritShift;
                w.Grade = grade;
                w.WearLevel = level;
                w.Price = price;
                w.AllowedJobs = f.Jobs;
                w.ExcludeFromLoot = unique != null;

                int computed = w.ComputeAttackBonus();
                if (computed != tableBonus)
                    Debug.LogWarning($"[ItemDataSeed] {name}: 표 공격 보너스 {tableBonus} ≠ 공식 {computed}");

                EditorUtility.SetDirty(w);
                list.Add(w);
            }

            // 일반 무기 30종 (표 순서). 공격 보너스는 30종 모두 공식 ceil((B + 8×floor((lvl−1)/3)) × R)과 일치.
            W("weapon.dagger.detective_folding", "탐정의 접이식 단검", WeaponCategory.Dagger, N, 1, 5, 70);
            W("weapon.dagger.silver_scalpel", "은도금 해부단검", WeaponCategory.Dagger, R, 4, 15, 215);
            W("weapon.dagger.black_rose", "검은 장미 단검", WeaponCategory.Dagger, E, 7, 29, 500);
            W("weapon.sword.nightwatch_saber", "야경대 세이버", WeaponCategory.SwordSaber, N, 1, 6, 80);
            W("weapon.sword.silver_inquisitor", "백은 수사검", WeaponCategory.SwordSaber, R, 4, 17, 245);
            W("weapon.sword.eclipse_rune", "월식의 룬검", WeaponCategory.SwordSaber, E, 7, 30, 570);
            W("weapon.greatsword.cult_hunter", "교단 사냥 대검", WeaponCategory.Greatsword, N, 1, 10, 95);
            W("weapon.greatsword.holy_steel", "성유강 대검", WeaponCategory.Greatsword, R, 4, 21, 290);
            W("weapon.greatsword.final_executioner", "종언의 처형검", WeaponCategory.Greatsword, E, 7, 36, 675);
            W("weapon.polearm.garrison_spear", "수비대 장창", WeaponCategory.Polearm, N, 1, 8, 85);
            W("weapon.polearm.rune_halberd", "룬각인 미늘창", WeaponCategory.Polearm, R, 4, 19, 260);
            W("weapon.polearm.gargoyle_breaker", "가고일 파쇄창", WeaponCategory.Polearm, E, 7, 33, 605);
            W("weapon.bow.fog_hunter", "안개 사냥 활", WeaponCategory.Bow, N, 1, 6, 75);
            W("weapon.bow.alchemy_crossbow", "연금술 석궁", WeaponCategory.Bow, R, 4, 17, 230);
            W("weapon.bow.silver_awl_crossbow", "은빛 송곳 석궁", WeaponCategory.Bow, E, 7, 30, 535);
            W("weapon.pistol.flintlock", "부싯돌 권총", WeaponCategory.Pistol, N, 1, 7, 90);
            W("weapon.pistol.silver_investigator", "은탄 수사권총", WeaponCategory.Pistol, R, 4, 18, 275);
            W("weapon.pistol.rune_trigger", "룬 격발 권총", WeaponCategory.Pistol, E, 7, 32, 640);
            W("weapon.musket.nightwatch_matchlock", "야경대 화승총", WeaponCategory.Musket, N, 1, 10, 105);
            W("weapon.musket.scope_rifle", "눈금 조준 장총", WeaponCategory.Musket, R, 4, 21, 325);
            W("weapon.musket.noble_hunter", "귀족 사냥 장총", WeaponCategory.Musket, E, 7, 36, 750);
            W("weapon.staff.quarantine", "검역소 지팡이", WeaponCategory.Staff, N, 1, 6, 85);
            W("weapon.staff.alchemy_rune", "연금술 룬 스태프", WeaponCategory.Staff, R, 4, 17, 260);
            W("weapon.staff.white_night_sanctum", "백야의 성역봉", WeaponCategory.Staff, E, 7, 30, 605);
            W("weapon.grimoire.plague_primer", "초급 역병 처방서", WeaponCategory.Grimoire, N, 1, 8, 95);
            W("weapon.grimoire.forbidden_anatomy", "금지된 해부학", WeaponCategory.Grimoire, R, 4, 19, 290);
            W("weapon.grimoire.sealed_alchemy", "봉인된 연금술서", WeaponCategory.Grimoire, E, 7, 33, 675);
            W("weapon.instrument.street_violin", "거리악사의 바이올린", WeaponCategory.Instrument, N, 1, 4, 80);
            W("weapon.instrument.silver_memorial_violin", "은현 추모 바이올린", WeaponCategory.Instrument, R, 4, 14, 245);
            W("weapon.instrument.requiem_music_box", "레퀴엠의 오르골", WeaponCategory.Instrument, E, 7, 27, 570);

            // 스토리 고유 무기 6종 — 상점 판매·되팔기 불가라 Price 0, 랜덤 전리품 제외.
            W("weapon.dagger.case_closed", "사건종결의 칼날", WeaponCategory.Dagger, EquipmentGrade.Unique, 7, 34, 0,
                "분석된 적에게 직접 공격 피해 +15%; 약점 간파와 연결");
            W("weapon.grimoire.black_quarantine", "검은 검역의 서", WeaponCategory.Grimoire, EquipmentGrade.Unique, 7, 39, 0,
                "응급 봉합의 기본 회복량 +10; 조제 숙련의 대상과 구분");
            W("weapon.staff.red_bishop", "붉은 주교의 지팡이", WeaponCategory.Staff, EquipmentGrade.Unique, 5, 23, 0,
                "교단 인간형 적에게 직접 공격 피해 +15%");
            W("weapon.musket.fog_tracker", "안개추적 장총", WeaponCategory.Musket, EquipmentGrade.Unique, 7, 42, 0,
                "영점 조정의 정확도 증가를 +20%p로 대체");
            W("weapon.greatsword.soul_reaper", "영혼회수자의 낫", WeaponCategory.Greatsword, EquipmentGrade.Legendary, 9, 50, 0,
                "크리티컬 후 자신의 죽음의 표식 제거; 공격당 1회");
            W("weapon.staff.castle_core", "고성 코어 스태프", WeaponCategory.Staff, EquipmentGrade.Special, 8, 36, 0,
                "고유 기술 영혼 방벽: MP 18·쿨다운 4턴·모든 아군의 양쪽 방어 +5, 2턴");

            return list;
        }

        // ───────────── 방어구·장신구 ─────────────

        private static List<ArmorDefinition> SeedArmors()
        {
            var list = new List<ArmorDefinition>();
            const EquipmentGrade N = EquipmentGrade.Normal, R = EquipmentGrade.Rare, E = EquipmentGrade.Epic;

            void A(string id, string name, GearSlot slot, EquipmentGrade grade, int level, int price,
                int pDef = 0, int mDef = 0, int hp = 0, int mp = 0, StatBlock stats = default, int focus = 0, string unique = "")
            {
                var a = LoadOrCreate<ArmorDefinition>(ArmorDir, "Armor_" + name.Replace(" ", ""));
                a.Id = id;
                a.DisplayName = name;
                a.Slot = slot;
                a.Grade = grade;
                a.WearLevel = level;
                a.Price = price;
                a.PhysicalDefense = pDef;
                a.MagicDefense = mDef;
                a.MaxHPBonus = hp;
                a.MaxMPBonus = mp;
                a.StatBonus = stats;
                a.MaxFocusBonus = focus;
                a.UniqueEffectDescription = unique;
                a.ExcludeFromLoot = false;
                EditorUtility.SetDirty(a);
                list.Add(a);
            }

            // 몸통
            A("armor.body.laborer_coat", "인부의 두꺼운 코트", GearSlot.Body, N, 1, 55, pDef: 2, hp: 10);
            A("armor.body.investigator_leather", "수사대 가죽 코트", GearSlot.Body, R, 4, 180, pDef: 6, mDef: 3, stats: Stats(spd: 3));
            A("armor.body.cult_hunter_plate", "교단 사냥 갑주", GearSlot.Body, E, 7, 420, pDef: 14, mDef: 5, stats: Stats(spd: -8));
            A("armor.body.quarantine_overcoat", "검역소 방호 외투", GearSlot.Body, R, 4, 190, pDef: 3, mDef: 8, stats: Stats(intel: 3));
            A("armor.body.silver_veil_robe", "은실 장막 로브", GearSlot.Body, E, 7, 450, pDef: 5, mDef: 14, stats: Stats(intel: 5));
            // 머리
            A("armor.head.nightwatch_tricorne", "야경대 삼각모", GearSlot.Head, N, 1, 40, pDef: 1, mDef: 1, stats: Stats(perc: 2));
            A("armor.head.plague_mask", "역병 의사의 가면", GearSlot.Head, R, 4, 140, pDef: 2, mDef: 5,
                unique: "중독 틱 피해 −1, 최소 1");
            A("armor.head.rune_hat", "룬 각인 모자", GearSlot.Head, E, 7, 250, pDef: 4, mDef: 6, focus: 1);
            // 반지
            A("armor.ring.copper_strength", "구리 힘의 반지", GearSlot.Ring, N, 1, 45, stats: Stats(str: 3));
            A("armor.ring.anatomist", "해부학자의 반지", GearSlot.Ring, R, 4, 150, stats: Stats(intel: 5));
            A("armor.ring.silver_aim", "은빛 조준 반지", GearSlot.Ring, R, 4, 150, stats: Stats(perc: 5));
            A("armor.ring.gambler_seal", "도박가의 인장", GearSlot.Ring, EquipmentGrade.Unique, 7, 480, stats: Stats(luck: 8),
                unique: "최초 행운 이벤트 재판정 비용 10 Cent 감소");
            A("armor.ring.marie_contract", "마리의 계약 인장", GearSlot.Ring, EquipmentGrade.Special, 7, 580, focus: 1,
                unique: "적 처치 시 자신 집중력 1 회복, 전투당 1회");
            // 목걸이
            A("armor.necklace.small_cross", "작은 십자가 부적", GearSlot.Necklace, N, 1, 50, hp: 10);
            A("armor.necklace.quarantine", "검역소 목걸이", GearSlot.Necklace, R, 4, 170, mDef: 2, mp: 20);
            A("armor.necklace.nightwatch_insignia", "야경대의 휘장", GearSlot.Necklace, E, 7, 260, pDef: 3, mDef: 3, stats: Stats(spd: 5));
            A("armor.necklace.broken_pocketwatch", "부서진 회중시계", GearSlot.Necklace, EquipmentGrade.Special, 7, 650, stats: Stats(spd: 5),
                unique: "고유 기술 시간 되감기: 집중력 1, 전투당 1회, 자신에게 돌격");

            return list;
        }

        // ───────────── 소모품·이동권·재료 ─────────────

        private static List<ConsumableDefinition> SeedConsumables()
        {
            var list = new List<ConsumableDefinition>();
            const UseContext Both = UseContext.Battle | UseContext.World;
            const UseContext Battle = UseContext.Battle;
            const UseContext World = UseContext.World;

            ConsumableDefinition C(string id, string name, string desc, ConsumableEffect effect, UseContext usable,
                int price, int stock, bool act2 = false, int amount = 0, int duration = 0,
                StatusEffectType[] cures = null, StatKind buffStat = StatKind.Strength, int maxStack = ConsumableDefinition.DefaultMaxStack)
            {
                var c = LoadOrCreate<ConsumableDefinition>(ConsumableDir, "Consumable_" + name.Replace(" ", ""));
                c.Id = id;
                c.DisplayName = name;
                c.EffectDescription = desc;
                c.Effect = effect;
                c.Amount = amount;
                c.Duration = duration;
                c.BuffStat = buffStat;
                c.Cures = cures ?? System.Array.Empty<StatusEffectType>();
                c.Usable = usable;
                c.Price = price;
                c.CityStock = stock;
                c.MaxStack = maxStack;
                c.AvailableFromAct2 = act2;
                EditorUtility.SetDirty(c);
                list.Add(c);
                return c;
            }

            // 회복
            C(ConsumableIds.Bandage, "압박 붕대", "HP +20, 약한 출혈 제거", ConsumableEffect.RestoreHP, Both, 20, 5,
                amount: 20, cures: new[] { StatusEffectType.WeakBleed });
            C(ConsumableIds.LifeElixir, "생명의 영약", "HP +50", ConsumableEffect.RestoreHP, Both, 50, 3, amount: 50);
            C("consumable.greater_life_elixir", "고급 생명의 영약", "HP +100", ConsumableEffect.RestoreHP, Both, 100, 2, act2: true, amount: 100);
            C(ConsumableIds.ManaElixir, "마력의 영약", "MP +30", ConsumableEffect.RestoreMP, Both, 30, 3, amount: 30);
            C("consumable.greater_mana_elixir", "고급 마력의 영약", "MP +75", ConsumableEffect.RestoreMP, Both, 75, 2, act2: true, amount: 75);
            C(ConsumableIds.FocusCandle, "집중의 향초", "집중력 +1", ConsumableEffect.RestoreFocus, Both, 45, 3, amount: 1);
            C("consumable.white_rosemary", "백색 로즈마리", "집중력 +3", ConsumableEffect.RestoreFocus, Both, 120, 1, act2: true, amount: 3);

            // 정화 — 아직 상태 종류가 없는 화상·공포·저주·부식·강화/치명적 출혈은 Cures에서 빠진다 (상태 추가 시 보충)
            C(ConsumableIds.Antidote, "해독제", "중독 모든 스택 제거", ConsumableEffect.Cure, Both, 25, 3,
                cures: new[] { StatusEffectType.Poison });
            C("consumable.hemostatic_salve", "지혈 연고", "모든 출혈 제거", ConsumableEffect.Cure, Both, 30, 2,
                cures: new[] { StatusEffectType.WeakBleed }); // 강화·치명적 출혈 미구현
            C("consumable.cooling_mercury_salve", "냉각 수은 연고", "화상 제거", ConsumableEffect.Cure, Both, 25, 2); // 화상 미구현 — Cures 비움
            C("consumable.calming_salts", "정신 안정 염", "혼란·공포 제거", ConsumableEffect.Cure, Both, 40, 2,
                cures: new[] { StatusEffectType.Confusion }); // 공포 미구현
            C("consumable.witch_bone", "마녀의 뼈", "저주 제거", ConsumableEffect.Cure, Both, 80, 1, act2: true); // 저주 미구현 — Cures 비움
            C("consumable.panacea", "만능 정화제", "중독·출혈·화상·혼란·공포·저주·부식 제거; 죽음의 표식 제외", ConsumableEffect.Cure, Both, 150, 1, act2: true,
                cures: new[] { StatusEffectType.Poison, StatusEffectType.WeakBleed, StatusEffectType.Confusion }); // 화상·공포·저주·부식 미구현

            // 전투 보조
            C("consumable.holy_water", "성수", "적 1개의 회피 상태를 2개 개인 턴 동안 해제", ConsumableEffect.RevealEvasion, Battle, 60, 2, duration: 2);

            // 수리 ("장비 파손·수리·등급 상승")
            C(ConsumableIds.RepairKit, "휴대 수리 도구", "파손 무기 1개 복구", ConsumableEffect.RepairWeapon, Both, 90, 1);
            C("consumable.suture_repair_kit", "봉합 수리 도구", "파손 방어구·장신구 1개 복구", ConsumableEffect.RepairGear, Both, 100, 1, act2: true);

            // 이동·야영 ("월드·안전 타일"은 World로 둔다 — 안전 타일 조건은 사용처에서 확인)
            C("consumable.fog_camp_set", "안개 야영 세트", "생존 파티의 최대 HP·MP 25%와 집중력 1 회복; 사용 후 1월드 라운드 경과",
                ConsumableEffect.CampRest, World, 200, 1); // 25%·1은 ConsumableRules.ApplyCampRest 고정값
            C(ConsumableIds.RuneTicket, "룬 기관차 탑승권", "기관차 칸에서 방문한 도시 중 1곳으로 파티 이동; 현재 월드 행동 종료",
                ConsumableEffect.RuneTicket, World, 60, 2);
            C(ConsumableIds.Teleport, "텔레포트권", "이미 탐색한 10칸 이내 안전 타일로 현재 캐릭터 이동; 행동 종료",
                ConsumableEffect.Teleport, World, 120, 1, amount: 10); // Amount = 최대 거리(칸)
            C(ConsumableIds.Escape, "탈출권", "보스 봉쇄를 제외한 전투에서 생존 아군 전원 퇴각; 경험치·전리품 없음",
                ConsumableEffect.Escape, Battle, 100, 1);

            // 버프 — 월드 3월드 라운드 / 전투 3개 개인 턴
            C("consumable.iron_tonic", "철심 강장제", "힘 +10, 월드 사용은 3월드 라운드, 전투 사용은 3개 개인 턴",
                ConsumableEffect.StatBuff, Both, 70, 1, act2: true, amount: 10, duration: 3, buffStat: StatKind.Strength);
            C("consumable.insight_distillate", "통찰의 증류액", "지력 +10, 강장제와 동일 기간",
                ConsumableEffect.StatBuff, Both, 70, 1, act2: true, amount: 10, duration: 3, buffStat: StatKind.Intelligence);
            C("consumable.swift_ampoule", "신속의 앰플", "아군 1명 속도 +15 (행동 1회). 다음 라운드 순서부터 반영, 사용한 라운드를 빼고 2라운드 유지. 다시 쓰면 기간만 갱신",
                ConsumableEffect.StatBuff, Battle, 65, 2, amount: 15, duration: 2, buffStat: StatKind.Speed); // 1막 판매 (속도 확정안 10/26)
            C("consumable.holy_silver_bullet", "성유 은탄", "다음 3회 활·총 공격의 인간형·망령형 대상 피해 +20%; 전투 종료 시 남은 횟수 소멸",
                ConsumableEffect.SilverBullet, Battle, 85, 1, act2: true, amount: 20, duration: 3); // Amount = 피해 +%, Duration = 공격 횟수
            C("consumable.alchemy_firebomb", "연금술 화염병", "모든 적에게 고정 기본값 20의 마법 피해; 각 마법 방어 적용, 크리티컬 없음",
                ConsumableEffect.Firebomb, Battle, 65, 2, amount: 20);
            C("consumable.fog_smoke_bomb", "안개 연막병", "다음 기습 슬롯 성공률 +15%p; 명탐정 패시브와 합산, 최대 95%",
                ConsumableEffect.AmbushBonus, World, 80, 1, amount: 15); // "월드·조우 전"

            // 재료·특수
            C(ConsumableIds.Reagent, "연금술 시약", "제작 재료; 개별 사용 효과 없음", ConsumableEffect.Material, UseContext.None, 10, 8,
                maxStack: 30); // 시약은 30개까지 (System.md)
            // 판매 조건 "도시당 플레이 전체 1개"는 아직 필드 없음 — 상점 구현 시 처리
            C("consumable.life_laurel", "생명의 월계관", "선택 캐릭터 LP +1, 최대 5; 사망자 부활은 불가",
                ConsumableEffect.LifeLaurel, World, 400, 1, amount: 1);
            // 판매 조건 "고성 진입 후 마리 상점, 플레이 전체 1개" — 가장 가까운 AvailableFromAct2로만 막고 나머지는 상점 구현 시
            C(ConsumableIds.MarieDeed, "마리의 집문서", "일반 무기 1개의 등급 1단계 상승; 세부 제한은 무기 절 참조",
                ConsumableEffect.WeaponUpgrade, World, 1800, 1, act2: true);

            // 성수가 거는 상태 = 장막 걷기와 같은 [회피 상태 해제]
            var holyWater = list.Find(c => c.Id == "consumable.holy_water");
            var revealed = AssetDatabase.LoadAssetAtPath<StatusEffectDefinition>("Assets/_Project/Data/StatusEffects/Status_장막걷힘.asset");
            if (holyWater != null && revealed != null) { holyWater.AppliedStatuses = new[] { revealed }; EditorUtility.SetDirty(holyWater); }

            return list;
        }
    }
}
