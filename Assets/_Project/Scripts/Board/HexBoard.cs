using System.Collections.Generic;
using Fantasia.Core;
using UnityEngine;

namespace Fantasia.Board
{
    // Generates a placeholder hex-shaped board (no art dependency) and answers
    // grid queries (lookup, movement range, pathing) for gameplay code to build on.
    public class HexBoard : MonoBehaviour
    {
        [SerializeField] private int radius = 4;
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private float tileGap = 0.05f;
        [SerializeField] private float tileHeight = 0.4f;
        [SerializeField, Range(0f, 1f)] private float obstacleChance = 0.18f;
        [SerializeField, Range(0f, 1f)] private float encounterChance = 0.1f;

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
                bool isEncounter = rng.NextDouble() < encounterChance;
                tile.SetEncounter(isEncounter);
                if (isEncounter && BoardSession.Instance != null && BoardSession.Instance.ClearedEncounters.Contains(coord))
                {
                    tile.SetCleared(true);
                }
            }

            _tiles[coord] = tile;
        }

        public bool TryGetTile(HexCoord coord, out HexTile tile) => _tiles.TryGetValue(coord, out tile);

        public Vector3 CoordToWorld(HexCoord coord) => transform.TransformPoint(coord.ToWorldPosition(tileSize));

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
