using Fantasia.Dice;

namespace Fantasia.Combat
{
    // D21 쓰러짐·부활·패배 수치 (System.md "승리 보상·아이템 종류·패배 약탈 확률표").
    public static class DefeatRules
    {
        public const float InBattleReviveHpRatio = 0.3f; // 전투 중 부활: 생존 아군 행동 1회 + 대상 LP 1 → HP 30%
        public const float WipeReturnHpRatio = 0.5f;     // 전원 쓰러짐: LP 1 써서 직전 안전 타일에서 HP 50%

        // 가정 (System.md에 없음 — 기획 확인 항목): 승리·퇴각으로 전투가 끝났을 때 쓰러져 있던 아군은
        // LP 1을 써서 HP 30%로 일어나고, LP가 없으면 영구 사망.
        public const float PostBattleReviveHpRatio = InBattleReviveHpRatio;

        // 약탈량 확률표 (합 100, 평균 16.5%): 없음 10 · 10% 40 · 20% 30 · 30% 15 · 40% 5
        private static readonly (int percent, int weight)[] PlunderTable = { (0, 10), (10, 40), (20, 30), (30, 15), (40, 5) };

        public static int RollPlunderPercent()
        {
            int roll = DiceRoller.Roll(100); // 1~100
            foreach (var (percent, weight) in PlunderTable)
            {
                if (roll <= weight) return percent;
                roll -= weight;
            }
            return 0;
        }
    }
}
