using Fantasia.Data;
using UnityEngine;

namespace Fantasia.UI
{
    // 11/13 무기 소품 (팀원 11/12, Editor/ArtSetup.Weapons가 손뼈에 "Weapon_종류" 이름으로 붙임).
    // 장착한 무기 종류(WeaponCategory)와 같은 이름의 소품만 켬. 맞는 소품이 없는 종류(검·대검·활)는 빈손.
    // 무기를 장착하지 않았거나(null) 적이면 프리팹 기본 그대로.
    public static class HeldWeapon
    {
        public static void Show(GameObject model, PartyMember member)
        {
            if (model == null || member == null) return;
            var weapon = member.EquippedWeapon;
            if (weapon == null) return;
            string want = "Weapon_" + PropName(weapon.Category);
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Weapon_")) t.gameObject.SetActive(t.name == want);
        }

        private static string PropName(WeaponCategory c) => c switch
        {
            WeaponCategory.Dagger => "Dagger",
            WeaponCategory.Pistol => "Pistol",
            WeaponCategory.Musket => "Musket",
            WeaponCategory.Staff => "Staff",
            WeaponCategory.Grimoire => "Grimoire",
            _ => "-",
        };
    }
}
