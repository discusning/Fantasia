using System.Linq;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D04 완료 기준 검증: System.md 표 수치와 PartyMember/Party 계산 결과를 대조한다.
    // GameDataSeed로 만든 명탐정/역병 의사/저격수 자산이 있어야 돈다
    // (먼저 Fantasia/Data/Seed Sample Game Data 실행).
    public static class PartyMemberSelfTest
    {
        private const string JobDir = "Assets/_Project/Data/Jobs";

        [MenuItem("Fantasia/Data/Run Party Member Self-Test")]
        public static void Run()
        {
            int failures = 0;

            var detective = LoadJob("Job_명탐정");
            var plagueDoctor = LoadJob("Job_역병의사");
            var sniper = LoadJob("Job_저격수");

            if (detective == null || plagueDoctor == null || sniper == null)
            {
                Debug.LogError("[PartyMemberSelfTest] 직업 자산을 찾을 수 없다 — 먼저 Fantasia/Data/Seed Sample Game Data를 실행할 것.");
                return;
            }

            failures += CheckDetective(detective);
            failures += CheckPlagueDoctor(plagueDoctor);
            failures += CheckSniper(sniper);
            failures += CheckLevelUp(detective);
            failures += CheckPartyExperienceSplit(detective, plagueDoctor, sniper);

            if (failures == 0)
                Debug.Log("[PartyMemberSelfTest] 전부 통과.");
            else
                Debug.LogError($"[PartyMemberSelfTest] {failures}건 실패. 위 로그 확인할 것.");
        }

        private static JobDefinition LoadJob(string name) =>
            AssetDatabase.LoadAssetAtPath<JobDefinition>($"{JobDir}/{name}.asset");

        private static int CheckDetective(JobDefinition job)
        {
            var m = new PartyMember(job);
            int errors = 0;
            errors += Expect("명탐정 MaxHP", m.MaxHP, 108);
            errors += Expect("명탐정 MaxMP", m.MaxMP, 0);
            errors += Expect("명탐정 FinalPhysicalAttack(기본8+단검보너스5)", m.FinalPhysicalAttack, 13);
            errors += Expect("명탐정 FinalMagicAttack", m.FinalMagicAttack, 0);
            errors += Expect("명탐정 시작 집중력", m.CurrentFocus, 3);
            errors += Expect("명탐정 시작 LP", m.CurrentLP, 3);
            return errors;
        }

        private static int CheckPlagueDoctor(JobDefinition job)
        {
            var m = new PartyMember(job);
            int errors = 0;
            errors += Expect("역병 의사 MaxHP", m.MaxHP, 132);
            errors += Expect("역병 의사 MaxMP", m.MaxMP, 132);
            errors += Expect("역병 의사 FinalMagicAttack(기본8+스태프보너스6)", m.FinalMagicAttack, 14);
            errors += Expect("역병 의사 FinalPhysicalAttack(맨손 기본4, 마법무기라 보너스 미적용)", m.FinalPhysicalAttack, 4);
            return errors;
        }

        private static int CheckSniper(JobDefinition job)
        {
            var m = new PartyMember(job);
            int errors = 0;
            errors += Expect("저격수 MaxHP", m.MaxHP, 76);
            errors += Expect("저격수 MaxMP", m.MaxMP, 0);
            errors += Expect("저격수 FinalPhysicalAttack(기본11+권총보너스7)", m.FinalPhysicalAttack, 18);
            errors += Expect("저격수 시작 LP", m.CurrentLP, 2);
            return errors;
        }

        private static int CheckLevelUp(JobDefinition job)
        {
            var m = new PartyMember(job);
            int errors = 0;
            int hpBefore = m.CurrentHP; // 풀피 상태(108)
            int xpNeeded = m.ExperienceToNextLevel; // 레벨1 → 60

            m.GrantExperience(xpNeeded);

            errors += Expect("레벨업 후 Level", m.Level, 2);
            errors += Expect("레벨업 후 MaxHP(108+8)", m.MaxHP, 116);
            errors += Expect("레벨업 후 CurrentHP(증가분만 회복, 108+8)", m.CurrentHP, hpBefore + 8);
            errors += Expect("레벨업 후 집중력(상한 3 유지)", m.CurrentFocus, 3);
            errors += Expect("레벨업으로 LP는 안 바뀜", m.CurrentLP, job.StartingLP);
            return errors;
        }

        private static int CheckPartyExperienceSplit(JobDefinition detectiveJob, JobDefinition plagueDoctorJob, JobDefinition sniperJob)
        {
            var party = new Party();
            var detective = new PartyMember(detectiveJob);
            var plagueDoctor = new PartyMember(plagueDoctorJob);
            var sniper = new PartyMember(sniperJob);
            party.AddMember(detective);
            party.AddMember(plagueDoctor);
            party.AddMember(sniper);

            sniper.TakeDamage(9999); // 전투 불참(사망) 취급

            int errors = 0;
            errors += Expect("파티원 수", party.Members.Count, 3);
            errors += Expect("생존자 수(저격수 사망 처리 후)", party.AliveMembers.Count(), 2);

            party.GrantExperienceToSurvivors(detectiveJob.StartingLP * 0 + 60); // 레벨1→2 경계값

            errors += Expect("생존자(명탐정) 레벨업", detective.Level, 2);
            errors += Expect("생존자(역병 의사) 레벨업", plagueDoctor.Level, 2);
            errors += Expect("사망자(저격수)는 경험치 못 받음", sniper.Level, 1);

            errors += Expect("파티 시작 Cent", party.Cent, Party.StartingCent);
            return errors;
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[PartyMemberSelfTest] {label}: 기대값 {expected}, 실제 {actual}");
            return 1;
        }
    }
}
