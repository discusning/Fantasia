using UnityEngine;

namespace Fantasia.Board
{
    // 11/12 팀장 결정 (2번): 카메라와 파티 말 사이에 낀 구조물(집·나무·거점 등)은 그동안만 반투명.
    // 말이 구조물 둘레를 돌거나 뒤로 지나갈 때, 카메라가 가까이 다가갈 때(이동 시점 C) 말이 가려지지 않게.
    // 가리지 않게 되면 천천히 원래대로. 구조물 칸은 HexTile.WithProps.
    [RequireComponent(typeof(Camera))]
    public class BoardOccluderFade : MonoBehaviour
    {
        public Transform Target;
        public const float FadedAlpha = 0.3f, FadeSpeed = 5f, NearRadius = 9f;
        private static readonly float[] Heights = { 0.15f, 0.55f, 0.95f }; // 말 발·몸·머리 — 하나라도 가리면 흐리게

        public static int DevFadedCount { get; private set; }
        private static bool _warmed;

        // 반투명 재질을 처음 쓸 때 한 번 멈칫(셰이더 준비)하는 걸 막으려고 보드 시작 때 미리 준비 (11/12 이동 QA에서 0.35초 멈춤 1번)
        private void Start()
        {
            if (_warmed) return;
            _warmed = true;
            var fades = Resources.LoadAll<Material>("BoardArt/Fade");
            var col = new ShaderVariantCollection();
            foreach (var m in fades)
                col.Add(new ShaderVariantCollection.ShaderVariant(m.shader, UnityEngine.Rendering.PassType.ForwardBase, m.shaderKeywords));
            col.WarmUp();
        }

        private void LateUpdate()
        {
            if (Target == null) return;
            Vector3 cam = transform.position;
            int faded = 0;
            for (int t = HexTile.WithProps.Count - 1; t >= 0; t--)
            {
                var tile = HexTile.WithProps[t];
                if (tile == null) { HexTile.WithProps.RemoveAt(t); continue; }
                bool hides = false;
                if (tile.PropVisible && (tile.transform.position - Target.position).sqrMagnitude < NearRadius * NearRadius)
                    hides = Hides(tile.PropBounds(), cam, Target.position);
                float goal = hides ? FadedAlpha : 1f;
                if (!Mathf.Approximately(tile.PropAlpha, goal))
                    tile.SetPropAlpha(Mathf.MoveTowards(tile.PropAlpha, goal, FadeSpeed * Time.deltaTime));
                if (tile.PropAlpha < 0.999f) faded++;
            }
            DevFadedCount = faded;
        }

        public static bool Hides(Bounds b, Vector3 cam, Vector3 target)
        {
            b.Expand(-0.1f); // 모서리만 스치는 건 무시
            foreach (float h in Heights)
            {
                var p = target + Vector3.up * h;
                var d = p - cam;
                float dist = d.magnitude;
                if (dist < 0.01f) continue;
                if (b.IntersectRay(new Ray(cam, d / dist), out float hit) && hit < dist - 0.05f && !b.Contains(p)) return true;
                if (b.Contains(p)) return true; // 말이 구조물 안에 들어감 (넘친 구조물)
            }
            return false;
        }
    }
}
