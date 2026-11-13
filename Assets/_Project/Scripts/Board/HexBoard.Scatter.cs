using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.Board
{
    // 11/13 팀장: 보드 장식 A+B+C안 (규칙·이동·클릭 영향 없음, 충돌체 지움, 좌표로 고정 무작위라 다시 불러와도 같음).
    //  A. 건물 밑동: 집이 서 있는 막힌 칸의 카메라 쪽 벽 밑에 낮은 소품(풀·덤불·통·상자)을 붙여 땅에 자리 잡게.
    //  B. 구역 테마: 거점에서 칸 거리로 시장(≤3) · 주택가(≤7) · 외곽. 소품 종류와 밀도가 다름 (시장 빽빽, 외곽 듬성).
    //  C. 크기 위계: 장면마다 큰 것 1(뒤) + 중간(가운데) + 작은 것(앞).
    // 플레이어 시점 (보드 카메라: 정면 +Z를 52° 내려다봄):
    //  - 장면은 길이 카메라 쪽·옆에 있는 막힌 칸에만 (뒤쪽이면 집에 가려 안 보임).
    //  - 벤치·수레·상자처럼 납작한 것은 긴 면이 카메라를 보게 (옆으로 보이면 선처럼 얇아 보임), ±12°만 틀어 딱딱하지 않게.
    //  - 큰 것은 뒤(+Z), 작은 것은 앞(−Z) → 작은 것이 큰 것에 가리지 않음. 가로등은 장면 옆 뒤쪽.
    //  - 칸 가운데(말 자리)와 지나가는 칸의 앞쪽은 비움.
    //  소품 = Quaternius Fantasy Props · Stylized Nature (CC0) → Resources/BoardArt/Deco (Editor/QuaterniusSetup)
    public partial class HexBoard
    {
        private struct ScatterKind { public string Path; public float Size; public bool Height; public bool Flat; public ScatterKind(string p, float s, bool h, bool flat = false) { Path = p; Size = s; Height = h; Flat = flat; } }
        // Size: Height면 높이, 아니면 가로 최대 길이 — tileSize 배수 (가로등 높이 0.95 기준 비례). Flat = 긴 면이 카메라를 봄
        private static readonly Dictionary<string, ScatterKind> Deco = new()
        {
            ["Lamp"] = new("BattleArt/StreetLamp", 0.95f, true),
            ["Barrel"] = new("BoardArt/Deco/Barrel", 0.2f, false),
            ["BarrelApples"] = new("BoardArt/Deco/Barrel_Apples", 0.2f, false),
            ["Crate"] = new("BoardArt/Deco/Crate_Wooden", 0.22f, false, true),
            ["FarmCrate"] = new("BoardArt/Deco/FarmCrate_Empty", 0.24f, false, true),
            ["FarmCrateApple"] = new("BoardArt/Deco/FarmCrate_Apple", 0.24f, false, true),
            ["Bucket"] = new("BoardArt/Deco/Bucket_Wooden_1", 0.11f, false),
            ["Bag"] = new("BoardArt/Deco/Bag", 0.13f, false),
            ["Bench"] = new("BoardArt/Deco/Bench", 0.46f, false, true),
            ["Stall"] = new("BoardArt/Deco/Stall_Cart_Empty", 0.58f, false, true),
            ["Bush"] = new("BattleArt/Trees_B", 0.34f, false), // 11/13 QA: 무료판 덤불은 잎 그림이 모델과 안 맞아(안 보이거나 빨감) 기존 덤불 모델
            ["BushFlowers"] = new("BoardArt/Deco/Plant_1", 0.26f, false), // 같은 이유로 풀포기
            ["Rock1"] = new("BoardArt/Deco/Rock_Medium_1", 0.24f, false),
            ["Rock2"] = new("BoardArt/Deco/Rock_Medium_2", 0.28f, false),
            ["Rock3"] = new("BoardArt/Deco/Rock_Medium_3", 0.22f, false),
            ["Grass"] = new("BoardArt/Deco/Grass_Common_Short", 0.2f, false),
            ["GrassWispy"] = new("BoardArt/Deco/Grass_Wispy_Short", 0.2f, false),
            ["Fern"] = new("BoardArt/Deco/Fern_1", 0.22f, false),
            ["Plant"] = new("BoardArt/Deco/Plant_1", 0.2f, false),
            ["Pebble"] = new("BoardArt/Deco/Pebble_Round_2", 0.1f, false),
        };

        private enum Zone { Market, Residential, Outskirts }

        // 가로등 장면 — (이름, x: 화면 가로, z: 깊이 +면 뒤) tileSize 배수. 가로등은 x=+0.34·z=+0.06 (장면 좌우는 뒤집힘)
        private static readonly Dictionary<Zone, (string k, float x, float z)[][]> Scenes = new()
        {
            [Zone.Market] = new[]
            {
                new[] { ("Stall", -0.08f, 0.1f), ("FarmCrateApple", 0.16f, -0.07f), ("Barrel", -0.32f, -0.03f), ("Grass", 0.26f, -0.13f) },        // 노점 수레
                new[] { ("Crate", -0.14f, 0.08f), ("FarmCrate", 0.08f, 0.07f), ("BarrelApples", -0.02f, -0.1f), ("Bag", -0.27f, -0.08f) },       // 상자 더미
                new[] { ("Barrel", -0.08f, 0.09f), ("BarrelApples", 0.12f, 0.03f), ("Barrel", -0.26f, -0.02f), ("Bucket", 0.04f, -0.12f) },      // 술통 모퉁이
            },
            [Zone.Residential] = new[]
            {
                new[] { ("Bench", -0.06f, 0.08f), ("BushFlowers", -0.36f, 0.06f), ("Grass", -0.14f, -0.11f), ("Bucket", 0.16f, -0.08f) },        // 쉼터 벤치
                new[] { ("Barrel", -0.2f, 0.08f), ("Crate", 0.02f, 0.08f), ("Bucket", -0.06f, -0.07f), ("Bag", 0.16f, -0.06f), ("Fern", -0.34f, -0.05f) }, // 작업 모퉁이
                new[] { ("BushFlowers", -0.1f, 0.1f), ("Bush", -0.34f, 0.05f), ("Plant", 0.12f, -0.04f), ("Grass", -0.06f, -0.12f) },           // 작은 정원
            },
            [Zone.Outskirts] = new[]
            {
                new[] { ("Rock2", -0.1f, 0.08f), ("Rock3", 0.12f, 0f), ("Fern", -0.3f, -0.04f), ("Grass", 0.02f, -0.11f) },                      // 길가 바위
                new[] { ("Bush", -0.12f, 0.1f), ("Rock1", 0.1f, -0.02f), ("GrassWispy", -0.32f, -0.06f) },                                     // 덤불 길목
            },
        };
        private static int LampSpacingOf(Zone z) => z == Zone.Market ? 3 : z == Zone.Residential ? 4 : 6;

        // A. 건물 밑동 소품 (카메라 쪽 벽 밑) — 구역마다
        private static readonly Dictionary<Zone, string[]> Foundation = new()
        {
            [Zone.Market] = new[] { "Crate", "Barrel", "FarmCrate", "Bag", "Grass" },
            [Zone.Residential] = new[] { "BushFlowers", "Bucket", "Grass", "Bush", "Barrel" },
            [Zone.Outskirts] = new[] { "Bush", "Grass", "Fern", "Rock3", "GrassWispy" },
        };

        // 빈 칸 자연물 (뒤쪽 모서리) — 시장 거리는 깨끗하게, 외곽일수록 많이
        private static float ClusterChanceOf(Zone z) => z == Zone.Market ? 0.05f : z == Zone.Residential ? 0.12f : 0.3f;
        private static readonly string[][] Clusters =
        {
            new[] { "Rock1", "Grass" }, new[] { "Rock3", "Pebble" }, new[] { "Bush" }, new[] { "GrassWispy", "Fern" }, new[] { "Plant", "Rock2" },
        };

        public static int DevLampScenes { get; private set; }
        public static int DevFoundations { get; private set; }

        private readonly Dictionary<string, GameObject> _decoPrefabs = new();
        private List<HexCoord> _hubs;

        private GameObject DecoPrefab(string key)
        {
            if (!_decoPrefabs.TryGetValue(key, out var p)) _decoPrefabs[key] = p = Resources.Load<GameObject>(Deco[key].Path);
            return p;
        }

        // 11/13 팀장 6차: 전투 맵도 싸운 자리 구역에 맞춤 (0 시장 · 1 주택가 · 2 외곽, 지도 없으면 -1)
        public int BattleThemeAt(HexCoord c) => Map == null || _hubs == null || _hubs.Count == 0 ? -1 : (int)ZoneOf(c);

        private Zone ZoneOf(HexCoord c)
        {
            int d = int.MaxValue;
            foreach (var h in _hubs) d = Mathf.Min(d, h.DistanceTo(c));
            return d <= 3 ? Zone.Market : d <= 7 ? Zone.Residential : Zone.Outskirts;
        }

        private void PlaceDeco(string key, Transform root, Vector3 local, uint h)
        {
            var p = DecoPrefab(key);
            if (p == null) return;
            var kind = Deco[key];
            float yaw = kind.Flat ? (((h >> 1) & 1) == 0 ? 0f : 180f) + ((h >> 5) % 25) - 12f : (h >> 3) % 360; // 납작한 것은 정면/뒷면만 ±12°
            Place(p, kind, root, local, yaw, faceCamera: kind.Flat);
        }

        private void BuildScatter()
        {
            if (Map == null) return;
            var root = new GameObject("Scatter").transform;
            root.SetParent(transform, false);
            _hubs = new List<HexCoord>();
            foreach (var t in Map.Tiles.Values) if (t.Kind == MapTileKind.Hub) _hubs.Add(t.Coord);
            if (_hubs.Count == 0) _hubs.Add(new HexCoord(0, 0));

            BuildStoneWalls(root); // 11/14 팀장: 돌담을 길 쪽 모서리를 따라 이어서
            var lampTiles = BuildLampScenes(root);
            BuildFoundations(root, lampTiles);

            foreach (var mapTile in Map.Tiles.Values)
            {
                if (mapTile.Kind != MapTileKind.Walkable || _art.PropFor(mapTile) != null) continue;
                uint h = Hash(mapTile.Row, mapTile.Col, 17);
                if ((h % 1000) / 1000f >= ClusterChanceOf(ZoneOf(mapTile.Coord))) continue;
                // 뒤쪽(카메라 반대, +Z) 모서리 셋 중 하나 — 칸 위의 말·앞 칸을 가리지 않게
                float[] backAngles = { 30f, 90f, 150f };
                float ang = backAngles[(h >> 20) % 3] * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var basePos = LocalPosition(mapTile.Coord) + dir * (tileSize * 0.62f) + Vector3.up * tileHeight;
                var c = Clusters[(h >> 10) % (uint)Clusters.Length];
                for (int i = 0; i < c.Length; i++)
                {
                    // 큰 것(첫째) 뒤, 작은 것 앞·옆 (C)
                    var off = c.Length > 1 ? new Vector3(i == 0 ? -0.07f : 0.08f, 0f, i == 0 ? 0.04f : -0.05f) * tileSize : Vector3.zero;
                    PlaceDeco(c[i], root, basePos + off, h >> (i * 3));
                }
            }
        }

        private HashSet<HexCoord> BuildLampScenes(Transform root)
        {
            var picks = new List<(HexCoord c, Zone z)>();
            var used = new HashSet<HexCoord>();
            if (DecoPrefab("Lamp") == null) return used;
            var cands = new List<(uint h, MapTile t, Vector3 outDir)>();
            foreach (var mapTile in Map.Tiles.Values)
            {
                if (mapTile.Kind != MapTileKind.Blocked) continue;
                var center = LocalPosition(mapTile.Coord);
                Vector3? best = null; float bestZ = float.MaxValue;
                for (int d = 0; d < 6; d++)
                {
                    if (!Map.Tiles.TryGetValue(mapTile.Coord.Neighbor(d), out var nb) || nb.Kind != MapTileKind.Walkable) continue;
                    var v = (LocalPosition(nb.Coord) - center).normalized;
                    if (v.z < bestZ) { bestZ = v.z; best = v; }
                }
                // 길이 카메라 쪽(−Z)이나 옆에 있을 때만 — 뒤쪽 길가 장면은 집에 가려 안 보임
                if (best != null && best.Value.z <= 0.1f) cands.Add((Hash(mapTile.Row, mapTile.Col, 53), mapTile, best.Value));
            }
            cands.Sort((a, b) => a.h.CompareTo(b.h)); // 좌표로 고정 무작위 순서
            foreach (var (h, t, outDir) in cands)
            {
                var zone = ZoneOf(t.Coord);
                bool far = true;
                foreach (var p in picks) if (p.c.DistanceTo(t.Coord) < Mathf.Max(LampSpacingOf(zone), LampSpacingOf(p.z))) { far = false; break; }
                if (!far) continue;
                picks.Add((t.Coord, zone)); used.Add(t.Coord);

                var anchor = LocalPosition(t.Coord) + outDir * (tileSize * 0.58f) + Vector3.up * tileHeight;
                float s = ((h >> 2) & 1) == 0 ? 1f : -1f; // 장면 좌우 뒤집기
                Vector3 At(float x, float z) => anchor + new Vector3(x * s, 0f, z) * tileSize;
                float faceOut = -Mathf.Atan2(outDir.z, outDir.x) * Mathf.Rad2Deg + 90f; // 등 머리가 길 쪽
                Place(DecoPrefab("Lamp"), Deco["Lamp"], root, At(0.34f, 0.06f), faceOut);
                var pool = Scenes[zone];
                var scene = pool[(h >> 6) % (uint)pool.Length];
                for (int i = 0; i < scene.Length; i++) PlaceDeco(scene[i].k, root, At(scene[i].x, scene[i].z), h >> (i * 2 + 1));
            }
            DevLampScenes = picks.Count;
            return used;
        }

        // A. 집이 선 막힌 칸: 카메라 쪽 벽 밑에 낮은 소품 2~3개 (벽에 붙여 땅에 자리 잡은 느낌)
        private void BuildFoundations(Transform root, HashSet<HexCoord> skip)
        {
            int count = 0;
            foreach (var mapTile in Map.Tiles.Values)
            {
                if (mapTile.Kind != MapTileKind.Blocked || skip.Contains(mapTile.Coord) || !TryGetTile(mapTile.Coord, out var tile) || !tile.PropVisible) continue;
                uint h = Hash(mapTile.Row, mapTile.Col, 29);
                if ((h % 100) >= 65) continue; // 모든 집에 붙이면 반복이 보임
                var wb = tile.PropBounds();
                if (wb.size.y < tileSize * 0.5f * transform.lossyScale.y) continue; // 낮은 돌담·나무 덤불은 건너뜀
                var center = LocalPosition(mapTile.Coord);
                var bmin = transform.InverseTransformPoint(wb.min); var bmax = transform.InverseTransformPoint(wb.max);
                float frontZ = Mathf.Max(bmin.z, center.z - tileSize * 0.8f) - tileSize * 0.03f; // 벽 바로 앞
                float x0 = Mathf.Max(bmin.x, center.x - tileSize * 0.6f) + tileSize * 0.08f, x1 = Mathf.Min(bmax.x, center.x + tileSize * 0.6f) - tileSize * 0.08f;
                // 나무 칸 앞에 상자·통은 어색 (QA) → 나무는 자연물만
                var src = HidesTileBehind(mapTile.Coord) ? FrontRowProp(mapTile) ?? _art.PropFor(mapTile) : _art.PropFor(mapTile); // 실제로 세운 구조물 (앞줄은 낮은 나무·돌담)
                bool trees = src == _art.BlockTrees || src == _art.LowTrees;
                var list = Foundation[trees ? Zone.Outskirts : ZoneOf(mapTile.Coord)];
                int n = 2 + (int)((h >> 8) % 2);
                for (int i = 0; i < n; i++)
                {
                    float t = (i + 0.15f * (((h >> (10 + i)) & 1) == 0 ? 1 : -1)) / (n - 1);
                    var pos = new Vector3(Mathf.Lerp(x0, x1, Mathf.Clamp01(t)), center.y + tileHeight, frontZ - tileSize * 0.04f * (i % 2));
                    // 칸 밖으로 나가지 않게 (가운데에서 0.82 안)
                    var flat = new Vector3(pos.x - center.x, 0f, pos.z - center.z);
                    if (flat.magnitude > tileSize * 0.82f) pos = center + flat.normalized * (tileSize * 0.82f) + Vector3.up * tileHeight;
                    PlaceDeco(list[(h >> (12 + i * 3)) % (uint)list.Length], root, pos, h >> (i * 5 + 2));
                }
                count++;
            }
            DevFoundations = count;
        }

        private void Place(GameObject prefab, ScatterKind kind, Transform root, Vector3 local, float yaw, bool faceCamera = false)
        {
            var go = Instantiate(prefab, root);
            go.name = "Scatter_" + kind.Path.Substring(kind.Path.LastIndexOf('/') + 1);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c); // 칸 클릭을 막지 않게
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is Fantasia.Combat.StreetLampGlow)) Destroy(mb); // 가로등 불빛만 남김
            go.transform.localScale = Vector3.one;
            go.transform.localPosition = local;
            if (faceCamera)
            {
                // 모델마다 긴 축이 달라서 0°에서 재 보고, 긴 쪽이 Z면 90° 돌려 긴 면이 화면 가로가 되게
                go.transform.localRotation = Quaternion.identity;
                var b0 = Bounds(go);
                if (b0.size.z > b0.size.x * 1.1f) yaw += 90f;
            }
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            FitSize(go, kind);
            // 바닥에 발을 맞춤 (모델 기준점이 가운데인 것도 있음)
            var b = Bounds(go);
            float groundY = transform.TransformPoint(local).y;
            go.transform.position += Vector3.up * (groundY - b.min.y);
            // 가로등 바닥 빛 웅덩이: 전투 바닥 높이(0.03)에 놓이므로 보드 바닥 위로
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == "LampPool") t.position = new Vector3(t.position.x, groundY + 0.02f, t.position.z);
        }

        // 11/13 팀장: 앞줄 막힌 칸의 돌담 방향 — 모두 같은 방향(가로)이라 어색 → 옆 막힌 칸과 이어지게 담 길이 방향을 맞춤.
        // 이어질 막힌 이웃이 없으면 카메라 쪽으로 가로 (정면이 보이게). 돌담 길이 방향 = 모델 X축.
        private void OrientWall(HexTile tile, HexCoord coord)
        {
            var prop = tile.transform.Find("Prop");
            if (prop == null) return;
            var center = LocalPosition(coord);
            float best = float.MaxValue; Vector3? along = null;
            for (int d = 0; d < 6; d++)
            {
                if (!Map.Tiles.TryGetValue(coord.Neighbor(d), out var nb) || nb.Kind != MapTileKind.Blocked) continue;
                var v = (LocalPosition(nb.Coord) - center).normalized;
                float score = Mathf.Abs(v.z); // 가로(카메라에 정면)에 가까운 이웃 먼저
                if (score < best) { best = score; along = v; }
            }
            if (along == null) return; // 기본 (가로) 유지
            float yaw = -Mathf.Atan2(along.Value.z, along.Value.x) * Mathf.Rad2Deg; // X축을 이웃 쪽으로
            prop.localRotation = Quaternion.Euler(0f, yaw + 180f, 0f);
        }

        private void FitSize(GameObject go, ScatterKind kind)
        {
            var b = Bounds(go);
            float cur = kind.Height ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            if (cur < 0.0001f) return;
            float want = tileSize * kind.Size * transform.lossyScale.x;
            go.transform.localScale = Vector3.one * (want / cur);
        }

        private static Bounds Bounds(GameObject go)
        {
            bool any = false; var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private static uint Hash(int a, int b, int salt)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }
    }
}
