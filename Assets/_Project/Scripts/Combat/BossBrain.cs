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
        public bool PendingCancelled;

        public bool HasPending => Pending != null && !PendingCancelled;
    }

    // D14 보스 행동 결정 (System.md "보스 공통 규칙" + 보스별 기술 표). 전투 화면과 분리해 셀프 테스트로 검증.
    // 보스 차례마다: ① 예고한 후속 행동이 있으면 그것(취소됐으면 건너뜀) → ② HP 임계치를 처음 넘었으면 예고
    // (피해 없음, 각 임계치 1회) → ③ 아니면 패턴을 순서대로.
    public static class BossBrain
    {
        public static EnemyAction NextAction(EnemyDefinition boss, BossState state, int currentHP, int maxHP)
        {
            if (state.Pending != null)
            {
                var pending = state.Pending;
                bool cancelled = state.PendingCancelled;
                state.Pending = null;
                state.PendingCancelled = false;
                // 취소된 후속 행동은 버리고 평소 패턴으로 (☆ 개발 임시 — 기획: "회복 취소"만 명시)
                if (!cancelled) return pending.FollowUp;
            }

            if (boss.Thresholds != null)
            {
                for (int i = 0; i < boss.Thresholds.Length; i++)
                {
                    var t = boss.Thresholds[i];
                    if (state.FiredThresholds.Contains(i) || currentHP > maxHP * t.HpRatio) continue;
                    state.FiredThresholds.Add(i);
                    if (t.FollowUp != null) state.Pending = t;
                    return t.Announce;
                }
            }

            if (!boss.HasPattern) return null;
            var action = boss.Pattern[state.PatternIndex % boss.Pattern.Length];
            state.PatternIndex++;
            return action;
        }

        // 예고와 후속 행동 사이에 공격 기술을 맞혔을 때 — 계수가 기준 이상이면 후속 행동 취소. 취소했으면 true.
        public static bool TryCancel(BossState state, float attackCoefficient)
        {
            if (!state.HasPending) return false;
            float min = state.Pending.CancelMinCoefficient;
            if (min <= 0f || attackCoefficient + 0.0001f < min) return false;
            state.PendingCancelled = true;
            return true;
        }
    }
}
