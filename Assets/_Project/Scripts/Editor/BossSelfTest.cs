using System.Linq;
using System.Collections.Generic;
using Fantasia.Combat;
using Fantasia.Data;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D14 검시관 아벨 모로 — System.md "보스급" + Boss_Abel_v1.0.md(기획 확정 2026-10-25) 수치·패턴·봉합 저지와 대조.
    public static class BossSelfTest
    {
        [MenuItem("Fantasia/Data/Run Boss Self-Test")]
        public static void Run()
        {
            var boss = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_검시관아벨모로.asset");
            if (boss == null) { Debug.LogError("[BossSelfTest] 아벨 모로 자산 없음 — Seed Sample Game Data 먼저"); return; }
            int e = 0;

            // 수치 (Boss_Abel_v1.0 기획 확정: HP 300 · 공격 24 · 마력 30, 나머지는 System.md 그대로)
            e += Expect("HP", boss.MaxHP, 300);
            e += Expect("속도", boss.Speed, 58);
            e += Expect("공격", boss.PhysicalAttack, 24);
            e += Expect("마력", boss.MagicAttack, 30);
            e += Expect("물리 방어", boss.PhysicalDefense, 6);
            e += Expect("마법 방어", boss.MagicDefense, 10);
            e += Expect("슬롯 4", boss.SlotCount, 4);
            e += Check(Mathf.Approximately(boss.SlotSuccessRate, 0.32f), "슬롯 성공률 32%");
            e += Check(boss.Targeting == EnemyTargeting.LowestHPRatio, "단일 공격 대상 = HP 비율 가장 낮은 아군");

            // 패턴: 해부의 일격 → 해부의 일격 → 봉인된 수은 반복
            var s = new BossState();
            var seq = new List<string>();
            for (int i = 0; i < 4; i++) seq.Add(BossBrain.NextAction(boss, s, 300, 300).Name);
            e += Check(string.Join(",", seq) == "해부의 일격,해부의 일격,봉인된 수은,해부의 일격", $"패턴 순서 ({string.Join(",", seq)})");
            var mercury = boss.Pattern[2];
            e += Check(mercury.Kind == EnemyActionKind.AreaAttack && mercury.UsesMagic && Mathf.Approximately(mercury.Coefficient, 0.8f), "봉인된 수은 = 마법 0.8M 전체");
            e += Check(boss.Pattern[0].OnCritStatuses.Length == 1 && boss.Pattern[0].OnCritStatuses[0].Effect == StatusEffectType.WeakBleed, "해부의 일격 크리티컬 → 약한 출혈");
            e += Check(mercury.OnHitStatuses.Length == 1 && mercury.OnHitStatuses[0].Effect == StatusEffectType.Poison && mercury.OnCritStatuses.Length == 0, "봉인된 수은 명중 → 중독 1스택 확정 (크리티컬 추가 없음)");

            // HP 60% 초과(181)면 예고 없음, 180 이하면 예고 → 아군 명령 턴 뒤 다음 보스 차례에 HP 60 회복
            s = new BossState();
            e += Check(BossBrain.NextAction(boss, s, 181, 300).Kind != EnemyActionKind.Announce, "HP 181에서는 예고 없음");
            var announce = BossBrain.NextAction(boss, s, 180, 300);
            e += Check(announce.Kind == EnemyActionKind.Announce && announce.Name == "금지된 봉합", "HP 180에서 금지된 봉합 예고");
            var wait = BossBrain.NextAction(boss, s, 170, 300);
            e += Check(wait.Kind == EnemyActionKind.Fizzle && s.HasPending, "예고 뒤 아군 명령 턴이 없으면 회복을 미룸");
            BossBrain.NotifyAllyCommandTurn(s);
            var heal = BossBrain.NextAction(boss, s, 170, 300);
            e += Check(heal.Kind == EnemyActionKind.SelfHeal && heal.HealAmount == 60, "아군 턴 뒤 다음 보스 차례 = HP 60 회복");
            e += Check(BossBrain.NextAction(boss, s, 50, 300).Kind != EnemyActionKind.Announce, "봉합은 전투당 1회 (회복 뒤 다시 60% 이하여도 반복 안 함)");

            // 패턴 위치 유지: 일격·일격 → 예고 → 회복 → 다음은 봉인된 수은 (처음부터 다시 아님)
            s = new BossState();
            BossBrain.NextAction(boss, s, 300, 300);
            BossBrain.NextAction(boss, s, 300, 300);
            BossBrain.NextAction(boss, s, 150, 300); // 예고
            BossBrain.NotifyAllyCommandTurn(s);
            BossBrain.NextAction(boss, s, 150, 300); // 회복
            e += Check(BossBrain.NextAction(boss, s, 210, 300).Name == "봉인된 수은", "봉합 뒤 저장된 패턴 순서로 복귀");

            // 공격 저지: 1.2배 이상 + 실제 피해 1 이상만 (0.9배 저지 사격·피해 0은 안 됨)
            s = new BossState();
            BossBrain.NextAction(boss, s, 150, 300); // 예고
            e += Check(!BossBrain.TryCancel(s, 0.9f, 20), "0.9배 저지 사격으로는 취소 안 됨");
            e += Check(!BossBrain.TryCancel(s, 1.19f, 20), "1.19배로는 취소 안 됨");
            e += Check(!BossBrain.TryCancel(s, 1.3f, 0), "피해 0이면 취소 안 됨");
            e += Check(BossBrain.TryCancel(s, 1.2f, 1), "1.2배(관통탄)·피해 1로 취소");
            BossBrain.NotifyAllyCommandTurn(s);
            var failed = BossBrain.NextAction(boss, s, 150, 300);
            e += Check(failed.Kind == EnemyActionKind.Fizzle && failed.Name == "봉합 실패", "저지되면 예정된 회복 차례를 봉합 실패로 소모");
            e += Check(BossBrain.NextAction(boss, s, 150, 300).Kind == EnemyActionKind.Attack, "실패 뒤 패턴 복귀");
            e += Check(BossBrain.NextAction(boss, s, 100, 300).Kind != EnemyActionKind.Announce, "저지돼도 봉합 사용 완료 — 재예약 없음");

            // 봉합 방해: 예고 중에만, 판정 없이 취소, 두 번 눌러도 한 번
            s = new BossState();
            e += Check(!BossBrain.CanInterrupt(s), "예고 전엔 봉합 방해 없음");
            BossBrain.NextAction(boss, s, 150, 300);
            e += Check(BossBrain.CanInterrupt(s) && s.Pending.InterruptName == "봉합 방해", "예고 중 봉합 방해 가능");
            e += Check(BossBrain.TryInterrupt(s), "봉합 방해로 취소");
            e += Check(!BossBrain.TryInterrupt(s) && !BossBrain.TryCancel(s, 1.3f, 30), "이미 저지됐으면 중복 처리 없음");

            // 1막 진행: 처치 → 보고 목표 → 보고 → 1막 완료·베르사유 목표 (다시 불러도 중복 없음)
            Fantasia.Core.BoardSession.EnsureExists();
            var bs = Fantasia.Core.BoardSession.Instance;
            bs.ResetForNewGame();
            e += Check(!Fantasia.Core.ActOneStory.NeedsReport(bs) && bs.Quests.Any(q => q.Title == Fantasia.Core.ActOneStory.QuestCase), "새 게임: 제임스의 의뢰 진행 중");
            Fantasia.Core.ActOneStory.MarkBossDefeated(bs);
            Fantasia.Core.ActOneStory.MarkBossDefeated(bs);
            e += Check(Fantasia.Core.ActOneStory.NeedsReport(bs) && bs.Quests.Count(q => q.Title == Fantasia.Core.ActOneStory.QuestReport) == 1, "처치 → 제임스에게 보고 (중복 없음)");
            Fantasia.Core.ActOneStory.CompleteReport(bs);
            Fantasia.Core.ActOneStory.CompleteReport(bs);
            e += Check(!Fantasia.Core.ActOneStory.NeedsReport(bs) && bs.Quests.Count(q => q.Title == Fantasia.Core.ActOneStory.QuestNext) == 1 && bs.CompletedQuests.Count(q => q.Title == Fantasia.Core.ActOneStory.QuestReport) == 1, "보고 → 1막 완료·베르사유 목표 (재실행 중복 없음)");
            bs.ResetForNewGame();

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
