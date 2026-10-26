using System;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 예/아니오 확인 창 (보스 권장 레벨 경고 등). 씬마다 새로 만든다.
    public class ConfirmPanel : MonoBehaviour
    {
        private static ConfirmPanel _instance;
        public static bool IsOpen => _instance != null && _instance._root.activeSelf;

        private GameObject _root;
        private Text _body, _yesLabel, _noLabel;
        private Action _onYes, _onNo;

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);

        public static void Show(string text, string yes, string no, Action onYes, Action onNo)
        {
            if (_instance == null) _instance = new GameObject("ConfirmPanel").AddComponent<ConfirmPanel>().Build();
            _instance._body.text = text;
            _instance._yesLabel.text = yes;
            _instance._noLabel.text = no;
            _instance._onYes = onYes;
            _instance._onNo = onNo;
            _instance._root.SetActive(true);
        }

        private void Choose(bool yes)
        {
            _root.SetActive(false);
            (yes ? _onYes : _onNo)?.Invoke();
        }

        private ConfirmPanel Build()
        {
            var canvasGO = new GameObject("ConfirmCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.55f));
            _root = dim.gameObject;
            var box = UGUIKit.CreateThemedPanel(UGUIKit.Safe16x9(dim.transform), "Box", new Vector2(0.3f, 0.38f), new Vector2(0.7f, 0.62f), Gold, new Color(0.08f, 0.07f, 0.09f, 0.97f), 2f, UITheme.Current != null ? UITheme.Current.PanelFrame : null);
            _body = UGUIKit.CreateText(box, "Text", new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.92f), "", 15, TextAnchor.MiddleCenter);
            _body.color = new Color(0.92f, 0.9f, 0.86f);
            var yes = UGUIKit.CreateThemedButton(box, "Yes", new Vector2(0.1f, 0.1f), new Vector2(0.45f, 0.34f), "", new Color(0.45f, 0.18f, 0.16f), 14);
            yes.onClick.AddListener(() => Choose(true));
            _yesLabel = yes.GetComponentInChildren<Text>();
            var no = UGUIKit.CreateThemedButton(box, "No", new Vector2(0.55f, 0.1f), new Vector2(0.9f, 0.34f), "", new Color(0.16f, 0.15f, 0.17f), 14);
            no.onClick.AddListener(() => Choose(false));
            _noLabel = no.GetComponentInChildren<Text>();
            _root.SetActive(false);
            return this;
        }

        public static void DevChoose(bool yes) { if (IsOpen) _instance.Choose(yes); }
    }
}
