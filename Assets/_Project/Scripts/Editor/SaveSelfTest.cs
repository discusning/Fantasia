using System.IO;
using System.Linq;
using Fantasia.Board;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.Items;
using Fantasia.World;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D27 — 진행 중인 게임 상태를 파일로 저장 → 세션 초기화 → 불러오기 후 값이 그대로인지.
    public static class SaveSelfTest
    {
        [MenuItem("Fantasia/Data/Run Save Self-Test")]
        public static void Run()
        {
            var catalog = ItemCatalog.Current;
            var detective = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_명탐정.asset");
            var doctor = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_역병의사.asset");
            var sniper = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_저격수.asset");
            if (catalog == null || detective == null || catalog.Jobs.Length == 0) { Debug.LogError("[SaveSelfTest] 카탈로그·직업 없음 (GameDataSeed 먼저)"); return; }
            BoardSession.EnsureExists();
            var s = BoardSession.Instance;
            string oldName = SaveGame.FileName;
            SaveGame.FileName = "save_selftest.json";
            int e = 0;
            try
            {
                // ── 진행 중인 게임 만들기 ──
                s.ResetForNewGame();
                var party = new Party();
                foreach (var j in new[] { detective, doctor, sniper }) party.AddMember(new PartyMember(j));
                party.GiveStartingItems(catalog);
                s.Party = party;
                party.AddCent(345);
                var a = party.Members[0];
                a.GrantExperience(130);
                a.TakeDamage(25);
                a.TrySpendMP(1);
                var saber = new GearItem(catalog.Weapon("weapon.sword.nightwatch_saber"), EquipmentGrade.Rare);
                party.Inventory.AddGear(saber);
                a.TryEquip(saber, party.Inventory, out _); // 시작 단검은 인벤토리로
                a.Statuses.Apply(catalog.Status("status.poison"));
                party.Members[2].MarkPermanentlyDead();
                var elixir = catalog.Consumable(ConsumableIds.LifeElixir);
                party.Inventory.AddConsumable(elixir, 3);
                a.QuickSlots[2] = elixir;

                s.PlayerPosition = new HexCoord(7, 12);
                s.LastSafeCoord = new HexCoord(6, 12);
                s.HasPlayerPosition = true;
                s.ClearedEncounters.Add(new HexCoord(3, 4));
                s.UsedMapTiles.Add(new HexCoord(5, 5));
                s.World.Restore(7, WorldPhase.AllyTurn, false, false);
                s.BloodTrail = 60;
                s.SoulHarvest = 14;
                s.Hunt.Active = true;
                s.Hunt.AllyTurnsLeft = 2;
                s.Hunt.Reapers.Add(new HexCoord(9, 9));
                s.KeyItems.Add("검시 기록");
                s.StoryFlags.Add(ActOneStory.FlagBossDefeated);
                ActOneStory.MarkBossDefeated(s); // 이미 플래그가 있으면 중복 처리 없음
                s.CompleteQuest(ActOneStory.QuestCase);
                s.SetQuest(ActOneStory.QuestReport, ActOneStory.QuestReportObjective, QuestType.Main);
                s.PendingLoot.Add(new Fantasia.Combat.LootDrop(new GearItem(catalog.Weapon("weapon.dagger.silver_scalpel"))));
                var london = catalog.ShopAtHub("S05");
                var shopElixir = london.Entries.First(x => x.DisplayName == "생명의 영약");
                ShopRules.Buy(s.Shop, london, shopElixir, party, false, false);
                var laurel = london.Entries.First(x => x.Limited);
                party.AddCent(200);
                ShopRules.Buy(s.Shop, london, laurel, party, false, false);
                s.Shop.LastRestockBoundary = 6;
                s.PendingMovePoints = 2; // 주사위 2 확정, 칸 선택 전 (Save.md 예시)
                s.PlaySeconds = 754f;
                GameRandom.Seed(4242);
                var rngAtSave = GameRandom.State;
                int rollAfterSave = GameRandom.Range(1, 7); // 저장 뒤 첫 판정
                GameRandom.State = rngAtSave;
                var battleEntry = new Fantasia.Combat.BattleSnapshot { started = false, enemyIds = new System.Collections.Generic.List<string> { "enemy.boss.abel_moreau" }, opening = 2 };
                s.BattleSnapshotJson = JsonUtility.ToJson(battleEntry);
                string runId = s.RunId;

                // 기대값 기록
                int seed = s.BoardSeed, cent = party.Cent, hp = a.CurrentHP, mp = a.CurrentMP, lv = a.Level, exp = a.Experience, maxHp = a.MaxHP;
                int elixirs = party.Inventory.CountOf(elixir), gearCount = party.Inventory.Gear.Count, consumableKinds = party.Inventory.Consumables.Count;

                e += Check(SaveGame.Save(s) && File.Exists(SaveGame.SavePath), "파일 저장");

                // ── 처음부터 → 불러오기 ──
                s.ResetForNewGame();
                e += Check(s.Party == null && s.BloodTrail == 0, "초기화");
                string error = SaveGame.Apply(SaveGame.ReadFile(), s, catalog);
                e += Check(error == null, "불러오기 " + error);
                if (error == null)
                {
                    var p = s.Party;
                    var b = p.Members[0];
                    e += Expect("보드 시드", s.BoardSeed, seed);
                    e += Expect("Cent", p.Cent, cent);
                    e += Expect("파티 인원", p.Members.Count, 3);
                    e += Check(p.Members.Select(m => m.Job).SequenceEqual(new[] { detective, doctor, sniper }), "직업·순서");
                    e += Expect("레벨", b.Level, lv);
                    e += Expect("경험치", b.Experience, exp);
                    e += Expect("HP", b.CurrentHP, hp);
                    e += Expect("최대 HP (장비 포함)", b.MaxHP, maxHp);
                    e += Expect("MP", b.CurrentMP, mp);
                    e += Check(b.EquippedWeaponItem != null && b.EquippedWeaponItem.Weapon.Id == "weapon.sword.nightwatch_saber" && b.EquippedWeaponItem.Grade == EquipmentGrade.Rare, "장착 무기·등급");
                    e += Check(p.Inventory.Gear.Any(g => g.IsStartingGear && g.SellPrice == 0), "시작 단검 되팔기 0 유지");
                    e += Expect("예비 장비 수", p.Inventory.Gear.Count, gearCount);
                    e += Expect("소모품 종류", p.Inventory.Consumables.Count, consumableKinds);
                    e += Expect("생명의 영약 수", p.Inventory.CountOf(elixir), elixirs);
                    e += Check(b.QuickSlots[2] == elixir, "퀵 슬롯");
                    e += Check(b.Statuses.Has(StatusEffectType.Poison), "중독 유지");
                    e += Check(p.Members[2].IsPermanentlyDead, "영구 사망 유지");
                    e += Check(s.PlayerPosition.Equals(new HexCoord(7, 12)) && s.LastSafeCoord.Equals(new HexCoord(6, 12)) && s.HasPlayerPosition, "위치");
                    e += Check(s.ClearedEncounters.Contains(new HexCoord(3, 4)) && s.UsedMapTiles.Contains(new HexCoord(5, 5)), "클리어·사용 칸");
                    e += Expect("월드 라운드", s.World.WorldRound, 7);
                    e += Check(s.BloodTrail == 60 && s.SoulHarvest == 14, "피의 흔적·영혼 수확");
                    e += Check(s.Hunt.Active && s.Hunt.AllyTurnsLeft == 2 && s.Hunt.Reapers.SequenceEqual(new[] { new HexCoord(9, 9) }), "피의 사냥");
                    e += Check(s.KeyItems.SequenceEqual(new[] { "검시 기록" }), "퀘스트 물건");
                    e += Check(s.StoryFlags.Contains(ActOneStory.FlagBossDefeated) && ActOneStory.NeedsReport(s), "스토리 플래그 (보스 처치·보고 전)");
                    e += Check(s.Quests.Any(q => q.Title == ActOneStory.QuestReport) && s.CompletedQuests.Any(q => q.Title == ActOneStory.QuestCase), "진행·완료 퀘스트");
                    e += Check(s.PendingLoot.Count == 1 && s.PendingLoot[0].Gear != null, "못 받은 전리품");
                    // City.md: 저장 후 불러오기로 재입고하지 않음 · 한정품 기록 유지
                    e += Expect("상점 영약 재고", ShopRules.StockOf(s.Shop, london, shopElixir), 2);
                    e += Expect("월계관 재고 (한정 구매 기록)", ShopRules.StockOf(s.Shop, london, laurel), 0);
                    e += Expect("재입고 경계", s.Shop.LastRestockBoundary, 6);
                    e += Expect("확정된 주사위 2 유지", s.PendingMovePoints, 2);
                    e += Check(Mathf.Approximately(s.PlaySeconds, 754f), "누적 플레이 시간");
                    e += Check(s.RunId == runId && s.Run == BoardSession.RunState.Active, "회차 ID·진행 상태");
                    e += Expect("저장된 난수 → 같은 판정 결과", GameRandom.Range(1, 7), rollAfterSave);
                    var entry = JsonUtility.FromJson<Fantasia.Combat.BattleSnapshot>(s.BattleSnapshotJson ?? "{}");
                    e += Check(entry != null && !entry.started && entry.enemyIds.Count == 1 && entry.opening == 2, "전투 진입 정보 유지");
                }

                // 전멸: 종료 기록을 먼저 저장 → 이어하기 불가, 예전 저장 사본으로도 되살리지 못함 (Save.md 1)
                SaveGame.Save(s);
                string backup = SaveGame.SavePath + ".bak_test";
                File.Copy(SaveGame.SavePath, backup, true);
                SaveGame.EndRun(s);
                e += Check(!SaveGame.IsContinuable(SaveGame.ReadFile()) && SaveGame.Apply(SaveGame.ReadFile(), s, catalog) != null, "종료 회차 → 이어하기·불러오기 거부");
                File.Copy(backup, SaveGame.SavePath, true);
                e += Check(SaveGame.ReadFile() != null && !SaveGame.IsContinuable(SaveGame.ReadFile()), "전멸 전 사본을 되돌려도 같은 회차는 거부");
                File.Delete(backup);
                string endedList = System.IO.Path.Combine(Application.persistentDataPath, System.IO.Path.GetFileNameWithoutExtension(SaveGame.FileName) + "_ended_runs.txt");
                if (File.Exists(endedList)) File.Delete(endedList);
                s.ResetForNewGame();
                e += Check(s.Run == BoardSession.RunState.Active && s.RunId != runId, "새 게임 = 새 회차");

                // 깨진 파일 → 실패하고 세션은 그대로
                File.WriteAllText(SaveGame.SavePath, "{ 깨진 파일");
                var before = s.Party;
                e += Check(SaveGame.Apply(SaveGame.ReadFile(), s, catalog) != null && s.Party == before, "깨진 파일 → 불러오기 거부, 진행 유지");
            }
            finally
            {
                if (File.Exists(SaveGame.SavePath)) File.Delete(SaveGame.SavePath);
                SaveGame.FileName = oldName;
                s.ResetForNewGame();
            }

            if (e == 0) Debug.Log("[SaveSelfTest] 전부 통과.");
            else Debug.LogError($"[SaveSelfTest] {e}건 실패.");
        }

        private static int Expect(string label, int actual, int expected)
        {
            if (actual == expected) return 0;
            Debug.LogError($"[SaveSelfTest] {label}: 기대 {expected}, 실제 {actual}");
            return 1;
        }

        private static int Check(bool ok, string label)
        {
            if (ok) return 0;
            Debug.LogError($"[SaveSelfTest] 실패: {label}");
            return 1;
        }
    }
}
