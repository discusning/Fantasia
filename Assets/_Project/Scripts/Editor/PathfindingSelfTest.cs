using System.Linq;
using Fantasia.Board;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // Verifies ReachabilityMap.GetPathTo returns a walkable, contiguous,
    // correctly-ordered path — the token-walk animation trusts this blindly.
    public static class PathfindingSelfTest
    {
        [MenuItem("Fantasia/Run Pathfinding Self-Test")]
        public static void Run()
        {
            var boardGO = new GameObject("PathfindingSelfTest_Board");
            var board = boardGO.AddComponent<HexBoard>();
            board.Generate();

            var start = new HexCoord(0, 0);
            int checkedPaths = 0;
            int failures = 0;

            for (int movePoints = 1; movePoints <= 4; movePoints++)
            {
                var reachability = board.ComputeReachability(start, movePoints);

                foreach (var target in reachability.ReachableTiles)
                {
                    checkedPaths++;
                    var path = reachability.GetPathTo(target);

                    if (path == null || path.Count == 0 || path.Count > movePoints || !path[^1].Equals(target))
                    {
                        Debug.LogError($"[Pathfinding] 실패: move={movePoints} target={target} path={PathToString(path)}");
                        failures++;
                        continue;
                    }

                    var previous = start;
                    foreach (var step in path)
                    {
                        bool isNeighbor = previous.DistanceTo(step) == 1;
                        bool isWalkable = board.TryGetTile(step, out var tile) && !tile.IsBlocked;
                        if (!isNeighbor || !isWalkable)
                        {
                            Debug.LogError($"[Pathfinding] 실패: move={movePoints} target={target} " +
                                           $"{previous}→{step} (인접 {isNeighbor}, 이동가능 {isWalkable})");
                            failures++;
                            break;
                        }
                        previous = step;
                    }
                }
            }

            Object.DestroyImmediate(boardGO);

            Debug.Log(failures == 0
                ? $"[Pathfinding] 통과 — 경로 {checkedPaths}개 전부 정상"
                : $"[Pathfinding] 실패 {failures}/{checkedPaths}건 발견");
        }

        private static string PathToString(System.Collections.Generic.List<HexCoord> path) =>
            path == null ? "null" : string.Join(",", path.Select(c => c.ToString()));
    }
}
