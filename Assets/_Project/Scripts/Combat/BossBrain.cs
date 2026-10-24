using System.Collections.Generic;
using Fantasia.Data;

namespace Fantasia.Combat
{
    // 보스 한 마리의 전투 중 상태 — 패턴 위치, 이미 발동한 HP 임계치, 예고 뒤 기다리는 후속 행동.
    public class BossState
    {
        public int PatternIndex;
        public readonly HashSet<int> FiredThresholds = new();
        public EnemyThresholdAction Pending;   // 예고했고 다음 행동으로 나갈 것
        public bool PendingCancelled;          // 저지됨 → 예정된 보스 차례를 "실패"로 소모
        public bool AllyTurnSinceAnnounce;     // 예고 뒤 아군이 명령을 고른 개인 턴이 있었는지

        public bool HasPending => Pending != null && !PendingCancelled;
    }

    // D14 보스 행동 결정 (System.md "보스 공통 규칙" + Boss_Abel_v1.0.md 확정). 전투 화면과 분리해 셀프 테스트로 검증.
    // 보스 차례마다: ① 예고한 후속 행동이 있으면 — 저지됐으면 "실패"로 차례 소모, 예고 뒤 아군 명령 턴이 없었으면 미룸,
    // 아니면 실행 → ② HP 임계치를 처음 넘었으면 예고 (피해 없음, 각 임계치 전투당 1회) → ③ 아니면 패턴을 순서대로.
    // 예고·실행·실패 차례는 패턴 위치를 넘기지 않는다 (끝나면 저장된 순서로 복귀).
    public static class BossBrain
    {
        public static EnemyAction NextAction(EnemyDefinition boss, BossState state, int currentHP, int maxHP)
        {
            if (state.Pending != null)
            {
                var pending = state.Pending;
                if (state.PendingCancelled)
                {
                    state.Pending = null;
                    state.PendingCancelled = false;
                    return new EnemyAction { Name = string.IsNullOrEmpty(pending.FailedName) ? pending.FollowUp.Name + " 실패" : pending.FailedName, Kind = EnemyActionKind.Fizzle, Description = "저지당해 이번 차례를 잃었다." };
                }
                // 예고부터 실행 전까지 아군 1명 이상에게 명령 턴 보장 — 없었으면 다음 보스 차례로 미룸
                if (!state.AllyTurnSinceAnnounce)
                    return new EnemyAction { Name = pending.Announce.Name + " 준비", Kind = EnemyActionKind.Fizzle, Description = "예고한 행동을 미룬다 (아군 차례 보장)." };
                state.Pending = null;
                return pending.FollowUp;
            }

            if (boss.Thresholds != null)
            {
                for (int i = 0; i < boss.Thresholds.Length; i++)
                {
                    var t = boss.Thresholds[i];
                    if (state.FiredThresholds.Contains(i) || currentHP > maxHP * t.HpRatio) continue;
                    state.FiredThresholds.Add(i);
                    if (t.FollowUp != null)
                    {
                        state.Pending = t;
                        state.PendingCancelled = false;
                        state.AllyTurnSinceAnnounce = false;
                    }
                    return t.Announce;
                }
            }

            if (!boss.HasPattern) return null;
            var action = boss.Pattern[state.PatternIndex % boss.Pattern.Length];
            state.PatternIndex++;
            return action;
        }

        // 아군이 행동을 고르는 개인 턴이 시작될 때 (예고 뒤 저지 기회 보장용)
        public static void NotifyAllyCommandTurn(BossState state)
        {
            if (state.HasPending) state.AllyTurnSinceAnnounce = true;
        }

        // 예고 중 공격 기술 명중 — 기술 계수가 기준 이상이고 실제 HP 피해 1 이상이면 저지 (크리티컬 배율은 더하지 않음).
        public static bool TryCancel(BossState state, float skillCoefficient, int damageDealt)
        {
            if (!state.HasPending || damageDealt < 1) return false;
            float min = state.Pending.CancelMinCoefficient;
            if (min <= 0f || skillCoefficient + 0.0001f < min) return false;
            state.PendingCancelled = true;
            return true;
        }

        // 전용 행동 (아벨 모로 「봉합 방해」): 판정 없이 저지. 이미 저지됐으면 false (중복 소모 방지).
        public static bool CanInterrupt(BossState state) => state.HasPending && !string.IsNullOrEmpty(state.Pending.InterruptName);

        public static bool TryInterrupt(BossState state)
        {
            if (!CanInterrupt(state)) return false;
            state.PendingCancelled = true;
            return true;
        }
    }
}
