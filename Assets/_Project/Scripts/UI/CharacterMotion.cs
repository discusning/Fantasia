using UnityEngine;

namespace Fantasia.UI
{
    // 받은 움직임(11/6, 디자인 컨트롤러: 상태 Idle·Attack·Hit·Death, 신호 Attack·Hit·Die)을 게임에서 부르는 곳.
    // Editor/ArtSetup이 움직임 컨트롤러가 있는 인물 프리팹에만 붙인다 → 없으면 호출하는 쪽이 예전 연출(눕히기 등)을 쓴다.
    [RequireComponent(typeof(Animator))]
    public class CharacterMotion : MonoBehaviour
    {
        public const float AttackLead = 0.35f; // 공격 시작 → 맞는 순간까지 (받은 Attack 1초 중 팔이 앞으로 나오는 때, 짐작)

        private Animator _animator;
        private Animator Anim => _animator != null ? _animator : _animator = GetComponent<Animator>();

        public bool IsDown { get; private set; }

        public static CharacterMotion Of(GameObject model) => model != null ? model.GetComponentInChildren<CharacterMotion>(true) : null;

        public void Attack()
        {
            if (IsDown || !Ready()) return;
            Anim.ResetTrigger("Hit");
            Anim.SetTrigger("Attack");
        }

        public void Hit()
        {
            if (IsDown || !Ready()) return;
            Anim.ResetTrigger("Attack");
            Anim.SetTrigger("Hit");
        }

        // instant: 쓰러진 채로 시작(전투 시작·이어하기) — 쓰러지는 과정 없이 누운 마지막 자세로
        public void Die(bool instant)
        {
            if (!Ready()) return;
            IsDown = true;
            Anim.ResetTrigger("Attack");
            Anim.ResetTrigger("Hit");
            if (instant) Anim.Play("Death", 0, 1f);
            else Anim.SetTrigger("Die");
        }

        // 부활 — 바로 서 있는 자세로 (일어나는 움직임은 받지 않음)
        public void Revive()
        {
            if (!Ready()) return;
            IsDown = false;
            Anim.ResetTrigger("Die");
            Anim.Play("Idle", 0, 0f);
        }

        // 꺼진 모델(퇴각 등)은 Animator가 돌지 않으므로 무시
        private bool Ready() => Anim != null && Anim.isActiveAndEnabled && Anim.runtimeAnimatorController != null;
    }
}
