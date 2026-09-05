using Fantasia.Characters;
using Fantasia.Core;
using Fantasia.Items;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Builds the status + inventory overlay entirely from code (see
    // Docs/Concept_Image/Images/Fantasia_Status_Inventory.png): 캐릭1/2/3 tabs swap
    // which character's portrait/stats are shown, ✕ closes it. No art yet
    // — portraits/icons are flat color placeholders swappable later. Every
    // section is a bordered outer+inner "Fill" pair so regions read as
    // distinct boxes instead of blending into the panel background.
    public class StatusInventoryPanel : MonoBehaviour
    {
        public Camera TargetCamera;
        public CharacterDefinition[] Characters = System.Array.Empty<CharacterDefinition>();

        // Only used when there's no live BoardSession (e.g. the headless
        // editor screenshot capture) — otherwise the grid reads/writes
        // BoardSession.Instance.Inventory so items picked up in combat (or
        // anywhere else that calls BoardSession.AddItem) actually show up here.
        public ItemDefinition[] InventoryItems = System.Array.Empty<ItemDefinition>();

        // Lazy, not a field initializer — GetBuiltinResource isn't allowed to
        // run during MonoBehaviour construction/deserialization.
        private static Font _uiFont;
        private static Font UIFont => _uiFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private static readonly Vector2 PanelSize = new Vector2(380f, 420f);
        private static readonly Color BorderColor = new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color FillColor = Color.white;
        private static readonly Color SlotFillColor = new Color(0.92f, 0.92f, 0.92f);

        private bool _built;
        private GameObject _panelRoot;
        private Image _portraitImage;
        private Text _portraitNameText;
        private Text _statsLabelText;
        private Text _statsValueText;
        private Button[] _tabButtons;
        private int _activeIndex;

        private Image[] _slotIcons;
        private Text[] _slotLabels;

        private RectTransform _dragGhost;
        private Image _dragGhostImage;
        private int _dragSourceIndex = -1;

        private GameObject _discardDialog;
        private Text _discardDialogLabel;
        private int _pendingDiscardIndex = -1;

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
            RefreshInventory();
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
            bool opening = !_panelRoot.activeSelf;
            if (opening) RefreshInventory();
            _panelRoot.SetActive(opening);
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
            BuildDiscardDialog(canvasGO.transform);
            BuildDragGhost(canvasGO.transform);

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

            int slotCount = BoardSession.InventoryCapacity;
            _slotIcons = new Image[slotCount];
            _slotLabels = new Text[slotCount];

            for (int i = 0; i < slotCount; i++)
            {
                var slotFill = CreateBorderedPanel(gridRect, $"Slot{i}", Vector2.zero, Vector2.one, BorderColor, SlotFillColor, 2f);

                _slotIcons[i] = CreateImage(slotFill, "Icon", new Vector2(0.15f, 0.28f), new Vector2(0.85f, 1f), Color.clear);
                _slotLabels[i] = CreateText(slotFill, "Label", new Vector2(0f, 0f), new Vector2(1f, 0.28f), "", 7, TextAnchor.LowerCenter);

                // Drag/drop, double-click and right-click all live on the
                // outer slot object — it already has a raycastable Image
                // (the border) from CreateBorderedPanel, so no Button needed.
                var slotView = slotFill.parent.gameObject.AddComponent<InventorySlotView>();
                slotView.Panel = this;
                slotView.Index = i;
            }

            RefreshInventory();
        }

        private void BuildDiscardDialog(Transform canvasRoot)
        {
            var dialogFill = CreateBorderedPanel(canvasRoot, "DiscardDialog", Vector2.zero, Vector2.one, BorderColor, new Color(0.98f, 0.98f, 0.9f), 2f);
            var dialogOuter = (RectTransform)dialogFill.transform.parent;
            dialogOuter.anchorMin = dialogOuter.anchorMax = new Vector2(0.5f, 0.5f);
            dialogOuter.sizeDelta = new Vector2(220f, 100f);
            dialogOuter.anchoredPosition = Vector2.zero;
            _discardDialog = dialogOuter.gameObject;

            _discardDialogLabel = CreateText(dialogFill, "Message", new Vector2(0.05f, 0.4f), new Vector2(0.95f, 0.95f), "", 11, TextAnchor.MiddleCenter);

            var yesBtn = CreateButton(dialogFill, "Yes", new Vector2(0.1f, 0.08f), new Vector2(0.48f, 0.35f), "예", new Color(0.85f, 0.9f, 0.85f), 11);
            yesBtn.onClick.AddListener(ConfirmDiscard);

            var noBtn = CreateButton(dialogFill, "No", new Vector2(0.52f, 0.08f), new Vector2(0.9f, 0.35f), "아니오", new Color(0.9f, 0.85f, 0.85f), 11);
            noBtn.onClick.AddListener(CancelDiscard);

            _discardDialog.SetActive(false);
        }

        private void BuildDragGhost(Transform canvasRoot)
        {
            var go = new GameObject("DragGhost", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(40f, 40f);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false; // must not steal the drop raycast from the slot underneath
            go.SetActive(false);

            _dragGhost = rt;
            _dragGhostImage = img;
        }

        // BoardSession.Instance.Inventory is the live inventory once the game
        // is running; InventoryItems is only a fallback for contexts with no
        // session (e.g. CreateSceneAndCaptureUI's headless screenshot) — that
        // fallback is display-only, drag/discard/use/equip all no-op on it.
        private ItemDefinition[] CurrentInventory()
        {
            if (BoardSession.Instance != null) return BoardSession.Instance.Inventory;

            var fallback = new ItemDefinition[BoardSession.InventoryCapacity];
            for (int i = 0; i < InventoryItems.Length && i < fallback.Length; i++) fallback[i] = InventoryItems[i];
            return fallback;
        }

        private void RefreshInventory()
        {
            if (_slotIcons == null) return; // not built yet

            var items = CurrentInventory();
            for (int i = 0; i < _slotIcons.Length; i++)
            {
                var item = i < items.Length ? items[i] : null;
                _slotIcons[i].color = item != null ? item.IconTint : Color.clear;
                _slotLabels[i].text = item != null ? item.ItemName : "";
            }
        }

        public void BeginDragSlot(int index, PointerEventData eventData)
        {
            var items = CurrentInventory();
            if (index >= items.Length || items[index] == null)
            {
                _dragSourceIndex = -1;
                return;
            }

            _dragSourceIndex = index;
            _dragGhostImage.color = items[index].IconTint;
            _dragGhost.gameObject.SetActive(true);
            UpdateGhostPosition(eventData);
        }

        public void DragSlot(PointerEventData eventData)
        {
            if (_dragSourceIndex < 0) return;
            UpdateGhostPosition(eventData);
        }

        // Dropped on a slot (filled or empty) -> swap. Dropped inside the
        // status/inventory window but not on a slot (a gap, the portrait
        // panel, the tab bar, ...) -> snap back to where it was, i.e. do
        // nothing (the data was never touched during the drag). Only a drop
        // outside the whole window -> ask before discarding.
        public void EndDragSlot(PointerEventData eventData)
        {
            _dragGhost.gameObject.SetActive(false);
            if (_dragSourceIndex < 0) return;

            int sourceIndex = _dragSourceIndex;
            _dragSourceIndex = -1;

            var hit = eventData.pointerCurrentRaycast.gameObject;
            var targetSlot = hit != null ? hit.GetComponentInParent<InventorySlotView>() : null;

            if (targetSlot != null)
            {
                SwapSlots(sourceIndex, targetSlot.Index);
                return;
            }

            var panelRect = (RectTransform)_panelRoot.transform;
            bool droppedInsideWindow = RectTransformUtility.RectangleContainsScreenPoint(panelRect, eventData.position, eventData.pressEventCamera);
            if (!droppedInsideWindow)
            {
                RequestDiscard(sourceIndex);
            }
        }

        private void UpdateGhostPosition(PointerEventData eventData)
        {
            var canvasRect = (RectTransform)_dragGhost.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, eventData.pressEventCamera, out var localPoint);
            _dragGhost.anchoredPosition = localPoint;
        }

        private void SwapSlots(int a, int b)
        {
            if (BoardSession.Instance == null) return;
            BoardSession.Instance.SwapItems(a, b);
            RefreshInventory();
        }

        private void RequestDiscard(int index)
        {
            var items = CurrentInventory();
            if (index >= items.Length || items[index] == null) return;

            _pendingDiscardIndex = index;
            _discardDialogLabel.text = $"{items[index].ItemName}\n아이템을 버리시겠습니까?";
            _discardDialog.SetActive(true);
        }

        private void ConfirmDiscard()
        {
            if (_pendingDiscardIndex >= 0 && BoardSession.Instance != null)
            {
                BoardSession.Instance.RemoveItemAt(_pendingDiscardIndex);
            }
            _pendingDiscardIndex = -1;
            _discardDialog.SetActive(false);
            RefreshInventory();
        }

        private void CancelDiscard()
        {
            _pendingDiscardIndex = -1;
            _discardDialog.SetActive(false);
        }

        public void UseSlot(int index)
        {
            var items = CurrentInventory();
            if (index >= items.Length || items[index] == null) return;

            // Actual use-effects (heal, feed, ...) depend on the stat/combat
            // connection GDD 6.3/6.7 hasn't settled yet — this just proves
            // the double-click hook works.
            Debug.Log($"[Inventory] 사용: {items[index].ItemName}");
        }

        public void TryEquipSlot(int index)
        {
            var items = CurrentInventory();
            if (index >= items.Length || items[index] == null) return;

            var item = items[index];
            if (item.Category != ItemCategory.Equipment) return;

            // Actual equip slots/stat application depend on the character/
            // equipment system GDD 6.3 hasn't settled yet — this just proves
            // the right-click hook works.
            Debug.Log($"[Inventory] 장착: {item.ItemName}");
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
