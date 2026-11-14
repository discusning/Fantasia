using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fantasia.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/14 손가락 쥐기(HandGrip) 확인 도구 — 디자인 손가락 본(origin/3D a9c2b17) 시험.
    //  Analyze: 동작마다 어느 손이 얼마나 움직이는지(서 있을 때 대비 위치 변화·높이) 콘솔에 적음
    //  Capture: 모델마다 한 장 — 줄 = 동작 시점, 칸 = 오른손 전/후 · 왼손 전/후 · 오른손 게임 시점(뒤 위에서)
    // 실행: -executeMethod Fantasia.Editor.GripCheckShot.Analyze | Capture  [-only Detective,Sniper] [-outdir <폴더>]
    public static class GripCheckShot
    {
        private const string Root = "Assets/_Project/Art/Models/Characters";
        private static readonly string[] Models = { "Detective", "Sniper", "PlagueDoctor", "AbelMoreau", "ShadowReaper" };
        private static string Arg(string k) { var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        public static void Analyze()
        {
            foreach (var m in Arg("-only")?.Split(',') ?? Models)
            {
                string name = "Model_" + m;
                var go = Spawn(name, out var animator);
                if (go == null) continue;
                var hr = Find(go, "Hand.R"); var hl = Find(go, "Hand.L");
                var idle = Clip(name, "Idle");
                Sample(animator, idle, idle != null ? idle.length * 0.5f : 0f);
                Vector3 r0 = go.transform.InverseTransformPoint(hr.position), l0 = go.transform.InverseTransformPoint(hl.position);
                foreach (var clipName in new[] { "Attack", "Skill", "Item", "Heal", "Hit", "Defend", "Dodge", "Victory", "Death", "Revive", "Recover", "Announce", "SelfHeal", "Walk" })
                {
                    var clip = Clip(name, clipName);
                    if (clip == null) continue;
                    var line = new List<string>();
                    float rMax = 0f, lMax = 0f;
                    for (int i = 0; i <= 8; i++)
                    {
                        float t = clip.length * i / 8f;
                        Sample(animator, clip, t);
                        var r = go.transform.InverseTransformPoint(hr.position); var l = go.transform.InverseTransformPoint(hl.position);
                        rMax = Mathf.Max(rMax, (r - r0).magnitude); lMax = Mathf.Max(lMax, (l - l0).magnitude);
                        line.Add($"{i / 8f:0.00}: R({r.x:0.00},{r.y:0.00},{r.z:0.00}) L({l.x:0.00},{l.y:0.00},{l.z:0.00})");
                    }
                    Debug.Log($"[GripAnalyze] {name} {clipName} {clip.length:0.00}초 · 오른손 최대 이동 {rMax:0.00} · 왼손 {lMax:0.00}\n   " + string.Join("\n   ", line));
                }
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(go);
            }
        }

        // 서 있는 자세에서 무기 긴 축 ↔ 주먹(Socket) 축 각도, 무기 손잡이 ↔ 주먹 가운데 거리
        public static void AnalyzeWeapons()
        {
            foreach (var m in Arg("-only")?.Split(',') ?? Models)
            {
                string name = "Model_" + m;
                var go = Spawn(name, out var animator);
                if (go == null) continue;
                var idle = Clip(name, "Idle");
                Sample(animator, idle, idle != null ? idle.length * 0.5f : 0f);
                foreach (var bn in new[] { "Hand.R", "Index1.R", "Middle1.R", "Socket_R" })
                {
                    var all = go.GetComponentsInChildren<Transform>(true).Where(t => t.name == bn).ToList();
                    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Debug.Log($"[GripWeapon] {name} 뼈 {bn}: {all.Count}개 · " + string.Join(" | ", all.Select(t => $"{t.parent?.name}/{t.name} 스킨 {smrs.Count(s => s.bones.Contains(t))}/{smrs.Length}")));
                }
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mid = Find(go, "Middle2.R"); if (mid == null) break;
                    var mesh = new Mesh(); smr.BakeMesh(mesh, true);
                    float best = 9f; foreach (var v in mesh.vertices) best = Mathf.Min(best, Vector3.Distance(smr.transform.TransformPoint(v), mid.position));
                    var mesh2 = new Mesh(); smr.BakeMesh(mesh2, false);
                    float best2 = 9f; foreach (var v in mesh2.vertices) best2 = Mathf.Min(best2, Vector3.Distance(smr.transform.TransformPoint(v), mid.position));
                    Debug.Log($"[GripWeapon] {name} 메시 {smr.name} (배율 {smr.transform.lossyScale:F3}, 뿌리뼈 {(smr.rootBone != null ? smr.rootBone.name : "-")}): 중지2 뼈와 가장 가까운 점 {best:0.000}m (scale 끔 {best2:0.000}m)");
                }
                foreach (var sd in new[] { "R", "L" })
                {
                    Vector3 P(string n) { var t = Find(go, n); return t != null ? go.transform.InverseTransformPoint(t.position) : Vector3.zero; }
                    var h = P("Hand." + sd); var sk = P("Socket_" + sd);
                    Debug.Log($"[GripWeapon] {name} {sd}: 손목 {h:F3} · 검지1 {P("Index1." + sd) - h:F3} · 중지1 {P("Middle1." + sd) - h:F3} · 중지2 {P("Middle2." + sd) - h:F3} · 엄지1 {P("Thumb1." + sd) - h:F3} · Socket {sk - h:F3} (손목에서 {(sk - h).magnitude:0.000}m) · 손 배율 {Find(go, "Hand." + sd).lossyScale.x:0.000}");
                }
                foreach (var w in go.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Weapon_") || t.name == "Scythe"))
                {
                    var hand = w.parent; string s = hand.name.EndsWith(".L") ? ".L" : ".R";
                    var sock = Find(go, "Socket" + s.Replace(".", "_"));
                    if (sock == null) { Debug.Log($"[GripWeapon] {name} {w.name}: Socket 없음"); continue; }
                    var mf = w.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault();
                    var mb = mf != null ? mf.sharedMesh.bounds : new Bounds();
                    var ext = mf != null ? Vector3.Scale(mb.size, mf.transform.lossyScale) : Vector3.one;
                    Vector3 along = ext.x >= ext.y && ext.x >= ext.z ? mf.transform.right : ext.y >= ext.z ? mf.transform.up : mf.transform.forward;
                    float Ang(Vector3 a) => Vector3.Angle(along, a);
                    var mdl = go.transform;
                    Debug.Log($"[GripWeapon] {name} {w.name} ({hand.name}): 손잡이↔주먹 {Vector3.Distance(w.position, sock.position):0.000}m · 무기 긴 축(모델 기준 {mdl.InverseTransformDirection(along):F2}) ↔ Socket X {Ang(sock.right):0}° Y {Ang(sock.up):0}° Z {Ang(sock.forward):0}° · Socket Y(손가락) {mdl.InverseTransformDirection(sock.up):F2} Z(엄지) {mdl.InverseTransformDirection(sock.forward):F2} · 무기 길이 {Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z)):0.00}m");
                }
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(go);
            }
        }

        // (동작, 시점 비율) — 비율은 클립 길이 대비
        private static readonly (string clip, float t, string label)[] Poses =
        {
            ("Idle", 0.5f, "서 있기"), ("Attack", 0.2f, "공격 준비"), ("Attack", 0.4f, "공격 맞는 때"), ("Skill", 0.3f, "스킬 앞"), ("Skill", 0.7f, "스킬 뒤"),
            ("Hit", 0.3f, "맞음"), ("Defend", 0.5f, "방어"), ("Item", 0.5f, "아이템"), ("Heal", 0.7f, "회복"), ("Death", 1f, "쓰러짐"),
            ("Announce", 0.5f, "예고"), ("SelfHeal", 0.6f, "자가 봉합"),
        };

        public static void Capture()
        {
            string outDir = Arg("-outdir") ?? Path.Combine(Application.dataPath, "../Logs/grip");
            Directory.CreateDirectory(outDir);
            const int Cell = 220; const int Cols = 5;
            var rt = new RenderTexture(Cell, Cell, 24) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave }; // 새 장면을 열 때 지워지지 않게
            foreach (var m in Arg("-only")?.Split(',') ?? Models)
            {
                string name = "Model_" + m;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var light = new GameObject("L").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40f, 150f, 0f); light.intensity = 1.1f;
                var light2 = new GameObject("L2").AddComponent<Light>(); light2.type = LightType.Directional; light2.transform.rotation = Quaternion.Euler(20f, -30f, 0f); light2.intensity = 0.5f;
                RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
                var cam = new GameObject("C").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.19f, 0.23f);
                cam.orthographic = true; cam.targetTexture = rt; cam.nearClipPlane = 0.01f;
                var go = Spawn(name, out var animator);
                if (go == null) continue;
                var show = Arg("-show"); // 예: -show Weapon_Musket → 그 무기만 켬 (꺼 둔 무기 점검)
                if (show != null) foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("Weapon_") && t.parent.name.EndsWith(".R")) t.gameObject.SetActive(t.name == show);
                var grip = HandGrip.Attach(go);
                if (grip == null) { Debug.Log($"[GripCheck] {name}: 손가락 본 없음"); continue; }
                grip.InitForEditor();
                var poses = Poses.Where(p => Clip(name, p.clip) != null).ToArray();
                var sheet = new Texture2D(Cell * Cols, Cell * poses.Length, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                var hr = Find(go, "Hand.R"); var hl = Find(go, "Hand.L");
                for (int row = 0; row < poses.Length; row++)
                {
                    var p = poses[row];
                    var clip = Clip(name, p.clip);
                    float t = Mathf.Min(clip.length * p.t, clip.length - 0.001f);
                    Sample(animator, clip, t);
                    for (int col = 0; col < Cols; col++)
                    {
                        bool right = col == 0 || col == 1 || col == 4;
                        bool after = col == 1 || col == 3 || (col == 4 && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-before") < 0); // -before: 전신 칸을 "전"으로
                        grip.ApplyForEditor(after ? p.clip : null, p.t);
                        var hand = right ? hr : hl;
                        var baked = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nobake") >= 0 ? new List<GameObject>() : Bake(go);
                        var sockT = Find(go, right ? "Socket_R" : "Socket_L"); var focus = sockT != null ? sockT.position : hand.position;

                        Vector3 dir;
                        bool front = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-front") >= 0;
                        if (col == 4 && front) dir = (go.transform.forward * 0.85f + go.transform.right * 0.45f + Vector3.up * 0.15f).normalized; // 앞 3/4 (적 시점처럼)
                        else if (col == 4) dir = (-go.transform.forward * Mathf.Cos(40f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(40f * Mathf.Deg2Rad)).normalized; // 게임 시점: 아군 뒤 위 40°
                        else dir = (go.transform.forward * 0.8f + (right ? go.transform.right : -go.transform.right) * 0.6f + Vector3.up * 0.25f).normalized; // 손 바깥 앞쪽 3/4
                        bool full = col == 4 && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-full") >= 0;
                        if (full) focus = go.transform.position + Vector3.up * 1.2f;
                        cam.transform.position = focus + dir * 4f; cam.transform.LookAt(focus);
                        cam.orthographicSize = full ? 1.5f : col == 4 ? (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-front") >= 0 ? 0.55f : 0.34f) : 0.2f;
                        // 점검 표시(-marks): 빨강 손목 · 초록 중지 뿌리 · 파랑 Socket · 노랑 무기 기준점
                        var marks = new List<GameObject>();
                        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-marks") >= 0 && col < 4)
                        {
                            void Mark(Vector3 pos, Color c) { var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); s.transform.position = pos; s.transform.localScale = Vector3.one * 0.012f; var mt = new Material(Shader.Find("Unlit/Color")); mt.color = c; s.GetComponent<Renderer>().sharedMaterial = mt; marks.Add(s); }
                            string sd = right ? ".R" : ".L";
                            Mark(hand.position, Color.red);
                            var mid1 = Find(go, "Middle1" + sd); if (mid1 != null) Mark(mid1.position, Color.green);
                            if (sockT != null) Mark(sockT.position, Color.blue);
                            foreach (var fn in new[] { "Index1", "Index2", "Middle2", "Thumb1", "Thumb2" }) { var ft = Find(go, fn + sd); if (ft != null) Mark(ft.position, fn.StartsWith("Thumb") ? Color.cyan : fn.StartsWith("Index") ? Color.white : Color.magenta); }
                            foreach (Transform c in hand) if (c.gameObject.activeInHierarchy && HandGrip.HoldOf(c.name) != HandGrip.Hold.None) Mark(c.position, Color.yellow);
                        }
                        cam.Render();
                        foreach (var mk in marks) Object.DestroyImmediate(mk);
                        RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, Cell, Cell), Cell * col, Cell * (poses.Length - 1 - row));
                        Unbake(go, baked);
                    }
                    grip.ApplyForEditor(null, 0f);
                }
                sheet.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, $"grip_{m}.png"), sheet.EncodeToPNG());
                Debug.Log($"[GripCheck] {name}: {string.Join(" / ", poses.Select(p => p.label))} · 쥔 것 {grip.DevHolds}");
                AnimationMode.StopAnimationMode();
            }
        }

        private static GameObject Spawn(string name, out Animator animator)
        {
            animator = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Prefabs/{name}.prefab");
            if (prefab == null) { Debug.Log($"[GripCheck] {name} 프리팹 없음"); return null; }
            var go = (GameObject)Object.Instantiate(prefab);
            animator = go.GetComponentInChildren<Animator>();
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            animator.runtimeAnimatorController = null;
            return go;
        }

        private static AnimationClip Clip(string name, string clip) => AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Root}/Animations/{name}/{name}@{clip}.anim");

        private static void Sample(Animator animator, AnimationClip clip, float t)
        {
            if (clip == null) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, t);
            AnimationMode.EndSampling();
        }

        private static Transform Find(GameObject go, string n) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

        // 배치 모드는 피부 갱신이 늦음 → 지금 뼈 자세를 메시로 구워 찍음
        private static List<GameObject> Bake(GameObject go)
        {
            var list = new List<GameObject>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh(); smr.BakeMesh(mesh, true);
                var bg = new GameObject("baked");
                bg.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation); bg.transform.localScale = smr.transform.lossyScale; // 굽힌 결과가 뼈와 맞게 (배율 빠뜨리면 손이 10cm 어긋나 보임)
                bg.AddComponent<MeshFilter>().sharedMesh = mesh;
                bg.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                list.Add(bg); smr.enabled = false;
            }
            return list;
        }

        private static void Unbake(GameObject go, List<GameObject> baked)
        {
            foreach (var b in baked) Object.DestroyImmediate(b);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.enabled = true;
        }
    }
}
