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
    // 피의 흔적·피의 사냥·영혼 수확 (11/15 긴 파일 정리: BoardTestController.cs에서 옮김, 내용 그대로)
    public partial class BoardTestController
    {
        // ── D24 피의 흔적·피의 사냥 / D25 영혼 수확 ──
        private List<string> RunBloodHuntEnemyTurn()
        {
            var session = BoardSession.Instance;
            int g = session.BloodTrail, s = session.SoulHarvest;
            var log = BloodHuntRules.OnEnemyTurn(Hunt, ref g, ref s, _testParty, _board.UsesWorldMap ? _board.Map : null,
                _currentCoord, GameRandom.NewSystemRandom(), out var victim);
            session.BloodTrail = g;
            session.SoulHarvest = s;
            foreach (var line in log)
                if (line.StartsWith("피의 사냥!") || line.StartsWith("피의 사냥 실패")) ItemAcquiredToast.Show(line);
            return log;
        }

        private void ShowReaperEncounter()
        {
            _encounterResultMessage = null;
            _pendingEnemy = ReaperEnemy;
            _pendingIsReaper = true;
            EncounterPanel.Show(ReaperEnemy, Leader, OnEncounterResolved);
        }

        // 사신전에서 이기고 돌아왔으면 그 사신 제거 (마지막이면 완수 보상), 아니면 사신은 그대로
        private void ResolveReaperFightResult()
        {
            var session = BoardSession.Instance;
            if (!session.PendingReaperFight) return;
            session.PendingReaperFight = false;
            if (!Hunt.ReaperKilled) return;
            int g = session.BloodTrail;
            int centBefore = _testParty.Cent;
            string message = BloodHuntRules.OnReaperKilled(Hunt, _currentCoord, ref g, _testParty, ItemCatalog.Current);
            RefreshReaperFigures();
            PlaytestLog.Cent("피의 사냥 사신 처치", _testParty.Cent - centBefore, _testParty.Cent, message);
            session.BloodTrail = g;
            _encounterResultMessage = message;
            ItemAcquiredToast.Show(message);
            Debug.Log("[World] " + message);
        }

        // 보드 위 사신 말 (사신 모델 축소)
        private void RefreshReaperFigures()
        {
            foreach (var go in _reaperFigures) if (go != null) Destroy(go);
            _reaperFigures.Clear();
            if (!Hunt.Active || ReaperEnemy == null) return;
            foreach (var coord in Hunt.Reapers)
            {
                var fig = Fantasia.UI.CharacterModels.Create(ReaperEnemy, out float reaperHeight); // 받은 모델이 있으면 그것, 없으면 임시 인형
                Fantasia.UI.BlobShadow.Attach(fig, reaperHeight); // 발밑 흐린 그림자 (11/10 기획 1A)
                fig.transform.position = _board.CoordToWorld(coord) + Vector3.up * _board.TileHeight;
                fig.transform.localScale = Vector3.one * 0.6f;
                fig.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                _reaperFigures.Add(fig);
            }
        }
    }
}
