using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fantasia.Board
{
    // 11/15 통합 플레이 테스트: 맵 구조물 점검 (자동 플레이 전용, 게임 중에는 부르지 않음).
    // (1) 길 칸 한가운데를 막는 장식 (말이 서는 자리)  (2) 큰 구조물끼리 크게 겹침 (뚫고 나옴)  (3) 칸 밖 허공에 뜬 장식
    public partial class HexBoard
    {
        public List<string> DevStructureAudit()
        {
            var found = new List<string>();
            var scatter = transform.Find("Scatter");
            if (scatter == null) return found;
            var items = new List<(string name, Bounds b)>();
            foreach (Transform c in scatter)
            {
                if (!c.gameObject.activeInHierarchy) continue;
                var rs = c.GetComponentsInChildren<Renderer>(false).Where(r => !(r is ParticleSystemRenderer) && !r.name.Contains("Halo")).ToArray();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                // 11/15: 가로등은 모델 하나에 팔·등이 붙어 바닥 범위가 1칸 넘게 잡힘 → 기둥(기준점) 둘레만 바닥으로 봄
                if (c.name.Contains("Lamp")) { var foot = new Bounds(new Vector3(c.position.x, b.center.y, c.position.z), new Vector3(tileSize * 0.16f, b.size.y, tileSize * 0.16f)); b = foot; }
                items.Add((c.name, b));
            }
            float s = tileSize;
            var walk = _tiles.Values.Where(t => !t.IsBlocked).ToList();
            var tallOnes = items.Where(i => !i.name.Contains("Grass") && i.b.size.y > s * 0.6f && (Mathf.Min(i.b.size.x, i.b.size.z) > s * 0.25f || i.name.Contains("Lamp"))).ToList();
            // (1) 길 칸 가운데 (반지름 0.28칸)를 덮는 키 큰 장식
            foreach (var t in walk)
            {
                var p = t.transform.position;
                foreach (var it in tallOnes)
                {
                    var cl = it.b.ClosestPoint(new Vector3(p.x, it.b.center.y, p.z));
                    float dd = new Vector2(cl.x - p.x, cl.z - p.z).magnitude; if (dd < s * 0.28f) { found.Add($"길 막음 · {it.name} → 칸 {t.Coordinate} (가운데에서 {dd / s:0.00}칸, 크기 {it.b.size.x:0.00}×{it.b.size.z:0.00}, 위치 ({it.b.center.x:0.0},{it.b.center.z:0.0}))"); break; }
                }
            }
            // (2) 큰 구조물끼리 바닥 면적이 작은 쪽의 45% 넘게 겹침
            for (int i = 0; i < tallOnes.Count; i++)
                for (int j = i + 1; j < tallOnes.Count; j++)
                {
                    Bounds a = tallOnes[i].b, b = tallOnes[j].b;
                    float ox = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x), oz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
                    if (ox <= 0 || oz <= 0) continue;
                    float frac = ox * oz / Mathf.Min(a.size.x * a.size.z, b.size.x * b.size.z);
                    if (frac > 0.45f) found.Add($"겹침 · {tallOnes[i].name} ↔ {tallOnes[j].name} ({frac * 100f:0}%) @({a.center.x:0.0},{a.center.z:0.0})");
                }
            // (3) 어느 칸 위도 아닌 곳 (가장 가까운 칸 가운데에서 1.2칸 넘게)
            var all = _tiles.Values.Select(t => t.transform.position).ToList();
            foreach (var it in items)
            {
                float d = all.Min(p => new Vector2(p.x - it.b.center.x, p.z - it.b.center.z).magnitude);
                if (d > s * 1.2f) found.Add($"칸 밖 · {it.name} (가까운 칸에서 {d / s:0.0}칸)");
            }
            return found;
        }
    }
}
