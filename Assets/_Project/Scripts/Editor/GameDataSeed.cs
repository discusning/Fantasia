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
            var bleed = EnsureWeakBleed();
            var poison = EnsureStatus("Status_중독", "status.poison", "중독", "중독", StatusEffectType.Poison, StatusEffectKind.DamageOverTime,
                duration: -1, maxStacks: 3, magnitude: 3, statPenalty: 5, clearedByVictory: false,
                "최대 3스택. 개인 턴 시작마다 스택 × 3 피해, HP 1 미만으로 낮추지 않음. 1스택 이상이면 모든 능력 스탯 −5를 한 번 적용. 전투 밖에서는 월드 개인 턴에 적용.");
            var confusion = EnsureStatus("Status_혼란", "status.confusion", "혼란", "혼란", StatusEffectType.Confusion, StatusEffectKind.ControlAction,
                duration: 1, maxStacks: 1, magnitude: 0, statPenalty: 0, clearedByVictory: true,
                "다음 개인 턴에 50% 확률로 소지한 일반 HP 회복약 1개를 자신에게 사용, 아니면 임의의 적을 기본 공격. 약이 없으면 기본 공격.");
            var bind = EnsureStatus("Status_속박", "status.bind", "속박", "속박", StatusEffectType.Bind, StatusEffectKind.StatModifier,
                duration: 2, maxStacks: 1, magnitude: 15, statPenalty: 0, clearedByVictory: true,
                "속도 −15. 별도 속도 감소와 같으면 큰 감소값 하나만 적용.");
            // 기간은 System.md에 없어 2턴으로 가정 (Docs/DevPlan 기획 확인 항목).
            var attackDown = EnsureStatus("Status_공격력감소", "status.attack_down", "공격력 감소", "공↓", StatusEffectType.AttackDown, StatusEffectKind.StatModifier,
                duration: 2, maxStacks: 1, magnitude: 3, statPenalty: 0, clearedByVictory: true,
                "공격력 −3 (가고일 유체 특성). 기간 미정 — 2턴 가정.");
            LinkCritStatuses(bleed, poison, confusion, bind, attackDown);
            SeedSkills(skill, poison);
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

        // D11 1차 3직업 스킬 12개 (System.md 각 직업 "스킬" 표) + 스킬이 쓰는 상태 4종.
        private static void SeedSkills(SkillDefinition fieldInvestigation, StatusEffectDefinition poison)
        {
            var defenseDown = EnsureStatus("Status_방어감소", "status.defense_down", "방어 감소", "방↓", StatusEffectType.DefenseDown, StatusEffectKind.StatModifier,
                duration: 2, maxStacks: 1, magnitude: 4, statPenalty: 0, clearedByVictory: true,
                "물리·마법 방어력 −4. 대상의 2개 개인 턴 동안 (약점 간파). 중첩되지 않는다.");
            var revealed = EnsureStatus("Status_장막걷힘", "status.evasion_revealed", "장막 걷힘", "장막", StatusEffectType.EvasionRevealed, StatusEffectKind.Special,
                duration: 2, maxStacks: 1, magnitude: 0, statPenalty: 0, clearedByVictory: true,
                "[회피 상태]를 대상의 2개 개인 턴 동안 해제 (장막 걷기). 해제 중엔 비크리티컬 공격도 명중.");
            // "자신의 다음 2턴 안에 공격하지 않으면 사라진다" — 기간은 개인 턴 시작에 줄어서 3으로 둬야 다음 2턴 동안 남는다.
            var zeroIn = EnsureStatus("Status_영점조정", "status.accuracy_up", "영점 조정", "영점", StatusEffectType.AccuracyUp, StatusEffectKind.StatModifier,
                duration: 3, maxStacks: 1, magnitude: 15, statPenalty: 0, clearedByVictory: true,
                "다음 무기 공격의 슬롯별 성공률 +15%p. 공격하면 소모, 자신의 다음 2턴 안에 공격하지 않으면 사라진다.");
            var slowed = EnsureStatus("Status_속도감소", "status.speed_down", "속도 감소", "속↓", StatusEffectType.Bind, StatusEffectKind.StatModifier,
                duration: 2, maxStacks: 1, magnitude: 15, statPenalty: 0, clearedByVictory: true,
                "속도 −15, 2턴 (저지 사격). 같은 의미의 속도 감소는 큰 값 하나만.");

            fieldInvestigation.Passive = PassiveType.AmbushSlotBonus;
            fieldInvestigation.PassiveValue = 0.10f;
            EditorUtility.SetDirty(fieldInvestigation);

            var detective = new[]
            {
                fieldInvestigation,
                Skill("Skill_Detective_약점간파", "skill.detective.expose_weakness", "약점 간파", JobId.Detective, SkillTarget.SingleEnemy, focus: 1, mp: 0, cooldown: 3,
                    "적의 물리·마법 방어력을 각각 4 낮춘다. 대상의 2개 개인 턴 동안 지속. 분석 효과는 확정 적용하며 중첩되지 않는다.",
                    s => { s.Effect = SkillEffect.ApplyStatus; s.AppliedStatuses = new[] { defenseDown }; }),
                Skill("Skill_Detective_일점돌파", "skill.detective.pinpoint_breach", "일점 돌파", JobId.Detective, SkillTarget.SingleEnemy, focus: 1, mp: 0, cooldown: 2,
                    "물리 1.3A로 공격. 현재 무기 슬롯으로 판정하며 크리티컬 시 추가로 방해함을 적용한다.",
                    s => { s.Effect = SkillEffect.Attack; s.Coefficient = 1.3f; s.OnCritTurnShift = TurnShift.Delay; }),
                Skill("Skill_Detective_장막걷기", "skill.detective.lift_the_veil", "장막 걷기", JobId.Detective, SkillTarget.SingleEnemy, focus: 1, mp: 0, cooldown: 4,
                    "적의 회피 상태를 2개 개인 턴 동안 해제한다. 그림자 적과 인공 사신의 방어 페이즈에 대응한다.",
                    s => { s.Effect = SkillEffect.ApplyStatus; s.AppliedStatuses = new[] { revealed }; }),
            };
            var plagueDoctor = new[]
            {
                Skill("Skill_PlagueDoctor_조제숙련", "skill.plague_doctor.compounding", "조제 숙련", JobId.PlagueDoctor, SkillTarget.None, focus: 0, mp: 0, cooldown: 0,
                    "자신이 사용하는 HP 회복 소모품의 회복량 +25%. 본인의 치료 스킬에는 적용하지 않고, 초과 회복은 버린다.",
                    s => { s.Type = SkillType.Passive; s.Passive = PassiveType.ConsumableHealBonus; s.PassiveValue = 0.25f; }),
                Skill("Skill_PlagueDoctor_응급봉합", "skill.plague_doctor.emergency_suture", "응급 봉합", JobId.PlagueDoctor, SkillTarget.SingleAlly, focus: 0, mp: 12, cooldown: 1,
                    "HP를 30 + floor(0.5M) 회복. 2슬롯 지력 판정을 전부 성공하면 출혈도 제거한다. 기본 회복은 확정.",
                    s => { s.Effect = SkillEffect.Heal; s.HealFlat = 30; s.HealMagicRatio = 0.5f; s.SlotCount = 2; s.RollStat = StatKind.Intelligence;
                           s.CleansedStatuses = new[] { StatusEffectType.WeakBleed }; s.CleanseNeedsFullSuccess = true; }),
                // 저주는 아직 상태가 없어 목록에서 빠짐 — 저주 추가 시 함께 넣는다.
                Skill("Skill_PlagueDoctor_역병정화", "skill.plague_doctor.purge_plague", "역병 정화", JobId.PlagueDoctor, SkillTarget.SingleAlly, focus: 0, mp: 18, cooldown: 3,
                    "중독·저주·혼란을 모두 제거한다. 죽음의 표식과 영혼 수확 의식은 제거하지 못한다.",
                    s => { s.Effect = SkillEffect.Cleanse; s.CleansedStatuses = new[] { StatusEffectType.Poison, StatusEffectType.Confusion }; }),
                Skill("Skill_PlagueDoctor_검은연무", "skill.plague_doctor.black_miasma", "검은 연무", JobId.PlagueDoctor, SkillTarget.AllEnemies, focus: 0, mp: 20, cooldown: 3,
                    "각 적에게 마법 0.9M 피해. 3슬롯 지력 공통 판정이며 크리티컬이면 각 적에게 중독 1스택 부여.",
                    s => { s.Effect = SkillEffect.Attack; s.Coefficient = 0.9f; s.UsesMagic = true; s.SlotCount = 3; s.RollStat = StatKind.Intelligence;
                           s.OnCritStatuses = new[] { poison }; }),
            };
            var sniper = new[]
            {
                Skill("Skill_Sniper_첫탄의침묵", "skill.sniper.first_shot_silence", "첫 탄의 침묵", JobId.Sniper, SkillTarget.None, focus: 0, mp: 0, cooldown: 0,
                    "전투 중 자신의 첫 활·총 공격 피해 +15%. 기본 공격 또는 공격 스킬 1회에 적용하며 다단히트로 재발동하지 않는다.",
                    s => { s.Type = SkillType.Passive; s.Passive = PassiveType.FirstGunShotBonus; s.PassiveValue = 0.15f; }),
                Skill("Skill_Sniper_영점조정", "skill.sniper.zero_in", "영점 조정", JobId.Sniper, SkillTarget.Self, focus: 1, mp: 0, cooldown: 3,
                    "다음 무기 공격의 슬롯별 성공률 +15%p. 행동을 소모하며 자신의 다음 2턴 안에 공격하지 않으면 사라진다.",
                    s => { s.Effect = SkillEffect.ApplyStatus; s.AppliedStatuses = new[] { zeroIn }; }),
                Skill("Skill_Sniper_관통탄", "skill.sniper.piercing_round", "관통탄", JobId.Sniper, SkillTarget.SingleEnemy, focus: 1, mp: 0, cooldown: 2,
                    "물리 1.2A 공격, 일반 피해에는 적 방어력의 50%만 적용. 크리티컬에는 공통 방어 무시 규칙만 적용한다.",
                    s => { s.Effect = SkillEffect.Attack; s.Coefficient = 1.2f; s.PierceRatio = 0.5f; }),
                Skill("Skill_Sniper_저지사격", "skill.sniper.suppressing_fire", "저지 사격", JobId.Sniper, SkillTarget.SingleEnemy, focus: 1, mp: 0, cooldown: 3,
                    "물리 0.9A 공격. 크리티컬이면 대상 속도 −15를 2턴 부여하고 초기화를 적용한다.",
                    s => { s.Effect = SkillEffect.Attack; s.Coefficient = 0.9f; s.OnCritStatuses = new[] { slowed }; s.OnCritTurnShift = TurnShift.Reset; }),
            };

            SetJobSkills("Job_명탐정", detective);
            SetJobSkills("Job_역병의사", plagueDoctor);
            SetJobSkills("Job_저격수", sniper);
        }

        private static SkillDefinition Skill(string assetName, string id, string displayName, JobId job, SkillTarget target,
            int focus, int mp, int cooldown, string description, System.Action<SkillDefinition> configure)
        {
            var s = LoadOrCreate<SkillDefinition>(SkillDir, assetName);
            s.Id = id;
            s.DisplayName = displayName;
            s.OwnerJob = job;
            s.Type = target == SkillTarget.None ? SkillType.Passive : SkillType.Active;
            s.Target = target;
            s.FocusCost = focus;
            s.HPCost = 0;
            s.MPCost = mp;
            s.CooldownTurns = cooldown;
            s.EffectDescription = description;
            // 이전 시드 값이 남지 않게 효과 칸 초기화 후 설정
            s.Effect = SkillEffect.None; s.Coefficient = 1f; s.UsesMagic = false; s.SlotCount = 0; s.RollStat = StatKind.Intelligence;
            s.PierceRatio = 0f; s.HealFlat = 0; s.HealMagicRatio = 0f; s.CleanseNeedsFullSuccess = false; s.OnCritTurnShift = TurnShift.None;
            s.AppliedStatuses = System.Array.Empty<StatusEffectDefinition>();
            s.OnCritStatuses = System.Array.Empty<StatusEffectDefinition>();
            s.CleansedStatuses = System.Array.Empty<StatusEffectType>();
            s.Passive = PassiveType.None; s.PassiveValue = 0f;
            configure(s);
            EditorUtility.SetDirty(s);
            return s;
        }

        private static void SetJobSkills(string jobAsset, SkillDefinition[] skills)
        {
            var job = AssetDatabase.LoadAssetAtPath<JobDefinition>($"{JobDir}/{jobAsset}.asset");
            if (job == null) return;
            job.Skills = skills;
            EditorUtility.SetDirty(job);
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

        private static StatusEffectDefinition EnsureStatus(string assetName, string id, string displayName, string shortLabel,
            StatusEffectType effect, StatusEffectKind kind, int duration, int maxStacks, int magnitude, int statPenalty, bool clearedByVictory, string rule)
        {
            var se = LoadOrCreate<StatusEffectDefinition>(StatusDir, assetName);
            se.Id = id;
            se.DisplayName = displayName;
            se.ShortLabel = shortLabel;
            se.Kind = kind;
            se.Effect = effect;
            se.DurationTurns = duration;
            se.MaxStacks = maxStacks;
            se.Magnitude = magnitude;
            se.StatPenalty = statPenalty;
            se.ClearedByCombatVictory = clearedByVictory;
            se.ClearedByGeneralCleanse = true;
            se.RuleDescription = rule;
            EditorUtility.SetDirty(se);
            return se;
        }

        // 1막 소형 적 4종 특성 (System.md "소형 적") + 단검 계열 크리티컬 효과 (System.md 무기 계열 표).
        // 그림자 사냥개의 [방해함]은 상태가 아니라 행동 순서 기능이라 OnCritTurnShift로 따로 둔다.
        // 적 대상 선택(Targeting)은 System.md에 적별 규칙이 없어 기본 무작위 그대로 — 기획이 정하면 데이터에서.
        private static void LinkCritStatuses(StatusEffectDefinition bleed, StatusEffectDefinition poison, StatusEffectDefinition confusion,
            StatusEffectDefinition bind, StatusEffectDefinition attackDown)
        {
            void Set(string enemyAsset, params StatusEffectDefinition[] statuses)
            {
                var e = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"{EnemyDir}/{enemyAsset}.asset");
                if (e == null) return;
                e.OnCritStatuses = statuses;
                EditorUtility.SetDirty(e);
            }
            Set("Enemy_그림자사냥개", confusion);
            var hound = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"{EnemyDir}/Enemy_그림자사냥개.asset");
            if (hound != null) { hound.OnCritTurnShift = TurnShift.Delay; EditorUtility.SetDirty(hound); } // [방해함]
            Set("Enemy_연성독거미유체", poison);
            Set("Enemy_역병쥐", bleed, bind);
            Set("Enemy_가고일유체", attackDown);

            var dagger = AssetDatabase.LoadAssetAtPath<WeaponDefinition>($"{WeaponDir}/Weapon_탐정의접이식단검.asset");
            if (dagger != null)
            {
                dagger.OnCritStatuses = new[] { bleed };
                EditorUtility.SetDirty(dagger);
            }
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
            se.Effect = StatusEffectType.WeakBleed;
            se.Magnitude = 2;
            se.StatPenalty = 0;
            se.ShortLabel = "출혈";
            se.RuleDescription = "개인 턴 시작마다 방어 무시 2 피해. HP 1 미만으로 낮추지 않는다.";
            EditorUtility.SetDirty(se);
            return se;
        }
    }
}
