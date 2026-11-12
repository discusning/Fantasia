using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/13 폐검시소 핏자국 (팀장 지시 "과하지 않게"). 마른 검붉은 자국 4종을 코드로 그려
    // 바닥에 눕힌 판(Quad) 프리팹으로 → Resources/BattleArt/Blood_{0..3}.prefab. 놓는 곳은 Combat/BattleScenery.MorgueBlood.
    //  0 큰 웅덩이(가장자리 번짐) · 1 튄 자국(작은 방울 흩어짐) · 2 끌린 자국(한쪽으로 긴) · 3 떨어진 방울 몇 개
    // 실행: 메뉴 Fantasia/Art/폐검시소 핏자국 · 배치 -executeMethod Fantasia.Editor.BloodDecalSetup.Run
    public static class BloodDecalSetup
    {
        private const string Dir = "Assets/_Project/Resources/BattleArt";
        private const int N = 256;
        private static readonly Color Dry = new Color(0.32f, 0.03f, 0.025f);   // 마른 피 (어두운 갈색빛 빨강)
        private static readonly Color Wet = new Color(0.46f, 0.04f, 0.035f);     // 가운데 조금 덜 마름

        [MenuItem("Fantasia/Art/폐검시소 핏자국")]
        public static void Run()
        {
            Directory.CreateDirectory(Dir);
            for (int v = 0; v < 4; v++)
            {
                string png = $"{Dir}/Blood_{v}.png";
                File.WriteAllBytes(png, Draw(v).EncodeToPNG());
                AssetDatabase.ImportAsset(png);
                if (AssetImporter.GetAtPath(png) is TextureImporter ti)
                {
                    ti.alphaIsTransparency = true;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.mipmapEnabled = true;
                    ti.SaveAndReimport();
                }
                // Standard Fade 모드는 빌드에서 투명 변형이 빠져 네모 판으로 보였음 → 키워드 없는 기본 투명 셰이더
                var mat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
                mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(png);
                mat.color = Color.white;
                string matPath = $"{Dir}/Blood_{v}.mat";
                AssetDatabase.DeleteAsset(matPath);
                AssetDatabase.CreateAsset(mat, matPath);

                var root = new GameObject($"Blood_{v}");
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(root.transform, false);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘 (크기 1m, 놓을 때 늘림)
                var r = quad.GetComponent<MeshRenderer>();
                r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/Blood_{v}.prefab");
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[BloodDecalSetup] 핏자국 4종 만듦");
        }

        private static Texture2D Draw(int variant)
        {
            var rnd = new System.Random(100 + variant);
            var a = new float[N * N];
            void Blob(float cx, float cy, float rad, float rough, float strength)
            {
                // 가장자리가 울퉁불퉁한 둥근 자국: 각도마다 반지름을 흔듦
                int k1 = rnd.Next(3, 7), k2 = rnd.Next(7, 13);
                float p1 = (float)rnd.NextDouble() * 6.28f, p2 = (float)rnd.NextDouble() * 6.28f;
                int x0 = Mathf.Max(0, (int)(cx - rad * 1.5f)), x1 = Mathf.Min(N - 1, (int)(cx + rad * 1.5f));
                int y0 = Mathf.Max(0, (int)(cy - rad * 1.5f)), y1 = Mathf.Min(N - 1, (int)(cy + rad * 1.5f));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float ang = Mathf.Atan2(dy, dx);
                        float rr = rad * (1f + rough * (0.6f * Mathf.Sin(ang * k1 + p1) + 0.4f * Mathf.Sin(ang * k2 + p2)));
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(1f, rr);
                        float v = Mathf.Clamp01((1f - d) * 6f) * strength;
                        int i = y * N + x;
                        a[i] = Mathf.Max(a[i], v);
                    }
            }
            float c = N / 2f;
            switch (variant)
            {
                case 0: // 큰 웅덩이 + 둘레 작은 방울
                    Blob(c, c, N * 0.3f, 0.18f, 1f);
                    Blob(c + N * 0.18f, c - N * 0.1f, N * 0.12f, 0.25f, 1f);
                    for (int i = 0; i < 9; i++) { float ang = (float)rnd.NextDouble() * 6.28f, dist = N * (0.36f + (float)rnd.NextDouble() * 0.1f); Blob(c + Mathf.Cos(ang) * dist, c + Mathf.Sin(ang) * dist, N * (0.015f + (float)rnd.NextDouble() * 0.02f), 0.2f, 0.9f); }
                    break;
                case 1: // 튄 자국: 가운데 작은 덩어리 + 한쪽으로 흩어진 방울
                    Blob(c - N * 0.12f, c, N * 0.12f, 0.3f, 1f);
                    for (int i = 0; i < 26; i++) { float t = (float)rnd.NextDouble(); Blob(c - N * 0.1f + t * N * 0.5f, c + ((float)rnd.NextDouble() - 0.5f) * N * (0.15f + t * 0.4f), N * (0.008f + (float)rnd.NextDouble() * 0.03f * (1f - t * 0.7f)), 0.2f, 0.95f); }
                    break;
                case 2: // 끌린 자국: 긴 띠 (끝으로 갈수록 옅어짐)
                    for (int i = 0; i < 40; i++) { float t = i / 39f; Blob(N * 0.12f + t * N * 0.76f, c + Mathf.Sin(t * 3f) * N * 0.04f, N * (0.1f - t * 0.05f), 0.25f, 1f - t * 0.55f); }
                    break;
                default: // 떨어진 방울 몇 개
                    for (int i = 0; i < 7; i++) Blob(N * (0.2f + (float)rnd.NextDouble() * 0.6f), N * (0.2f + (float)rnd.NextDouble() * 0.6f), N * (0.03f + (float)rnd.NextDouble() * 0.05f), 0.22f, 1f);
                    break;
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            for (int i = 0; i < px.Length; i++)
            {
                float v = a[i];
                // 가운데(진한 곳)는 덜 마른 색, 가장자리는 마른 색 + 옅게
                var col = Color.Lerp(Dry, Wet, Mathf.Clamp01((v - 0.6f) * 2.5f));
                col.a = Mathf.Clamp01(v) * 0.82f;
                px[i] = col;
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
