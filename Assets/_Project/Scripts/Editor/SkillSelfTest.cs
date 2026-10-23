using System.Linq;
using Fantasia.Combat;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D11 완료 기준 검증 (System.md 1차 3직업 스킬 표): 계수·관통·고유 판정 슬롯, 회복량,
    // 방어 감소·회피 상태 해제·영점 조정, 쿨다운(자신의 다음 N개 개인 턴 불가), 비용 부족 사유, 패시브 값.
    // GameDataSeed로 만든 직업·스킬·적 자산이 있어야 돈다.
    public static class SkillSelfTest
    {
        private const string Root = "Assets/_Project/Data";

        [MenuItem("Fantasia/Data/Run Skill Self-Test")]
        public static void Run()
        {
            var detectiveJob = Load<JobDefinition>("Jobs/Job_명탐정");
            var doctorJob = Load<JobDefinition>("Jobs/Job_역병의사");
            var sniperJob = Load<JobDefinition>("Jobs/Job_저격수");
            var hound = Load<EnemyDefinition>("Enemies/Enemy_그림자사냥개");
            if (detectiveJob == null || doctorJob == null || sniperJob == null || hound == null)
            {
                Debug.LogError("[SkillSelfTest] 시드 자산이 없다 — 먼저 Fantasia/Data/Seed Sample Game Data 실행.");
                return;
            }

            int failures = 0;
            var detective = new BattleUnit(new PartyMember(detectiveJob), 0);
            var doctor = new BattleUnit(new PartyMember(doctorJob), 1);
            var sniper = new BattleUnit(new PartyMember(sniperJob), 2);

            failures += Expect("명탐정 액티브 3개", detective.ActiveSkills().Count(), 3);
            failures += Expect("역병 의사 액티브 3개", doctor.ActiveSkills().Count(), 3);
            failures += Expect("저격수 액티브 3개", sniper.ActiveSkills().Count(), 3);
            failures += ExpectFloat("현장 검증 기습 +10%p", detective.Member.PassiveValue(PassiveType.AmbushSlotBonus), 0.1f);
            failures += ExpectFloat("첫 탄의 침묵 +15%", sniper.Member.PassiveValue(PassiveType.FirstGunShotBonus), 0.15f);

            // 일점 돌파 1.3A (명탐정 공격력 13): 일반 floor(16.9)=16, 크리 floor(33.8)=33
            var breach = Skill(detective, "skill.detective.pinpoint_breach");
            var req = detective.SkillAttack(breach, false);
            failures += Expect("일점 돌파 일반", AttackResolver.NormalDamage(req.Coefficient, req.AttackValue, 0), 16);
            failures += Expect("일점 돌파 크리", AttackResolver.CriticalDamage(req.Coefficient, req.AttackValue), 33);
            failures += Expect("일점 돌파 = 무기 슬롯", req.SlotCount, 2);

            // 관통탄 1.2A (저격수 18): floor(21.6)=21 − floor(방어 6 × 0.5)=3 → 18, 크리 floor(43.2)=43
            var piercing = Skill(sniper, "skill.sniper.piercing_round");
            req = sniper.SkillAttack(piercing, false);
            failures += Expect("관통탄 일반 (방어 6, 관통 50%)", AttackResolver.NormalDamage(req.Coefficient, req.AttackValue, 6, req.PierceRatio), 18);
            failures += Expect("관통탄 크리 (관통 재적용 없음)", AttackResolver.CriticalDamage(req.Coefficient, req.AttackValue), 43);

            // 검은 연무: 고유 3슬롯 지력 판정 (지력 71+4=75 → 40.83%), 마법 0.9M (M=14 → 12)
            var miasma = Skill(doctor, "skill.plague_doctor.black_miasma");
            req = doctor.SkillAttack(miasma, false);
            failures += Expect("검은 연무 3슬롯", req.SlotCount, 3);
            failures += ExpectFloat("검은 연무 지력 판정", req.SlotChance, 0.40833f);
            failures += ExpectBool("검은 연무 마법", req.Type == DamageType.Magic, true);
            failures += Expect("검은 연무 0.9M", AttackResolver.NormalDamage(req.Coefficient, req.AttackValue, 0), 12);

            // 응급 봉합: 30 + floor(0.5 × 14) = 37
            var suture = Skill(doctor, "skill.plague_doctor.emergency_suture");
            failures += Expect("응급 봉합 회복량", suture.HealFlat + Mathf.FloorToInt(suture.HealMagicRatio * doctor.Member.FinalMagicAttack + 0.0001f), 37);

            // 약점 간파 → 그림자 사냥개 방어 2/10 → 0/6
            var houndUnit = new BattleUnit(hound, 0);
            var expose = Skill(detective, "skill.detective.expose_weakness");
            houndUnit.Statuses.Apply(expose.AppliedStatuses[0]);
            failures += Expect("약점 간파 물리 방어 (2−4 → 0)", houndUnit.Defense().PhysicalDefense, 0);
            failures += Expect("약점 간파 마법 방어 (10−4)", houndUnit.Defense().MagicDefense, 6);

            // 장막 걷기 → 회피 상태 해제
            failures += ExpectBool("그림자 사냥개 회피 상태", houndUnit.HasEvasionState, true);
            houndUnit.Statuses.Apply(Skill(detective, "skill.detective.lift_the_veil").AppliedStatuses[0]);
            failures += ExpectBool("장막 걷기 후 회피 상태 해제", houndUnit.HasEvasionState, false);

            // 영점 조정: 무기 공격 슬롯 +15%p (권총 인지 95 → 59.5% → 74.5%), 고유 판정 스킬에는 미적용
            sniper.Statuses.Apply(Skill(sniper, "skill.sniper.zero_in").AppliedStatuses[0]);
            failures += ExpectFloat("영점 조정 기본 공격", sniper.BasicAttack(false).SlotChance, 0.745f);
            failures += ExpectFloat("영점 조정 관통탄(무기 판정)", sniper.SkillAttack(piercing, false).SlotChance, 0.745f);
            sniper.Statuses.BeginTurn();
            sniper.Statuses.BeginTurn();
            failures += ExpectBool("영점 조정 다음 2턴 동안 유지", sniper.Statuses.Has(StatusEffectType.AccuracyUp), true);
            sniper.Statuses.BeginTurn();
            failures += ExpectBool("영점 조정 2턴 뒤 소멸", sniper.Statuses.Has(StatusEffectType.AccuracyUp), false);

            // 쿨다운 2: 사용 뒤 다음 2개 개인 턴 불가, 그 다음 턴부터 가능
            detective.StartCooldown(breach);
            detective.TickCooldowns();
            failures += ExpectBool("쿨다운 — 다음 턴 불가", detective.CannotUseReason(breach) != null, true);
            detective.TickCooldowns();
            failures += ExpectBool("쿨다운 — 그다음 턴도 불가", detective.CannotUseReason(breach) != null, true);
            detective.TickCooldowns();
            failures += ExpectBool("쿨다운 — 세 번째 턴부터 가능", detective.CannotUseReason(breach) == null, true);

            // 비용 부족
            detective.Member.TrySpendFocus(detective.Member.CurrentFocus);
            failures += ExpectBool("집중력 0이면 사용 불가", detective.CannotUseReason(breach) != null, true);

            if (failures == 0) Debug.Log("[SkillSelfTest] 전부 통과.");
            else Debug.LogError($"[SkillSelfTest] {failures}건 실패. 위 로그 확인할 것.");
        }

        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>($"{Root}/{path}.asset");

        private static SkillDefinition Skill(BattleUnit unit, string id) => unit.Member.Job.Skills.First(s => s != null && s.Id == id);

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[SkillSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectFloat(string label, float actual, float expected)
        {
            if (Mathf.Abs(actual - expected) <= 0.0005f) return 0;
            Debug.LogError($"[SkillSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectBool(string label, bool actual, bool expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[SkillSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }
    }
}
