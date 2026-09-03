using System.Collections.Generic;
using System.Linq;
using Fantasia.Board;
using Fantasia.Core;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // Verifies the board reproduces identically across regenerations sharing
    // a BoardSession seed, and that clearing an encounter actually sticks —
    // this is what makes position/cleared-tile persistence across the
    // board<->combat scene round trip meaningful (rather than the board just
    // reshuffling every time the player leaves and comes back).
    public static class BoardSessionSelfTest
    {
        [MenuItem("Fantasia/Run Board Session Self-Test")]
        public static void Run()
        {
            BoardSession.EnsureExists();

            var layout1 = GenerateLayout();
            var layout2 = GenerateLayout();

            bool identical = layout1.Count == layout2.Count &&
                              layout1.All(kv => layout2.TryGetValue(kv.Key, out var other) && other == kv.Value);

            Debug.Log(identical
                ? $"[BoardSession] 통과 — 같은 시드로 재생성한 보드 {layout1.Count}개 타일 전부 동일"
                : "[BoardSession] 실패 — 재생성된 보드가 이전과 다름");

            var encounterCoord = layout1.FirstOrDefault(kv => kv.Value == TileState.Encounter).Key;
            BoardSession.Instance.ClearedEncounters.Add(encounterCoord);

            var boardGO = new GameObject("BoardSessionSelfTest_Board3");
            var board3 = boardGO.AddComponent<HexBoard>();
            board3.Generate();
            bool found = board3.TryGetTile(encounterCoord, out var clearedTile);
            // Evaluate before destroying — a destroyed Object compares equal
            // to null in Unity's overload, which would otherwise make this
            // look like a failure regardless of the real result.
            bool clearedCorrectly = found && clearedTile != null && clearedTile.IsEncounter && clearedTile.IsCleared;
            Object.DestroyImmediate(boardGO);
            Debug.Log(clearedCorrectly
                ? $"[BoardSession] 통과 — {encounterCoord} 클리어 처리 후 재생성해도 클리어 상태 유지됨"
                : $"[BoardSession] 실패 — {encounterCoord} 클리어 상태가 반영되지 않음");
        }

        private enum TileState { Base, Blocked, Encounter }

        private static Dictionary<HexCoord, TileState> GenerateLayout()
        {
            var boardGO = new GameObject("BoardSessionSelfTest_Board");
            var board = boardGO.AddComponent<HexBoard>();
            board.Generate();

            var layout = new Dictionary<HexCoord, TileState>();
            for (int q = -4; q <= 4; q++)
            {
                int r1 = Mathf.Max(-4, -q - 4);
                int r2 = Mathf.Min(4, -q + 4);
                for (int r = r1; r <= r2; r++)
                {
                    var coord = new HexCoord(q, r);
                    if (!board.TryGetTile(coord, out var tile)) continue;
                    layout[coord] = tile.IsBlocked ? TileState.Blocked : tile.IsEncounter ? TileState.Encounter : TileState.Base;
                }
            }

            Object.DestroyImmediate(boardGO);
            return layout;
        }
    }
}
