using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/14 팀장 1안 "런던 안개": 보드 안개 층 재질 + 이어 붙여도 이음새 없는 안개 무늬 그림을 만듦.
    // 실행: -executeMethod Fantasia.Editor.FogVeilSetup.Build → Resources/BoardArt/FogNoise.png · Mat_FogVeil.mat
    // 셰이더: Legacy Particles/Alpha Blended (키워드 없음 → 빌드에서 빠질 걱정 없음, 정점 알파로 경계가 서서히 짙어짐)
    public static class FogVeilSetup
    {
        private const string TexPath = "Assets/_Project/Resources/BoardArt/FogNoise.png";
        private const string MatPath = "Assets/_Project/Resources/BoardArt/Mat_FogVeil.mat";

        [MenuItem("Fantasia/Board/Fog Veil Build")]
        public static void Build()
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    // 주기 노이즈 여러 겹 (가장자리가 맞물려 이어 붙여도 이음새 없음)
                    float n = 0f, amp = 0.55f, sum = 0f;
                    for (int o = 0; o < 4; o++)
                    {
                        int period = 4 << o;
                        n += amp * PeriodicNoise(u * period, v * period, period, 17 + o * 31);
                        sum += amp; amp *= 0.5f;
                    }
                    n /= sum;
                    float a = Mathf.SmoothStep(0.32f, 0.72f, n); // 11/14 팀장 2차: 뭉게구름처럼 덩어리가 또렷하게
                    a = 0.18f + 0.82f * a;
                    float l = 0.86f + 0.14f * n; // 구름 안쪽이 조금 더 밝음
                    px[y * S + x] = new Color(l, l, l, a);
                }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(TexPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(TexPath);
            imp.wrapMode = TextureWrapMode.Repeat; imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.sRGBTexture = true;
            imp.SaveAndReimport();

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, MatPath); }
            mat.shader = shader;
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            mat.SetColor("_TintColor", new Color(0.3f, 0.3f, 0.27f, 0.5f));
            mat.renderQueue = 3050; // 다른 반투명(칸 칠)보다 뒤에
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            BuildHalo();
            Debug.Log("[FogVeil] 저장 " + TexPath + " · " + MatPath + " · 셰이더 " + (shader != null ? shader.name : "없음"));
        }

        // 11/14 팀장 3차: 안개 속 가로등 불빛 번짐 (가운데 밝고 바깥으로 부드럽게 사라지는 둥근 빛, 더하기 섞기)
        private const string HaloTexPath = "Assets/_Project/Resources/BoardArt/FogLampHaloTex.png";
        private const string HaloMatPath = "Assets/_Project/Resources/BoardArt/Mat_FogLampHalo.mat";
        private static void BuildHalo()
        {
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r); a = a * a * (0.6f + 0.4f * a); // 가운데 진하고 끝은 0
                    px[y * S + x] = new Color(a, a, a, a);
                }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(HaloTexPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(HaloTexPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(HaloTexPath);
            imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.sRGBTexture = false; imp.SaveAndReimport(); // 선형: 끝까지 부드럽게 퍼짐
            var shader = Shader.Find("Legacy Shaders/Particles/Additive");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(HaloMatPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, HaloMatPath); }
            mat.shader = shader;
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(HaloTexPath);
            mat.SetColor("_TintColor", new Color(0.5f, 0.36f, 0.2f, 0.5f));
            mat.renderQueue = 3060; // 안개 층 위에
            EditorUtility.SetDirty(mat);
        }

        // 격자점 값이 주기로 돌아오는 값 노이즈 (부드러운 보간)
        private static float PeriodicNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
            float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        private static float Hash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period; y = ((y % period) + period) % period;
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }
}
