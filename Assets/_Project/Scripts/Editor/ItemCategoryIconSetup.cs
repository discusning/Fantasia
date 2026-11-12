using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/12 팀장 지시: 그림이 없는 아이템도 어떤 종류인지 바로 알게 "종류 아이콘"(검·총·지팡이·코트·물약·두루마리 등).
    // 디자인 그림이 오기 전 임시 — 단순한 모양을 코드로 그려 PNG로 저장 (Resources/ItemCategory/Cat_*.png).
    // 디자인이 같은 이름으로 그림을 주면 파일만 바꾸면 됨 (D10). 아이템 자체 그림이 있으면 그 그림이 먼저.
    public static class ItemCategoryIconSetup
    {
        private const string Dir = "Assets/_Project/Resources/ItemCategory";
        private const int Size = 128, SS = 4; // 4배로 그려 줄임 (계단 없앰)

        private static readonly Color Steel = new Color(0.84f, 0.86f, 0.9f), SteelDark = new Color(0.6f, 0.63f, 0.68f);
        private static readonly Color Gold = new Color(0.93f, 0.74f, 0.35f), Wood = new Color(0.58f, 0.38f, 0.21f);
        private static readonly Color Leather = new Color(0.5f, 0.31f, 0.2f), Cloth = new Color(0.36f, 0.33f, 0.42f);
        private static readonly Color Paper = new Color(0.94f, 0.88f, 0.74f), Glass = new Color(0.62f, 0.8f, 0.92f);
        private static readonly Color Potion = new Color(0.86f, 0.25f, 0.25f), Orb = new Color(0.45f, 0.75f, 1f);
        private static readonly Color Gem = new Color(0.85f, 0.2f, 0.3f), Outline = new Color(0.08f, 0.06f, 0.05f, 1f);

        private abstract class Shape
        {
            public Color Fill;
            public abstract float Dist(Vector2 p); // 안쪽이면 음수 (근사)
        }
        private class Circle : Shape { public Vector2 C; public float R; public override float Dist(Vector2 p) => (p - C).magnitude - R; }
        private class Capsule : Shape
        {
            public Vector2 A, B; public float R;
            public override float Dist(Vector2 p)
            {
                var ab = B - A; float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - A, ab) / ab.sqrMagnitude);
                return (p - (A + ab * t)).magnitude - R;
            }
        }
        private class Arc : Shape
        {
            public Vector2 C; public float R, W, A0, A1; // 각도(도)
            public override float Dist(Vector2 p)
            {
                var d = p - C; float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float mid = (A0 + A1) * 0.5f, half = (A1 - A0) * 0.5f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, mid)) <= half) return Mathf.Abs(d.magnitude - R) - W;
                Vector2 e0 = C + new Vector2(Mathf.Cos(A0 * Mathf.Deg2Rad), Mathf.Sin(A0 * Mathf.Deg2Rad)) * R;
                Vector2 e1 = C + new Vector2(Mathf.Cos(A1 * Mathf.Deg2Rad), Mathf.Sin(A1 * Mathf.Deg2Rad)) * R;
                return Mathf.Min((p - e0).magnitude, (p - e1).magnitude) - W;
            }
        }
        private class Poly : Shape
        {
            public Vector2[] P;
            public override float Dist(Vector2 p)
            {
                bool inside = false; float best = float.MaxValue;
                for (int i = 0, j = P.Length - 1; i < P.Length; j = i++)
                {
                    var a = P[j]; var b = P[i];
                    if ((b.y > p.y) != (a.y > p.y) && p.x < (a.x - b.x) * (p.y - b.y) / (a.y - b.y) + b.x) inside = !inside;
                    var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
                }
                return inside ? -best : best;
            }
        }

        // 길이 방향(x) 기준으로 그린 무기를 45°로 눕혀 왼쪽 아래 → 오른쪽 위
        private static Vector2 D(float along, float across)
        {
            const float k = 0.70710678f;
            return new Vector2(0.5f + (along - across) * k, 0.5f + (along + across) * k);
        }
        private static Poly P(Color c, params Vector2[] pts) => new Poly { Fill = c, P = pts };
        private static Capsule L(Color c, Vector2 a, Vector2 b, float r) => new Capsule { Fill = c, A = a, B = b, R = r };
        private static Circle O(Color c, Vector2 at, float r) => new Circle { Fill = c, C = at, R = r };
        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static Dictionary<string, Shape[]> Icons() => new()
        {
            ["Dagger"] = new Shape[] { P(Steel, D(-0.05f, -0.055f), D(0.3f, 0f), D(-0.05f, 0.055f)), L(Gold, D(-0.07f, -0.12f), D(-0.07f, 0.12f), 0.025f), L(Leather, D(-0.09f, 0), D(-0.24f, 0), 0.032f), O(Gold, D(-0.27f, 0), 0.04f) },
            ["SwordSaber"] = new Shape[] { P(Steel, D(-0.12f, -0.045f), D(0.36f, -0.03f), D(0.42f, 0f), D(0.36f, 0.03f), D(-0.12f, 0.045f)), L(Gold, D(-0.14f, -0.14f), D(-0.14f, 0.14f), 0.026f), L(Leather, D(-0.16f, 0), D(-0.32f, 0), 0.03f), O(Gold, D(-0.35f, 0), 0.04f) },
            ["Greatsword"] = new Shape[] { P(Steel, D(-0.1f, -0.08f), D(0.36f, -0.06f), D(0.44f, 0f), D(0.36f, 0.06f), D(-0.1f, 0.08f)), L(SteelDark, D(-0.06f, 0f), D(0.34f, 0f), 0.012f), L(Gold, D(-0.12f, -0.18f), D(-0.12f, 0.18f), 0.032f), L(Leather, D(-0.15f, 0), D(-0.36f, 0), 0.034f), O(Gold, D(-0.4f, 0), 0.045f) },
            ["Polearm"] = new Shape[] { L(Wood, D(-0.44f, 0), D(0.22f, 0), 0.026f), P(Steel, D(0.18f, -0.07f), D(0.44f, 0f), D(0.18f, 0.07f), D(0.22f, 0f)), L(Gold, D(0.17f, -0.05f), D(0.17f, 0.05f), 0.02f) },
            ["Bow"] = new Shape[] { new Arc { Fill = Wood, C = V(0.36f, 0.36f), R = 0.42f, W = 0.035f, A0 = -5f, A1 = 95f }, L(Paper, V(0.78f, 0.33f), V(0.33f, 0.78f), 0.01f), L(Leather, V(0.6f, 0.6f), V(0.69f, 0.69f), 0.04f), L(Steel, V(0.2f, 0.2f), V(0.66f, 0.66f), 0.012f), P(Steel, V(0.7f, 0.62f), V(0.78f, 0.78f), V(0.62f, 0.7f)) },
            ["Pistol"] = new Shape[] { P(SteelDark, V(0.18f, 0.56f), V(0.82f, 0.56f), V(0.82f, 0.68f), V(0.18f, 0.68f)), P(Wood, V(0.3f, 0.58f), V(0.5f, 0.58f), V(0.42f, 0.22f), V(0.22f, 0.22f)), new Arc { Fill = Steel, C = V(0.5f, 0.5f), R = 0.07f, W = 0.015f, A0 = 180f, A1 = 360f }, L(Gold, V(0.2f, 0.62f), V(0.3f, 0.62f), 0.02f) },
            ["Musket"] = new Shape[] { L(SteelDark, D(-0.1f, 0.02f), D(0.46f, 0.02f), 0.026f), P(Wood, D(-0.46f, -0.1f), D(-0.1f, -0.02f), D(0.24f, -0.02f), D(0.24f, 0.04f), D(-0.1f, 0.06f), D(-0.46f, 0.06f)), L(Gold, D(-0.02f, 0.05f), D(0.06f, 0.05f), 0.02f) },
            ["Staff"] = new Shape[] { L(Wood, D(-0.46f, 0), D(0.2f, 0), 0.028f), new Arc { Fill = Gold, C = D(0.28f, 0), R = 0.1f, W = 0.022f, A0 = -90f, A1 = 180f }, O(Orb, D(0.28f, 0), 0.075f), O(Color.white, D(0.3f, 0.03f), 0.018f) },
            ["Grimoire"] = new Shape[] { P(Paper, V(0.22f, 0.2f), V(0.8f, 0.22f), V(0.8f, 0.8f), V(0.22f, 0.78f)), P(Leather, V(0.18f, 0.18f), V(0.74f, 0.18f), V(0.74f, 0.82f), V(0.18f, 0.82f)), L(Gold, V(0.22f, 0.2f), V(0.22f, 0.8f), 0.02f), new Arc { Fill = Gold, C = V(0.48f, 0.5f), R = 0.13f, W = 0.022f, A0 = 0f, A1 = 360f }, O(Gem, V(0.48f, 0.5f), 0.05f) },
            ["Instrument"] = new Shape[] { L(Wood, D(0.05f, 0), D(0.44f, 0), 0.03f), O(Wood, D(-0.18f, 0), 0.2f), O(Wood, D(0.04f, 0), 0.15f), O(Outline, D(-0.06f, 0), 0.03f), L(Paper, D(-0.3f, 0), D(0.42f, 0), 0.006f), L(Leather, D(-0.32f, -0.04f), D(-0.32f, 0.04f), 0.02f) },
            ["Body"] = new Shape[] { P(Cloth, V(0.32f, 0.84f), V(0.42f, 0.78f), V(0.5f, 0.66f), V(0.58f, 0.78f), V(0.68f, 0.84f), V(0.86f, 0.7f), V(0.78f, 0.44f), V(0.72f, 0.5f), V(0.74f, 0.12f), V(0.26f, 0.12f), V(0.28f, 0.5f), V(0.22f, 0.44f), V(0.14f, 0.7f)), L(Gold, V(0.5f, 0.6f), V(0.5f, 0.6f), 0.022f), L(Gold, V(0.5f, 0.45f), V(0.5f, 0.45f), 0.022f), L(Gold, V(0.5f, 0.3f), V(0.5f, 0.3f), 0.022f) },
            ["Head"] = new Shape[] { P(Cloth, V(0.1f, 0.38f), V(0.3f, 0.42f), V(0.32f, 0.64f), V(0.5f, 0.74f), V(0.68f, 0.64f), V(0.7f, 0.42f), V(0.9f, 0.38f), V(0.5f, 0.28f)), L(Gold, V(0.3f, 0.43f), V(0.7f, 0.43f), 0.02f) },
            ["Ring"] = new Shape[] { new Arc { Fill = Gold, C = V(0.5f, 0.42f), R = 0.22f, W = 0.045f, A0 = 0f, A1 = 360f }, P(Gold, V(0.4f, 0.62f), V(0.6f, 0.62f), V(0.56f, 0.7f), V(0.44f, 0.7f)), O(Gem, V(0.5f, 0.74f), 0.08f), O(Color.white, V(0.48f, 0.77f), 0.02f) },
            ["Necklace"] = new Shape[] { new Arc { Fill = Gold, C = V(0.5f, 0.66f), R = 0.28f, W = 0.016f, A0 = 200f, A1 = 340f }, L(Gold, V(0.5f, 0.38f), V(0.5f, 0.38f), 0.03f), P(Gold, V(0.5f, 0.36f), V(0.6f, 0.24f), V(0.5f, 0.1f), V(0.4f, 0.24f)), O(Gem, V(0.5f, 0.24f), 0.045f) },
            ["Potion"] = new Shape[] { O(Glass, V(0.5f, 0.36f), 0.24f), O(Potion, V(0.5f, 0.33f), 0.19f), P(Glass, V(0.42f, 0.56f), V(0.58f, 0.56f), V(0.58f, 0.74f), V(0.42f, 0.74f)), P(Wood, V(0.4f, 0.74f), V(0.6f, 0.74f), V(0.58f, 0.86f), V(0.42f, 0.86f)), O(Color.white, V(0.42f, 0.42f), 0.04f) },
            ["Scroll"] = new Shape[] { P(Paper, V(0.24f, 0.26f), V(0.76f, 0.26f), V(0.76f, 0.74f), V(0.24f, 0.74f)), L(Paper, V(0.2f, 0.76f), V(0.8f, 0.76f), 0.06f), L(Paper, V(0.2f, 0.24f), V(0.8f, 0.24f), 0.06f), L(SteelDark, V(0.32f, 0.6f), V(0.68f, 0.6f), 0.012f), L(SteelDark, V(0.32f, 0.5f), V(0.68f, 0.5f), 0.012f), L(SteelDark, V(0.32f, 0.4f), V(0.6f, 0.4f), 0.012f), O(Gem, V(0.66f, 0.34f), 0.06f) },
            ["Pouch"] = new Shape[] { O(Leather, V(0.5f, 0.38f), 0.27f), P(Leather, V(0.36f, 0.6f), V(0.64f, 0.6f), V(0.72f, 0.8f), V(0.28f, 0.8f)), L(Gold, V(0.34f, 0.6f), V(0.66f, 0.6f), 0.025f), O(Gold, V(0.5f, 0.36f), 0.06f) },
        };

        [MenuItem("Fantasia/Art/Item Category Icons")]
        public static void Run()
        {
            Directory.CreateDirectory(Dir);
            const float outline = 0.022f; // 테두리 굵기 (그림 크기 비율)
            int n = Size * SS;
            foreach (var kv in Icons())
            {
                var big = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                    // 0.06 여백 안으로 줄여 그림
                    var q = (p - new Vector2(0.5f, 0.5f)) / 0.88f + new Vector2(0.5f, 0.5f);
                    Color c = Color.clear;
                    foreach (var s in kv.Value) if (s.Dist(q) < outline) c = Outline; // 먼저 테두리 (모든 모양)
                    foreach (var s in kv.Value)
                    {
                        float d = s.Dist(q);
                        if (d < 0f)
                        {
                            // 위쪽 빛: 모양 안쪽 위가 살짝 밝게
                            float shade = Mathf.Lerp(0.82f, 1.08f, q.y);
                            c = new Color(Mathf.Clamp01(s.Fill.r * shade), Mathf.Clamp01(s.Fill.g * shade), Mathf.Clamp01(s.Fill.b * shade), 1f);
                        }
                        else if (d < outline) c = Outline; // 겹친 모양끼리도 경계선
                    }
                    big[y * n + x] = c;
                }
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                var px = new Color[Size * Size];
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        var c = big[(y * SS + sy) * n + x * SS + sx];
                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                    }
                    px[y * Size + x] = a > 0f ? new Color(r / a, g / a, b / a, a / (SS * SS)) : Color.clear;
                }
                tex.SetPixels(px);
                tex.Apply();
                string path = $"{Dir}/Cat_{kv.Key}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single; // 여러 장(Multiple)이면 Resources.Load<Sprite>로 못 불러옴
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            Debug.Log($"[ItemCategoryIconSetup] 종류 아이콘 {Icons().Count}개 → {Dir}");
        }
    }
}
