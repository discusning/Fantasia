using System.Linq;
using Fantasia.Combat;
using Fantasia.Data;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D09 완료 기준 검증: 속도 내림차순·동률 아군 우선·같은 진영 편성순, 라운드당 1회 행동,
    // 쓰러진/퇴각한 참가자 건너뛰기, 기습 성공/실패 첫 라운드 진영 순서, 속도 변화는 다음 라운드 반영.
    public static class BattleQueueSelfTest
    {
        [MenuItem("Fantasia/Data/Run Battle Queue Self-Test")]
        public static void Run()
        {
            int failures = 0;
            failures += CheckSpeedOrderAndTies();
            failures += CheckAmbush();
            failures += CheckSkipsDownedAndRoundAdvance();

            if (failures == 0) Debug.Log("[BattleQueueSelfTest] 전부 통과.");
            else Debug.LogError($"[BattleQueueSelfTest] {failures}건 실패. 위 로그 확인할 것.");
        }

        private static (BattleUnit[] allies, BattleUnit[] enemies) MakeUnits(int[] allySpeeds, int[] enemySpeeds)
        {
            var allies = allySpeeds.Select((s, i) =>
            {
                var job = ScriptableObject.CreateInstance<JobDefinition>();
                job.DisplayName = $"아군{i}";
                job.Stats = new StatBlock(50, s, 50, 50, 50, 50);
                return new BattleUnit(new PartyMember(job), i);
            }).ToArray();
            var enemies = enemySpeeds.Select((s, i) =>
            {
                var e = ScriptableObject.CreateInstance<EnemyDefinition>();
                e.DisplayName = $"적{i}";
                e.Speed = s;
                e.MaxHP = 10;
                return new BattleUnit(e, i);
            }).ToArray();
            return (allies, enemies);
        }

        private static string Names(System.Collections.Generic.IEnumerable<BattleUnit> units) => string.Join(",", units.Select(u => u.DisplayName));

        private static int CheckSpeedOrderAndTies()
        {
            // 아군 속도 70·60·60, 적 속도 70·65 → 70(아군0) 70(적0) 65(적1) 60(아군1) 60(아군2)
            var (a, e) = MakeUnits(new[] { 70, 60, 60 }, new[] { 70, 65 });
            var order = BattleQueue.ComputeOrder(a.Concat(e), 2, EncounterOutcome.FightStarted);
            return Expect("속도순·동률 아군 우선·편성순", Names(order), "아군0,적0,적1,아군1,아군2");
        }

        private static int CheckAmbush()
        {
            int errors = 0;
            var (a, e) = MakeUnits(new[] { 40, 30 }, new[] { 90, 80 });
            errors += Expect("기습 성공 1라운드: 아군 먼저", Names(BattleQueue.ComputeOrder(a.Concat(e), 1, EncounterOutcome.AmbushSuccess)), "아군0,아군1,적0,적1");
            errors += Expect("기습 실패 1라운드: 적 먼저", Names(BattleQueue.ComputeOrder(a.Concat(e), 1, EncounterOutcome.AmbushFailure)), "적0,적1,아군0,아군1");
            errors += Expect("기습 성공 2라운드: 속도순 복귀", Names(BattleQueue.ComputeOrder(a.Concat(e), 2, EncounterOutcome.AmbushSuccess)), "적0,적1,아군0,아군1");
            errors += Expect("몰래 지나가기 실패: 페널티 없음", Names(BattleQueue.ComputeOrder(a.Concat(e), 1, EncounterOutcome.SneakFailure)), "적0,적1,아군0,아군1");
            return errors;
        }

        private static int CheckSkipsDownedAndRoundAdvance()
        {
            int errors = 0;
            var (a, e) = MakeUnits(new[] { 50, 40 }, new[] { 45 });
            var queue = new BattleQueue(a.Concat(e), EncounterOutcome.FightStarted);

            errors += Expect("1라운드 1번째", queue.Advance().DisplayName, "아군0");
            e[0].TakeDamage(99); // 적이 차례 전에 쓰러짐 → 건너뜀
            errors += Expect("쓰러진 참가자 건너뜀", queue.Advance().DisplayName, "아군1");
            a[1].HasRetreated = true;
            errors += Expect("새 라운드 시작", queue.Advance().DisplayName, "아군0");
            errors += Expect("라운드 번호", queue.Round.ToString(), "2");
            errors += Expect("퇴각한 참가자 빠짐", Names(queue.Preview(3).Select(x => x.Unit)), "아군0,아군0,아군0");
            return errors;
        }

        private static int Expect(string label, string actual, string expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[BattleQueueSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }
    }
}
