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
        Fizzle,      // 아무 효과 없이 차례 소모 (저지당한 봉합 "실패", 예고 실행 미룸)
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
        // 명중만 하면 확정 적용 (크리티컬이어도 추가 없음, 회피하면 없음) — 봉인된 수은 중독 1스택 (Boss_Abel_v1.0)
        public StatusEffectDefinition[] OnHitStatuses = Array.Empty<StatusEffectDefinition>();
        [TextArea] public string Description;
    }

    // HP 임계치에서 전투당 1회 발동하는 예고 → 후속 행동 (예: 아벨 모로 금지된 봉합).
    // 예고 뒤 후속 행동 전에 저지(전용 행동 또는 계수 CancelMinCoefficient 이상 공격 기술)하면 후속 차례는 "실패"로 소모.
    [Serializable]
    public class EnemyThresholdAction
    {
        [Range(0f, 1f)] public float HpRatio;     // 이 비율 이하가 되면 (예: 0.6)
        public EnemyAction Announce;              // 예고 (피해 없음)
        public EnemyAction FollowUp;              // 다음 행동 (예: 자가 HP 30 회복)
        public float CancelMinCoefficient;        // 이 계수 이상 공격 기술 + 실제 피해 1 이상이면 취소 (0이면 불가)
        public string InterruptName;              // 예고 중 아군 전용 행동 (집중력 0, 판정 없이 취소). 비면 없음 — 예: 봉합 방해
        public string FailedName;                 // 저지됐을 때 보스 차례 이름 — 예: 봉합 실패
        [TextArea] public string CounterHint;     // 예고 UI 대응 안내 상세 (해당 기술 이름)
    }
}
