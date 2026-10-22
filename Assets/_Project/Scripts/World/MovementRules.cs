using Fantasia.Dice;

namespace Fantasia.World
{
    // 보드 이동 규칙 (System.md "기본 조작 > 이동" + "피의 흔적 시스템").
    public static class MovementRules
    {
        public const int ReaperMovementPenalty = 2; // 사신 출현 중 이동력 감소

        // 주사위 1~6을 굴려 이번 턴 이동력을 정한다. 사신에게 쫓기는 중이면 -2(최소 0).
        // 실제 이동 칸 수는 호출하는 쪽에서 0~이 값 사이로 플레이어가 선택한다
        // (HexBoard.ComputeReachability가 이미 그 범위를 BFS로 계산해준다).
        public static int RollMovePoints(bool pursuedByReaper)
        {
            int roll = DiceRoller.Roll(6);
            return pursuedByReaper ? System.Math.Max(0, roll - ReaperMovementPenalty) : roll;
        }

        // 이벤트 발판 도착 시 추가 이동 0/1/2, 각 1/3 균등 추첨 (System.md "이벤트 수치·도시 거점 보완").
        public static int RollEventBonusMove()
        {
            return DiceRoller.Roll(3) - 1; // 1,2,3 -> 0,1,2
        }
    }
}
