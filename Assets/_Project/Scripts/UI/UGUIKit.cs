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
        // 게임 글꼴: UITheme에 지정돼 있으면 그것 → 없으면 Resources/Fonts의 ZEN SERIF(팀 지정 글꼴) → 없으면 기본 글꼴.
        // ZEN SERIF 파일은 라이선스상 재배포 금지라 저장소에 없음 — 팀원은 Assets/_Project/Resources/Fonts/에 직접 넣는다.
        public const string GameFontResource = "Fonts/ZEN-SERIF-TTF-Regular";
        private static Font _uiFont;
        public static Font UIFont => _uiFont ??= UITheme.Current.Font != null ? UITheme.Current.Font
            : Resources.Load<Font>(GameFontResource) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 해상도 공통 규칙 (배포용): UI는 16:9 기준 크기로 그리고 화면에 맞춰 비율대로 키운다.
        // Expand — 가로·세로 중 작은 쪽에 맞춰 키움 → 21:9·16:10에서도 글자·창이 화면 밖으로 넘치지 않음.
        public static readonly Vector2 ReferenceResolution = new Vector2(1280, 720);

        public static void ConfigureScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }

        // 화면 가운데 16:9 영역 — 창·메뉴처럼 가운데 뜨는 것은 여기에 붙여 울트라와이드에서 옆으로 늘어나지 않게 한다.
        // 상단 바·파티 카드처럼 가장자리에 붙는 HUD는 쓰지 않는다 (화면 끝에 붙어야 함).
        public static Transform Safe16x9(Transform fullScreenParent)
        {
            var existing = fullScreenParent.Find("Safe16x9");
            if (existing != null) return existing;
            var rt = CreateRect(fullScreenParent, "Safe16x9", Vector2.zero, Vector2.one);
            var fitter = rt.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;
            return rt;
        }

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

        // 아이콘 그림 (UITheme 아이콘 칸). 그림이 없으면 만들지 않고 null — 호출부는 글자만 보이게 둔다.
        public static Image CreateIcon(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Sprite sprite)
        {
            if (sprite == null) return null;
            var img = CreateImage(parent, name, anchorMin, anchorMax, Color.white);
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
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
