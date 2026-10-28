using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fantasia.Board;
using Fantasia.Data;
using Fantasia.Items;
using Fantasia.World;
using UnityEngine;

namespace Fantasia.Core
{
    // D27 저장·불러오기 (Docs/Planning/Save.md 기획 확정) — 진행 슬롯 1개, 자동 저장만, JSON (persistentDataPath/save_slot1.json).
    // 원칙: 행동 입력 → 결과 계산 → 상태·게임 난수 저장 → 결과 표시. 저장 지점은 Checkpoint(이유)로 부른다.
    // 회차: RunId + 상태(진행·종료·1막 클리어). 전멸하면 "종료"를 먼저 저장하고 종료 회차 목록에도 기록 → 이어하기·복구 불가.
    // 저장 대상: 파티(직업·레벨·자원·장비·퀵 슬롯·상태), 인벤토리, Cent, 월드맵 위치·클리어·사용 칸, 월드 시계,
    // 확정된 주사위 이동력, 피의 흔적·영혼 수확·피의 사냥, 상점 재고·한정·재입고 경계, 증거품, 못 받은 전리품,
    // 스토리 플래그, 진행·완료 퀘스트, 누적 플레이 시간, 게임 난수 상태.
    public static class SaveGame
    {
        public const int Version = 2;
        public static string FileName = "save_slot1.json"; // 셀프 테스트·자동 플레이가 다른 파일로 바꿔 쓴다
        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        private static string EndedRunsPath => Path.Combine(Application.persistentDataPath, Path.GetFileNameWithoutExtension(FileName) + "_ended_runs.txt");
        public static bool HasSave => File.Exists(SavePath);
        public static string LastError { get; private set; }
        public static event Action<string> Saved; // 자동 플레이 점검용 (저장 지점 이름)

        // 저장 지점 (Save.md 3): 진행 중 회차만.
        // 실패하면 경고 → 진행 일시정지 → 재시도 → 저장 성공 후 재개 (세이브 추가 수정안 — SaveErrorPanel)
        // 성능 점검용: 마지막 자동 저장에 걸린 시간·이번 실행의 저장 횟수
        public static double LastSaveMs { get; private set; }
        public static int SaveCount { get; private set; }

        public static bool Checkpoint(string reason)
        {
            var s = BoardSession.Instance;
            if (s == null || s.Party == null || s.Run == BoardSession.RunState.Ended) return false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool ok = Save(s);
            LastSaveMs = watch.Elapsed.TotalMilliseconds; // 성능 점검용 (PerfMonitor)
            SaveCount++;
            if (ok) { Debug.Log($"[SaveGame] 저장 — {reason}"); Saved?.Invoke(reason); }
            else if (Application.isPlaying) Fantasia.UI.SaveErrorPanel.Show(reason, LastError, () => Save(s));
            return ok;
        }

        // 전멸 확정: 종료 기록을 먼저 저장 (게임 오버 화면보다 앞) + 종료 회차 목록 + 최근 종료 회차 기록 1건
        public static void EndRun(BoardSession s)
        {
            s.Run = BoardSession.RunState.Ended;
            try { File.AppendAllText(EndedRunsPath, s.RunId + "\n"); } catch (Exception e) { Debug.LogError("[SaveGame] 종료 기록 실패: " + e.Message); }
            var (row, col) = WorldMapData.AxialToOffset(s.PlayerPosition);
            WriteLastEnded(new EndedRecord
            {
                runId = s.RunId, act = "1막", region = string.IsNullOrEmpty(s.RegionName) ? "런던" : s.RegionName,
                tile = WorldMapData.Label(row, col), worldRound = s.World.WorldRound, endedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            });
            if (!Save(s) && Application.isPlaying) Fantasia.UI.SaveErrorPanel.Show("회차 종료", LastError, () => Save(s));
            Debug.Log($"[SaveGame] 회차 종료 저장 — {s.RunId}");
        }

        // ── 최근 종료 회차 기록 (세이브 추가 수정안 3): 진행 데이터와 별도, 1건만, 불러오기 불가 — 메인 메뉴 표시용 ──
        [Serializable] public class EndedRecord { public string runId, act, region, tile, endedAt; public int worldRound; }
        private static string LastEndedPath => Path.Combine(Application.persistentDataPath, Path.GetFileNameWithoutExtension(FileName) + "_last_ended.json");

        private static void WriteLastEnded(EndedRecord r)
        {
            try { File.WriteAllText(LastEndedPath, JsonUtility.ToJson(r)); }
            catch (Exception e) { Debug.LogError("[SaveGame] 종료 회차 기록 실패: " + e.Message); }
        }

        public static EndedRecord ReadLastEnded()
        {
            try { return File.Exists(LastEndedPath) ? JsonUtility.FromJson<EndedRecord>(File.ReadAllText(LastEndedPath)) : null; }
            catch { return null; }
        }

        public static void DeleteLastEndedForTest() { if (File.Exists(LastEndedPath)) File.Delete(LastEndedPath); }

        public static bool IsEndedRun(string runId)
        {
            try { return !string.IsNullOrEmpty(runId) && File.Exists(EndedRunsPath) && File.ReadAllLines(EndedRunsPath).Contains(runId); }
            catch { return false; }
        }

        // 이어하기 가능한 저장인지 (종료 회차·종료 목록에 있는 회차는 불가)
        public static bool IsContinuable(Data d) => d != null && d.runState != (int)BoardSession.RunState.Ended && !IsEndedRun(d.runId);

        // ───────── 저장 파일 형식 (JsonUtility — public 필드만, Dictionary 불가) ─────────
        [Serializable] public class Coord { public int q, r; }
        [Serializable] public class Gear { public bool weapon; public string id; public int grade; public bool starting; }
        [Serializable] public class StackData { public string id; public int count; }
        [Serializable] public class StatusData { public string id; public int turns; public int stacks; }

        [Serializable]
        public class MemberData
        {
            public int job;
            public int level, experience, hp, mp, focus, lp;
            public bool dead;
            public Gear[] equipped = new Gear[5]; // GearSlot 순서, 빈 칸은 id ""
            public string[] quickSlots = new string[PartyMember.QuickSlotCount];
            public List<StatusData> statuses = new();
            public int prayer;              // 십자가 석상 활성 기도 (PrayerKind)
        }

        [Serializable] public class StockData { public string city, item; public int count; }
        [Serializable] public class LootData { public Gear gear; public string consumable; }
        [Serializable] public class QuestData { public string title, objective; public int kind; }
        [Serializable] public class WitchData { public Coord tile; public int round; }

        [Serializable]
        public class Data
        {
            public int version = Version;
            public string savedAt;
            public string runId;
            public string playerName;     // 플레이어 이름 (UI 솔루션 Q4) — 예전 저장에는 없음 → 기본 "탐정"
            public int runState;          // BoardSession.RunState
            public float playSeconds;
            public uint[] rng;            // GameRandom 상태
            public int pendingMove = -1, pendingBonusMove = -1; // 확정된 주사위 이동력 (칸 선택 전)
            public int pendingEncounter = -1;                    // 확정된 조우 판정 결과
            public bool inBattle;         // 전투 진행 중 저장 (이어하기 표시용 — 전투 상태는 battle)
            public string battleJson;     // 전투 스냅숏 (BattleSnapshot, JSON)
            public int boardSeed;
            public Coord player, lastSafe;
            public bool hasPlayerPosition;
            public List<Coord> cleared = new(), usedTiles = new();
            public List<WitchData> witchRounds = new();
            public int worldRound, phase;
            public bool rolled, eventBonusUsed;
            public int bloodTrail, soulHarvest;
            public bool huntActive, reaperKilled;
            public int huntTurnsLeft, huntGrace;
            public List<Coord> reapers = new();
            public int ambushBonus;
            public string regionName;
            public List<string> keyItems = new();
            public int cent;
            public List<MemberData> members = new();
            public List<StackData> consumables = new();
            public List<Gear> gear = new();
            public List<LootData> pendingLoot = new();
            public List<StockData> shopStock = new();
            public List<StockData> shopLimited = new();
            public int shopLastRestock;
            public List<string> storyFlags = new();
            public List<QuestData> quests = new(), completedQuests = new();
        }

        // ───────── 저장 ─────────
        public static Data Capture(BoardSession s)
        {
            var d = new Data
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                runId = s.RunId,
                playerName = s.PlayerName,
                runState = (int)s.Run,
                playSeconds = s.PlaySeconds,
                rng = GameRandom.State,
                pendingMove = s.PendingMovePoints,
                pendingBonusMove = s.PendingBonusMove,
                pendingEncounter = s.PendingEncounterOutcome,
                inBattle = !string.IsNullOrEmpty(s.BattleSnapshotJson),
                battleJson = s.BattleSnapshotJson ?? "",
                boardSeed = s.BoardSeed,
                player = C(s.PlayerPosition),
                lastSafe = C(s.LastSafeCoord),
                hasPlayerPosition = s.HasPlayerPosition,
                cleared = s.ClearedEncounters.Select(C).ToList(),
                usedTiles = s.UsedMapTiles.Select(C).ToList(),
                witchRounds = s.WitchRounds.Select(kv => new WitchData { tile = C(kv.Key), round = kv.Value }).ToList(),
                worldRound = s.World.WorldRound,
                phase = (int)s.World.Phase,
                rolled = s.World.HasRolledThisTurn,
                eventBonusUsed = s.World.EventBonusUsedThisTurn,
                bloodTrail = s.BloodTrail,
                soulHarvest = s.SoulHarvest,
                huntActive = s.Hunt.Active,
                reaperKilled = s.Hunt.ReaperKilled,
                huntTurnsLeft = s.Hunt.AllyTurnsLeft,
                huntGrace = s.Hunt.GraceRounds,
                reapers = s.Hunt.Reapers.Select(C).ToList(),
                ambushBonus = s.PendingAmbushBonus,
                regionName = s.RegionName,
                keyItems = s.KeyItems.ToList(),
                pendingLoot = s.PendingLoot.Select(l => new LootData { gear = G(l.Gear), consumable = l.Consumable != null ? l.Consumable.Id : "" }).ToList(),
                shopStock = s.Shop.Stock.Select(kv => new StockData { city = kv.Key.city, item = kv.Key.item, count = kv.Value }).ToList(),
                shopLimited = s.Shop.LimitedBought.Select(k => new StockData { city = k.city, item = k.item }).ToList(),
                shopLastRestock = s.Shop.LastRestockBoundary,
                storyFlags = s.StoryFlags.ToList(),
                quests = s.Quests.Select(Q).ToList(),
                completedQuests = s.CompletedQuests.Select(Q).ToList(),
            };
            var party = s.Party;
            if (party != null)
            {
                d.cent = party.Cent;
                foreach (var m in party.Members)
                {
                    var md = new MemberData
                    {
                        job = (int)m.Job.Id,
                        level = m.Level, experience = m.Experience,
                        hp = m.CurrentHP, mp = m.CurrentMP, focus = m.CurrentFocus, lp = m.CurrentLP,
                        dead = m.IsPermanentlyDead,
                    };
                    for (int i = 0; i < md.equipped.Length; i++) md.equipped[i] = G(m.GetEquipped((GearSlot)i));
                    for (int i = 0; i < md.quickSlots.Length; i++) md.quickSlots[i] = m.QuickSlots[i] != null ? m.QuickSlots[i].Id : "";
                    md.statuses = m.Statuses.All.Select(a => new StatusData { id = a.Definition.Id, turns = a.RemainingTurns, stacks = a.Stacks }).ToList();
                    md.prayer = (int)m.Prayer;
                    d.members.Add(md);
                }
                d.consumables = party.Inventory.Consumables.Select(st => new StackData { id = st.Definition.Id, count = st.Count }).ToList();
                d.gear = party.Inventory.Gear.Select(G).ToList();
            }
            return d;
        }

        public static bool Save(BoardSession s)
        {
            LastError = null;
            try
            {
                string json = JsonUtility.ToJson(Capture(s), true);
                // 쓰다가 꺼져도 이전 저장이 남게: 임시 파일에 쓰고 바꿔치기
                string tmp = SavePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(tmp, SavePath);
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError("[SaveGame] 저장 실패: " + e.Message);
                return false;
            }
        }

        // ───────── 불러오기 ─────────
        public static Data ReadFile()
        {
            try
            {
                if (!HasSave) return null;
                var d = JsonUtility.FromJson<Data>(File.ReadAllText(SavePath));
                if (d == null || d.version != Version || d.members == null || d.members.Count == 0) return null;
                return d;
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveGame] 저장 파일을 읽지 못함: " + e.Message);
                return null;
            }
        }

        // 세션에 덮어씀. 반환: 실패 이유 (null = 성공). 실패하면 세션은 건드리지 않는다.
        public static string Apply(Data d, BoardSession s, ItemCatalog catalog)
        {
            if (d == null) return "저장 파일 없음";
            if (catalog == null) return "아이템 카탈로그 없음";
            if (!IsContinuable(d)) return "종료된 회차";

            // 파티부터 만들어 보고, 하나라도 못 찾으면 중단 (세션은 그대로)
            var party = new Party();
            foreach (var md in d.members)
            {
                var job = catalog.Job((JobId)md.job);
                if (job == null) return $"직업 {md.job} 없음";
                var m = new PartyMember(job);
                for (int i = 0; i < md.equipped.Length && i < 5; i++) m.RestoreEquipped((GearSlot)i, ToGear(md.equipped[i], catalog));
                for (int i = 0; i < md.quickSlots.Length && i < PartyMember.QuickSlotCount; i++)
                    m.QuickSlots[i] = string.IsNullOrEmpty(md.quickSlots[i]) ? null : catalog.Consumable(md.quickSlots[i]);
                foreach (var st in md.statuses) m.Statuses.Restore(catalog.Status(st.id), st.turns, st.stacks);
                m.SetPrayer((PrayerKind)md.prayer); // 최대 HP·집중력에 영향 — 현재 값 복원보다 먼저
                m.RestoreState(md.level, md.experience, md.hp, md.mp, md.focus, md.lp, md.dead);
                party.AddMember(m);
            }
            party.RestoreCent(d.cent);
            foreach (var st in d.consumables)
            {
                var c = catalog.Consumable(st.id);
                if (c != null) party.Inventory.AddConsumable(c, st.count);
            }
            foreach (var g in d.gear)
            {
                var item = ToGear(g, catalog);
                if (item != null) party.Inventory.AddGear(item);
            }

            s.ResetForNewGame();
            s.Party = party;
            s.RunId = d.runId;
            s.PlayerName = string.IsNullOrWhiteSpace(d.playerName) ? BoardSession.DefaultPlayerName : d.playerName;
            s.Run = (BoardSession.RunState)d.runState;
            s.PlaySeconds = d.playSeconds;
            GameRandom.State = d.rng;
            s.PendingMovePoints = d.pendingMove;
            s.PendingBonusMove = d.pendingBonusMove;
            s.PendingEncounterOutcome = d.pendingEncounter;
            s.BattleSnapshotJson = string.IsNullOrEmpty(d.battleJson) ? null : d.battleJson;
            s.RestoreBoardSeed(d.boardSeed);
            s.PlayerPosition = H(d.player);
            s.LastSafeCoord = H(d.lastSafe);
            s.HasPlayerPosition = d.hasPlayerPosition;
            foreach (var c in d.cleared) s.ClearedEncounters.Add(H(c));
            foreach (var c in d.usedTiles) s.UsedMapTiles.Add(H(c));
            foreach (var w in d.witchRounds) s.WitchRounds[H(w.tile)] = w.round;
            s.World.Restore(Math.Max(1, d.worldRound), (WorldPhase)d.phase, d.rolled, d.eventBonusUsed);
            s.BloodTrail = d.bloodTrail;
            s.SoulHarvest = d.soulHarvest;
            s.Hunt.Active = d.huntActive;
            s.Hunt.ReaperKilled = d.reaperKilled;
            s.Hunt.AllyTurnsLeft = d.huntTurnsLeft;
            s.Hunt.GraceRounds = d.huntGrace;
            foreach (var c in d.reapers) s.Hunt.Reapers.Add(H(c));
            s.PendingAmbushBonus = d.ambushBonus;
            if (!string.IsNullOrEmpty(d.regionName)) s.RegionName = d.regionName;
            s.KeyItems.AddRange(d.keyItems);
            foreach (var l in d.pendingLoot)
            {
                var gear = ToGear(l.gear, catalog);
                if (gear != null) s.PendingLoot.Add(new Fantasia.Combat.LootDrop(gear));
                else if (!string.IsNullOrEmpty(l.consumable) && catalog.Consumable(l.consumable) is { } c) s.PendingLoot.Add(new Fantasia.Combat.LootDrop(c));
            }
            foreach (var st in d.shopStock) s.Shop.Stock[(st.city, st.item)] = st.count;
            foreach (var st in d.shopLimited) s.Shop.LimitedBought.Add((st.city, st.item));
            s.Shop.LastRestockBoundary = d.shopLastRestock;
            foreach (var f in d.storyFlags) s.StoryFlags.Add(f);
            if (d.quests.Count > 0 || d.completedQuests.Count > 0) s.RestoreQuests(d.quests.Select(BQ), d.completedQuests.Select(BQ));
            return null;
        }

        private static QuestData Q(BoardSession.QuestEntry q) => new() { title = q.Title, objective = q.Objective, kind = (int)q.Kind };
        private static BoardSession.QuestEntry BQ(QuestData q) => new(q.title, q.objective, (QuestType)q.kind);

        private static Coord C(HexCoord h) => new() { q = h.Q, r = h.R };
        private static HexCoord H(Coord c) => c != null ? new HexCoord(c.q, c.r) : default;

        private static Gear G(GearItem g) => g == null ? new Gear { id = "" }
            : new Gear { weapon = g.IsWeapon, id = g.IsWeapon ? g.Weapon.Id : g.Armor.Id, grade = (int)g.Grade, starting = g.IsStartingGear };

        private static GearItem ToGear(Gear g, ItemCatalog catalog)
        {
            if (g == null || string.IsNullOrEmpty(g.id)) return null;
            var grade = (EquipmentGrade)g.grade;
            if (g.weapon)
            {
                var w = catalog.Weapon(g.id);
                return w != null ? new GearItem(w, grade) { IsStartingGear = g.starting } : null;
            }
            var a = catalog.Armor(g.id);
            return a != null ? new GearItem(a, grade) : null;
        }
    }
}
