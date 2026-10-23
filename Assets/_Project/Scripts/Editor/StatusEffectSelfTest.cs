using Fantasia.Combat;
using Fantasia.Data;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D12 완료 기준 검증 (System.md "상태 효과 수치와 처리 순서"):
    // 출혈 3턴×2·HP 1 하한, 중독 3스택·스택×3 피해·전 스탯 −5 1회·전투 뒤 유지, 같은 이름 갱신,
    // 속박 속도 −15가 다음 라운드 순서에 반영, 혼란 1턴은 다음 자기 턴에 적용, 공격력 −3.
    public static class StatusEffectSelfTest
    {
        [MenuItem("Fantasia/Data/Run Status Effect Self-Test")]
        public static void Run()
        {
            var job = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_명탐정.asset");
            if (job == null)
            {
                Debug.LogError("[StatusEffectSelfTest] 명탐정 자산이 없다 — 먼저 Fantasia/Data/Seed Sample Game Data 실행.");
                return;
            }

            int failures = 0;
            failures += CheckBleed(job);
            failures += CheckPoison(job);
            failures += CheckRefreshAndBind(job);
            failures += CheckConfusionTiming(job);
            failures += CheckAttackDown(job);

            if (failures == 0) Debug.Log("[StatusEffectSelfTest] 전부 통과.");
            else Debug.LogError($"[StatusEffectSelfTest] {failures}건 실패. 위 로그 확인할 것.");
        }

        private static StatusEffectDefinition Make(StatusEffectType type, int duration, int maxStacks, int magnitude, int statPenalty = 0, bool clearedByVictory = true)
        {
            var d = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            d.DisplayName = type.ToString();
            d.Effect = type;
            d.DurationTurns = duration;
            d.MaxStacks = maxStacks;
            d.Magnitude = magnitude;
            d.StatPenalty = statPenalty;
            d.ClearedByCombatVictory = clearedByVictory;
            return d;
        }

        private static int CheckBleed(JobDefinition job)
        {
            int errors = 0;
            var set = new StatusSet();
            set.Apply(Make(StatusEffectType.WeakBleed, 3, 1, 2));
            errors += Expect("출혈 1턴 피해", set.BeginTurn().Damage, 2);
            errors += Expect("출혈 2턴 피해", set.BeginTurn().Damage, 2);
            errors += Expect("출혈 3턴 피해", set.BeginTurn().Damage, 2);
            errors += Expect("출혈 3턴 뒤 해제", set.BeginTurn().Damage, 0);
            errors += Expect("지속 피해 HP 1 하한 (HP 2에 9 피해 → 1)", StatusSet.ClampDamageOverTime(9, 2), 1);
            errors += Expect("HP 1이면 지속 피해 0", StatusSet.ClampDamageOverTime(5, 1), 0);
            return errors;
        }

        private static int CheckPoison(JobDefinition job)
        {
            int errors = 0;
            var member = new PartyMember(job); // 명탐정 힘 71 + 단검 2 = 73
            var poison = Make(StatusEffectType.Poison, -1, 3, 3, statPenalty: 5, clearedByVictory: false);
            for (int i = 0; i < 4; i++) member.Statuses.Apply(poison);

            errors += Expect("중독 최대 3스택", member.Statuses.All[0].Stacks, 3);
            errors += Expect("중독 전 스탯 −5는 1회 (힘 73 → 68)", member.FinalStats.Strength, 68);
            errors += Expect("중독 피해 = 스택×3", member.Statuses.BeginTurn().Damage, 9);
            errors += Expect("중독은 정화 전까지 유지", member.Statuses.BeginTurn().Damage, 9);

            member.Statuses.Apply(Make(StatusEffectType.WeakBleed, 3, 1, 2));
            member.Statuses.ClearOnBattleEnd();
            errors += Expect("전투 뒤 중독만 남음", member.Statuses.All.Count, 1);
            errors += ExpectBool("남은 것은 중독", member.Statuses.Has(StatusEffectType.Poison), true);
            return errors;
        }

        private static int CheckRefreshAndBind(JobDefinition job)
        {
            int errors = 0;
            var member = new PartyMember(job); // 속도 70 + 단검 3 = 73
            var bind = Make(StatusEffectType.Bind, 2, 1, 15);
            member.Statuses.Apply(bind);
            member.Statuses.BeginTurn(); // 남은 1턴
            member.Statuses.Apply(bind); // 같은 이름 → 갱신(기간 2턴으로), 스택 없음
            errors += Expect("같은 이름 갱신 — 기간 리셋", member.Statuses.All[0].RemainingTurns, 2);
            errors += Expect("같은 이름 갱신 — 중첩 없음", member.Statuses.All[0].Stacks, 1);
            errors += Expect("속박 속도 −15 (73 → 58)", member.FinalStats.Speed, 58);

            // 속도 변화는 다음 라운드 정렬에 반영: 속박 전엔 명탐정(73) > 적(60), 속박 후엔 적(60) > 명탐정(58).
            var ally = new BattleUnit(member, 0);
            var enemyDef = ScriptableObject.CreateInstance<EnemyDefinition>();
            enemyDef.DisplayName = "적";
            enemyDef.Speed = 60;
            enemyDef.MaxHP = 10;
            var order = BattleQueue.ComputeOrder(new[] { ally, new BattleUnit(enemyDef, 0) }, 2, EncounterOutcome.FightStarted);
            errors += ExpectBool("속박 후 적이 먼저", !order[0].IsAlly, true);
            return errors;
        }

        private static int CheckConfusionTiming(JobDefinition job)
        {
            int errors = 0;
            var set = new StatusSet();
            set.Apply(Make(StatusEffectType.Confusion, 1, 1, 0));
            errors += ExpectBool("혼란 1턴: 다음 자기 턴 행동에 적용", set.BeginTurn().Confused, true);
            errors += ExpectBool("혼란은 그 턴이 지나면 해제", set.BeginTurn().Confused, false);
            return errors;
        }

        private static int CheckAttackDown(JobDefinition job)
        {
            var member = new PartyMember(job); // 공격력 8 + 단검 5 = 13
            member.Statuses.Apply(Make(StatusEffectType.AttackDown, 2, 1, 3));
            return Expect("공격력 −3 (13 → 10)", member.FinalPhysicalAttack, 10);
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[StatusEffectSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectBool(string label, bool actual, bool expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[StatusEffectSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }
    }
}
