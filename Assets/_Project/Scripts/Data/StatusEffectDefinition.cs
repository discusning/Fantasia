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

        [Header("기간")]
        public int DurationTurns; // -1 = 정화 전까지, 0 = 즉시(스턴 등)
        public int MaxStacks = 1;

        [Header("정화")]
        public bool ClearedByCombatVictory;
        public bool ClearedByGeneralCleanse = true; // false면 전용 아이템/효과로만 제거 (예: 죽음의 표식)

        [Header("효과")]
        [TextArea(2, 6)] public string RuleDescription; // System.md 처리 규칙 원문 그대로
    }
}
