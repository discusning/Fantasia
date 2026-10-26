using System.Collections.Generic;
using System.Linq;
using Fantasia.Data;

namespace Fantasia.Items
{
    // D17 소모품 효과 (System.md "소모품·이동권·재료"). 별도 확률 판정 없이 효과를 적용한다.
    // 파티원 1명에게 쓰는 효과만 여기서 — 적 대상(성수·화염병)·탈출권은 전투(BattleController)가 처리한다.
    public static class ConsumableRules
    {
        public static bool TargetsAlly(ConsumableDefinition def) => def.Effect switch
        {
            ConsumableEffect.RestoreHP or ConsumableEffect.RestoreMP or ConsumableEffect.RestoreFocus
                or ConsumableEffect.Cure or ConsumableEffect.LifeLaurel => true,
            _ => false,
        };

        public static bool TargetsEnemy(ConsumableDefinition def) => def.Effect == ConsumableEffect.RevealEvasion;

        // 효과가 없으면(HP 가득, 고칠 상태 없음 등) 쓰지 않고 이유를 돌려준다 — 아이템 낭비 방지.
        public static bool CanUseOn(ConsumableDefinition def, PartyMember target, out string reason)
        {
            reason = null;
            if (target == null || target.IsPermanentlyDead) { reason = "대상 없음"; return false; }
            if (target.IsDowned) { reason = "쓰러진 동료에게는 부활이 필요"; return false; }
            bool cures = def.Cures != null && def.Cures.Any(t => target.Statuses.Has(t));
            switch (def.Effect)
            {
                case ConsumableEffect.RestoreHP:
                    if (target.CurrentHP >= target.MaxHP && !cures) { reason = "HP가 가득 참"; return false; }
                    return true;
                case ConsumableEffect.RestoreMP:
                    if (target.MaxMP == 0) { reason = "MP를 쓰지 않는 직업"; return false; }
                    if (target.CurrentMP >= target.MaxMP) { reason = "MP가 가득 참"; return false; }
                    return true;
                case ConsumableEffect.RestoreFocus:
                    if (target.CurrentFocus >= target.MaxFocus) { reason = "집중력이 가득 참"; return false; }
                    return true;
                case ConsumableEffect.Cure:
                    if (!cures) { reason = "제거할 상태 없음"; return false; }
                    return true;
                case ConsumableEffect.LifeLaurel:
                    if (target.CurrentLP >= PartyMember.MaxLP) { reason = $"LP 최대({PartyMember.MaxLP})"; return false; }
                    return true;
                default:
                    reason = "이 대상에게 쓰는 아이템이 아님";
                    return false;
            }
        }

        // 파티원 1명에게 적용 (인벤토리 차감은 호출부). 로그 문구를 돌려준다.
        public static string ApplyToMember(ConsumableDefinition def, PartyMember target)
        {
            var parts = new List<string>();
            switch (def.Effect)
            {
                case ConsumableEffect.RestoreHP:
                    int before = target.CurrentHP;
                    target.Heal(def.Amount + (target.Prayer == PrayerKind.Life ? PrayerRules.LifeItemHeal : 0)); // 생명의 기도
                    parts.Add($"HP +{target.CurrentHP - before}");
                    break;
                case ConsumableEffect.RestoreMP:
                    parts.Add($"MP +{target.RestoreMP(def.Amount)}");
                    break;
                case ConsumableEffect.RestoreFocus:
                    parts.Add($"집중력 +{target.RestoreFocus(def.Amount)}");
                    break;
                case ConsumableEffect.LifeLaurel:
                    if (target.GainLP()) parts.Add($"LP +1 ({target.CurrentLP})");
                    break;
            }
            if (def.Cures != null)
                foreach (var t in def.Cures)
                    if (target.Statuses.Has(t)) { target.Statuses.Remove(t); parts.Add($"{StatusLabel(t)} 제거"); }
            return $"{target.Job.DisplayName}에게 [{def.DisplayName}] — {(parts.Count > 0 ? string.Join(", ", parts) : "효과 없음")}";
        }

        // 안개 야영 세트: 생존 파티의 최대 HP·MP 25%와 집중력 1 회복 (월드·안전 타일)
        public static string ApplyCampRest(Party party)
        {
            foreach (var m in party.AliveMembers)
            {
                m.Heal(m.MaxHP / 4);
                m.RestoreMP(m.MaxMP / 4);
                m.RestoreFocus(1);
            }
            return "[안개 야영 세트] 생존 파티 HP·MP 25%, 집중력 1 회복";
        }

        public readonly struct WorldUseResult
        {
            public readonly bool Used;
            public readonly string Message;
            public readonly bool EndsAllyTurn; // 안개 야영 세트: 사용 후 1월드 라운드 경과
            public WorldUseResult(bool used, string message, bool endsAllyTurn = false) { Used = used; Message = message; EndsAllyTurn = endsAllyTurn; }
        }

        // 보드(월드)에서 사용. 인벤토리 차감까지 한다.
        // - 회복약·정화약: 아군 턴에 파티원별 1회 (☆ 개발 임시 — usedThisTurn이 기록)
        // - 안개 야영 세트: 생존 파티 회복 후 아군 턴 종료
        // - 안개 연막병: 다음 기습 보너스 (pendingAmbushBonus에 더함, 1회분만)
        public static WorldUseResult UseInWorld(ConsumableDefinition def, PartyMember target, Party party,
            HashSet<PartyMember> usedThisTurn, ref int pendingAmbushBonus)
        {
            if (def == null) return new WorldUseResult(false, "아이템 없음");
            if (party.Inventory.CountOf(def) <= 0) return new WorldUseResult(false, $"{def.DisplayName}: 남은 개수 없음");
            if (!def.UsableIn(UseContext.World))
                return new WorldUseResult(false, def.IsImplemented ? $"{def.DisplayName}: 전투에서만 사용" : $"{def.DisplayName}: 아직 사용할 수 없음 (연결 대기)");

            switch (def.Effect)
            {
                case ConsumableEffect.CampRest:
                    party.Inventory.RemoveConsumable(def);
                    return new WorldUseResult(true, ApplyCampRest(party) + " — 아군 턴 종료", endsAllyTurn: true);
                case ConsumableEffect.AmbushBonus:
                    if (pendingAmbushBonus > 0) return new WorldUseResult(false, $"{def.DisplayName}: 이미 효과가 남아 있음");
                    party.Inventory.RemoveConsumable(def);
                    pendingAmbushBonus = def.Amount;
                    return new WorldUseResult(true, $"[{def.DisplayName}] 다음 기습 슬롯 성공률 +{def.Amount}%p");
            }

            if (!CanUseOn(def, target, out string reason)) return new WorldUseResult(false, $"{def.DisplayName}: {reason}");
            if (def.Effect != ConsumableEffect.LifeLaurel && usedThisTurn != null && usedThisTurn.Contains(target))
                return new WorldUseResult(false, $"{target.Job.DisplayName}: 이번 아군 턴에 이미 사용함");
            party.Inventory.RemoveConsumable(def);
            if (def.Effect != ConsumableEffect.LifeLaurel) usedThisTurn?.Add(target);
            return new WorldUseResult(true, ApplyToMember(def, target));
        }

        private static string StatusLabel(StatusEffectType t) => t switch
        {
            StatusEffectType.WeakBleed => "출혈",
            StatusEffectType.Poison => "중독",
            StatusEffectType.Confusion => "혼란",
            StatusEffectType.Bind => "속박",
            _ => t.ToString(),
        };
    }
}
