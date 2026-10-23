using System;
using System.IO;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D03 산출물: 새 Fantasia.Data 스키마가 실제로 동작하는지 보여주는 샘플 1개씩.
    // System.md 수치를 그대로 옮겨 적었다. 나머지 2직업/3적/10스킬/10상태는
    // D04·D11·D12·D13에서 이 패턴을 반복해 채운다.
    public static class GameDataSeed
    {
        private const string JobDir = "Assets/_Project/Data/Jobs";
        private const string WeaponDir = "Assets/_Project/Data/Weapons";
        private const string EnemyDir = "Assets/_Project/Data/Enemies";
        private const string SkillDir = "Assets/_Project/Data/Skills";
        private const string StatusDir = "Assets/_Project/Data/StatusEffects";

        // -executeMethod entry point for CLI/CI runs (batch mode has no menu).
        public static void SeedAndValidate()
        {
            SeedAll();
            GameDataValidator.Validate();
            PartyMemberSelfTest.Run();
            WorldSelfTest.Run();
            EncounterSelfTest.Run();
        }

        [MenuItem("Fantasia/Data/Seed Sample Game Data")]
        public static void SeedAll()
        {
            var skill = EnsureFieldInvestigation();
            var dagger = EnsureFoldingDagger();
            EnsureDetective(dagger, skill);

            var staff = EnsureQuarantineStaff();
            EnsurePlagueDoctor(staff);

            var pistol = EnsureFlintlockPistol();
            EnsureSniper(pistol);

            EnsureShadowHound();
            EnsureSmallEnemy("Enemy_연성독거미유체", "enemy.small.alchemic_spider_larva", "연성 독거미(유체)", "키메라형",
                hp: 36, speed: 44, physAtk: 0, magAtk: 6, physDef: 3, magDef: 5,
                "연금술 시약과 독거미가 융합된 키메라. 판정 성공 시 [중독](1스택, 매턴 3 피해/속도 -5)을 부여함.");
            EnsureSmallEnemy("Enemy_역병쥐", "enemy.small.plague_rat", "역병 쥐", "야수형",
                hp: 28, speed: 66, physAtk: 8, magAtk: 0, physDef: 4, magDef: 1,
                "18세기 유럽 도시에 우글거리는 병균 쥐. 판정 성공 시 [약한 출혈](매턴 지속 -2<방무>)과 [속박]을 부여함.");
            EnsureSmallEnemy("Enemy_가고일유체", "enemy.small.gargoyle_larva", "가고일 유체", "구조물형",
                hp: 42, speed: 35, physAtk: 10, magAtk: 0, physDef: 6, magDef: 6,
                "잭 더 리퍼를 숭배하는 정보원. 판정 성공 시 아군의 공격력 감쇠 디버프 부여 [공격력 -3]");
            EnsureWeakBleed();
            UIThemeSetup.EnsureAsset();
            AssetDatabase.SaveAssets();
            Debug.Log("[GameDataSeed] 샘플 자산 생성/갱신 완료 (1차 3직업: 명탐정·역병 의사·저격수 + 시작 무기, 현장 검증, 1막 소형 적 4종, 약한 출혈)");
        }

        private static T LoadOrCreate<T>(string dir, string name) where T : ScriptableObject
        {
            Directory.CreateDirectory(dir);
            string path = $"{dir}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static SkillDefinition EnsureFieldInvestigation()
        {
            var s = LoadOrCreate<SkillDefinition>(SkillDir, "Skill_Detective_현장검증");
            s.Id = "skill.detective.field_investigation";
            s.DisplayName = "현장 검증";
            s.OwnerJob = JobId.Detective;
            s.Type = SkillType.Passive;
            s.Target = SkillTarget.None;
            s.FocusCost = 0;
            s.HPCost = 0;
            s.MPCost = 0;
            s.CooldownTurns = 0;
            s.EffectDescription = "조우 화면에 적 HP·속도·양쪽 방어력을 표시한다. 명탐정이 조우를 시작하면 기습 슬롯 성공률 +10%p.";
            EditorUtility.SetDirty(s);
            return s;
        }

        private static WeaponDefinition EnsureFoldingDagger()
        {
            var w = LoadOrCreate<WeaponDefinition>(WeaponDir, "Weapon_탐정의접이식단검");
            w.Id = "weapon.dagger.detective_folding";
            w.DisplayName = "탐정의 접이식 단검";
            w.Category = WeaponCategory.Dagger;
            w.SlotCount = 2;
            w.BaseBonusB = 5;
            w.BaseAttackMPCost = 0;
            w.WearStatBonusDescription = "힘 +2, 속도 +3";
            w.WearStatBonus = new StatBlock(strength: 2, speed: 3, vitality: 0, intelligence: 0, luck: 0, perception: 0);
            w.CritEffectDescription = "크리티컬 시 약한 출혈 3턴";
            w.Grade = EquipmentGrade.Normal;
            w.WearLevel = 1;
            w.Price = 70;
            w.AllowedJobs = new[] { JobId.Detective, JobId.Assassin, JobId.Gambler };
            EditorUtility.SetDirty(w);
            return w;
        }

        private static JobDefinition EnsureDetective(WeaponDefinition startingWeapon, SkillDefinition passive)
        {
            var j = LoadOrCreate<JobDefinition>(JobDir, "Job_명탐정");
            j.Id = JobId.Detective;
            j.DisplayName = "명탐정";
            j.Role = "근접 공격·수사·약점 분석";
            j.StartingHP = 108;
            j.StartingMP = 0;
            j.StartingFocus = 3;
            j.StartingLP = 3;
            j.BasePhysicalAttack = 8;
            j.BaseMagicAttack = 0;
            j.BasePhysicalDefense = 6;
            j.BaseMagicDefense = 6;
            j.Stats = new StatBlock(strength: 71, speed: 70, vitality: 54, intelligence: 33, luck: 50, perception: 38);
            j.AllowedWeaponCategories = new[] { WeaponCategory.Dagger, WeaponCategory.SwordSaber, WeaponCategory.Greatsword };
            j.StartingWeapon = startingWeapon;
            j.Skills = new[] { passive };
            EditorUtility.SetDirty(j);
            return j;
        }

        private static WeaponDefinition EnsureQuarantineStaff()
        {
            var w = LoadOrCreate<WeaponDefinition>(WeaponDir, "Weapon_검역소지팡이");
            w.Id = "weapon.staff.quarantine";
            w.DisplayName = "검역소 지팡이";
            w.Category = WeaponCategory.Staff;
            w.SlotCount = 3;
            w.BaseBonusB = 6;
            w.BaseAttackMPCost = 4;
            w.WearStatBonusDescription = "지력 +4, 최대 MP +8";
            w.WearStatBonus = new StatBlock(strength: 0, speed: 0, vitality: 0, intelligence: 4, luck: 0, perception: 0);
            w.MaxMPBonus = 8;
            w.CritEffectDescription = "크리티컬 시 마법 방어 -3, 2턴";
            w.Grade = EquipmentGrade.Normal;
            w.WearLevel = 1;
            w.Price = 85;
            w.AllowedJobs = new[] { JobId.PlagueDoctor, JobId.Alchemist, JobId.Gambler };
            EditorUtility.SetDirty(w);
            return w;
        }

        private static JobDefinition EnsurePlagueDoctor(WeaponDefinition startingWeapon)
        {
            var j = LoadOrCreate<JobDefinition>(JobDir, "Job_역병의사");
            j.Id = JobId.PlagueDoctor;
            j.DisplayName = "역병 의사";
            j.Role = "치료·해독·마법 지원";
            j.StartingHP = 132;
            j.StartingMP = 132;
            j.StartingFocus = 3;
            j.StartingLP = 3;
            j.BasePhysicalAttack = 4; // 맨손 기본 공격력 (System.md "레벨·기본 자원")
            j.BaseMagicAttack = 8;
            j.BasePhysicalDefense = 4;
            j.BaseMagicDefense = 10;
            j.Stats = new StatBlock(strength: 32, speed: 53, vitality: 66, intelligence: 71, luck: 50, perception: 27);
            j.AllowedWeaponCategories = new[] { WeaponCategory.Staff, WeaponCategory.Grimoire };
            j.StartingWeapon = startingWeapon;
            j.Skills = Array.Empty<SkillDefinition>(); // D11에서 채움
            EditorUtility.SetDirty(j);
            return j;
        }

        private static WeaponDefinition EnsureFlintlockPistol()
        {
            var w = LoadOrCreate<WeaponDefinition>(WeaponDir, "Weapon_부싯돌권총");
            w.Id = "weapon.pistol.flintlock";
            w.DisplayName = "부싯돌 권총";
            w.Category = WeaponCategory.Pistol;
            w.SlotCount = 2;
            w.BaseBonusB = 7;
            w.BaseAttackMPCost = 0;
            w.WearStatBonusDescription = "인지 +4";
            w.WearStatBonus = new StatBlock(strength: 0, speed: 0, vitality: 0, intelligence: 0, luck: 0, perception: 4);
            w.CritEffectDescription = "기본 공격은 추가 상태 효과 없음; 높은 크리티컬 빈도";
            w.Grade = EquipmentGrade.Normal;
            w.WearLevel = 1;
            w.Price = 90;
            w.AllowedJobs = new[] { JobId.Sniper, JobId.Gambler };
            EditorUtility.SetDirty(w);
            return w;
        }

        private static JobDefinition EnsureSniper(WeaponDefinition startingWeapon)
        {
            var j = LoadOrCreate<JobDefinition>(JobDir, "Job_저격수");
            j.Id = JobId.Sniper;
            j.DisplayName = "저격수";
            j.Role = "원거리 단일 공격·턴 지연";
            j.StartingHP = 76;
            j.StartingMP = 0;
            j.StartingFocus = 3;
            j.StartingLP = 2;
            j.BasePhysicalAttack = 11;
            j.BaseMagicAttack = 0;
            j.BasePhysicalDefense = 3;
            j.BaseMagicDefense = 3;
            j.Stats = new StatBlock(strength: 58, speed: 33, vitality: 38, intelligence: 20, luck: 50, perception: 91);
            j.AllowedWeaponCategories = new[] { WeaponCategory.Bow, WeaponCategory.Pistol, WeaponCategory.Musket };
            j.StartingWeapon = startingWeapon;
            j.Skills = Array.Empty<SkillDefinition>(); // D11에서 채움
            EditorUtility.SetDirty(j);
            return j;
        }

        // System.md "소형 적" + "기존 일반 적의 보완 스탯" 표 (1막 소형 4종 공통: 2슬롯·45%).
        private static EnemyDefinition EnsureSmallEnemy(string assetName, string id, string displayName, string typeTag,
            int hp, int speed, int physAtk, int magAtk, int physDef, int magDef, string trait)
        {
            var e = LoadOrCreate<EnemyDefinition>(EnemyDir, assetName);
            e.Id = id;
            e.DisplayName = displayName;
            e.Size = EnemySize.Small;
            e.TypeTag = typeTag;
            e.MaxHP = hp;
            e.Speed = speed;
            e.PhysicalAttack = physAtk;
            e.MagicAttack = magAtk;
            e.PhysicalDefense = physDef;
            e.MagicDefense = magDef;
            e.SlotCount = 2;
            e.SlotSuccessRate = 0.45f;
            e.HasEvasionState = false;
            e.TraitDescription = trait;
            e.DeploymentAct = "1막";
            EditorUtility.SetDirty(e);
            return e;
        }

        private static EnemyDefinition EnsureShadowHound()
        {
            var e = LoadOrCreate<EnemyDefinition>(EnemyDir, "Enemy_그림자사냥개");
            e.Id = "enemy.small.shadow_hound";
            e.DisplayName = "그림자 사냥개";
            e.Size = EnemySize.Small;
            e.TypeTag = "망령형";
            e.MaxHP = 32;
            e.Speed = 62;
            e.PhysicalAttack = 5;
            e.MagicAttack = 0;
            e.PhysicalDefense = 2;
            e.MagicDefense = 10;
            e.SlotCount = 2;
            e.SlotSuccessRate = 0.45f;
            e.HasEvasionState = true;
            e.TraitDescription = "[회피 상태] 패시브 보유. 공격 판정 성공 시 대상에게 [혼란]을 부여하며 타임라인을 [방해함] 위치로 밀어냄.";
            e.DeploymentAct = "1막";
            EditorUtility.SetDirty(e);
            return e;
        }

        private static StatusEffectDefinition EnsureWeakBleed()
        {
            var se = LoadOrCreate<StatusEffectDefinition>(StatusDir, "Status_약한출혈");
            se.Id = "status.bleed.weak";
            se.DisplayName = "약한 출혈";
            se.Kind = StatusEffectKind.DamageOverTime;
            se.DurationTurns = 3;
            se.MaxStacks = 1;
            se.ClearedByCombatVictory = true;
            se.ClearedByGeneralCleanse = true;
            se.RuleDescription = "개인 턴 시작마다 방어 무시 2 피해. HP 1 미만으로 낮추지 않는다.";
            EditorUtility.SetDirty(se);
            return se;
        }
    }
}
