using UnityEngine;

namespace Fantasia.UI
{
    // 발밑 흐린 원형 그림자 (2026-11-10 기획 결정 1A — 전투·보드판, 파티장 말·사신 말 모두).
    // 실시간 그림자 대신 바닥에 눕힌 반투명 원판 1장: 가볍고, 인물이 떠 보이지 않게 땅에 붙여 줌.
    // 모델 루트의 자식으로 붙여서 루트가 움직이면(반 걸음·판 위 이동) 같이 따라감. 루트 배율도 같이 받음.
    public static class BlobShadow
    {
        private const float Opacity = 0.5f;
        private static Material _material;

        // height: 모델 키 (루트 기준 단위, CharacterModels.Create의 height)
        public static GameObject Attach(GameObject root, float height)
        {
            if (root == null) return null;
            var old = root.transform.Find("BlobShadow");
            if (old != null) return old.gameObject;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BlobShadow";
            var col = go.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            float radius = Mathf.Max(0.45f, height * 0.24f);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.02f, 0f); // 바닥에 겹쳐 깜빡이지 않게 살짝 위
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            return go;
        }

        private static Material Material
        {
            get
            {
                if (_material != null) return _material;
                _material = new Material(Shader.Find("Sprites/Default")) // 빌드에 항상 포함되는 셰이더
                {
                    name = "BlobShadow",
                    mainTexture = BuildTexture(),
                    color = new Color(0f, 0f, 0f, Opacity),
                    renderQueue = 2990, // 땅 위·인물 뒤
                };
                return _material;
            }
        }

        private static Texture2D BuildTexture()
        {
            const int Size = 64;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BlobShadow" };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) / Size * 2f - 1f, dy = (y + 0.5f) / Size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 1f - Mathf.SmoothStep(0.25f, 1f, d); // 가운데 진하고 가장자리로 흐려짐
                px[y * Size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
