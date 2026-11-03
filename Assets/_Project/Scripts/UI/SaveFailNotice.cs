using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 저장 실패 안내 (1막 게임기획 보고서 11/3 A3): 게임을 멈추지 않는 알림 4초.
    // 상단 바보다 앞에, 문구 전체가 들어가는 넓은 칸. 계속 보이는 표시는 보드 안내 칸의 빨간 "저장 실패" 줄.
    public class SaveFailNotice : MonoBehaviour
    {
        private const float VisibleSeconds = 4f;
        private static SaveFailNotice _instance;
        public static string DevLast;
        public static bool DevVisible => _instance != null && _instance._root.activeSelf;

        private GameObject _root;
        private Text _label;
        private float _hideAt;

        public static void Show(string message)
        {
            DevLast = message;
            if (_instance == null)
            {
                _instance = new GameObject("SaveFailNotice").AddComponent<SaveFailNotice>();
                DontDestroyOnLoad(_instance.gameObject);
                _instance.Build();
            }
            _instance._label.text = message;
            _instance._root.SetActive(true);
            _instance._hideAt = Time.unscaledTime + VisibleSeconds;
        }

        private void Update()
        {
            if (_root != null && _root.activeSelf && Time.unscaledTime >= _hideAt) _root.SetActive(false);
        }

        private void Build()
        {
            var canvasGO = new GameObject("SaveFailNoticeCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 70; // 상단 바·결과 창보다 앞
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            var fill = UGUIKit.CreateThemedPanel(canvasGO.transform, "Notice", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Color(0.88f, 0.4f, 0.35f), new Color(0.16f, 0.07f, 0.07f, 0.95f), 2f);
            var outer = (RectTransform)fill.parent;
            outer.sizeDelta = new Vector2(640f, 46f);
            outer.anchoredPosition = new Vector2(0f, -132f); // 상단 바 바로 아래
            _label = UGUIKit.CreateText(fill, "Label", Vector2.zero, Vector2.one, "", 14, TextAnchor.MiddleCenter);
            _label.color = new Color(0.98f, 0.9f, 0.86f);
            _label.resizeTextForBestFit = true;
            _label.resizeTextMinSize = 10;
            _label.resizeTextMaxSize = 14;
            foreach (var g in canvasGO.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false; // 클릭을 막지 않음
            _root = outer.gameObject;
            _root.SetActive(false);
        }
    }
}
