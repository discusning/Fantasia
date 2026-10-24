using System;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Data;

namespace Fantasia.Items
{
    // 상점 진행 상태 (BoardSession이 들고 저장 대상 — City_Shop.md §14).
    public class ShopState
    {
        // (도시 Id, 상품 Id) → 남은 재고. 키가 없으면 아직 판매 개시 전 (해금되는 순간 최대 수량으로 개시).
        public readonly Dictionary<(string city, string item), int> Stock = new();
        public readonly HashSet<(string city, string item)> LimitedBought = new();
        public int LastRestockBoundary;  // 마지막으로 처리한 3라운드 경계 (완료 라운드 수)

        public void Clear()
        {
            Stock.Clear();
            LimitedBought.Clear();
            LastRestockBoundary = 0;
        }
    }

    public readonly struct ShopResult
    {
        public readonly bool Ok;
        public readonly string Message;
        public readonly bool EndsAllyTurn; // 서비스·제작은 월드 행동 종료
        public ShopResult(bool ok, string message, bool endsAllyTurn = false) { Ok = ok; Message = message; EndsAllyTurn = endsAllyTurn; }
    }

    public enum ShopService { Inn, Treatment, Meditation }

    // D20 도시 상점 (Docs/Planning/City.md §4·§5, City_Shop.md §13). 거래 실패 시 Cent·재고 모두 그대로.
    public static class ShopRules
    {
        public const int RestockEveryRounds = 3;
        public const float SellRatio = 0.4f;

        // ── 판매 단계·표시 ──
        // 2막 = 런던 보스(검시관) 처치, 고성 = 베르사유 보스(세베린) 처치 (WorldMap.md)
        public static bool StageUnlocked(ShopStage stage, bool act2, bool castle) =>
            stage == ShopStage.Base || (stage == ShopStage.Act2 && (act2 || castle)) || (stage == ShopStage.Castle && castle);

        // 지금 빌드에서 숨기는 상품 (City_Shop.md §13 마지막 줄): 파티에 없는 직업 전용 무기, 아직 없는 시스템 품목
        public static bool IsHidden(ShopEntry entry, IEnumerable<JobId> partyJobs)
        {
            switch (entry.Item)
            {
                case WeaponDefinition w:
                    return w.AllowedJobs == null || !w.AllowedJobs.Intersect(partyJobs).Any();
                case ConsumableDefinition c:
                    return !(c.IsImplemented || c.Effect == ConsumableEffect.Material);
                default:
                    return false;
            }
        }

        // 해금된 상품은 처음 보는 순간 최대 수량으로 개시 (한정품은 산 적 없으면 1)
        public static int StockOf(ShopState state, CityShopDefinition shop, ShopEntry entry)
        {
            var key = (shop.Id, entry.ItemId);
            if (entry.Limited) return state.LimitedBought.Contains(key) ? 0 : entry.MaxStock;
            if (!state.Stock.TryGetValue(key, out int left))
            {
                left = entry.MaxStock;
                state.Stock[key] = left;
            }
            return left;
        }

        // 완료 월드 라운드 3·6·9…마다 해금된 모든 일반 상품을 최대 수량까지 (남은 수에 더하지 않음, 놓친 재입고 누적 없음)
        public static bool TryRestock(ShopState state, IEnumerable<CityShopDefinition> shops, int completedRounds)
        {
            if (completedRounds <= 0 || completedRounds % RestockEveryRounds != 0 || completedRounds == state.LastRestockBoundary) return false;
            state.LastRestockBoundary = completedRounds;
            foreach (var shop in shops.Where(x => x != null))
                foreach (var entry in shop.Entries)
                {
                    var key = (shop.Id, entry.ItemId);
                    if (!entry.Limited && state.Stock.ContainsKey(key)) state.Stock[key] = entry.MaxStock;
                }
            return true;
        }

        public static int RoundsUntilRestock(int completedRounds) => RestockEveryRounds - (completedRounds % RestockEveryRounds);

        // ── 사기 ──
        public static ShopResult Buy(ShopState state, CityShopDefinition shop, ShopEntry entry, Party party, bool act2, bool castle)
        {
            if (!StageUnlocked(entry.Stage, act2, castle)) return new ShopResult(false, $"{entry.DisplayName}: {StageLabel(entry.Stage)}에 판매 시작");
            if (StockOf(state, shop, entry) <= 0) return new ShopResult(false, entry.Limited ? $"{entry.DisplayName}: 구매 완료 (한정품)" : $"{entry.DisplayName}: 품절");
            if (party.Cent < entry.Price) return new ShopResult(false, $"Cent 부족 ({entry.Price} 필요, 소지 {party.Cent})");

            // 인벤토리 여유 확인 → 넣기 (실패하면 아무것도 바뀌지 않음)
            switch (entry.Item)
            {
                case ConsumableDefinition c:
                    if (party.Inventory.SpaceFor(c) <= 0) return new ShopResult(false, $"{c.DisplayName}: 보관 한도({c.MaxStack}개) 가득");
                    party.Inventory.AddConsumable(c);
                    break;
                case WeaponDefinition w:
                    if (!party.Inventory.AddGear(new GearItem(w))) return new ShopResult(false, "장비 보관함 가득 (20개)");
                    break;
                case ArmorDefinition a:
                    if (!party.Inventory.AddGear(new GearItem(a))) return new ShopResult(false, "장비 보관함 가득 (20개)");
                    break;
                default:
                    return new ShopResult(false, "알 수 없는 상품");
            }
            party.SpendCent(entry.Price);
            var key = (shop.Id, entry.ItemId);
            if (entry.Limited) state.LimitedBought.Add(key);
            else state.Stock[key] = StockOf(state, shop, entry) - 1;
            return new ShopResult(true, $"{entry.DisplayName} 구입 −{entry.Price} Cent");
        }

        // ── 팔기 (판 물건은 재고에 더하지 않음) ──
        public static int SellPrice(ConsumableDefinition c) => (int)Math.Floor(c.Price * SellRatio);

        public static ShopResult SellConsumable(Party party, ConsumableDefinition c)
        {
            if (party.Inventory.CountOf(c) <= 0) return new ShopResult(false, "가진 것 없음");
            int price = SellPrice(c);
            party.Inventory.RemoveConsumable(c);
            party.AddCent(price);
            return new ShopResult(true, $"{c.DisplayName} 판매 +{price} Cent");
        }

        public static ShopResult SellGear(Party party, GearItem gear)
        {
            if (!party.Inventory.Gear.Contains(gear)) return new ShopResult(false, "장착 중인 장비는 먼저 해제");
            int price = gear.SellPrice;
            party.Inventory.RemoveGear(gear);
            party.AddCent(price);
            return new ShopResult(true, $"{gear.DisplayName} 판매 +{price} Cent{(price == 0 ? " (최초 지급 장비)" : "")}");
        }

        // ── 서비스 (City.md §4) — 2026-10-24 결정: 파티 일괄 적용, 1인 가격 × 생존 인원, 이용하면 아군 턴 종료 ──
        public static int ServicePrice(ShopService s) => s switch { ShopService.Inn => 30, ShopService.Treatment => 50, _ => 45 };

        public static string ServiceLabel(ShopService s) => s switch
        {
            ShopService.Inn => "숙박",
            ShopService.Treatment => "정밀 치료",
            _ => "집중 명상",
        };

        public static string ServiceDescription(ShopService s) => s switch
        {
            ShopService.Inn => "최대 HP·MP 50%와 집중력 1 회복, 혼란·공포 제거",
            ShopService.Treatment => "중독·출혈·화상·저주·부식 제거 (죽음의 표식 제외)",
            _ => "집중력 최대치까지 회복",
        };

        public static ShopResult UseService(Party party, ShopService s)
        {
            var members = party.AliveMembers.ToList();
            int cost = ServicePrice(s) * members.Count;
            if (members.Count == 0) return new ShopResult(false, "이용할 파티원 없음");
            if (party.Cent < cost) return new ShopResult(false, $"Cent 부족 ({cost} 필요)");
            party.SpendCent(cost);
            foreach (var m in members)
            {
                switch (s)
                {
                    case ShopService.Inn:
                        m.Heal(m.MaxHP / 2);
                        m.RestoreMP(m.MaxMP / 2);
                        m.RestoreFocus(1);
                        m.Statuses.Remove(StatusEffectType.Confusion);
                        break;
                    case ShopService.Treatment:
                        m.Statuses.Remove(StatusEffectType.Poison);
                        m.Statuses.Remove(StatusEffectType.WeakBleed);
                        break;
                    case ShopService.Meditation:
                        m.RestoreFocus(m.MaxFocus);
                        break;
                }
            }
            return new ShopResult(true, $"{ServiceLabel(s)} — 파티 {members.Count}명 −{cost} Cent · 아군 턴 종료", endsAllyTurn: true);
        }

        // ── 작업대 제작 (System.md "연금술 제작·상점 서비스") — 시약 + 제작비, 1개 확정, 월드 행동 종료 ──
        public static readonly (string resultId, int reagents, int fee)[] Recipes =
        {
            (ConsumableIds.Bandage, 2, 10),
            (ConsumableIds.LifeElixir, 4, 10),
            (ConsumableIds.ManaElixir, 3, 10),
            (ConsumableIds.FocusCandle, 4, 10),
            ("consumable.witch_bone", 8, 20),
        };

        public static ShopResult Craft(Party party, ItemCatalog catalog, int recipeIndex)
        {
            var (resultId, reagents, fee) = Recipes[recipeIndex];
            var result = catalog.Consumable(resultId);
            var reagent = catalog.Consumable(ConsumableIds.Reagent);
            if (result == null || reagent == null) return new ShopResult(false, "제작 데이터 없음");
            if (party.Inventory.CountOf(reagent) < reagents) return new ShopResult(false, $"시약 부족 ({reagents}개 필요, 보유 {party.Inventory.CountOf(reagent)})");
            if (party.Cent < fee) return new ShopResult(false, $"Cent 부족 ({fee} 필요)");
            if (party.Inventory.SpaceFor(result) <= 0) return new ShopResult(false, $"{result.DisplayName}: 보관 한도 가득");
            party.Inventory.RemoveConsumable(reagent, reagents);
            party.SpendCent(fee);
            party.Inventory.AddConsumable(result);
            return new ShopResult(true, $"{result.DisplayName} 제작 (시약 {reagents} · {fee} Cent) · 아군 턴 종료", endsAllyTurn: true);
        }

        public static string StageLabel(ShopStage s) => s switch
        {
            ShopStage.Act2 => "2막 진입(검시관 처치) 후",
            ShopStage.Castle => "고성 진입 후",
            _ => "도시 해금 시",
        };
    }
}
