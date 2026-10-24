namespace Fantasia.World
{
    public enum WorldPhase
    {
        AllyTurn,  // 파티장(1번) 1명이 파티를 이끌고 이동·행동
        EnemyTurn, // 월드 처리 (피의 흔적·영혼 수확·사신 추격·재고 시계 등)
    }

    // 월드 턴 (2026-10-24 기획 결정 — Docs/Planning/Decisions.md):
    // [아군 턴] → [적 턴] 2단계, 이 둘을 마치면 1월드 라운드. 보드에선 파티장 1명만 이동한다.
    // 전투 라운드·개인 턴과는 별개 시계이며, 전투·메뉴를 여닫는 것으로는 진행하지 않는다.
    public class WorldClock
    {
        public int WorldRound { get; private set; } = 1;
        public WorldPhase Phase { get; private set; } = WorldPhase.AllyTurn;
        public bool HasRolledThisTurn { get; private set; }
        public bool EventBonusUsedThisTurn { get; private set; }

        // 아군 턴에 주사위는 1번만. 이미 굴렸거나 적 턴이면 false.
        public bool TryBeginRoll()
        {
            if (Phase != WorldPhase.AllyTurn || HasRolledThisTurn) return false;
            HasRolledThisTurn = true;
            return true;
        }

        // 이벤트 칸 추가 이동은 아군 턴당 1회 (보너스로 다른 이벤트 칸에 가도 연쇄 없음).
        public bool TryConsumeEventBonus()
        {
            if (Phase != WorldPhase.AllyTurn || EventBonusUsedThisTurn) return false;
            EventBonusUsedThisTurn = true;
            return true;
        }

        // D27 불러오기
        public void Restore(int worldRound, WorldPhase phase, bool rolled, bool eventBonusUsed)
        {
            WorldRound = worldRound;
            Phase = phase;
            HasRolledThisTurn = rolled;
            EventBonusUsedThisTurn = eventBonusUsed;
        }

        public void EndAllyTurn()
        {
            if (Phase == WorldPhase.AllyTurn) Phase = WorldPhase.EnemyTurn;
        }

        // 적 턴 처리가 끝나면 월드 라운드 +1, 다음 아군 턴.
        public void EndEnemyTurn()
        {
            if (Phase != WorldPhase.EnemyTurn) return;
            WorldRound++;
            Phase = WorldPhase.AllyTurn;
            HasRolledThisTurn = false;
            EventBonusUsedThisTurn = false;
        }
    }
}
