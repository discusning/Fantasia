using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Shared code-only uGUI construction helpers for panels that build their
    // UI at runtime instead of from a prefab (no art/prefabs yet). Extracted
    // out of StatusInventoryPanel once ItemAcquiredToast needed the same
    // handful of primitives.
    public static class UGUIKit
    {
        public static readonly Color DefaultBorderColor = new Color(0.5f, 0.5f, 0.5f);

        // Lazy, not a field initializer — GetBuiltinResource isn't allowed to
        // run during MonoBehaviour construction/deserialization.
        // UITheme에 글꼴이 지정돼 있으면 그걸 쓴다 (한글 글꼴 교체 지점).
        private static Font _uiFont;
        public static Font UIFont => _uiFont ??= UITheme.Current.Font != null
            ? UITheme.Current.Font
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image CreateImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var rt = CreateRect(parent, name, anchorMin, anchorMax);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        // Outer image (border color, full anchor rect) + inset inner "Fill"
        // image (content color) — the standard no-sprite way to get a visible
        // border in legacy uGUI. Returns the inner Fill transform to parent
        // content under. Callers that need to resize/reposition the outer
        // (e.g. anchoring it to a point and setting sizeDelta) can still do
        // so afterward via fill.transform.parent — the inner Fill is anchor-
        // stretched, so it follows whatever the outer's rect ends up being.
        public static Transform CreateBorderedPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color borderColor, Color fillColor, float insetPx)
        {
            var outer = CreateImage(parent, name, anchorMin, anchorMax, borderColor);

            var innerGO = new GameObject("Fill", typeof(RectTransform));
            var innerRect = (RectTransform)innerGO.transform;
            innerRect.SetParent(outer.transform, false);
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(insetPx, insetPx);
            innerRect.offsetMax = new Vector2(-insetPx, -insetPx);
            innerGO.AddComponent<Image>().color = fillColor;

            return innerRect;
        }

        // CreateBorderedPanel + UITheme 스프라이트. 테마에 틀/바탕 그림이 있으면 입히고(틀은 색을 곱해
        // 아군·적·강조 구분을 유지), 없으면 단색 그대로. 새 UI는 이걸 써서 아트 교체가 되게 한다.
        public static Transform CreateThemedPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color borderColor, Color fillColor, float insetPx, Sprite fillSprite = null)
        {
            var fill = CreateBorderedPanel(parent, name, anchorMin, anchorMax, borderColor, fillColor, insetPx);
            var theme = UITheme.Current;
            UITheme.Apply(fill.parent.GetComponent<Image>(), theme.PanelFrame, tint: true);
            UITheme.Apply(fill.GetComponent<Image>(), fillSprite != null ? fillSprite : theme.PanelFill);
            return fill;
        }

        // 테마 버튼 그림 + (있으면) 아이콘. 아이콘이 있으면 글자는 아래쪽 작은 캡션이 된다.
        public static Button CreateThemedButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label, Color color, int fontSize, Sprite icon = null)
        {
            var button = CreateButton(parent, name, anchorMin, anchorMax, label, color, fontSize);
            UITheme.Apply((Image)button.targetGraphic, UITheme.Current.Button);
            if (icon != null)
            {
                var fill = button.targetGraphic.transform;
                var iconImage = CreateImage(fill, "Icon", new Vector2(0.2f, 0.32f), new Vector2(0.8f, 0.95f), Color.white);
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                var labelRect = (RectTransform)fill.Find("Label");
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = new Vector2(1f, 0.32f);
            }
            return button;
        }

        public static Text CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string content, int fontSize, TextAnchor alignment)
        {
            var rt = CreateRect(parent, name, anchorMin, anchorMax);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = UIFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.black;
            text.text = content;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label, Color color, int fontSize)
        {
            var fill = CreateBorderedPanel(parent, name, anchorMin, anchorMax, DefaultBorderColor, color, 1.5f);
            var btn = fill.parent.gameObject.AddComponent<Button>();
            var labelText = CreateText(fill, "Label", Vector2.zero, Vector2.one, label, fontSize, TextAnchor.MiddleCenter);

            // Explicit targetGraphic so Unity's built-in Selectable transition
            // actually tints something on hover/press — without this a Button
            // added purely from script gives no pressed-state feedback at all.
            btn.targetGraphic = fill.GetComponent<Image>();
            _ = labelText; // label is purely visual, no reference needed after creation
            return btn;
        }
    }
}
