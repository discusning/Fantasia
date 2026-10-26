using Fantasia.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 게임 중 메뉴 (Esc) — [계속] [설정] [메인 메뉴로] [게임 종료] (저장 예외 34번 기본안).
    // 보드·전투에서만 열린다. 다른 창이 열려 있으면 Esc는 그 창이 먼저 처리한다.
    // 자동 저장만 하는 게임이라 나갈 때 따로 저장하지 않는다 — 이어하기는 마지막 자동 저장 지점부터.
    public class PauseMenu : MonoBehaviour
    {
        private static PauseMenu _instance;
        public static bool IsOpen => _instance != null && (_instance._root.activeSelf || _instance._inSettings);

        private GameObject _root, _confirm;
        private Text _confirmText;
        private System.Action _confirmAction;
        private float _timeScaleBefore = 1f;
        private int _closedFrame = -1;

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color Fill = new Color(0.08f, 0.07f, 0.09f, 0.97f);
        private static readonly Color ButtonFill = new Color(0.16f, 0.15f, 0.17f, 1f);
        private static readonly Color AccentFill = new Color(0.45f, 0.18f, 0.16f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("PauseMenu");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PauseMenu>().Build();
        }

        private static bool InGameScene
        {
            get
            {
                string scene = SceneManager.GetActiveScene().name;
                return scene == "BoardTest" || scene == "CombatTest";
            }
        }

        private bool _modalWasOpen, _inSettings;

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape) || Time.frameCount == _closedFrame || _inSettings) return;
            if (IsOpen)
            {
                if (_confirm.activeSelf) _confirm.SetActive(false);
                else Close();
                return;
            }
            // 다른 창(상점·인벤토리·사건 기록 등)이 열려 있으면 그 창이 Esc로 닫힌다 — 이번 프레임에 닫힌 창도 포함 (지난 프레임 상태)
            if (!InGameScene || _inSettings || _modalWasOpen || BoardHintPanel.AnyModalOpen) return;
            Open();
        }

        private void LateUpdate() => _modalWasOpen = BoardHintPanel.AnyModalOpen;

        // 설정 창은 메뉴를 잠시 가리고 열었다가 닫으면 메뉴로 돌아온다
        private void OpenSettings()
        {
            _inSettings = true;
            _root.SetActive(false);
            SettingsPanel.Open(() => { _inSettings = false; _root.SetActive(true); });
        }

        public static void Open()
        {
            if (_instance == null || IsOpen) return;
            _instance._timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f; // 전투 연출·적 턴 진행을 멈춤
            _instance._confirm.SetActive(false);
            _instance._root.SetActive(true);
        }

        private void Close()
        {
            _root.SetActive(false);
            Time.timeScale = _timeScaleBefore;
            _closedFrame = Time.frameCount;
        }

        private void Ask(string text, System.Action action)
        {
            _confirmText.text = text;
            _confirmAction = action;
            _confirm.SetActive(true);
        }

        private void ToMainMenu()
        {
            Close();
            Time.timeScale = 1f;
            SceneManager.LoadScene(MainMenuController.SceneName);
        }

        private PauseMenu Build()
        {
            var canvasGO = new GameObject("PauseCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60; // 게임 창들 위, 설정(50)보다 위지만 설정을 열 땐 메뉴를 가림
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.6f));
            _root = dim.gameObject;
            var win = UGUIKit.CreateThemedPanel(UGUIKit.Safe16x9(dim.transform), "Window", new Vector2(0.38f, 0.27f), new Vector2(0.62f, 0.73f), Gold, Fill, 2f, UITheme.Current != null ? UITheme.Current.PanelFrame : null);
            var title = UGUIKit.CreateText(win, "Title", new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.96f), "메뉴", 20, TextAnchor.MiddleCenter);
            title.color = Gold;
            title.fontStyle = FontStyle.Bold;

            string[] labels = { "계속", "설정", "메인 메뉴로", "게임 종료" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                float y1 = 0.76f - i * 0.17f;
                var b = UGUIKit.CreateThemedButton(win, $"B{i}", new Vector2(0.12f, y1 - 0.13f), new Vector2(0.88f, y1), labels[i], i == 0 ? AccentFill : ButtonFill, 15);
                b.onClick.AddListener(() =>
                {
                    switch (index)
                    {
                        case 0: Close(); break;
                        case 1: OpenSettings(); break;
                        case 2: Ask("메인 메뉴로 나갈까요?\n이어하기는 마지막 자동 저장 지점부터 시작합니다.", ToMainMenu); break;
                        default: Ask("게임을 종료할까요?\n이어하기는 마지막 자동 저장 지점부터 시작합니다.", Application.Quit); break;
                    }
                });
            }
            var note = UGUIKit.CreateText(win, "Note", new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.08f), "자동 저장 — 따로 저장할 필요 없음", 10, TextAnchor.MiddleCenter);
            note.color = new Color(0.6f, 0.57f, 0.53f);

            // 나가기 확인
            var cdim = UGUIKit.CreateImage(dim.transform, "Confirm", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.4f));
            _confirm = cdim.gameObject;
            var box = UGUIKit.CreateThemedPanel(UGUIKit.Safe16x9(cdim.transform), "Box", new Vector2(0.33f, 0.38f), new Vector2(0.67f, 0.62f), Gold, Fill, 2f);
            _confirmText = UGUIKit.CreateText(box, "Text", new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.92f), "", 14, TextAnchor.MiddleCenter);
            _confirmText.color = new Color(0.92f, 0.9f, 0.86f);
            UGUIKit.CreateThemedButton(box, "No", new Vector2(0.08f, 0.1f), new Vector2(0.46f, 0.34f), "취소", ButtonFill, 14).onClick.AddListener(() => _confirm.SetActive(false));
            UGUIKit.CreateThemedButton(box, "Yes", new Vector2(0.54f, 0.1f), new Vector2(0.92f, 0.34f), "확인", AccentFill, 14).onClick.AddListener(() => _confirmAction?.Invoke());
            _confirm.SetActive(false);

            _root.SetActive(false);
            return this;
        }

        // 자동 플레이 점검용
        public static void DevOpen() => Open();
        public static void DevClose() { if (IsOpen) _instance.Close(); }
        public static void DevToMainMenu() { if (IsOpen) _instance.ToMainMenu(); }
    }
}
