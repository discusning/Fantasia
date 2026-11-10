using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Plain "X 획득" banner, no animation beyond show/auto-hide — deliberately
    // undramatic per design direction (see Docs/Concept_Image/Concept/
    // combat승리_아이템 획득.png for the "make the player feel it" reference,
    // kept simple rather than matching that screen's density).
    //
    // 전리품·이벤트 등 어디서든 ItemAcquiredToast.Show("…")로 띄운다. Screen Space - Overlay (not Camera) because this object
    // survives scene loads but no single scene camera does, and a toast
    // fired right before a scene transition (e.g. combat -> board) needs to
    // keep showing across it.
    public class ItemAcquiredToast : MonoBehaviour
    {
        public static ItemAcquiredToast Instance { get; private set; }

        private const float VisibleSeconds = 1.6f;

        private GameObject _root;
        private Image _icon;
        private Text _label;
        private float _hideAt;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("ItemAcquiredToast").AddComponent<ItemAcquiredToast>();
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
        }

        // 11/11 규칙 1: 알림은 줄을 서서 하나씩 (예전엔 새 알림이 앞 알림을 바로 덮어 앞 것이 안 보였음)
        // 규칙 2: 대화창·창·턴 판이 떠 있으면 기다렸다가 나옴 (UIFlow.ToastBlocked)
        private const float GapSeconds = 0.2f, MaxQueue = 6;
        private readonly System.Collections.Generic.Queue<(string label, Sprite icon, Color tint)> _queue = new System.Collections.Generic.Queue<(string, Sprite, Color)>();
        private float _nextAt;
        private (string label, Sprite icon, Color tint) _current;
        private (string label, Sprite icon, Color tint)? _held; // 턴 판·창 때문에 잠깐 내린 알림

        private void Update()
        {
            if (_root.activeSelf && UIFlow.ToastBlocked)
            {
                // 떠 있는 중에 턴 판·창이 올라오면 잠깐 내렸다가, 끝나면 처음부터 다시 보임 (겹쳐 읽기 어려웠음)
                _held = _current;
                _root.SetActive(false);
            }
            else if (_root.activeSelf && Time.unscaledTime >= _hideAt)
            {
                _root.SetActive(false);
                _nextAt = Time.unscaledTime + GapSeconds;
            }
            if (!_root.activeSelf && Time.unscaledTime >= _nextAt && !UIFlow.ToastBlocked)
            {
                if (_held.HasValue) { var h = _held.Value; _held = null; Display(h.label, h.icon, h.tint, sound: false); }
                else if (_queue.Count > 0) { var next = _queue.Dequeue(); Display(next.label, next.icon, next.tint); }
            }
        }

        // 메인 메뉴로 가면 못 본 알림은 버림 (새 게임에 예전 알림이 뜨지 않게)
        private void OnSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
        {
            if (to.name != "MainMenu") return;
            _queue.Clear();
            _held = null;
            if (_root != null) _root.SetActive(false);
        }

        private void OnEnable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        private void OnDisable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnSceneChanged;

        // "X 획득" 알림. 줄에 넣고, 차례가 오면 나옴.
        public static string DevLast; // 자동 플레이 QA: 마지막으로 요청한 알림
        public static int DevQueued => Instance != null ? Instance._queue.Count + (Instance._held.HasValue ? 1 : 0) : 0;
        public static string DevShowing => Instance != null && Instance._root.activeSelf ? Instance._label.text : null;
        public static void Show(string label, Sprite icon = null, Color? tint = null)
        {
            DevLast = label;
            EnsureExists();
            var q = Instance._queue;
            // 같은 글이 이미 떠 있거나 줄 끝에 있으면 또 넣지 않음
            if ((Instance._root.activeSelf && Instance._label.text == label) || (q.Count > 0 && System.Linq.Enumerable.Last(q).label == label)) return;
            if (q.Count >= MaxQueue) q.Dequeue(); // 너무 밀리면 가장 오래된 것부터 버림
            q.Enqueue((label, icon, tint ?? new Color(0.75f, 0.65f, 0.4f)));
        }

        private void Display(string label, Sprite icon, Color tint, bool sound = true)
        {
            if (sound) Fantasia.Core.SfxPlayer.Play(Fantasia.Core.SfxIds.ItemGet); // 소리는 실제로 보일 때 (다시 보일 때는 없이)
            _current = (label, icon, tint);
            bool hasIcon = icon != null;
            _icon.sprite = icon;
            _icon.color = Color.white;
            _icon.gameObject.SetActive(hasIcon); // 11/11: 그림이 없으면 색 네모 대신 글만
            _label.text = label;
            // 피의 흔적·사냥 경고는 붉은 글, 나머지(획득·완료)는 금색 글
            // 11/11 팀장 결정 규칙 3: 종류별 색 — 경고(피의 흔적·사냥) 붉은색 · 의뢰 완료 초록 · 획득·그 밖 금색. 왼쪽 띠도 같은 색
            var kind = label.StartsWith("피의") ? WarnText : label.StartsWith("의뢰 완료") ? QuestText : GoldText;
            _label.color = kind;
            _accent.color = new Color(kind.r, kind.g, kind.b, 0.9f);
            var labelRect = (RectTransform)_label.transform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            float iconSpace = hasIcon ? Height : 0f;
            labelRect.offsetMin = new Vector2(PadX + iconSpace, 0f);
            labelRect.offsetMax = new Vector2(-PadX, 0f);
            // 글 길이에 맞춰 폭을 늘림 (긴 의뢰 완료·피의 사냥 문장이 잘리던 것)
            float width = Mathf.Clamp(_label.preferredWidth + PadX * 2f + iconSpace, MinWidth, MaxWidth);
            _outer.sizeDelta = new Vector2(width, Height);
            _root.SetActive(true);
            _hideAt = Time.unscaledTime + VisibleSeconds;
        }

        // 11/11 팀장 지적: 예전 흰 상자·회색 테두리 → 칸 정보 상자와 같은 얇은 어두운 판 + 금색 글 (디자인 U8 "작은 상자" 오기 전 임시)
        private static readonly Color GoldText = new Color(0.98f, 0.86f, 0.55f);
        private static readonly Color WarnText = new Color(1f, 0.55f, 0.5f);
        private static readonly Color QuestText = new Color(0.62f, 0.9f, 0.6f);
        private Image _accent;
        private const float Height = 54f, PadX = 22f, MinWidth = 320f, MaxWidth = 960f;
        private RectTransform _outer;

        private void Build()
        {
            var canvasGO = new GameObject("ItemAcquiredToastCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45; // 11/10 UI 점검: 0이면 상단 바(4) 뒤에 가려 안 보였음

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            var inner = UGUIKit.CreateBorderedPanel(canvasGO.transform, "Banner", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), UGUIKit.DefaultBorderColor, new Color(0.12f, 0.1f, 0.1f, 0.96f), 2f);
            UGUIKit.ApplyThinPlate(inner, 0.5f);
            _outer = (RectTransform)inner.transform.parent;
            _outer.sizeDelta = new Vector2(MinWidth, Height);
            _outer.anchoredPosition = new Vector2(0f, -152f); // 상단 바·지역명 칸 아래 (11/11 지역명 칸 내린 만큼 같이)

            // 종류 색 띠 (왼쪽 끝 세로 줄)
            _accent = UGUIKit.CreateImage(inner, "Accent", new Vector2(0f, 0.18f), new Vector2(0f, 0.82f), GoldText);
            var accentRect = (RectTransform)_accent.transform;
            accentRect.offsetMin = new Vector2(7f, 0f);
            accentRect.offsetMax = new Vector2(11f, 0f);
            _icon = UGUIKit.CreateImage(inner, "Icon", new Vector2(0f, 0f), new Vector2(0f, 1f), Color.clear);
            var iconRect = (RectTransform)_icon.transform;
            iconRect.offsetMin = new Vector2(PadX * 0.6f, 8f);
            iconRect.offsetMax = new Vector2(PadX * 0.6f + Height - 16f, -8f);
            _icon.preserveAspect = true;
            _label = UGUIKit.CreateText(inner, "Label", new Vector2(0f, 0f), new Vector2(1f, 1f), "", 18, TextAnchor.MiddleCenter);
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var shadow = _label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(1f, -1f);

            _root = _outer.gameObject;
            _root.SetActive(false);
        }
    }
}
