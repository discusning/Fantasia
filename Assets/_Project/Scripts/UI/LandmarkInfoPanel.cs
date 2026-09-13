using Fantasia.Board;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Ctrl+Right-click info popup for board landmarks (see HexTile.Landmark /
    // BoardTestController.TryInspectLandmark). Styled after the "Lucky's
    // Vault" card in Docs/Concept_Image/Concept/판타지아_UI(1).png and For
    // the King's chest/event popups generally: title + short flavor text +
    // a close button, nothing more, since landmark effects don't exist yet.
    //
    // Right-click is normally a "command" button in this genre (Total War's
    // campaign map, for one) — Ctrl is what turns it into "just look here,"
    // so it won't collide with any future plain-right-click use. See
    // Docs/References/LandmarkUI_Research_2026-09-13.md for the full survey
    // this design is based on (10 games).
    //
    // Screen Space - Overlay + DontDestroyOnLoad, same reasoning as
    // ItemAcquiredToast/QuestTrackerPanel.
    public class LandmarkInfoPanel : MonoBehaviour
    {
        public static LandmarkInfoPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

        private GameObject _root;
        private Image _accentBorder;
        private Text _badgeText;
        private Text _nameText;
        private Text _categoryText;
        private Text _descriptionText;

        private bool _initialized;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("LandmarkInfoPanel").AddComponent<LandmarkInfoPanel>().Initialize();
        }

        public static void Show(LandmarkDefinition landmark)
        {
            EnsureExists();
            Instance.ShowInternal(landmark);
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
        }

        private void ShowInternal(LandmarkDefinition landmark)
        {
            if (landmark == null) return;

            _accentBorder.color = landmark.AccentColor;
            _badgeText.text = landmark.Badge;
            _nameText.text = landmark.LandmarkName;
            _nameText.color = landmark.AccentColor;
            _categoryText.text = landmark.Category;
            _descriptionText.text = landmark.Description;
            _root.SetActive(true);
        }

        private void Close() => _root.SetActive(false);

        private void Build()
        {
            var canvasGO = new GameObject("LandmarkInfoCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<GraphicRaycaster>();

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            // Full-screen dim backdrop doubles as the toggle root (Card is a
            // child of it) so one SetActive shows/hides both, and it reads
            // as a modal like the reference card-on-darkened-map look.
            var backdrop = UGUIKit.CreateImage(canvasGO.transform, "Root", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.35f));
            _root = backdrop.gameObject;

            var inner = UGUIKit.CreateBorderedPanel(backdrop.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Color.white, new Color(0.08f, 0.08f, 0.1f, 0.96f), 2.5f);
            _accentBorder = inner.transform.parent.GetComponent<Image>();
            var outerRect = (RectTransform)inner.transform.parent;
            outerRect.sizeDelta = new Vector2(360f, 220f);
            outerRect.anchoredPosition = Vector2.zero;

            // Badge is the placeholder "icon" (color alone isn't distinct/
            // accessible enough — see research doc) until real art exists.
            _badgeText = UGUIKit.CreateText(inner, "Badge", new Vector2(0.04f, 0.7f), new Vector2(0.22f, 0.94f), "", 26, TextAnchor.MiddleCenter);
            _badgeText.fontStyle = FontStyle.Bold;

            _nameText = UGUIKit.CreateText(inner, "Name", new Vector2(0.24f, 0.78f), new Vector2(0.96f, 0.94f), "", 16, TextAnchor.MiddleLeft);
            _nameText.fontStyle = FontStyle.Bold;

            _categoryText = UGUIKit.CreateText(inner, "Category", new Vector2(0.24f, 0.68f), new Vector2(0.96f, 0.78f), "", 10, TextAnchor.MiddleLeft);
            _categoryText.color = new Color(0.75f, 0.75f, 0.75f);

            _descriptionText = UGUIKit.CreateText(inner, "Description", new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.62f), "", 12, TextAnchor.UpperLeft);
            _descriptionText.color = new Color(0.9f, 0.9f, 0.9f);

            var closeButton = UGUIKit.CreateButton(inner, "CloseButton", new Vector2(0.34f, 0.05f), new Vector2(0.66f, 0.18f), "닫기", new Color(0.2f, 0.2f, 0.24f), 12);
            closeButton.onClick.AddListener(Close);

            _root.SetActive(false);
        }
    }
}
