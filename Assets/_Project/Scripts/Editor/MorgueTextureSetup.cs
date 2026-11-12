using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/13 C2 폐검시소: 실내 돌바닥·돌벽 텍스처 (받은 바닥 텍스처는 육각 칸 모양이라 넓게 이어 붙일 수 없음).
    // 코드로 그린 이어 붙는 그림 → Resources/BattleArt/MorgueFloor.png · MorgueWall.png. 놓는 곳은 Combat/BattleScenery.BuildMorgue.
    // 실행: 메뉴 Fantasia/Art/폐검시소 바닥·벽 · 배치 -executeMethod Fantasia.Editor.MorgueTextureSetup.Run
    public static class MorgueTextureSetup
    {
        private const string Dir = "Assets/_Project/Resources/BattleArt";
        private const int N = 512;

        [MenuItem("Fantasia/Art/폐검시소 바닥·벽")]
        public static void Run()
        {
            Directory.CreateDirectory(Dir);
            // 바닥: 한 그림 = 2×2 큰 돌판 (반 줄씩 엇갈림), 회색 돌 + 어두운 줄눈
            Save("MorgueFloor", Draw(cols: 2, rows: 2, mortar: 7, baseCol: new Color(0.47f, 0.46f, 0.45f), vary: 0.07f, seed: 11));
            // 벽: 한 그림 = 4×8 벽돌 (가로로 긴 돌), 조금 더 어둡고 따뜻하게
            Save("MorgueWall", Draw(cols: 4, rows: 8, mortar: 5, baseCol: new Color(0.42f, 0.39f, 0.36f), vary: 0.09f, seed: 23));
            AssetDatabase.Refresh();
            foreach (var n in new[] { "MorgueFloor", "MorgueWall" })
            {
                if (AssetImporter.GetAtPath($"{Dir}/{n}.png") is TextureImporter ti)
                {
                    ti.wrapMode = TextureWrapMode.Repeat;
                    ti.mipmapEnabled = true;
                    ti.anisoLevel = 4;
                    ti.SaveAndReimport();
                }
            }
            Debug.Log("[MorgueTextureSetup] 바닥·벽 텍스처 만듦");
        }

        private static Texture2D Draw(int cols, int rows, int mortar, Color baseCol, float vary, int seed)
        {
            var rnd = new System.Random(seed);
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            int cw = N / cols, rh = N / rows;
            // 돌마다 밝기 (반 줄 엇갈림 포함해 이어 붙도록 열 수+1)
            var tone = new float[rows, cols + 1];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c <= cols; c++)
                    tone[r, c] = (float)(rnd.NextDouble() * 2 - 1) * vary;
            for (int r = 0; r < rows; r++) tone[r, cols] = tone[r, 0];

            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            {
                int r = y / rh;
                int shift = (r % 2 == 1) ? cw / 2 : 0;
                for (int x = 0; x < N; x++)
                {
                    int sx = (x + shift) % N;
                    int c = sx / cw;
                    int lx = sx - c * cw, ly = y - r * rh;
                    int edge = Mathf.Min(Mathf.Min(lx, cw - 1 - lx), Mathf.Min(ly, rh - 1 - ly));
                    // 잔무늬 (이어 붙게 주기 함수)
                    float u = (float)x / N, v = (float)y / N;
                    float grain = 0.035f * Mathf.Sin(u * Mathf.PI * 2 * 9 + Mathf.Sin(v * Mathf.PI * 2 * 5) * 2f)
                                + 0.03f * Mathf.Sin(v * Mathf.PI * 2 * 13 + Mathf.Cos(u * Mathf.PI * 2 * 7) * 1.5f)
                                + 0.02f * (float)(rnd.NextDouble() - 0.5);
                    Color col = baseCol * (1f + tone[r, c] + grain);
                    if (edge < mortar) col = Color.Lerp(new Color(0.12f, 0.11f, 0.11f), col, edge / (float)mortar * 0.5f);
                    else if (edge < mortar + 3) col *= 0.9f; // 돌 가장자리 살짝 어둡게 (깎인 느낌)
                    col.a = 1f;
                    px[y * N + x] = col;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static void Save(string name, Texture2D tex)
        {
            File.WriteAllBytes($"{Dir}/{name}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
