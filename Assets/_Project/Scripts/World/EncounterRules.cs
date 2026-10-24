using System;
using Fantasia.Combat;
using Fantasia.Data;

namespace Fantasia.World
{
    public enum EncounterOutcome
    {
        FightStarted,    // 전투: 판정 없이 일반 속도 큐로 전투 시작
        AmbushSuccess,   // 기습 성공: 첫 라운드 아군 선공
        AmbushFailure,   // 기습 실패: 첫 라운드 적 선공
        SneakSuccess,    // 몰래 회피 성공: 전투 없이 통과, 경험치·전리품 없음
        SneakFailure,    // 몰래 회피 실패: 일반 전투 시작 (기습 페널티 없음)
        RetreatSuccess,  // 전투 전 퇴각: 판정 없이 직전 안전 타일로 복귀
    }

    // 적 조우 판정 (System.md "전투" + "공격·조우·회피 확률표"). 보스·고성 지하 코어는
    // 전투 전 몰래 회피와 퇴각을 막는다 — 보스의 기습은 첫 라운드 순서만 바꿀 뿐 페이즈·
    // 의식을 생략하지 않는다(그 부분은 보스 전투 로직 쪽 책임, 여기서는 막지 않는다).
    public static class EncounterRules
    {
        public const int RetreatDuringCombatSlotCount = 2;

        public static bool AllowsPreCombatAvoidance(EnemyDefinition enemy) => enemy.Size != EnemySize.Boss && !enemy.BlocksEscape;

        // 전투: 선택만으로 결정, 판정 없음.
        public static EncounterOutcome Fight() => EncounterOutcome.FightStarted;

        // 판정 결과 + 슬롯 몇 개가 성공했는지 (조우 UI가 슬롯을 하나씩 공개하는 데 쓴다).
        public readonly struct Roll
        {
            public readonly EncounterOutcome Outcome;
            public readonly SlotCheck.Result Slots;

            public Roll(EncounterOutcome outcome, SlotCheck.Result slots)
            {
                Outcome = outcome;
                Slots = slots;
            }
        }

        // 기습·몰래 회피의 슬롯 1개 확률 (UI 확률표 표시용 — 실제 판정과 같은 공식).
        // slotBonus: 명탐정 "현장 검증" 같은 기습 보정 (+0.1 = +10%p).
        public static float AmbushSlotChance(EnemyDefinition enemy, int perceptionStat, float slotBonus = 0f) =>
            SlotCheck.FinalSlotChance(enemy.SlotCount, perceptionStat, slotBonus);

        public static float SneakSlotChance(EnemyDefinition enemy, int speedStat) =>
            SlotCheck.FinalSlotChance(enemy.SlotCount, speedStat);

        // 기습: 인지 기반 슬롯 판정. 적의 슬롯 수(소형2/중형3/대형·보스4)를 그대로 쓴다.
        public static EncounterOutcome ResolveAmbush(EnemyDefinition enemy, int perceptionStat, int focusSlots = 0) =>
            RollAmbush(enemy, perceptionStat, focusSlots).Outcome;

        public static Roll RollAmbush(EnemyDefinition enemy, int perceptionStat, int focusSlots = 0, float slotBonus = 0f)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            var result = SlotCheck.Roll(enemy.SlotCount, perceptionStat, Math.Min(focusSlots, 1), slotBonus);
            return new Roll(result.AllSucceeded ? EncounterOutcome.AmbushSuccess : EncounterOutcome.AmbushFailure, result);
        }

        // 몰래 회피: 속도 기반 슬롯 판정. 보스는 애초에 호출하면 안 된다(예외).
        public static EncounterOutcome ResolveSneak(EnemyDefinition enemy, int speedStat, int focusSlots = 0) =>
            RollSneak(enemy, speedStat, focusSlots).Outcome;

        public static Roll RollSneak(EnemyDefinition enemy, int speedStat, int focusSlots = 0)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            if (!AllowsPreCombatAvoidance(enemy))
                throw new InvalidOperationException($"{enemy.DisplayName}(보스)은 몰래 회피를 할 수 없다.");

            var result = SlotCheck.Roll(enemy.SlotCount, speedStat, Math.Min(focusSlots, 1));
            return new Roll(result.AllSucceeded ? EncounterOutcome.SneakSuccess : EncounterOutcome.SneakFailure, result);
        }

        // 전투 전 퇴각: 판정 없이 즉시 성공. 보스는 호출하면 안 된다(예외).
        public static EncounterOutcome ResolveRetreatBeforeCombat(EnemyDefinition enemy)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            if (!AllowsPreCombatAvoidance(enemy))
                throw new InvalidOperationException($"{enemy.DisplayName}(보스)은 전투 전 퇴각을 할 수 없다.");

            return EncounterOutcome.RetreatSuccess;
        }

        // 전투 중 퇴각: 2슬롯 고정, 속도 기반. 성공하면 해당 캐릭터만 전투 이탈.
        public static bool ResolveRetreatDuringCombat(int speedStat, int focusSlots = 0)
        {
            var result = SlotCheck.Roll(RetreatDuringCombatSlotCount, speedStat, Math.Min(focusSlots, 1));
            return result.AllSucceeded;
        }
    }
}
