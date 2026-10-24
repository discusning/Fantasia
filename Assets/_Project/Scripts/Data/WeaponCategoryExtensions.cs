namespace Fantasia.Data
{
    public static class WeaponCategoryExtensions
    {
        public static string Label(this WeaponCategory category) => category switch
        {
            WeaponCategory.Dagger => "단검",
            WeaponCategory.SwordSaber => "검·세이버",
            WeaponCategory.Greatsword => "양손검·대검",
            WeaponCategory.Polearm => "창·장병기",
            WeaponCategory.Bow => "활·석궁",
            WeaponCategory.Pistol => "권총",
            WeaponCategory.Musket => "화승총·룬 장총",
            WeaponCategory.Staff => "스태프",
            WeaponCategory.Grimoire => "마법서",
            WeaponCategory.Instrument => "악기",
            _ => category.ToString(),
        };

        // 스태프·마법서·악기만 마법 계열 (System.md "무기" 계열 표의 "마법 / 지력" 행).
        public static bool IsMagic(this WeaponCategory category) => category switch
        {
            WeaponCategory.Staff or WeaponCategory.Grimoire or WeaponCategory.Instrument => true,
            _ => false,
        };

        // 슬롯 확률에 쓰는 연관 스탯 (System.md 확률표: 검·단검·대검=힘, 창·활·총=인지,
        // 스태프·마법서·악기=지력).
        public static StatKind RelevantStat(this WeaponCategory category) => category switch
        {
            WeaponCategory.Dagger or WeaponCategory.SwordSaber or WeaponCategory.Greatsword => StatKind.Strength,
            WeaponCategory.Polearm or WeaponCategory.Bow or WeaponCategory.Pistol or WeaponCategory.Musket => StatKind.Perception,
            _ => StatKind.Intelligence,
        };

        // 일반 공격에서 무시하는 방어력 비율 (System.md 무기 계열 표: 창 25%, 화승총 50%).
        // 크리티컬은 이미 방어 무시이므로 여기 값을 다시 적용하지 않는다.
        public static float DefensePierceRatio(this WeaponCategory category) => category switch
        {
            WeaponCategory.Polearm => 0.25f,
            WeaponCategory.Musket => 0.5f,
            _ => 0f,
        };
    }
}
