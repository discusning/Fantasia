namespace Fantasia.Data
{
    // 장비 등급 6단계 (System.md "무기 > 등급·착용 레벨·가격 계산" 표).
    public enum EquipmentGrade
    {
        Normal,    // 노말
        Rare,      // 희귀
        Epic,      // 에픽
        Unique,    // 유니크
        Legendary, // 전설
        Special,   // 스페셜
    }

    // 등급별 추첨 확률(%)·공격 보너스 배율 R·상점 가격 배율 Q. 표 수치 그대로,
    // 코드에서 다시 손으로 베끼지 않도록 한 곳에 모아둔다.
    public static class EquipmentGradeTable
    {
        public readonly struct Row
        {
            public readonly int DrawWeight;
            public readonly float AttackBonusMultiplier; // R
            public readonly float PriceMultiplier;       // Q

            public Row(int drawWeight, float attackBonusMultiplier, float priceMultiplier)
            {
                DrawWeight = drawWeight;
                AttackBonusMultiplier = attackBonusMultiplier;
                PriceMultiplier = priceMultiplier;
            }
        }

        public static Row Get(EquipmentGrade grade) => grade switch
        {
            EquipmentGrade.Normal => new Row(50, 1f, 1f),
            EquipmentGrade.Rare => new Row(30, 1.15f, 1.5f),
            EquipmentGrade.Epic => new Row(15, 1.35f, 2.3f),
            EquipmentGrade.Unique => new Row(2, 1.6f, 4f),
            EquipmentGrade.Legendary => new Row(1, 1.9f, 7f),
            EquipmentGrade.Special => new Row(2, 1.6f, 4f),
            _ => throw new System.ArgumentOutOfRangeException(nameof(grade)),
        };
    }
}
