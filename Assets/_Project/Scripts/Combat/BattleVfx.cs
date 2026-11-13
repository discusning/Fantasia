using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.Combat
{
    // 11/13 팀장 결정: 전투 이펙트 1차 (맞음 · 크리티컬 · 빗나감 · 회복 · 방어).
    // 이펙트 그림은 받은 에셋(무료 ①Magic Effects ②Hit Impact ④Quick Effects)에서 골라 Resources/VFX/{이름}.prefab 으로 둠.
    // 파일이 없으면 아무것도 안 나옴 (게임은 그대로 돎). 크리티컬이 없으면 맞음으로 대신.
    public static class BattleVfx
    {
        public const string Hit = "Hit", HitMagic = "HitMagic", HitShot = "HitShot", Crit = "Crit", CritMelee = "CritMelee", CritMagic = "CritMagic", Muzzle = "Muzzle", Miss = "Miss", Heal = "Heal", Defend = "Defend", Revive = "Revive";

        private static readonly Dictionary<string, GameObject> Cache = new();
        private static readonly Dictionary<string, string> Fallback = new() { { Crit, Hit }, { Revive, Heal }, { HitMagic, Hit }, { HitShot, Hit }, { CritMelee, Crit }, { CritMagic, Crit } };

        // 자동 플레이 점검용: 이번 실행에서 나온 이펙트 수
        public static readonly Dictionary<string, int> DevPlayed = new();

        public static bool Has(string key) => Load(key) != null;

        public static GameObject Play(string key, Vector3 position, float scale = 1f, Transform follow = null)
        {
            var prefab = Load(key);
            if (prefab == null && Fallback.TryGetValue(key, out var alt)) prefab = Load(alt);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, position, Quaternion.identity);
            if (follow != null) go.transform.SetParent(follow, true);
            go.transform.localScale = prefab.transform.localScale * scale;
            float life = 0.5f;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = false; // 일부 에셋은 반복 — 한 번만
                life = Mathf.Max(life, main.duration + main.startLifetime.constantMax + main.startDelay.constantMax);
                ps.Play(true);
            }
            Object.Destroy(go, Mathf.Min(life + 0.2f, 4f));
            DevPlayed[key] = DevPlayed.TryGetValue(key, out var n) ? n + 1 : 1;
            return go;
        }

        private static GameObject Load(string key)
        {
            if (Cache.TryGetValue(key, out var p)) return p;
            p = Resources.Load<GameObject>("VFX/" + key);
            Cache[key] = p;
            return p;
        }
    }
}
