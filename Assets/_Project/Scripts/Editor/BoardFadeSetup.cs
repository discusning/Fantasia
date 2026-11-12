using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fantasia.Editor
{
    // 11/12 (2번): 보드 구조물의 반투명 재질을 미리 만듦 → Resources/BoardArt/Fade/<재질>_Fade.mat
    // 런타임에 Standard 재질을 Fade로 바꾸면 빌드에 반투명 셰이더 변형이 빠져 있어 안 보일 수 있음 → 재질 파일로 미리 둠.
    // HexTile.SetPropAlpha가 이름으로 불러 씀. ArtSetup.Run 끝에서도 부름.
    public static class BoardFadeSetup
    {
        private const string BoardArt = "Assets/_Project/Resources/BoardArt";
        private const string FadeDir = BoardArt + "/Fade";

        [MenuItem("Fantasia/Art/Board Fade Materials")]
        public static void Run()
        {
            Directory.CreateDirectory(FadeDir);
            var made = new List<string>();
            var skipped = new List<string>();
            var mats = new HashSet<Material>();
            foreach (var path in Directory.GetFiles(BoardArt, "*.prefab"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("Tile") || name == "Dice") continue; // 칸·주사위는 가리지 않음
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                if (prefab == null) continue;
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
            }
            foreach (var m in mats)
            {
                if (m.shader == null || m.shader.name != "Standard") { skipped.Add($"{m.name}({m.shader?.name})"); continue; }
                string dst = $"{FadeDir}/{m.name}_Fade.mat";
                var f = AssetDatabase.LoadAssetAtPath<Material>(dst);
                if (f == null) { f = new Material(m); AssetDatabase.CreateAsset(f, dst); }
                else f.CopyPropertiesFromMaterial(m);
                f.shader = m.shader;
                f.SetFloat("_Mode", 2f); // Fade
                f.SetOverrideTag("RenderType", "Transparent");
                f.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                f.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                f.SetInt("_ZWrite", 0);
                f.DisableKeyword("_ALPHATEST_ON");
                f.EnableKeyword("_ALPHABLEND_ON");
                f.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                f.renderQueue = (int)RenderQueue.Transparent;
                f.enableInstancing = true;
                EditorUtility.SetDirty(f);
                made.Add(m.name);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[BoardFadeSetup] 반투명 재질 {made.Count}개: {string.Join(", ", made)}" + (skipped.Count > 0 ? $"\n건너뜀(Standard 아님): {string.Join(", ", skipped)}" : ""));
        }
    }
}
