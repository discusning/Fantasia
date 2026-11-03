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
        public int AllyTurnsLeft;                       // 남은 월드 라운드 (3 → 0이면 실패). 저장 키 호환으로 이름 유지
        public readonly List<HexCoord> Reapers = new(); // 보드 위 남은 사신 위치
        public int GraceRounds;                         // 끝난 뒤 피의 흔적이 안 오르는 월드 라운드
        public bool ReaperKilled;                       // 전투에서 사신을 잡았음 → 보드 복귀 때 그 사신 제거·보상
        public bool JustSpawned;                        // 생성 직후 아군 턴에는 사신이 움직이지 않음

        public void Clear()
        {
            Active = false;
            AllyTurnsLeft = 0;
            Reapers.Clear();
            ReaperKilled = false;
            JustSpawned = false;
        }
    }

    // D24 피의 흔적 G · 피의 사냥 + D25 영혼 수확 S.
    // 기준: 1막 게임기획 보고서 11/3 §06 (팀장 확정) — 사신 3마리·3월드 라운드·전부 처치·라운드당 1칸.
    // 보드와 분리된 규칙 — 칸 고르기·거리는 WorldMapData로, 화면 처리는 BoardTestController가 한다.
    public static class BloodHuntRules
    {
        public const int TrailPerRound = 10;   // 적 턴마다 G +10
        public const int TrailMax = 100;
        public const int ReaperCount = 3;
        public const int RoundLimit = 3;        // 보고서 권고 (결정서 2라운드 → 1턴 1전투라 3라운드)
        public const int ReaperStepsPerRound = 1;
        public const int GraceRoundsAfter = 2;
        public const int SpawnMinDistance = 1;  // 파티 근처 1~2칸 (모자라면 더 먼 칸)
        public const int SpawnMaxDistance = 2;
        public const int SuccessCent = 100;     // 3마리 전부 처치 보상 (+ 생명의 영약 1개)
        public const int FailSoulPenalty = 5;   // 실패 시 S +5
        public const int SoulPerRound = 2;      // D25: 월드 라운드마다 S +2
        public const int SoulMax = 100;
        public const int RedSkyThreshold = 75;  // D25: S 75 이상 붉은 하늘

        // 적 턴 처리. 반환: 로그 줄. failedMember: 실패로 HP 1이 된 파티원(없으면 null).
        public static List<string> OnEnemyTurn(BloodHuntState hunt, ref int bloodTrail, ref int soulHarvest, Party party,
            WorldMapData map, HexCoord partyPos, Random rng, out PartyMember failedMember)
        {
            var log = new List<string>();
            failedMember = null;

            // D25 영혼 수확: 월드 라운드마다 +2 (유예 중에도)
            int soulBefore = soulHarvest;
            soulHarvest = Math.Min(SoulMax, soulHarvest + SoulPerRound);
            AddSoulLines(log, soulBefore, soulHarvest);

            if (hunt.Active)
            {
                hunt.AllyTurnsLeft--;
                if (hunt.AllyTurnsLeft <= 0)
                {
                    int soulBeforeFail = soulHarvest;
                    failedMember = Fail(hunt, ref bloodTrail, ref soulHarvest, party, rng);
                    log.Add(failedMember != null
                        ? $"피의 사냥 실패 — 사신의 치명적 공격: {failedMember.Job.DisplayName} HP 1 · 영혼 수확 +{FailSoulPenalty}"
                        : $"피의 사냥 실패 — 사신이 사라졌다 · 영혼 수확 +{FailSoulPenalty}");
                    AddSoulLines(log, soulBeforeFail, soulHarvest);
                }
                else log.Add($"피의 사냥: 남은 라운드 {hunt.AllyTurnsLeft} · 남은 사신 {hunt.Reapers.Count}");
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
                    hunt.AllyTurnsLeft = RoundLimit; // 생성 라운드는 세지 않음 — 다음 완료 라운드부터
                    hunt.JustSpawned = true;
                    hunt.Reapers.AddRange(spawns);
                    log.Add($"피의 사냥! 인조 그림자 사신 {spawns.Count}마리 출현 — {RoundLimit}라운드 안에 전부 처치");
                }
            }
            return log;
        }

        private static void AddSoulLines(List<string> log, int before, int after)
        {
            foreach (int w in new[] { 25, 50, RedSkyThreshold })
                if (before < w && after >= w) log.Add($"영혼 수확 {w} 도달{(w == RedSkyThreshold ? " — 하늘이 붉게 물든다" : "")}");
        }

        // 실패: 생존 파티원 1명 균등 추첨 HP 1 (MP·LP 유지), S +5, G 0, 남은 사신 제거
        private static PartyMember Fail(BloodHuntState hunt, ref int bloodTrail, ref int soulHarvest, Party party, Random rng)
        {
            PartyMember victim = null;
            var alive = party?.AliveMembers.ToList() ?? new List<PartyMember>();
            if (alive.Count > 0)
            {
                victim = alive[rng.Next(alive.Count)];
                if (victim.CurrentHP > 1) victim.TakeDamage(victim.CurrentHP - 1);
            }
            soulHarvest = Math.Min(SoulMax, soulHarvest + FailSoulPenalty);
            End(hunt, ref bloodTrail);
            return victim;
        }

        // 사신 1마리 처치 (전투 보상 35 Cent·경험치 40은 전투가 줌): 그 사신 제거.
        // 마지막 1마리였으면 완수 — G 0, 사냥 끝, 100 Cent + 생명의 영약. 반환: 안내 문구
        public static string OnReaperKilled(BloodHuntState hunt, HexCoord at, ref int bloodTrail, Party party, ItemCatalog catalog)
        {
            hunt.ReaperKilled = false;
            int i = hunt.Reapers.IndexOf(at);
            if (i < 0 && hunt.Reapers.Count > 0) i = 0; // 위치가 어긋났으면 하나만 제거
            if (i >= 0) hunt.Reapers.RemoveAt(i);
            if (hunt.Reapers.Count > 0) return $"사신 처치 — 남은 사신 {hunt.Reapers.Count}마리 (남은 라운드 {hunt.AllyTurnsLeft})";
            return Succeed(hunt, ref bloodTrail, party, catalog);
        }

        // 전부 처치: G 0, 사냥 끝, 보상
        public static string Succeed(BloodHuntState hunt, ref int bloodTrail, Party party, ItemCatalog catalog)
        {
            End(hunt, ref bloodTrail);
            party?.AddCent(SuccessCent);
            var elixir = catalog != null ? catalog.Consumable(ConsumableIds.LifeElixir) : null;
            if (elixir != null && party != null) party.Inventory.AddConsumable(elixir);
            return $"피의 사냥 완수 — 사신을 모두 처치했다. 보상 {SuccessCent} Cent{(elixir != null ? " + 생명의 영약" : "")}";
        }

        private static void End(BloodHuntState hunt, ref int bloodTrail)
        {
            hunt.Clear();
            bloodTrail = 0;
            hunt.GraceRounds = GraceRoundsAfter;
        }

        // 파티가 있는 지역의 이동 가능한 빈 칸(특수 칸 제외)에서 무작위 3칸 — 파티 근처 1~2칸 우선, 모자라면 그다음 가까운 칸
        public static List<HexCoord> PickSpawnTiles(WorldMapData map, HexCoord partyPos, Random rng)
        {
            int region = map.RegionOf(partyPos);
            var candidates = map.Tiles.Values
                .Where(t => t.Kind == MapTileKind.Walkable && map.RegionOf(t.Coord) == region && !t.Coord.Equals(partyPos))
                .Select(t => t.Coord).ToList();
            var picked = new List<HexCoord>();
            foreach (var ring in candidates.GroupBy(c => Math.Max(SpawnMaxDistance, c.DistanceTo(partyPos))).OrderBy(g => g.Key))
            {
                var pool = ring.Where(c => c.DistanceTo(partyPos) >= SpawnMinDistance).ToList();
                while (picked.Count < ReaperCount && pool.Count > 0)
                {
                    int i = rng.Next(pool.Count);
                    picked.Add(pool[i]);
                    pool.RemoveAt(i);
                }
                if (picked.Count >= ReaperCount) break;
            }
            return picked;
        }

        // 아군 턴 시작: 사신마다 파티 쪽으로 1칸 (이동 가능 칸, 다른 사신과 겹치지 않음). 생성 직후 아군 턴은 그대로.
        // 파티 칸에 닿은 사신의 번호를 돌려준다 (-1 = 없음) — 그 사신과 바로 전투.
        public static int MoveReapers(BloodHuntState hunt, WorldMapData map, HexCoord partyPos, Func<HexCoord, bool> exitOpen = null)
        {
            if (!hunt.Active) return -1;
            if (hunt.JustSpawned) { hunt.JustSpawned = false; return -1; }
            var dist = DistancesTo(map, partyPos, exitOpen);
            for (int i = 0; i < hunt.Reapers.Count; i++)
            {
                for (int step = 0; step < ReaperStepsPerRound; step++)
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
