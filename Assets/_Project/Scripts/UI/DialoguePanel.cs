using Fantasia.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Bare-bones sequential dialogue box — proves the "completing a quest
    // triggers an NPC dialogue" mechanism end to end. Each line names its
    // speaker (a story with several characters won't always be one NPC
    // talking) and DialogueStageController in DialogueTest highlights
    // whichever placeholder capsule matches. No real character portraits,
    // skip-to-next-key-line, or accept/decline choice yet (see GDD 6.6 idea
    // notes, based on Docs/Concept_Image/Concept/판타지아_UI(대화).png) —
    // this pass is deliberately just "a few text boxes in a row, then done."
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
        // A story with several characters won't always mean talking to the
        // same one — each line names its own speaker so the conversation
        // partner can change mid-dialogue (DialogueStageController reacts
        // to SpeakerChanged to swap which placeholder capsule is highlighted).
        public readonly struct DialogueLine
        {
            public readonly string Speaker;
            public readonly string Text;

            public DialogueLine(string speaker, string text)
            {
                Speaker = speaker;
                Text = text;
            }
        }

        public static DialoguePanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

        public string CurrentSpeaker { get; private set; }

        // Fires whenever the shown line's speaker changes — DialogueStageController
        // (in DialogueTest) subscribes to this instead of DialoguePanel needing
        // to know anything about the 3D scene it's floating over.
        public event System.Action<string> SpeakerChanged;

        private const string DialogueSceneName = "DialogueTest";
        private const string ReturnSceneName = "BoardTest";

        // Prototype-only: completing this specific dummy sub quest fires a
        // sample dialogue. Real per-quest dialogue data doesn't exist yet.
        // Two speakers so the character-switching mechanism actually gets
        // exercised, not just a single NPC monologue.
        private const string TestQuestTitle = "1차 개발 완성";
        private static readonly DialogueLine[] SampleLines =
        {
            new DialogueLine("팀장 요정", "수고하셨습니다, 개발자님."),
            new DialogueLine("개발자", "감사합니다! 1차 프로토타입을 마무리했어요."),
            new DialogueLine("팀장 요정", "이제 다음 단계로 넘어갈 준비가 된 것 같습니다."),
            new DialogueLine("개발자", "앞으로의 작업도 기대해주세요!"),
        };

        private GameObject _root;
        private Text _speakerText;
        private Text _bodyText;
        private Text _nextButtonLabel;
        private DialogueLine[] _lines;
        private int _lineIndex;
        private DialogueLine[] _pendingLines;

        private bool _initialized;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("DialoguePanel").AddComponent<DialoguePanel>().Initialize();
        }

        public static void Play(DialogueLine[] lines)
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

        private void PlayInternal(DialogueLine[] lines)
        {
            if (lines == null || lines.Length == 0) return;

            _lines = lines;
            _lineIndex = 0;
            ShowCurrentLine();
            _root.SetActive(true);
        }

        private void ShowCurrentLine()
        {
            var line = _lines[_lineIndex];
            _speakerText.text = line.Speaker;
            _bodyText.text = line.Text;
            _nextButtonLabel.text = _lineIndex == _lines.Length - 1 ? "닫기" : "다음";

            CurrentSpeaker = line.Speaker;
            SpeakerChanged?.Invoke(line.Speaker);
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

            _speakerText = UGUIKit.CreateText(inner, "Speaker", new Vector2(0.04f, 0.8f), new Vector2(0.6f, 0.94f), "", 14, TextAnchor.MiddleLeft);
            _speakerText.color = new Color(0.95f, 0.85f, 0.55f);
            _speakerText.fontStyle = FontStyle.Bold;

            _bodyText = UGUIKit.CreateText(inner, "Body", new Vector2(0.04f, 0.32f), new Vector2(0.96f, 0.78f), "", 16, TextAnchor.UpperLeft);
            _bodyText.color = Color.white;

            var nextButton = UGUIKit.CreateButton(inner, "NextButton", new Vector2(0.78f, 0.06f), new Vector2(0.96f, 0.26f), "다음", new Color(0.2f, 0.2f, 0.24f), 13);
            nextButton.onClick.AddListener(Advance);
            _nextButtonLabel = nextButton.GetComponentInChildren<Text>();

            _root = outerRect.gameObject;
            _root.SetActive(false);
        }
    }
}
