using System.Linq;
using Fantasia.Board;
using Fantasia.Data;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 지도 칸 규칙 확정안 (Planning/MapTiles.md) 대조: 런던 적 칸 10곳 합계, 좌표가 지도와 맞는지, 회복 계산, 보물 표.
    public static class MapTileRulesSelfTest
    {
        [MenuItem("Fantasia/Data/Run Map Tile Rules Self-Test")]
        public static void Run()
        {
            int e = 0;
            var map = WorldMapData.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(WorldMapSelfTest.MapPath).text);
            var catalog = ItemCatalog.Current;

            // 1. 적 칸: 10전투 · 23마리 · 경험치 250 · Cent 500, 전부 런던 일반 적 칸, 적 데이터 있음
            var enc = MapTileRules.Encounters;
            e += Expect("적 칸 수", enc.Count, 10);
            e += Expect("적 수 합계", enc.Values.Sum(x => x.EnemyIds.Length), 23);
            e += Expect("경험치 합계", enc.Values.Sum(x => x.Exp), 250);
            e += Expect("Cent 합계", enc.Values.Sum(x => x.Cent), 500);
            foreach (var (label, x) in enc.Select(p => (p.Key, p.Value)))
            {
                var t = map.TileAt(label);
                e += Check(t.Kind == MapTileKind.Enemy && map.RegionOf(t.Coord) == 0, $"{label} 런던 일반 적 칸");
                e += Check(MapTileRules.ResolveEnemies(x, catalog) != null, $"{label} 적 데이터");
            }
            e += Expect("런던 일반 적 칸 전부 배치", map.TilesInRegion(0).Count(t => t.Kind == MapTileKind.Enemy && !enc.ContainsKey(t.Label)), 0);
            // 성장 목표: 같은 캐릭터가 8칸 이기면 3레벨(누적 180), 10칸 다 이겨도 4레벨(360) 미만
            int lowest8 = enc.Values.Select(x => x.Exp).OrderBy(v => v).Take(8).Sum();
            e += Check(lowest8 >= 180 && enc.Values.Sum(x => x.Exp) < 360, $"어느 8칸 = 3레벨 ({lowest8}), 10칸 < 4레벨");

            // 3. 회복 칸 = 우물 (UI 솔루션 Q1): 6곳, 고른 1명만 집중력 +1, HP·MP는 그대로, 모두 가득이면 쓰지 않음
            e += Expect("회복 칸(우물) 수", map.Tiles.Values.Count(t => t.Kind == MapTileKind.Recovery), 6);
            var doctor = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_역병의사.asset");
            var party = new Party();
            var m = new PartyMember(doctor);
            var other = new PartyMember(doctor);
            party.AddMember(m);
            party.AddMember(other);
            e += Check(!MapTileRules.WellWouldHelp(party), "모두 집중력 가득 — 우물 쓰지 않음");
            m.TakeDamage(m.MaxHP - 1);
            m.TrySpendMP(m.MaxMP);
            m.TrySpendFocus(2);
            other.TrySpendFocus(2);
            int hpBefore = m.CurrentHP, mpBefore = m.CurrentMP, otherFocus = other.CurrentFocus;
            e += Check(MapTileRules.WellWouldHelp(party) && MapTileRules.CanUseWell(m), "집중력이 빈 동료가 있으면 사용 가능");
            e += Expect("고른 1명 집중력 +1", MapTileRules.ApplyWell(m), 1);
            e += Check(m.CurrentHP == hpBefore && m.CurrentMP == mpBefore, "HP·MP는 그대로");
            e += Expect("다른 동료 집중력 그대로", other.CurrentFocus, otherFocus);
            while (m.CurrentFocus < m.MaxFocus) m.RestoreFocus(1);
            e += Check(!MapTileRules.CanUseWell(m) && MapTileRules.ApplyWell(m) == 0, "집중력 가득 찬 동료는 고를 수 없음");

            // 4. 보물 칸: 3곳 전부 지도 보물 칸, 소모품 데이터 있음, 런던 J06 = 100 Cent + 생명의 영약 + 해독제
            foreach (var (label, t) in MapTileRules.Treasures.Select(p => (p.Key, p.Value)))
            {
                e += Check(map.TileAt(label).Kind == MapTileKind.Treasure, $"{label} 보물 칸");
                e += Check(t.ConsumableIds.All(id => catalog.Consumable(id) != null), $"{label} 소모품 데이터");
            }
            e += Expect("보물 칸 수", map.Tiles.Values.Count(t => t.Kind == MapTileKind.Treasure), MapTileRules.Treasures.Count);
            e += Expect("런던 보물 Cent", MapTileRules.TreasureAt("J06").Cent, 100);

            // UI 솔루션 Q3·Q4·Q7·Q9 규칙
            e += Check(Fantasia.UI.BoardTopBar.TimeOfDayIndex(1) == 0 && Fantasia.UI.BoardTopBar.TimeOfDayIndex(2) == 1
                       && Fantasia.UI.BoardTopBar.TimeOfDayIndex(3) == 2 && Fantasia.UI.BoardTopBar.TimeOfDayIndex(4) == 3
                       && Fantasia.UI.BoardTopBar.TimeOfDayIndex(5) == 0, "Q3 아침·낮·저녁·새벽 = (월드 R − 1) % 4");
            e += Check(Fantasia.UI.MainMenuController.CleanName("  ") == "탐정" && Fantasia.UI.MainMenuController.CleanName(" 홈즈\n ") == "홈즈"
                       && Fantasia.UI.MainMenuController.CleanName("가나다라마바사아자차카") == "가나다라마바사아자차", "Q4 이름 정리 (빈 값·공백·줄바꿈·10글자)");
            var w10 = MapTileRules.ResolveEnemies(MapTileRules.EncounterAt("W10"), catalog);
            e += Check(MapTileRules.CompositionText(w10) == "역병 쥐 ×2 · 가고일 유체 ×1 / 총 3마리", $"Q7·Q9 W10 구성 글 ({MapTileRules.CompositionText(w10)})");
            e += Expect("Q9 Q13 종류 수", MapTileRules.Group(MapTileRules.ResolveEnemies(MapTileRules.EncounterAt("Q13"), catalog)).Count, 3);
            e += Expect("Q7 사냥개만 나오는 칸 수", enc.Values.Count(x => x.EnemyIds.All(id => id == "enemy.small.shadow_hound")), 1);

            if (e == 0) Debug.Log("[MapTileRulesSelfTest] 전부 통과.");
            else Debug.LogError($"[MapTileRulesSelfTest] {e}건 실패.");
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[MapTileRulesSelfTest] {label}: 기대 {expected}, 실제 {actual}");
            return 1;
        }

        private static int Check(bool ok, string label)
        {
            if (ok) return 0;
            Debug.LogError($"[MapTileRulesSelfTest] 실패: {label}");
            return 1;
        }
    }
}
