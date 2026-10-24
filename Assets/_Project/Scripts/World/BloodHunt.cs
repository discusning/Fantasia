using System;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Board;
using Fantasia.Data;

namespace Fantasia.World
{
    // 피의 사냥 진행 상태 (BoardSession이 들고 씬을 오가도 유지).
    public class BloodHuntState
    {
        public bool Active;
        public int AllyTurnsLeft;                       // 남은 아군 턴 (3 → 0이면 실패)
        public readonly List<HexCoord> Reapers = new(); // 보드 위 사신 위치
        public int GraceRounds;                         // 끝난 뒤 피의 흔적이 안 오르는 월드 라운드
        public bool ReaperKilled;                       // 전투에서 사신을 잡았음 → 보드 복귀 때 성공 처리

        public void Clear()
        {
            Active = false;
            AllyTurnsLeft = 0;
            Reapers.Clear();
            ReaperKilled = false;
        }
    }

    // D24 피의 흔적 G · 피의 사냥 + D25 영혼 수확 S (Docs/Planning/System.md 개정 절 + Decisions.md 「피의 사냥 정리」).
    // 보드와 분리된 규칙 — 칸 고르기·거리는 WorldMapData로, 화면 처리는 BoardTestController가 한다.
    public static class BloodHuntRules
    {
        public const int TrailPerRound = 10;   // 적 턴마다 G +10
        public const int TrailMax = 100;
        public const int ReaperCount = 4;
        public const int AllyTurnLimit = 3;
        public const int ReaperStepsPerAllyTurn = 2;
        public const int GraceRoundsAfter = 2;  // ☆ 원 System.md 유예 — 기획 확인 대기
        public const int MinSpawnDistance = 3;  // ☆ 바로 옆 생성 방지
        public const int SuccessCent = 100;     // ☆ 성공 보상 (+ 생명의 영약 1개)
        public const int SoulPerRound = 2;      // D25: 월드 라운드마다 S +2
        public const int SoulMax = 100;
        public const int RedSkyThreshold = 75;  // D25: S 75 이상 붉은 하늘

        // 적 턴 처리. 반환: 로그 줄. failedMember: 실패로 HP·MP 1이 된 파티원(없으면 null).
        public static List<string> OnEnemyTurn(BloodHuntState hunt, ref int bloodTrail, ref int soulHarvest, Party party,
            WorldMapData map, HexCoord partyPos, Random rng, out PartyMember failedMember)
        {
            var log = new List<string>();
            failedMember = null;

            // D25 영혼 수확: 월드 라운드마다 +2
            int soulBefore = soulHarvest;
            soulHarvest = Math.Min(SoulMax, soulHarvest + SoulPerRound);
            foreach (int w in new[] { 25, 50, RedSkyThreshold })
                if (soulBefore < w && soulHarvest >= w) log.Add($"영혼 수확 {w} 도달{(w == RedSkyThreshold ? " — 하늘이 붉게 물든다" : "")}");

            if (hunt.Active)
            {
                hunt.AllyTurnsLeft--;
                if (hunt.AllyTurnsLeft <= 0)
                {
                    failedMember = Fail(hunt, ref bloodTrail, party, rng);
                    log.Add(failedMember != null
                        ? $"피의 사냥 실패 — 사신의 치명적 공격: {failedMember.Job.DisplayName} HP·MP 1"
                        : "피의 사냥 실패 — 사신이 사라졌다");
                }
                else log.Add($"피의 사냥: 남은 아군 턴 {hunt.AllyTurnsLeft}");
                return log;
            }

            if (hunt.GraceRounds > 0)
            {
                hunt.GraceRounds--;
                return log;
            }

            bloodTrail = Math.Min(TrailMax, bloodTrail + TrailPerRound);
            if (bloodTrail >= TrailMax && map != null)
            {
                var spawns = PickSpawnTiles(map, partyPos, rng);
                if (spawns.Count > 0)
                {
                    hunt.Clear();
                    hunt.Active = true;
                    hunt.AllyTurnsLeft = AllyTurnLimit;
                    hunt.Reapers.AddRange(spawns);
                    log.Add($"피의 사냥! 인조 그림자 사신 {spawns.Count}마리 출현 — {AllyTurnLimit}아군 턴 안에 1마리라도 처치");
                }
            }
            return log;
        }

        // 실패: 생존 파티원 1명 균등 추첨 HP·MP 1 (MP 없는 직업은 HP만), G 0, 사냥 끝 (System.md 개정 절)
        private static PartyMember Fail(BloodHuntState hunt, ref int bloodTrail, Party party, Random rng)
        {
            PartyMember victim = null;
            var alive = party?.AliveMembers.ToList() ?? new List<PartyMember>();
            if (alive.Count > 0)
            {
                victim = alive[rng.Next(alive.Count)];
                if (victim.CurrentHP > 1) victim.TakeDamage(victim.CurrentHP - 1);
                if (victim.CurrentMP > 1) victim.TrySpendMP(victim.CurrentMP - 1);
            }
            End(hunt, ref bloodTrail);
            return victim;
        }

        // 1마리라도 처치: G 0, 사냥 끝, 보상
        public static string Succeed(BloodHuntState hunt, ref int bloodTrail, Party party, ItemCatalog catalog)
        {
            End(hunt, ref bloodTrail);
            party?.AddCent(SuccessCent);
            var elixir = catalog != null ? catalog.Consumable(ConsumableIds.LifeElixir) : null;
            if (elixir != null && party != null) party.Inventory.AddConsumable(elixir);
            return $"피의 사냥 성공 — 사신을 처치했다. 보상 {SuccessCent} Cent{(elixir != null ? " + 생명의 영약" : "")}";
        }

        private static void End(BloodHuntState hunt, ref int bloodTrail)
        {
            hunt.Clear();
            bloodTrail = 0;
            hunt.GraceRounds = GraceRoundsAfter;
        }

        // 파티가 있는 지역의 이동 가능한 빈 칸(특수 칸 제외)에서 무작위 4칸, 파티에서 3칸 이상
        public static List<HexCoord> PickSpawnTiles(WorldMapData map, HexCoord partyPos, Random rng)
        {
            int region = map.RegionOf(partyPos);
            var candidates = map.Tiles.Values
                .Where(t => t.Kind == MapTileKind.Walkable && map.RegionOf(t.Coord) == region && t.Coord.DistanceTo(partyPos) >= MinSpawnDistance)
                .Select(t => t.Coord).ToList();
            var picked = new List<HexCoord>();
            while (picked.Count < ReaperCount && candidates.Count > 0)
            {
                int i = rng.Next(candidates.Count);
                picked.Add(candidates[i]);
                candidates.RemoveAt(i);
            }
            return picked;
        }

        // 아군 턴 시작: 사신마다 파티 쪽으로 최대 2칸 (이동 가능 칸, 다른 사신과 겹치지 않음).
        // 파티 칸에 닿은 사신의 번호를 돌려준다 (-1 = 없음) — 그 사신과 바로 전투.
        public static int MoveReapers(BloodHuntState hunt, WorldMapData map, HexCoord partyPos, Func<HexCoord, bool> exitOpen = null)
        {
            if (!hunt.Active) return -1;
            var dist = DistancesTo(map, partyPos, exitOpen);
            for (int i = 0; i < hunt.Reapers.Count; i++)
            {
                for (int step = 0; step < ReaperStepsPerAllyTurn; step++)
                {
                    var here = hunt.Reapers[i];
                    if (here.Equals(partyPos)) break;
                    int best = dist.TryGetValue(here, out int d) ? d : int.MaxValue;
                    HexCoord? next = null;
                    foreach (var n in map.PassableNeighbors(here, exitOpen))
                    {
                        if (!dist.TryGetValue(n, out int nd) || nd >= best) continue;
                        if (!n.Equals(partyPos) && hunt.Reapers.Contains(n)) continue;
                        best = nd;
                        next = n;
                    }
                    if (next == null) break;
                    hunt.Reapers[i] = next.Value;
                }
                if (hunt.Reapers[i].Equals(partyPos)) return i;
            }
            return -1;
        }

        private static Dictionary<HexCoord, int> DistancesTo(WorldMapData map, HexCoord target, Func<HexCoord, bool> exitOpen)
        {
            var dist = new Dictionary<HexCoord, int> { [target] = 0 };
            var queue = new Queue<HexCoord>();
            queue.Enqueue(target);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var n in map.PassableNeighbors(c, exitOpen))
                {
                    if (dist.ContainsKey(n)) continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }
    }
}
