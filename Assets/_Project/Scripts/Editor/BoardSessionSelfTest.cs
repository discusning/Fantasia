using System.Linq;
using Fantasia.Board;
using Fantasia.Core;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 보드 ↔ 전투 씬을 오가도 보드 상태가 이어지는지 (확정 월드맵 기준, 2026-10-24 재작성):
    // 다시 생성해도 같은 칸 배치, 적 칸 클리어 유지, 보스 처치 → 지역 출구 개방, 회복·보물 사용 기록 유지.
    public static class BoardSessionSelfTest
    {
        [MenuItem("Fantasia/Run Board Session Self-Test")]
        public static void Run()
        {
            BoardSession.EnsureExists();
            var session = BoardSession.Instance;
            session.ClearedEncounters.Clear();
            session.UsedMapTiles.Clear();
            session.StoryFlags.Clear();
            int e = 0;

            var first = Build(out var map);
            if (map == null) { Debug.LogError("[BoardSession] 월드맵 자산 없음"); return; }
            int tileCount = first.Tiles.Count();
            var enemyCoord = map.TileAt("P04").Coord;
            var enemyBefore = first.TryGetTile(enemyCoord, out var t0) ? t0.Enemy : null;
            Object.DestroyImmediate(first.gameObject);

            // 1) 다시 만들어도 같은 배치·같은 적
            var second = Build(out _);
            e += Check(second.Tiles.Count() == tileCount, $"재생성 칸 수 {tileCount}");
            e += Check(second.TryGetTile(enemyCoord, out var t1) && t1.Enemy == enemyBefore && enemyBefore != null, "P04 적 칸의 적이 매번 같음");
            e += Check(second.TryGetTile(map.TileAt("K12").Coord, out var exit) && exit.IsBlocked, "보스 처치 전 K12 출구 잠김");
            Object.DestroyImmediate(second.gameObject);

            // 2) 클리어·보스 처치·회복 사용 후 다시 생성
            session.ClearedEncounters.Add(enemyCoord);
            session.ClearedEncounters.Add(map.TileAt("M11").Coord); // 런던 보스
            session.UsedMapTiles.Add(map.TileAt("S16").Coord);
            var bossOnly = Build(out _);
            e += Check(bossOnly.TryGetTile(map.TileAt("K12").Coord, out var exitB) && exitB.IsBlocked, "보스 처치만으로는 K12 잠김 (제임스 보고 전)");
            Object.DestroyImmediate(bossOnly.gameObject);
            session.StoryFlags.Add(Fantasia.Core.ActOneStory.FlagReported); // 제임스 보고 = 1막 완료
            var third = Build(out _);
            e += Check(third.TryGetTile(enemyCoord, out var t2) && t2.IsCleared, "P04 클리어 유지");
            // 이번 버전은 1막까지 — 보고 뒤에도 K12는 2막 구현 전까지 잠김 ("2막 베르사유 — 추후 공개", 세이브 추가 수정안 1)
            e += Check(third.TryGetTile(map.TileAt("K12").Coord, out var exit2) && exit2.IsBlocked == !HexBoard.Act2Available, "보고 후 K12: 2막 전엔 잠김");
            e += Check(third.TryGetTile(map.TileAt("K26").Coord, out var exit3) && exit3.IsBlocked, "베르사유 출구 K26은 그대로 잠김");
            e += Check(third.TryGetTile(map.TileAt("S16").Coord, out var rec) && rec.IsUsed, "회복 칸 S16 사용 기록 유지");
            var reach = third.ComputeReachability(map.TileAt("L11").Coord, 2);
            e += Check(reach.Contains(map.TileAt("J12").Coord) == HexBoard.Act2Available, "2막 전엔 L11 → J12 (베르사유) 이동 불가");
            Object.DestroyImmediate(third.gameObject);

            session.ClearedEncounters.Clear();
            session.UsedMapTiles.Clear();
            session.StoryFlags.Clear();
            if (e == 0) Debug.Log("[BoardSession] 통과 — 월드맵 재생성·클리어·출구 개방·사용 기록 유지");
            else Debug.LogError($"[BoardSession] {e}건 실패");
        }

        private static HexBoard Build(out WorldMapData map)
        {
            var go = new GameObject("BoardSessionSelfTest_Board");
            var board = go.AddComponent<HexBoard>();
            board.MapAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(WorldMapSelfTest.MapPath);
            board.EncounterEnemyPool = new[] { "Enemy_그림자사냥개", "Enemy_연성독거미유체", "Enemy_역병쥐", "Enemy_가고일유체" }
                .Select(n => AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"Assets/_Project/Data/Enemies/{n}.asset"))
                .Where(x => x != null).ToArray();
            board.Generate();
            map = board.Map;
            return board;
        }

        private static int Check(bool ok, string label)
        {
            if (ok) return 0;
            Debug.LogError($"[BoardSession] 실패: {label}");
            return 1;
        }
    }
}
