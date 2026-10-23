namespace Fantasia.Data
{
    // 크리티컬 명중 시 대상의 행동 순서를 미는 효과 (System.md "턴의 변화" — 기획이 다시 검토할 예정이라
    // 규칙은 Combat/BattleQueue 한 곳에만 둔다).
    public enum TurnShift
    {
        None,
        Delay, // 방해함 — 아직 행동하지 않은 대상을 남은 큐의 2/3 위치로
        Reset, // 초기화 — 아직 행동하지 않은 대상을 라운드 맨 뒤로
    }

    // 적이 공격 대상을 고르는 규칙 (D13 "적별 대상 선택을 데이터로"). System.md에 적별 규칙이 아직 없어
    // 기본은 Random — 기획이 적마다 정하면 데이터에서 바꾼다.
    public enum EnemyTargeting
    {
        Random,       // 살아 있는 아군 중 균등 추첨
        LowestHP,     // 현재 HP가 가장 낮은 아군 (마무리형)
        LowestHPRatio,// HP 비율이 가장 낮은 아군
        HighestAttack,// 최종 공격력·마력이 가장 높은 아군 (위협 제거형)
        Front,        // 편성 맨 앞(첫 번째)부터
    }
}
