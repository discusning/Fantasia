using Fantasia.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Bare-bones sequential dialogue box — proves the "completing a quest
    // triggers an NPC dialogue" mechanism end to end. No character portrait
    // (the "NPC" capsule in DialogueTest fills in for that), skip-to-
    // next-key-line, or accept/decline choice yet (see GDD 6.6 idea notes,
    // based on Docs/Concept_Image/Concept/판타지아_UI(대화).png) — this
    // pass is deliberately just "a few text boxes in a row, then done."
    //
    // Per feedback, the text box alone floating over the board wasn't
    // enough — the player should visibly move to a space where the 3D
    // conversation partner exists, the same way an encounter moves you to
    // CombatTest. So a quest completion now loads a dedicated DialogueTest
    // scene (a placeholder capsule NPC + camera), plays the lines once that
    // scene is loaded, and returns to BoardTest when the last line closes.
    //
    // Subscribes to BoardSession.QuestCompleted itself (same decoupling
    // pattern as ItemAcquiredToast/QuestTrackerPanel) rather than having
    // BoardTestController know anything about dialogue.
    //
    // Screen Space - Overlay + DontDestroyOnLoad, same reasoning as the
    // other persistent UI panels in this project — it needs to keep
    // showing across the BoardTest -> DialogueTest -> BoardTest round trip.
    public class DialoguePanel : MonoBehaviour
    {
        public static DialoguePanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

        private const string DialogueSceneName = "DialogueTest";
        private const string ReturnSceneName = "BoardTest";

        // Prototype-only: completing this specific dummy sub quest fires a
        // sample dialogue. Real per-quest dialogue data doesn't exist yet.
        private const string TestQuestTitle = "1차 개발 완성";
        private static readonly string[] SampleLines =
        {
            "수고하셨습니다, 개발자님.",
            "1차 프로토타입이 무사히 완성되었네요.",
            "이제 다음 단계로 넘어갈 준비가 된 것 같습니다.",
            "앞으로의 작업도 기대하겠습니다!",
        };

        private GameObject _root;
        private Text _bodyText;
        private Text _nextButtonLabel;
        private string[] _lines;
        private int _lineIndex;
        private string[] _pendingLines;

        private bool _initialized;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("DialoguePanel").AddComponent<DialoguePanel>().Initialize();
        }

        public static void Play(string[] lines)
        {
            EnsureExists();
            Instance.PlayInternal(lines);
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
            BoardSession.Instance.QuestCompleted += OnQuestCompleted;
        }

        private void OnQuestCompleted(BoardSession.QuestEntry quest)
        {
            if (quest.Title != TestQuestTitle) return;

            _pendingLines = SampleLines;
            SceneManager.sceneLoaded += OnDialogueSceneLoaded;
            SceneManager.LoadScene(DialogueSceneName);
        }

        private void OnDialogueSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != DialogueSceneName) return;
            SceneManager.sceneLoaded -= OnDialogueSceneLoaded;
            PlayInternal(_pendingLines);
        }

        private void PlayInternal(string[] lines)
        {
            if (lines == null || lines.Length == 0) return;

            _lines = lines;
            _lineIndex = 0;
            ShowCurrentLine();
            _root.SetActive(true);
        }

        private void ShowCurrentLine()
        {
            _bodyText.text = _lines[_lineIndex];
            _nextButtonLabel.text = _lineIndex == _lines.Length - 1 ? "닫기" : "다음";
        }

        private void Advance()
        {
            if (_lineIndex < _lines.Length - 1)
            {
                _lineIndex++;
                ShowCurrentLine();
            }
            else
            {
                _root.SetActive(false);

                // Only leave the dialogue space if that's actually where we
                // are — Play(lines) can also be called directly (e.g. tests)
                // without ever loading DialogueTest.
                if (SceneManager.GetActiveScene().name == DialogueSceneName)
                {
                    SceneManager.LoadScene(ReturnSceneName);
                }
            }
        }

        private void Build()
        {
            var canvasGO = new GameObject("DialogueCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<GraphicRaycaster>();

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            // Bottom-anchored wide box — classic VN-style dialogue placement
            // (GDD 7 already calls for this look in the prologue).
            var inner = UGUIKit.CreateBorderedPanel(canvasGO.transform, "DialogueBox", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                UGUIKit.DefaultBorderColor, new Color(0.08f, 0.08f, 0.1f, 0.95f), 2f);
            var outerRect = (RectTransform)inner.transform.parent;
            outerRect.pivot = new Vector2(0.5f, 0f);
            outerRect.sizeDelta = new Vector2(900f, 160f);
            outerRect.anchoredPosition = new Vector2(0f, 40f);

            _bodyText = UGUIKit.CreateText(inner, "Body", new Vector2(0.04f, 0.32f), new Vector2(0.96f, 0.92f), "", 16, TextAnchor.UpperLeft);
            _bodyText.color = Color.white;

            var nextButton = UGUIKit.CreateButton(inner, "NextButton", new Vector2(0.78f, 0.06f), new Vector2(0.96f, 0.26f), "다음", new Color(0.2f, 0.2f, 0.24f), 13);
            nextButton.onClick.AddListener(Advance);
            _nextButtonLabel = nextButton.GetComponentInChildren<Text>();

            _root = outerRect.gameObject;
            _root.SetActive(false);
        }
    }
}
