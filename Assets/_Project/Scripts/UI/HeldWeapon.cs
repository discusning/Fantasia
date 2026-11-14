using Fantasia.Data;
using UnityEngine;

namespace Fantasia.UI
{
    // 11/13 무기 소품 (팀원 11/12, Editor/ArtSetup.Weapons가 손뼈에 "Weapon_종류" 이름으로 붙임).
    // 장착한 무기 종류(WeaponCategory)와 같은 이름의 소품만 켬. 맞는 소품이 없는 종류(검·대검·활)는 직업 시작 무기 모델 (11/14).
    // 무기를 장착하지 않았거나(null) 적이면 프리팹 기본 그대로.
    public static class HeldWeapon
    {
        public static void Show(GameObject model, PartyMember member)
        {
            if (model == null || member == null) return;
            var weapon = member.EquippedWeapon;
            if (weapon == null) return;
            string want = "Weapon_" + PropName(weapon.Category);
            var props = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(model.GetComponentsInChildren<Transform>(true), t => t.name.StartsWith("Weapon_")));
            // 11/14 팀장 결정: 맞는 모델이 없는 종류(검·대검·활·창·악기)는 빈손 대신 그 직업 시작 무기 모델을 들게 함 — 능력치만 장착 무기대로.
            // (디자인 대검 모델은 받았지만 1막엔 안 씀)
            if (!props.Exists(t => t.name == want) && member.Job != null && member.Job.StartingWeapon != null)
                want = "Weapon_" + PropName(member.Job.StartingWeapon.Category);
            foreach (var t in props) t.gameObject.SetActive(t.name == want);
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
