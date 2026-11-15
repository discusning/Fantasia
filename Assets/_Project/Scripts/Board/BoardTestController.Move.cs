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
    // 주사위·이동·도착·특수 칸(회복·보물) (11/15 긴 파일 정리: BoardTestController.cs에서 옮김, 내용 그대로)
    public partial class BoardTestController
    {
        // 아군 턴에 주사위 1회 (이미 굴렸거나 적 턴이면 무시).
        private void TryRoll()
        {
            if (!Clock.TryBeginRoll()) return;
            SfxPlayer.Play(SfxIds.Dice);
            _lastEventBonus = -1;
            _worldMessage = null;
            _lastRoll = MovementRules.RollMovePoints(pursuedByReaper: Hunt.Active, out int face); // 피의 사냥 중 주사위 −2 (☆)
            // 결과 확정 → 저장 → 표시 (Save.md: 주사위 2가 나오면 껐다 켜도 2인 상태로 칸 선택부터)
            BoardSession.Instance.PendingMovePoints = _lastRoll;
            SaveGame.Checkpoint($"주사위 확정 — {_lastRoll}");
            // 3D 주사위가 굴러 나온 눈(face)이 위로 멈춘 뒤 갈 수 있는 칸 표시 (2026-11-06). 모델이 없으면 바로 표시.
            _diceRolling = true;
            _lastFace = face;
            _dice.Roll(face, () => { _diceRolling = false; ShowRollResult(); }, "이동 " + _lastRoll);
        }

        private bool _diceRolling;
        private int _lastFace;
        private BoardDice _dice;

        private void ShowRollResult()
        {
            HighlightReachable(_lastRoll);
            // 이동력 0(피의 사냥 −2로 1·2) 또는 갈 칸이 없으면 제자리 — 멈추지 않게 아군 턴을 바로 끝낸다
            if (!_reachability.ReachableTiles.Any())
            {
                _awaitingSelection = false;
                _board.ClearHighlights();
                _encounterResultMessage = _lastRoll == 0 ? "이동력 0 — 사신에게 쫓겨 움직이지 못했다" : "갈 수 있는 칸이 없다";
                StartCoroutine(EndAllyTurnAfter(1.2f));
            }
        }

        private void HighlightReachable(int movePoints)
        {
            _reachability = _board.ComputeReachability(_currentCoord, movePoints);
            _board.ClearHighlights();
            foreach (var coord in _reachability.ReachableTiles)
            {
                if (_board.TryGetTile(coord, out var tile)) tile.SetReachable(true);
            }
            _awaitingSelection = true;
        }

        private void TrySelectTile()
        {
            var cam = Camera.main;
            if (cam == null || UIFlow.PointerBlockedByUI()) return; // 11/14: 주사위 버튼·카드 등 UI 위 클릭이 칸 선택으로 새지 않게 (11/15: 투명한 판·글자는 막지 않음)

            // 11/15 팀장 "마우스로 지정해도 안 됨": 예전엔 광선이 처음 맞은 것만 봄 → 칸 위 건물·장식·말 콜라이더에 막히면 칸이 안 골라짐.
            // 이제 광선이 지나는 것 중 가장 가까운 "갈 수 있는 칸".
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            foreach (var hit in Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                var tile = hit.collider.GetComponentInParent<HexTile>();
                if (tile == null || !_reachability.Contains(tile.Coordinate)) continue;
                MoveTo(tile.Coordinate);
                return;
            }
        }

        private void MoveTo(HexCoord target)
        {
            var path = _reachability.GetPathTo(target);
            _board.ClearHighlights();
            _awaitingSelection = false;
            BoardSession.Instance.PendingMovePoints = -1; // 칸을 골랐으면 확정된 이동력 소모
            BoardSession.Instance.PendingBonusMove = -1;
            StartCoroutine(WalkPath(path));
        }

        private IEnumerator WalkPath(List<HexCoord> path)
        {
            _isMoving = true;
            ClearCampTent(); // 11/14 Q4: 다음 이동이 시작되면 텐트를 치움
            _dice.Dock(); // 11/12: 칸을 고르면 주사위는 오른쪽으로 (이번 턴 동안 눈을 보여 줌)
            var walker = Fantasia.UI.CharacterMotion.Of(_tokenFigure); // 11/13 N9 걷기 모션
            if (walker != null) walker.SetWalking(true);
            // 11/13 팀장 지적 "시점이 확확 바뀜": 카메라는 출발→도착 전체 방향으로 한 번만 천천히 돎
            if (path.Count > 0 && Camera.main != null && Camera.main.TryGetComponent<BoardCameraRig>(out var camRig))
                camRig.SetTravel(_token.position, TokenSurfacePosition(path[path.Count - 1]));

            bool first = true;
            foreach (var coord in path)
            {
                var start = _token.position;
                var end = TokenSurfacePosition(coord);
                // 첫 칸만 제자리에서 돌고, 그다음부터는 걸으면서 부드럽게 방향을 바꿈 (칸마다 멈춰 휙 돌던 것)
                if (first) { yield return FaceToward(end); first = false; }
                var dir = end - start; dir.y = 0f;
                var fromRot = _token.rotation;
                var toRot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir, Vector3.up) : fromRot;

                for (float t = 0f; t < secondsPerTile; t += Time.deltaTime)
                {
                    float k = t / secondsPerTile;
                    _token.position = Vector3.Lerp(start, end, k);
                    _token.rotation = Quaternion.Slerp(fromRot, toRot, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.45f))); // 칸의 앞 45% 동안 돌기
                    yield return null;
                }
                _token.rotation = toRot;
                _token.position = end;
                SfxPlayer.Play(SfxIds.Step);
                if (!coord.Equals(_currentCoord)) _lastSafeCoord = _currentCoord;
                _currentCoord = coord;
            }

            _isMoving = false;
            if (walker != null) walker.SetWalking(false);
            _dice.Hide(); // 11/12: 도착하면 오른쪽 주사위도 치움 (조우·이벤트 창과 겹치지 않게)
            BoardSession.Instance.PlayerPosition = _currentCoord;
            BoardSession.Instance.LastSafeCoord = _lastSafeCoord;
            SaveGame.Checkpoint("이동 완료"); // Save.md 3 — 도착 처리(조우·칸 효과)보다 먼저
            yield return Arrive();
        }

        // 도착 처리 — 이동 직후, 또는 이동 완료 저장에서 이어할 때 (조우 창·칸 효과·보고·턴 종료)
        private IEnumerator Arrive()
        {
            // 피의 사냥 사신이 있는 칸에 도착 → 그 사신과 조우
            if (Hunt.Active && Hunt.Reapers.Contains(_currentCoord) && ReaperEnemy != null)
            {
                ShowReaperEncounter();
                yield break;
            }

            // 도착한 칸이 인카운터면 조우 창 (지나가기만 하면 발동하지 않음, 클리어한 칸은 다시 안 뜸).
            if (_board.TryGetTile(_currentCoord, out var landedTile) && landedTile.IsEncounter && !landedTile.IsCleared
                && landedTile.Enemy != null)
            {
                // 정식 보스가 아직 없는 보스 칸(베르사유·로마)은 "개발 중" — 임시 전투로 진행·막 완료 처리하지 않음 (Boss_Abel_v1.0 07)
                if (landedTile.IsBoss && !landedTile.Enemy.IsBoss && !DevAllowTempBosses)
                {
                    _encounterResultMessage = $"{landedTile.MapLabel} — 이 지역의 보스는 개발 중입니다";
                    StartCoroutine(EndAllyTurn());
                    yield break;
                }
                // 1막 보스 권장 레벨 경고 (진입 제한은 없음): [진입] [돌아가기]
                if (ActOneStory.IsActOneBoss(landedTile.Enemy) && _testParty.LivingMembers.Any(m => m.Level < ActOneStory.RecommendedLevel))
                {
                    bool? enter = null;
                    ConfirmPanel.Show(ActOneStory.LevelWarning, "진입", "돌아가기", () => enter = true, () => enter = false);
                    while (enter == null) yield return null;
                    if (enter == false)
                    {
                        // 돌아가기: 직전 칸으로 물러나고 아군 턴 종료 (☆ 불이익 없음)
                        _currentCoord = _lastSafeCoord;
                        _token.position = TokenSurfacePosition(_currentCoord);
                        BoardSession.Instance.PlayerPosition = _currentCoord;
                        StartCoroutine(EndAllyTurn());
                        yield break;
                    }
                }
                _encounterResultMessage = null;
                _pendingEnemy = landedTile.Enemy;
                _pendingIsReaper = false;
                // 판정 결과가 이미 확정·저장된 상태에서 이어하면 창 없이 그 결과로 (Save.md)
                if (BoardSession.Instance.PendingEncounterOutcome >= 0) { OnEncounterResolved((EncounterOutcome)BoardSession.Instance.PendingEncounterOutcome); yield break; }
                EncounterPanel.Show(landedTile.Enemy, Leader, OnEncounterResolved, TileEnemies(landedTile)); // 적 전체 구성 (Q9)
                yield break;
            }

            // 1막: 보스 처치 뒤 런던 거점에 오면 제임스에게 보고 → 1막 완료·베르사유 해금 (그다음 상점)
            if (_board.UsesWorldMap && ActOneStory.NeedsReport(BoardSession.Instance) && _board.Map.TileAt(ActOneStory.HubLabel).Coord.Equals(_currentCoord))
                yield return ReportToJames();

            HandleMapTile(landedTile);

            // 이벤트 칸 추가 이동이 생기면 한 번 더 움직이고, 아니면 아군 턴 종료.
            // 거점 도착으로 상점이 열렸으면 닫을 때까지 기다림 (서비스·제작을 쓰면 상점이 아군 턴을 끝냄)
            // 상점·이벤트·우물 창을 닫을 때까지 (우물은 확인창 → 선택 창 순서라 한 프레임 넘겨 다시 확인)
            while (Fantasia.UI.ShopPanel.IsOpen || EventPanel.IsOpen || ConfirmPanel.IsOpen || TreasurePanel.IsOpen) { yield return null; if (!ConfirmPanel.IsOpen) yield return null; }
            if (_turnEnding || Clock.Phase != WorldPhase.AllyTurn) yield break;
            if (!TryGrantEventBonusMove(landedTile)) StartCoroutine(EndAllyTurn());
        }

        // 월드맵 특수 칸 도착 처리 — 회복·보물은 칸마다 1회 (☆ 개발 임시: 기획 규칙 대기, WorldMap.md)
        private void HandleMapTile(HexTile tile)
        {
            if (tile == null || !_board.UsesWorldMap) return;
            var session = BoardSession.Instance;
            switch (tile.MapKind)
            {
                case MapTileKind.Hub:
                    OpenShopHere();
                    break;
                case MapTileKind.Event:
                    OpenEventHere(tile);
                    break;
                case MapTileKind.Recovery when !tile.IsUsed:
                    OfferRecovery(tile);
                    break;
                case MapTileKind.Treasure when !tile.IsUsed:
                    OpenTreasure(tile);
                    SaveGame.Checkpoint("칸 효과");
                    break;
            }
        }

        // 적 칸 위에 마우스를 올리면 적 구성 (UI 솔루션 Q7): "역병 쥐 ×2 · 가고일 유체 ×1 / 총 3마리"
        private void UpdateTileTooltip()
        {
            var cam = Camera.main;
            bool blocked = cam == null || PauseMenu.IsOpen || BoardTopBar.TurnDropBusy || BoardHintPanel.AnyModalOpen || UIFlow.PointerBlockedByUI();
            HexTile tile = null;
            if (!blocked) tile = Physics.RaycastAll(cam.ScreenPointToRay(Input.mousePosition), 500f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Select(h => h.collider.GetComponentInParent<HexTile>()).FirstOrDefault(t => t != null); // 11/15 건물·장식에 막히지 않게
            var enemies = tile != null && !tile.IsCleared ? TileEnemies(tile) : null;
            if (enemies == null) { TileTooltip.Hide(); return; }
            TileTooltip.Show($"<b>{tile.MapLabel}</b>\n{Fantasia.World.MapTileRules.CompositionText(enemies)}", Input.mousePosition);
        }

        // 그 칸에서 싸우게 될 적 목록 — 지도 툴팁·조우 창·전투가 같은 목록을 쓴다 (Q7·Q9)
        public List<EnemyDefinition> TileEnemies(HexTile tile)
        {
            if (tile == null || !tile.IsEncounter || tile.Enemy == null) return null;
            if (tile.Enemy.IsBoss) return new List<EnemyDefinition> { tile.Enemy };
            var fixedGroup = Fantasia.World.MapTileRules.ResolveEnemies(Fantasia.World.MapTileRules.EncounterAt(CoordLabel(tile.Coordinate)), ItemCatalog.Current);
            return fixedGroup ?? new List<EnemyDefinition> { tile.Enemy };
        }

        // 지도 좌표 이름 (예: J06)
        private static string CoordLabel(HexCoord c) { var (row, col) = WorldMapData.AxialToOffset(c); return WorldMapData.Label(row, col); }

        // 회복 칸 = 우물 (UI 솔루션 Q1): [이용] / [지나가기] → 1명 골라 집중력 +1. 우물마다 1회.
        // 모두 집중력이 가득 찼거나, 취소·지나가기면 횟수를 쓰지 않는다.
        private void OfferRecovery(HexTile tile)
        {
            if (!Fantasia.World.MapTileRules.WellWouldHelp(_testParty))
            {
                _encounterResultMessage = $"{tile.MapLabel} — 모든 동료의 집중력이 가득 찼습니다 (다음에 이용 가능)";
                return;
            }
            ConfirmPanel.Show($"{tile.MapLabel}\n생존 동료 1명의 집중력 {Fantasia.World.MapTileRules.WellFocus} 회복 · 이 우물에서 1회", "이용", "지나가기", () =>
            {
                var session = BoardSession.Instance;
                EventPanel.OpenWell(_testParty, member =>
                {
                    if (session.UsedMapTiles.Contains(tile.Coordinate) || !Fantasia.World.MapTileRules.CanUseWell(member)) return "회복할 수 없다";
                    int gained = Fantasia.World.MapTileRules.ApplyWell(member);
                    session.UsedMapTiles.Add(tile.Coordinate);
                    tile.SetUsed(true);
                    SaveGame.Checkpoint("칸 효과");
                    return $"{member.Job.DisplayName} 집중력 +{gained} ({member.CurrentFocus}/{member.MaxFocus})";
                }, message => _encounterResultMessage = message != null ? $"{tile.MapLabel} {message}" : $"{tile.MapLabel} — 떠남 (다음에 이용 가능)");
            }, () => _encounterResultMessage = $"{tile.MapLabel} — 지나감 (다음에 이용 가능)");
        }

        // 보물 칸 (MapTileRules): 고정 보상 전부 지급, 타일별 파티 공유 1회. 인벤토리가 차면 받을 물건으로 남김.
        private void OpenTreasure(HexTile tile)
        {
            var session = BoardSession.Instance;
            var treasure = Fantasia.World.MapTileRules.TreasureAt(CoordLabel(tile.Coordinate));
            session.UsedMapTiles.Add(tile.Coordinate);
            tile.SetUsed(true);
            if (treasure == null) { _encounterResultMessage = $"{tile.MapLabel} — 빈 보물 칸"; return; }
            _testParty.AddCent(treasure.Cent);
            var names = new List<string> { $"{treasure.Cent} Cent" };
            var shown = new List<(Sprite icon, string name, Color color)>(); // 11/13 보물 창에 보일 물건
            int stored = 0;
            foreach (var id in treasure.ConsumableIds)
            {
                var c = ItemCatalog.Current != null ? ItemCatalog.Current.Consumable(id) : null;
                if (c == null) continue;
                var drop = new Fantasia.Combat.LootDrop(c);
                if (!Fantasia.Combat.LootRules.TryAdd(_testParty.Inventory, drop)) { session.PendingLoot.Add(drop); stored++; }
                names.Add(c.DisplayName);
                shown.Add((ItemLook.IconOf(c), c.DisplayName, ItemLook.NameColor(c)));
            }
            string list = string.Join(" + ", names);
            _encounterResultMessage = $"{tile.MapLabel} — {list}";
            TreasurePanel.Show($"{tile.MapLabel}", treasure.Cent, shown, stored); // 11/13: 알림 한 줄 대신 보상 창 (받았는지 바로 보이게)
            RefreshTopBar(announce: false);
            PlaytestLog.Cent("보물 칸", treasure.Cent, _testParty.Cent, $"{tile.MapLabel}: {list}");
        }
    }
}
