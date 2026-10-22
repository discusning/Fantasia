using System.Collections.Generic;
using System.Linq;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D03 완료 기준: "필수값·중복 ID·잘못된 참조를 검증한다." Assets/_Project/Data
    // 아래 Fantasia.Data 자산을 전부 훑어서 문제를 Console에 모아 보여준다.
    // 기획자가 .asset 값만 고쳐도 바로 재검증할 수 있도록 메뉴 아이템으로 둔다.
    public static class GameDataValidator
    {
        [MenuItem("Fantasia/Data/Validate Game Data")]
        public static void Validate()
        {
            int errorCount = 0;

            errorCount += ValidateJobs();
            errorCount += ValidateWeapons();
            errorCount += ValidateEnemies();
            errorCount += ValidateSkills();
            errorCount += ValidateStatusEffects();

            if (errorCount == 0)
                Debug.Log("[GameDataValidator] 통과 — 문제 없음.");
            else
                Debug.LogError($"[GameDataValidator] {errorCount}건의 문제를 찾았다. 위 로그를 확인할 것.");
        }

        private static List<T> LoadAll<T>() where T : ScriptableObject
        {
            string filter = $"t:{typeof(T).Name}";
            return AssetDatabase.FindAssets(filter, new[] { "Assets/_Project/Data" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .ToList();
        }

        private static int CheckDuplicateIds(string typeName, IEnumerable<(string id, string assetName)> entries)
        {
            int errors = 0;
            var seen = new Dictionary<string, string>();
            foreach (var (id, assetName) in entries)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogError($"[GameDataValidator] {typeName} '{assetName}': Id가 비어 있다.");
                    errors++;
                    continue;
                }
                if (seen.TryGetValue(id, out var existing))
                {
                    Debug.LogError($"[GameDataValidator] {typeName} Id 중복: '{id}' ({existing} / {assetName})");
                    errors++;
                }
                else
                {
                    seen[id] = assetName;
                }
            }
            return errors;
        }

        private static int ValidateJobs()
        {
            var jobs = LoadAll<JobDefinition>();
            int errors = 0;

            // 직업은 JobId enum 자체가 식별자다. 같은 JobId를 쓰는 자산이 2개 이상이면 중복.
            errors += CheckDuplicateIds("JobDefinition",
                jobs.Select(j => (j.Id.ToString(), j.name)));

            foreach (var j in jobs)
            {
                if (string.IsNullOrWhiteSpace(j.DisplayName))
                    LogError($"JobDefinition '{j.name}': DisplayName이 비어 있다.", ref errors);
                if (j.StartingHP <= 0)
                    LogError($"JobDefinition '{j.name}': StartingHP가 0 이하다.", ref errors);
                if (j.StartingLP <= 0)
                    LogError($"JobDefinition '{j.name}': StartingLP가 0 이하다.", ref errors);
                if (j.StartingWeapon == null)
                    LogError($"JobDefinition '{j.name}': StartingWeapon 참조가 없다.", ref errors);
                if (j.AllowedWeaponCategories == null || j.AllowedWeaponCategories.Length == 0)
                    LogError($"JobDefinition '{j.name}': AllowedWeaponCategories가 비어 있다.", ref errors);
                if (j.StartingWeapon != null && j.AllowedWeaponCategories != null
                    && !j.AllowedWeaponCategories.Contains(j.StartingWeapon.Category))
                    LogError($"JobDefinition '{j.name}': StartingWeapon 계열({j.StartingWeapon.Category})이 AllowedWeaponCategories에 없다.", ref errors);

                errors += CheckStatCap(j.name, j.Stats);
            }
            return errors;
        }

        private static int CheckStatCap(string ownerName, StatBlock s)
        {
            int errors = 0;
            void Check(string label, int value)
            {
                if (value < 0 || value > StatBlock.StatCap)
                {
                    Debug.LogError($"[GameDataValidator] '{ownerName}': {label} 값 {value}이 0~{StatBlock.StatCap} 범위를 벗어났다.");
                    errors++;
                }
            }
            Check("힘", s.Strength);
            Check("속도", s.Speed);
            Check("활력", s.Vitality);
            Check("지력", s.Intelligence);
            Check("행운", s.Luck);
            Check("인지", s.Perception);
            return errors;
        }

        private static int ValidateWeapons()
        {
            var weapons = LoadAll<WeaponDefinition>();
            int errors = CheckDuplicateIds("WeaponDefinition", weapons.Select(w => (w.Id, w.name)));

            foreach (var w in weapons)
            {
                if (string.IsNullOrWhiteSpace(w.DisplayName))
                    LogError($"WeaponDefinition '{w.name}': DisplayName이 비어 있다.", ref errors);
                if (w.SlotCount != 2 && w.SlotCount != 3 && w.SlotCount != 4)
                    LogError($"WeaponDefinition '{w.name}': SlotCount가 {w.SlotCount}다 (2/3/4만 허용).", ref errors);
                if (w.AllowedJobs == null || w.AllowedJobs.Length == 0)
                    LogError($"WeaponDefinition '{w.name}': AllowedJobs가 비어 있다.", ref errors);
                if (w.Price < 0)
                    LogError($"WeaponDefinition '{w.name}': Price가 음수다.", ref errors);
            }
            return errors;
        }

        private static int ValidateEnemies()
        {
            var enemies = LoadAll<EnemyDefinition>();
            int errors = CheckDuplicateIds("EnemyDefinition", enemies.Select(e => (e.Id, e.name)));

            foreach (var e in enemies)
            {
                if (string.IsNullOrWhiteSpace(e.DisplayName))
                    LogError($"EnemyDefinition '{e.name}': DisplayName이 비어 있다.", ref errors);
                if (e.MaxHP <= 0)
                    LogError($"EnemyDefinition '{e.name}': MaxHP가 0 이하다.", ref errors);

                int expectedSlots = e.Size == EnemySize.Small ? 2 : e.Size == EnemySize.Medium ? 3 : 4;
                if (e.SlotCount != expectedSlots)
                    LogError($"EnemyDefinition '{e.name}': Size={e.Size}인데 SlotCount={e.SlotCount} (기대값 {expectedSlots}).", ref errors);

                if (e.SlotSuccessRate <= 0f || e.SlotSuccessRate > 1f)
                    LogError($"EnemyDefinition '{e.name}': SlotSuccessRate가 {e.SlotSuccessRate:P0}로 범위를 벗어났다.", ref errors);
            }
            return errors;
        }

        private static int ValidateSkills()
        {
            var skills = LoadAll<SkillDefinition>();
            int errors = CheckDuplicateIds("SkillDefinition", skills.Select(s => (s.Id, s.name)));

            foreach (var s in skills)
            {
                if (string.IsNullOrWhiteSpace(s.DisplayName))
                    LogError($"SkillDefinition '{s.name}': DisplayName이 비어 있다.", ref errors);
                if (string.IsNullOrWhiteSpace(s.EffectDescription))
                    LogError($"SkillDefinition '{s.name}': EffectDescription이 비어 있다.", ref errors);
                if (s.Type == SkillType.Passive && (s.FocusCost != 0 || s.HPCost != 0 || s.MPCost != 0))
                    LogError($"SkillDefinition '{s.name}': 패시브인데 비용이 설정돼 있다.", ref errors);
            }
            return errors;
        }

        private static int ValidateStatusEffects()
        {
            var statuses = LoadAll<StatusEffectDefinition>();
            int errors = CheckDuplicateIds("StatusEffectDefinition", statuses.Select(s => (s.Id, s.name)));

            foreach (var s in statuses)
            {
                if (string.IsNullOrWhiteSpace(s.DisplayName))
                    LogError($"StatusEffectDefinition '{s.name}': DisplayName이 비어 있다.", ref errors);
                if (s.DurationTurns < -1)
                    LogError($"StatusEffectDefinition '{s.name}': DurationTurns가 {s.DurationTurns}다 (-1 미만 불가).", ref errors);
                if (string.IsNullOrWhiteSpace(s.RuleDescription))
                    LogError($"StatusEffectDefinition '{s.name}': RuleDescription이 비어 있다.", ref errors);
            }
            return errors;
        }

        private static void LogError(string message, ref int errors)
        {
            Debug.LogError($"[GameDataValidator] {message}");
            errors++;
        }
    }
}
