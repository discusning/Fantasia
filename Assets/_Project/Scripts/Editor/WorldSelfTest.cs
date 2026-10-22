using Fantasia.Data;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D05·D06 완료 기준 검증: 이동력 범위, 이벤트 보너스 범위, 월드 라운드 진행 규칙.
    public static class WorldSelfTest
    {
        private const string JobDir = "Assets/_Project/Data/Jobs";
        private const int Trials = 5000;

        [MenuItem("Fantasia/Data/Run World Clock & Movement Self-Test")]
        public static void Run()
        {
            int failures = 0;
            failures += CheckMovementRollRange();
            failures += CheckReaperPenalty();
            failures += CheckEventBonusRange();
            failures += CheckWorldClockRoundAdvance();
            failures += CheckWorldClockSkipsDead();
            failures += CheckEventBonusOncePerTurn();

            if (failures == 0)
                Debug.Log("[WorldSelfTest] 전부 통과.");
            else
                Debug.LogError($"[WorldSelfTest] {failures}건 실패. 위 로그 확인할 것.");
        }

        private static int CheckMovementRollRange()
        {
            int errors = 0;
            for (int i = 0; i < Trials; i++)
            {
                int roll = MovementRules.RollMovePoints(pursuedByReaper: false);
                if (roll < 1 || roll > 6)
                {
                    Debug.LogError($"[WorldSelfTest] RollMovePoints(false)가 범위 밖 값 {roll}을 냈다.");
                    errors++;
                    break;
                }
            }
            return errors;
        }

        private static int CheckReaperPenalty()
        {
            int errors = 0;
            for (int i = 0; i < Trials; i++)
            {
                int roll = MovementRules.RollMovePoints(pursuedByReaper: true);
                if (roll < 0 || roll > 4) // 1~6 - 2, 최소 0
                {
                    Debug.LogError($"[WorldSelfTest] RollMovePoints(true, 사신 추격)가 범위 밖 값 {roll}을 냈다.");
                    errors++;
                    break;
                }
            }
            return errors;
        }

        private static int CheckEventBonusRange()
        {
            int errors = 0;
            for (int i = 0; i < Trials; i++)
            {
                int bonus = MovementRules.RollEventBonusMove();
                if (bonus < 0 || bonus > 2)
                {
                    Debug.LogError($"[WorldSelfTest] RollEventBonusMove가 범위 밖 값 {bonus}을 냈다.");
                    errors++;
                    break;
                }
            }
            return errors;
        }

        private static (Party party, PartyMember a, PartyMember b, PartyMember c) BuildTestParty()
        {
            var detective = AssetDatabase.LoadAssetAtPath<JobDefinition>($"{JobDir}/Job_명탐정.asset");
            var plagueDoctor = AssetDatabase.LoadAssetAtPath<JobDefinition>($"{JobDir}/Job_역병의사.asset");
            var sniper = AssetDatabase.LoadAssetAtPath<JobDefinition>($"{JobDir}/Job_저격수.asset");

            var party = new Party();
            var a = new PartyMember(detective);
            var b = new PartyMember(plagueDoctor);
            var c = new PartyMember(sniper);
            party.AddMember(a);
            party.AddMember(b);
            party.AddMember(c);
            return (party, a, b, c);
        }

        private static int CheckWorldClockRoundAdvance()
        {
            var (party, a, b, c) = BuildTestParty();
            var clock = new WorldClock();
            int errors = 0;

            errors += Expect("초기 월드 라운드", clock.WorldRound, 1);

            clock.NotifyWorldActionTaken(party, a);
            errors += Expect("2명 중 1명만 행동 — 라운드 안 바뀜", clock.WorldRound, 1);

            clock.NotifyWorldActionTaken(party, b);
            clock.NotifyWorldActionTaken(party, c);
            errors += Expect("전원 행동 후 라운드 +1", clock.WorldRound, 2);

            return errors;
        }

        private static int CheckWorldClockSkipsDead()
        {
            var (party, a, b, c) = BuildTestParty();
            c.TakeDamage(9999); // 사망 처리
            var clock = new WorldClock();
            int errors = 0;

            clock.NotifyWorldActionTaken(party, a);
            clock.NotifyWorldActionTaken(party, b);
            // c는 사망했으니 행동 안 해도 라운드가 넘어가야 한다 (생존자 기준).
            errors += Expect("사망자 제외, 생존자만 행동해도 라운드 +1", clock.WorldRound, 2);

            return errors;
        }

        private static int CheckEventBonusOncePerTurn()
        {
            var (_, a, _, _) = BuildTestParty();
            var clock = new WorldClock();
            int errors = 0;

            clock.BeginTurn(a);
            bool first = clock.TryConsumeEventBonus(a);
            bool second = clock.TryConsumeEventBonus(a); // 연쇄 금지 — 같은 턴에 두 번째는 실패해야 함

            errors += ExpectBool("첫 이벤트 보너스 사용 성공", first, true);
            errors += ExpectBool("같은 턴 두 번째 사용은 실패(연쇄 금지)", second, false);

            clock.BeginTurn(a); // 다음 턴 시작 — 다시 가능해야 함
            bool thirdAfterNewTurn = clock.TryConsumeEventBonus(a);
            errors += ExpectBool("새 턴 시작 후 다시 사용 가능", thirdAfterNewTurn, true);

            return errors;
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[WorldSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectBool(string label, bool actual, bool expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[WorldSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }
    }
}
