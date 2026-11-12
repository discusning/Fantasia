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
            // 11/10 받은 창 틀(Panel2_Window): 떠 있는 창(아래 이름)만 장식 틀을 창 바깥으로 둘러 씌움 — 안쪽 글 자리는 그대로.
            // 파티 카드·순서 칸·행동 막대 등 붙박이 판은 장식이 겹쳐서 예전 단색 테두리 유지. PanelFrame을 바탕 그림으로 넘긴 호출도 무시.
            if (fillSprite == theme.PanelFrame) fillSprite = null;
            bool window = theme.PanelFrame != null && WindowFrameNames.Contains(name);
            if (window) AddWindowFrame(fill, theme.PanelFrame);
            if (fillSprite == null && !window && theme.PanelFill != null) ApplyInnerPlate(fill, fillColor); // 11/11 U3: 안쪽 판 그림 (받은 N09)
            else UITheme.Apply(fill.GetComponent<Image>(), fillSprite);
            return fill;
        }

        // 11/11 UI 요청 U3 "안쪽 판" — 디자인 답: 받은 그림을 다시 씀 → N09 공통메뉴 내용패널(여백 자름, HUD/Cut/InnerPlate)을 9칸 나누기로 깔고,
        // 예전 "검은 판 + 노란 선"(바깥 단색 테두리)은 숨김. 예전 바탕색은 살짝 섞어 남김 (보스 예고 = 붉은 기 등 구분 유지).
        public const float PlateScale = 6f; // 그림 테두리 약 40px → 화면 약 7
        public static readonly Color PlateNormal = new Color(0.8f, 0.8f, 0.8f, 1f);
        public static void ApplyInnerPlate(Transform fill, Color fillColor)
        {
            var sprite = UITheme.Current != null ? UITheme.Current.PanelFill : null;
            if (sprite == null) return;
            var outer = fill.parent != null ? fill.parent.GetComponent<Image>() : null;
            if (outer != null) outer.color = new Color(0f, 0f, 0f, 0f);
            var rect = (RectTransform)fill;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = fill.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = PlateScale;
            var c = new Color(Mathf.Min(1f, fillColor.r * 3f), Mathf.Min(1f, fillColor.g * 3f), Mathf.Min(1f, fillColor.b * 3f));
            var tint = Color.Lerp(Color.white, c, 0.35f);
            image.color = new Color(tint.r, tint.g, tint.b, fillColor.a);
        }

        // 판 강조 (파티 카드 지금 차례 · 적 카드 대상 표시) — 바깥 단색 테두리 대신 판 밝기·색으로
        public static bool SetPlateTint(Transform fill, Color color)
        {
            var image = fill != null ? fill.GetComponent<Image>() : null;
            if (image == null || UITheme.Current == null || UITheme.Current.PanelFill == null || image.sprite != UITheme.Current.PanelFill) return false;
            image.color = new Color(color.r, color.g, color.b, image.color.a);
            return true;
        }

        // 11/11 UI 요청 U4 "얼굴 액자" — 받은 순서 띠 청동 칸 틀(가운데 뚫림)을 얼굴 위에 덮음. 틀은 얼굴보다 grow(화면 단위)만큼 큼
        public static Image AddPortraitFrame(RectTransform face, float grow = 4f)
        {
            var art = UITheme.Current != null ? UITheme.Current.TimelineSlot : null;
            if (art == null || face == null) return null;
            var img = CreateImage(face, "FaceFrame", Vector2.zero, Vector2.one, Color.white);
            img.rectTransform.offsetMin = new Vector2(-grow, -grow); // 얼굴보다 grow만큼 바깥으로
            img.rectTransform.offsetMax = new Vector2(grow, grow);
            img.type = Image.Type.Sliced; // 9칸 나누기 (테두리 24px) — 3:4 인물 칸에도 찌그러지지 않게
            img.pixelsPerUnitMultiplier = 2.4f;
            img.sprite = art;
            img.raycastTarget = false;
            img.transform.SetAsLastSibling();
            return img;
        }

        // 11/11 UI 요청 U6 "제목 띠" — 받은 나무 탭(TopBar_RegionTab, 테마 Banner)을 띠로. fill = CreateBorderedPanel/CreateThemedPanel 안쪽
        public static bool ApplyBand(Transform fill, float heightUnits = 30f)
        {
            var sprite = UITheme.Current != null ? UITheme.Current.Banner : null;
            if (sprite == null || fill == null) return false;
            var outer = fill.parent != null ? fill.parent.GetComponent<Image>() : null;
            if (outer != null) outer.color = new Color(0f, 0f, 0f, 0f);
            var rect = (RectTransform)fill;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = fill.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = sprite.rect.height / Mathf.Max(8f, heightUnits);
            image.color = Color.white;
            return true;
        }

        // 11/11 UI 요청 U6 적 크기 표식 · U10 주사위 칸 표식 — 받은 청동 원(N22, 테마 LevelBadge). 마름모 회전은 풂.
        // 색(성공·실패·아직)은 원에 곱해서 남김 — 호출한 쪽이 fill Image 색을 바꾸면 그대로 반영됨.
        public static bool ApplyRing(Transform fill, bool tintable = false)
        {
            var sprite = UITheme.Current == null ? null : tintable && UITheme.Current.SlotIcon != null ? UITheme.Current.SlotIcon : UITheme.Current.LevelBadge; // tintable = 가운데 밝은 칠용 원 (성공·실패 색이 잘 보이게)
            if (sprite == null || fill == null) return false;
            var outerRect = (RectTransform)fill.parent;
            var outer = outerRect.GetComponent<Image>();
            if (outer != null) outer.color = new Color(0f, 0f, 0f, 0f);
            outerRect.localRotation = Quaternion.identity;
            var rect = (RectTransform)fill;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = fill.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            return true;
        }

        // 창 틀 그림의 안쪽 나무 띠 두께(원본 px: 왼 140 · 아래 194 · 오른 136 · 위 220) ÷ WindowFrameScale 만큼 창 밖으로 넓혀 그림.
        private const float WindowFrameScale = 5f;
        private static readonly System.Collections.Generic.HashSet<string> WindowFrameNames = new() { "Window", "Box", "NameBox", "ResultCard", "RollPanel" };
        private static void AddWindowFrame(Transform fill, Sprite frame)
        {
            var outer = fill.parent.GetComponent<Image>();
            outer.color = new Color(0f, 0f, 0f, 0f); // 클릭 막기는 그대로, 단색 테두리만 숨김
            var img = CreateImage(outer.transform, "Frame", Vector2.zero, Vector2.one, Color.white);
            img.transform.SetAsFirstSibling();
            img.raycastTarget = false;
            img.sprite = frame;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = WindowFrameScale;
            img.rectTransform.offsetMin = new Vector2(-140f, -194f) / WindowFrameScale;
            img.rectTransform.offsetMax = new Vector2(136f, 220f) / WindowFrameScale;
            fill.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f); // 틀 안쪽 갈색 바탕이 보이게 살짝만 어둡게
            var header = UITheme.Current.WindowHeader;
            var rt = (RectTransform)outer.transform;
            // 위 장식 + 이름 칸: 큰 창(Window)만, 위에 공간이 있을 때만 (상점·인벤토리는 화면 위 끝에 붙어 장식이 잘림)
            if (header != null && outer.name == "Window" && rt.anchorMax.y <= 0.9f)
            {
                var h = CreateImage(outer.transform, "Header", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Color.white);
                h.sprite = header;
                h.raycastTarget = false;
                h.rectTransform.pivot = new Vector2(0.5f, 0f);
                h.rectTransform.sizeDelta = new Vector2(275f, 109f);       // 그림 860×341 × 0.32
                h.rectTransform.anchoredPosition = new Vector2(0f, -30f);  // 이름 칸이 창 안쪽 위 끝에 걸침
                var plate = CreateRect(h.transform, "Plate", new Vector2(0.09f, 0.07f), new Vector2(0.91f, 0.27f));
                outer.gameObject.AddComponent<WindowTitleToHeader>().Init(fill, plate);
            }
        }

        // 테마 버튼 그림 + (있으면) 아이콘. 아이콘이 있으면 글자는 아래쪽 작은 캡션이 된다.
        public static Button CreateThemedButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label, Color color, int fontSize, Sprite icon = null)
        {
            var button = CreateButton(parent, name, anchorMin, anchorMax, label, color, fontSize);
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
            btn.onClick.AddListener(Fantasia.Core.SfxPlayer.Click); // 버튼 클릭 소리 (파일 없으면 무음)
            var labelText = CreateText(fill, "Label", Vector2.zero, Vector2.one, label, fontSize, TextAnchor.MiddleCenter);

            // Explicit targetGraphic so Unity's built-in Selectable transition
            // actually tints something on hover/press — without this a Button
            // added purely from script gives no pressed-state feedback at all.
            btn.targetGraphic = fill.GetComponent<Image>();
            ApplyButtonArt(btn, labelText);
            return btn;
        }

        // 11/10 받은 사각 버튼 판(Button2_Plain): 9칸 나누기로 늘림. 버튼 색은 반쯤 섞어 남기고(구분용), 글자는 밝게.
        public static readonly Color ButtonLabelLight = new Color(0.96f, 0.9f, 0.76f);
        private static void ApplyButtonArt(Button btn, Text label)
        {
            var sprite = UITheme.Current.Button;
            if (sprite == null) return;
            var fillImage = (Image)btn.targetGraphic;
            fillImage.transform.parent.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var fillRect = (RectTransform)fillImage.transform; // 테두리 1.5px 안쪽 → 꽉 채움
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            fillImage.sprite = sprite;
            fillImage.type = Image.Type.Sliced;
            fillImage.pixelsPerUnitMultiplier = 3f; // 그림 테두리 24px → 8px
            fillImage.color = Color.Lerp(fillImage.color, Color.white, 0.5f);
            label.color = ButtonLabelLight;
        }

        // 작은 상자·안쪽 판용 얇은 판 (11/11 UI 점검 — 디자인 U3·U8 그림이 오기 전 임시).
        // 받은 사각 버튼 그림(얇은 구리 테두리)을 어둡게 깔아 씀. fill = CreateThemedPanel/CreateBorderedPanel이 돌려준 안쪽.
        public static void ApplyThinPlate(Transform fill, float brightness = 0.55f)
        {
            var sprite = UITheme.Current != null ? UITheme.Current.Button : null;
            if (sprite == null) return;
            var outer = fill.parent != null ? fill.parent.GetComponent<Image>() : null;
            if (outer != null) outer.color = new Color(0f, 0f, 0f, 0f);
            var rect = (RectTransform)fill;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = fill.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            image.color = new Color(brightness, brightness, brightness, 0.96f);
        }

        // 11/11 U8 작은 상자: 위치·크기는 그대로 두고 그림만 얇은 판으로 (CreateImage로 만든 단독 상자용 — 도움말·의뢰 상자 등)
        // 11/11 U9 목록 한 줄: 얇은 판 위 보통 줄 · 고른 줄 색 (곱함)
        public static readonly Color RowNormal = new Color(0.5f, 0.48f, 0.47f, 1f);
        public static readonly Color RowPicked = new Color(1f, 0.84f, 0.52f, 1f);
        // 안쪽 판(U3) 카드의 보통·고른 색 (점술 카드·기도 칸 — 요청서 "고른 상태는 테두리가 더 밝은 것")
        public static Color PlateTint(bool selected, Color oldNormal, Color oldSelected) =>
            UITheme.Current != null && UITheme.Current.PanelFill != null ? (selected ? RowPicked : PlateNormal) : (selected ? oldSelected : oldNormal);
        public static Color RowTint(bool selected, Color oldNormal, Color oldSelected) =>
            UITheme.Current != null && UITheme.Current.Button != null ? (selected ? RowPicked : RowNormal) : (selected ? oldSelected : oldNormal);

        public static bool SkinThinPlate(Image image, float brightness = 0.55f, float alpha = 0.96f)
        {
            var sprite = UITheme.Current != null ? UITheme.Current.Button : null;
            if (sprite == null || image == null) return false;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            image.color = new Color(brightness, brightness, brightness, alpha);
            return true;
        }
    }
}

namespace Fantasia.UI
{
    // 창을 만든 코드가 "Title" 글자를 만든 뒤(같은 프레임) 첫 Start에서 그 글자를 위 장식의 이름 칸으로 옮긴다.
    // 각 창 코드는 그대로 두고, 제목 글(상점 이름 등 바뀌는 글 포함)은 같은 Text를 계속 쓴다.
    public class WindowTitleToHeader : MonoBehaviour
    {
        private Transform _fill;
        private RectTransform _plate;
        public void Init(Transform fill, RectTransform plate) { _fill = fill; _plate = plate; }

        private void Start()
        {
            if (_fill == null || _plate == null) return;
            var title = _fill.Find("Title")?.GetComponent<Text>();
            if (title == null) { _plate.parent.gameObject.SetActive(false); return; } // 제목 없는 창은 장식도 숨김
            var r = title.rectTransform;
            r.SetParent(_plate, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            title.alignment = TextAnchor.MiddleCenter;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 10;
            title.resizeTextMaxSize = Mathf.Min(title.fontSize, 17);
            title.color = new Color(0.98f, 0.86f, 0.55f);
        }
    }
}
