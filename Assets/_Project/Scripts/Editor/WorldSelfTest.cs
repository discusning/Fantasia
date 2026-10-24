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
            failures += CheckTwoPhaseRound();
            failures += CheckRollOncePerAllyTurn();
            failures += CheckEventBonusOncePerTurn();
            failures += CheckLeaderSuccession();
            failures += CheckPoisonWorldTick();

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

        // 월드 라운드 = [아군 턴] → [적 턴] (2026-10-24 기획 결정).
        private static int CheckTwoPhaseRound()
        {
            var clock = new WorldClock();
            int errors = 0;
            errors += Expect("초기 월드 라운드", clock.WorldRound, 1);
            errors += ExpectBool("처음은 아군 턴", clock.Phase == WorldPhase.AllyTurn, true);

            clock.EndAllyTurn();
            errors += ExpectBool("아군 턴 뒤 적 턴", clock.Phase == WorldPhase.EnemyTurn, true);
            errors += Expect("적 턴 중엔 라운드 그대로", clock.WorldRound, 1);

            clock.EndEnemyTurn();
            errors += ExpectBool("적 턴 뒤 다시 아군 턴", clock.Phase == WorldPhase.AllyTurn, true);
            errors += Expect("아군 턴 + 적 턴 = 라운드 +1", clock.WorldRound, 2);

            clock.EndEnemyTurn(); // 아군 턴에 적 턴 종료를 불러도 라운드가 늘지 않는다
            errors += Expect("페이즈 순서가 안 맞으면 무시", clock.WorldRound, 2);
            return errors;
        }

        private static int CheckRollOncePerAllyTurn()
        {
            var clock = new WorldClock();
            int errors = 0;
            errors += ExpectBool("아군 턴 첫 주사위", clock.TryBeginRoll(), true);
            errors += ExpectBool("같은 턴 두 번째 주사위 불가", clock.TryBeginRoll(), false);
            clock.EndAllyTurn();
            errors += ExpectBool("적 턴엔 주사위 불가", clock.TryBeginRoll(), false);
            clock.EndEnemyTurn();
            errors += ExpectBool("다음 아군 턴엔 다시 가능", clock.TryBeginRoll(), true);
            return errors;
        }

        private static int CheckEventBonusOncePerTurn()
        {
            var clock = new WorldClock();
            int errors = 0;
            errors += ExpectBool("첫 이벤트 보너스 사용 성공", clock.TryConsumeEventBonus(), true);
            errors += ExpectBool("같은 아군 턴 두 번째는 실패(연쇄 금지)", clock.TryConsumeEventBonus(), false);
            clock.EndAllyTurn();
            clock.EndEnemyTurn();
            errors += ExpectBool("다음 아군 턴엔 다시 가능", clock.TryConsumeEventBonus(), true);
            return errors;
        }

        // 파티장 = 1번 고정, 1번이 쓰러지거나 영구 사망하면 다음 순번 생존자 (개발 임시 결정).
        private static int CheckLeaderSuccession()
        {
            var (party, a, b, _) = BuildTestParty();
            int errors = 0;
            errors += ExpectBool("파티장 = 1번", party.Leader == a, true);
            a.TakeDamage(9999);
            errors += ExpectBool("1번이 쓰러지면 2번", party.Leader == b, true);
            a.MarkPermanentlyDead();
            errors += ExpectBool("1번 영구 사망 시에도 2번", party.Leader == b, true);
            return errors;
        }

        // 중독은 전투 밖에서 아군 턴 시작에 1회 피해 (개발 임시 결정), HP 1 하한.
        private static int CheckPoisonWorldTick()
        {
            var (party, a, _, _) = BuildTestParty();
            var poison = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            poison.Effect = StatusEffectType.Poison;
            poison.DurationTurns = -1;
            poison.MaxStacks = 3;
            poison.Magnitude = 3;
            a.Statuses.Apply(poison);
            int before = a.CurrentHP;
            var log = WorldTurnRules.BeginAllyTurn(party);
            int errors = 0;
            errors += Expect("아군 턴 시작 중독 피해 3", before - a.CurrentHP, 3);
            errors += Expect("로그 1줄 (중독 걸린 1명)", log.Count, 1);
            a.TakeDamage(a.CurrentHP - 1);
            WorldTurnRules.BeginAllyTurn(party);
            errors += Expect("HP 1 하한", a.CurrentHP, 1);
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
