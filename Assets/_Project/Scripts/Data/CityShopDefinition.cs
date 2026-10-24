using System;
using UnityEngine;

namespace Fantasia.Data
{
    // 판매 단계 (City_Shop.md §4·§6): 도시 해금 시 / 2막 진입(검시관 처치) 후 / 고성 진입(세베린 처치) 후
    public enum ShopStage
    {
        Base,
        Act2,
        Castle,
    }

    [Serializable]
    public class ShopEntry
    {
        public ScriptableObject Item;   // WeaponDefinition · ArmorDefinition · ConsumableDefinition
        public int MaxStock;            // 재입고 후 최대 재고 (파티 전체가 함께 살 수 있는 수)
        public ShopStage Stage;
        public bool Limited;            // 재입고 없는 한정품 (생명의 월계관 등) — 플레이 전체 1회

        public string ItemId => Item switch
        {
            WeaponDefinition w => w.Id,
            ArmorDefinition a => a.Id,
            ConsumableDefinition c => c.Id,
            _ => null,
        };

        public string DisplayName => Item switch
        {
            WeaponDefinition w => w.DisplayName,
            ArmorDefinition a => a.DisplayName,
            ConsumableDefinition c => c.DisplayName,
            _ => "?",
        };

        public int Price => Item switch
        {
            WeaponDefinition w => w.Price,
            ArmorDefinition a => a.Price,
            ConsumableDefinition c => c.Price,
            _ => 0,
        };
    }

    // 도시 상점 한 곳 (Docs/Planning/City.md · City_Shop.md). Editor/ShopDataSeed가 판매표대로 채운다.
    [CreateAssetMenu(fileName = "Shop", menuName = "Fantasia/Data/City Shop Definition")]
    public class CityShopDefinition : ScriptableObject
    {
        public string Id;            // city.london 등
        public string DisplayName;   // 런던
        public string HubLabel;      // 월드맵 거점 칸 (S05)
        public string Landmark;      // 그리니치 전망대·수사대 상점
        public ShopEntry[] Entries = Array.Empty<ShopEntry>();
    }
}
