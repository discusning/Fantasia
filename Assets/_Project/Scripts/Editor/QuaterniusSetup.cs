using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/13 팀장이 받은 Quaternius Fantasy Props MegaKit · Stylized Nature MegaKit (Standard 무료판, CC0) 중 보드 장식용만.
    // 모델(Art/Models/Quaternius) → 보드 톤에 맞춘 표준 재질 → Resources/BoardArt/Deco/{이름}.prefab
    // 실행: -executeMethod Fantasia.Editor.QuaterniusSetup.Build
    public static class QuaterniusSetup
    {
        private const string Src = "Assets/_Project/Art/Models/Quaternius/";
        private const string MatDir = Src + "Materials/";
        private const string Out = "Assets/_Project/Resources/BoardArt/Deco/";

        [MenuItem("Fantasia/Board/Quaternius Deco Build")]
        public static void Build()
        {
            Directory.CreateDirectory(MatDir); Directory.CreateDirectory(Out);
            AssetDatabase.Refresh();
            foreach (var f in Directory.GetFiles(Src + "Textures", "*.png"))
            {
                var im = AssetImporter.GetAtPath(f.Replace('\\', '/')) as TextureImporter;
                if (im == null) continue;
                bool normal = f.Contains("_Normal");
                im.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                im.sRGBTexture = !normal;
                im.alphaIsTransparency = !normal;
                im.maxTextureSize = 1024;
                im.SaveAndReimport();
            }

            // 보드 톤: 원본이 밝고 채도가 높아 어둡고 바랜 런던 거리에 맞게 누름
            var wood = new Color(0.78f, 0.74f, 0.7f);
            var green = new Color(0.62f, 0.68f, 0.56f);
            Material Furniture = Mat("Q_Furniture", "T_Trim_Furniture_BaseColor", "T_Trim_Furniture_Normal", wood, false);
            Material Metal = Mat("Q_Metal", "T_Trim_Metal_BaseColor", "T_Trim_Metal_Normal", wood, false);
            Material Props = Mat("Q_Props", "T_Trim_Props_BaseColor", null, wood, false);
            Material Cloth = Mat("Q_Cloth", "T_Trim_Cloth_BaseColor", "T_Trim_Cloth_Normal", wood, false);
            Material Rocks = Mat("Q_Rocks", "Rocks_Diffuse", null, new Color(0.72f, 0.72f, 0.7f), false);
            Material LeavesBush = Mat("Q_LeavesBush", "Leaves_NormalTree_C", null, green, true);
            Material LeavesTree = Mat("Q_LeavesTree", "Leaves_NormalTree_C", null, green, true);
            Material Flowers = Mat("Q_Flowers", "Flowers", null, new Color(0.75f, 0.72f, 0.7f), true);
            Material Grass = Mat("Q_Grass", "Grass", null, green, true);
            Material Leaves = Mat("Q_Leaves", "Leaves", null, green, true);
            Material Branch = Mat("Q_Branch", null, null, new Color(0.25f, 0.19f, 0.14f), false);
            // 11/13 6차 전투 맵: 나무·고목·버섯·디딤돌 (껍질은 잎보다 먼저 골라야 함 — "Bark_NormalTree"에 "NormalTree"가 들어 있음)
            Material Bark = Mat("Q_Bark", "Bark_NormalTree", "Bark_NormalTree_Normal", new Color(0.7f, 0.66f, 0.62f), false);
            Material BarkDead = Mat("Q_BarkDead", "Bark_DeadTree", "Bark_DeadTree_Normal", new Color(0.7f, 0.66f, 0.62f), false);
            Material Mushroom = Mat("Q_Mushroom", "Mushrooms", null, new Color(0.8f, 0.76f, 0.72f), false);
            Material PathRocks = Mat("Q_PathRocks", "PathRocks_Diffuse", null, new Color(0.7f, 0.7f, 0.68f), false);

            Material Pick(string n)
            {
                if (n.Contains("Bark_DeadTree")) return BarkDead;
                if (n.Contains("Bark")) return Bark;
                if (n.Contains("Mushroom")) return Mushroom;
                if (n.Contains("PathRocks")) return PathRocks;
                if (n.Contains("Furniture")) return Furniture;
                if (n.Contains("Metal")) return Metal;
                if (n.Contains("Props")) return Props;
                if (n.Contains("Cloth") || n.Contains("Banner")) return Cloth;
                if (n.Contains("Rock") || n.Contains("Pebble")) return Rocks;
                if (n.Contains("TwistedTree")) return LeavesBush;
                if (n.Contains("NormalTree")) return LeavesTree;
                if (n.Contains("Flower")) return Flowers;
                if (n.Contains("Grass")) return Grass;
                if (n.Contains("Leaves")) return Leaves;
                return Branch;
            }

            int made = 0;
            foreach (var dir in new[] { "Props", "Nature" })
            foreach (var f in Directory.GetFiles(Src + dir, "*.fbx"))
            {
                string path = f.Replace('\\', '/');
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi != null)
                {
                    mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                    mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                    mi.SaveAndReimport();
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                var root = new GameObject(Path.GetFileNameWithoutExtension(path));
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.transform.SetParent(root.transform, false);
                foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = Pick(mats[i] != null ? mats[i].name : "");
                    r.sharedMaterials = mats;
                }
                PrefabUtility.SaveAsPrefabAsset(root, Out + root.name + ".prefab");
                Object.DestroyImmediate(root);
                made++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[QuaterniusSetup] 장식 프리팹 {made}개");
        }

        private static Material Mat(string name, string albedo, string normal, Color tint, bool cutout)
        {
            string p = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, p); }
            m.shader = Shader.Find("Standard");
            m.color = tint;
            if (albedo != null) m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "Textures/" + albedo + ".png");
            if (normal != null)
            {
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "Textures/" + normal + ".png"));
                m.SetFloat("_BumpScale", 0.8f);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Glossiness", 0.08f);
            m.SetFloat("_Metallic", 0f);
            if (cutout)
            {
                m.SetFloat("_Mode", 1f);
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                m.SetInt("_ZWrite", 1);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cutoff", 0.45f);
                m.renderQueue = 2450;
                m.doubleSidedGI = true;
            }
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
