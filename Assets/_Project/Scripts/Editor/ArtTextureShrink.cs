using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 받은 원본 PNG(최대 4096)를 게임에서 실제로 쓰는 크기(인물 2048 · 나머지 1024, ArtSetup 가져오기 설정과 같음)로
    // 파일 자체를 줄인다 — 화면은 그대로, 저장소(Git LFS) 용량만 줄어듦. 원본은 ArtSource/2026-11-05_디자인_3D원본 (git 제외).
    // 그래픽 장치가 필요해 -nographics 없이 돌린다.
    public static class ArtTextureShrink
    {
        private const string Root = "Assets/_Project/Art/Models";

        [MenuItem("Fantasia/Art/Shrink Received Texture Files")]
        public static void Run()
        {
            long before = 0, after = 0;
            int changed = 0;
            foreach (var path in Directory.GetFiles(Root, "*.png", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')))
            {
                int max = path.Contains("/Characters/") ? 2048 : 1024;
                var bytes = File.ReadAllBytes(path);
                before += bytes.Length;
                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                src.LoadImage(bytes);
                if (src.width <= max && src.height <= max) { after += bytes.Length; Object.DestroyImmediate(src); continue; }
                float k = (float)max / Mathf.Max(src.width, src.height);
                int w = Mathf.RoundToInt(src.width * k), h = Mathf.RoundToInt(src.height * k);
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(src, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                dst.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var png = dst.EncodeToPNG();
                File.WriteAllBytes(path, png);
                after += png.Length;
                changed++;
                Object.DestroyImmediate(src);
                Object.DestroyImmediate(dst);
            }
            AssetDatabase.Refresh();
            Debug.Log($"[ArtTextureShrink] {changed}개 줄임 · {before / 1048576f:0}MB → {after / 1048576f:0}MB");
        }
    }
}
