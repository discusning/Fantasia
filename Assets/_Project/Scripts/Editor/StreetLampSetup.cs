using System.IO;
using System.Linq;
using Fantasia.Combat;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/12 가로등 불빛 준비 (Combat/StreetLampGlow). ArtSetup(전투 소품 프리팹) 뒤에 돌림. 여러 번 돌려도 같은 결과.
    // ① 발광 그림: 받은 모델에 "불 들어가는 부분" 정보가 없어서, 색 그림에서 유리 색(밝고 따뜻한 색)만 골라 _Emission.png를 만듦.
    //    디자인이 발광 그림을 주면 그 파일로 바꾸기만 하면 됨 (Docs/Planning/StreetLamp_시간대_제안.md 7).
    // ② 불 위치: 발광 부분에 닿는 꼭짓점들의 가운데 → 프리팹에 "LampPoint" + StreetLampGlow.
    // ③ 빛 번짐 그림(부드러운 원) + 더하기 재질 → Resources/BattleArt/LampGlow.
    // 배치: -executeMethod Fantasia.Editor.StreetLampSetup.Run
    public static class StreetLampSetup
    {
        private const string Props = "Assets/_Project/Art/Models/Props";
        private const string BattleArt = "Assets/_Project/Resources/BattleArt";
        private static readonly (string role, string prop, bool wall, float minV, float minS)[] Lamps =
        {
            ("StreetLamp", "Prop_StreetLamp", false, 0.62f, 0.2f),  // 베이지 유리판
            ("WallLamp", "Prop_WallLamp", true, 0.68f, 0.45f),      // 주황 유리 조각 (갈색 돌·쇠는 빼려고 더 진하고 밝은 것만)
        };

        [MenuItem("Fantasia/Art/가로등 불빛 준비")]
        public static void Run()
        {
            var log = new System.Text.StringBuilder("[StreetLampSetup]\n");
            MakeGlowAssets();
            foreach (var (role, prop, wall, minV, minS) in Lamps)
            {
                string dir = $"{Props}/{prop}";
                var albedoBytes = File.ReadAllBytes($"{dir}/{prop}_Albedo.png");
                var albedo = new Texture2D(2, 2);
                albedo.LoadImage(albedoBytes);
                var mask = BuildMask(albedo, minV, minS, out float share);
                string emPath = $"{dir}/{prop}_Emission.png";
                File.WriteAllBytes(emPath, mask.EncodeToPNG());
                AssetDatabase.ImportAsset(emPath);
                if (AssetImporter.GetAtPath(emPath) is TextureImporter ti) { ti.maxTextureSize = 1024; ti.mipmapEnabled = true; ti.SaveAndReimport(); }

                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{prop}.mat");
                mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emPath));
                mat.SetColor("_EmissionColor", Color.black); // 켜는 세기는 게임에서 (StreetLampGlow)
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(mat);

                string prefabPath = $"{BattleArt}/{role}.prefab";
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                var old = root.transform.Find("LampPoint");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                foreach (var g in root.GetComponents<StreetLampGlow>()) Object.DestroyImmediate(g);

                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                Vector3 sum = Vector3.zero; int n = 0; float weight = 0f;
                foreach (var r in renderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var verts = mf.sharedMesh.vertices; var uvs = mf.sharedMesh.uv;
                    if (uvs.Length != verts.Length) continue;
                    // 삼각형 가운데 UV가 발광이면, 그 삼각형 넓이만큼 무게를 줌 (기둥에 찍힌 작은 점들은 넓이가 작아 무시됨)
                    var tris = mf.sharedMesh.triangles;
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        int a0 = tris[i], a1 = tris[i + 1], a2 = tris[i + 2];
                        var uv = (uvs[a0] + uvs[a1] + uvs[a2]) / 3f;
                        if (mask.GetPixelBilinear(uv.x, uv.y).r < 0.5f) continue;
                        Vector3 p0 = r.transform.TransformPoint(verts[a0]), p1 = r.transform.TransformPoint(verts[a1]), p2 = r.transform.TransformPoint(verts[a2]);
                        float area = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                        sum += (p0 + p1 + p2) / 3f * area; weight += area; n++;
                    }
                }
                var point = new GameObject("LampPoint").transform;
                point.SetParent(root.transform, false);
                var b = renderers.Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
                point.position = weight > 0f ? sum / weight : new Vector3(b.center.x, b.max.y * 0.9f, b.center.z);
                if (wall) point.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(point.position - new Vector3(b.center.x, point.position.y, b.center.z), Vector3.up).sqrMagnitude > 0.0001f
                    ? point.position - new Vector3(b.center.x, point.position.y, b.center.z) : Vector3.forward); // 벽에서 튀어나온 쪽 = 앞
                var glow = root.AddComponent<StreetLampGlow>();
                glow.Point = point;
                glow.Glass = renderers;
                glow.Wall = wall;
                log.AppendLine($"{role}: 발광 {share:P1} · 불 위치 {point.localPosition} (삼각형 {n}개) · 크기 {b.size}");
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        // 밝고 따뜻한 색(유리)만 흰색. 1픽셀 번지게 해서 모서리 틈 줄임
        private static Texture2D BuildMask(Texture2D src, float minV, float minS, out float share)
        {
            int w = src.width, h = src.height;
            var px = src.GetPixels32();
            var on = new bool[px.Length];
            int count = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out float hh, out float s, out float v);
                bool warm = hh >= 20f / 360f && hh <= 55f / 360f;
                on[i] = warm && v >= minV && s >= minS;
                if (on[i]) count++;
            }
            share = count / (float)px.Length;
            var outPx = new Color32[px.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool hit = false;
                    for (int dy = -1; dy <= 1 && !hit; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx >= 0 && yy >= 0 && xx < w && yy < h && on[yy * w + xx]) { hit = true; break; }
                        }
                    outPx[y * w + x] = hit ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPx);
            tex.Apply();
            return tex;
        }

        private static void MakeGlowAssets()
        {
            // 바닥 빛 웅덩이: 둥근 원
            MakeGlow("LampGlow", (u, v) => Smooth(1f - new Vector2(u, v).magnitude));
            // 등 둘레 빛 번짐 (11/12 팀장 지적: 지붕 판을 뚫고 위로 빛이 나옴):
            // 쇠 지붕 위로는 빛이 안 나가게 가운데보다 위는 빨리 사라지고(0.12), 아래로는 조금 길게(1.25배) — 유리 옆·아래로 새는 빛
            MakeGlow("LampHalo", (u, v) =>
            {
                float up = v > 0f ? Mathf.Clamp01(1f - v / 0.12f) : 1f;
                var p = new Vector2(u, v < 0f ? v / 1.25f : v);
                return Smooth(1f - p.magnitude) * Smooth(up);
            });
        }

        private static float Smooth(float a) { a = Mathf.Clamp01(a); return a * a * (3f - 2f * a); }

        // u, v: 가운데 0, 가장자리 ±1 (v는 위가 +)
        private static void MakeGlow(string name, System.Func<float, float, float> alpha)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / (size / 2f) - 1f, v = (y + 0.5f) / (size / 2f) - 1f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(u, v)));
                }
            tex.Apply();
            Directory.CreateDirectory(BattleArt);
            string texPath = $"{BattleArt}/{name}.png";
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(texPath);
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti) { ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport(); }
            string matPath = $"{BattleArt}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorUtility.SetDirty(mat);
        }
    }
}
