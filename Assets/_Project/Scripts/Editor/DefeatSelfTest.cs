using Fantasia.Combat;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D21 완료 기준 검증 (System.md "레벨·기본 자원" + "패배 시 약탈"):
    // LP 0이어도 HP가 남으면 생존, 부활 HP 30%/50%·출혈·중독 제거·그 밖의 상태 유지, LP 소모,
    // 영구 사망·전원 영구 사망, 약탈 확률표(10/40/30/15/5, 평균 16.5%)와 소수점 버림.
    public static class DefeatSelfTest
    {
        [MenuItem("Fantasia/Data/Run Defeat Self-Test")]
        public static void Run()
        {
            var job = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_명탐정.asset");
            if (job == null)
            {
                Debug.LogError("[DefeatSelfTest] 명탐정 자산이 없다 — 먼저 Fantasia/Data/Seed Sample Game Data 실행.");
                return;
            }

            int failures = 0;
            failures += CheckLpAndRevive(job);
            failures += CheckPermanentDeath(job);
            failures += CheckPlunder();

            if (failures == 0) Debug.Log("[DefeatSelfTest] 전부 통과.");
            else Debug.LogError($"[DefeatSelfTest] {failures}건 실패. 위 로그 확인할 것.");
        }

        private static StatusEffectDefinition Status(StatusEffectType type)
        {
            var d = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            d.Effect = type;
            d.DurationTurns = type == StatusEffectType.Poison ? -1 : 3;
            d.MaxStacks = type == StatusEffectType.Poison ? 3 : 1;
            d.Magnitude = 2;
            return d;
        }

        private static int CheckLpAndRevive(JobDefinition job)
        {
            int errors = 0;
            var m = new PartyMember(job); // 명탐정 HP 108, LP 3
            while (m.TrySpendLP()) { }
            errors += ExpectBool("LP 0이어도 HP가 남으면 생존", m.IsAlive && !m.IsDowned, true);
            errors += ExpectBool("LP 0이면 더 쓸 수 없음", m.TrySpendLP(), false);

            var r = new PartyMember(job);
            r.Statuses.Apply(Status(StatusEffectType.WeakBleed));
            r.Statuses.Apply(Status(StatusEffectType.Poison));
            r.Statuses.Apply(Status(StatusEffectType.AttackDown)); // 저주 대신 "부활로 안 지워지는 상태" 확인용
            r.TakeDamage(999);
            errors += ExpectBool("HP 0 = 쓰러짐", r.IsDowned, true);

            errors += ExpectBool("LP 1 소모", r.TrySpendLP() && r.CurrentLP == 2, true);
            r.Revive(DefeatRules.InBattleReviveHpRatio);
            errors += Expect("전투 중 부활 HP 30% (108 → 32)", r.CurrentHP, 32);
            errors += ExpectBool("부활 시 출혈 제거", r.Statuses.Has(StatusEffectType.WeakBleed), false);
            errors += ExpectBool("부활 시 중독 제거", r.Statuses.Has(StatusEffectType.Poison), false);
            errors += ExpectBool("다른 상태는 유지", r.Statuses.Has(StatusEffectType.AttackDown), true);

            r.TakeDamage(999);
            r.Revive(DefeatRules.WipeReturnHpRatio);
            errors += Expect("전원 쓰러짐 복귀 HP 50% (108 → 54)", r.CurrentHP, 54);
            return errors;
        }

        private static int CheckPermanentDeath(JobDefinition job)
        {
            int errors = 0;
            var party = new Party();
            var a = new PartyMember(job);
            var b = new PartyMember(job);
            party.AddMember(a);
            party.AddMember(b);

            a.MarkPermanentlyDead();
            a.Heal(50);
            errors += ExpectBool("영구 사망은 회복되지 않음", a.CurrentHP == 0 && a.IsPermanentlyDead && !a.IsDowned, true);
            errors += ExpectBool("한 명 남으면 계속", party.IsWipedOut, false);
            b.MarkPermanentlyDead();
            errors += ExpectBool("전원 영구 사망 → 플레이 종료", party.IsWipedOut, true);
            return errors;
        }

        private static int CheckPlunder()
        {
            int errors = 0;
            var party = new Party(); // 180 Cent
            errors += Expect("약탈 30% (180 → 54 손실)", party.LoseCentPercent(30), 54);
            errors += Expect("남은 Cent", party.Cent, 126);
            errors += Expect("소수점 버림 (126의 10% = 12)", party.LoseCentPercent(10), 12);

            const int trials = 40000;
            var counts = new int[41];
            long sum = 0;
            for (int i = 0; i < trials; i++)
            {
                int p = DefeatRules.RollPlunderPercent();
                counts[p]++;
                sum += p;
            }
            errors += ExpectNear("없음 10%", counts[0] / (float)trials, 0.10f);
            errors += ExpectNear("10% 확률 40%", counts[10] / (float)trials, 0.40f);
            errors += ExpectNear("20% 확률 30%", counts[20] / (float)trials, 0.30f);
            errors += ExpectNear("30% 확률 15%", counts[30] / (float)trials, 0.15f);
            errors += ExpectNear("40% 확률 5%", counts[40] / (float)trials, 0.05f);
            errors += ExpectNear("평균 손실 16.5%", sum / (float)trials / 100f, 0.165f);
            return errors;
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[DefeatSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectBool(string label, bool actual, bool expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[DefeatSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectNear(string label, float actual, float expected)
        {
            if (Mathf.Abs(actual - expected) <= 0.015f) return 0;
            Debug.LogError($"[DefeatSelfTest] {label}: 기대값 {expected:P1}, 측정 {actual:P1}");
            return 1;
        }
    }
}
