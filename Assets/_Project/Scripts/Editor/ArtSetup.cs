using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fantasia.Data;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Fantasia.Editor
{
    // 디자인에서 받은 3D 에셋(2026-11-06, Docs/Art/Model_Request_1105) 연결.
    // ① 가져오기 설정(텍스처 크기·노멀 맵·리그 종류) ② 게임 크기에 맞춘 감싸기 프리팹 ③ 직업·적 데이터 Model 칸 연결.
    // 다시 받은 파일로 바꾼 뒤 이 메뉴를 한 번 더 돌리면 된다.
    public static class ArtSetup
    {
        private const string Root = "Assets/_Project/Art/Models";
        private const string PrefabDir = "Assets/_Project/Art/Prefabs";
        private const string BoardArtDir = "Assets/_Project/Resources/BoardArt";

        // 모델 이름 → (게임 속 키 m, 사람형 여부). 키는 PlaceholderFigure / CharacterModels와 같은 값.
        private static readonly Dictionary<string, (float height, bool human)> Characters = new()
        {
            ["Model_Detective"] = (2.4f, true),
            ["Model_PlagueDoctor"] = (2.4f, true),
            ["Model_Sniper"] = (2.4f, true),
            ["Model_AbelMoreau"] = (3.6f, true),
            ["Model_ShadowReaper"] = (2.6f, true),
            ["Model_PlagueRat"] = (1.1f, true),   // 받은 모델이 두 발로 선 쥐인간 → 0.8m는 너무 작아 1.1m
            ["Model_ShadowHound"] = (1.3f, false),
            ["Model_GargoyleLarva"] = (2.0f, false),
        };

        private static readonly Dictionary<string, string> JobAssets = new()
        {
            ["Model_Detective"] = "Assets/_Project/Data/Jobs/Job_명탐정.asset",
            ["Model_PlagueDoctor"] = "Assets/_Project/Data/Jobs/Job_역병의사.asset",
            ["Model_Sniper"] = "Assets/_Project/Data/Jobs/Job_저격수.asset",
        };
        private static readonly Dictionary<string, string> EnemyAssets = new()
        {
            ["Model_AbelMoreau"] = "Assets/_Project/Data/Enemies/Enemy_검시관아벨모로.asset",
            ["Model_ShadowReaper"] = "Assets/_Project/Data/Enemies/Enemy_인조그림자사신.asset",
            ["Model_PlagueRat"] = "Assets/_Project/Data/Enemies/Enemy_역병쥐.asset",
            ["Model_ShadowHound"] = "Assets/_Project/Data/Enemies/Enemy_그림자사냥개.asset",
            ["Model_GargoyleLarva"] = "Assets/_Project/Data/Enemies/Enemy_가고일유체.asset",
        };

        // 보드 배치물: 역할 이름 → (원본 경로, Meshy처럼 누워 있으면 true, 맞출 크기 종류, 값 m)
        // fit: "w" = 가로·세로 중 큰 쪽을 값에 맞춤 (칸 안에 들어가게) / "h" = 높이를 값에 맞춤
        private static readonly (string role, string src, bool lying, string fit, float size)[] BoardProps =
        {
            ("Tile_Road_A", Root + "/Tiles/Tile_Road_A.prefab", false, "-", 1f),
            ("Tile_Road_B", Root + "/Tiles/Tile_Road_B.prefab", false, "-", 1f),
            ("Hub", Root + "/Buildings/Prop_Hub_Greenwich.prefab", false, "w", 2.3f),
            ("Boss", Root + "/Buildings/Prop_Boss_Morgue.prefab", false, "w", 2.3f),
            ("Block_A", Root + "/Buildings/Prop_House_A.prefab", false, "w", 1.45f),
            ("Block_B", Root + "/Buildings/Prop_House_B.prefab", false, "w", 1.45f),
            ("Block_Trees", Root + "/Props/Prop_Trees_A/Prop_Trees_A.fbx", true, "w", 1.55f),
            ("Treasure", Root + "/Props/Prop_Treasure/Prop_Treasure.fbx", true, "w", 0.8f),
            ("Recovery", Root + "/Props/Prop_Well/Prop_Well.fbx", true, "w", 1.35f),
            ("Event_FortuneTeller", Root + "/Props/Prop_FortuneTent/Prop_FortuneTent.fbx", true, "w", 1.45f),
            ("Event_Fountain", Root + "/Props/Prop_Fountain/Prop_Fountain.fbx", true, "w", 1.4f),
            ("Exit", Root + "/Props/Prop_ExitGate/Prop_ExitGate.fbx", true, "w", 1.5f),
        };

        [MenuItem("Fantasia/Art/Set Up Received 3D Assets")]
        public static void Run()
        {
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(BoardArtDir);
            ConfigureTextures();
            ConfigureModels();
            AssetDatabase.Refresh();
            var report = new List<string>();
            BuildCharacterPrefabs(report);
            BuildBoardPrefabs(report);
            AssetDatabase.SaveAssets();
            File.WriteAllLines(Path.Combine(Application.dataPath, "../Logs/art_setup_report.txt"), report);
            Debug.Log("[ArtSetup] 완료\n" + string.Join("\n", report));
        }

        // 원본 PNG는 4096까지 와서, 게임 안에서는 인물 2048·나머지 1024로 줄여 쓴다 (파일은 그대로).
        private static void ConfigureTextures()
        {
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                bool changed = false;
                int max = path.Contains("/Characters/") ? 2048 : 1024;
                if (ti.maxTextureSize != max) { ti.maxTextureSize = max; changed = true; }
                bool normal = Path.GetFileNameWithoutExtension(path).EndsWith("_Normal");
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                if (ti.textureCompression != TextureImporterCompression.Compressed) { ti.textureCompression = TextureImporterCompression.Compressed; changed = true; }
                if (changed) ti.SaveAndReimport();
            }
        }

        private static void ConfigureModels()
        {
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetImporter.GetAtPath(path) is not ModelImporter mi) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool changed = false;
                if (Characters.TryGetValue(name, out var c))
                {
                    var type = c.human ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                    if (mi.animationType != type) { mi.animationType = type; changed = true; }
                    if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
                }
                else if (path.Contains("/Props/"))
                {
                    if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; changed = true; }
                    if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
                }
                if (changed) mi.SaveAndReimport();
            }
        }

        // ── 인물 ──
        private static void BuildCharacterPrefabs(List<string> report)
        {
            var pose = HumanoidStandPose();
            foreach (var (name, spec) in Characters)
            {
                string fbx = $"{Root}/Characters/FBX/{name}.fbx";
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (src == null) { report.Add($"{name}: 파일 없음"); continue; }
                var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
                string rig = avatar == null ? "뼈대 없음" : $"{(avatar.isHuman ? "사람형" : "일반")} 뼈대 {(avatar.isValid ? "정상" : "오류")}";

                var root = new GameObject(name);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
                model.transform.SetParent(root.transform, false);
                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // 화면 밖 초상 촬영장에서도 자세 유지
                animator.applyRootMotion = false;
                if (avatar != null && avatar.isHuman && avatar.isValid) { animator.runtimeAnimatorController = pose; model.AddComponent<Fantasia.UI.ModelGrounding>(); } // 자세 적용 뒤 발을 바닥에 맞춤

                // 크기·발 위치는 서 있는 자세 기준으로 맞춘다 (T자 자세 팔 폭은 무시)
                var b = Bounds(model);
                float scale = spec.height / Mathf.Max(b.size.y, 0.01f);
                model.transform.localScale *= scale;
                b = Bounds(model);
                model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);

                if (name == "Model_ShadowReaper") AttachScythe(model, report);

                string prefabPath = $"{PrefabDir}/{name}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
                Assign(name, prefab);
                report.Add($"{name}: {rig}, 키 {b.size.y:F2}m → {spec.height}m (배율 {scale:F2}), 연결 {(JobAssets.ContainsKey(name) || EnemyAssets.ContainsKey(name) ? "완료" : "-")}");
            }
        }

        // 사람형 뼈대에 바로 쓸 수 있는 "팔 내린 서 있기" 자세 — 받은 모델에 움직임이 없어 T자로 서는 문제를 임시로 막고,
        // 동시에 움직임 파일이 오면 같은 방식(Animator)으로 재생되는지 확인하는 용도.
        private static AnimatorController HumanoidStandPose()
        {
            string clipPath = PrefabDir + "/Pose_StandArmsDown.anim";
            var clip = new AnimationClip { name = "Pose_StandArmsDown" };
            void Curve(string muscle, float v) => AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(Animator), muscle), AnimationCurve.Constant(0f, 1f, v));
            // 사람형 움직임은 몸 중심 높이(RootT.y, 키 비율 단위)가 없으면 0 = 몸이 바닥까지 내려감 → 서 있는 높이 1
            Curve("RootT.x", 0f); Curve("RootT.y", 1f); Curve("RootT.z", 0f);
            Curve("RootQ.x", 0f); Curve("RootQ.y", 0f); Curve("RootQ.z", 0f); Curve("RootQ.w", 1f);
            Curve("Left Arm Down-Up", -0.55f); Curve("Right Arm Down-Up", -0.55f);
            Curve("Left Arm Front-Back", 0.15f); Curve("Right Arm Front-Back", 0.15f);
            Curve("Left Forearm Stretch", 0.75f); Curve("Right Forearm Stretch", 0.75f);
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.DeleteAsset(clipPath);
            AssetDatabase.CreateAsset(clip, clipPath);
            string ctrlPath = PrefabDir + "/Pose_StandArmsDown.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            return AnimatorController.CreateAnimatorControllerAtPathWithClip(ctrlPath, clip);
        }

        private static void AttachScythe(GameObject model, List<string> report)
        {
            // 사람형 뼈대 연결표에서 "RightHand"에 해당하는 실제 뼈 이름을 찾는다 (Meshy 뼈 이름은 모델마다 다를 수 있음)
            var avatar = model.GetComponent<Animator>()?.avatar;
            string boneName = avatar != null && avatar.isHuman ? avatar.humanDescription.human.FirstOrDefault(h => h.humanName == "RightHand").boneName : null;
            var hand = boneName == null ? null : model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == boneName);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Props/Prop_Scythe/Prop_Scythe.fbx");
            if (hand == null || src == null) { report.Add($"  낫: 붙이지 못함 (손 뼈 {(hand == null ? "없음" : "있음")})"); return; }
            var scythe = (GameObject)PrefabUtility.InstantiatePrefab(src);
            ApplyPropMaterial(scythe, "Prop_Scythe");
            scythe.name = "Scythe";
            scythe.transform.SetParent(hand, false);
            // 원본은 0.95m로 누워 있음 → 세우고 사신 키에 맞게 (월드 기준 약 2.2m)
            float world = hand.lossyScale.y;
            scythe.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            scythe.transform.localScale = Vector3.one * (2.3f / world);
            scythe.transform.localPosition = Vector3.zero;
            report.Add($"  낫: {hand.name}에 붙임");
        }

        private static void Assign(string name, GameObject prefab)
        {
            if (JobAssets.TryGetValue(name, out var jp) && AssetDatabase.LoadAssetAtPath<JobDefinition>(jp) is { } job) { job.Model = prefab; EditorUtility.SetDirty(job); }
            if (EnemyAssets.TryGetValue(name, out var ep) && AssetDatabase.LoadAssetAtPath<EnemyDefinition>(ep) is { } enemy) { enemy.Model = prefab; EditorUtility.SetDirty(enemy); }
        }

        // ── 보드 ──
        private static void BuildBoardPrefabs(List<string> report)
        {
            foreach (var (role, srcPath, lying, fit, size) in BoardProps)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
                if (src == null) { report.Add($"보드 {role}: 원본 없음 {srcPath}"); continue; }
                var root = new GameObject(role);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
                model.transform.SetParent(root.transform, false);
                if (lying) { model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * model.transform.localRotation; ApplyPropMaterial(model, Path.GetFileNameWithoutExtension(srcPath)); }
                foreach (var col in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = role.StartsWith("Tile") ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On; }
                var b = Bounds(model);
                float scale = fit == "w" ? size / Mathf.Max(b.size.x, b.size.z, 0.01f) : fit == "h" ? size / Mathf.Max(b.size.y, 0.01f) : 1f;
                model.transform.localScale *= scale;
                b = Bounds(model);
                model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                b = Bounds(model);
                int tris = model.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh ? m.sharedMesh.triangles.Length / 3 : 0);
                PrefabUtility.SaveAsPrefabAsset(root, $"{BoardArtDir}/{role}.prefab");
                Object.DestroyImmediate(root);
                report.Add($"보드 {role}: {b.size.x:F2}×{b.size.y:F2}×{b.size.z:F2}m, 면 {tris}");
            }
        }

        // Meshy 소품은 텍스처가 FBX에 연결되어 오지 않아 같은 폴더 PNG로 재질을 만든다.
        private static void ApplyPropMaterial(GameObject model, string name)
        {
            string dir = $"{Root}/Props/{name}";
            string matPath = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard")) { enableInstancing = true };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_Albedo.png");
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_Normal.png");
            if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Glossiness", 0.15f);
            EditorUtility.SetDirty(mat);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
        }

        private static Bounds Bounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
