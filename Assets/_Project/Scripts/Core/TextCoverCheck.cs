using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.Core
{
    // 11/11 개발용 점검: 화면 글자가 (1) 칸이 좁아 잘렸는지 (2) 뒤에 그려진 그림·글자에 덮였는지 (3) 화면 밖으로 나갔는지 찾는다.
    // 자동 플레이가 사진 찍을 때마다 부름 → 로그에 "[글자]"로 남김. 같은 글자는 한 번만.
    // 큰 창이 HUD를 덮는 건 정상이라, 같은 캔버스(화면 묶음) 안에서만 덮임을 본다.
    public static class TextCoverCheck
    {
        private static readonly HashSet<string> Seen = new HashSet<string>();

        public static List<string> Run()
        {
            var found = new List<string>();
            var texts = Object.FindObjectsByType<Text>(FindObjectsSortMode.None);
            var graphics = Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None);



            var covers = new List<(Graphic g, Rect r, int sort, Canvas root)>();
            foreach (var g in graphics)
            {
                if (!Visible(g, 0.6f) || g.canvas == null) continue;
                if (g is Image img && img.sprite == null && img.type != Image.Type.Simple) continue;
                covers.Add((g, g is Text gt ? GlyphRect(gt) : WorldRect(g.rectTransform), SortOf(g.canvas), g.canvas.rootCanvas)); // 글자는 실제 찍힌 범위로
            }

            foreach (var t in texts)
            {
                if (!Visible(t, 0.3f) || t.canvas == null || string.IsNullOrWhiteSpace(t.text)) continue;
                var glyph = GlyphRect(t);
                if (glyph.width < 2f || glyph.height < 2f) continue;
                string who = $"{Path(t.transform)} \"{Snip(t.text)}\"";

                // (1) 잘림: 세로로 넘치는데 잘라내기 모드
                if (!t.resizeTextForBestFit && t.verticalOverflow == VerticalWrapMode.Truncate
                    && t.preferredHeight > t.rectTransform.rect.height + 2f)
                    Add(found, $"잘림 · {who} (필요 {t.preferredHeight:0} > 칸 {t.rectTransform.rect.height:0})");

                // (3) 화면 밖
                if (glyph.xMin < -1f || glyph.yMin < -1f || glyph.xMax > Screen.width + 1f || glyph.yMax > Screen.height + 1f)
                    Add(found, $"화면 밖 · {who}");

                // (2) 덮임
                int sortT = SortOf(t.canvas);
                var rootT = t.canvas.rootCanvas;
                foreach (var c in covers)
                {
                    if (c.g == t || c.g.transform.IsChildOf(t.transform)) continue;
                    bool later;
                    if (c.root == rootT && c.sort == sortT) later = c.g.depth > t.depth;
                    else later = c.sort > sortT;
                    if (!later) continue;
                    if (c.root != rootT) continue; // 다른 창·HUD끼리 덮는 건 층 규칙(창이 위)이라 정상 — 같은 화면 묶음 안만 봄
                    if (!c.r.Overlaps(glyph)) continue;
                    float ox = Mathf.Min(c.r.xMax, glyph.xMax) - Mathf.Max(c.r.xMin, glyph.xMin);
                    float oy = Mathf.Min(c.r.yMax, glyph.yMax) - Mathf.Max(c.r.yMin, glyph.yMin);
                    float frac = ox * oy / (glyph.width * glyph.height);
                    if (frac < 0.2f) continue;
                    Add(found, $"덮임 · {who} ← {Path(c.g.transform)} ({frac * 100f:0}%)");
                }
            }
            return found;
        }

        private static void Add(List<string> list, string line) { if (Seen.Add(line)) list.Add(line); }

        private static bool Visible(Graphic g, float minAlpha)
        {
            if (!g.isActiveAndEnabled || g.canvasRenderer.cull) return false;
            return g.color.a * g.canvasRenderer.GetInheritedAlpha() >= minAlpha;
        }

        private static int SortOf(Canvas c)
        {
            while (c != null && !c.isRootCanvas && !c.overrideSorting)
                c = c.transform.parent != null ? c.transform.parent.GetComponentInParent<Canvas>() : null;
            return c != null ? c.sortingOrder : 0;
        }

        private static Rect WorldRect(RectTransform rt)
        {
            var w = new Vector3[4];
            rt.GetWorldCorners(w);
            return Rect.MinMaxRect(w[0].x, w[0].y, w[2].x, w[2].y);
        }

        // 실제로 찍힌 글자 범위 (칸 전체가 아니라)
        private static Rect GlyphRect(Text t)
        {
            var verts = t.cachedTextGenerator.verts;
            if (verts == null || verts.Count == 0) return Rect.zero;
            float unit = 1f / t.pixelsPerUnit;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < verts.Count; i++)
            {
                var p = t.rectTransform.TransformPoint(verts[i].position * unit);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        private static string Snip(string s)
        {
            s = s.Replace("\n", " ");
            return s.Length > 18 ? s.Substring(0, 18) + "…" : s;
        }
    }
}
