using System.Collections.Generic;
using System.Linq;
using Fantasia.Data;
using UnityEngine;

namespace Fantasia.Combat
{
    // D13 일반 적 행동 — 1막 소형 적은 기본 공격만 쓰고(특성은 크리티컬 시 OnCritStatuses/OnCritTurnShift),
    // 대상은 EnemyDefinition.Targeting 규칙으로 고른다. 보스 패턴(D14~)은 별도 클래스로.
    public static class EnemyAI
    {
        public static BattleUnit ChooseTarget(BattleUnit enemy, IReadOnlyList<BattleUnit> allies)
        {
            var candidates = allies.Where(a => a.InBattle).ToList();
            if (candidates.Count == 0) return null;

            return enemy.Enemy.Targeting switch
            {
                EnemyTargeting.LowestHP => candidates.OrderBy(a => a.CurrentHP).ThenBy(a => a.FormationIndex).First(),
                EnemyTargeting.LowestHPRatio => candidates.OrderBy(a => (float)a.CurrentHP / Mathf.Max(1, a.MaxHP)).ThenBy(a => a.FormationIndex).First(),
                EnemyTargeting.HighestAttack => candidates.OrderByDescending(a => Mathf.Max(a.Member.FinalPhysicalAttack, a.Member.FinalMagicAttack)).ThenBy(a => a.FormationIndex).First(),
                EnemyTargeting.Front => candidates.OrderBy(a => a.FormationIndex).First(),
                _ => candidates[Random.Range(0, candidates.Count)],
            };
        }
    }
}
