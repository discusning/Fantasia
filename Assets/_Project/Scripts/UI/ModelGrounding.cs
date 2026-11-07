using UnityEngine;

namespace Fantasia.UI
{
    // 받은 사람형 3D 모델의 발을 바닥에 맞춘다. 사람형 움직임(자세)은 몸 높이를 키 비율로 정하므로
    // 모델마다 조금 뜨거나 묻힐 수 있다 → 움직임이 처음 적용된 뒤 발끝 뼈 높이를 재서 한 번만 맞춘다.
    // (Editor/ArtSetup이 인물 프리팹에 붙인다. 쓰러짐처럼 몸이 기울어 있으면 똑바로 설 때까지 기다린다.)
    [RequireComponent(typeof(Animator))]
    public class ModelGrounding : MonoBehaviour
    {
        private const float SoleHeight = 0.03f; // 발끝 뼈(관절 중심)가 바닥에서 떠 있는 정도
        private static readonly HumanBodyBones[] Feet = { HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };

        private int _frames;
        private bool _done;

        private void LateUpdate()
        {
            if (_done || ++_frames < 2) return; // 움직임이 한 번 적용된 뒤
            var parent = transform.parent;
            if (parent == null || Vector3.Dot(parent.up, Vector3.up) < 0.95f) return;
            var animator = GetComponent<Animator>();
            if (animator == null || !animator.isHuman) { _done = true; return; }
            if (animator.GetCurrentAnimatorStateInfo(0).IsName("Death")) return; // 쓰러진 채 시작하면 일어설 때까지 기다림

            float lowest = float.PositiveInfinity;
            bool toes = false;
            foreach (var bone in Feet)
            {
                var t = animator.GetBoneTransform(bone);
                if (t == null) continue;
                if (bone == HumanBodyBones.LeftToes || bone == HumanBodyBones.RightToes) { toes = true; lowest = Mathf.Min(lowest, t.position.y); }
                else if (!toes) lowest = Mathf.Min(lowest, t.position.y - 0.08f); // 발목 뼈뿐이면 발목 높이만큼 아래가 바닥
            }
            _done = true;
            if (float.IsInfinity(lowest)) return;
            float delta = lowest - SoleHeight - parent.position.y;
            transform.position -= Vector3.up * delta;
        }
    }
}
