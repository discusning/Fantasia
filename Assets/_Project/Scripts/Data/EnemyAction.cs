using System;
using UnityEngine;

namespace Fantasia.Data
{
    // 적·보스가 쓰는 기술 하나 (System.md "보스급" 기술 표).
    public enum EnemyActionKind
    {
        Attack,      // 단일 대상 공격
        AreaAttack,  // 모든 아군 — 공통 슬롯 판정 1회, 방어·회피는 각자 (보스 공통 규칙)
        Announce,    // 예고 — 피해 없음, 다음 행동을 알림
        SelfHeal,    // 자기 HP 회복
    }

    [Serializable]
    public class EnemyAction
    {
        public string Name;
        public EnemyActionKind Kind;
        public float Coefficient = 1f;     // 1.0A, 0.8M 등
        public bool UsesMagic;
        public int HealAmount;             // SelfHeal
        public StatusEffectDefinition[] OnCritStatuses = Array.Empty<StatusEffectDefinition>();
        [TextArea] public string Description;
    }

    // HP 임계치에서 1회 발동하는 예고 → 후속 행동 (예: 아벨 모로 금지된 봉합).
    // 예고 뒤 후속 행동 전에 계수 CancelMinCoefficient 이상의 공격 기술을 맞히면 후속 행동 취소.
    [Serializable]
    public class EnemyThresholdAction
    {
        [Range(0f, 1f)] public float HpRatio;     // 이 비율 이하가 되면 (예: 0.6)
        public EnemyAction Announce;              // 예고 (피해 없음)
        public EnemyAction FollowUp;              // 다음 행동 (예: 자가 HP 30 회복)
        public float CancelMinCoefficient;        // 0이면 취소 불가
    }
}
