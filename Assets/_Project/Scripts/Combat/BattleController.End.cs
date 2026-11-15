using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.Items;
using Fantasia.UI;
using Fantasia.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Fantasia.Combat
{
    // 전투 종료·결과·약탈 (11/15 긴 파일 정리: BattleController.cs에서 옮김, 내용 그대로)
    public partial class BattleController
    {
        // ── 전투 종료 (D21, System.md "패배 시 약탈") ──
        // 승리: 적 전멸 / 퇴각: 쓰러지지 않은 아군이 모두 이탈 / 패배: 아군 전원 쓰러짐.
        private bool CheckBattleEnd()
        {
            if (_phase == Phase.Finished) return true;
            bool enemiesLeft = _enemies.Any(e => e.IsAlive);
            bool alliesLeft = _allies.Any(a => a.InBattle);
            if (enemiesLeft && alliesLeft) return false;

            _phase = Phase.Finished;
            BoardSession.Instance.BattleSnapshotJson = null; // 전투 끝 — 이어하기는 보드에서
            RefreshBossTelegraph(); // 전투 종료 → 예고 해제
            foreach (var ally in _allies) ally.Statuses.CarryOverAfterBattle(); // 11/15 팀장: 디버프는 다음 전투로 (버프만 지움). 예전 11/14: 전투 끝에 모두 풀림
            _hud.SetTimeline(_queue.Preview(BattleHud.TimelineMax), _queue.Round, _queue.OrderChangeOf);
            bool wiped = enemiesLeft && !_allies.Any(a => a.IsAlive);
            Log($"전투 종료 — {(!enemiesLeft ? "승리" : wiped ? "패배(전원 쓰러짐)" : "퇴각")} · 라운드 {_queue.Round} · 아군 HP {string.Join(", ", _allies.Select(u => $"{u.DisplayName} {u.CurrentHP}/{u.MaxHP} LP {u.Member.CurrentLP}"))}");
            PartyHudPanel.SetActive(null);
            HideMarker();
            var session = BoardSession.Instance;

            if (!enemiesLeft)
            {
                if (session.PendingEncounterCoord.HasValue) session.ClearedEncounters.Add(session.PendingEncounterCoord.Value);
                if (session.PendingReaperFight) session.Hunt.ReaperKilled = true; // D24 피의 사냥 사신 처치 → 보드에서 성공 처리
                // 경험치는 끝까지 서 있던 캐릭터만 → 그다음 쓰러진 아군 정리
                bool actBoss = _enemies.Any(en => ActOneStory.IsActOneBoss(en.Enemy)) && !session.StoryFlags.Contains(ActOneStory.FlagBossDefeated);
                if (actBoss) MusicPlayer.Stop(0.7f); // 처치 직후 보스 음악 0.7초에 걸쳐 종료
                string loot = GrantRewards(session);
                string downed = SettleDowned(DefeatRules.PostBattleReviveHpRatio);
                if (actBoss)
                {
                    // Boss_Abel_v1.0 처치 후 순서: 처치·최초 보상·증거품·퀘스트 갱신(중복 없이) → 처치 후 대화 → 보상 창 → 월드 복귀
                    session.StoryFlags.Add(ActOneStory.FlagRewardGiven);
                    ActOneStory.MarkBossDefeated(session);
                    EndPlayLog("승리 (보스)", LastRewards, 0);
                    SaveGame.Checkpoint("보스 처치·보상·증거품"); // 보상 확정 → 저장 → 대화·보상 창
                    StartCoroutine(AfterVictoryMotion(() => StartCoroutine(PlayDialogueThen(ActOneStory.AfterBattle, () =>
                    {
                        session.StoryFlags.Add(ActOneStory.FlagAfterDialogueDone); // 도중 종료면 이어하기 때 이 대화만 다시 (결정서 T16-35)
                        SaveGame.Checkpoint("처치 후 대화");
                        SfxPlayer.Play(SfxIds.Victory); _hud.ShowPanelResult(true, "아벨 모로 격파", RewardSummary(), $"검시관 아벨 모로를 쓰러뜨렸다.\n{loot}{downed}", ReturnToBoard);
                    }))));
                }
                else
                {
                    EndPlayLog("승리", LastRewards, 0); SaveGame.Checkpoint("전투 승리·보상");
                    StartCoroutine(AfterVictoryMotion(() => { SfxPlayer.Play(SfxIds.Victory); _hud.ShowPanelResult(true, "전투 승리", RewardSummary(), $"적을 모두 쓰러뜨렸다.\n{loot}{downed}", ReturnToBoard); }));
                }
            }
            else if (!wiped)
            {
                // 전원 이탈 → 직전 안전 타일. 단일·전원 이탈은 약탈 없음.
                string downed = SettleDowned(DefeatRules.PostBattleReviveHpRatio);
                session.PlayerPosition = session.LastSafeCoord;
                Log("  → 전원 이탈: 직전 안전 타일로 이동 (약탈 없음)");
                EndPlayLog("퇴각", null, 0);
                SaveGame.Checkpoint("전투 퇴각");
                _hud.ShowResult("퇴각", $"파티가 전투에서 이탈해 직전 안전 타일로 물러났다.\n조우는 그대로 남는다.{downed}", ReturnToBoard);
            }
            else
            {
                // 전원 쓰러짐: 약탈 1회(인원수만큼 반복하지 않음) → LP 남은 캐릭터는 LP 1로 안전 타일에서 HP 50% 복귀,
                // LP 없는 캐릭터는 영구 사망. 아무도 복귀하지 못하면 플레이 종료.
                // 부의 기도: 파티에 받은 캐릭터가 있으면 약탈 면역 (판정 난수는 그대로 소비 — 이어하기 재현 유지)
                int percent = DefeatRules.RollPlunderPercent();
                bool plunderImmune = PrayerRules.PartyHas(session.Party, PrayerKind.Wealth);
                if (plunderImmune) percent = 0;
                int lost = session.Party.LoseCentPercent(percent);
                PlaytestLog.Cent("약탈", -lost, session.Party.Cent, plunderImmune ? "부의 기도 면역" : $"{percent}%");
                Log(plunderImmune ? "  → 부의 기도 — 약탈 면역" : $"  → 약탈 판정: 소지 Cent의 {percent}% → {lost} Cent 손실 (남은 {session.Party.Cent})");
                string downed = SettleDowned(DefeatRules.WipeReturnHpRatio);

                if (session.Party.IsWipedOut)
                {
                    Log("  → 전원 영구 사망 — 플레이 종료");
                    EndPlayLog("전멸 (회차 종료)", null, lost);
                    SaveGame.EndRun(session); // Save.md 1: 회차 종료 기록을 먼저 저장한 뒤 게임 오버 표시
                    SfxPlayer.Play(SfxIds.Defeat); _hud.ShowPanelResult(false, ActOneStory.GameOverTitle, "모든 캐릭터가 영구 사망했다", $"{ActOneStory.GameOverBody}{downed}", NewGameAfterWipe, "새 게임", "메인 메뉴", RestartGame); // 결정서 06
                }
                else
                {
                    session.PlayerPosition = session.LastSafeCoord;
                    EndPlayLog("패배", null, lost);
                    SaveGame.Checkpoint("전투 패배");
                    SfxPlayer.Play(SfxIds.Defeat); _hud.ShowPanelResult(false, "전투 패배", lost > 0 ? $"약탈 {lost} Cent · 안전 타일에서 다시 일어남" : "안전 타일에서 다시 일어남", $"파티가 쓰러졌다. 약탈 {lost} Cent ({percent}%).{downed}\n직전 안전 타일에서 다시 일어났다.", ReturnToBoard);
                }
            }
            return true;
        }

        // 쓰러진 아군 정리: LP가 있으면 1을 써서 hpRatio로 복귀, 없으면 영구 사망. 결과 창에 붙일 문구를 돌려준다.
        private string SettleDowned(float hpRatio)
        {
            var lines = new List<string>();
            foreach (var ally in _allies.Where(a => a.Member.IsDowned))
            {
                if (ally.Member.TrySpendLP())
                {
                    ally.Member.Revive(hpRatio);
                    lines.Add($"{ally.DisplayName}: LP 1 소모 → HP {ally.CurrentHP} (남은 LP {ally.Member.CurrentLP})");
                }
                else
                {
                    ally.Member.MarkPermanentlyDead();
                    lines.Add($"{ally.DisplayName}: LP 없음 → 영구 사망");
                }
                Log("  → " + lines[^1]);
            }
            return lines.Count > 0 ? "\n" + string.Join("\n", lines) : "";
        }

        // 게임 오버 [새 게임]: 파티 구성 화면부터
        private void NewGameAfterWipe()
        {
            Fantasia.UI.MainMenuController.OpenPartySelectOnStart = true;
            RestartGame();
        }

        private void RestartGame()
        {
            BoardSession.Instance.ResetForNewGame();
            // 게임 오버 [메인 메뉴] (메인 메뉴 씬이 빌드에 없으면 보드로)
            Fantasia.Core.SceneFader.Load(Application.CanStreamedLevelBeLoaded(Fantasia.UI.MainMenuController.SceneName) ? Fantasia.UI.MainMenuController.SceneName : BoardSceneName);
        }

        // D19 승리 보상: Cent(첫 칸) + 추가 칸 아이템, 생존 캐릭터별 경험치. 인벤토리가 차서 못 받은 것은
        // BoardSession.PendingLoot에 남겨 인벤토리 창에서 버리기·교체 후 받게 한다 (자동 삭제 안 함).
        private string GrantRewards(BoardSession session)
        {
            var party = session.Party;
            var levels = party.Members.ToDictionary(m => m, m => m.Level);
            var rewards = LootRules.Roll(_setup.Enemies, party, ItemCatalog.Current, GameRandom.NewSystemRandom());
            // 지도 적 칸 확정 보상 — 몬스터별 합산 대신 전투 전체 값 (아이템 칸은 기존 표 그대로)
            if (_setup.FixedCent > 0) rewards.Cent = _setup.FixedCent;
            if (_setup.FixedExp > 0) rewards.Experience = _setup.FixedExp;
            // 부의 기도: 전투 Cent +20%, 여러 명이 받아도 파티당 1회 (버림)
            if (PrayerRules.PartyHas(party, PrayerKind.Wealth)) rewards.Cent += Mathf.FloorToInt(rewards.Cent * PrayerRules.WealthCentBonus);
            if (DevShowcaseLoot) ShowcaseLoot(rewards);
            LootRules.Apply(rewards, party);
            session.PendingLoot.AddRange(rewards.Overflow);
            LastRewards = rewards;
            PlaytestLog.Cent("전투 보상", rewards.Cent, party.Cent, string.Join(" + ", _setup.Enemies.Select(e => e.DisplayName)));

            var lines = new List<string> { $"보상 {rewards.SlotCount}칸 — {rewards.Cent} Cent (소지 {party.Cent})" };
            var shown = new List<string>(lines); // 결과 창 글 — 아이템은 아래 그림 띠로 (11/12)
            var cells = new List<(Sprite, string, Color)>();
            foreach (var item in rewards.Items)
            {
                bool lost = rewards.Overflow.Contains(item);
                lines.Add($"· {item.Name}{(lost ? " (인벤토리 가득 — 보관함에 대기)" : "")}");
                cells.Add((ItemLook.IconOf(item), ItemLook.NameWithGrade(item) + (lost ? "\n(보관함 대기)" : ""), ItemLook.NameColor(item)));
            }
            _hud.SetResultItems(cells);
            foreach (var key in _setup.Enemies.Where(en => en.KeyItemRewards != null).SelectMany(en => en.KeyItemRewards))
            {
                if (session.KeyItems.Contains(key)) continue;
                session.KeyItems.Add(key);
                lines.Add($"★ 퀘스트 물건: {key}");
                shown.Add($"★ 퀘스트 물건: {key}");
            }
            var survivors = party.AliveMembers.ToList();
            lines.Add($"경험치 +{rewards.Experience} ({string.Join(", ", survivors.Select(m => m.Job.DisplayName))})");
            shown.Add(lines[^1]);
            foreach (var m in survivors.Where(m => m.Level > levels[m]))
            {
                lines.Add($"{m.Job.DisplayName} 레벨 업! Lv {m.Level}");
                shown.Add(lines[^1]);
            }
            Log($"  → 보상: {string.Join(" / ", lines)}");
            if (rewards.Items.Count > 0) ItemAcquiredToast.Show($"전리품 {rewards.Items.Count}개 획득"); // 11/12 팀장: 하나씩 알림 X → 결과 창에서 그림·등급 색으로 한꺼번에
            return string.Join("\n", shown);
        }

        public BattleRewards LastRewards { get; private set; }

        // 점검용 (자동 플레이): 다음 승리 보상 아이템을 전설 무기 · 에픽 방어구 · 소모품으로 (결과 창 그림 띠·등급 색 사진)
        public static bool DevShowcaseLoot;
        private static void ShowcaseLoot(BattleRewards rewards)
        {
            DevShowcaseLoot = false;
            var cat = ItemCatalog.Current;
            if (cat == null) return;
            var w = cat.Weapons.FirstOrDefault(x => x != null && x.Icon == null);
            var a = cat.Armors.FirstOrDefault(x => x != null);
            var c = cat.Consumables.FirstOrDefault(x => x != null && x.Icon != null);
            rewards.Items.Clear();
            if (w != null) rewards.Items.Add(new LootDrop(new GearItem(w, EquipmentGrade.Legendary)));
            if (a != null) rewards.Items.Add(new LootDrop(new GearItem(a, EquipmentGrade.Epic)));
            if (c != null) rewards.Items.Add(new LootDrop(c));
        }

        // 결과 판 한 줄 요약: "180 Cent · 경험치 100 · 전리품 획득" (보고서 11/3 B6)
        private string RewardSummary()
        {
            var r = LastRewards;
            if (r == null) return "";
            var parts = new List<string> { $"{r.Cent} Cent", $"경험치 {r.Experience}" };
            if (r.Items.Count > 0) parts.Add("전리품 획득");
            return string.Join(" · ", parts);
        }
    }
}
