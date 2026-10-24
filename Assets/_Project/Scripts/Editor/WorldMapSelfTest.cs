using System.Linq;
using Fantasia.Board;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 월드맵 글자 격자(Data/Boards/WorldMap.txt)를 기획 PDF(Docs/Planning/WorldMap/)의 숫자와 대조한다:
    // 전체 칸 수, 지역별 칸·특수 지점 수, 지점표의 "거점까지 최단 칸 수", 인접 칸 판정 몇 개.
    public static class WorldMapSelfTest
    {
        public const string MapPath = "Assets/_Project/Data/Boards/WorldMap.txt";

        [MenuItem("Fantasia/Data/Run World Map Self-Test")]
        public static void Run()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(MapPath);
            if (asset == null) { Debug.LogError($"[WorldMapSelfTest] {MapPath} 없음"); return; }
            var map = WorldMapData.Parse(asset.text);
            int e = 0;

            // 전체 (PDF 2쪽): 이동 가능 501 / 장애물 216 / 잠긴 출구 3, 특수 58
            var tiles = map.Tiles.Values.ToList();
            e += Expect("이동 가능 칸", tiles.Count(t => t.IsPassable), 501);
            e += Expect("장애물", tiles.Count(t => t.Kind == MapTileKind.Blocked), 216);
            e += Expect("잠긴 출구", tiles.Count(t => t.Kind == MapTileKind.Exit), 3);
            e += Expect("일반 적", tiles.Count(t => t.Kind == MapTileKind.Enemy), 34);
            e += Expect("보스", tiles.Count(t => t.Kind == MapTileKind.Boss), 3);

            // 지역 (PDF 3·18·33쪽): 출구 잠금 중 연결된 이동 칸, 일반 적/이벤트/회복/보물
            var expected = new (int cells, int enemy, int ev, int rec, int tre)[] { (168, 10, 2, 2, 1), (163, 10, 2, 2, 1), (170, 14, 2, 2, 1) };
            for (int r = 0; r < 3; r++)
            {
                var region = map.TilesInRegion(r).ToList();
                string n = WorldMapData.RegionDefs[r].name;
                e += Expect($"{n} 이동 칸", region.Count, expected[r].cells);
                e += Expect($"{n} 일반 적", region.Count(t => t.Kind == MapTileKind.Enemy), expected[r].enemy);
                e += Expect($"{n} 이벤트", region.Count(t => t.Kind == MapTileKind.Event), expected[r].ev);
                e += Expect($"{n} 회복", region.Count(t => t.Kind == MapTileKind.Recovery), expected[r].rec);
                e += Expect($"{n} 보물", region.Count(t => t.Kind == MapTileKind.Treasure), expected[r].tre);
                e += Expect($"{n} 보스", region.Count(t => t.Kind == MapTileKind.Boss), 1);
            }

            // 지점표의 거점 거리 (출구는 개방 후 거리)
            var checks = new (string hub, string target, int dist)[]
            {
                ("S05", "M11", 9), ("S05", "N15", 13), ("S05", "O18", 15), ("S05", "P04", 3), ("S05", "X13", 11), ("S05", "K12", 11), ("S05", "J06", 9),
                ("F18", "I25", 8), ("F18", "H08", 11), ("F18", "G29", 11), ("F18", "E09", 11), ("F18", "K26", 10), ("F18", "A21", 7),
                ("S31", "U35", 5), ("S31", "P20", 12), ("S31", "O22", 11), ("S31", "W24", 10), ("S31", "U36", 6), ("S31", "L36", 9),
            };
            foreach (var (hub, target, dist) in checks)
            {
                var a = map.TileAt(hub).Coord;
                var b = map.TileAt(target).Coord;
                bool isExit = map.TileAt(target).Kind == MapTileKind.Exit;
                e += Expect($"{hub}→{target} 거리", map.Distance(a, b, isExit ? _ => true : null), dist);
            }

            // 인접 판정 (PDF 지점 쪽): S05 왼쪽 아래 T04 이동 불가, 오른쪽 위 R05 이동 가능 / K12는 L11·J12와 맞닿음
            e += ExpectNeighbor(map, "S05", "T04", MapTileKind.Blocked);
            e += ExpectNeighbor(map, "S05", "R05", MapTileKind.Walkable);
            e += ExpectNeighbor(map, "K12", "L11", MapTileKind.Walkable);
            e += ExpectNeighbor(map, "K12", "J12", MapTileKind.Walkable);
            e += ExpectNeighbor(map, "K26", "L26", MapTileKind.Walkable);
            e += Expect("출구 잠금 중 런던→베르사유 거점 연결 없음", map.Distance(map.TileAt("S05").Coord, map.TileAt("F18").Coord), -1);
            e += ExpectTrue("출구 개방 후 런던→베르사유 연결", map.Distance(map.TileAt("S05").Coord, map.TileAt("F18").Coord, _ => true) > 0);

            if (e == 0) Debug.Log("[WorldMapSelfTest] 전부 통과 — 기획 PDF 숫자와 일치.");
            else Debug.LogError($"[WorldMapSelfTest] {e}건 실패.");
        }

        private static int ExpectNeighbor(WorldMapData map, string a, string b, MapTileKind kind)
        {
            var ta = map.TileAt(a);
            var tb = map.TileAt(b);
            bool adjacent = ta.Coord.DistanceTo(tb.Coord) == 1;
            if (adjacent && tb.Kind == kind) return 0;
            Debug.LogError($"[WorldMapSelfTest] {a}–{b}: 인접 {adjacent}, 종류 {tb.Kind} (기대 {kind})");
            return 1;
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[WorldMapSelfTest] {label}: 기대 {expected}, 실제 {actual}");
            return 1;
        }

        private static int ExpectTrue(string label, bool ok)
        {
            if (ok) return 0;
            Debug.LogError($"[WorldMapSelfTest] 실패: {label}");
            return 1;
        }
    }
}
