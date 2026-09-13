using Fantasia.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Always-on quest tracker anchored to the screen's top-right, per the
    // reference layout in Docs/Concept_Image/Concept/판타지아_UI(1).png
    // (title / objective line, right side of screen). Title color signals
    // Main vs Sub (see QuestType) instead of a text label — matches common
    // RPG convention (e.g. The Witcher 3, Genshin Impact) and keeps the box
    // from getting more cluttered.
    // Real quest content doesn't exist yet (GDD TBD) — this only proves the
    // UI mechanism using the dummy quest BoardSession seeds itself with.
    //
    // Screen Space - Overlay + DontDestroyOnLoad, same reasoning as
    // ItemAcquiredToast: no single scene camera persists across the
    // board <-> combat scene swap, but this panel should.
    public class QuestTrackerPanel : MonoBehaviour
    {
        public static QuestTrackerPanel Instance { get; private set; }

        private static readonly Color MainQuestColor = new Color(0.95f, 0.85f, 0.55f); // gold
        private static readonly Color SubQuestColor = new Color(0.55f, 0.78f, 0.95f); // blue

        private GameObject _root;
        private Text _titleText;
        private Text _objectiveText;

        private bool _initialized;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("QuestTrackerPanel").AddComponent<QuestTrackerPanel>().Initialize();
        }

        private void Awake() => Initialize();

        // Idempotent, same reasoning as BoardSession.Initialize() — AddComponent
        // reliably fires Awake in Play mode, but not always from editor
        // tooling, so EnsureExists() also calls this directly rather than
        // trusting Awake.
        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

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
            _titleText.color = session.QuestKind == QuestType.Main ? MainQuestColor : SubQuestColor;
            _objectiveText.text = session.QuestObjective;
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
            // so the two don't overlap. Semi-transparent fill so the board
            // reads through it (per feedback) instead of a solid black slab.
            var inner = UGUIKit.CreateBorderedPanel(canvasGO.transform, "QuestBox", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Color(0.65f, 0.6f, 0.5f, 0.55f), new Color(0.05f, 0.05f, 0.08f, 0.5f), 1.5f);
            var outerRect = (RectTransform)inner.transform.parent;
            // ~30% smaller than the original 270x100 — full size read as too
            // intrusive during actual play (per feedback).
            outerRect.sizeDelta = new Vector2(189f, 70f);

            // Anchor point (1,1) is the canvas's top-right *corner* — without
            // matching the pivot to that same corner, anchoredPosition offsets
            // from the rect's center instead, pushing roughly half the box
            // past the right edge of the screen (the clipping bug reported).
            // Pivot (1,1) makes anchoredPosition mean "top-right corner of
            // this box, offset from the top-right corner of the canvas".
            outerRect.pivot = new Vector2(1f, 1f);
            outerRect.anchoredPosition = new Vector2(-10f, -110f);

            // Color is set per-quest in Refresh() (gold=Main, blue=Sub) — the
            // value here is just a sane default before the first Refresh().
            _titleText = UGUIKit.CreateText(inner, "Title", new Vector2(0.08f, 0.62f), new Vector2(0.95f, 0.92f), "", 11, TextAnchor.MiddleLeft);
            _titleText.color = MainQuestColor;
            _titleText.fontStyle = FontStyle.Bold;

            // Thin divider under the title — small touch so the box doesn't
            // read as one undifferentiated block of text.
            var divider = UGUIKit.CreateImage(inner, "Divider", new Vector2(0.08f, 0.58f), new Vector2(0.92f, 0.6f), new Color(1f, 1f, 1f, 0.25f));
            _ = divider;

            _objectiveText = UGUIKit.CreateText(inner, "Objective", new Vector2(0.08f, 0.08f), new Vector2(0.95f, 0.52f), "", 8, TextAnchor.UpperLeft);
            _objectiveText.color = new Color(0.92f, 0.92f, 0.92f);

            _root = outerRect.gameObject;
            _root.SetActive(false);
        }
    }
}
