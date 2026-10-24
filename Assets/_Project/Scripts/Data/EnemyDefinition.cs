using System;
using UnityEngine;

namespace Fantasia.Data
{
    // 적 정의 (System.md "적" 절 — 소형/중형/대형/보스 공통 스키마).
    // 보스는 Pattern(반복 기술)·Thresholds(HP 임계치 1회 예고)를 채운다 (D14 — 별도 클래스 없이 같은 스키마).
    [CreateAssetMenu(fileName = "Enemy", menuName = "Fantasia/Data/Enemy Definition")]
    public class EnemyDefinition : ScriptableObject
    {
        [Header("식별")]
        public string Id;
        public string DisplayName;
        public EnemySize Size;
        [TextArea] public string TypeTag; // 망령형/키메라형/야수형/구조물형/인간형 등

        [Header("아트")]
        // 적 3D 모델 프리팹 — 전투 필드·초상 공용. 비우면 임시 인형 (JobDefinition.Model과 같은 규칙).
        public GameObject Model;
        // 2D 초상 그림 — 넣으면 3D보다 우선. 조우 창 마름모 안에도 이 그림이 들어간다.
        public Sprite Portrait;

        [Header("전투 수치")]
        public int MaxHP;
        public int Speed;
        public int PhysicalAttack;
        public int MagicAttack;
        public int PhysicalDefense;
        public int MagicDefense;

        [Header("판정")]
        public int SlotCount; // Size와 일치해야 함 (소형2/중형3/대형·보스4) — 검증 대상
        [Range(0f, 1f)] public float SlotSuccessRate; // System.md 보완 스탯 표 (예: 45%)

        [Header("특성")]
        // [회피 상태] 패시브 — 크리티컬이 아니면 모든 공격을 회피 (그림자 사냥개 등, System.md 확률표).
        public bool HasEvasionState;
        // 공격 판정 성공(크리티컬) 시 대상에게 거는 상태 (D13 특성 — 기획자가 데이터로 조정).
        public StatusEffectDefinition[] OnCritStatuses = System.Array.Empty<StatusEffectDefinition>();
        // 크리티컬 명중 시 대상의 턴을 민다 (그림자 사냥개: 방해함).
        public TurnShift OnCritTurnShift;
        // 공격 대상 고르는 규칙 (기본 무작위).
        public EnemyTargeting Targeting;
        [TextArea] public string TraitDescription;
        public string DeploymentAct; // 배치 구간, 예: "1막"

        [Header("보상 (System.md 수입·가격 책정 근거) — 0이면 크기별 기본값")]
        public int FixedRewardCent; // 보스·인조 그림자 사신처럼 정해진 값 (예: 검시관 180)
        public int FixedRewardExp;

        [Header("보스 (System.md 보스급) — 비우면 기본 공격만 쓰는 일반 적")]
        public EnemyAction[] Pattern = Array.Empty<EnemyAction>();   // 순서대로 반복
        public EnemyThresholdAction[] Thresholds = Array.Empty<EnemyThresholdAction>(); // 각 1회
        // 주요 보스: 전투 전 몰래 지나가기·후퇴, 전투 중 퇴각·탈출권 불가 (System.md 조우 규칙)
        public bool BlocksEscape;
        // 처치 시 확정 획득하는 퀘스트 물건 (예: 검시 기록, 붉은 룬 조각)
        public string[] KeyItemRewards = Array.Empty<string>();

        public bool IsBoss => Size == EnemySize.Boss;
        public bool HasPattern => Pattern != null && Pattern.Length > 0;
    }
}
