using System.Collections.Generic;
using Fantasia.Data;
using UnityEngine;

namespace Fantasia.UI
{
    // 11/11 팀장 결정: 조우 창 왼쪽 위(예전 "소형" 크기 원 자리)에 출몰한 적의 3D 모델을 Idle 동작 그대로 보여준다.
    // 초상 촬영장(PortraitStage)과 같은 방식 — 화면 밖 먼 곳에 모델 + 전용 카메라 + 조명, 배경은 투명.
    // 상반신·머리 위주 구도(11/11 팀장 요청), 아래 잘린 선은 이름 띠 뒤로 숨김. 모델의 Animator 기본 상태(Idle)가 그대로 돈다. 움직임이 없는 임시 인형은 살짝 몸을 튼다.
    // 적 종류마다 한 번만 만들고, 지금 보이는 적의 카메라만 켠다 (창이 닫히면 모두 끔).
    public static class EncounterModelView
    {
        public const int TexWidth = 448, TexHeight = 320; // 1.4:1 — UI 칸도 같은 비율
        private static readonly Vector3 Origin = new Vector3(-400f, -5000f, 0f); // 초상 촬영장(x≥0)과 떨어진 곳
        // 11/12 적마다 구도 넓히기 (1 = 기본 "머리~가슴"). 가고일 유체: 머리가 몸에 비해 커서 얼굴만 꽉 차고 뿔·팔이 잘려 깨져 보였음 → 1.8배 (머리·뿔·팔·어깨)
        private static readonly Dictionary<string, float> FrameScale = new() { ["Enemy_가고일유체"] = 1.8f };
        private const float Spacing = 40f;
        private const float Yaw = 35f; // 전투처럼 비스듬히 (정면보다 몸 모양이 잘 보임)

        private sealed class Stage { public GameObject Root; public Camera Camera; public RenderTexture Texture; }
        private static readonly Dictionary<EnemyDefinition, Stage> Stages = new();
        private static Transform _holder;

        public static Texture Show(EnemyDefinition enemy)
        {
            if (_holder == null) { foreach (var s in Stages.Values) if (s.Texture != null) { s.Texture.Release(); Object.Destroy(s.Texture); } Stages.Clear(); } // 씬이 바뀌어 촬영장이 사라졌으면 새로
            HideAll();
            if (enemy == null) return null;
            if (!Stages.TryGetValue(enemy, out var stage) || stage.Root == null)
            {
                stage = Build(enemy, Stages.Count);
                Stages[enemy] = stage;
            }
            stage.Camera.enabled = true;
            return stage.Texture;
        }

        // 11/11 다른 종류 적 2마리: 이미 켠 카메라는 그대로 두고 하나 더 켬
        public static Texture ShowAlso(EnemyDefinition enemy)
        {
            if (enemy == null) return null;
            if (!Stages.TryGetValue(enemy, out var stage) || stage.Root == null) { stage = Build(enemy, Stages.Count); Stages[enemy] = stage; }
            stage.Camera.enabled = true;
            return stage.Texture;
        }

        public static void HideAll()
        {
            foreach (var s in Stages.Values) if (s.Camera != null) s.Camera.enabled = false;
        }

        private static Stage Build(EnemyDefinition enemy, int index)
        {
            if (_holder == null) _holder = new GameObject("EncounterModelStudio").transform; // 씬과 함께 사라짐
            var root = new GameObject($"EncounterStage_{enemy.name}");
            root.transform.SetParent(_holder, false);
            root.transform.position = Origin + Vector3.right * (index * Spacing);

            var model = CharacterModels.Create(enemy, out float height);
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 180f + Yaw, 0f); // 카메라(+z) 쪽을 보게
            var animator = model.GetComponentInChildren<Animator>();
            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            else model.AddComponent<PortraitIdleSway>();

            // 실제 그림 범위(렌더러 묶음)로 구도 계산
            var bounds = new Bounds(root.transform.position + Vector3.up * height * 0.5f, new Vector3(height * 0.6f, height, height * 0.6f));
            bool first = true;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }
            // 11/11 팀장 요청: 가슴·어깨부터 머리까지만 크게. 그림 범위는 모델마다 들쭉날쭉(쥐는 머리보다 훨씬 높게 잡힘)이라
            // 머리 뼈 위치를 기준으로 잡는다. Idle 첫 자세로 한 번 돌려 둔 뒤 읽음. 머리 뼈가 없으면 그림 범위의 위쪽 30%.
            const float fov = 28f, margin = 1.12f;
            float halfV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            if (animator != null) animator.Update(0f);
            var head = FindHead(model, animator);
            float frameH; Vector3 look;
            // 11/12 팀장 지적: 연성 독거미처럼 낮고 옆으로 넓은 적은 "머리~가슴"으로 잡으면 눈만 화면 가득(머리 높이 0.33m).
            // 몸 폭이 키보다 1.7배 넘으면 몸 전체를 위에서 비스듬히 잡는다.
            float spread = Mathf.Max(bounds.size.x, bounds.size.z);
            bool wide = spread > bounds.size.y * 1.7f; // 거미 2.0배 · 가고일(날개) 1.4배 → 가고일은 얼굴 구도 유지
            if (wide)
            {
                frameH = Mathf.Max(bounds.size.y, spread * 0.62f);
                look = bounds.center;
                head = null; // 머리 따라가기 안 함
            }
            else if (head != null)
            {
                float headH = Mathf.Max(0.2f, head.position.y - root.transform.position.y); // 땅~머리 뼈
                frameH = headH * 0.36f * (FrameScale.TryGetValue(enemy.name, out var fs) ? fs : 1f);
                look = head.position - Vector3.up * frameH * (fs > 1f ? 0.3f : 0.12f); // 넓힌 적은 가운데를 조금 아래로 (어깨까지)
            }
            else
            {
                frameH = bounds.size.y * 0.3f;
                look = new Vector3(bounds.center.x, bounds.max.y - frameH * 0.56f, bounds.center.z);
            }
            float distance = frameH * margin * 0.5f / halfV + frameH * 0.3f;
            // 11/12 팀장 지적: 가고일 유체가 깨져 보임 — 머리에 붙은 카메라가 앞으로 뻗은 날개 "안"에 들어가 면이 잘렸음.
            // 카메라는 늘 모델 앞 끝(그림 범위 가장 앞)보다 0.25m 바깥. 모자라면 그만큼 물러남(조금 더 넓게 보임).
            float outside = look.z - bounds.min.z + 0.25f;
            bool pushedBack = distance < outside;
            if (pushedBack) distance = outside;
            Debug.Log($"[EncounterModel] {enemy.name} 머리 뼈={(head != null ? head.name : "없음")} 구간={frameH:0.00} 크기={bounds.size.x:0.00}×{bounds.size.y:0.00}×{bounds.size.z:0.00}{(wide ? " (넓은 몸: 전체)" : "")}{(pushedBack ? $" (모델 밖으로 물러남 {distance:0.00}m)" : "")}");

            var camGO = new GameObject("EncounterModelCamera");
            camGO.transform.SetParent(root.transform, false);
            camGO.transform.position = look + new Vector3(0f, frameH * (wide ? 0.55f : 0.1f), -distance); // 살짝 위에서 (넓은 몸은 등이 보이게 더 위에서)
            camGO.transform.LookAt(look);
            if (head != null) { var follow = camGO.AddComponent<HeadFollow>(); follow.Head = head; follow.Drop = head.position.y - look.y; /* 위에서 정한 가운데 높이 그대로 */ follow.Offset = new Vector3(0f, frameH * 0.1f, -distance); } // Idle로 숙인 머리를 따라감 (쥐처럼 구부정한 적)
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = distance + Mathf.Min(bounds.size.magnitude, 12f) + 2f; // 옆 촬영장(40)까지는 안 닿게
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // 투명 — 창 판 위에 모델만
            cam.depth = -10f;
            var tex = new RenderTexture(TexWidth, TexHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = $"EncounterModel_{enemy.name}" };
            cam.targetTexture = tex;
            cam.enabled = false;

            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.transform.SetParent(root.transform, false);
            key.transform.position = look + new Vector3(-distance * 0.5f, bounds.size.y * 1.2f + 1f, -distance * 0.7f);
            key.type = LightType.Point;
            key.range = distance * 3f + 4f;
            key.intensity = 1.1f;
            var rim = new GameObject("RimLight").AddComponent<Light>();
            rim.transform.SetParent(root.transform, false);
            rim.transform.position = look + new Vector3(distance * 0.6f, bounds.size.y, distance * 0.6f);
            rim.type = LightType.Point;
            rim.range = distance * 3f + 4f;
            rim.intensity = 0.6f;
            rim.color = new Color(1f, 0.85f, 0.65f);

            return new Stage { Root = root, Camera = cam, Texture = tex };
        }

        private static Transform FindHead(GameObject model, Animator animator)
        {
            if (animator != null && animator.isHuman) { var h = animator.GetBoneTransform(HumanBodyBones.Head); if (h != null) return h; }
            Transform best = null;
            foreach (var t in model.GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                if (!n.Contains("head") || n.Contains("end") || n.Contains("top") || n.Contains("nub")) continue;
                if (best == null || t.name.Length < best.name.Length) best = t; // "Head"가 "HeadBand_L"보다 우선
            }
            return best;
        }
    }
}

    // 조우 창 모델 카메라가 Idle 중인 머리를 따라간다. 처음 한 번은 바로 맞추고, 그 뒤엔 숨쉬기 정도는 천천히 따라가 흔들림이 없게.
    public class HeadFollow : MonoBehaviour
    {
        public Transform Head;
        public float Drop;
        public Vector3 Offset;
        private bool _placed;
        private Vector3 _look;

        private void LateUpdate()
        {
            if (Head == null) return;
            var target = Head.position - Vector3.up * Drop;
            _look = _placed ? Vector3.Lerp(_look, target, 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime)) : target;
            _placed = true;
            transform.position = _look + Offset;
            transform.LookAt(_look);
        }

        private void OnEnable() => _placed = false;
    }
