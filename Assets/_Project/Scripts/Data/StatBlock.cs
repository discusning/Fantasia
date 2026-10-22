using System;

namespace Fantasia.Data
{
    public enum StatKind
    {
        Strength,     // 힘
        Speed,        // 속도
        Vitality,     // 활력
        Intelligence, // 지력
        Luck,         // 행운
        Perception,   // 인지
    }

    // 힘·속도·활력·지력·행운·인지 (System.md "능력치"). 상한 99는 여기서 강제하지 않고
    // GameDataValidator가 자산 검증 시점에 확인한다 (도박가 행운 100 예외는 패시브
    // 보너스로 처리되므로 기본 스탯 자체는 그대로 99 상한).
    [Serializable]
    public struct StatBlock
    {
        public int Strength;      // 힘
        public int Speed;         // 속도
        public int Vitality;      // 활력
        public int Intelligence;  // 지력
        public int Luck;          // 행운
        public int Perception;    // 인지

        public const int StatCap = 99;

        public StatBlock(int strength, int speed, int vitality, int intelligence, int luck, int perception)
        {
            Strength = strength;
            Speed = speed;
            Vitality = vitality;
            Intelligence = intelligence;
            Luck = luck;
            Perception = perception;
        }

        public int Get(StatKind kind) => kind switch
        {
            StatKind.Strength => Strength,
            StatKind.Speed => Speed,
            StatKind.Vitality => Vitality,
            StatKind.Intelligence => Intelligence,
            StatKind.Luck => Luck,
            StatKind.Perception => Perception,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        // 장비 착용 보정처럼 음수가 섞인 더하기 (예: 대검 속도 −4).
        public static StatBlock operator +(StatBlock a, StatBlock b) => new(
            a.Strength + b.Strength,
            a.Speed + b.Speed,
            a.Vitality + b.Vitality,
            a.Intelligence + b.Intelligence,
            a.Luck + b.Luck,
            a.Perception + b.Perception);

        // 최종 스탯은 0~99 (System.md "능력치" 상한). 도박가 행운 100 예외는 패시브 구현 시(D30) 처리.
        public StatBlock ClampToCap() => new(
            Math.Clamp(Strength, 0, StatCap),
            Math.Clamp(Speed, 0, StatCap),
            Math.Clamp(Vitality, 0, StatCap),
            Math.Clamp(Intelligence, 0, StatCap),
            Math.Clamp(Luck, 0, StatCap),
            Math.Clamp(Perception, 0, StatCap));
    }
}
