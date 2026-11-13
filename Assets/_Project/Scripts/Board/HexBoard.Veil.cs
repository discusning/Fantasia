using System.Collections.Generic;
using Fantasia.World;
using UnityEngine;

namespace Fantasia.Board
{
    // 11/14 팀장 결정 (Docs/Planning/보드_가장자리_가리기_연구_1114.md 1안 "런던 안개"):
    // 아직 안 열린 지역(베르사유·로마 제국)과 지도 바깥을 런던 안개로 가림.
    //  - 보이는 칸 = 열린 지역 칸 + 그 바로 둘레 한 칸(장애물·출구). 나머지 칸·바닥·장식은 숨김 (규칙·데이터는 그대로)
    //  - 숨긴 자리 위에 천천히 흐르는 안개 층 3장 — 보이는 칸 경계에서 점점 짙어짐
    //  - 판 바깥 배경색·거리 안개는 BoardSkyTint가 같은 안개 색으로 맞춤, 카메라는 보이는 범위 안으로 (BoardCameraRig)
    public partial class HexBoard
    {
        private HashSet<HexCoord> _veilHidden;
        public Bounds VisibleBounds { get; private set; }
        public bool HasVeil => _veilHidden != null && _veilHidden.Count > 0;
        public static int DevHiddenTiles { get; private set; }
        public bool IsVeiled(HexCoord c) => _veilHidden != null && _veilHidden.Contains(c);

        private void ComputeVeil()
        {
            var open = new HashSet<int> { 0 }; // 런던은 늘 열림
            foreach (var t in Map.Tiles.Values)
            {
                if (t.Kind != MapTileKind.Exit || !IsExitOpen(t.Coord)) continue;
                for (int d = 0; d < 6; d++) { int r = Map.RegionOf(t.Coord.Neighbor(d)); if (r >= 0) open.Add(r); }
            }
            var visible = new HashSet<HexCoord>();
            foreach (var t in Map.Tiles.Values)
            {
                if (!open.Contains(Map.RegionOf(t.Coord))) continue;
                visible.Add(t.Coord);
                for (int d = 0; d < 6; d++)
                {
                    var n = t.Coord.Neighbor(d);
                    if (Map.Tiles.ContainsKey(n) && Map.RegionOf(n) < 0) visible.Add(n); // 둘레 장애물·출구
                }
            }
            // 11/14 QA "칸이 뚫려 보임": 런던 안쪽에 막힌 칸이 2겹 이상 뭉친 곳은 가운데 칸이 런던 칸과 안 맞닿아 숨겨졌음
            // → 지역 없는 칸(막힌 칸·출구)은 가장 가까운 지역을 따라감 (여러 출발점 너비 우선 탐색). 그 지역이 열려 있으면 보임
            var nearest = new Dictionary<HexCoord, int>();
            var q = new Queue<HexCoord>();
            foreach (var t in Map.Tiles.Values) { int r = Map.RegionOf(t.Coord); if (r >= 0) { nearest[t.Coord] = r; q.Enqueue(t.Coord); } }
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int d = 0; d < 6; d++)
                {
                    var n = c.Neighbor(d);
                    if (!Map.Tiles.ContainsKey(n) || nearest.ContainsKey(n)) continue;
                    nearest[n] = nearest[c]; q.Enqueue(n);
                }
            }
            foreach (var kv in nearest) if (open.Contains(kv.Value)) visible.Add(kv.Key);
            _veilHidden = new HashSet<HexCoord>();
            foreach (var c in Map.Tiles.Keys) if (!visible.Contains(c)) _veilHidden.Add(c);
            DevHiddenTiles = _veilHidden.Count;

            bool any = false; var b = new Bounds();
            foreach (var c in visible)
            {
                var p = transform.TransformPoint(LocalPosition(c));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
            VisibleBounds = b;
        }

        // 바닥 한 장에서 숨긴 칸 자리는 뺌 (BuildGround 전에)
        private bool GroundCellVisible(Vector3 localPos) => _veilHidden == null || !_veilHidden.Contains(LocalToCoord(localPos));

        private void ApplyVeil()
        {
            if (!HasVeil) return;
            foreach (var c in _veilHidden)
                if (_tiles.TryGetValue(c, out var tile) && tile != null) tile.gameObject.SetActive(false);
            var scatter = transform.Find("Scatter");
            if (scatter != null)
                foreach (Transform child in scatter)
                    if (_veilHidden.Contains(LocalToCoord(transform.InverseTransformPoint(child.position)))) child.gameObject.SetActive(false);
            BuildFogLayers();
            if (scatter != null) BuildFogEdgeLamps(scatter);
        }

        // ── 11/14 팀장 3차: 1700년대 런던 + 곳곳의 가로등과 이어지게 — 안개 경계 길가에 가로등, 안개 속에 따뜻한 불빛 번짐 ──
        // (가로등 불 자체는 StreetLampGlow가 시간대마다 켬. 번짐은 안개에 비친 빛이라 낮에도 아주 옅게 보임)
        private const int EdgeLampSpacing = 4;
        private readonly List<Material> _haloMats = new();
        public static int DevEdgeLamps { get; private set; }
        public static readonly List<Renderer> DevHalos = new();

        private void BuildFogEdgeLamps(Transform root)
        {
            var lamp = DecoPrefab("Lamp");
            var haloSrc = Resources.Load<Material>("BoardArt/Mat_FogLampHalo");
            DevHalos.Clear();
            if (lamp == null) return;
            var picks = new List<HexCoord>();
            var cands = new List<(uint h, HexCoord ring, Vector3 inDir)>();
            foreach (var t in Map.Tiles.Values)
            {
                if (_veilHidden.Contains(t.Coord) || Map.RegionOf(t.Coord) >= 0) continue; // 보이는 둘레 칸(장애물·출구)만
                bool border = false; Vector3? toRoad = null; float best = float.MaxValue;
                var center = LocalPosition(t.Coord);
                for (int d = 0; d < 6; d++)
                {
                    var n = t.Coord.Neighbor(d);
                    if (_veilHidden.Contains(n)) { border = true; continue; }
                    if (!Map.Tiles.TryGetValue(n, out var nt)) { if (OpensToOutside(t.Coord, d)) border = true; continue; } // 런던 안쪽 빈 구멍은 경계가 아님
                    if (nt.Kind != MapTileKind.Blocked && Map.RegionOf(n) >= 0)
                    {
                        var v = (LocalPosition(n) - center).normalized;
                        if (v.z < best) { best = v.z; toRoad = v; } // 카메라 쪽 길을 우선 (가로등이 집에 안 가림)
                    }
                }
                if (border && toRoad != null) cands.Add((Hash(t.Row, t.Col, 71), t.Coord, toRoad.Value));
            }
            cands.Sort((a, b) => a.h.CompareTo(b.h));
            foreach (var (h, ring, inDir) in cands)
            {
                bool far = true;
                foreach (var p in picks) if (p.DistanceTo(ring) < EdgeLampSpacing) { far = false; break; }
                if (!far) continue;
                picks.Add(ring);
                var basePos = LocalPosition(ring) + Vector3.up * tileHeight;
                var lampPos = basePos + inDir * (tileSize * 0.62f); // 길과 맞닿은 칸 끝
                float yaw = -Mathf.Atan2(inDir.z, inDir.x) * Mathf.Rad2Deg + 90f;
                Place(lamp, Deco["Lamp"], root, lampPos, yaw);
                if (haloSrc != null)
                {
                    // 번짐: 가로등 머리 높이, 안개 쪽으로 조금 — 카메라를 보는 큰 판
                    var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(halo.GetComponent<Collider>());
                    halo.name = "FogLampHalo";
                    halo.transform.SetParent(root, false);
                    halo.transform.localPosition = lampPos - inDir * (tileSize * 0.35f) + Vector3.up * (tileSize * 1.25f);
                    halo.transform.localScale = Vector3.one * (tileSize * 4.2f);
                    var m = new Material(haloSrc);
                    var r = halo.GetComponent<MeshRenderer>(); r.sharedMaterial = m;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                    halo.AddComponent<FaceCamera>();
                    _haloMats.Add(m);
                    DevHalos.Add(r);
                }
            }
            DevEdgeLamps = picks.Count;
        }

        // 그 방향으로 4칸 가는 동안 칸이 하나도 없으면 지도 바깥
        private bool OpensToOutside(HexCoord from, int d)
        {
            var c = from;
            for (int i = 0; i < 4; i++) { c = c.Neighbor(d); if (Map.Tiles.ContainsKey(c) && !_veilHidden.Contains(c)) return false; }
            return true;
        }

        private class FaceCamera : MonoBehaviour
        {
            private void LateUpdate() { var c = Camera.main; if (c != null) transform.rotation = c.transform.rotation; }
        }

        private HexCoord LocalToCoord(Vector3 p)
        {
            float r = -p.z / (1.5f * tileSize);
            float q = p.x / (Mathf.Sqrt(3f) * tileSize) - r / 2f;
            float s = -q - r;
            int rq = Mathf.RoundToInt(q), rr = Mathf.RoundToInt(r), rs = Mathf.RoundToInt(s);
            float dq = Mathf.Abs(rq - q), dr = Mathf.Abs(rr - r), ds = Mathf.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs; else if (dr > ds) rr = -rq - rs;
            return new HexCoord(rq, rr);
        }

        // ── 안개 층 ──
        private static readonly (float height, float alpha, float uvScale, Vector2 drift)[] FogLayers =
        {
            (0.35f, 0.95f, 1f / 10f, new Vector2(0.006f, 0.002f)), // 11/14 팀장 2차: 크게 뭉친 구름
            (1.1f, 0.75f, 1f / 14f, new Vector2(-0.004f, 0.005f)),
            (2.2f, 0.55f, 1f / 18f, new Vector2(0.003f, -0.004f)),
        };
        private readonly List<(Material mat, Vector2 drift)> _fogMats = new();
        public static int DevFogLayers { get; private set; }

        private void BuildFogLayers()
        {
            var src = Resources.Load<Material>("BoardArt/Mat_FogVeil");
            if (src == null) { Debug.LogWarning("[Veil] BoardArt/Mat_FogVeil 없음"); return; }
            // 범위: 지도 전체 + 둘레 여유 (카메라를 멀리 빼도 판 끝이 안 보이게)
            var all = new Bounds(); bool any = false;
            foreach (var c in Map.Tiles.Keys) { var p = LocalPosition(c); if (!any) { all = new Bounds(p, Vector3.zero); any = true; } else all.Encapsulate(p); }
            all.Expand(new Vector3(tileSize * 24f, 0f, tileSize * 24f));
            var visibleCenters = new List<Vector2>();
            // 기준 = 열린 지역의 밟는 칸 (둘레 장애물 한 칸은 반쯤 안개에 잠겨 판 끝이 칼같이 잘려 보이지 않게)
            foreach (var c in Map.Tiles.Keys) if (!_veilHidden.Contains(c) && Map.RegionOf(c) >= 0) { var p = LocalPosition(c); visibleCenters.Add(new Vector2(p.x, p.z)); }

            float step = tileSize * 0.6f;
            int nx = Mathf.CeilToInt(all.size.x / step) + 1, nz = Mathf.CeilToInt(all.size.z / step) + 1;
            var alpha = new float[nx * nz];
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    var v = new Vector2(all.min.x + ix * step, all.min.z + iz * step);
                    float best = float.MaxValue;
                    foreach (var c in visibleCenters) { float d = (c - v).sqrMagnitude; if (d < best) best = d; }
                    float dist = Mathf.Sqrt(best) / tileSize; // 가장 가까운 보이는 칸 중심까지 (칸 수)
                    alpha[iz * nx + ix] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 2.5f, dist));
                }
            // 11/14 팀장 2차 QA: 런던 안쪽 빈 칸(구멍)에도 구름이 끼어 판 한가운데가 지저분함
            // → 지도 바깥과 이어진 빈 곳에만 구름. 보이는 칸(둘레 포함) 위가 아닌 점들을 바깥 테두리에서부터 채워 나감
            var anyVisible = new List<Vector2>();
            foreach (var c in Map.Tiles.Keys) if (!_veilHidden.Contains(c)) { var p = LocalPosition(c); anyVisible.Add(new Vector2(p.x, p.z)); }
            var open = new bool[nx * nz];
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    var v = new Vector2(all.min.x + ix * step, all.min.z + iz * step);
                    float best = float.MaxValue;
                    foreach (var c in anyVisible) { float d = (c - v).sqrMagnitude; if (d < best) best = d; }
                    open[iz * nx + ix] = Mathf.Sqrt(best) > tileSize * 0.95f; // 보이는 칸 밖
                }
            var outside = new bool[nx * nz];
            var queue = new Queue<int>();
            for (int i = 0; i < nx * nz; i++)
            {
                int ix = i % nx, iz = i / nx;
                if ((ix == 0 || iz == 0 || ix == nx - 1 || iz == nz - 1) && open[i]) { outside[i] = true; queue.Enqueue(i); }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(); int ix = i % nx, iz = i / nx;
                void Try(int x, int z) { if (x < 0 || z < 0 || x >= nx || z >= nz) return; int j = z * nx + x; if (outside[j] || !open[j]) return; outside[j] = true; queue.Enqueue(j); }
                Try(ix + 1, iz); Try(ix - 1, iz); Try(ix, iz + 1); Try(ix, iz - 1);
            }
            // 바깥과 이어진 빈 곳 + 그 바로 옆(판 끝이 반쯤 잠기는 둘레)만 구름
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    int i = iz * nx + ix;
                    if (outside[i]) continue;
                    bool nearOutside = false;
                    for (int dz = -3; dz <= 3 && !nearOutside; dz++)
                        for (int dx = -3; dx <= 3 && !nearOutside; dx++)
                        {
                            int x = ix + dx, z = iz + dz;
                            if (x >= 0 && z >= 0 && x < nx && z < nz && outside[z * nx + x]) nearOutside = true;
                        }
                    if (!nearOutside) alpha[i] = 0f;
                }

            var root = new GameObject("FogVeil").transform;
            root.SetParent(transform, false);
            foreach (var layer in FogLayers)
            {
                var verts = new Vector3[nx * nz]; var cols = new Color[nx * nz]; var uvs = new Vector2[nx * nz];
                for (int iz = 0; iz < nz; iz++)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        int i = iz * nx + ix;
                        var p = new Vector3(all.min.x + ix * step, tileHeight + layer.height * tileSize, all.min.z + iz * step);
                        verts[i] = p; uvs[i] = new Vector2(p.x, p.z) * layer.uvScale / tileSize;
                        cols[i] = new Color(1f, 1f, 1f, alpha[i] * layer.alpha);
                    }
                var tris = new List<int>((nx - 1) * (nz - 1) * 6);
                for (int iz = 0; iz < nz - 1; iz++)
                    for (int ix = 0; ix < nx - 1; ix++)
                    {
                        int a = iz * nx + ix, b2 = a + 1, c2 = a + nx, d2 = c2 + 1;
                        if (alpha[a] + alpha[b2] + alpha[c2] + alpha[d2] < 0.01f) continue; // 보이는 칸 위는 비움
                        tris.Add(a); tris.Add(c2); tris.Add(b2); tris.Add(b2); tris.Add(c2); tris.Add(d2);
                    }
                var mesh = new Mesh { name = "FogVeil", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.vertices = verts; mesh.colors = cols; mesh.uv = uvs; mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
                var go = new GameObject("FogLayer");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                var m = new Material(src);
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                _fogMats.Add((m, layer.drift));
            }
            DevFogLayers = _fogMats.Count;
        }

        private void Update()
        {
            if (_fogMats.Count == 0) return;
            var col = BoardSkyTint.VeilColor * 0.5f * 1.15f; // 이 셰이더는 색을 2배로 그림. 배경보다 조금 밝게 → 흐르는 안개 덩어리가 보임
            col.a = 1f;
            float t = Time.time;
            foreach (var (m, drift) in _fogMats)
            {
                if (m == null) continue;
                m.SetColor(TintId, col);
                m.mainTextureOffset = drift * t;
            }
            // 가로등 불빛이 안개에 비친 번짐: 저녁·새벽 진하게, 낮에도 옅게 (조금씩 일렁임)
            float lamp = 0.15f + 0.85f * Fantasia.Combat.StreetLampGlow.CurrentLevel;
            for (int i = 0; i < _haloMats.Count; i++)
            {
                var hm = _haloMats[i]; if (hm == null) continue;
                float flick = 1f + 0.06f * Mathf.Sin(t * 2.3f + i * 1.7f);
                hm.SetColor(TintId, new Color(1f, 0.7f, 0.38f, 0.5f) * (0.75f * lamp * flick));
            }
        }
        private static readonly int TintId = Shader.PropertyToID("_TintColor");
    }
}
