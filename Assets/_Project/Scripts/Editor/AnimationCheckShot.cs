using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Fantasia.Editor
{
    // 받은 움직임(Art/Models/Characters/Animations) 확인용 사진 (2026-11-06).
    // 모델마다 한 줄: Idle 중간 · Attack 0.4초 · Hit 0.2초 · Death 끝. 모델은 받은 FBX 그대로, 키를 같게 맞춰 비교.
    // 결과: Logs/anim_check.png + 콘솔에 모델별 클립 길이·움직인 뼈 수.
    // -prefab: FBX 대신 게임용 프리팹(Art/Prefabs, 낫 등 붙인 것 포함)을 찍음 (2026-11-08).
    public static class AnimationCheckShot
    {
        private const string Root = "Assets/_Project/Art/Models/Characters";
        private static float _yaw = 30f, _pad = 1f;
        private static readonly string[] AllModels =
            { "Detective", "PlagueDoctor", "Sniper", "PlagueRat", "SpiderLarva", "GargoyleLarva", "ShadowHound", "ShadowReaper", "AbelMoreau" };
        private static readonly (string clip, float t)[] DefaultPoses = { ("Idle", 1f), ("Attack", 0.25f), ("Attack", 0.5f), ("Attack", 0.75f), ("Hit", 0.2f), ("Death", -1f) }; // 음수: 길이 비율

        public static void Capture()
        {
            // 옵션 (11/10, 디자인 요청 자료용): -only ShadowReaper · -cell 500 · -poses "Idle:1,Attack:0.2,..." · -yaw 30 · -out <png>
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string k) { int i = System.Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var Models = Arg("-only") is { } only ? only.Split(",") : AllModels;
            var Poses = Arg("-poses") is { } ps ? ps.Split(",").Select(p => { var kv = p.Split(":"); return (clip: kv[0], t: float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture)); }).ToArray() : DefaultPoses;
            int Cell = int.TryParse(Arg("-cell"), out int c) ? c : 300;
            _yaw = float.TryParse(Arg("-yaw"), out float y) ? y : 30f;
            _pad = float.TryParse(Arg("-pad"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pd) ? pd : 1f; // 1보다 크면 여백 넓게
            string outPath = Arg("-out") ?? Path.Combine(Application.dataPath, "../Logs/anim_check.png");
            var sheet = new Texture2D(Cell * (Poses.Length + 1), Cell * Models.Length, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(Cell, Cell, 24) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
            var report = new List<string>();

            for (int row = 0; row < Models.Length; row++)
            {
                string name = "Model_" + Models[row];
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var light = new GameObject("L").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
                RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
                var cam = new GameObject("C").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.17f, 0.16f, 0.2f);
                cam.orthographic = true;
                cam.targetTexture = rt;

                bool usePrefab = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-prefab") >= 0;
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(usePrefab ? $"Assets/_Project/Art/Prefabs/{name}.prefab" : $"{Root}/FBX/{name}.fbx");
                var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"{Root}/Animations/{name}/{name}.controller");
                if (fbx == null) { report.Add($"{name}: FBX 없음"); continue; }
                var go = (GameObject)Object.Instantiate(fbx);
                var animator = go.GetComponentInChildren<Animator>() ?? go.AddComponent<Animator>();
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false; // 프리팹의 발 맞춤 등이 끼어들지 않게
                animator.runtimeAnimatorController = null; // 클립을 직접 재생 (컨트롤러 기본 상태 Idle이 덮어쓰지 않게). 컨트롤러는 있는지만 확인
                bool hasCtrl = ctrl != null;
                var avatar = animator.avatar;
                string rig = (hasCtrl ? "" : "컨트롤러 없음 ") + (avatar == null ? "아바타 없음" : avatar.isHuman ? (avatar.isValid ? "사람형" : "사람형(오류)") : "일반");

                // 기준 자세(T자세)에서 크기·중심
                var b = Bounds(go);
                float size = Mathf.Max(b.size.y, b.size.x * 0.8f, b.size.z * 0.8f) * 0.62f * _pad;
                var line = new List<string>();

                // 맨 왼쪽: 움직임 없는 기본 자세
                cam.orthographicSize = size;
                Aim(cam, b);
                cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, Cell, Cell), 0, Cell * (Models.Length - 1 - row));

                for (int col = 0; col < Poses.Length; col++)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Root}/Animations/{name}/{name}@{Poses[col].clip}.anim");
                    if (clip == null) { line.Add($"{Poses[col].clip} 없음"); continue; }
                    float t = Poses[col].t < 0 ? clip.length * -Poses[col].t : Mathf.Min(Poses[col].t, clip.length);
                    // 게임 재생과 같은 경로(Playable)로 그 시점 자세를 계산 — 사람형 근육 커브까지 적용
                    AnimationMode.StartAnimationMode();
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, clip, t);
                    AnimationMode.EndSampling();
                    // 배치 모드에서는 뼈가 움직여도 피부(스킨) 갱신이 늦어서, 지금 뼈 자세를 메시로 구워 그 메시를 찍는다
                    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
                    var baked = new List<GameObject>();
                    foreach (var smr in smrs)
                    {
                        var mesh = new Mesh();
                        smr.BakeMesh(mesh, true);
                        var bg = new GameObject("baked");
                        bg.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                        bg.AddComponent<MeshFilter>().sharedMesh = mesh;
                        bg.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                        baked.Add(bg);
                        smr.enabled = false;
                    }
                    var nb = Bounds(baked.Count > 0 ? baked[0] : go);
                    cam.orthographicSize = size;
                    Aim(cam, b);
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, Cell, Cell), Cell * (col + 1), Cell * (Models.Length - 1 - row));
                    foreach (var bg in baked) Object.DestroyImmediate(bg);
                    foreach (var smr in smrs) smr.enabled = true;
                    AnimationMode.StopAnimationMode();
                    line.Add($"{Poses[col].clip} {clip.length:0.0}초{(clip.isLooping ? " 반복" : "")} 높이 {nb.size.y / b.size.y:0.00}배");
                }
                report.Add($"{name} [{rig}] " + string.Join(" · ", line));
            }
            sheet.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            foreach (var r in report) Debug.Log("[AnimationCheck] " + r);
        }

        // 모델 정면(+Z)에서 오른쪽으로 30도 · 위에서 10도 비스듬히
        private static void Aim(Camera cam, Bounds b)
        {
            cam.transform.position = b.center + Quaternion.Euler(-10f, _yaw, 0f) * Vector3.forward * 20f;
            cam.transform.LookAt(b.center);
        }

        private static Bounds Bounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
