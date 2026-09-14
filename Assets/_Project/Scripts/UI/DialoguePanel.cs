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
    // whichever placeholder capsule matches. No real character portraits
    // yet (see GDD 6.6 idea notes, based on
    // Docs/Concept_Image/Concept/판타지아_UI(대화).png).
    //
    // Skip always exists (every dialogue needs it), but jumps to the last
    // line rather than closing outright — matching how VN engines like
    // Ren'Py stop skipping at the next choice/unread line instead of
    // barreling past it. Here the "choice" is whatever the last line is:
    // a plain close, or a quest offer.
    //
    // Accept/Decline only exist when PlayInternal was given a QuestOffer —
    // a dialogue that just ends (no offer) never shows them, since forcing
    // an accept/decline prompt onto a conversation with nothing to decide
    // reads as broken (see e.g. WoW players' complaints about quests with
    // no real Decline option).
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

        // Present only when the dialogue's last line is actually offering a
        // quest — Accept adds it via BoardSession.SetQuest, Decline just
        // closes. Nothing else about the dialogue changes.
        public readonly struct QuestOffer
        {
            public readonly string Title;
            public readonly string Objective;
            public readonly QuestType Kind;

            public QuestOffer(string title, string objective, QuestType kind)
            {
                Title = title;
                Objective = objective;
                Kind = kind;
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
            new DialogueLine("팀장 요정", "다음 단계도 준비가 필요할 것 같은데..."),
            new DialogueLine("팀장 요정", "이어서 2차 개발도 맡아주시겠어요?"),
        };
        private static readonly QuestOffer SampleOffer = new QuestOffer("2차 개발 완성", "판타지아 프로토타입 2차 개발을 마무리하라", QuestType.Sub);

        private GameObject _root;
        private Text _speakerText;
        private Text _bodyText;
        private GameObject _nextButtonRoot;
        private Text _nextButtonLabel;
        private GameObject _acceptButtonRoot;
        private GameObject _declineButtonRoot;
        private DialogueLine[] _lines;
        private int _lineIndex;
        private QuestOffer? _offer;
        private DialogueLine[] _pendingLines;
        private QuestOffer? _pendingOffer;

        private bool _initialized;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("DialoguePanel").AddComponent<DialoguePanel>().Initialize();
        }

        public static void Play(DialogueLine[] lines, QuestOffer? offer = null)
        {
            EnsureExists();
            Instance.PlayInternal(lines, offer);
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
            _pendingOffer = SampleOffer;
            SceneManager.sceneLoaded += OnDialogueSceneLoaded;
            SceneManager.LoadScene(DialogueSceneName);
        }

        private void OnDialogueSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != DialogueSceneName) return;
            SceneManager.sceneLoaded -= OnDialogueSceneLoaded;
            PlayInternal(_pendingLines, _pendingOffer);
        }

        private void PlayInternal(DialogueLine[] lines, QuestOffer? offer)
        {
            if (lines == null || lines.Length == 0) return;

            _lines = lines;
            _lineIndex = 0;
            _offer = offer;
            ShowCurrentLine();
            _root.SetActive(true);
        }

        private void ShowCurrentLine()
        {
            var line = _lines[_lineIndex];
            _speakerText.text = line.Speaker;
            _bodyText.text = line.Text;

            bool isLast = _lineIndex == _lines.Length - 1;
            bool showOffer = isLast && _offer.HasValue;

            _nextButtonRoot.SetActive(!showOffer);
            _nextButtonLabel.text = isLast ? "닫기" : "다음";
            _acceptButtonRoot.SetActive(showOffer);
            _declineButtonRoot.SetActive(showOffer);

            CurrentSpeaker = line.Speaker;
            SpeakerChanged?.Invoke(line.Speaker);
        }

        // Jumps straight to the last line instead of closing outright — the
        // last line is this dialogue's one "choice point" (a quest offer,
        // or just the closing line), and skip should never silently resolve
        // that on the player's behalf.
        private void Skip()
        {
            if (_lineIndex >= _lines.Length - 1) return;
            _lineIndex = _lines.Length - 1;
            ShowCurrentLine();
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
                CloseAndReturn();
            }
        }

        private void AcceptOffer()
        {
            if (_offer.HasValue)
            {
                var offer = _offer.Value;
                BoardSession.Instance.SetQuest(offer.Title, offer.Objective, offer.Kind);
            }
            CloseAndReturn();
        }

        private void DeclineOffer() => CloseAndReturn();

        private void CloseAndReturn()
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
            _nextButtonRoot = nextButton.gameObject;

            // Same slot as NextButton — only one of the two states is ever
            // active at once (see ShowCurrentLine).
            var declineButton = UGUIKit.CreateButton(inner, "DeclineButton", new Vector2(0.78f, 0.06f), new Vector2(0.96f, 0.26f), "거절", new Color(0.35f, 0.2f, 0.2f), 13);
            declineButton.onClick.AddListener(DeclineOffer);
            _declineButtonRoot = declineButton.gameObject;

            var acceptButton = UGUIKit.CreateButton(inner, "AcceptButton", new Vector2(0.58f, 0.06f), new Vector2(0.76f, 0.26f), "수락", new Color(0.2f, 0.35f, 0.2f), 13);
            acceptButton.onClick.AddListener(AcceptOffer);
            _acceptButtonRoot = acceptButton.gameObject;

            // Both start hidden — ShowCurrentLine() turns them on only for
            // an offer's last line; without this they'd briefly show at
            // Build() time before the first PlayInternal() call.
            _declineButtonRoot.SetActive(false);
            _acceptButtonRoot.SetActive(false);

            // Always present (every dialogue can be skipped), separate
            // corner from the advance/offer buttons so it can't be
            // mistaken for "confirm."
            var skipButton = UGUIKit.CreateButton(inner, "SkipButton", new Vector2(0.84f, 0.86f), new Vector2(0.97f, 0.98f), "스킵", new Color(0.18f, 0.18f, 0.2f), 10);
            skipButton.onClick.AddListener(Skip);

            _root = outerRect.gameObject;
            _root.SetActive(false);
        }
    }
}
