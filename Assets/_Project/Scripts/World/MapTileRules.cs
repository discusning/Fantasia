using System.Collections.Generic;
using System.Linq;
using Fantasia.Data;

namespace Fantasia.World
{
    // 지도 칸 규칙 확정안 (Planning/MapTiles.md, 2026-10-26 기획 — 대기 요청 1·3·4번 답).
    // 적 칸 조합·보상, 회복 칸, 보물 칸 고정 보상. 값은 여기 한 곳에서만 바꾼다.
    public static class MapTileRules
    {
        // ── 1. 적 칸: 칸마다 고정 조합. 경험치 = 승리 시 생존 참가자 각자, Cent = 파티 지갑에 1회 ──
        // 몬스터별 기본 경험치·Cent는 주지 않는다 (전투 전체 기준). 아이템 전리품은 기존 표 그대로 1회.
        public sealed class Encounter
        {
            public readonly string[] EnemyIds;
            public readonly int Exp, Cent;
            public Encounter(int exp, int cent, params string[] enemyIds) { EnemyIds = enemyIds; Exp = exp; Cent = cent; }
        }

        private const string Rat = "enemy.small.plague_rat";
        private const string Spider = "enemy.small.alchemic_spider_larva";
        private const string Gargoyle = "enemy.small.gargoyle_larva";
        private const string Hound = "enemy.small.shadow_hound";

        public static readonly IReadOnlyDictionary<string, Encounter> Encounters = new Dictionary<string, Encounter>
        {
            // 런던 10곳 — 거점(S05)에서 멀수록 강해짐
            ["P04"] = new(20, 40, Rat, Rat),
            ["O07"] = new(20, 40, Rat, Spider),
            ["Q01"] = new(20, 40, Rat, Rat),
            ["U01"] = new(20, 40, Rat, Spider),
            ["W04"] = new(20, 40, Spider, Spider),
            ["X07"] = new(20, 40, Rat, Gargoyle),
            ["W10"] = new(30, 60, Rat, Rat, Gargoyle),
            ["Q13"] = new(30, 60, Rat, Spider, Gargoyle),
            ["N15"] = new(35, 70, Hound, Hound),
            ["O18"] = new(35, 70, Gargoyle, Gargoyle, Spider),
        };

        public static Encounter EncounterAt(string label) =>
            label != null && Encounters.TryGetValue(label, out var e) ? e : null;

        // 적 구성 글 (UI 솔루션 Q7·Q9): 같은 적끼리 묶어 "역병 쥐 ×2 · 가고일 유체 ×1" — 지도 툴팁·조우 창·전투가 같은 목록을 쓴다.
        public static List<(EnemyDefinition enemy, int count)> Group(IEnumerable<EnemyDefinition> enemies) =>
            enemies.Where(e => e != null).GroupBy(e => e.Id).Select(g => (g.First(), g.Count())).ToList();

        public static string CompositionText(IEnumerable<EnemyDefinition> enemies)
        {
            var groups = Group(enemies);
            return $"{string.Join(" · ", groups.Select(g => $"{g.enemy.DisplayName} ×{g.count}"))} / 총 {groups.Sum(g => g.count)}마리";
        }

        public static List<EnemyDefinition> ResolveEnemies(Encounter encounter, ItemCatalog catalog)
        {
            if (encounter == null || catalog == null) return null;
            var list = encounter.EnemyIds.Select(catalog.Enemy).ToList();
            return list.All(e => e != null) ? list : null;
        }

        // ── 3. 회복 칸 = 우물 (전체 6곳, UI 솔루션 Q1 2026-10-27 — 10/26 "전원 HP·MP 30%"를 대체) ──
        // 살아 있는 1명을 골라 집중력 +1. 무료, 우물마다 플레이당 1회. HP·MP·LP·부활·상태 이상은 없음.
        // 집중력을 채울 사람이 없거나 취소하면 횟수를 쓰지 않는다.
        public const int WellFocus = 1;

        public static bool CanUseWell(PartyMember m) => m != null && m.IsAlive && m.CurrentFocus < m.MaxFocus;
        public static bool WellWouldHelp(Party party) => party.AliveMembers.Any(CanUseWell);

        // 선택한 1명에게 집중력 +1 (최대치까지). 실제로 오른 양을 돌려준다.
        public static int ApplyWell(PartyMember m) => CanUseWell(m) ? m.RestoreFocus(WellFocus) : 0;

        // ── 4. 보물 칸: 고정 보상, 타일별 파티 공유 1회, 표의 보상 전부 지급 (무작위 전리품 없음) ──
        public sealed class Treasure
        {
            public readonly int Cent;
            public readonly string[] ConsumableIds;
            public Treasure(int cent, params string[] consumableIds) { Cent = cent; ConsumableIds = consumableIds; }
        }

        public static readonly IReadOnlyDictionary<string, Treasure> Treasures = new Dictionary<string, Treasure>
        {
            ["J06"] = new(100, ConsumableIds.LifeElixir, ConsumableIds.Antidote),                    // 런던
            ["G29"] = new(160, ConsumableIds.ManaElixir, ConsumableIds.FocusCandle),                 // 베르사유
            ["L36"] = new(250, "consumable.greater_life_elixir", "consumable.white_rosemary"),       // 로마 제국
        };

        public static Treasure TreasureAt(string label) =>
            label != null && Treasures.TryGetValue(label, out var t) ? t : null;
    }
}
