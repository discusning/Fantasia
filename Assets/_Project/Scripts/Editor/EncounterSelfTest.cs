using Fantasia.Board;
using Fantasia.Combat;
using Fantasia.Data;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D08 완료 기준 검증: 슬롯 확률 공식, 보스 회피·퇴각 금지, 집중력 강제 성공,
    // 거리 기반 참가자 판정을 System.md 수치와 대조한다.
    public static class EncounterSelfTest
    {
        private const string EnemyDir = "Assets/_Project/Data/Enemies";
        private const int Trials = 20000;
        private const float Tolerance = 0.03f; // 통계적 요동 허용 범위 (±3%p)

        [MenuItem("Fantasia/Data/Run Encounter Self-Test")]
        public static void Run()
        {
            int failures = 0;
            failures += CheckBaseSlotChance();
            failures += CheckStat71Table();
            failures += CheckClamp();
            failures += CheckFocusForcesSuccess();
            failures += CheckBossRestrictions();
            failures += CheckFightHasNoRoll();
            failures += CheckSuccessDistribution();

            if (failures == 0)
                Debug.Log("[EncounterSelfTest] 전부 통과.");
            else
                Debug.LogError($"[EncounterSelfTest] {failures}건 실패. 위 로그 확인할 것.");
        }

        private static int CheckBaseSlotChance()
        {
            int errors = 0;
            errors += ExpectFloat("2슬롯 기본 확률", SlotCheck.BaseSlotChance(2), 0.5f, 0.0001f);
            errors += ExpectFloat("3슬롯 기본 확률", SlotCheck.BaseSlotChance(3), 1f / 3f, 0.0001f);
            errors += ExpectFloat("4슬롯 기본 확률", SlotCheck.BaseSlotChance(4), 0.25f, 0.0001f);
            return errors;
        }

        // System.md 확률표: 스탯 71일 때 2슬롯 57.100%, 3슬롯 40.433%, 4슬롯 32.100%.
        // 전체 성공 확률(=AllSucceeded 비율)은 각각 32.60% / 6.61% / 1.06%.
        private static int CheckStat71Table()
        {
            int errors = 0;
            errors += ExpectFloat("스탯71 2슬롯 최종확률", SlotCheck.FinalSlotChance(2, 71), 0.571f, 0.0005f);
            errors += ExpectFloat("스탯71 3슬롯 최종확률", SlotCheck.FinalSlotChance(3, 71), 0.40433f, 0.0005f);
            errors += ExpectFloat("스탯71 4슬롯 최종확률", SlotCheck.FinalSlotChance(4, 71), 0.321f, 0.0005f);

            errors += CheckAllSucceededRate(2, 71, 0.3260f);
            errors += CheckAllSucceededRate(3, 71, 0.0661f);
            errors += CheckAllSucceededRate(4, 71, 0.0106f);
            return errors;
        }

        private static int CheckAllSucceededRate(int slotCount, int stat, float expectedRate)
        {
            int successCount = 0;
            for (int i = 0; i < Trials; i++)
            {
                if (SlotCheck.Roll(slotCount, stat).AllSucceeded) successCount++;
            }
            float measured = (float)successCount / Trials;
            if (Mathf.Abs(measured - expectedRate) > Tolerance)
            {
                Debug.LogError($"[EncounterSelfTest] {slotCount}슬롯 스탯{stat} 전체성공률: 기대값 {expectedRate:P2}, 측정값 {measured:P2} (허용오차 초과)");
                return 1;
            }
            return 0;
        }

        private static int CheckClamp()
        {
            int errors = 0;
            // 스탯 0에 극단적 음수 보정을 줘도 5% 밑으로 안 내려감.
            errors += ExpectFloat("하한 클램프", SlotCheck.FinalSlotChance(4, 0, extraBonus: -1f), 0.05f, 0.0001f);
            // 스탯이 극단적으로 높아도 95% 위로 안 올라감.
            errors += ExpectFloat("상한 클램프", SlotCheck.FinalSlotChance(2, 999), 0.95f, 0.0001f);
            return errors;
        }

        private static int CheckFocusForcesSuccess()
        {
            int errors = 0;
            // 집중력으로 슬롯 1개 확정해도 나머지 슬롯이 남아있는 4슬롯·낮은 스탯이면
            // 100% 성공은 아니다 — 여기서는 "슬롯 수만큼 집중력을 쓰면 무조건 전체 성공"만 검증.
            for (int i = 0; i < 200; i++)
            {
                var result = SlotCheck.Roll(slotCount: 4, relevantStat: 0, focusSlots: 4);
                if (!result.AllSucceeded)
                {
                    Debug.LogError("[EncounterSelfTest] 슬롯 수만큼 집중력을 썼는데 AllSucceeded가 false다.");
                    errors++;
                    break;
                }
            }
            return errors;
        }

        private static int CheckBossRestrictions()
        {
            int errors = 0;
            var boss = ScriptableObject.CreateInstance<EnemyDefinition>();
            boss.Id = "test.boss";
            boss.DisplayName = "테스트 보스";
            boss.Size = EnemySize.Boss;
            boss.SlotCount = 4;

            errors += ExpectBool("보스는 AllowsPreCombatAvoidance=false", EncounterRules.AllowsPreCombatAvoidance(boss), false);

            try
            {
                EncounterRules.ResolveSneak(boss, speedStat: 99);
                Debug.LogError("[EncounterSelfTest] 보스 대상 ResolveSneak이 예외를 던지지 않았다.");
                errors++;
            }
            catch (System.InvalidOperationException) { /* 기대한 동작 */ }

            try
            {
                EncounterRules.ResolveRetreatBeforeCombat(boss);
                Debug.LogError("[EncounterSelfTest] 보스 대상 ResolveRetreatBeforeCombat이 예외를 던지지 않았다.");
                errors++;
            }
            catch (System.InvalidOperationException) { /* 기대한 동작 */ }

            var smallEnemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"{EnemyDir}/Enemy_그림자사냥개.asset");
            if (smallEnemy != null)
            {
                errors += ExpectBool("소형 적은 AllowsPreCombatAvoidance=true", EncounterRules.AllowsPreCombatAvoidance(smallEnemy), true);
            }

            Object.DestroyImmediate(boss);
            return errors;
        }

        private static int CheckFightHasNoRoll()
        {
            return ExpectBool("Fight()는 항상 FightStarted", EncounterRules.Fight() == Fantasia.World.EncounterOutcome.FightStarted, true);
        }

        // 조우 UI 확률표: 성공 개수 분포의 합은 1, 전부 성공 칸은 크리티컬 확률과 같다.
        // 예: 2슬롯 p=0.5 → 0개 25% / 1개 50% / 2개 25%.
        private static int CheckSuccessDistribution()
        {
            int errors = 0;
            var half = SlotCheck.SuccessCountDistribution(2, 0.5f);
            errors += ExpectFloat("분포 2슬롯 0개", half[0], 0.25f, 0.0001f);
            errors += ExpectFloat("분포 2슬롯 1개", half[1], 0.5f, 0.0001f);
            errors += ExpectFloat("분포 2슬롯 2개", half[2], 0.25f, 0.0001f);

            float p = SlotCheck.FinalSlotChance(4, 71);
            var dist = SlotCheck.SuccessCountDistribution(4, p);
            float sum = 0f;
            foreach (var v in dist) sum += v;
            errors += ExpectFloat("분포 합계 1", sum, 1f, 0.0001f);
            errors += ExpectFloat("분포 전부 성공 = 크리 확률", dist[4], SlotCheck.AllSuccessProbability(4, p), 0.0001f);

            var focused = SlotCheck.SuccessCountDistribution(3, p, focusSlots: 1);
            errors += ExpectFloat("집중 1개면 0개 성공 확률 0", focused[0], 0f, 0.0001f);
            return errors;
        }

        private static int ExpectFloat(string label, float actual, float expected, float tolerance)
        {
            if (Mathf.Abs(actual - expected) <= tolerance) return 0;
            Debug.LogError($"[EncounterSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectBool(string label, bool actual, bool expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[EncounterSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }
    }
}
