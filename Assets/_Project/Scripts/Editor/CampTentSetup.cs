using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/14 기획 회신 Q4: 안개 야영 세트 성공 → 그 칸에 텐트 (다음 이동 시작에 치움).
    // 받은 텐트 모델이 없어 직접 조립: 천 A자 텐트 + 장대 + 돌 둘레 모닥불(받은 WarFX 작은 불꽃) + 자루.
    // 크기 기준: 텐트 깊이 1m (게임에서 칸 크기에 맞춰 줄임). 정면(입구) = −z (보드 카메라 쪽).
    // 실행: -executeMethod Fantasia.Editor.CampTentSetup.Build → Resources/BoardArt/CampTent.prefab
    public static class CampTentSetup
    {
        private const string Dir = "Assets/_Project/Art/Models/CampTent/";
        private const string Out = "Assets/_Project/Resources/BoardArt/CampTent.prefab";
        private const string Fire = "Assets/ThirdParty/JMO Assets/WarFX/_Effects/Fire/No Smoke/WFX_Fire SmallFlame (No Smoke).prefab";

        [MenuItem("Fantasia/Board/Camp Tent Build")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();
            var canvas = Mat("Mat_Tent_Canvas", new Color(0.62f, 0.5f, 0.34f)); // 11/14 QA: 밝은 회색처럼 보여 짙은 캔버스색
            var inside = Mat("Mat_Tent_Inside", new Color(0.16f, 0.12f, 0.09f));
            var wood = Mat("Mat_Tent_Wood", new Color(0.33f, 0.22f, 0.14f));
            var stone = Mat("Mat_Tent_Stone", new Color(0.42f, 0.41f, 0.4f));

            var root = new GameObject("CampTent");
            // 텐트 (폭 0.9 · 깊이 1 · 높이 0.7)
            var tent = new GameObject("Tent", typeof(MeshFilter), typeof(MeshRenderer));
            tent.transform.SetParent(root.transform, false);
            tent.GetComponent<MeshFilter>().sharedMesh = SaveMesh(TentMesh(0.9f, 1f, 0.7f), "CampTent_Mesh");
            tent.GetComponent<MeshRenderer>().sharedMaterials = new[] { canvas, inside };
            // 장대: 입구 양쪽 끝이 위로 조금 솟음
            foreach (float z in new[] { -0.5f, 0.5f })
                Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.38f, z), new Vector3(0.035f, 0.4f, 0.035f), wood);
            // 모닥불: 텐트 앞 오른쪽 (말이 서는 가운데는 비움)
            var fire = new GameObject("Campfire").transform;
            fire.SetParent(root.transform, false);
            fire.localPosition = new Vector3(0.55f, 0f, -0.75f);
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f;
                Prim(PrimitiveType.Sphere, fire, new Vector3(Mathf.Cos(a) * 0.17f, 0.03f, Mathf.Sin(a) * 0.17f), new Vector3(0.09f, 0.06f, 0.08f), stone);
            }
            Prim(PrimitiveType.Cylinder, fire, new Vector3(0f, 0.04f, 0f), new Vector3(0.04f, 0.13f, 0.04f), wood).transform.localRotation = Quaternion.Euler(90f, 30f, 0f);
            Prim(PrimitiveType.Cylinder, fire, new Vector3(0f, 0.05f, 0f), new Vector3(0.04f, 0.13f, 0.04f), wood).transform.localRotation = Quaternion.Euler(90f, -40f, 0f);
            var firePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Fire);
            if (firePrefab != null)
            {
                var f = (GameObject)PrefabUtility.InstantiatePrefab(firePrefab);
                PrefabUtility.UnpackPrefabInstance(f, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                f.transform.SetParent(fire, false);
                f.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                f.transform.localScale = Vector3.one * 0.18f;
            }
            else Debug.LogWarning("[CampTent] 불꽃 에셋 없음");
            // 자루 (Quaternius) — 텐트 입구 왼쪽
            var bag = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/BoardArt/Deco/Bag.prefab");
            if (bag != null)
            {
                var b = (GameObject)PrefabUtility.InstantiatePrefab(bag);
                PrefabUtility.UnpackPrefabInstance(b, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = new Vector3(-0.55f, 0f, -0.55f);
                b.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
                var rs = b.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0) { var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); if (bb.size.y > 0.001f) b.transform.localScale *= 0.22f / bb.size.y; }
            }
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            PrefabUtility.SaveAsPrefabAsset(root, Out);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[CampTent] 저장 " + Out);
        }

        private static GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        // A자 텐트: 지붕 두 면(겉 천) + 뒤 삼각 면 + 입구 안쪽 어두운 삼각(속이 보이는 느낌). 하위 메시 0 = 천, 1 = 안쪽
        private static Mesh TentMesh(float w, float d, float h)
        {
            float x = w / 2f, z = d / 2f;
            var v = new System.Collections.Generic.List<Vector3>();
            var t0 = new System.Collections.Generic.List<int>();
            var t1 = new System.Collections.Generic.List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e, System.Collections.Generic.List<int> tl)
            { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(e); tl.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
              int j = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(e); tl.AddRange(new[] { j, j + 2, j + 1, j, j + 3, j + 2 }); } // 양면 (뒷면은 꼭짓점 따로 — 법선이 서로 지우지 않게)
            void Tri(Vector3 a, Vector3 b, Vector3 c, System.Collections.Generic.List<int> tl)
            { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); tl.AddRange(new[] { i, i + 1, i + 2 });
              int j = v.Count; v.Add(a); v.Add(b); v.Add(c); tl.AddRange(new[] { j, j + 2, j + 1 }); }
            var ridgeF = new Vector3(0f, h, -z); var ridgeB = new Vector3(0f, h, z);
            Quad(new Vector3(-x, 0f, -z), ridgeF, ridgeB, new Vector3(-x, 0f, z), t0); // 왼쪽 지붕
            Quad(new Vector3(x, 0f, -z), new Vector3(x, 0f, z), ridgeB, ridgeF, t0);   // 오른쪽 지붕
            Tri(new Vector3(-x, 0f, z), ridgeB, new Vector3(x, 0f, z), t0);             // 뒤
            // 입구: 반쯤 열린 천 + 안쪽 어둠
            Tri(new Vector3(-x, 0f, -z), ridgeF, new Vector3(-x * 0.25f, 0f, -z - 0.04f), t0);
            Tri(new Vector3(-x * 0.25f, 0f, -z + 0.02f), ridgeF, new Vector3(x, 0f, -z + 0.02f), t1);
            var m = new Mesh { name = "CampTent" };
            m.SetVertices(v);
            m.subMeshCount = 2;
            m.SetTriangles(t0, 0); m.SetTriangles(t1, 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh SaveMesh(Mesh m, string name)
        {
            string p = Dir + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (old != null) { old.Clear(); EditorUtility.CopySerialized(m, old); return old; }
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        private static Material Mat(string name, Color c)
        {
            string p = Dir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, p); }
            m.color = c; m.SetFloat("_Glossiness", 0.05f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
