using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 보드 왼쪽 안내 (조작법·주사위·이번 턴 결과). 예전 개발용 화면 글자(OnGUI)를 대신한다 — 모든 빌드에서 보이고,
    // 다른 창(인벤토리·상점·대화·결과 화면 등)이 열려 있으면 숨는다.
    public class BoardHintPanel : MonoBehaviour
    {
        private static BoardHintPanel _instance;
        private GameObject _root;
        private Text _text;

        // 열려 있으면 안내·개발 버튼을 숨길 창들 (새 창은 여기에 등록)
        private static readonly List<Func<bool>> Modals = new()
        {
            () => InventoryPanel.IsOpen, () => ShopPanel.IsOpen, () => DialoguePanel.IsOpen, () => EncounterPanel.IsOpen,
            () => CaseLogPanel.IsOpen, () => ConfirmPanel.IsOpen, () => ActClearPanel.IsOpen, () => SaveErrorPanel.IsOpen,
            () => SettingsPanel.IsOpen, () => LandmarkInfoPanel.IsOpen,
        };

        private static readonly Dictionary<string, Func<bool>> Registered = new();
        public static void RegisterModal(string name, Func<bool> isOpen) => Registered[name] = isOpen;
        public static bool AnyModalOpen => Modals.Any(m => m()) || Registered.Values.Any(m => m());

        public static void EnsureExists()
        {
            if (_instance == null) _instance = new GameObject("BoardHintPanel").AddComponent<BoardHintPanel>().Build();
        }

        // 한 줄씩 (빈 줄은 건너뜀)
        public static void Set(params string[] lines)
        {
            if (_instance == null) return;
            bool show = !AnyModalOpen;
            if (_instance._root.activeSelf != show) _instance._root.SetActive(show);
            if (!show) return;
            _instance._text.text = string.Join("\n", lines.Where(l => !string.IsNullOrEmpty(l)));
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }

        private BoardHintPanel Build()
        {
            var canvasGO = new GameObject("BoardHintCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1; // 보드 HUD 위, 모든 창 아래
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var bg = UGUIKit.CreateImage(canvasGO.transform, "Hint", new Vector2(0f, 1f), new Vector2(0f, 1f), new Color(0.05f, 0.04f, 0.05f, 0.62f));
            bg.raycastTarget = false;
            var rect = (RectTransform)bg.transform;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, -92f); // 상단 바·지역 배너 아래
            var fitter = bg.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            _root = bg.gameObject;

            _text = UGUIKit.CreateText(bg.transform, "Text", Vector2.zero, Vector2.one, "", 12, TextAnchor.UpperLeft);
            _text.color = new Color(0.93f, 0.9f, 0.85f);
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.raycastTarget = false;
            _text.lineSpacing = 1.15f;
            return this;
        }
    }
}
