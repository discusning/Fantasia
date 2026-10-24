using System.Linq;
using Fantasia.Board;
using Fantasia.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // -executeMethod entry point: 저장된 BoardTest 씬을 그대로 열어 Play 때와 같은 순서로
    // 보드·말·하단 파티 HUD를 만든 뒤 Logs/board_scene_preview.png로 찍는다. 씬 자체의
    // 설정(TestPartyJobs 등)이 실제로 반영되는지 확인하는 용도 — 값을 손으로 넣지 않는다.
    public static class BoardScenePreview
    {
        private const string ScenePath = "Assets/_Project/Scenes/Overworld/BoardTest.unity";

        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var controller = Object.FindFirstObjectByType<BoardTestController>();
            var cam = Camera.main;
            if (controller == null || cam == null)
            {
                Debug.LogError("[BoardScenePreview] BoardTestController 또는 Main Camera가 씬에 없다.");
                return;
            }

            var board = controller.GetComponent<HexBoard>();
            board.Generate();
            controller.SpawnToken();
            controller.BuildTestParty();

            // 월드맵: 전체 지도 한 장 (위에서 내려다봄) → 그다음 평소처럼 파티 토큰 따라가는 카메라
            if (board.UsesWorldMap)
            {
                var rot = cam.transform.rotation;
                var bounds = new Bounds(board.Tiles.First().transform.position, Vector3.zero);
                foreach (var t in board.Tiles) bounds.Encapsulate(t.transform.position);
                cam.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
                cam.transform.position = bounds.center - cam.transform.forward * 62f;
                EditorScreenshotUtility.Capture(cam, "board_worldmap_overview.png");
                cam.transform.rotation = rot;
                var token = GameObject.Find("Token");
                if (token != null) Fantasia.Board.BoardCameraRig.Attach(cam, token.transform);
            }

            var hud = PartyHudPanel.Instance;
            if (hud == null)
            {
                Debug.LogError("[BoardScenePreview] 파티 HUD가 만들어지지 않았다.");
                return;
            }
            hud.RenderPortraitsNow();

            var canvas = hud.GetComponentInChildren<Canvas>(true);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();

            int cards = 0;
            foreach (Transform child in canvas.transform)
                if (child.gameObject.activeSelf) cards++;
            Debug.Log($"[BoardScenePreview] 표시된 파티 카드 수: {cards}");

            // 상단 바 — 미리보기용 예시 값 (피의 흔적 40, 영혼 수확 12, 라운드 3)
            var session = Fantasia.Core.BoardSession.Instance;
            session.BloodTrail = 40;
            session.SoulHarvest = 12;
            var party = session.Party;
            var bar = BoardTopBar.EnsureExists();
            bar.Refresh(Fantasia.World.WorldPhase.AllyTurn, 3, party.Leader, session.BloodTrail, session.SoulHarvest, party.Cent, "런던 · 화이트채플", announce: false);
            PortraitStudio.RenderAllNow();
            var barCanvas = bar.GetComponentInChildren<Canvas>(true);
            barCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            barCanvas.worldCamera = cam;
            barCanvas.planeDistance = 0.9f;
            Canvas.ForceUpdateCanvases();
            EditorScreenshotUtility.Capture(cam, "board_scene_preview.png");

            // 적 턴 + 전환 배너
            bar.Refresh(Fantasia.World.WorldPhase.EnemyTurn, 3, party.Leader, 90, 78, party.Cent, "런던 · 화이트채플", announce: false);
            bar.PreviewSplash(ally: false, party.Leader);
            Canvas.ForceUpdateCanvases();
            EditorScreenshotUtility.Capture(cam, "board_scene_preview_enemy.png");
        }
    }
}
