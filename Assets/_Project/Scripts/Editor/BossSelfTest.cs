using System.Collections.Generic;
using Fantasia.Combat;
using Fantasia.Data;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D14 검시관 아벨 모로 — System.md "보스급" 수치·패턴과 대조.
    public static class BossSelfTest
    {
        [MenuItem("Fantasia/Data/Run Boss Self-Test")]
        public static void Run()
        {
            var boss = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_검시관아벨모로.asset");
            if (boss == null) { Debug.LogError("[BossSelfTest] 아벨 모로 자산 없음 — Seed Sample Game Data 먼저"); return; }
            int e = 0;

            // 수치 (System.md 보스 표)
            e += Expect("HP", boss.MaxHP, 180);
            e += Expect("속도", boss.Speed, 58);
            e += Expect("공격", boss.PhysicalAttack, 18);
            e += Expect("마력", boss.MagicAttack, 24);
            e += Expect("물리 방어", boss.PhysicalDefense, 6);
            e += Expect("마법 방어", boss.MagicDefense, 10);
            e += Expect("슬롯 4", boss.SlotCount, 4);
            e += Check(Mathf.Approximately(boss.SlotSuccessRate, 0.32f), "슬롯 성공률 32%");

            // 패턴: 해부의 일격 → 해부의 일격 → 봉인된 수은 반복
            var s = new BossState();
            var seq = new List<string>();
            for (int i = 0; i < 4; i++) seq.Add(BossBrain.NextAction(boss, s, 180, 180).Name);
            e += Check(string.Join(",", seq) == "해부의 일격,해부의 일격,봉인된 수은,해부의 일격", $"패턴 순서 ({string.Join(",", seq)})");
            var mercury = boss.Pattern[2];
            e += Check(mercury.Kind == EnemyActionKind.AreaAttack && mercury.UsesMagic && Mathf.Approximately(mercury.Coefficient, 0.8f), "봉인된 수은 = 마법 0.8M 전체");
            e += Check(boss.Pattern[0].OnCritStatuses.Length == 1 && boss.Pattern[0].OnCritStatuses[0].Effect == StatusEffectType.WeakBleed, "해부의 일격 크리티컬 → 약한 출혈");
            e += Check(mercury.OnCritStatuses.Length == 1 && mercury.OnCritStatuses[0].Effect == StatusEffectType.Poison, "봉인된 수은 크리티컬 → 중독");

            // HP 60% 초과면 예고 없음, 60% 이하(108)면 금지된 봉합 예고 → 다음 행동 회복 30
            s = new BossState();
            e += Check(BossBrain.NextAction(boss, s, 109, 180).Kind != EnemyActionKind.Announce, "HP 109(60.5%)에서는 예고 없음");
            var announce = BossBrain.NextAction(boss, s, 108, 180);
            e += Check(announce.Kind == EnemyActionKind.Announce && announce.Name == "금지된 봉합", "HP 108(60%)에서 금지된 봉합 예고");
            var heal = BossBrain.NextAction(boss, s, 100, 180);
            e += Check(heal.Kind == EnemyActionKind.SelfHeal && heal.HealAmount == 30, "예고 다음 행동 = HP 30 회복");
            e += Check(BossBrain.NextAction(boss, s, 50, 180).Kind != EnemyActionKind.Announce, "임계치는 1회만 (회복 뒤 다시 60% 이하여도 반복 안 함)");

            // 취소: 1.0배 기본 공격으론 안 되고, 1.2배 이상 공격 기술이면 취소 → 회복 대신 평소 패턴
            s = new BossState();
            BossBrain.NextAction(boss, s, 100, 180); // 예고
            e += Check(!BossBrain.TryCancel(s, 1.0f), "1.0배로는 취소 안 됨");
            e += Check(!BossBrain.TryCancel(s, 1.19f), "1.19배로는 취소 안 됨");
            e += Check(BossBrain.TryCancel(s, 1.2f), "1.2배(관통탄)로 취소");
            e += Check(BossBrain.NextAction(boss, s, 100, 180).Kind != EnemyActionKind.SelfHeal, "취소되면 회복하지 않음");

            // 봉쇄·보상
            e += Check(!EncounterRules.AllowsPreCombatAvoidance(boss), "몰래 지나가기·전투 전 후퇴 불가");
            e += Check(boss.BlocksEscape, "전투 중 퇴각·탈출권 불가");
            var party = new Party();
            var rewards = LootRules.Roll(new List<EnemyDefinition> { boss }, party, ItemCatalog.Current, new System.Random(7));
            e += Expect("보상 Cent 180", rewards.Cent, 180);
            e += Expect("경험치 100", rewards.Experience, 100);
            e += Expect("보상 4칸 고정", rewards.SlotCount, 4);
            e += Check(System.Array.IndexOf(boss.KeyItemRewards, "검시 기록") >= 0 && System.Array.IndexOf(boss.KeyItemRewards, "붉은 룬 조각") >= 0, "검시 기록·붉은 룬 조각 확정");

            if (e == 0) Debug.Log("[BossSelfTest] 전부 통과.");
            else Debug.LogError($"[BossSelfTest] {e}건 실패.");
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[BossSelfTest] {label}: 기대 {expected}, 실제 {actual}");
            return 1;
        }

        private static int Check(bool ok, string label)
        {
            if (ok) return 0;
            Debug.LogError($"[BossSelfTest] 실패: {label}");
            return 1;
        }
    }
}
