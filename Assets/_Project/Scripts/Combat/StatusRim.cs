using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.Combat
{
    // 11/13 팀장: 방어력 감소 "걸려 있는 동안" A안 — 파티클은 몸 위치와 어긋나고 눈처럼 보임 →
    // 모델 가장자리가 강철빛 파랑으로 천천히 깜빡임 (다른 게임의 상태 테두리 빛 방식).
    // StatusAura가 만드는 빈 오브젝트에 붙음: 켜지면 모델 렌더러마다 테두리 재질을 하나 더 얹고, 지워지면 원래대로.
    public class StatusRim : MonoBehaviour
    {
        private readonly List<(Renderer r, Material[] original)> _applied = new();
        private Material _mat;
        private float _fade = 1f;
        private bool _stopping;
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private SpriteRenderer _icon;
        private Transform _iconRoot;
        private Vector3 _iconBase;

        // 11/13 팀장: A안이 애매하면 B안 섞기 — 어두운 모델(쥐)은 테두리가 약해 머리 위에 "금 간 방패" 아이콘을 같이 띄움 (임시 그림, 디자인 그림 오면 교체)
        public void ShowIcon(string texturePath, Vector3 worldPos, float size)
        {
            var tex = Resources.Load<Texture2D>(texturePath);
            if (tex == null) return;
            var go = new GameObject("Icon");
            go.transform.SetParent(transform, false);
            go.transform.position = worldPos;
            go.transform.localScale = Vector3.one * size;
            _icon = go.AddComponent<SpriteRenderer>();
            _icon.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            _icon.sortingOrder = 50;
            _iconRoot = go.transform; _iconBase = worldPos - transform.position;
        }

        public static StatusRim Attach(GameObject holder, GameObject model, string materialPath)
        {
            var src = Resources.Load<Material>(materialPath);
            if (src == null) return null;
            var rim = holder.AddComponent<StatusRim>();
            rim._mat = new Material(src);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer) || !r.enabled || r.GetComponentInParent<ParticleSystem>() != null) continue;
                // 발밑 그림자 판 같은 납작한 판(꼭짓점 몇 개)은 빼기 — 쥐 발밑에 파란 네모가 빛났음
                if (r is MeshRenderer && (r.GetComponent<MeshFilter>() == null || r.GetComponent<MeshFilter>().sharedMesh == null || r.GetComponent<MeshFilter>().sharedMesh.vertexCount <= 24)) continue;
                var mats = r.sharedMaterials;
                var next = new Material[mats.Length + 1];
                mats.CopyTo(next, 0);
                next[mats.Length] = rim._mat;
                rim._applied.Add((r, mats));
                r.sharedMaterials = next;
            }
            return rim;
        }

        // StatusAura가 풀릴 때 부름 — 1초에 걸쳐 흐려진 뒤 지워짐
        public void FadeOut() => _stopping = true;

        private void Update()
        {
            if (_mat == null) return;
            if (_stopping) _fade = Mathf.MoveTowards(_fade, 0f, Time.deltaTime);
            float pulse = 0.25f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / 1.6f)); // 1.6초 주기로 천천히 (11/13 QA: 몸 전체가 파래져 세기 낮춤)
            _mat.SetFloat(StrengthId, pulse * _fade);
            if (_iconRoot != null)
            {
                _iconRoot.position = transform.position + _iconBase + Vector3.up * (0.04f * Mathf.Sin(Time.time * 2.4f)); // 살짝 떠 있는 느낌
                var cam = Camera.main; if (cam != null) _iconRoot.rotation = cam.transform.rotation; // 늘 카메라를 봄
                _icon.color = new Color(1f, 1f, 1f, (0.75f + 0.25f * pulse) * _fade);
            }
        }

        private void OnDestroy()
        {
            // 11/13 QA: 방어·공격 약화가 같이 걸린 뒤 하나가 풀리면 다른 쪽이 기억한 재질 목록에 지워진 재질이 남아 분홍으로 깨짐
            // → 통째로 되돌리지 않고 지금 목록에서 내 재질(과 지워진 재질)만 뺌
            foreach (var (r, _) in _applied)
            {
                if (r == null) continue;
                var keep = new List<Material>();
                foreach (var m in r.sharedMaterials) if (m != null && m != _mat) keep.Add(m);
                r.sharedMaterials = keep.ToArray();
            }
            _applied.Clear();
            if (_mat != null) Destroy(_mat);
        }
    }
}
