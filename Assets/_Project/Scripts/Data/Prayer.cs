using System.Collections.Generic;
using System.Linq;

namespace Fantasia.Data
{
    // 십자가 석상의 기도 (System.md "십자가 석상의 기도" + Planning/EventTiles.md 10/26 확정).
    // 캐릭터당 활성 기도 1개, 새 기도를 받으면 교체. 효과는 이번 회차 동안만.
    // 1차는 6종만 선택지에 보인다 — 성급함(이동력·지원 범위)·순수(면역)는 관련 시스템 뒤에 추가.
    public enum PrayerKind { None, Focus, Wealth, Life, Shadow, Blood, Wisdom, Haste, Purity }

    public static class PrayerRules
    {
        public static readonly IReadOnlyList<PrayerKind> Offered = new[]
        {
            PrayerKind.Focus, PrayerKind.Wealth, PrayerKind.Life, PrayerKind.Shadow, PrayerKind.Blood, PrayerKind.Wisdom,
        };

        public const int FocusMaxBonus = 2;
        public const float WealthCentBonus = 0.2f;
        public const int LifeVitality = 5, LifeItemHeal = 2;
        public const int ShadowSpeed = 5;
        public const float ShadowEvasion = 0.05f;
        public const float BloodSlotBonus = 0.10f, BloodHumanoidDamage = 0.10f;
        public const int WisdomIntelligence = 5;
        public const float WisdomExpBonus = 0.25f;

        public static string Name(PrayerKind k) => k switch
        {
            PrayerKind.Focus => "집중의 기도",
            PrayerKind.Wealth => "부의 기도",
            PrayerKind.Life => "생명의 기도",
            PrayerKind.Shadow => "그림자의 기도",
            PrayerKind.Blood => "피의 기도",
            PrayerKind.Wisdom => "지혜의 기도",
            PrayerKind.Haste => "성급함의 기도",
            PrayerKind.Purity => "순수의 기도",
            _ => "기도 없음",
        };

        public static string Effect(PrayerKind k) => k switch
        {
            PrayerKind.Focus => $"최대 집중력 +{FocusMaxBonus} (최대 6) · 혼란 면역",
            PrayerKind.Wealth => $"전투 Cent +{WealthCentBonus * 100:0}% (파티당 1회) · 약탈 면역",
            PrayerKind.Life => $"활력 +{LifeVitality} · 회복 아이템 HP 회복량 +{LifeItemHeal}",
            PrayerKind.Shadow => $"속도 +{ShadowSpeed} · 회피 +{ShadowEvasion * 100:0}%p · 기습 페널티 면역",
            PrayerKind.Blood => $"공격 슬롯 성공률 +{BloodSlotBonus * 100:0}%p · 인간형 상대 피해 +{BloodHumanoidDamage * 100:0}%",
            PrayerKind.Wisdom => $"지력 +{WisdomIntelligence} · 본인 획득 경험치 +{WisdomExpBonus * 100:0}%",
            _ => "",
        };

        public static StatBlock StatBonus(PrayerKind k) => k switch
        {
            PrayerKind.Life => new StatBlock(0, 0, LifeVitality, 0, 0, 0),
            PrayerKind.Shadow => new StatBlock(0, ShadowSpeed, 0, 0, 0, 0),
            PrayerKind.Wisdom => new StatBlock(0, 0, 0, WisdomIntelligence, 0, 0),
            _ => default,
        };

        // 파티 단위 효과: 누가 받아도 1회만 (부의 기도 Cent 보너스·약탈 면역, 그림자의 기도 기습 페널티 면역)
        public static bool PartyHas(Party party, PrayerKind k) => party != null && party.Members.Any(m => !m.IsPermanentlyDead && m.Prayer == k);

        public static bool IsHumanoid(EnemyDefinition enemy) => enemy != null && !string.IsNullOrEmpty(enemy.TypeTag) && enemy.TypeTag.Contains("인간형");
    }
}
