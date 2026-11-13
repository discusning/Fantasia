using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.Board
{
    // 11/13 팀장: 돌판 칸을 늘어놓은 모습이 어색함 → ①안 (Civilization 방식): 보드 전체에 이어진 바닥 한 장 + 칸은 얇은 선과 반투명 색으로만.
    //  바닥 질감: Resources/BoardArt/Ground_Albedo (+ Ground_Normal) — 이어 붙여도 이음새 없는(타일링) 돌길 그림을 넣으면 바로 바뀜.
    //  없으면 임시로 돌 색 얼룩 질감을 만들어 씀.
    public partial class HexBoard
    {
        public const bool GroundMode = true;
        private const float GroundTexMeters = 2.6f; // 질감 한 장이 덮는 넓이 (m) — 돌 크기 조절 (캐릭터 발 크기쯤 돌)

        private Material _overlayFill, _overlayLine, _groundMat;
        private Mesh _fillMesh, _lineMesh;
        private readonly List<Vector3> _groundCells = new();

        // 칸 하나: 반투명 칠(상태 색) + 얇은 테두리. 칠 렌더러를 돌려줌 (HexTile이 색을 바꿈)
        private Renderer SpawnOverlay(Transform tile, Vector3 localPos)
        {
            EnsureOverlayAssets();
            _groundCells.Add(localPos);
            var fill = new GameObject("Fill");
            fill.transform.SetParent(tile, false);
            fill.transform.localPosition = Vector3.up * (tileHeight + 0.012f);
            fill.AddComponent<MeshFilter>().sharedMesh = _fillMesh;
            var fr = fill.AddComponent<MeshRenderer>();
            fr.sharedMaterial = _overlayFill;
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var line = new GameObject("Line");
            line.transform.SetParent(tile, false);
            line.transform.localPosition = Vector3.up * (tileHeight + 0.015f);
            line.AddComponent<MeshFilter>().sharedMesh = _lineMesh;
            var lr = line.AddComponent<MeshRenderer>();
            lr.sharedMaterial = _overlayLine;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return fr;
        }

        private void EnsureOverlayAssets()
        {
            if (_fillMesh != null) return;
            bool pointy = UsesWorldMap;
            _fillMesh = FlatHex(tileSize * 0.93f, 0f, pointy, "HexFill");
            _lineMesh = FlatHex(tileSize * 0.99f, tileSize * 0.945f, pointy, "HexLine"); // 11/13: 얇게 했더니 돌길 무늬에 묻힘 → 조금 굵게
            _overlayFill = FadeMaterial(new Color(1f, 1f, 1f, 0f));
            _overlayLine = FadeMaterial(new Color(0.07f, 0.05f, 0.035f, 0.6f)); // 어두운 갈색 선 (돌길 위에서 보이게 진하게)
        }

        // 표준 셰이더 반투명 (보드 구조물 투명용 Fade 재질이 이미 빌드에 들어가 같은 변형을 씀)
        private static Material FadeMaterial(Color c)
        {
            // 11/13: 새로 만든 표준 재질의 반투명 변형은 빌드에서 빠져 불투명 회색으로 그려졌음(바닥을 가림)
            // → 빌드에 이미 들어간 보드 구조물 반투명 재질(Resources/BoardArt/Fade)을 복사해 같은 변형을 씀. 그림은 빼고 색만
            var src = Resources.Load<Material>("BoardArt/Fade/Prop_StoneWall_Fade");
            var m = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
            m.enableInstancing = true;
            if (src != null) { m.mainTexture = null; foreach (var t in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_ParallaxMap" }) if (m.HasProperty(t)) m.SetTexture(t, null); m.SetFloat("_Metallic", 0f); }
            m.SetFloat("_Mode", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
            m.SetFloat("_Glossiness", 0f);
            m.color = c;
            return m;
        }

        // 납작한 육각형 (inner > 0 이면 테두리 고리)
        private static Mesh FlatHex(float outer, float inner, bool pointy, string name)
        {
            float off = pointy ? 30f : 0f;
            var v = new List<Vector3>(); var t = new List<int>();
            Vector3 C(int i, float r) { float a = Mathf.Deg2Rad * (60f * i + off); return new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)); }
            if (inner <= 0f)
            {
                v.Add(Vector3.zero);
                for (int i = 0; i < 6; i++) v.Add(C(i, outer));
                for (int i = 0; i < 6; i++) { t.Add(0); t.Add(1 + (i + 1) % 6); t.Add(1 + i); }
            }
            else
            {
                for (int i = 0; i < 6; i++) { v.Add(C(i, outer)); v.Add(C(i, inner)); }
                for (int i = 0; i < 6; i++)
                {
                    int o0 = i * 2, i0 = i * 2 + 1, o1 = ((i + 1) % 6) * 2, i1 = o1 + 1;
                    t.Add(o0); t.Add(i0); t.Add(o1); // 11/13: 위에서 보이게 감는 방향 (전에는 뒤집혀 선이 안 그려졌음)
                    t.Add(i0); t.Add(i1); t.Add(o1);
                }
            }
            var n = new List<Vector3>(); for (int i = 0; i < v.Count; i++) n.Add(Vector3.up);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // 모든 칸 자리를 틈 없이 덮는 바닥 한 장 (칸 높이 만큼 두께, 바깥 옆면 포함). 질감은 월드 좌표로 이어짐.
        private void BuildGround()
        {
            if (_groundCells.Count == 0) return;
            var prism = HexMeshBuilder.BuildHexPrism(tileSize * 1.002f, tileHeight, pointyTop: UsesWorldMap);
            var pv = prism.vertices; var pt = prism.triangles; var pn = prism.normals;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            foreach (var c in _groundCells)
            {
                if (!GroundCellVisible(c)) continue; // 11/14 안개로 가린 칸은 바닥도 뺌
                int start = v.Count;
                for (int i = 0; i < pv.Length; i++)
                {
                    var p = pv[i] + c;
                    v.Add(p); n.Add(pn[i]);
                    // 옆면은 높이로, 윗면은 바닥 좌표로 (이웃 칸과 무늬가 이어짐)
                    uv.Add(Mathf.Abs(pn[i].y) > 0.5f ? new Vector2(p.x, p.z) / GroundTexMeters : new Vector2(p.x + p.z, p.y) / GroundTexMeters);
                }
                foreach (var i in pt) t.Add(start + i);
            }
            var mesh = new Mesh { name = "BoardGround", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            var go = new GameObject("Ground");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = GroundMaterial();
            r.receiveShadows = true;
        }

        private Material GroundMaterial()
        {
            if (_groundMat != null) return _groundMat;
            _groundMat = new Material(Shader.Find("Standard"));
            var albedo = Resources.Load<Texture2D>("BoardArt/Ground_Albedo");
            var normal = Resources.Load<Texture2D>("BoardArt/Ground_Normal");
            _groundMat.mainTexture = albedo != null ? albedo : TempGroundTexture();
            _groundMat.color = albedo != null ? new Color(0.8f, 0.78f, 0.76f) : Color.white; // 누런 줄눈을 살짝 눌러 보드 톤에 맞춤
            if (normal != null) { _groundMat.SetTexture("_BumpMap", normal); _groundMat.SetFloat("_BumpScale", 0.6f); _groundMat.EnableKeyword("_NORMALMAP"); }
            var ao = Resources.Load<Texture2D>("BoardArt/Ground_AO");
            if (ao != null) { _groundMat.SetTexture("_OcclusionMap", ao); _groundMat.SetFloat("_OcclusionStrength", 0.8f); }
            _groundMat.SetFloat("_Glossiness", 0.05f);
            return _groundMat;
        }

        // 임시 바닥 (에셋이 오기 전): 차분한 돌 색에 큰 얼룩·작은 얼룩 (이음새 없게 둘레를 감싸는 노이즈)
        private static Texture2D TempGroundTexture()
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, name = "TempGround" };
            var px = new Color[S * S];
            var baseCol = new Color(0.47f, 0.44f, 0.40f);
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / S * Mathf.PI * 2f, w = (float)y / S * Mathf.PI * 2f;
                // 원통 좌표로 감싸 이음새 없는 노이즈
                float big = Mathf.PerlinNoise(3f + Mathf.Cos(u) * 1.2f + Mathf.Sin(w) * 0.6f, 7f + Mathf.Sin(u) * 1.2f + Mathf.Cos(w) * 1.2f);
                float small = Mathf.PerlinNoise(11f + Mathf.Cos(u) * 5f + Mathf.Sin(w) * 2f, 5f + Mathf.Sin(u) * 5f + Mathf.Cos(w) * 5f);
                float k = 0.86f + (big - 0.5f) * 0.18f + (small - 0.5f) * 0.1f;
                px[y * S + x] = baseCol * k;
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }
    }
}
