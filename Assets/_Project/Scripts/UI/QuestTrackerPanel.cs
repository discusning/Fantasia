using Fantasia.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Always-on quest tracker anchored to the screen's top-right, per the
    // reference layout in Docs/Concept_Image/Concept/판타지아_UI(1).png
    // (title / objective line / rounds-remaining, right side of screen).
    // Real quest content doesn't exist yet (GDD TBD) — this only proves the
    // UI mechanism using the dummy quest BoardSession seeds itself with.
    //
    // Screen Space - Overlay + DontDestroyOnLoad, same reasoning as
    // ItemAcquiredToast: no single scene camera persists across the
    // board <-> combat scene swap, but this panel should.
    public class QuestTrackerPanel : MonoBehaviour
    {
        public static QuestTrackerPanel Instance { get; private set; }

        private GameObject _root;
        private Text _titleText;
        private Text _objectiveText;
        private Text _roundsText;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("QuestTrackerPanel").AddComponent<QuestTrackerPanel>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);

            Build();

            BoardSession.EnsureExists();
            BoardSession.Instance.QuestChanged += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            var session = BoardSession.Instance;
            bool hasQuest = session != null && session.HasQuest;
            _root.SetActive(hasQuest);
            if (!hasQuest) return;

            _titleText.text = session.QuestTitle;
            _objectiveText.text = session.QuestObjective;
            _roundsText.text = $"({session.QuestRoundsRemaining} Rounds)";
        }

        private void Build()
        {
            var canvasGO = new GameObject("QuestTrackerCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            // Anchored below DevSceneNav's OnGUI box (top-right, ~100px tall)
            // so the two don't overlap.
            var inner = UGUIKit.CreateBorderedPanel(canvasGO.transform, "QuestBox", new Vector2(1f, 1f), new Vector2(1f, 1f),
                UGUIKit.DefaultBorderColor, new Color(0.12f, 0.12f, 0.16f, 0.9f), 2f);
            var outerRect = (RectTransform)inner.transform.parent;
            outerRect.sizeDelta = new Vector2(260f, 92f);
            outerRect.anchoredPosition = new Vector2(-10f, -110f);

            _titleText = UGUIKit.CreateText(inner, "Title", new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.95f), "", 14, TextAnchor.MiddleLeft);
            _titleText.color = new Color(0.95f, 0.85f, 0.55f);
            _titleText.fontStyle = FontStyle.Bold;

            _objectiveText = UGUIKit.CreateText(inner, "Objective", new Vector2(0.05f, 0.28f), new Vector2(0.95f, 0.62f), "", 11, TextAnchor.UpperLeft);
            _objectiveText.color = Color.white;

            _roundsText = UGUIKit.CreateText(inner, "Rounds", new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.28f), "", 11, TextAnchor.LowerRight);
            _roundsText.color = new Color(0.75f, 0.75f, 0.75f);

            _root = outerRect.gameObject;
            _root.SetActive(false);
        }
    }
}
