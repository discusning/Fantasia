using System;
using System.Collections.Generic;
using System.Linq;

namespace Fantasia.Board
{
    public enum MapTileKind
    {
        None,      // 칸 없음 (지도 바깥)
        Walkable,
        Blocked,   // 물·산·숲 장애물
        Hub,       // 고정 거점 (도시)
        Enemy,     // 일반 적 — 들어가면 전투
        Boss,      // 보스 — 처치하면 그 지역 출구 개방
        Event,
        Recovery,
        Treasure,
        Exit,      // 잠긴 출구 — 보스 처치 전 이동 불가
    }

    public readonly struct MapTile
    {
        public readonly int Row;        // 0 = A
        public readonly int Col;        // 0 = 01
        public readonly MapTileKind Kind;
        public readonly HexCoord Coord; // 보드 axial 좌표

        public MapTile(int row, int col, MapTileKind kind)
        {
            Row = row;
            Col = col;
            Kind = kind;
            Coord = WorldMapData.OffsetToAxial(row, col);
        }

        public string Label => WorldMapData.Label(Row, Col);
        public bool IsPassable => Kind != MapTileKind.None && Kind != MapTileKind.Blocked && Kind != MapTileKind.Exit;
    }

    // 확정 월드맵 (Docs/Planning/WorldMap.md) — Data/Boards/WorldMap.txt 글자 격자를 읽는다.
    // 좌표: 행 A~X(24) × 열 01~36. 짝수 번째 행(B·D·F…)이 반 칸 오른쪽 → pointy-top "odd-r" 오프셋.
    // 지역: 거점에서 출발해 잠긴 출구를 넘지 않고 닿는 칸 (런던 168 · 베르사유 163 · 로마 제국 170).
    public class WorldMapData
    {
        public const int Rows = 24, Cols = 36;

        public static readonly (string name, string hubLabel)[] RegionDefs =
        {
            ("런던", "S05"),
            ("베르사유", "F18"),
            ("로마 제국", "S31"),
        };

        private readonly Dictionary<HexCoord, MapTile> _tiles = new();
        private readonly Dictionary<HexCoord, int> _region = new(); // 칸 → 지역 번호 (출구·장애물은 없음)

        public IReadOnlyDictionary<HexCoord, MapTile> Tiles => _tiles;

        public static WorldMapData Parse(string text)
        {
            var map = new WorldMapData();
            int row = 0;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split(' ');
                if (parts.Length != 2 || parts[1].Length != Cols)
                    throw new FormatException($"월드맵 {row + 1}번째 행 형식 오류: '{line}'");
                for (int col = 0; col < Cols; col++)
                {
                    var kind = KindOf(parts[1][col]);
                    if (kind == MapTileKind.None) continue;
                    var tile = new MapTile(row, col, kind);
                    map._tiles[tile.Coord] = tile;
                }
                row++;
            }
            if (row != Rows) throw new FormatException($"월드맵 행 수 {row} (기대 {Rows})");
            map.ComputeRegions();
            return map;
        }

        public static MapTileKind KindOf(char c) => c switch
        {
            '.' => MapTileKind.Walkable,
            '#' => MapTileKind.Blocked,
            'H' => MapTileKind.Hub,
            'E' => MapTileKind.Enemy,
            'B' => MapTileKind.Boss,
            'V' => MapTileKind.Event,
            'R' => MapTileKind.Recovery,
            'T' => MapTileKind.Treasure,
            'X' => MapTileKind.Exit,
            _ => MapTileKind.None,
        };

        // odd-r 오프셋(행, 열) ↔ axial. 홀수 인덱스 행(B·D·F…)이 오른쪽으로 반 칸.
        public static HexCoord OffsetToAxial(int row, int col) => new(col - (row - (row & 1)) / 2, row);
        public static (int row, int col) AxialToOffset(HexCoord c) => (c.R, c.Q + (c.R - (c.R & 1)) / 2);

        public static string Label(int row, int col) => $"{(char)('A' + row)}{col + 1:00}";

        public static bool TryParseLabel(string label, out HexCoord coord)
        {
            coord = default;
            if (label == null || label.Length != 3 || label[0] < 'A' || label[0] >= 'A' + Rows) return false;
            if (!int.TryParse(label.Substring(1), out int col) || col < 1 || col > Cols) return false;
            coord = OffsetToAxial(label[0] - 'A', col - 1);
            return true;
        }

        public bool TryGetTile(HexCoord coord, out MapTile tile) => _tiles.TryGetValue(coord, out tile);

        public MapTile TileAt(string label) =>
            TryParseLabel(label, out var c) && _tiles.TryGetValue(c, out var t) ? t : throw new ArgumentException($"없는 칸: {label}");

        // 지역 번호 (0 런던 · 1 베르사유 · 2 로마 제국), 출구·장애물·지도 밖은 -1
        public int RegionOf(HexCoord coord) => _region.TryGetValue(coord, out int r) ? r : -1;
        public string RegionName(HexCoord coord) => RegionOf(coord) is int r and >= 0 ? RegionDefs[r].name : null;
        public IEnumerable<MapTile> TilesInRegion(int region) => _tiles.Values.Where(t => RegionOf(t.Coord) == region);

        // 출구는 그 출구와 맞닿은 지역 둘을 잇는다 (보스 처치 후). 잠겨 있으면 통과 불가.
        public IEnumerable<HexCoord> PassableNeighbors(HexCoord c, Func<HexCoord, bool> exitOpen = null)
        {
            for (int d = 0; d < 6; d++)
            {
                var n = c.Neighbor(d);
                if (!_tiles.TryGetValue(n, out var t)) continue;
                if (t.IsPassable || (t.Kind == MapTileKind.Exit && exitOpen != null && exitOpen(n))) yield return n;
            }
        }

        // 최단 칸 수 (잠긴 출구 통과 안 함). 닿지 못하면 -1.
        public int Distance(HexCoord from, HexCoord to, Func<HexCoord, bool> exitOpen = null)
        {
            if (from.Equals(to)) return 0;
            var dist = new Dictionary<HexCoord, int> { [from] = 0 };
            var queue = new Queue<HexCoord>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var n in PassableNeighbors(c, exitOpen))
                {
                    if (dist.ContainsKey(n)) continue;
                    dist[n] = dist[c] + 1;
                    if (n.Equals(to)) return dist[n];
                    // 출구 칸 자체가 목적지가 아니면 출구를 지나 계속 간다
                    queue.Enqueue(n);
                }
            }
            return -1;
        }

        private void ComputeRegions()
        {
            for (int r = 0; r < RegionDefs.Length; r++)
            {
                if (!TryParseLabel(RegionDefs[r].hubLabel, out var hub) || !_tiles.ContainsKey(hub)) continue;
                var queue = new Queue<HexCoord>();
                queue.Enqueue(hub);
                _region[hub] = r;
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    foreach (var n in PassableNeighbors(c))
                    {
                        if (_region.ContainsKey(n)) continue;
                        _region[n] = r;
                        queue.Enqueue(n);
                    }
                }
            }
        }
    }
}
