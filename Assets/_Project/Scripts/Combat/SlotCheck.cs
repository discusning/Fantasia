using Fantasia.Dice;
using UnityEngine;

namespace Fantasia.Combat
{
    // 슬롯 판정 공통 엔진 (System.md "공격·조우·회피 확률표"). 무기 공격 크리티컬,
    // 기습·몰래 회피·퇴각처럼 "슬롯 전부 성공 여부"가 필요한 모든 판정이 이걸 공유한다.
    public static class SlotCheck
    {
        public const float MinChance = 0.05f;
        public const float MaxChance = 0.95f;

        // 슬롯 1개 기본 확률: 2슬롯 50%, 3슬롯 100/3%, 4슬롯 25%.
        public static float BaseSlotChance(int slotCount) => slotCount switch
        {
            2 => 0.5f,
            3 => 100f / 3f / 100f,
            4 => 0.25f,
            _ => throw new System.ArgumentOutOfRangeException(nameof(slotCount), "슬롯 수는 2/3/4만 허용한다."),
        };

        // p = clamp(기본 확률 + 연관 스탯×0.1%p + 장비·스킬 보정, 5~95%)
        public static float FinalSlotChance(int slotCount, int relevantStat, float extraBonus = 0f)
        {
            float raw = BaseSlotChance(slotCount) + relevantStat * 0.001f + extraBonus;
            return Mathf.Clamp(raw, MinChance, MaxChance);
        }

        public readonly struct Result
        {
            public readonly int SlotCount;
            public readonly int SuccessCount;
            public readonly bool AllSucceeded; // 전체 성공 = 크리티컬/기습·회피 성공 등 "판정 성공" 기준

            public Result(int slotCount, int successCount)
            {
                SlotCount = slotCount;
                SuccessCount = successCount;
                AllSucceeded = successCount == slotCount;
            }
        }

        // focusSlots: 집중력으로 확정 성공시킬 슬롯 개수. 호출하는 쪽에서 "행동당 1개"
        // 제한을 지켜서 넘겨야 한다 (여기서는 강제하지 않음 — 여러 판정 유형이 공유하는
        // 저수준 엔진이라 상위 규칙은 호출부 책임).
        public static Result Roll(int slotCount, int relevantStat, int focusSlots = 0, float extraBonus = 0f)
        {
            return RollWithChance(slotCount, FinalSlotChance(slotCount, relevantStat, extraBonus), focusSlots);
        }

        // 확률을 직접 주는 판정. 적은 스탯 공식 대신 자신의 슬롯별 확률(소형 45% 등)을
        // 그대로 쓴다 (System.md "기존 일반 적의 보완 스탯").
        public static Result RollWithChance(int slotCount, float chance, int focusSlots = 0)
        {
            int success = 0;
            for (int i = 0; i < slotCount; i++)
            {
                bool forced = i < focusSlots;
                bool ok = forced || DiceRoller.RollChance(chance);
                if (ok) success++;
            }
            return new Result(slotCount, success);
        }

        // 전체 성공(크리티컬) 확률 = p^(n − 확정 슬롯 수). UI의 "최종 크리티컬 확률" 표시용
        // (System.md: 집중력 없이 (p)^n, 1개 확정 시 (p)^(n−1)).
        public static float AllSuccessProbability(int slotCount, float chance, int focusSlots = 0)
        {
            int rolled = System.Math.Max(0, slotCount - focusSlots);
            return Mathf.Pow(chance, rolled);
        }

        // 성공 개수별 확률 분포. [k] = 정확히 k개 성공할 확률 (확정 슬롯은 항상 성공으로 셈).
        // 조우·공격 UI의 "판정 확률" 표 표시용.
        public static float[] SuccessCountDistribution(int slotCount, float chance, int focusSlots = 0)
        {
            int forced = System.Math.Clamp(focusSlots, 0, slotCount);
            int rolled = slotCount - forced;
            var dist = new float[slotCount + 1];
            for (int s = 0; s <= rolled; s++)
            {
                dist[forced + s] = Binomial(rolled, s) * Mathf.Pow(chance, s) * Mathf.Pow(1f - chance, rolled - s);
            }
            return dist;
        }

        private static float Binomial(int n, int k)
        {
            float result = 1f;
            for (int i = 1; i <= k; i++) result = result * (n - k + i) / i;
            return result;
        }
    }
}
