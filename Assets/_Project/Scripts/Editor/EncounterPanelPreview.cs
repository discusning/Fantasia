using Fantasia.Data;
using Fantasia.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // -executeMethod entry point for headless UI verification of EncounterPanel (+ the bottom
    // PartyHudPanel) — force-opens it against the seeded 그림자 사냥개 + 3인 파티 and saves
    // Logs/encounter_panel_screenshot.png. The panel is Screen Space - Overlay in
    // game, which a camera render can't see, so the preview switches it to
    // Screen Space - Camera just for the capture.
    public static class EncounterPanelPreview
    {
        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("PreviewCamera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.36f, 0.34f);

            var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/Enemy_그림자사냥개.asset");
            var job = AssetDatabase.LoadAssetAtPath<JobDefinition>("Assets/_Project/Data/Jobs/Job_명탐정.asset");
            if (enemy == null || job == null)
            {
                Debug.LogError("[EncounterPanelPreview] 샘플 자산이 없다 — 먼저 Fantasia/Data/Seed Sample Game Data 실행.");
                return;
            }

            // 하단 파티 HUD도 함께 — 게임에서처럼 조우 창 아래에 깔린다.
            var party = new Party();
            party.AddMember(new PartyMember(job));
            foreach (var name in new[] { "Job_역병의사", "Job_저격수" })
            {
                var other = AssetDatabase.LoadAssetAtPath<JobDefinition>($"Assets/_Project/Data/Jobs/{name}.asset");
                if (other != null) party.AddMember(new PartyMember(other));
            }
            PartyHudPanel.Show(party, party.Members[0]);
            EncounterPanel.Show(enemy, party.Members[0], _ => { });

            PartyHudPanel.Instance.RenderPortraitsNow();
            ToCameraCanvas(PartyHudPanel.Instance, cam, 2f);
            ToCameraCanvas(EncounterPanel.Instance, cam, 1f);
            Canvas.ForceUpdateCanvases();

            EditorScreenshotUtility.Capture(cam, "encounter_panel_screenshot.png");
        }

        // 가까운 planeDistance가 앞에 그려진다 (조우 창 1 < HUD 2).
        private static void ToCameraCanvas(Component panel, Camera cam, float planeDistance)
        {
            var canvas = panel.GetComponentInChildren<Canvas>(true);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = planeDistance;
        }
    }
}
