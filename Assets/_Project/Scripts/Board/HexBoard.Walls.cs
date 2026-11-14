using System.Collections.Generic;
using Fantasia.World;
using UnityEngine;

namespace Fantasia.Board
{
    // 11/14 팀장: 돌담 배치 다시 — 칸 가운데 덩그러니 놓인 담 하나가 어색함.
    //  - 앞줄 막힌 칸(뒤 칸을 가리면 안 되는 칸)을 이어진 덩어리로 묶고, 덩어리째 돌담 / 낮은 나무를 정함 (담이 중간에 끊기지 않게)
    //  - 돌담은 길과 맞닿은 칸 모서리를 따라 한 조각씩 → 이웃 돌담 칸과 모서리에서 이어져 한 줄 담장이 됨
    //  - 뾰족한 육각 칸이라 모서리는 가로에서 ±30° 또는 세로 → 카메라(+Z를 내려다봄)에서 담 옆면이 보이고 정면으로 막히지 않음
    //  - 담 안쪽은 덤불·풀을 심어 작은 정원처럼 (빈 칸이 아니라 "담 두른 마당"으로 읽히게)
    public partial class HexBoard
    {
        private Dictionary<HexCoord, int> _frontGroup;
        private HashSet<int> _stoneGroups;
        public static int DevWallSegments { get; private set; }
        public static readonly List<Vector3> DevWallSpots = new();

        // 앞줄 막힌 칸에 세울 낮은 소품 (덩어리째 고정 추첨: 돌담 2 : 나무 3)
        private GameObject FrontRowProp(MapTile t)
        {
            if (_art.LowStone == null || _art.LowTrees == null) return _art.LowBlockFor(t);
            BuildFrontGroups();
            return _frontGroup.TryGetValue(t.Coord, out var g) && _stoneGroups.Contains(g) ? _art.LowStone : _art.LowTrees;
        }

        private void BuildFrontGroups()
        {
            if (_frontGroup != null) return;
            _frontGroup = new Dictionary<HexCoord, int>(); _stoneGroups = new HashSet<int>();
            int id = 0;
            foreach (var t in Map.Tiles.Values)
            {
                if (t.Kind != MapTileKind.Blocked || _frontGroup.ContainsKey(t.Coord) || !HidesTileBehind(t.Coord)) continue;
                var q = new Queue<HexCoord>(); q.Enqueue(t.Coord); _frontGroup[t.Coord] = id;
                int minRow = t.Row, minCol = t.Col, size = 0;
                while (q.Count > 0)
                {
                    var c = q.Dequeue(); size++;
                    var mt = Map.Tiles[c]; if (mt.Row < minRow || (mt.Row == minRow && mt.Col < minCol)) { minRow = mt.Row; minCol = mt.Col; }
                    for (int d = 0; d < 6; d++)
                    {
                        var n = c.Neighbor(d);
                        if (_frontGroup.ContainsKey(n) || !Map.Tiles.TryGetValue(n, out var nt) || nt.Kind != MapTileKind.Blocked || !HidesTileBehind(n)) continue;
                        _frontGroup[n] = id; q.Enqueue(n);
                    }
                }
                if (Hash(minRow, minCol, 97) % 5 < 2) _stoneGroups.Add(id);
                id++;
            }
        }

        private void BuildStoneWalls(Transform root)
        {
            DevWallSegments = 0; DevWallSpots.Clear(); DevGardens = 0; DevGardenProps = 0; DevGardens = 0; DevGardenProps = 0;
            if (_art.LowStone == null) return;
            BuildFrontGroups();
            foreach (var kv in _frontGroup)
            {
                if (!_stoneGroups.Contains(kv.Value)) continue;
                var coord = kv.Key;
                var center = LocalPosition(coord);
                var mapTile = Map.Tiles[coord];
                int placed = 0;
                for (int d = 0; d < 6; d++)
                {
                    var n = coord.Neighbor(d);
                    if (!Map.Tiles.TryGetValue(n, out var nt) || nt.Kind == MapTileKind.Blocked) continue; // 길 쪽 모서리만 (돌담 칸끼리는 안쪽)
                    var v = LocalPosition(n) - center; v.y = 0f;
                    var along = new Vector3(-v.z, 0f, v.x).normalized;
                    var mid = center + v * 0.5f * WallInset + Vector3.up * tileHeight;
                    PlaceWallSegment(root, mid, along, tileSize * WallInset * 1.08f);
                    placed++;
                }
                if (placed == 0) continue;
                DevWallSpots.Add(transform.TransformPoint(center));
                BuildWalledGarden(root, center, mapTile);
            }
        }

        // 11/14 팀장 "돌담 안쪽이 휑함 — 더 풍성하게, 튀지 않게, 칙칙하지 않게":
        // 칸마다 정원 3종 중 하나(좌표로 고정 추첨) — 가운데 주인공 1 + 둘레 소품 + 풀밭(풀 여러 포기)으로 빈 바닥을 덮음.
        //  꽃밭: 꽃 무더기·큰 잎 화초·디딤돌 / 텃밭: 당근·사과 상자·양동이 / 쉼터: 작은 나무·화분·의자
        // 높이는 모두 낮게(작은 나무만 0.62) — 앞줄 칸이라 뒤 칸·말을 가리면 안 됨. 큰 것은 뒤(+Z), 작은 것은 앞.
        public static int DevGardens { get; private set; }
        public static int DevGardenProps { get; private set; }
        private static readonly string[][] GardenThemes =
        {
            new[] { "GardenBush", "PlantBig", "Flowers", "Flowers", "StepStones", "GardenFern", "Mushroom" },          // 꽃밭 (11/14 QA: 크기 키우고 덤불로 부피)
            new[] { "GardenBush", "CarrotCrate", "GardenApples", "Flowers", "GardenBucket", "GrassTall", "GardenPebble" }, // 텃밭
            new[] { "SmallTree", "GardenBush", "Pot", "Flowers", "Stool", "Vase", "Flowers" },                   // 쉼터
        };

        private void BuildWalledGarden(Transform root, Vector3 center, MapTile mapTile)
        {
            uint h = Hash(mapTile.Row, mapTile.Col, 41);
            var theme = GardenThemes[h % (uint)GardenThemes.Length];
            DevGardens++;
            // 자리: 가운데 뒤쪽 1(주인공) + 둘레 6 (뒤 3 = 큰 것, 앞 3 = 작은 것). 담(반지름 약 0.39)에 닿지 않게 0.28 안쪽
            var slots = new (float ang, float r)[]
            {
                (90f, 0.06f),
                (40f, 0.25f), (140f, 0.25f), (90f, 0.28f),
                (-30f, 0.24f), (-150f, 0.24f), (-90f, 0.2f),
            };
            float spin = (h >> 8) % 40 - 20f; // 칸마다 조금씩 돌려 똑같아 보이지 않게
            for (int i = 0; i < theme.Length && i < slots.Length; i++)
            {
                float a = (slots[i].ang + spin) * Mathf.Deg2Rad;
                var p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (tileSize * slots[i].r) + Vector3.up * tileHeight;
                PlaceDeco(theme[i], root, p, h >> (i * 3));
                DevGardenProps++;
            }
            // 풀밭: 빈 바닥이 안 보이게 풀 8포기를 고리 두 겹으로
            for (int i = 0; i < 8; i++)
            {
                float a = (i * 45f + spin + ((h >> (i + 3)) % 18)) * Mathf.Deg2Rad, r = tileSize * (i % 2 == 0 ? 0.15f : 0.33f);
                var p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r + Vector3.up * tileHeight;
                PlaceDeco(i % 3 == 0 ? "GardenWispy" : "GardenGrass", root, p, h >> i);
                DevGardenProps++;
            }
        }

        private const float WallInset = 0.88f; // 모서리에서 조금 안쪽 (이웃 길 칸 선을 덮지 않게)
        private float _wallYaw0 = float.NaN, _wallLen0, _wallHeight0;

        private void PlaceWallSegment(Transform root, Vector3 mid, Vector3 along, float length)
        {
            var go = Instantiate(_art.LowStone, root);
            go.name = "Scatter_StoneWall";
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            go.transform.localPosition = mid;
            if (float.IsNaN(_wallYaw0))
            {
                // 모델 긴 축 재기 (한 번만): X가 길면 0°, Z가 길면 90°
                go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                var b0 = Bounds(go);
                _wallYaw0 = b0.size.z > b0.size.x ? 90f : 0f;
                _wallLen0 = Mathf.Max(b0.size.x, b0.size.z) / Mathf.Max(0.0001f, transform.lossyScale.x);
                _wallHeight0 = b0.size.y / Mathf.Max(0.0001f, transform.lossyScale.y);
            }
            float yaw = -Mathf.Atan2(along.z, along.x) * Mathf.Rad2Deg + _wallYaw0;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float s = length / Mathf.Max(0.0001f, _wallLen0);
            float hs = Mathf.Min(s, tileSize * 0.34f / Mathf.Max(0.0001f, _wallHeight0)); // 낮게 (뒤 칸·말을 가리지 않게)
            go.transform.localScale = new Vector3(_wallYaw0 == 0f ? s : s, hs, s);
            var b = Bounds(go);
            float groundY = transform.TransformPoint(mid).y;
            go.transform.position += Vector3.up * (groundY - b.min.y);
            DevWallSegments++;
        }
    }
}
