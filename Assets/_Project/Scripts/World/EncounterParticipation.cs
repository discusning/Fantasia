using Fantasia.Board;

namespace Fantasia.World
{
    // 조우에 전투로 참가하는 아군 범위 판정 (System.md "이벤트 수치·도시 거점 보완").
    // 시작 타일에서 2칸 이내 생존 캐릭터가 기본, 성급함의 기도를 받은 캐릭터는 3칸.
    // 참가 범위 밖 캐릭터는 해당 전투 보상을 받지 않고 다음 월드 행동을 기다린다
    // (보상 제외 처리는 호출하는 쪽 — D19 전리품 지급 — 책임).
    public static class EncounterParticipation
    {
        public const int DefaultSupportRange = 2;
        public const int HastePrayerSupportRange = 3;

        public static bool IsWithinRange(HexCoord memberCoord, HexCoord encounterCoord, bool hasHastePrayer)
        {
            int range = hasHastePrayer ? HastePrayerSupportRange : DefaultSupportRange;
            return memberCoord.DistanceTo(encounterCoord) <= range;
        }
    }
}
