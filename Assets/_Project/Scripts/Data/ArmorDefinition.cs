using UnityEngine;

namespace Fantasia.Data
{
    // 착용 슬롯 5칸 (System.md "무기 > 종류·착용·공격 규칙": 무기 1·몸통 1·머리 1·반지 1·목걸이 1).
    public enum GearSlot
    {
        Weapon,
        Body,
        Head,
        Ring,
        Necklace,
    }

    public static class GearSlotExtensions
    {
        public static string Label(this GearSlot slot) => slot switch
        {
            GearSlot.Weapon => "무기",
            GearSlot.Body => "몸통",
            GearSlot.Head => "머리",
            GearSlot.Ring => "반지",
            GearSlot.Necklace => "목걸이",
            _ => slot.ToString(),
        };
    }

    // 방어구·장신구 정의 (System.md "방어구와 장신구 · 17종"). 표의 값은 그 행의 등급·레벨 기준 완성값이다.
    // 랜덤 전리품에서 다른 등급이 나오면 GearItem이 방어·최대 HP·MP만 R 비율로 다시 계산한다.
    [CreateAssetMenu(fileName = "Armor", menuName = "Fantasia/Data/Armor Definition")]
    public class ArmorDefinition : ScriptableObject
    {
        [Header("식별")]
        public string Id;
        public string DisplayName;
        public GearSlot Slot = GearSlot.Body; // Body·Head·Ring·Necklace
        public Sprite Icon;                    // 비우면 UI가 슬롯별 임시 아이콘

        [Header("표 기준 등급·레벨")]
        public EquipmentGrade Grade = EquipmentGrade.Normal;
        public int WearLevel = 1;
        public int Price;

        [Header("효과 (등급 R로 다시 곱하는 값)")]
        public int PhysicalDefense;
        public int MagicDefense;
        public int MaxHPBonus;
        public int MaxMPBonus;

        [Header("효과 (등급으로 다시 곱하지 않는 값)")]
        public StatBlock StatBonus;
        public int MaxFocusBonus;
        [TextArea] public string UniqueEffectDescription; // 고유 효과 원문 (예: "중독 틱 피해 −1") — 계산 연결은 필요할 때

        // 랜덤 전리품에 넣지 않는 장비 (스토리 고유 등)
        public bool ExcludeFromLoot;
    }
}
