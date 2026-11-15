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

                // 11/15 통합 플레이 테스트 추가 점검 (4)~(7)
                var box = WorldRect(t.rectTransform);
                // (4) 가로 넘침: 줄바꿈 없이 칸 밖으로 삐져나감
                if (t.horizontalOverflow == HorizontalWrapMode.Overflow && glyph.width > box.width + 4f)
                    Add(found, $"가로 넘침 · {who} (글자 {glyph.width:0} > 칸 {box.width:0}px)");
                // (5) 너무 작음: 1080 화면 기준 줄 높이 14px 미만 (자동 크기로 줄어든 것 포함)
                var gen = t.cachedTextGenerator;
                if (gen.lineCount > 0)
                {
                    float px = gen.lines[0].height * 1080f / Mathf.Max(1f, Screen.height);
                    if (px < 14f) Add(found, $"작음 · {who} (줄 높이 {px:0}px@1080)");
                    // (6) 한 줄이 너무 김 (읽기 힘듦): 줄바꿈 없는 한 줄 48자 넘음
                    for (int li = 0; li < gen.lineCount; li++)
                    {
                        int start = gen.lines[li].startCharIdx, end = li + 1 < gen.lineCount ? gen.lines[li + 1].startCharIdx : gen.characterCountVisible;
                        if (end - start > 48 && !t.supportRichText) { Add(found, $"긴 줄 · {who} ({end - start}자)"); break; }
                        if (end - start > 60) { Add(found, $"긴 줄 · {who} (약 {end - start}자)"); break; }
                    }
                }
                // (8) 글자 사라짐: 칸이 좁아 일부 글자가 아예 안 그려짐 (자동 크기 포함)
                if (gen.characterCountVisible > 0 || gen.characterCount > 0)
                {
                    int plainLen = System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "").Replace("\n", "").Length;
                    if (t.verticalOverflow == VerticalWrapMode.Truncate && gen.characterCountVisible < plainLen * 0.9f - 1)
                        Add(found, $"글자 사라짐 · {who} ({gen.characterCountVisible}/{plainLen}자만 보임)");
                }
                // (7) 글자 많음: 한 칸에 180자 넘음
                string plain = System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "");
                if (plain.Length > 180) Add(found, $"글자 많음 · {who} ({plain.Length}자)");

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
