using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fantasia.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/12 팀장 결정: 받은 아이템 3D 모델(Meshy)을 3D로 쓰지 않고 아이콘 그림으로 찍어 쓴다.
    // 모델마다 같은 카메라 각도·조명·여백, 투명 배경 256×256 → UI/Sprites/Items/Icon_<이름>.png → 아이템 데이터 Icon 칸에 연결.
    // 증거품(사건 기록)은 Resources/EvidenceIcons/에 저장 → CaseLogPanel이 이름으로 읽음.
    // 모델 폴더(Art/Models/Items)는 git 제외 — 원본은 ArtSource/2026-11-11_디자인_*. 다시 찍을 때만 필요.
    // 실행: 메뉴 Fantasia/Art/아이템 아이콘 찍기 · 배치: -executeMethod Fantasia.Editor.ItemIconStudio.Run [-only 성수,해독제]
    public static class ItemIconStudio
    {
        private const string ModelRoot = "Assets/_Project/Art/Models/Items";
        private const string IconDir = "Assets/_Project/UI/Sprites/Items";
        private const string EvidenceDir = "Assets/_Project/Resources/EvidenceIcons";
        private const int Size = 256, Render = 512;

        private enum Kind { Consumable, Armor, Evidence }

        // id = 모델 폴더 Item_<id> 이름 = 데이터 파일 이름 뒷부분. yaw·tilt·roll: 모델마다 보기 좋은 방향 (도). tilt는 앞으로 숙임.
        private static readonly (string id, Kind kind, float yaw, float tilt, float roll)[] Items =
        {
            ("집중의향초", Kind.Consumable, 25f, 0f, 0f),
            ("지혈연고", Kind.Consumable, 25f, 0f, 0f),
            ("정신안정염", Kind.Consumable, 25f, 0f, 0f),
            ("성수", Kind.Consumable, 25f, 0f, 0f),
            ("탈출권", Kind.Consumable, 25f, 0f, 0f),
            ("휴대수리도구", Kind.Consumable, 15f, -70f, 0f) /* 바닥에 누운 꾸러미 → 세움 */,
            ("연금술시약", Kind.Consumable, 25f, 0f, 0f),
            ("안개야영세트", Kind.Consumable, 25f, 0f, 0f),
            ("연금술화염병", Kind.Consumable, 25f, 0f, 0f),
            ("안개연막병", Kind.Consumable, 25f, 0f, 0f),
            ("생명의월계관", Kind.Consumable, 0f, -55f, 0f) /* 관을 위에서 */,
            ("신속의앰플", Kind.Consumable, 25f, 0f, -40f) /* 가늘어서 비스듬히 */,
            ("생명의영약", Kind.Consumable, 25f, 0f, 0f),
            ("마력의영약", Kind.Consumable, 25f, 0f, -25f),
            ("해독제", Kind.Consumable, 25f, 0f, 0f),
            ("텔레포트권", Kind.Consumable, 25f, 0f, 0f),
            ("마리의집문서", Kind.Consumable, 25f, 0f, 0f),
            ("인부의두꺼운코트", Kind.Armor, 25f, 0f, 0f),
            ("야경대삼각모", Kind.Armor, 25f, -25f, 0f),
            ("구리힘의반지", Kind.Armor, 25f, 0f, 0f),
            ("작은십자가부적", Kind.Armor, 25f, 0f, 0f),
            ("마리의계약인장", Kind.Armor, 25f, 0f, 0f),
            ("부서진회중시계", Kind.Armor, 25f, 0f, 0f),
            ("검시기록", Kind.Evidence, 15f, -65f, 0f) /* 누운 장부 → 세움 */,
            ("붉은룬조각", Kind.Evidence, 25f, 0f, 0f),
        };

        [MenuItem("Fantasia/Art/아이템 아이콘 찍기")]
        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            int oi = System.Array.IndexOf(args, "-only");
            var only = oi >= 0 && oi + 1 < args.Length ? new HashSet<string>(args[oi + 1].Split(',')) : null;

            ConfigureImporters();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.4f, 0.4f);
            RenderSettings.skybox = null;

            Directory.CreateDirectory(IconDir);
            Directory.CreateDirectory(EvidenceDir);
            var origin = new Vector3(0f, -10000f, 0f);
            var camGO = new GameObject("IconCamera");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 22f;
            cam.enabled = false;
            Light(new Vector3(40f, -35f, 0f), 1.15f, new Color(1f, 0.96f, 0.9f));   // 왼쪽 위 앞 (주광)
            Light(new Vector3(15f, 140f, 0f), 0.55f, new Color(0.85f, 0.9f, 1f));   // 오른쪽 뒤 (윤곽)
            Light(new Vector3(-20f, 20f, 0f), 0.25f, Color.white);                  // 아래 앞 (그늘 채움)

            var big = new RenderTexture(Render, Render, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            var small = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32);
            var report = new List<string>();
            var sheet = new List<Texture2D>();

            foreach (var item in Items)
            {
                if (only != null && !only.Contains(item.id)) continue;
                string dir = $"{ModelRoot}/Item_{item.id}";
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/Item_{item.id}.fbx");
                if (fbx == null) { report.Add($"{item.id}: 모델 없음"); continue; }
                var go = (GameObject)Object.Instantiate(fbx);
                go.transform.position = origin;
                go.transform.rotation = Quaternion.Euler(item.tilt, 180f + item.yaw, item.roll) * fbx.transform.rotation; // 가져올 때 세운 회전(Z 위 → Y 위) 위에 곱함. 앞면이 카메라(−z)를 보게
                var mat = new Material(Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave };
                mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/Item_{item.id}_Albedo.png");
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/Item_{item.id}_Normal.png");
                if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
                mat.SetFloat("_Glossiness", 0.35f);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;

                // 구도: 그림 범위를 감싸는 공이 화면에 꽉 차게 (아이템마다 같은 여백)
                var b = BoundsOf(go);
                float radius = Mathf.Max(0.001f, b.extents.magnitude);
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.86f; // 공보다 조금 당겨 빈 곳 줄임 (모서리만 살짝)
                var dirCam = Quaternion.Euler(18f, 0f, 0f) * Vector3.back; // 살짝 위에서
                cam.transform.position = b.center + dirCam * dist;
                cam.transform.LookAt(b.center);
                cam.nearClipPlane = dist * 0.05f;
                cam.farClipPlane = dist * 3f;
                cam.targetTexture = big;
                cam.Render();
                Graphics.Blit(big, small);

                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                var prev = RenderTexture.active;
                RenderTexture.active = small;
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = prev;
                AddOutline(tex);
                tex.Apply();

                string outPath = item.kind == Kind.Evidence ? $"{EvidenceDir}/Icon_{item.id}.png" : $"{IconDir}/Icon_{item.id}.png";
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                sheet.Add(tex);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mat);
                report.Add($"{item.id}: 크기 {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00}");
            }

            AssetDatabase.Refresh();
            foreach (var item in Items)
            {
                if (only != null && !only.Contains(item.id)) continue;
                string path = item.kind == Kind.Evidence ? $"{EvidenceDir}/Icon_{item.id}.png" : $"{IconDir}/Icon_{item.id}.png";
                ImportAsSprite(path);
                Assign(item.id, item.kind, path, report);
            }
            AssetDatabase.SaveAssets();
            SaveSheet(sheet);
            big.Release(); small.Release();
            Debug.Log("[ItemIconStudio]\n" + string.Join("\n", report));
        }

        private static void Light(Vector3 euler, float intensity, Color color)
        {
            var l = new GameObject("IconLight").AddComponent<Light>();
            l.type = LightType.Directional;
            l.transform.rotation = Quaternion.Euler(euler);
            l.intensity = intensity;
            l.color = color;
            l.shadows = LightShadows.None;
        }

        private static Bounds BoundsOf(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        // 어두운 칸 위에서도 모양이 보이게 바깥 2픽셀 어두운 테두리 (그림 뒤에)
        private static void AddOutline(Texture2D tex)
        {
            var src = tex.GetPixels32();
            var dst = (Color32[])src.Clone();
            const int r = 2;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    if (src[i].a >= 250) continue;
                    byte near = 0;
                    for (int dy = -r; dy <= r && near < 255; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= Size || yy >= Size || dx * dx + dy * dy > r * r + 1) continue;
                            byte a = src[yy * Size + xx].a;
                            if (a > near) near = a;
                        }
                    if (near == 0) continue;
                    float edge = near / 255f * 0.75f;            // 테두리 진하기
                    var c = src[i];
                    float a0 = c.a / 255f;
                    float outA = a0 + edge * (1f - a0);            // 그림 뒤에 깔기
                    if (outA <= 0f) continue;
                    float k = a0 / outA;
                    dst[i] = new Color32((byte)(c.r * k + 12 * (1 - k)), (byte)(c.g * k + 9 * (1 - k)), (byte)(c.b * k + 8 * (1 - k)), (byte)(outA * 255f));
                }
            tex.SetPixels32(dst);
        }

        private static void ImportAsSprite(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = Size;
            ti.textureCompression = TextureImporterCompression.Uncompressed; // 작은 그림이라 압축 얼룩 없이
            ti.SaveAndReimport();
        }

        private static void Assign(string id, Kind kind, string iconPath, List<string> report)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (sprite == null) { report.Add($"{id}: 아이콘 읽기 실패"); return; }
            switch (kind)
            {
                case Kind.Consumable:
                    var c = AssetDatabase.LoadAssetAtPath<ConsumableDefinition>($"Assets/_Project/Data/Items/Consumables/Consumable_{id}.asset");
                    if (c == null) { report.Add($"{id}: 소모품 데이터 없음"); return; }
                    c.Icon = sprite; EditorUtility.SetDirty(c);
                    break;
                case Kind.Armor:
                    var a = AssetDatabase.LoadAssetAtPath<ArmorDefinition>($"Assets/_Project/Data/Armor/Armor_{id}.asset");
                    if (a == null) { report.Add($"{id}: 장비 데이터 없음"); return; }
                    a.Icon = sprite; EditorUtility.SetDirty(a);
                    break;
            }
        }

        // 확인용 한 장 (Logs/item_icons.png): 어두운 칸 바탕 위에 7개씩
        private static void SaveSheet(List<Texture2D> icons)
        {
            if (icons.Count == 0) return;
            const int cols = 7, cell = Size + 16;
            int rows = (icons.Count + cols - 1) / cols;
            var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, false);
            var bg = new Color32(52, 34, 26, 255);
            var fill = Enumerable.Repeat(bg, sheet.width * sheet.height).ToArray();
            sheet.SetPixels32(fill);
            for (int i = 0; i < icons.Count; i++)
            {
                int ox = (i % cols) * cell + 8, oy = (rows - 1 - i / cols) * cell + 8;
                var px = icons[i].GetPixels32();
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        var s = px[y * Size + x];
                        var d = sheet.GetPixel(ox + x, oy + y);
                        float a = s.a / 255f;
                        sheet.SetPixel(ox + x, oy + y, new Color(s.r / 255f * a + d.r * (1 - a), s.g / 255f * a + d.g * (1 - a), s.b / 255f * a + d.b * (1 - a), 1f));
                    }
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "../Logs/item_icons.png"), sheet.EncodeToPNG());
        }

        // 받은 모델 가져오기 설정: 움직임·재질 없음, 굴곡 지도는 NormalMap, 텍스처 1024
        private static void ConfigureImporters()
        {
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { ModelRoot }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetImporter.GetAtPath(path) is not ModelImporter mi) continue;
                if (mi.animationType == ModelImporterAnimationType.None && mi.materialImportMode == ModelImporterMaterialImportMode.None) continue;
                mi.animationType = ModelImporterAnimationType.None;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.SaveAndReimport();
            }
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelRoot }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                bool normal = path.EndsWith("_Normal.png");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType == type && ti.maxTextureSize == 1024) continue;
                ti.textureType = type;
                ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }
        }
    }
}
