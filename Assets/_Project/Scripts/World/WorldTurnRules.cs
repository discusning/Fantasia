using System.Collections.Generic;
using Fantasia.Data;

namespace Fantasia.World
{
    // 아군 턴 시작·적 턴에 일어나는 월드 처리 (Docs/Planning/Decisions.md — 월드 보드가 아직 구상 전이라
    // 개발 임시 결정 포함). 피의 흔적 G·영혼 수확 S·사신 추격·상점 재고는 해당 업무(D24·D25·D20)에서
    // 이 두 지점에 붙인다 — 지금은 자리만 있다.
    public static class WorldTurnRules
    {
        // 아군 턴 시작: 전투 밖에서도 남는 상태(중독)의 지속 피해 1회 — HP 1 하한, 기간 감소.
        public static List<string> BeginAllyTurn(Party party)
        {
            var log = new List<string>();
            if (party == null) return log;
            foreach (var member in party.LivingMembers)
            {
                if (member.IsDowned) continue;
                var start = member.Statuses.BeginTurn();
                if (start.Damage <= 0) continue;
                int dot = StatusSet.ClampDamageOverTime(start.Damage, member.CurrentHP);
                member.TakeDamage(dot);
                log.Add($"{member.Job.DisplayName} {start.Summary} → -{dot} (HP {member.CurrentHP}/{member.MaxHP})");
            }
            return log;
        }

        // 적 턴: 보드 위 적은 고정 배치(이동·생성 없음). 월드 라운드 단위 처리만 여기서.
        public static List<string> RunEnemyTurn(WorldClock clock)
        {
            var log = new List<string>();
            // D24 피의 흔적: G +10, 100이면 인조 사신 생성 / 사신 추격 1칸
            // D25 영혼 수확: S +2
            // D20 상점: 3월드 라운드마다 재고 보충
            return log;
        }
    }
}
