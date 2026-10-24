using Fantasia.Core;

namespace Fantasia.Dice
{
    // Minimal reusable dice primitive. Combat's slot-roll checks and the
    // overworld's movement roll both build on this rather than duplicating
    // random calls. 결과는 저장되는 게임 난수(GameRandom)에서 — 껐다 켜도 같은 결과 (Save.md).
    public static class DiceRoller
    {
        public static int Roll(int sides)
        {
            return GameRandom.Range(1, sides + 1);
        }

        public static bool RollChance(float chance01)
        {
            return GameRandom.Value < chance01;
        }
    }
}
