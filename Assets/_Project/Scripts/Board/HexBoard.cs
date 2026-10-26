using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using UnityEngine;

namespace Fantasia.Board
{
    // 보드 생성 + 칸 질의(조회·이동 범위·경로). MapAsset(Data/Boards/WorldMap.txt)이 있으면 확정 월드맵
    // (Docs/Planning/WorldMap.md, 720칸, pointy-top), 없으면 예전 무작위 육각 보드(셀프 테스트용).
    public class HexBoard : MonoBehaviour
    {
        [SerializeField] private int radius = 4;
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private float tileGap = 0.05f;
        [SerializeField] private float tileHeight = 0.4f;
        [SerializeField, Range(0f, 1f)] private float obstacleChance = 0.18f;
        [SerializeField, Range(0f, 1f)] private float encounterChance = 0.1f;
        [SerializeField, Range(0f, 1f)] private float eventChance = 0.08f;

        // 인카운터 칸에 배치할 적 후보 — 실제 소형 4종 등 콘텐츠는 D13에서 채운다.
        // 지금은 D04/D08 샘플(그림자 사냥개 1종)만 있어도 동작하게 설계. Assigned by
        // BoardTestSceneSetup.
        public EnemyDefinition[] EncounterEnemyPool = System.Array.Empty<EnemyDefinition>();

        // 확정 월드맵 글자 격자. BoardTestSceneSetup이 넣는다.
        public TextAsset MapAsset;
        // 지역별 보스 (0 런던 · 1 베르사유 · 2 로마). 비어 있으면 보스 칸은 임시로 소형 적.
        public EnemyDefinition[] RegionBosses = System.Array.Empty<EnemyDefinition>();
        public WorldMapData Map { get; private set; }
        public bool UsesWorldMap => Map != null;

        private readonly Dictionary<HexCoord, HexTile> _tiles = new Dictionary<HexCoord, HexTile>();
        private Material _tileMaterial;

        private bool _generated;

        // Tiles are placed with their base (not top surface) at the tile's
        // own transform position — callers placing anything on the board
        // surface need this to sit above it correctly.
        public float TileHeight => tileHeight;

        private void Awake()
        {
            BoardSession.EnsureExists();
            Generate();
        }

        // Awake doesn't fire reliably for objects created by editor tooling
        // outside Play mode, so callers building a scene in edit mode (see
        // BoardTestSceneSetup) must call this explicitly.
        public void Generate()
        {
            if (_generated) return;
            _generated = true;

            // GPU instancing (not dynamic batching, which Built-in RP is dropping
            // and URP never supported) is what actually pays off once every
            // tile shares one mesh + material like this.
            _tileMaterial = new Material(Shader.Find("Standard")) { enableInstancing = true };

            if (MapAsset != null)
            {
                GenerateFromMap();
                return;
            }
            // Every tile is the same size, so build the prism once and share
            // it — 61 tiles at radius 4 previously meant 61 identical meshes.
            var sharedMesh = HexMeshBuilder.BuildFlatTopHexPrism(tileSize - tileGap, tileHeight);

            // A local RNG seeded from BoardSession (not UnityEngine.Random's
            // global state) so the same board reproduces every reload without
            // making dice rolls/combat elsewhere suspiciously repetitive too.
            var rng = new System.Random(BoardSession.Instance != null ? BoardSession.Instance.BoardSeed : System.Environment.TickCount);

            for (int q = -radius; q <= radius; q++)
            {
                int r1 = Mathf.Max(-radius, -q - radius);
                int r2 = Mathf.Min(radius, -q + radius);
                for (int r = r1; r <= r2; r++)
                {
                    CreateTile(new HexCoord(q, r), sharedMesh, rng);
                }
            }
        }

        // ── 확정 월드맵 ──
        // 적 칸: 칸마다 정해진 적 (적 구성 기획 전까지 1막 소형 적 풀에서 좌표 기준 고정 추첨 — 다시 불러와도 같은 적).
        // 보스 칸도 전투 칸 (보스 데이터는 D14). 잠긴 출구는 그 지역 보스를 잡기 전까지 막힘.
        private void GenerateFromMap()
        {
            Map = WorldMapData.Parse(MapAsset.text);
            var sharedMesh = HexMeshBuilder.BuildHexPrism(tileSize - tileGap, tileHeight, pointyTop: true);
            var session = BoardSession.Instance;

            foreach (var mapTile in Map.Tiles.Values)
            {
                var coord = mapTile.Coord;
                var tile = SpawnTileObject(coord, sharedMesh, $"Hex {mapTile.Label}");
                tile.SetMapKind(mapTile.Kind, MapTileName(mapTile));
                switch (mapTile.Kind)
                {
                    case MapTileKind.Blocked:
                        tile.SetBlocked(true);
                        break;
                    case MapTileKind.Exit:
                        tile.SetBlocked(!IsExitOpen(coord));
                        break;
                    case MapTileKind.Enemy:
                    case MapTileKind.Boss:
                        int region = Map.RegionOf(coord);
                        var regionBoss = mapTile.Kind == MapTileKind.Boss && region >= 0 && region < RegionBosses.Length ? RegionBosses[region] : null;
                        // 일반 적 칸: 확정 조합이 있으면 그 첫 적을 칸 대표로 (MapTileRules), 없으면 임시 풀
                        var fixedGroup = mapTile.Kind == MapTileKind.Enemy
                            ? Fantasia.World.MapTileRules.ResolveEnemies(Fantasia.World.MapTileRules.EncounterAt(mapTile.Label), ItemCatalog.Current) : null;
                        var enemy = regionBoss != null ? regionBoss
                            : fixedGroup != null ? fixedGroup[0]
                            : EncounterEnemyPool.Length > 0 ? EncounterEnemyPool[StableIndex(mapTile, EncounterEnemyPool.Length)] : null;
                        tile.SetEncounter(enemy != null, enemy);
                        if (session != null && session.ClearedEncounters.Contains(coord)) tile.SetCleared(true);
                        break;
                    case MapTileKind.Event:
                        tile.SetEventTile(true);
                        break;
                    case MapTileKind.Recovery:
                    case MapTileKind.Treasure:
                        if (session != null && session.UsedMapTiles.Contains(coord)) tile.SetUsed(true);
                        break;
                }
            }
        }

        // 진행이 바뀌었을 때 출구 칸 막힘 다시 계산
        public void RefreshExits()
        {
            if (Map == null) return;
            foreach (var t in Map.Tiles.Values)
                if (t.Kind == MapTileKind.Exit && TryGetTile(t.Coord, out var tile)) tile.SetBlocked(!IsExitOpen(t.Coord));
        }

        // 2막(베르사유) 진입 지원 여부 — 이번 버전은 1막 클리어까지 (K12 잠금, "2막 베르사유 — 추후 공개")
        public const bool Act2Available = false;

        // 그 지역 보스를 처치했으면 지역에 맞닿은 출구가 열린다.
        public bool IsExitOpen(HexCoord exit)
        {
            var session = BoardSession.Instance;
            if (Map == null || session == null) return false;
            for (int d = 0; d < 6; d++)
            {
                int region = Map.RegionOf(exit.Neighbor(d));
                if (region < 0) continue;
                var boss = Map.TilesInRegion(region).FirstOrDefault(t => t.Kind == MapTileKind.Boss);
                // 런던(구역 0) 출구 K12: 이번 버전은 1막까지 — 2막 구현 전엔 잠금 (세이브 추가 수정안), 2막 뒤엔 보고 후 개방
                if (region == 0 && (!Act2Available || !session.StoryFlags.Contains(Fantasia.Core.ActOneStory.FlagReported))) continue;
                if (boss.Kind == MapTileKind.Boss && session.ClearedEncounters.Contains(boss.Coord)) return true;
            }
            return false;
        }

        private static int StableIndex(MapTile t, int count) => ((t.Row * 37 + t.Col * 101) % count + count) % count;

        private string MapTileName(MapTile t)
        {
            int region = Map.RegionOf(t.Coord);
            string regionName = region >= 0 ? WorldMapData.RegionDefs[region].name : null;
            string kind = t.Kind switch
            {
                MapTileKind.Hub => t.Label switch { "S05" => "그리니치 거점", "F18" => "베르사유 궁전", "S31" => "제국 광장", _ => "거점" },
                MapTileKind.Boss => t.Label switch { "M11" => "폐검시소", "I25" => "지하 예배당", "U35" => "고성 시험 시설", _ => "보스" },
                MapTileKind.Enemy => "적",
                MapTileKind.Event => "이벤트",
                MapTileKind.Recovery => "회복",
                MapTileKind.Treasure => "보물",
                MapTileKind.Exit => t.Label == "K12" && !Act2Available ? "2막 베르사유 — 추후 공개" : "출구",
                _ => null,
            };
            return kind == null ? t.Label : $"{t.Label} {(regionName != null ? regionName + " · " : "")}{kind}";
        }

        private HexTile SpawnTileObject(HexCoord coord, Mesh sharedMesh, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = LocalPosition(coord);
            go.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _tileMaterial;
            go.AddComponent<MeshCollider>().sharedMesh = sharedMesh;
            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord);
            _tiles[coord] = tile;
            return tile;
        }

        // 여러 칸에서 출발하는 이동 거리 (장애물·잠긴 출구 제외) — 자동 플레이가 막힌 길을 돌아가게.
        public Dictionary<HexCoord, int> DistanceField(IEnumerable<HexCoord> sources)
        {
            var dist = new Dictionary<HexCoord, int>();
            var queue = new Queue<HexCoord>();
            foreach (var s in sources) { dist[s] = 0; queue.Enqueue(s); }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                for (int d = 0; d < HexCoord.Directions.Length; d++)
                {
                    var n = c.Neighbor(d);
                    if (dist.ContainsKey(n) || !_tiles.TryGetValue(n, out var t) || t.IsBlocked) continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        private void CreateTile(HexCoord coord, Mesh sharedMesh, System.Random rng)
        {
            var go = new GameObject($"Hex {coord}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = coord.ToWorldPosition(tileSize);

            go.AddComponent<MeshFilter>().sharedMesh = sharedMesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _tileMaterial;

            go.AddComponent<MeshCollider>().sharedMesh = sharedMesh;

            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord);

            // Origin stays walkable and encounter-free — that's where the token spawns.
            bool isOrigin = coord.Q == 0 && coord.R == 0;
            bool blocked = !isOrigin && rng.NextDouble() < obstacleChance;
            tile.SetBlocked(blocked);

            if (!blocked && !isOrigin)
            {
                bool isEncounter = rng.NextDouble() < encounterChance && EncounterEnemyPool.Length > 0;
                var enemy = isEncounter ? EncounterEnemyPool[rng.Next(EncounterEnemyPool.Length)] : null;
                tile.SetEncounter(isEncounter, enemy);
                if (isEncounter && BoardSession.Instance != null && BoardSession.Instance.ClearedEncounters.Contains(coord))
                {
                    tile.SetCleared(true);
                }

                // 인카운터 칸과는 배타적 — 전투와 월드 이벤트가 같은 칸에서 동시에 뜨지 않게 한다.
                if (!isEncounter)
                {
                    tile.SetEventTile(rng.NextDouble() < eventChance);
                }
            }

            _tiles[coord] = tile;
        }

        public bool TryGetTile(HexCoord coord, out HexTile tile) => _tiles.TryGetValue(coord, out tile);
        public IEnumerable<HexTile> Tiles => _tiles.Values;

        public Vector3 CoordToWorld(HexCoord coord) => transform.TransformPoint(LocalPosition(coord));

        private Vector3 LocalPosition(HexCoord coord) => UsesWorldMap ? coord.ToWorldPositionPointy(tileSize) : coord.ToWorldPosition(tileSize);

        // BFS over the tile graph (not straight-line distance) so blocked tiles
        // are respected and can't be cut through or landed on. Keeps parent
        // links so callers can both list reachable tiles and reconstruct the
        // step-by-step path to any one of them from a single pass.
        public ReachabilityMap ComputeReachability(HexCoord start, int movePoints)
        {
            var parent = new Dictionary<HexCoord, HexCoord>();
            var visited = new HashSet<HexCoord> { start };
            var frontier = new Queue<(HexCoord coord, int remaining)>();
            frontier.Enqueue((start, movePoints));

            while (frontier.Count > 0)
            {
                var (coord, remaining) = frontier.Dequeue();
                if (remaining <= 0) continue;

                for (int d = 0; d < HexCoord.Directions.Length; d++)
                {
                    var neighbor = coord.Neighbor(d);
                    if (visited.Contains(neighbor)) continue;
                    if (!_tiles.TryGetValue(neighbor, out var neighborTile) || neighborTile.IsBlocked) continue;

                    visited.Add(neighbor);
                    parent[neighbor] = coord;
                    frontier.Enqueue((neighbor, remaining - 1));
                }
            }

            return new ReachabilityMap(start, parent);
        }

        public void ClearHighlights()
        {
            foreach (var tile in _tiles.Values)
            {
                tile.SetReachable(false);
            }
        }

        // Wraps one ComputeReachability() BFS pass so both "which tiles can I
        // highlight" and "what path do I walk to this one" reuse the same data.
        public sealed class ReachabilityMap
        {
            private readonly HexCoord _start;
            private readonly Dictionary<HexCoord, HexCoord> _parent;

            public ReachabilityMap(HexCoord start, Dictionary<HexCoord, HexCoord> parent)
            {
                _start = start;
                _parent = parent;
            }

            public IEnumerable<HexCoord> ReachableTiles => _parent.Keys;

            public bool Contains(HexCoord coord) => _parent.ContainsKey(coord);

            public List<HexCoord> GetPathTo(HexCoord target)
            {
                if (!_parent.ContainsKey(target)) return null;

                var path = new List<HexCoord>();
                var current = target;
                while (!current.Equals(_start))
                {
                    path.Add(current);
                    current = _parent[current];
                }
                path.Reverse();
                return path;
            }
        }
    }
}
