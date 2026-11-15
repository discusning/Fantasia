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
    // 자동 플레이 점검용 Dev 함수 (11/15 긴 파일 정리: BoardTestController.cs에서 옮김, 내용 그대로)
    public partial class BoardTestController
    {
        // ───── 자동 플레이 점검(Dev/AutoPlayBot)용 — 키보드·마우스 대신 같은 굴림·이동 코드를 부른다 ─────
        // 자동 플레이 점검에서 특정 적 조합을 강제할 때 (예: 그림자 사냥개 특성 확인). 평소엔 null.
        public static string DevForcedEnemyId;
        public static int DevForceOpening = -1; // 자동 플레이: 조우 결과 강제 (예: 기습 실패)

        // 새 아군 턴을 시작할 수 있는 상태 (이동·창·적 턴 연출이 모두 끝났고 아직 안 굴림).
        public bool DevIsIdle => !_isMoving && !_turnEnding && !EncounterPanel.IsOpen && !LandmarkInfoPanel.IsOpen && !DialoguePanel.IsOpen && !InventoryPanel.IsOpen && !Fantasia.UI.ShopPanel.IsOpen && !CaseLogPanel.IsOpen && !ConfirmPanel.IsOpen && !EventPanel.IsOpen && !TreasurePanel.IsOpen
                                 && Clock.Phase == WorldPhase.AllyTurn && !Clock.HasRolledThisTurn;
        public bool DevAwaitingSelection => _awaitingSelection;
        public int DevWorldRound => Clock.WorldRound;
        public HexCoord DevPosition => _currentCoord;
        public int DevLastRoll => _lastRoll;
        public string DevResultMessage => _encounterResultMessage;
        // 그 칸에 도착한 것처럼 (회복·보물·이벤트 칸 점검용)
        public void DevArriveAt(string label) { DevTeleport(label); StartCoroutine(Arrive()); }
        // 석상 배치 전(런던 밖) 점검용: 그 칸을 석상으로 보고 기도 창을 연다
        public void DevOpenStatueAt(string label)
        {
            if (!WorldMapData.TryParseLabel(label, out var c)) return;
            _board.TryGetTile(c, out var t);
            OpenStatue(t, c, m => { if (m != null) _encounterResultMessage = m; });
        }

        public void DevRoll() => TryRoll();
        public bool DevDiceRolling => _diceRolling; // 3D 주사위가 구르는 중 (멈춘 뒤 칸 선택)
        public BoardDice DevDice => _dice;
        public int DevLastFace => _lastFace; // 주사위에 나온 눈 (피의 사냥 −2 전)
        public void DevOpenShop() => OpenShopHere();
        public void DevRefreshTopBar() => RefreshTopBar(announce: false);
        public void DevLoadLastSave() => LoadLastSave();
        // 런던 거점으로 옮겨 제임스 보고 (자동 플레이 점검용)
        public void DevReportToJames() { DevTeleport(ActOneStory.HubLabel); StartCoroutine(ReportToJames()); }

        // 자동 플레이: 월드맵 칸 이름(예: "M10")으로 파티를 옮긴다
        public void DevTeleport(HexCoord coord)
        {
            if (!_board.TryGetTile(coord, out _)) return;
            _currentCoord = coord; _lastSafeCoord = coord;
            BoardSession.Instance.PlayerPosition = coord;
            _token.position = TokenSurfacePosition(coord);
        }

        public void DevTeleport(string label)
        {
            if (!_board.UsesWorldMap || !WorldMapData.TryParseLabel(label, out var coord) || !_board.TryGetTile(coord, out _)) return;
            _currentCoord = coord;
            _lastSafeCoord = coord;
            BoardSession.Instance.PlayerPosition = coord;
            _token.position = TokenSurfacePosition(coord);
            var rig = Camera.main != null ? Camera.main.GetComponent<BoardCameraRig>() : null;
            if (rig != null) rig.SnapToTarget();
        }

        // 이동 가능한 칸 중 아직 안 깬 인카운터가 있으면 그 칸, 없으면 가장 가까운 인카운터에 제일 다가가는 칸.
        public void DevMoveTowardEncounter() => DevMoveTowardEncounter(bossOnly: false);

        // 피의 사냥 점검: 가장 가까운 사신 쪽으로
        public void DevMoveTowardReaper()
        {
            if (!_awaitingSelection || !Hunt.Active) return;
            var field = _board.DistanceField(Hunt.Reapers);
            HexCoord best = _currentCoord;
            int bestDistance = int.MaxValue;
            foreach (var coord in _reachability.ReachableTiles)
            {
                int d = field.TryGetValue(coord, out int v) ? v : int.MaxValue;
                if (d < bestDistance) { bestDistance = d; best = coord; }
            }
            MoveTo(best);
        }

        // 피의 사냥 점검: 전투 칸을 피해 가까운 빈 칸으로 (게이지가 찰 때까지 시간 보내기)
        // 이동 QA: 목표 쪽으로, 단 적 칸에는 서지 않음
        public void DevMoveTowardAvoiding(IEnumerable<HexCoord> targets)
        {
            if (!_awaitingSelection) return;
            var field = _board.DistanceField(targets.ToList());
            var options = _reachability.ReachableTiles.Where(c => _board.TryGetTile(c, out var t) && !t.IsEncounter && !t.IsBoss).ToList();
            if (options.Count == 0) return;
            var best = options.OrderBy(c => field.TryGetValue(c, out int v) ? v : int.MaxValue).ThenByDescending(c => c.DistanceTo(_currentCoord)).First();
            MoveTo(best);
        }
        public bool DevMoving => _isMoving;
        public Transform DevToken => _token;

        public void DevMoveAvoidingEncounters()
        {
            if (!_awaitingSelection) return;
            var safe = _reachability.ReachableTiles
                .Where(c => _board.TryGetTile(c, out var t) && !t.IsEncounter && t.MapKind == MapTileKind.Walkable)
                .OrderBy(c => c.DistanceTo(_currentCoord)).ToList();
            if (safe.Count > 0) MoveTo(safe[0]);
        }

        // 통플레이 점검: 목표 칸들 중 실제 이동 거리로 가장 가까운 쪽으로 (도달 가능한 칸 중 최선)
        public void DevMoveToward(IEnumerable<HexCoord> targets)
        {
            if (!_awaitingSelection) return;
            var field = _board.DistanceField(targets.ToList());
            HexCoord best = _currentCoord;
            int bestDistance = int.MaxValue;
            foreach (var coord in _reachability.ReachableTiles)
            {
                int d = field.TryGetValue(coord, out int v) ? v : int.MaxValue;
                if (d < bestDistance) { bestDistance = d; best = coord; }
            }
            if (best.Equals(_currentCoord) && _reachability.ReachableTiles.Any()) best = _reachability.ReachableTiles.First();
            MoveTo(best);
        }

        public HexBoard DevBoard => _board;

        // 영상 촬영용: 적 칸에서 가장 먼 빈 길 칸으로 (조우·이벤트 창이 안 뜨게)
        public void DevMoveToQuietTile()
        {
            if (!_awaitingSelection) return;
            var encounters = _board.Tiles.Where(t => t.IsEncounter && !t.IsCleared).Select(t => t.Coordinate).ToList();
            var field = _board.DistanceField(encounters);
            var quiet = _reachability.ReachableTiles
                .Where(c => _board.TryGetTile(c, out var t) && !t.IsEncounter && t.MapKind == MapTileKind.Walkable)
                .OrderByDescending(c => field.TryGetValue(c, out int v) ? v : 999).ToList();
            MoveTo(quiet.Count > 0 ? quiet[0] : _reachability.ReachableTiles.First());
        }

        // bossOnly: 보스 칸만 목표 (자동 플레이 -autoplay-boss)
        public void DevMoveTowardEncounter(bool bossOnly)
        {
            if (!_awaitingSelection) return;
            var encounters = new List<HexCoord>();
            foreach (var tile in _board.Tiles)
                if (tile.IsEncounter && !tile.IsCleared && tile.Enemy != null && tile.IsBoss == bossOnly) encounters.Add(tile.Coordinate);

            // 장애물을 돌아가는 실제 이동 거리로 가장 가까운 적 칸 쪽
            var field = _board.DistanceField(encounters);
            HexCoord best = _currentCoord;
            int bestDistance = int.MaxValue;
            foreach (var coord in _reachability.ReachableTiles)
            {
                int d = field.TryGetValue(coord, out int v) ? v : int.MaxValue;
                if (d < bestDistance) { bestDistance = d; best = coord; }
            }
            MoveTo(best);
        }


        // 11/14 팀장: 안내 상자를 없앤 뒤 "방금 일어난 일"은 아이템 알림(2초 토스트)으로 — 추가 이동·전투/칸 결과, 저장 실패는 빨간 글자.
        private int _toastedBonus = -1;
        private string _toastedResult;
        private string _toastedSaveError;
        private void ToastBoardEvents()
        {
            if (EncounterPanel.IsOpen || BoardHintPanel.AnyModalOpen) return; // 창이 닫힌 뒤에 보여 줌
            if (_lastEventBonus >= 0 && _lastEventBonus != _toastedBonus) ItemAcquiredToast.Show($"이벤트 칸 추가 이동 +{_lastEventBonus}칸");
            _toastedBonus = _lastEventBonus;
            if (!string.IsNullOrEmpty(_encounterResultMessage) && _encounterResultMessage != _toastedResult) ItemAcquiredToast.Show(_encounterResultMessage);
            _toastedResult = _encounterResultMessage;
            var err = SaveGame.LastError;
            if (err != null && err != _toastedSaveError) ItemAcquiredToast.Show("저장 실패", textColor: new Color(0.88f, 0.4f, 0.35f));
            _toastedSaveError = err;
        }
        // 보드 왼쪽 안내 (BoardHintPanel — 예전 개발용 화면 글자를 대신, 다른 창이 열리면 숨김)
        private void RefreshHint()
        {
            string rollHint = Clock.Phase != WorldPhase.AllyTurn ? "적 턴 진행 중..."
                : Clock.HasRolledThisTurn ? $"주사위: {_lastRoll}" : "Space: 주사위 굴리기";
            BoardHintPanel.Set(
                $"<b>{rollHint}</b>   <color=#CFC8BC>파티장 {Leader?.Job.DisplayName ?? "-"}</color>",
                _awaitingSelection ? "밝은 칸 클릭: 이동" : _isMoving ? "이동 중..." : null,
                _lastEventBonus >= 0 ? $"이벤트 칸 추가 이동 +{_lastEventBonus}칸 (이번 턴 1회)" : null,
                // 11/14 탐정 수첩: 칸 색·판정 칸·피의 흔적·집중력·키·카메라 안내(기획 회신 07 문구 포함)는 수첩으로 옮김 → 여기는 이번 턴 상태만
                "<color=#CFC8BC>H: 탐정 수첩 (조작·규칙)" + (OnHubTile ? " · B: 상점" : "") + (_hasSave && DevLoadAllowed ? " · F9: 불러오기(개발)" : "") + "</color>",
                SaveGame.LastError != null ? "<color=#E0675A>저장 실패</color>" : null,
                !EncounterPanel.IsOpen && !string.IsNullOrEmpty(_encounterResultMessage) ? _encounterResultMessage : null,
                !string.IsNullOrEmpty(_worldMessage) ? "턴 시작: " + _worldMessage : null);
        }
    }
}
