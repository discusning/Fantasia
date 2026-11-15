using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.UI;
using Fantasia.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Board
{
    // 도시 상점·전투 시작 (11/15 긴 파일 정리: BoardTestController.cs에서 옮김, 내용 그대로)
    public partial class BoardTestController
    {
        // ── D20 도시 상점: 거점 칸에서 열림 (도착하면 자동, 아군 턴 중 B) ──
        private void OpenShopHere()
        {
            if (!_board.UsesWorldMap || !_board.TryGetTile(_currentCoord, out var tile) || tile.MapKind != MapTileKind.Hub) return;
            var (row, col) = WorldMapData.AxialToOffset(_currentCoord);
            var shop = ItemCatalog.Current != null ? ItemCatalog.Current.ShopAtHub(WorldMapData.Label(row, col)) : null;
            if (shop == null)
            {
                _encounterResultMessage = $"{tile.MapLabel} — 안전 지대";
                return;
            }
            Fantasia.UI.ShopPanel.Open(shop, _testParty, ShopStages, () => Clock.WorldRound - 1);
        }

        // 판매 단계: 2막 = 2막 진입(런던 보스 M11 처치 + 2막 구현), 고성 = 베르사유 보스(I25) 처치
        private (bool act2, bool castle) ShopStages()
        {
            var cleared = BoardSession.Instance.ClearedEncounters;
            bool Cleared(string label) => _board.UsesWorldMap && cleared.Contains(_board.Map.TileAt(label).Coord);
            // 결정서 08: 2막 고급 상품은 실제 2막 진입 때 — 1막 클리어 직전(아벨 처치)에는 열지 않음
            return (HexBoard.Act2Available && Cleared("M11"), Cleared("I25"));
        }

        private bool OnHubTile => _board != null && _board.UsesWorldMap && _board.TryGetTile(_currentCoord, out var t) && t.MapKind == MapTileKind.Hub;

        // 조우한 적 + 같은 구역 소형 적 1마리 (프로토타입 편성 — 실제 조우 조합은 D13·P06 기획).
        // 기습 성공/실패는 BattleQueue가 첫 라운드 순서에 반영한다. 전투가 이 아군 턴의 마지막 행동.
        private void EnterCombat(EncounterOutcome opening)
        {
            if (DevForceOpening >= 0) opening = (EncounterOutcome)DevForceOpening; // 자동 플레이 점검용
            var setup = new Fantasia.Combat.BattleSetup { Opening = opening };
            var session = BoardSession.Instance;
            session.PendingReaperFight = _pendingIsReaper;
            if (_pendingIsReaper)
            {
                // 피의 사냥 사신 1마리와 전투 (☆). 이기면 BattleController가 Hunt.ReaperKilled 표시
                setup.Enemies.Add(ReaperEnemy);
                session.PendingBattle = setup;
                session.PendingEncounterCoord = null;
                session.PendingBattleTheme = -1;
                session.LastSafeCoord = _lastSafeCoord;
                session.AllyTurnEndedByBattle = true;
                SaveBattleEntry(setup);
                Fantasia.Core.SceneFader.Load(CombatSceneName);
                return;
            }
            var pool = _board.EncounterEnemyPool;
            var forced = System.Array.Find(pool.Concat(_board.RegionBosses).Append(ReaperEnemy).ToArray(), e => e != null && e.Id == DevForcedEnemyId);
            var first = forced != null ? forced : _pendingEnemy;
            // 지도 적 칸 확정 조합·보상 (MapTileRules) — 런던 10곳
            var fixedEncounter = forced == null && _board.UsesWorldMap ? Fantasia.World.MapTileRules.EncounterAt(CoordLabel(_currentCoord)) : null;
            var fixedGroup = Fantasia.World.MapTileRules.ResolveEnemies(fixedEncounter, ItemCatalog.Current);
            if (fixedGroup != null && first != null && !first.IsBoss)
            {
                setup.Enemies.AddRange(fixedGroup);
                setup.FixedExp = fixedEncounter.Exp;
                setup.FixedCent = fixedEncounter.Cent;
            }
            else
            {
                setup.Enemies.Add(first);
                // 보스는 단독 (D14). 보스 데이터가 없는 보스 칸은 임시로 소형 적 3마리 — 처치하면 지역 출구가 열린다
                int extra = first != null && first.IsBoss ? 0 : _board.TryGetTile(_currentCoord, out var here) && here.IsBoss ? 2 : 1;
                for (int i = 0; i < extra && pool.Length > 0; i++) setup.Enemies.Add(forced != null ? forced : pool[GameRandom.Range(0, pool.Length)]);
            }
            // 전리품 장비 레벨 = 지역 (런던 1 · 베르사유 4 · 로마 7)
            if (_board.UsesWorldMap) Fantasia.Combat.LootRules.CurrentZoneWearLevel = ZoneWearLevel(_board.Map.RegionOf(_currentCoord));
            session.PendingBattle = setup;
            session.PendingEncounterCoord = _currentCoord;
            session.PendingBattleTheme = _board.BattleThemeAt(_currentCoord);
            session.LastSafeCoord = _lastSafeCoord;
            session.AllyTurnEndedByBattle = true;
            SaveBattleEntry(setup);
            Fantasia.Core.SceneFader.Load(CombatSceneName);
        }

        // 전투 진입 저장 (조우 결과 확정 뒤, 전투 씬 전) — 이어하면 같은 적·같은 개시·같은 난수로 전투 시작
        private void SaveBattleEntry(Fantasia.Combat.BattleSetup setup)
        {
            var s = BoardSession.Instance;
            var snap = new Fantasia.Combat.BattleSnapshot
            {
                started = false,
                enemyIds = setup.Enemies.Where(e => e != null).Select(e => e.Id).ToList(),
                opening = (int)setup.Opening,
                hasEncounterCoord = s.PendingEncounterCoord.HasValue,
                coordQ = s.PendingEncounterCoord?.Q ?? 0,
                coordR = s.PendingEncounterCoord?.R ?? 0,
                reaperFight = s.PendingReaperFight,
                zoneWearLevel = Fantasia.Combat.LootRules.CurrentZoneWearLevel,
                fixedExp = setup.FixedExp,
                fixedCent = setup.FixedCent,
            };
            s.BattleSnapshotJson = JsonUtility.ToJson(snap);
            SaveGame.Checkpoint("전투 진입");
        }

        // EncounterPanel이 판정(슬롯 공개 연출 포함)을 끝낸 뒤 호출한다.
        private void OnEncounterResolved(EncounterOutcome outcome)
        {
            BoardSession.Instance.PendingEncounterOutcome = -1; // 처리함
            _encounterResultMessage = EncounterPanel.OutcomeMessage(outcome);
            switch (outcome)
            {
                case EncounterOutcome.SneakSuccess:
                    // 전투 없이 이 칸에 남는다 (경험치·전리품 없음).
                    StartCoroutine(EndAllyTurn());
                    break;
                case EncounterOutcome.RetreatSuccess:
                    _currentCoord = _lastSafeCoord;
                    _token.position = TokenSurfacePosition(_currentCoord);
                    BoardSession.Instance.PlayerPosition = _currentCoord;
                    StartCoroutine(EndAllyTurn());
                    break;
                default:
                    EnterCombat(outcome);
                    break;
            }
        }

        // 이벤트 발판에 도착하면 0/1/2칸 중 랜덤으로 추가 이동 — 아군 턴당 1회(연쇄 금지). 추가 이동이 생겼으면 true.
        private bool TryGrantEventBonusMove(HexTile landedTile)
        {
            if (landedTile == null || !landedTile.IsEventTile || !Clock.TryConsumeEventBonus()) return false;

            _lastEventBonus = MovementRules.RollEventBonusMove();
            if (_lastEventBonus <= 0) { SaveGame.Checkpoint("이벤트 칸 — 추가 이동 0"); return false; }

            BoardSession.Instance.PendingBonusMove = _lastEventBonus;
            SaveGame.Checkpoint($"이벤트 칸 — 추가 이동 {_lastEventBonus}");
            HighlightReachable(_lastEventBonus);
            if (!_reachability.ReachableTiles.Any()) { _awaitingSelection = false; _board.ClearHighlights(); return false; } // 갈 칸 없으면 보너스 없이 턴 종료
            return true;
        }

        // 아군 턴 종료 → 적 턴(월드 처리) → 다음 아군 턴 시작(중독 등 보드 지속 피해).
        private IEnumerator EndAllyTurnAfter(float seconds)
        {
            _turnEnding = true; // 기다리는 동안 다른 입력 막기 (EndAllyTurn이 다시 켬)
            yield return new WaitForSeconds(seconds);
            _dice.Hide(); // 못 움직이고 턴이 끝난 경우 (이동력 0 등)
            _turnEnding = false;
            yield return EndAllyTurn();
        }

        private IEnumerator EndAllyTurn()
        {
            _turnEnding = true;
            _dice.Hide(); // 11/12: 오른쪽에 둔 주사위는 턴이 끝나면 치움
            _board.ClearHighlights();
            _awaitingSelection = false;

            Clock.EndAllyTurn();
            BoardSession.Instance.PendingMovePoints = -1;
            BoardSession.Instance.PendingBonusMove = -1;
            // 11/15 팀장 "멈췄다가 한꺼번에 진행": 턴 끝 프레임에 300ms 멈춤 → 단계별 시간 기록 (30ms 넘는 단계만)
            var sw = System.Diagnostics.Stopwatch.StartNew(); var lap = new System.Text.StringBuilder();
            void Lap(string name) { if (sw.Elapsed.TotalMilliseconds > 30) lap.Append($"{name} {sw.Elapsed.TotalMilliseconds:0}ms · "); sw.Restart(); }
            SaveGame.Checkpoint("아군 턴 종료"); // 적 턴 처리 전 — 이어하면 적 턴부터 한 번만 처리
            Lap("저장");
            RefreshTopBar(announce: true);
            Lap("상단 바");
            var enemyLog = WorldTurnRules.RunEnemyTurn(Clock);
            Lap("적 턴 규칙");
            enemyLog.AddRange(RunBloodHuntEnemyTurn());
            Lap("피의 사냥");
            RefreshReaperFigures();
            Lap("사신 말");
            if (lap.Length > 0) Debug.Log("[턴 끝 시간] " + lap);
            yield return new WaitForSeconds(EnemyTurnSeconds);

            Clock.EndEnemyTurn();
            // D20 상점 재입고: 완료 월드 라운드 3·6·9…
            if (ItemCatalog.Current != null && Fantasia.Items.ShopRules.TryRestock(BoardSession.Instance.Shop, ItemCatalog.Current.Shops, Clock.WorldRound - 1))
                enemyLog.Add("도시 상점 재입고");
            BoardSession.Instance.UsedWorldItemThisTurn.Clear();
            var allyLog = WorldTurnRules.BeginAllyTurn(_testParty);
            var lines = enemyLog.Concat(allyLog).ToList();

            // 피의 사냥: 아군 턴마다 사신이 1칸씩 다가옴 (생성 직후 턴은 제외) — 파티 칸에 닿으면 바로 조우
            int caught = _board.UsesWorldMap ? BloodHuntRules.MoveReapers(Hunt, _board.Map, _currentCoord, _board.IsExitOpen) : -1;
            RefreshReaperFigures();
            if (Hunt.Active) lines.Add($"사신이 다가온다 (남은 라운드 {Hunt.AllyTurnsLeft} · 남은 사신 {Hunt.Reapers.Count})");

            _worldMessage = lines.Count > 0 ? string.Join(" / ", lines) : null;
            foreach (var line in lines) Debug.Log("[World] " + line);
            RefreshTopBar(announce: true);
            _turnEnding = false;
            if (caught < 0) AutoSave();
            if (caught >= 0 && ReaperEnemy != null) ShowReaperEncounter();
        }
    }
}
