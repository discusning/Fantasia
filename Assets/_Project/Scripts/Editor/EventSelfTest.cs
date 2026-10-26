using Fantasia.Core;
using Fantasia.Data;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 이벤트 칸 — System.md "이벤트 수치·도시 거점 보완" 점술마녀·분수대 규칙과 대조.
    public static class EventSelfTest
    {
        [MenuItem("Fantasia/Data/Run Event Self-Test")]
        public static void Run()
        {
            var detective = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_명탐정.asset");
            var doctor = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_역병의사.asset");
            if (detective == null) { Debug.LogError("[EventSelfTest] 직업 자산 없음"); return; }
            int e = 0;
            var party = new Party();
            party.AddMember(new PartyMember(detective));
            party.AddMember(new PartyMember(doctor));
            var judge = party.Members[0];

            e += Check(EventTiles.KindAt("M02") == EventKind.FortuneTeller && EventTiles.KindAt("S10") == EventKind.Fountain, "런던 이벤트 칸 배정 (확정)");
            e += Check(EventTiles.SuccessPercent(judge) == Mathf.Min(judge.FinalStats.Luck, 95), "성공률 = 행운 (최대 95)");

            // 참가비 20 — 부족하면 아무것도 안 바뀜
            party.SpendCent(party.Cent - 10);
            var poor = EventTiles.PlayWitch(party, judge, FortuneCard.Wealth, null);
            e += Check(!poor.Ok && party.Cent == 10, "Cent 부족 → 참여 불가, 그대로");
            party.AddCent(1000);

            // 같은 게임 난수면 같은 결과 (저장 → 공개)
            GameRandom.Seed(77);
            int cent0 = party.Cent;
            var r1 = EventTiles.PlayWitch(party, judge, FortuneCard.Wealth, null);
            e += Expect("부: 참가비·보상", party.Cent - cent0, r1.Success ? EventTiles.WealthCent - EventTiles.WitchFee : -EventTiles.WitchFee);
            GameRandom.Seed(77);
            var r2 = EventTiles.PlayWitch(party, judge, FortuneCard.Wealth, null);
            e += Check(r1.Success == r2.Success, "같은 난수 → 같은 판정");

            // 회복 카드는 대상 필요, 성공하면 HP 50
            e += Check(!EventTiles.PlayWitch(party, judge, FortuneCard.Recovery, null).Ok, "회복: 대상 없으면 불가");
            var hurt = party.Members[1];
            hurt.TakeDamage(80);
            int hp = hurt.CurrentHP;
            for (int seed = 0; seed < 50; seed++)
            {
                GameRandom.Seed(seed);
                var r = EventTiles.PlayWitch(party, judge, FortuneCard.Recovery, hurt);
                if (r.Success) { e += Expect("회복 성공 HP +50", hurt.CurrentHP - hp, 50); break; }
            }

            // 3월드 라운드 뒤 재참여
            e += Expect("직후 대기", EventTiles.WitchWaitRounds(5, 5), 3);
            e += Expect("2라운드 뒤 대기", EventTiles.WitchWaitRounds(5, 7), 1);
            e += Expect("3라운드 뒤 참여 가능", EventTiles.WitchWaitRounds(5, 8), 0);
            e += Expect("참여 기록 없음", EventTiles.WitchWaitRounds(0, 1), 0);

            // 분수대: LP 최대 5, 쓰러진 캐릭터 불가
            var m = party.Members[0];
            e += Check(EventTiles.CanUseFountain(m), "분수대: LP 5 미만이면 가능");
            while (m.GainLP()) { }
            e += Check(!EventTiles.CanUseFountain(m) && m.CurrentLP == PartyMember.MaxLP, "LP 최대 5에서 불가");

            e += CheckPrayers(detective, doctor);

            if (e == 0) Debug.Log("[EventSelfTest] 전부 통과.");
            else Debug.LogError($"[EventSelfTest] {e}건 실패.");
        }

        private static StatusEffectDefinition ConfusionDef()
        {
            var d = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            d.DisplayName = "혼란";
            d.Effect = StatusEffectType.Confusion;
            d.DurationTurns = 1;
            d.MaxStacks = 1;
            return d;
        }

        // 십자가 석상 기도 6종 (System.md "십자가 석상의 기도" 표)
        private static int CheckPrayers(JobDefinition detective, JobDefinition doctor)
        {
            int e = 0;
            e += Expect("1차 선택지 6종", PrayerRules.Offered.Count, 6);
            e += Check(!System.Linq.Enumerable.Contains(PrayerRules.Offered, PrayerKind.Haste) && !System.Linq.Enumerable.Contains(PrayerRules.Offered, PrayerKind.Purity), "성급함·순수 숨김");

            var m = new PartyMember(detective);
            int hp0 = m.MaxHP, focus0 = m.MaxFocus;
            var s0 = m.FinalStats;
            float slot0 = Fantasia.Combat.AttackResolver.FromPartyMember(m).SlotChance;

            m.SetPrayer(PrayerKind.Focus);
            e += Expect("집중: 최대 집중력 +2", m.MaxFocus, Mathf.Min(6, focus0 + 2));
            e += Check(m.Statuses.Apply(ConfusionDef()) == null, "집중: 혼란 면역");

            m.SetPrayer(PrayerKind.Life); // 교체 — 집중 효과 사라짐
            e += Expect("교체: 집중 보너스 제거", m.MaxFocus, focus0);
            e += Expect("생명: 활력 +5", m.FinalStats.Vitality, s0.Vitality + 5);
            e += Expect("생명: 최대 HP +10", m.MaxHP, hp0 + 10);
            var elixir = ItemCatalog.Current.Consumable(ConsumableIds.LifeElixir);
            m.TakeDamage(100);
            int before = m.CurrentHP;
            Fantasia.Items.ConsumableRules.ApplyToMember(elixir, m);
            e += Expect("생명: 회복 아이템 +2", m.CurrentHP - before, elixir.Amount + 2);

            m.SetPrayer(PrayerKind.Shadow);
            e += Expect("그림자: 속도 +5", m.FinalStats.Speed, s0.Speed + 5);
            e += Check(Mathf.Abs(Fantasia.Combat.AttackResolver.DefenseOf(m).EvasionChance - Fantasia.Combat.AttackResolver.EvasionChance(s0.Speed + 5, 0.05f)) < 0.0001f, "그림자: 회피 +5%p (속도 +5 포함)");

            m.SetPrayer(PrayerKind.Blood);
            e += Check(Mathf.Abs(Fantasia.Combat.AttackResolver.FromPartyMember(m).SlotChance - Mathf.Min(0.95f, slot0 + 0.10f)) < 0.0001f, "피: 슬롯 성공률 +10%p (최대 95%)");

            m.SetPrayer(PrayerKind.Wisdom);
            e += Expect("지혜: 지력 +5", m.FinalStats.Intelligence, s0.Intelligence + 5);
            int exp0 = m.Experience;
            m.GrantExperience(30);
            e += Expect("지혜: 경험치 +25% (버림)", m.Experience - exp0, 37);

            // 파티 단위 효과 (부의 기도: 여러 명이어도 1회)
            var party = new Party();
            party.AddMember(m);
            var d = new PartyMember(doctor);
            party.AddMember(d);
            e += Check(!PrayerRules.PartyHas(party, PrayerKind.Wealth), "부: 없음");
            d.SetPrayer(PrayerKind.Wealth);
            e += Check(PrayerRules.PartyHas(party, PrayerKind.Wealth), "부: 파티에 있음");
            return e;
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[EventSelfTest] {label}: 기대 {expected}, 실제 {actual}");
            return 1;
        }

        private static int Check(bool ok, string label)
        {
            if (ok) return 0;
            Debug.LogError($"[EventSelfTest] 실패: {label}");
            return 1;
        }
    }
}
