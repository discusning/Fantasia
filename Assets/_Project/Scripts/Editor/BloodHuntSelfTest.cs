using System.Linq;
using Fantasia.Board;
using Fantasia.Data;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D24 피의 흔적·피의 사냥 / D25 영혼 수확 — Decisions.md 「피의 사냥 정리」·System.md 개정 절과 대조.
    public static class BloodHuntSelfTest
    {
        [MenuItem("Fantasia/Data/Run Blood Hunt Self-Test")]
        public static void Run()
        {
            var mapAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(WorldMapSelfTest.MapPath);
            var job = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_역병의사.asset");
            var job2 = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_명탐정.asset");
            if (mapAsset == null || job == null || job2 == null) { Debug.LogError("[BloodHuntSelfTest] 맵·직업 자산 없음"); return; }
            var map = WorldMapData.Parse(mapAsset.text);
            var start = map.TileAt("S05").Coord;
            var rng = new System.Random(42);
            int e = 0;

            var hunt = new BloodHuntState();
            var party = new Party();
            party.AddMember(new PartyMember(job));
            party.AddMember(new PartyMember(job2));
            int g = 0, s = 0;

            // 적 턴마다 G +10, S +2 → 10번째 적 턴에 G 100 → 피의 사냥
            for (int round = 1; round <= 9; round++)
            {
                BloodHuntRules.OnEnemyTurn(hunt, ref g, ref s, party, map, start, rng, out _);
                if (hunt.Active) { e += Fail($"{round}라운드에 너무 일찍 시작"); break; }
            }
            e += Expect("9라운드 G", g, 90);
            e += Expect("9라운드 S", s, 18);
            BloodHuntRules.OnEnemyTurn(hunt, ref g, ref s, party, map, start, rng, out _);
            e += Check(hunt.Active, "10라운드 G 100 → 피의 사냥 시작");
            e += Expect("사신 4마리", hunt.Reapers.Count, 4);
            e += Expect("남은 아군 턴 3", hunt.AllyTurnsLeft, 3);
            int london = map.RegionOf(start);
            e += Check(hunt.Reapers.All(c => map.RegionOf(c) == london), "사신은 파티가 있는 지역(런던)에만");
            e += Check(hunt.Reapers.All(c => map.Tiles[c].Kind == MapTileKind.Walkable), "사신은 빈 이동 칸에만 (특수 칸 X)");
            e += Check(hunt.Reapers.All(c => c.DistanceTo(start) >= BloodHuntRules.MinSpawnDistance), "파티에서 3칸 이상");
            e += Check(hunt.Reapers.Distinct().Count() == 4, "사신끼리 겹치지 않음");

            // 아군 턴마다 2칸씩 다가옴
            var before = hunt.Reapers.Select(c => map.Distance(c, start)).ToList();
            BloodHuntRules.MoveReapers(hunt, map, start);
            var after = hunt.Reapers.Select(c => map.Distance(c, start)).ToList();
            e += Check(Enumerable.Range(0, 4).All(i => after[i] == System.Math.Max(0, before[i] - 2) || after[i] == before[i] - 1),
                $"사신이 최대 2칸 접근 (전 {string.Join(",", before)} → 후 {string.Join(",", after)})");

            // 3아군 턴 동안 못 잡으면 실패: 1명 HP·MP 1, G 0, 사냥 끝
            int sBefore = s;
            BloodHuntRules.OnEnemyTurn(hunt, ref g, ref s, party, map, start, rng, out _);
            BloodHuntRules.OnEnemyTurn(hunt, ref g, ref s, party, map, start, rng, out _);
            e += Check(hunt.Active && hunt.AllyTurnsLeft == 1, "2번 지나도 진행 중 (남은 1)");
            BloodHuntRules.OnEnemyTurn(hunt, ref g, ref s, party, map, start, rng, out var victim);
            e += Check(!hunt.Active, "3아군 턴 뒤 실패로 종료");
            e += Check(victim != null && victim.CurrentHP == 1 && (victim.MaxMP == 0 || victim.CurrentMP == 1), "치명적 공격: 1명 HP·MP 1");
            e += Expect("실패 후 G 0", g, 0);
            e += Expect("실패해도 S는 라운드 +2만 (실패 +5 없음)", s, sBefore + 6);

            // 유예 2라운드 동안 G 안 오름
            BloodHuntRules.OnEnemyTurn(hunt, ref g, ref s, party, map, start, rng, out _);
            BloodHuntRules.OnEnemyTurn(hunt, ref g, ref s, party, map, start, rng, out _);
            e += Expect("유예 2라운드 G", g, 0);
            BloodHuntRules.OnEnemyTurn(hunt, ref g, ref s, party, map, start, rng, out _);
            e += Expect("유예 끝나면 G +10", g, 10);

            // 성공: 1마리 처치 → G 0, 종료, 100 Cent + 생명의 영약
            hunt.Clear();
            hunt.Active = true;
            hunt.AllyTurnsLeft = 2;
            g = 100;
            int cent = party.Cent;
            BloodHuntRules.Succeed(hunt, ref g, party, ItemCatalog.Current);
            e += Check(!hunt.Active && g == 0, "성공 → 종료, G 0");
            e += Expect("성공 보상 Cent", party.Cent, cent + 100);
            e += Expect("성공 보상 영약", party.Inventory.CountOf(ItemCatalog.Current.Consumable(ConsumableIds.LifeElixir)), 1);

            // 파티 칸에 닿으면 그 사신 번호
            hunt.Clear();
            hunt.Active = true;
            hunt.AllyTurnsLeft = 3;
            hunt.Reapers.Add(map.TileAt("S07").Coord); // S05에서 2칸
            e += Expect("2칸 거리 사신이 파티 칸 도착", BloodHuntRules.MoveReapers(hunt, map, start), 0);

            // S 상한 100, 75에서 붉은 하늘 문구
            hunt.Clear();
            int s2 = 74, g2 = 0;
            var log = BloodHuntRules.OnEnemyTurn(hunt, ref g2, ref s2, party, map, start, rng, out _);
            e += Check(log.Any(l => l.Contains("붉게")), "S 75 도달 → 붉은 하늘 문구");
            s2 = 99;
            BloodHuntRules.OnEnemyTurn(hunt, ref g2, ref s2, party, map, start, rng, out _);
            e += Expect("S 상한 100", s2, 100);

            if (e == 0) Debug.Log("[BloodHuntSelfTest] 전부 통과.");
            else Debug.LogError($"[BloodHuntSelfTest] {e}건 실패.");
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[BloodHuntSelfTest] {label}: 기대 {expected}, 실제 {actual}");
            return 1;
        }

        private static int Check(bool ok, string label) => ok ? 0 : Fail(label);

        private static int Fail(string label)
        {
            Debug.LogError($"[BloodHuntSelfTest] 실패: {label}");
            return 1;
        }
    }
}
