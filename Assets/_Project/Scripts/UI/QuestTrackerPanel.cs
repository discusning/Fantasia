using System.Linq;
using Fantasia.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Always-on quest tracker anchored to the screen's top-right, per the
    // reference layout in Docs/Concept_Image/Concept/판타지아_UI(지형 오브젝트, 퀘스트).png
    // (title / objective line, right side of screen). Several quests can be
    // active at once (e.g. one Main + one Sub) — each gets its own stacked
    // box, with the Main quest pinned at the top and Sub quests stacking
    // below it, so the primary objective stays in the same spot regardless
    // of how many side quests are active.
    //
    // Title color signals Main vs Sub (see QuestType) instead of a text
    // label — gold/silver "tier" coloring is the common convention for
    // quest-log importance (medal-tier metaphor used across MMO/mobile RPG
    // quest logs) and keeps each box from getting more cluttered with an
    // extra label.
    //
    // The title also gets a slow brightness pulse ("shimmer") so the boxes
    // draw the eye a bit more, per feedback that they read as too quiet.
    // A moving specular sweep (the flashier version seen on legendary item
    // names in some ARPGs) needs a custom shader/mask on the text mesh; a
    // periodic Lerp-toward-white on the plain uGUI Text color is the
    // lightweight code-only equivalent and is deliberately kept subtle.
    //
    // Real quest content doesn't exist yet (GDD TBD) — this only proves the
    // UI mechanism using the dummy quests BoardSession seeds itself with.
    //
    // Screen Space - Overlay + DontDestroyOnLoad, same reasoning as
    // ItemAcquiredToast: no single scene camera persists across the
    // board <-> combat scene swap, but this panel should.
    public class QuestTrackerPanel : MonoBehaviour
    {
        public static QuestTrackerPanel Instance { get; private set; }

        private static readonly Color MainQuestColor = new Color(0.95f, 0.85f, 0.55f); // gold
        private static readonly Color SubQuestColor = new Color(0.78f, 0.8f, 0.82f); // silver

        private const float ShimmerSpeed = 1.6f; // radians/sec
        private const float ShimmerStrength = 0.35f; // 0 = off, 1 = flashes fully white

        // Placeholder cap on simultaneous quest boxes — not a real design
        // limit, just enough rows pre-built to cover "a main plus a
        // handful of sides" without instantiating UI on every refresh.
        private const int MaxQuestRows = 4;
        private const float RowWidth = 170f; // 189 * 0.9 — 10% smaller, per feedback
        private const float RowHeight = 63f; // 70 * 0.9
        private const float RowGap = 6f;
        private const float TopMargin = 110f; // clears DevSceneNav's OnGUI box (top-right, ~100px tall)
        private const float RightMargin = 10f;

        private class QuestRow
        {
            public GameObject Root;
            public Text Title;
            public Text Objective;
            public Color BaseColor = MainQuestColor;
        }

        private readonly QuestRow[] _rows = new QuestRow[MaxQuestRows];

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
            // Main quest is pinned at the top, subs stack below it — OrderBy
            // is stable, so quests of the same kind keep their original
            // relative order.
            var ordered = session == null
                ? System.Array.Empty<BoardSession.QuestEntry>()
                : session.Quests.OrderBy(q => q.Kind == QuestType.Main ? 0 : 1).ToArray();

            for (int i = 0; i < _rows.Length; i++)
            {
                bool active = i < ordered.Length;
                _rows[i].Root.SetActive(active);
                if (!active) continue;

                var quest = ordered[i];
                _rows[i].Title.text = quest.Title;
                _rows[i].Objective.text = quest.Objective;
                _rows[i].BaseColor = quest.Kind == QuestType.Main ? MainQuestColor : SubQuestColor;
            }
        }

        // Shimmer only touches color, not text/layout, so it's cheap enough
        // to run every frame — no need to gate it behind a coroutine/timer.
        private void Update()
        {
            float glow = (Mathf.Sin(Time.unscaledTime * ShimmerSpeed) + 1f) * 0.5f;
            for (int i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                if (!row.Root.activeSelf) continue;
                row.Title.color = Color.Lerp(row.BaseColor, Color.white, glow * ShimmerStrength);
            }
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

            for (int i = 0; i < _rows.Length; i++)
            {
                _rows[i] = BuildRow(canvasGO.transform, i);
            }
        }

        private QuestRow BuildRow(Transform canvasTransform, int index)
        {
            // Semi-transparent fill so the board reads through it (per
            // feedback) instead of a solid black slab.
            var inner = UGUIKit.CreateBorderedPanel(canvasTransform, $"QuestBox{index}", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Color(0.65f, 0.6f, 0.5f, 0.55f), new Color(0.05f, 0.05f, 0.08f, 0.5f), 1.5f);
            var outerRect = (RectTransform)inner.transform.parent;
            outerRect.sizeDelta = new Vector2(RowWidth, RowHeight);

            // Anchor point (1,1) is the canvas's top-right *corner* — without
            // matching the pivot to that same corner, anchoredPosition offsets
            // from the rect's center instead, pushing roughly half the box
            // past the right edge of the screen (a clipping bug hit earlier).
            // Pivot (1,1) makes anchoredPosition mean "top-right corner of
            // this box, offset from the top-right corner of the canvas".
            outerRect.pivot = new Vector2(1f, 1f);
            outerRect.anchoredPosition = new Vector2(-RightMargin, -TopMargin - index * (RowHeight + RowGap));

            var title = UGUIKit.CreateText(inner, "Title", new Vector2(0.08f, 0.62f), new Vector2(0.95f, 0.92f), "", 10, TextAnchor.MiddleLeft);
            title.color = MainQuestColor;
            title.fontStyle = FontStyle.Bold;

            // Thin divider under the title — small touch so the box doesn't
            // read as one undifferentiated block of text.
            var divider = UGUIKit.CreateImage(inner, "Divider", new Vector2(0.08f, 0.58f), new Vector2(0.92f, 0.6f), new Color(1f, 1f, 1f, 0.25f));
            _ = divider;

            var objective = UGUIKit.CreateText(inner, "Objective", new Vector2(0.08f, 0.08f), new Vector2(0.95f, 0.52f), "", 7, TextAnchor.UpperLeft);
            objective.color = new Color(0.92f, 0.92f, 0.92f);

            outerRect.gameObject.SetActive(false);
            return new QuestRow { Root = outerRect.gameObject, Title = title, Objective = objective };
        }
    }
}
