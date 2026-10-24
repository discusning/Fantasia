using System.Linq;
using UnityEngine;

namespace Fantasia.Data
{
    // 게임에 있는 모든 무기·방어구·장신구·소모품 목록 — 전리품 추첨과 상점이 여기서 고른다.
    // Resources/ItemCatalog.asset 하나만 둔다 (Editor/ItemDataSeed가 채움).
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "Fantasia/Data/Item Catalog")]
    public class ItemCatalog : ScriptableObject
    {
        public WeaponDefinition[] Weapons = System.Array.Empty<WeaponDefinition>();
        public ArmorDefinition[] Armors = System.Array.Empty<ArmorDefinition>();
        public ConsumableDefinition[] Consumables = System.Array.Empty<ConsumableDefinition>();
        public CityShopDefinition[] Shops = System.Array.Empty<CityShopDefinition>(); // D20 도시 상점 (Editor/ShopDataSeed)
        // D27 불러오기용 조회표 (Editor/GameDataSeed가 채움)
        public JobDefinition[] Jobs = System.Array.Empty<JobDefinition>();
        public StatusEffectDefinition[] Statuses = System.Array.Empty<StatusEffectDefinition>();
        public EnemyDefinition[] Enemies = System.Array.Empty<EnemyDefinition>();

        public CityShopDefinition ShopAtHub(string hubLabel) => Shops.FirstOrDefault(s => s != null && s.HubLabel == hubLabel);

        private static ItemCatalog _current;
        public static ItemCatalog Current => _current != null ? _current : (_current = Resources.Load<ItemCatalog>("ItemCatalog"));

        public ConsumableDefinition Consumable(string id) => Consumables.FirstOrDefault(c => c != null && c.Id == id);
        public WeaponDefinition Weapon(string id) => Weapons.FirstOrDefault(w => w != null && w.Id == id);
        public ArmorDefinition Armor(string id) => Armors.FirstOrDefault(a => a != null && a.Id == id);
        public JobDefinition Job(JobId id) => Jobs.FirstOrDefault(j => j != null && j.Id == id);
        public StatusEffectDefinition Status(string id) => Statuses.FirstOrDefault(s => s != null && s.Id == id);
        public EnemyDefinition Enemy(string id) => Enemies.FirstOrDefault(e => e != null && e.Id == id);
    }

    // 자주 찾는 소모품 Id (ItemDataSeed와 같은 값)
    public static class ConsumableIds
    {
        public const string Bandage = "consumable.bandage";             // 압박 붕대
        public const string LifeElixir = "consumable.life_elixir";      // 생명의 영약
        public const string ManaElixir = "consumable.mana_elixir";      // 마력의 영약
        public const string FocusCandle = "consumable.focus_candle";    // 집중의 향초
        public const string Antidote = "consumable.antidote";           // 해독제
        public const string RuneTicket = "consumable.rune_ticket";      // 룬 기관차 탑승권
        public const string Teleport = "consumable.teleport";           // 텔레포트권
        public const string Escape = "consumable.escape";               // 탈출권
        public const string MarieDeed = "consumable.marie_deed";        // 마리의 집문서
        public const string RepairKit = "consumable.repair_kit";        // 휴대 수리 도구
        public const string Reagent = "consumable.reagent";             // 연금술 시약
    }
}
