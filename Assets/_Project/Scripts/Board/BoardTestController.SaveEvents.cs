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
    // 저장·불러오기, 1막 보고, 이벤트 칸 (11/15 긴 파일 정리: BoardTestController.cs에서 옮김, 내용 그대로)
    public partial class BoardTestController
    {
        // ── D27 저장·불러오기: 아군 턴이 시작될 때 자동 저장, F9로 마지막 저장 불러오기 ──
        private bool _hasSave;
        public string LastSaveMessage { get; private set; }

        private void AutoSave()
        {
            if (_testParty == null || _testParty.IsWipedOut) return;
            bool ok = SaveGame.Checkpoint($"아군 턴 시작 — {Clock.WorldRound}라운드");
            _hasSave |= ok;
            LastSaveMessage = ok ? $"자동 저장 — {Clock.WorldRound}라운드" : "자동 저장 실패";
        }

        // F9 불러오기는 개발용 (에디터·개발 빌드만, 배포 빌드에서는 동작 안 함 — Save.md 2)
        public static bool DevLoadAllowed => Application.isEditor || Debug.isDebugBuild;

        private void LoadLastSave()
        {
            if (!DevLoadAllowed) return;
            string error = SaveGame.Apply(SaveGame.ReadFile(), BoardSession.Instance, ItemCatalog.Current);
            if (error != null)
            {
                _worldMessage = "불러오기 실패: " + error;
                Debug.LogWarning("[SaveGame] " + _worldMessage);
                return;
            }
            Debug.Log("[SaveGame] 불러오기 — 보드 다시 열기");
            Fantasia.Core.SceneFader.Load(BoardSession.Instance.BattleSnapshotJson != null ? CombatSceneName : SceneManager.GetActiveScene().name); // 전투 중 저장이면 전투로
        }

        // K12 위 안내 글 — 이번 버전은 1막까지 (세이브 추가 수정안 1)
        private void PlaceExitNotice()
        {
            if (!_board.UsesWorldMap || HexBoard.Act2Available) return;
            var coord = _board.Map.TileAt("K12").Coord;
            var go = new GameObject("ExitNotice_K12");
            go.transform.SetParent(transform, false);
            go.transform.position = _board.CoordToWorld(coord) + Vector3.up * 1.6f;
            // 11/14 팀장 "2막 글자가 안개에 끊겨 보임": 3D 글자는 안개·앞 집에 가려짐 → 화면 UI 글자가 이 자리를 따라다님 (늘 다 보임)
            go.AddComponent<WorldNoticeLabel>().Init("2막 베르사유\n추후 공개");
        }

        private class FaceCamera : MonoBehaviour
        {
            private void LateUpdate()
            {
                if (Camera.main != null) transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
            }
        }

        // ── 1막 마무리: 제임스에게 보고 (Boss_Abel_v1.0 05) — 대사 생략·재접속에도 같은 결과 ──
        private IEnumerator ReportToJames()
        {
            DialoguePanel.Play(ActOneStory.ReportToJames, skipCloses: true);
            while (DialoguePanel.IsOpen) yield return null;
            // 세이브 추가 수정안 1: 보고 완료 → 클리어 저장 → 결과 화면 → 메인 메뉴 (K12·베르사유는 2막 구현 때)
            ActOneStory.CompleteReport(BoardSession.Instance);
            SaveGame.Checkpoint("1막 클리어 (제임스 보고)");
            _board.RefreshExits();
            _worldMessage = "1막 클리어 — 다음 목적지 베르사유 (2막에서 공개)";
            Debug.Log("[Story] " + _worldMessage);
            bool closed = false;
            ActClearPanel.Show(SaveGame.Capture(BoardSession.Instance), "메인 메뉴로", () => closed = true);
            while (!closed) yield return null;
            Fantasia.Core.SceneFader.Load(MainMenuController.SceneName);
        }

        // 개발용: 정식 보스가 없는 보스 칸의 임시 소형 적 전투 허용 (내부 동선 점검 — 자동 플레이 -autoplay-temp-boss)
        public static bool DevAllowTempBosses;

        // ── 이벤트 칸 (점술마녀·분수대 — EventTiles) ──
        // 십자가 석상: 기도 6종 중 1종을 캐릭터 1명에게, 석상당 회차 1회 (확정 때 소모). 기존 기도는 교체.
        private void OpenStatue(HexTile tile, HexCoord coord, System.Action<string> closed)
        {
            var session = BoardSession.Instance;
            EventPanel.OpenStatue(_testParty, session.UsedMapTiles.Contains(coord), (member, prayer) =>
            {
                if (session.UsedMapTiles.Contains(coord) || !member.IsAlive) return "기도를 받을 수 없다";
                var before = member.Prayer;
                member.SetPrayer(prayer);
                session.UsedMapTiles.Add(coord);
                if (tile != null) tile.SetUsed(true);
                SaveGame.Checkpoint($"십자가 석상 — {member.Job.DisplayName} {PrayerRules.Name(prayer)}");
                Debug.Log($"[Event] 십자가 석상 {member.Job.DisplayName} {PrayerRules.Name(before)} → {PrayerRules.Name(prayer)}");
                return before == PrayerKind.None ? $"{member.Job.DisplayName} — {PrayerRules.Name(prayer)}"
                    : $"{member.Job.DisplayName} — {PrayerRules.Name(before)} → {PrayerRules.Name(prayer)} (교체)";
            }, closed);
        }

        private void OpenEventHere(HexTile tile)
        {
            var (row, col) = WorldMapData.AxialToOffset(_currentCoord);
            var kind = EventTiles.KindAt(WorldMapData.Label(row, col));
            var session = BoardSession.Instance;
            var coord = _currentCoord;
            void Closed(string message) { if (message != null) _encounterResultMessage = message; }
            switch (kind)
            {
                case EventKind.FortuneTeller:
                    session.WitchRounds.TryGetValue(coord, out int last);
                    EventPanel.OpenWitch(_testParty, Leader, EventTiles.WitchWaitRounds(last, Clock.WorldRound), (card, target) =>
                    {
                        var result = EventTiles.PlayWitch(_testParty, Leader, card, target);
                        if (!result.Ok) return result;
                        session.WitchRounds[coord] = Clock.WorldRound;
                        SaveGame.Checkpoint($"점술마녀 판정 — {EventTiles.CardName(card)} {(result.Success ? "성공" : "실패")}"); // 결과 공개 전 저장
                        Debug.Log($"[Event] 점술마녀 {EventTiles.CardName(card)} — {result.Message}");
                        return result;
                    }, Closed);
                    break;
                case EventKind.Fountain:
                    EventPanel.OpenFountain(_testParty, session.UsedMapTiles.Contains(coord), member =>
                    {
                        if (!EventTiles.CanUseFountain(member) || session.UsedMapTiles.Contains(coord)) return "기운을 받을 수 없다";
                        bool lp = member.GainLP();
                        int focus = member.RestoreFocus(member.MaxFocus);
                        session.UsedMapTiles.Add(coord);
                        tile.SetUsed(true);
                        SaveGame.Checkpoint($"분수대 — {member.Job.DisplayName} LP {(lp ? "+1" : "그대로")} · 집중 +{focus}");
                        Debug.Log($"[Event] 분수대 {member.Job.DisplayName} LP {member.CurrentLP} · 집중 {member.CurrentFocus}/{member.MaxFocus} (+{focus})");
                        return $"{member.Job.DisplayName} 집중 {member.CurrentFocus}/{member.MaxFocus}{(focus > 0 ? $" (+{focus})" : "")}{(lp ? $" · LP +1 (LP {member.CurrentLP})" : "")}";
                    }, Closed);
                    break;
                case EventKind.Statue:
                    OpenStatue(tile, coord, Closed);
                    break;
                default:
                    _encounterResultMessage = $"{tile.MapLabel} — 이 이벤트는 2막 이후 공개";
                    break;
            }
        }
    }
}
