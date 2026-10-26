using UnityEngine;

namespace Fantasia.Data
{
    // 상태 효과 처리 방식 분류. DurationTurns 해석은 Kind에 따라 다르다:
    // DamageOverTime=턴마다/특정 턴 고정 피해, StatModifier=능력치 증감,
    // ControlAction=행동 강제/변경, Special=개별 로직(죽음의 표식 등).
    public enum StatusEffectKind
    {
        DamageOverTime,
        StatModifier,
        ControlAction,
        Special,
    }

    // 구현된 상태 효과 처리 (D12 1차: 1막 소형 적·단검이 쓰는 5종). 나머지(부식·화상·공포·
    // 죽음의 표식·저주·죽음 저항·스턴 등)는 해당 적·스킬과 함께 추가한다.
    public enum StatusEffectType
    {
        None,
        WeakBleed,  // 약한 출혈 — 턴 시작 방어 무시 피해, HP 1 하한
        Poison,     // 중독 — 스택×피해, HP 1 하한, 모든 스탯 감소, 정화 전까지
        Confusion,  // 혼란 — 다음 개인 턴 행동 강제 (회복약 없으면 임의의 적 기본 공격)
        Bind,       // 속박 — 속도 감소
        AttackDown, // 공격력(·마력) 감소
        DefenseDown,     // 물리·마법 방어 감소 (약점 간파)
        EvasionRevealed, // [회피 상태] 일시 해제 (장막 걷기·성수)
        AccuracyUp,      // 다음 무기 공격 슬롯 성공률 +Magnitude%p, 공격하면 소모 (영점 조정)
        SpeedUp,         // 속도 +Magnitude (신속의 앰플) — 라운드 단위: 사용한 라운드 제외, 이후 DurationTurns 라운드 끝까지
    }

    // 상태 효과 정의 (System.md "버프와 디버프 > 상태 효과 수치와 처리 순서" 표,
    // 11종: 부식/약한·강화·치명적 출혈/화상/중독/혼란/공포/죽음의 표식/저주/속박/
    // 죽음 저항/스턴). DurationTurns는 "개인 턴" 기준, -1은 "정화 전까지"(무기한).
    [CreateAssetMenu(fileName = "StatusEffect", menuName = "Fantasia/Data/Status Effect Definition")]
    public class StatusEffectDefinition : ScriptableObject
    {
        [Header("식별")]
        public string Id;
        public string DisplayName;
        public StatusEffectKind Kind;
        // 적 HP 바·파티 카드의 상태 아이콘 칸에 쓰는 그림. 비우면 ShortLabel 글자 배지로 대신한다.
        public Sprite Icon;
        public string ShortLabel; // 아이콘 없을 때 칸에 들어갈 두 글자 (예: "출혈")

        [Header("기간")]
        public int DurationTurns; // -1 = 정화 전까지, 0 = 즉시(스턴 등)
        public int MaxStacks = 1;

        [Header("정화")]
        public bool ClearedByCombatVictory;
        public bool ClearedByGeneralCleanse = true; // false면 전용 아이템/효과로만 제거 (예: 죽음의 표식)

        [Header("효과")]
        // 실제 처리 방식 (Combat/StatusSet이 이 값으로 분기). 새 종류는 개발 검토 후 추가.
        public StatusEffectType Effect;
        public int Magnitude;   // 출혈: 턴당 피해 / 중독: 스택당 피해 / 속박: 속도 감소 / 공격력 감소: 감소량
        public int StatPenalty; // 중독: 모든 능력 스탯 감소 (스택 수와 무관하게 1회)
        [TextArea(2, 6)] public string RuleDescription; // System.md 처리 규칙 원문 그대로
    }
}
