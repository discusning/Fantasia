using System.Linq;
using Fantasia.Data;
using Fantasia.Items;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D20 — Docs/Planning/City_Shop.md §12 합계·§13 거래 규칙·§15 확인할 동작과 대조.
    public static class ShopSelfTest
    {
        [MenuItem("Fantasia/Data/Run Shop Self-Test")]
        public static void Run()
        {
            var catalog = ItemCatalog.Current;
            var london = catalog != null ? catalog.ShopAtHub("S05") : null;
            var versailles = catalog != null ? catalog.ShopAtHub("F18") : null;
            var rome = catalog != null ? catalog.ShopAtHub("S31") : null;
            var detective = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_명탐정.asset");
            var doctor = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_역병의사.asset");
            var sniper = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_저격수.asset");
            if (london == null || versailles == null || rome == null || detective == null) { Debug.LogError("[ShopSelfTest] 상점·직업 자산 없음"); return; }
            int e = 0;

            // §12 도시별 최종 판매 규모 (확장 도시 제외, 모든 단계 해금·한정품 미구매)
            e += Expect("런던 품목", london.Entries.Length, 37);
            e += Expect("런던 수량", london.Entries.Sum(x => x.MaxStock), 65);
            e += Expect("베르사유 품목", versailles.Entries.Length, 51);
            e += Expect("베르사유 수량", versailles.Entries.Sum(x => x.MaxStock), 80);
            e += Expect("로마 품목", rome.Entries.Length, 71);
            e += Expect("로마 수량", rome.Entries.Sum(x => x.MaxStock), 100);

            // 1차 빌드 숨김: 파티(명탐정·역병 의사·저격수)가 못 쓰는 무기(장창·바이올린)와 미구현 시스템 품목
            var jobs = new[] { detective.Id, doctor.Id, sniper.Id };
            var londonBase = london.Entries.Where(x => x.Stage == ShopStage.Base && !ShopRules.IsHidden(x, jobs)).ToList();
            e += Expect("런던 1막 노말 무기 (3직업용)", londonBase.Count(x => x.Item is WeaponDefinition), 8);
            e += Expect("런던 1막 방어구·장신구", londonBase.Count(x => x.Item is ArmorDefinition), 4);
            e += Check(londonBase.All(x => !(x.Item is ConsumableDefinition c) || c.Effect != ConsumableEffect.RepairWeapon), "수리 도구 숨김 (파손 2차)");

            // §15: 라운드 0에 영약 2개 구매 → 1개 → 완료 라운드 3에 정확히 3개
            var state = new ShopState();
            var party = NewParty(detective, doctor, sniper);
            party.AddCent(1000);
            var elixir = london.Entries.First(x => x.DisplayName == "생명의 영약");
            ShopRules.Buy(state, london, elixir, party, false, false);
            ShopRules.Buy(state, london, elixir, party, false, false);
            e += Expect("영약 2개 산 뒤 남은 수", ShopRules.StockOf(state, london, elixir), 1);
            e += Check(!ShopRules.TryRestock(state, catalog.Shops, 2), "완료 라운드 2는 재입고 아님");
            e += Check(ShopRules.TryRestock(state, catalog.Shops, 3), "완료 라운드 3 재입고");
            e += Expect("재입고 후 정확히 최대 3", ShopRules.StockOf(state, london, elixir), 3);
            e += Check(!ShopRules.TryRestock(state, catalog.Shops, 3), "같은 경계 두 번 처리 안 함");

            // 실패 시 Cent·재고 그대로: 돈 부족 / 품절 / 미해금 / 보관 가득
            var poor = NewParty(detective, doctor, sniper);
            poor.SpendCent(poor.Cent - 10);
            var r = ShopRules.Buy(state, london, elixir, poor, false, false);
            e += Check(!r.Ok && poor.Cent == 10 && ShopRules.StockOf(state, london, elixir) == 3, "Cent 부족 → 아무것도 안 바뀜");
            var rare = london.Entries.First(x => x.DisplayName == "은도금 해부단검");
            int cent = party.Cent;
            e += Check(!ShopRules.Buy(state, london, rare, party, false, false).Ok && party.Cent == cent, "2막 전 희귀 무기 구매 불가");
            e += Check(ShopRules.Buy(state, london, rare, party, true, false).Ok, "2막 진입 후 구매 가능");
            var candle = london.Entries.First(x => x.DisplayName == "집중의 향초");
            for (int i = 0; i < 3; i++) ShopRules.Buy(state, london, candle, party, false, false);
            cent = party.Cent;
            e += Check(!ShopRules.Buy(state, london, candle, party, false, false).Ok && party.Cent == cent, "품절 → 구매 불가, Cent 그대로");

            // 런던에서 산 것이 다른 도시 재고에 영향 없음
            var vElixir = versailles.Entries.First(x => x.DisplayName == "생명의 영약");
            e += Expect("베르사유 영약 재고 영향 없음", ShopRules.StockOf(state, versailles, vElixir), 3);

            // 한정품: 1회 구매 뒤 재입고·되팔기로 복구 안 됨
            var laurel = london.Entries.First(x => x.Limited);
            e += Check(ShopRules.Buy(state, london, laurel, party, false, false).Ok, "월계관 구매");
            ShopRules.TryRestock(state, catalog.Shops, 6);
            e += Expect("월계관 재입고 안 됨", ShopRules.StockOf(state, london, laurel), 0);
            ShopRules.SellConsumable(party, (ConsumableDefinition)laurel.Item);
            e += Expect("되팔아도 한정 구매 기록 유지", ShopRules.StockOf(state, london, laurel), 0);

            // 되팔기: 40% 버림, 판 물건은 재고에 안 더함, 최초 지급 무기 0
            var bandage = (ConsumableDefinition)london.Entries.First(x => x.DisplayName == "압박 붕대").Item;
            party.Inventory.AddConsumable(bandage);
            cent = party.Cent;
            ShopRules.SellConsumable(party, bandage);
            e += Expect("붕대 되팔기 20×40%=8", party.Cent - cent, 8);
            var start = party.Members[0].EquippedWeaponItem;
            party.Members[0].TryUnequip(GearSlot.Weapon, party.Inventory, out _);
            cent = party.Cent;
            ShopRules.SellGear(party, start);
            e += Expect("최초 지급 무기 되팔기 0", party.Cent - cent, 0);

            // 서비스: 파티 일괄, 1인 가격 × 생존 인원, 아군 턴 종료
            var svc = NewParty(detective, doctor, sniper);
            svc.Members[0].TakeDamage(60);
            cent = svc.Cent;
            var inn = ShopRules.UseService(svc, ShopService.Inn);
            e += Check(inn.Ok && inn.EndsAllyTurn, "숙박 이용 → 아군 턴 종료");
            e += Expect("숙박 30 × 3명", cent - svc.Cent, 90);
            e += Expect("숙박 HP 50% 회복", svc.Members[0].CurrentHP, Mathf.Min(svc.Members[0].MaxHP, svc.Members[0].MaxHP - 60 + svc.Members[0].MaxHP / 2));

            // 작업대: 시약 2 + 10 Cent → 붕대 1
            var craft = NewParty(detective, doctor, sniper);
            var reagent = catalog.Consumable(ConsumableIds.Reagent);
            craft.Inventory.AddConsumable(reagent, 2);
            int bandBefore = craft.Inventory.CountOf(bandage);
            var c = ShopRules.Craft(craft, catalog, 0);
            e += Check(c.Ok && craft.Inventory.CountOf(bandage) == bandBefore + 1 && craft.Inventory.CountOf(reagent) == 0, "붕대 제작 (시약 2)");

            if (e == 0) Debug.Log("[ShopSelfTest] 전부 통과.");
            else Debug.LogError($"[ShopSelfTest] {e}건 실패.");
        }

        private static Party NewParty(params JobDefinition[] jobs)
        {
            var p = new Party();
            foreach (var j in jobs) p.AddMember(new PartyMember(j));
            return p;
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[ShopSelfTest] {label}: 기대 {expected}, 실제 {actual}");
            return 1;
        }

        private static int Check(bool ok, string label)
        {
            if (ok) return 0;
            Debug.LogError($"[ShopSelfTest] 실패: {label}");
            return 1;
        }
    }
}
