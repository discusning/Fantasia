using Fantasia.Core;
using Fantasia.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // Plain "X 획득" banner, no animation beyond show/auto-hide — deliberately
    // undramatic per design direction (see Docs/Concept_Image/Concept/
    // combat승리_아이템 획득.png for the "make the player feel it" reference,
    // kept simple rather than matching that screen's density).
    //
    // Subscribes to BoardSession.ItemAdded, which fires for every source
    // (combat loot, future events, camp, ...) — this needs no per-source
    // wiring. Screen Space - Overlay (not Camera) because this object
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

            BoardSession.EnsureExists();
            BoardSession.Instance.ItemAdded += OnItemAdded;
        }

        private void Update()
        {
            if (_root.activeSelf && Time.unscaledTime >= _hideAt)
            {
                _root.SetActive(false);
            }
        }

        private void OnItemAdded(ItemDefinition item)
        {
            _icon.color = item.IconTint;
            _label.text = $"{item.ItemName} 획득";
            _root.SetActive(true);
            _hideAt = Time.unscaledTime + VisibleSeconds;
        }

        private void Build()
        {
            var canvasGO = new GameObject("ItemAcquiredToastCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var inner = UGUIKit.CreateBorderedPanel(canvasGO.transform, "Banner", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), UGUIKit.DefaultBorderColor, new Color(0.98f, 0.98f, 0.95f), 2f);
            var outerRect = (RectTransform)inner.transform.parent;
            outerRect.sizeDelta = new Vector2(240f, 44f);
            outerRect.anchoredPosition = new Vector2(0f, -40f);

            _icon = UGUIKit.CreateImage(inner, "Icon", new Vector2(0.05f, 0.15f), new Vector2(0.28f, 0.85f), Color.clear);
            _label = UGUIKit.CreateText(inner, "Label", new Vector2(0.32f, 0f), new Vector2(0.97f, 1f), "", 12, TextAnchor.MiddleLeft);

            _root = outerRect.gameObject;
            _root.SetActive(false);
        }
    }
}
