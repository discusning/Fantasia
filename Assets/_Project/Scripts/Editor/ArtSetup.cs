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
    public static partial class ArtSetup
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
            ["Model_GargoyleLarva"] = (2.0f, true),   // 11/6 받은 움직임이 사람형 근육 커브 + 디자인 원본 설정도 사람형
            ["Model_SpiderLarva"] = (0.9f, false),   // 11/6 받음 (옆으로 넓은 몸, 키 0.9m = 임시 인형과 같음)
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
            ["Model_SpiderLarva"] = "Assets/_Project/Data/Enemies/Enemy_연성독거미유체.asset",
        };

        // 보드 배치물: 역할 이름 → (원본 경로, Meshy 소품이면 true(전용 재질·가벼운 메시), 맞출 크기 종류, 값 m)
        // 11/11: Meshy FBX는 Unity가 이미 Y-up으로 세워서 가져옴 → 예전 −90도 눕히기를 빼니 나무·우물 등이 바로 섬
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
            // 11/10 기획 2A: 카메라 쪽 앞줄 막힌 칸은 높은 집 대신 낮은 나무·돌담 (뒤 칸·적 표시를 가리지 않게)
            ("Block_LowTrees", Root + "/Props/Prop_Trees_A/Prop_Trees_A.fbx", true, "h", 0.75f),
            ("Block_LowStone", Root + "/Props/Prop_StoneWall/Prop_StoneWall.fbx", true, "w", 1.3f),
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
            BuildDicePrefab(report);
            BuildBattlePrefabs(report);
            AssetDatabase.SaveAssets();
            StreetLampSetup.Run(); // 가로등 프리팹에 불 위치·발광 다시 넣음 (위에서 프리팹을 새로 저장하므로)
            BoardFadeSetup.Run(); // 11/12 보드 구조물 반투명 재질
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
                int max = path.Contains("/Characters/") && path.EndsWith("_Albedo.png") ? 2048 : 1024; // 최적화 (11/6): 인물 색만 2048, 굴곡·금속 지도는 화면 크기상 1024면 충분
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
                // 받은 움직임 컨트롤러(Idle·Attack·Hit·Death, 11/6)가 있으면 그것, 없으면 사람형만 "팔 내린 서 있기" 자세
                var motion = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"{Root}/Characters/Animations/{name}/{name}.controller");
                bool human = avatar != null && avatar.isHuman && avatar.isValid;
                if (motion != null) animator.runtimeAnimatorController = motion;
                else if (human) animator.runtimeAnimatorController = pose;
                if (human) model.AddComponent<Fantasia.UI.ModelGrounding>(); // 자세 적용 뒤 발을 바닥에 맞춤
                if (motion != null) model.AddComponent<Fantasia.UI.CharacterMotion>();
                rig += motion != null ? " · 움직임 4종" : " · 움직임 없음";

                // 크기·발 위치는 서 있는 자세 기준으로 맞춘다 (T자 자세 팔 폭은 무시)
                var b = Bounds(model);
                float scale = spec.height / Mathf.Max(b.size.y, 0.01f);
                model.transform.localScale *= scale;
                b = Bounds(model);
                model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);

                UseNormalMap(model, name);
                if (name == "Model_ShadowReaper" && AssetDatabase.LoadAssetAtPath<GameObject>($"{WeaponRoot}/Prop_Weapon_Scythe/Prop_Weapon_Scythe.fbx") == null) AttachScythe(model, report); // 새 낫 파일이 있으면 AttachWeapons가 붙임
                AttachWeapons(model, name, report);

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
            foreach (var mf in scythe.GetComponentsInChildren<MeshFilter>())
                mf.sharedMesh = Lighter(mf.sharedMesh, BoardSingleTris, $"{Root}/Props/Prop_Scythe/Prop_Scythe_Board.asset");
            scythe.name = "Scythe";
            scythe.transform.SetParent(hand, false);
            // 원본은 누워 있고 cm 단위(FBX 루트 배율 100)라, 그 배율을 덮어쓰면 낫이 2cm가 됨 (11/8 수정).
            // 메시 실제 길이로 배율을 계산해 월드 기준 약 2.2m로 세운다.
            float world = hand.lossyScale.y;
            var size = scythe.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size;
            float meshLen = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            scythe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 날이 위로 (-90이면 서 있을 때 날이 발밑)
            scythe.transform.localScale = Vector3.one * (2.2f / (meshLen * world));
            scythe.transform.localPosition = Vector3.zero;
            report.Add($"  낫: {hand.name}에 붙임 (길이 2.2m)");
        }

        // 아군 3명은 Normal(표면 굴곡) 텍스처가 같이 왔는데 재질에 연결이 안 되어 와서 연결한다 (안 쓰면 파일만 차지).
        private static void UseNormalMap(GameObject model, string name)
        {
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Characters/Textures/{name}_Normal.png");
            if (nrm == null) return;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mat = r.sharedMaterial;
                if (mat == null || !mat.HasProperty("_BumpMap") || mat.GetTexture("_BumpMap") != null) continue;
                mat.SetTexture("_BumpMap", nrm);
                mat.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(mat);
            }
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
                if (lying)
                {
                    string propName = Path.GetFileNameWithoutExtension(srcPath);
                    ApplyPropMaterial(model, propName);
                    // Meshy 소품은 3만 면 → 보드에서는 멀리서 작게 보이므로 가벼운 메시로 (나무는 여러 칸에 반복돼 더 줄임)
                    int budget = role == "Block_Trees" || role.StartsWith("Block_Low") ? BoardRepeatedTris : BoardSingleTris;
                    foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                        mf.sharedMesh = Lighter(mf.sharedMesh, budget, $"{Root}/Props/{propName}/{propName}_Board.asset");
                }
                foreach (var col in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                foreach (var mat in model.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
                {
                    mat.enableInstancing = true; // 같은 칸·건물이 여러 개 → 한 번에 그리기
                    if (role.StartsWith("Tile")) mat.color = Fantasia.Board.HexTile.ArtBaseColor; // 기본 칸은 색 덮어쓰기 없이 이 색
                    EditorUtility.SetDirty(mat);
                }
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


        // ── 전투 배경 (2026-11-11 일반 전투 런던 거리 프로토타입) ──
        // 이미 받은 집·나무·울타리·돌 아치·돌담을 실제 크기(높이 m)로 맞춰 Resources/BattleArt에. 놓는 곳은 Combat/BattleScenery.
        private const string BattleArtDir = "Assets/_Project/Resources/BattleArt";
        private static readonly (string role, string src, bool prop, float height)[] BattleProps =
        {
            ("House_A", Root + "/Buildings/Prop_House_A.prefab", false, 6.5f),
            ("House_B", Root + "/Buildings/Prop_House_B.prefab", false, 6f),
            ("House_C", Root + "/Props/Prop_House_C/Prop_House_C.fbx", true, 6.5f),
            ("Trees_A", Root + "/Props/Prop_Trees_A/Prop_Trees_A.fbx", true, 4.5f),
            ("Trees_B", Root + "/Props/Prop_Trees_B/Prop_Trees_B.fbx", true, 5f),
            ("Fence", Root + "/Props/Prop_Fence/Prop_Fence.fbx", true, 1.3f),
            ("StoneArch", Root + "/Props/Prop_StoneArch/Prop_StoneArch.fbx", true, 4.5f),
            ("StoneWall", Root + "/Props/Prop_StoneWall/Prop_StoneWall.fbx", true, 1.2f),
            // 11/12 C1 런던 시내 소품 (받은 6종). 크기: 양수 = 높이(m), 음수 = 가로 폭(m) — 하수구·돌길처럼 납작한 것
            ("Cart", Root + "/Props/Prop_Cart/Prop_Cart.fbx", true, 1.5f),
            ("WoodFence", Root + "/Props/Prop_WoodFence/Prop_WoodFence.fbx", true, 1.2f),
            ("Sewer", Root + "/Props/Prop_Sewer/Prop_Sewer.fbx", true, -1.3f),
            ("Cobble", Root + "/Props/Prop_Cobble/Prop_Cobble.fbx", true, -4f),
            ("WallLamp", Root + "/Props/Prop_WallLamp/Prop_WallLamp.fbx", true, 1.1f),
            ("StreetLamp", Root + "/Props/Prop_StreetLamp/Prop_StreetLamp.fbx", true, 3.8f),
            // 11/13 C2 폐검시소 (아벨 모로전) 받은 8종. 검시대·시체는 길이(음수)로, 시체는 검시대 위에 올림 (BattleScenery.Morgue)
            ("Morgue_Table", Root + "/Props/Prop_Morgue_Table/Prop_Morgue_Table.fbx", true, -2.3f),
            ("Morgue_Corpse", Root + "/Props/Prop_Morgue_Corpse/Prop_Morgue_Corpse.fbx", true, -1.8f),
            ("Morgue_Skeleton", Root + "/Props/Prop_Morgue_Skeleton/Prop_Morgue_Skeleton.fbx", true, 1.9f),
            ("Morgue_Cabinet", Root + "/Props/Prop_Morgue_Cabinet/Prop_Morgue_Cabinet.fbx", true, 2.1f),
            ("Morgue_Desk", Root + "/Props/Prop_Morgue_Desk/Prop_Morgue_Desk.fbx", true, 1.0f),
            ("Morgue_Basin", Root + "/Props/Prop_Morgue_Basin/Prop_Morgue_Basin.fbx", true, 0.85f),
            ("Morgue_Door", Root + "/Props/Prop_Morgue_Door/Prop_Morgue_Door.fbx", true, 2.7f),
            ("Morgue_Screen", Root + "/Props/Prop_Morgue_Screen/Prop_Morgue_Screen.fbx", true, 1.8f),
        };

        private static void BuildBattlePrefabs(List<string> report)
        {
            Directory.CreateDirectory(BattleArtDir);
            foreach (var (role, srcPath, prop, height) in BattleProps)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
                if (src == null) { report.Add($"전투 배경 {role}: 원본 없음 {srcPath}"); continue; }
                var root = new GameObject(role);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
                model.transform.SetParent(root.transform, false);
                if (prop)
                {
                    string propName = Path.GetFileNameWithoutExtension(srcPath);
                    ApplyPropMaterial(model, propName);
                    foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                        mf.sharedMesh = Lighter(mf.sharedMesh, BoardSingleTris, $"{Root}/Props/{propName}/{propName}_Battle.asset");
                }
                foreach (var col in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var b = Bounds(model);
                model.transform.localScale *= height >= 0f ? height / Mathf.Max(b.size.y, 0.01f) : -height / Mathf.Max(Mathf.Max(b.size.x, b.size.z), 0.01f);
                b = Bounds(model);
                model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                b = Bounds(model);
                PrefabUtility.SaveAsPrefabAsset(root, $"{BattleArtDir}/{role}.prefab");
                Object.DestroyImmediate(root);
                report.Add($"전투 배경 {role}: {b.size.x:F1}×{b.size.y:F1}×{b.size.z:F1}m");
            }
        }
        // ── 보드 주사위 (2026-11-06) ── 가운데 기준·한 변 1m로 맞춤 → Board/BoardDice가 원하는 크기로 키움
        private static void BuildDicePrefab(List<string> report)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Dice/Dice_01.prefab");
            if (src == null) { report.Add("주사위: 원본 없음"); return; }
            var root = new GameObject("Dice");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            model.transform.SetParent(root.transform, false);
            foreach (var col in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            var b = Bounds(model);
            model.transform.localScale *= 1f / Mathf.Max(b.size.x, b.size.y, b.size.z, 0.01f);
            b = Bounds(model);
            model.transform.localPosition -= b.center;
            PrefabUtility.SaveAsPrefabAsset(root, $"{BoardArtDir}/Dice.prefab");
            Object.DestroyImmediate(root);
            report.Add("주사위: 한 변 1m, 가운데 기준");
        }

        // ── 가벼운 메시 (2026-11-06 최적화) ──
        // 보드 소품 면 수 상한: 한 번씩 놓이는 것(거점 소품·우물·출구 등) / 여러 칸에 반복되는 것(나무)
        private const int BoardSingleTris = 8000;
        private const int BoardRepeatedTris = 4000;

        // Unity 메시 단순화(Mesh LOD)로 단계를 만든 뒤 maxTris 이하인 첫 단계만 떼어 새 메시로 저장한다.
        // 원본 FBX는 그대로 (다시 돌리면 같은 결과). 하위 메시 1개(Meshy 소품)만 다룬다.
        private static Mesh Lighter(Mesh src, int maxTris, string path)
        {
            if (src == null || src.subMeshCount != 1 || src.triangles.Length / 3 <= maxTris) return src;
            var work = Object.Instantiate(src);
            MeshLodUtility.GenerateMeshLods(work, 12);
            int level = 0;
            for (int i = 0; i < work.lodCount; i++) { level = i; if (work.GetLod(0, i).indexCount / 3 <= maxTris) break; }
            var range = work.GetLod(0, level);
            int[] all;
            using (var data = Mesh.AcquireReadOnlyMeshData(work))
            {
                var md = data[0];
                all = md.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16
                    ? md.GetIndexData<ushort>().Select(x => (int)x).ToArray()
                    : md.GetIndexData<uint>().Select(x => (int)x).ToArray();
            }
            // 쓰는 꼭짓점만 남기고 번호를 다시 매김
            var map = new Dictionary<int, int>();
            var tri = new int[range.indexCount];
            for (int k = 0; k < range.indexCount; k++)
            {
                int v = all[range.indexStart + k];
                if (!map.TryGetValue(v, out int n)) { n = map.Count; map[v] = n; }
                tri[k] = n;
            }
            var order = new int[map.Count];
            foreach (var kv in map) order[kv.Value] = kv.Key;
            T[] Pick<T>(T[] a) => a.Length == 0 ? a : order.Select(i => a[i]).ToArray();
            var uv = new List<Vector2>();
            work.GetUVs(0, uv);
            var m = new Mesh { name = src.name + "_Board" };
            if (order.Length > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = Pick(work.vertices);
            m.normals = Pick(work.normals);
            m.tangents = Pick(work.tangents);
            if (uv.Count > 0) m.SetUVs(0, order.Select(i => uv[i]).ToList());
            m.colors = Pick(work.colors);
            m.triangles = tri;
            m.RecalculateBounds();
            Object.DestroyImmediate(work);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
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
