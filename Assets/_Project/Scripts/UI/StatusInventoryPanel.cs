using Fantasia.Characters;
using Fantasia.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Builds the status + inventory overlay entirely from code (see
    // Docs/Concept/Images/Fantasia_Status_Inventory.png): 캐릭1/2/3 tabs swap
    // which character's portrait/stats are shown, ✕ closes it. No art yet
    // — portraits/icons are flat color placeholders swappable later. Every
    // section is a bordered outer+inner "Fill" pair so regions read as
    // distinct boxes instead of blending into the panel background.
    public class StatusInventoryPanel : MonoBehaviour
    {
        public Camera TargetCamera;
        public CharacterDefinition[] Characters = System.Array.Empty<CharacterDefinition>();
        public ItemDefinition[] InventoryItems = System.Array.Empty<ItemDefinition>();

        // Lazy, not a field initializer — GetBuiltinResource isn't allowed to
        // run during MonoBehaviour construction/deserialization.
        private static Font _uiFont;
        private static Font UIFont => _uiFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private static readonly Vector2 PanelSize = new Vector2(380f, 420f);
        private static readonly Color BorderColor = new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color FillColor = Color.white;
        private static readonly Color SlotFillColor = new Color(0.92f, 0.92f, 0.92f);
        private const int InventorySlotCount = 12;

        private bool _built;
        private GameObject _panelRoot;
        private Image _portraitImage;
        private Text _portraitNameText;
        private Text _statsLabelText;
        private Text _statsValueText;
        private Button[] _tabButtons;
        private int _activeIndex;

        private void Awake()
        {
            BuildIfNeeded();
            Close();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.I)) Toggle();
        }

        public void Open()
        {
            BuildIfNeeded();
            _panelRoot.SetActive(true);
        }

        public void Close()
        {
            BuildIfNeeded();
            _panelRoot.SetActive(false);
        }

        public void Toggle()
        {
            BuildIfNeeded();
            _panelRoot.SetActive(!_panelRoot.activeSelf);
        }

        public void BuildIfNeeded()
        {
            if (_built) return;
            _built = true;
            Build();
        }

        private void Build()
        {
            var canvasGO = new GameObject("StatusInventoryCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = TargetCamera;
            canvas.planeDistance = 2f;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasGO.AddComponent<GraphicRaycaster>();

            var panelFill = CreateBorderedPanel(canvasGO.transform, "Panel", Vector2.zero, Vector2.one, BorderColor, new Color(0.96f, 0.96f, 0.96f), 2f);
            var panelOuterRect = (RectTransform)panelFill.transform.parent;
            panelOuterRect.anchorMin = panelOuterRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelOuterRect.sizeDelta = PanelSize;
            panelOuterRect.anchoredPosition = Vector2.zero;
            _panelRoot = panelOuterRect.gameObject;

            BuildTabBar(panelFill);
            BuildUpperSection(panelFill);
            BuildInventory(panelFill);

            if (Characters.Length > 0) SetActiveCharacter(0);
        }

        private void BuildTabBar(Transform parent)
        {
            var barFill = CreateBorderedPanel(parent, "TabBar", new Vector2(0f, 0.92f), new Vector2(1f, 1f), BorderColor, FillColor, 2f);

            _tabButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                float x0 = i * 0.2f + 0.01f;
                var btn = CreateButton(barFill, $"CharTab{i + 1}", new Vector2(x0, 0.08f), new Vector2(x0 + 0.18f, 0.92f),
                    $"캐릭{i + 1}", new Color(0.9f, 0.9f, 0.9f), 10);
                int captured = i;
                btn.onClick.AddListener(() => SetActiveCharacter(captured));
                _tabButtons[i] = btn;
            }

            // Small square close button pinned to the top-right corner, not a
            // tab-width button — matches a conventional 창 닫기 X.
            var closeBtn = CreateButton(barFill, "CloseButton", new Vector2(0.9f, 0.08f), new Vector2(0.99f, 0.92f),
                "✕", new Color(0.9f, 0.75f, 0.75f), 12);
            closeBtn.onClick.AddListener(Close);
        }

        private void BuildUpperSection(Transform parent)
        {
            var upper = CreateRect(parent, "UpperSection", new Vector2(0f, 0.5f), new Vector2(1f, 0.9f));

            var portraitFill = CreateBorderedPanel(upper, "PortraitPanel", new Vector2(0f, 0f), new Vector2(0.65f, 1f), BorderColor, FillColor, 2f);
            _portraitImage = CreateImage(portraitFill, "Portrait", new Vector2(0.1f, 0.12f), new Vector2(0.9f, 0.85f), Color.gray);
            _portraitNameText = CreateText(portraitFill, "Name", new Vector2(0f, 0f), new Vector2(1f, 0.1f), "", 13, TextAnchor.MiddleCenter);

            var statusFill = CreateBorderedPanel(upper, "StatusPanel", new Vector2(0.68f, 0f), new Vector2(1f, 1f), BorderColor, FillColor, 2f);
            // Two parallel columns (labels / values) instead of one string —
            // keeps numbers lined up regardless of how long each label is.
            _statsLabelText = CreateText(statusFill, "StatLabels", new Vector2(0.08f, 0.05f), new Vector2(0.62f, 0.95f), "", 11, TextAnchor.UpperLeft);
            _statsValueText = CreateText(statusFill, "StatValues", new Vector2(0.62f, 0.05f), new Vector2(0.95f, 0.95f), "", 11, TextAnchor.UpperLeft);
        }

        private void BuildInventory(Transform parent)
        {
            var invFill = CreateBorderedPanel(parent, "InventoryPanel", new Vector2(0f, 0f), new Vector2(1f, 0.48f), BorderColor, FillColor, 2f);

            var gridRect = CreateRect(invFill, "Grid", new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.94f));
            var layout = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(52f, 52f);
            layout.spacing = new Vector2(6f, 6f);

            for (int i = 0; i < InventorySlotCount; i++)
            {
                var slotFill = CreateBorderedPanel(gridRect, $"Slot{i}", Vector2.zero, Vector2.one, BorderColor, SlotFillColor, 2f);

                if (i < InventoryItems.Length && InventoryItems[i] != null)
                {
                    var item = InventoryItems[i];
                    CreateImage(slotFill, "Icon", new Vector2(0.15f, 0.28f), new Vector2(0.85f, 1f), item.IconTint);
                    CreateText(slotFill, "Label", new Vector2(0f, 0f), new Vector2(1f, 0.28f), item.ItemName, 7, TextAnchor.LowerCenter);
                }
            }
        }

        private void SetActiveCharacter(int index)
        {
            if (Characters.Length == 0) return;
            _activeIndex = Mathf.Clamp(index, 0, Characters.Length - 1);
            var c = Characters[_activeIndex];

            _portraitImage.color = c.PortraitTint;
            _portraitNameText.text = c.CharacterName;
            _statsLabelText.text = "HP\n물리 공격력\n마력\n물리 방어력\n마법 방어력\n속도";
            _statsValueText.text = $"{c.MaxHP}\n{c.PhysicalAttack}\n{c.MagicAttack}\n{c.PhysicalDefense}\n{c.MagicDefense}\n{c.Speed}";

            for (int i = 0; i < _tabButtons.Length; i++)
            {
                _tabButtons[i].image.color = i == _activeIndex ? new Color(0.75f, 0.85f, 1f) : new Color(0.9f, 0.9f, 0.9f);
            }
        }

        // --- code-only UI building blocks ---

        private static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
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

        private static Image CreateImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var rt = CreateRect(parent, name, anchorMin, anchorMax);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        // Outer image (border color, full anchor rect) + inset inner "Fill"
        // image (content color) — the standard no-sprite way to get a visible
        // border in legacy uGUI. Returns the inner Fill transform to parent
        // content under.
        private static Transform CreateBorderedPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color borderColor, Color fillColor, float insetPx)
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

        private static Text CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string content, int fontSize, TextAnchor alignment)
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

        private static Button CreateButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label, Color color, int fontSize)
        {
            var fill = CreateBorderedPanel(parent, name, anchorMin, anchorMax, BorderColor, color, 1.5f);
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
