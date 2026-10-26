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

            // 3. 회복 칸: 6곳, HP·MP 30% 올림 · 집중력 +1, 회복할 게 없으면 쓰지 않음
            e += Expect("회복 칸 수", map.Tiles.Values.Count(t => t.Kind == MapTileKind.Recovery), 6);
            var doctor = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_역병의사.asset");
            var party = new Party();
            var m = new PartyMember(doctor);
            party.AddMember(m);
            e += Check(!MapTileRules.RecoveryWouldHelp(party), "가득 찬 파티 — 회복 칸 쓰지 않음");
            m.TakeDamage(m.MaxHP - 1);
            m.TrySpendMP(m.MaxMP);
            m.TrySpendFocus(2);
            var (hp, mp, focus) = MapTileRules.ApplyRecovery(party);
            e += Expect("HP 30% 올림", hp, Mathf.CeilToInt(m.MaxHP * 0.3f));
            e += Expect("MP 30% 올림", mp, Mathf.CeilToInt(m.MaxMP * 0.3f));
            e += Expect("집중력 +1", focus, 1);

            // 4. 보물 칸: 3곳 전부 지도 보물 칸, 소모품 데이터 있음, 런던 J06 = 100 Cent + 생명의 영약 + 해독제
            foreach (var (label, t) in MapTileRules.Treasures.Select(p => (p.Key, p.Value)))
            {
                e += Check(map.TileAt(label).Kind == MapTileKind.Treasure, $"{label} 보물 칸");
                e += Check(t.ConsumableIds.All(id => catalog.Consumable(id) != null), $"{label} 소모품 데이터");
            }
            e += Expect("보물 칸 수", map.Tiles.Values.Count(t => t.Kind == MapTileKind.Treasure), MapTileRules.Treasures.Count);
            e += Expect("런던 보물 Cent", MapTileRules.TreasureAt("J06").Cent, 100);

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
