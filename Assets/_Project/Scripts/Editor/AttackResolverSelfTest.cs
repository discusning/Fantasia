using Fantasia.Combat;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D10 완료 기준 검증: 일부 슬롯 실패 시 기본 피해, 크리티컬 방어 무시 2배, 관통 이중 적용 금지,
    // 회피 상한·회피 상태, 집중력 확정 시 크리티컬 확률을 System.md 수치와 대조한다.
    // 직업·적 자산 검사는 Fantasia/Data/Seed Sample Game Data 실행 후에만 돈다.
    public static class AttackResolverSelfTest
    {
        private const string JobDir = "Assets/_Project/Data/Jobs";
        private const string EnemyDir = "Assets/_Project/Data/Enemies";
        private const int Trials = 20000;
        private const float Tolerance = 0.02f; // 통계적 요동 허용 범위 (±2%p)

        [MenuItem("Fantasia/Data/Run Attack Resolver Self-Test")]
        public static void Run()
        {
            int failures = 0;
            failures += CheckDamageFormulas();
            failures += CheckCritProbabilityTable();
            failures += CheckEvasion();
            failures += CheckComputeRules();
            failures += CheckMonteCarlo();
            failures += CheckPartyMemberRequests();
            failures += CheckEnemyRequest();

            if (failures == 0)
                Debug.Log("[AttackResolverSelfTest] 전부 통과.");
            else
                Debug.LogError($"[AttackResolverSelfTest] {failures}건 실패. 위 로그 확인할 것.");
        }

        private static int CheckDamageFormulas()
        {
            int errors = 0;
            // System.md 예시: 공격력 21 크리티컬 → 방어 무시 42.
            errors += Expect("크리티컬 21 → 42", AttackResolver.CriticalDamage(1f, 21), 42);
            errors += Expect("일반 21 − 방어6", AttackResolver.NormalDamage(1f, 21, 6), 15);
            errors += Expect("일반 피해 하한 0", AttackResolver.NormalDamage(1f, 5, 10), 0);
            errors += Expect("계수 1.3 × 10 = 13 (부동소수점 내림 방지)", AttackResolver.NormalDamage(1.3f, 10, 0), 13);
            errors += Expect("계수 0.9 × 21 = 18 (내림)", AttackResolver.NormalDamage(0.9f, 21, 0), 18);
            errors += Expect("관통 50%: 20 − 방어10의 절반", AttackResolver.NormalDamage(1f, 20, 10, 0.5f), 15);
            errors += Expect("관통 25%: 20 − floor(10×0.75)", AttackResolver.NormalDamage(1f, 20, 10, 0.25f), 13);
            errors += Expect("크리티컬 계수 1.3 × 10 × 2", AttackResolver.CriticalDamage(1.3f, 10), 26);
            return errors;
        }

        // System.md 확률표 (스탯 71): 전체 성공 32.60/6.61/1.06%, 집중력 1 사용 시 57.10/16.35/3.31%.
        private static int CheckCritProbabilityTable()
        {
            int errors = 0;
            errors += ExpectFloat("2슬롯 크리", SlotCheck.AllSuccessProbability(2, SlotCheck.FinalSlotChance(2, 71)), 0.3260f, 0.0005f);
            errors += ExpectFloat("3슬롯 크리", SlotCheck.AllSuccessProbability(3, SlotCheck.FinalSlotChance(3, 71)), 0.0661f, 0.0005f);
            errors += ExpectFloat("4슬롯 크리", SlotCheck.AllSuccessProbability(4, SlotCheck.FinalSlotChance(4, 71)), 0.0106f, 0.0005f);
            errors += ExpectFloat("2슬롯 크리(집중1)", SlotCheck.AllSuccessProbability(2, SlotCheck.FinalSlotChance(2, 71), 1), 0.5710f, 0.0005f);
            errors += ExpectFloat("3슬롯 크리(집중1)", SlotCheck.AllSuccessProbability(3, SlotCheck.FinalSlotChance(3, 71), 1), 0.1635f, 0.0005f);
            errors += ExpectFloat("4슬롯 크리(집중1)", SlotCheck.AllSuccessProbability(4, SlotCheck.FinalSlotChance(4, 71), 1), 0.0331f, 0.0005f);
            return errors;
        }

        private static int CheckEvasion()
        {
            int errors = 0;
            errors += ExpectFloat("회피 속도70", AttackResolver.EvasionChance(70), 0.12f, 0.0001f);
            errors += ExpectFloat("회피 상한 40%", AttackResolver.EvasionChance(99, 0.5f), 0.40f, 0.0001f);
            errors += ExpectFloat("회피 보정 +5%p", AttackResolver.EvasionChance(70, 0.05f), 0.17f, 0.0001f);
            return errors;
        }

        private static int CheckComputeRules()
        {
            int errors = 0;
            var attack = new AttackRequest
            {
                SlotCount = 4, SlotChance = 0.25f, Coefficient = 1f, AttackValue = 20,
                Type = DamageType.Physical, PierceRatio = 0.5f,
            };
            var defense = new DefenseProfile { PhysicalDefense = 10, MagicDefense = 0, CurrentHP = 100 };
            var partial = new SlotCheck.Result(4, 1);
            var full = new SlotCheck.Result(4, 4);

            // 일부 슬롯 실패해도 일반 공격은 기본 피해 (빗나가지 않음).
            var o = AttackResolver.Compute(attack, defense, partial, false);
            errors += ExpectBool("슬롯 일부 실패 — 명중", !o.Evaded && !o.IsCritical, true);
            errors += Expect("슬롯 일부 실패 — 관통 적용 일반 피해", o.Damage, 15);

            // 크리티컬은 방어 무시 2배, 관통을 다시 곱하지 않는다.
            o = AttackResolver.Compute(attack, defense, full, false);
            errors += Expect("크리티컬 — 방어·관통 무관 40", o.Damage, 40);

            // 마법 공격은 마법 방어 참조.
            var magic = attack;
            magic.Type = DamageType.Magic;
            magic.PierceRatio = 0f;
            o = AttackResolver.Compute(magic, new DefenseProfile { PhysicalDefense = 99, MagicDefense = 3, CurrentHP = 100 }, partial, false);
            errors += Expect("마법 공격은 마법 방어 참조", o.Damage, 17);

            // 회피 상태 적: 비크리티컬 회피, 크리티컬 명중.
            var evasive = defense;
            evasive.HasEvasionState = true;
            o = AttackResolver.Compute(attack, evasive, partial, false);
            errors += ExpectBool("회피 상태 — 비크리티컬 회피", o.Evaded && o.EvadedByState, true);
            o = AttackResolver.Compute(attack, evasive, full, false);
            errors += ExpectBool("회피 상태 — 크리티컬 명중", !o.Evaded && o.Damage == 40, true);

            // 일반 회피는 크리티컬에도 적용.
            o = AttackResolver.Compute(attack, defense, full, true);
            errors += ExpectBool("일반 회피 — 크리티컬도 회피", o.Evaded && !o.EvadedByState && o.Damage == 0, true);

            // 현재 HP 초과분은 실제 차감량에서 제외.
            var lowHp = defense;
            lowHp.CurrentHP = 7;
            o = AttackResolver.Compute(attack, lowHp, full, false);
            errors += ExpectBool("초과 피해 제외 (피해 40, 차감 7)", o.Damage == 40 && o.AppliedDamage == 7, true);
            return errors;
        }

        private static int CheckMonteCarlo()
        {
            int errors = 0;
            var attack = new AttackRequest
            {
                SlotCount = 2, SlotChance = SlotCheck.FinalSlotChance(2, 71), UseFocus = true,
                Coefficient = 1f, AttackValue = 10, Type = DamageType.Physical,
            };
            var defense = new DefenseProfile { EvasionChance = AttackResolver.EvasionChance(70), CurrentHP = 999 };

            int crits = 0, evades = 0;
            for (int i = 0; i < Trials; i++)
            {
                var o = AttackResolver.Resolve(attack, defense);
                if (o.IsCritical) crits++;
                if (o.Evaded) evades++;
            }
            errors += ExpectFloat("몬테카를로 집중1 크리 57.1%", (float)crits / Trials, 0.571f, Tolerance);
            errors += ExpectFloat("몬테카를로 회피 12%", (float)evades / Trials, 0.12f, Tolerance);
            return errors;
        }

        private static int CheckPartyMemberRequests()
        {
            var detective = AssetDatabase.LoadAssetAtPath<JobDefinition>($"{JobDir}/Job_명탐정.asset");
            var plagueDoctor = AssetDatabase.LoadAssetAtPath<JobDefinition>($"{JobDir}/Job_역병의사.asset");
            var sniper = AssetDatabase.LoadAssetAtPath<JobDefinition>($"{JobDir}/Job_저격수.asset");
            if (detective == null || plagueDoctor == null || sniper == null)
            {
                Debug.LogWarning("[AttackResolverSelfTest] 직업 자산이 없어 파티원 검사를 건너뜀 — Seed Sample Game Data 먼저 실행.");
                return 0;
            }

            int errors = 0;
            // 명탐정: 단검 2슬롯, 힘 71 + 단검 +2 = 73 → 57.3%, 공격력 8 + 단검 5 = 13, 물리.
            var d = new PartyMember(detective);
            var a = AttackResolver.FromPartyMember(d);
            errors += Expect("명탐정 슬롯 수", a.SlotCount, 2);
            errors += ExpectFloat("명탐정 슬롯 확률(힘73)", a.SlotChance, 0.573f, 0.0005f);
            errors += Expect("명탐정 공격력", a.AttackValue, 13);
            errors += ExpectBool("명탐정 물리", a.Type == DamageType.Physical, true);
            errors += Expect("명탐정 최종 속도(70+단검3)", d.FinalStats.Speed, 73);

            // 역병 의사: 스태프 3슬롯, 지력 71 + 4 = 75 → 40.833%, 마력 8 + 6 = 14, 마법.
            a = AttackResolver.FromPartyMember(new PartyMember(plagueDoctor));
            errors += Expect("역병 의사 슬롯 수", a.SlotCount, 3);
            errors += ExpectFloat("역병 의사 슬롯 확률(지력75)", a.SlotChance, 0.40833f, 0.0005f);
            errors += Expect("역병 의사 마력", a.AttackValue, 14);
            errors += ExpectBool("역병 의사 마법", a.Type == DamageType.Magic, true);

            // 저격수: 권총 2슬롯, 인지 91 + 4 = 95 → 59.5%, 공격력 11 + 7 = 18.
            a = AttackResolver.FromPartyMember(new PartyMember(sniper));
            errors += ExpectFloat("저격수 슬롯 확률(인지95)", a.SlotChance, 0.595f, 0.0005f);
            errors += Expect("저격수 공격력", a.AttackValue, 18);

            // 맨손: 2슬롯·힘 판정·무기 보너스 0.
            var unarmed = new PartyMember(detective);
            unarmed.EquipWeapon(null);
            a = AttackResolver.FromPartyMember(unarmed);
            errors += Expect("맨손 슬롯 수", a.SlotCount, 2);
            errors += ExpectFloat("맨손 슬롯 확률(힘71)", a.SlotChance, 0.571f, 0.0005f);
            errors += Expect("맨손 공격력(무기 보너스 0)", a.AttackValue, 8);

            // 방어 행동 +8, 회피는 최종 속도 기준.
            var def = AttackResolver.DefenseOf(d, defending: true);
            errors += Expect("방어 행동 물리 방어 6+8", def.PhysicalDefense, 14);
            errors += ExpectFloat("명탐정 회피(속도73)", def.EvasionChance, 0.123f, 0.0001f);

            // 집중력: 부족하면 차감 안 됨.
            errors += ExpectBool("집중력 1 사용", d.TrySpendFocus(1) && d.CurrentFocus == 2, true);
            errors += ExpectBool("집중력 부족 시 실패·미차감", !d.TrySpendFocus(3) && d.CurrentFocus == 2, true);
            return errors;
        }

        private static int CheckEnemyRequest()
        {
            var hound = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"{EnemyDir}/Enemy_그림자사냥개.asset");
            if (hound == null)
            {
                Debug.LogWarning("[AttackResolverSelfTest] 적 자산이 없어 적 검사를 건너뜀.");
                return 0;
            }

            int errors = 0;
            var a = AttackResolver.FromEnemy(hound);
            errors += Expect("그림자 사냥개 슬롯 수", a.SlotCount, 2);
            errors += ExpectFloat("그림자 사냥개 슬롯 확률 45% 그대로", a.SlotChance, 0.45f, 0.0001f);
            errors += Expect("그림자 사냥개 공격력", a.AttackValue, 5);
            var def = AttackResolver.DefenseOf(hound, currentHP: 32, hasEvasionState: true);
            errors += ExpectBool("적 일반 회피 0, 회피 상태 보유", def.EvasionChance == 0f && def.HasEvasionState, true);
            return errors;
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[AttackResolverSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectFloat(string label, float actual, float expected, float tolerance)
        {
            if (Mathf.Abs(actual - expected) <= tolerance) return 0;
            Debug.LogError($"[AttackResolverSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectBool(string label, bool actual, bool expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[AttackResolverSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }
    }
}
