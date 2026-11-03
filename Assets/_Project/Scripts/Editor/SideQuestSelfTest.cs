using System.Linq;
using Fantasia.Board;
using Fantasia.Core;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 런던 선택 의뢰 2개 (1막 기획 결정서 04 A3): 목표 칸이 지도·적 칸 조합과 맞는지, 수락 전 승리 소급 인정,
    // 두 칸 승리 → 80 Cent + 생존 동료 각 경험치 40 1회, 다시 정산해도 반복 없음, 한 칸만 이기면 진행 중.
    public static class SideQuestSelfTest
    {
        [MenuItem("Fantasia/Data/Run Side Quest Self-Test")]
        public static void Run()
        {
            int e = 0;
            var map = WorldMapData.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(WorldMapSelfTest.MapPath).text);
            foreach (var q in SideQuests.All)
                foreach (var label in q.Targets)
                    e += Check(map.TileAt(label).Kind == MapTileKind.Enemy && Fantasia.World.MapTileRules.EncounterAt(label) != null, $"{q.Id} 목표 {label} = 런던 적 칸");
            e += Check(Fantasia.World.MapTileRules.EncounterAt("P04").EnemyIds.Concat(Fantasia.World.MapTileRules.EncounterAt("Q01").EnemyIds).Count() == 4, "의뢰 1 목표 역병 쥐 총 4마리");

            BoardSession.EnsureExists();
            var s = BoardSession.Instance;
            s.ResetForNewGame();
            var jobs = ItemCatalog.Current.Jobs.Where(j => j != null).OrderBy(j => j.Id).Take(3).ToArray();
            var party = new Party();
            foreach (var j in jobs) party.AddMember(new PartyMember(j));
            s.Party = party;
            WorldMapData.TryParseLabel("P04", out var p04);
            WorldMapData.TryParseLabel("Q01", out var q01);
            WorldMapData.TryParseLabel("O07", out var o07);
            var q1 = SideQuests.All[0];
            var q2 = SideQuests.All[1];

            // 수락 전에 이긴 칸 = 소급 인정. 두 칸 다 이겼으면 수락하면서 정산
            s.ClearedEncounters.Add(p04);
            s.ClearedEncounters.Add(q01);
            int cent = party.Cent, exp = party.Members[0].Experience, level = party.Members[0].Level;
            e += Check(SideQuests.SettleAll(s).Count == 0, "수락 전에는 정산 없음");
            SideQuests.Accept(s, q1);
            var done = SideQuests.SettleAll(s);
            e += Check(done.Count == 1 && done[0] == q1 && SideQuests.IsDone(s, q1), "수락 → 소급 인정 → 완료");
            e += Expect("의뢰 보상 Cent", party.Cent - cent, SideQuests.RewardCent);
            e += Check(party.Members[0].Level > level || party.Members[0].Experience - exp == SideQuests.RewardExp, "생존 동료 경험치 40");
            cent = party.Cent;
            e += Check(SideQuests.SettleAll(s).Count == 0 && party.Cent == cent, "다시 정산해도 보상 반복 없음");
            e += Check(!SideQuests.Accept(s, q1), "같은 의뢰 다시 수락 불가");

            // 한 칸만 이기면 진행 중 (1/2)
            SideQuests.Accept(s, q2);
            s.ClearedEncounters.Add(o07);
            e += Check(SideQuests.SettleAll(s).Count == 0 && SideQuests.TargetsWon(s, q2) == 1 && !SideQuests.IsDone(s, q2), "의뢰 2: 1/2칸 → 진행 중");
            e += Check(s.Quests.Any(x => x.Title == q2.Title) && s.CompletedQuests.Any(x => x.Title == q1.Title), "사건 기록: 진행 중·완료 목록");

            s.ResetForNewGame();
            if (e == 0) Debug.Log("[SideQuestSelfTest] 전부 통과.");
            else Debug.LogError($"[SideQuestSelfTest] {e}건 실패.");
        }

        private static int Check(bool ok, string what)
        {
            if (ok) return 0;
            Debug.LogError($"[SideQuestSelfTest] {what} — 실패");
            return 1;
        }

        private static int Expect(string what, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[SideQuestSelfTest] {what}: 기대 {expected}, 실제 {actual}");
            return 1;
        }
    }
}
