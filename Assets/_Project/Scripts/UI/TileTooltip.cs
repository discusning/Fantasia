using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 지도 칸 위에 마우스를 올리면 뜨는 작은 설명 (UI 솔루션 Q7 — 적 칸 구성 "역병 쥐 ×2 · 가고일 유체 ×1 / 총 3마리").
    // 마우스 옆에 붙어 다니고, 다른 창이 열려 있거나 칸에서 벗어나면 숨는다.
    public class TileTooltip : MonoBehaviour
    {
        private static TileTooltip _instance;
        private RectTransform _box, _canvasRect;
        private Text _text;

        public static void Show(string text, Vector2 screenPos)
        {
            if (_instance == null) _instance = new GameObject("TileTooltip").AddComponent<TileTooltip>().Build();
            _instance.Place(text, screenPos);
        }

        public static void Hide()
        {
            if (_instance != null && _instance._box.gameObject.activeSelf) _instance._box.gameObject.SetActive(false);
        }

        public static bool IsShowing => _instance != null && _instance._box.gameObject.activeSelf;
        public static string Text => _instance != null ? _instance._text.text : null;

        private void Place(string text, Vector2 screenPos)
        {
            _box.gameObject.SetActive(true);
            _text.text = text;
            var size = new Vector2(Mathf.Min(_text.preferredWidth, 360f) + 20f, _text.preferredHeight + 14f);
            _box.sizeDelta = size;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out var local);
            // 마우스 오른쪽 아래, 화면 밖으로 나가면 반대쪽
            var half = _canvasRect.rect.size * 0.5f;
            float x = local.x + 18f, y = local.y - 18f;
            if (x + size.x > half.x) x = local.x - 18f - size.x;
            if (y - size.y < -half.y) y = local.y + 18f + size.y;
            _box.anchoredPosition = new Vector2(x, y);
        }

        private TileTooltip Build()
        {
            var canvasGO = new GameObject("TooltipCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            UGUIKit.ConfigureScaler(canvasGO.AddComponent<CanvasScaler>());
            _canvasRect = (RectTransform)canvasGO.transform;

            var fill = UGUIKit.CreateThemedPanel(canvasGO.transform, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Color(0.72f, 0.6f, 0.38f), new Color(0.08f, 0.07f, 0.09f, 0.95f), 1.5f, UITheme.Current.PanelFrame);
            _box = (RectTransform)fill.parent;
            _box.pivot = new Vector2(0f, 1f);
            foreach (var g in _box.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            _text = UGUIKit.CreateText(fill, "Text", Vector2.zero, Vector2.one, "", 13, TextAnchor.MiddleLeft);
            _text.color = new Color(0.94f, 0.9f, 0.84f);
            _text.raycastTarget = false;
            _text.supportRichText = true;
            var rt = _text.rectTransform;
            rt.offsetMin = new Vector2(8f, 4f);
            rt.offsetMax = new Vector2(-8f, -4f);
            _box.gameObject.SetActive(false);
            return this;
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
