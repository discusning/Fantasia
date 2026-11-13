using System;
using System.Collections;
using System.Collections.Generic;
using Fantasia.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/13 팀장 지적: 보물 칸을 밟아도 창이 없어 받았는지 알 수 없음 → 보상 창.
    // 11/13 팀장 2차: 위에서 내려오는 판(전투 승리 연출)은 보물 상자와 안 맞음 → 보라 이벤트 칸과 같은 큰 창 틀(위 장식 + 이름 칸)로.
    //   창이 가운데에서 살짝 커지며 나타남 → 물건 칸이 하나씩 톡톡 나타남 → [확인].
    // 첫 칸은 Cent(동전 그림). 가방이 차서 못 넣은 물건은 "받을 물건으로 보관" 한 줄.
    public class TreasurePanel : MonoBehaviour
    {
        public const int MaxCells = 4;
        private static TreasurePanel _instance;
        public static bool IsOpen => _instance != null && _instance._root.activeSelf;

        private GameObject _root, _buttonGO;
        private RectTransform _window;
        private CanvasGroup _windowGroup;
        private Text _place, _note;
        private readonly List<(GameObject root, Image frame, Image icon, Text name)> _cells = new();
        private Action _onClose;

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color CentColor = new Color(0.96f, 0.82f, 0.42f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextSoft = new Color(0.82f, 0.78f, 0.72f);
        private static readonly Color PanelFill = new Color(0.08f, 0.07f, 0.09f, 0.97f);

        // items: (그림, 이름, 색). place = 칸 이름(예: "J06 보물 상자"), stored = 가방이 차서 보관한 개수
        public static void Show(string place, int cent, List<(Sprite icon, string name, Color color)> items, int stored, Action onClose = null)
        {
            if (_instance == null) _instance = new GameObject("TreasurePanel").AddComponent<TreasurePanel>().Build();
            _instance.Open(place, cent, items, stored, onClose);
        }

        private void Open(string place, int cent, List<(Sprite icon, string name, Color color)> items, int stored, Action onClose)
        {
            _onClose = onClose;
            var cells = new List<(Sprite icon, string name, Color color)>();
            if (cent > 0) cells.Add((UITheme.Current != null ? UITheme.Current.CentIcon : null, $"+{cent} Cent", CentColor));
            cells.AddRange(items);
            int n = Mathf.Min(cells.Count, MaxCells);
            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                c.root.SetActive(false);
                if (i >= n) continue;
                var rt = (RectTransform)c.root.transform;
                rt.anchorMin = new Vector2((float)i / n, 0f);
                rt.anchorMax = new Vector2((float)(i + 1) / n, 1f);
                c.frame.color = cells[i].color;
                c.icon.sprite = cells[i].icon;
                c.icon.enabled = cells[i].icon != null;
                c.name.text = cells[i].name;
                c.name.color = cells[i].color;
            }
            _place.text = items.Count > 0 ? $"{place}에서 {cells.Count}가지를 찾았습니다" : $"{place}에서 Cent를 찾았습니다";
            _note.text = stored > 0 ? $"가방이 가득 차 {stored}개는 '받을 물건'에 보관했습니다 (가방에서 받기)" : "모두 가방에 넣었습니다";
            DevSummary = string.Join(" + ", cells.ConvertAll(c => c.name));

            _root.SetActive(true);
            SfxPlayer.Play(SfxIds.ItemGet);
            StopAllCoroutines();
            StartCoroutine(Reveal(n));
        }

        // 창이 살짝 커지며 나타남(0.18초) → 칸이 0.09초 간격으로 톡톡 → 버튼
        private IEnumerator Reveal(int n)
        {
            _buttonGO.SetActive(false);
            for (float t = 0f; t < 0.18f; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 0.18f);
                _window.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, k);
                _windowGroup.alpha = k;
                yield return null;
            }
            _window.localScale = Vector3.one;
            _windowGroup.alpha = 1f;
            for (int i = 0; i < n; i++)
            {
                var cell = _cells[i].root.transform;
                _cells[i].root.SetActive(true);
                if (i > 0) SfxPlayer.Play(SfxIds.Coin);
                for (float t = 0f; t < 0.12f; t += Time.unscaledDeltaTime)
                {
                    float k = t / 0.12f;
                    cell.localScale = Vector3.one * (k < 0.7f ? Mathf.Lerp(0.6f, 1.08f, k / 0.7f) : Mathf.Lerp(1.08f, 1f, (k - 0.7f) / 0.3f));
                    yield return null;
                }
                cell.localScale = Vector3.one;
                yield return new WaitForSecondsRealtime(0.05f);
            }
            _buttonGO.SetActive(true);
        }

        private void Close()
        {
            if (!IsOpen) return;
            StopAllCoroutines();
            _root.SetActive(false);
            SfxPlayer.Play(SfxIds.UiClose);
            var cb = _onClose; _onClose = null;
            cb?.Invoke();
        }

        private void Update()
        {
            if (IsOpen && _buttonGO.activeInHierarchy && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))) Close();
        }

        private TreasurePanel Build()
        {
            var canvasGO = new GameObject("TreasureCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 38;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.5f));
            _root = dim.gameObject;

            // 보라 이벤트 칸과 같은 큰 창 ("Window" 이름이면 틀 + 위 장식 + 이름 칸, 제목은 장식 이름 칸으로 옮겨짐)
            var fill = UGUIKit.CreateThemedPanel(UGUIKit.Safe16x9(dim.transform), "Window", new Vector2(0.33f, 0.3f), new Vector2(0.67f, 0.68f), Gold, PanelFill, 2.5f);
            _window = (RectTransform)fill.parent;
            _windowGroup = _window.gameObject.AddComponent<CanvasGroup>();
            var title = UGUIKit.CreateText(fill, "Title", new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.98f), "보물 상자", 20, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.color = CentColor;

            _place = UGUIKit.CreateText(fill, "Place", new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.9f), "", 14, TextAnchor.MiddleCenter);
            _place.color = TextSoft;
            _place.raycastTarget = false;

            var strip = UGUIKit.CreateRect(fill, "Items", new Vector2(0.04f, 0.34f), new Vector2(0.96f, 0.76f));
            for (int i = 0; i < MaxCells; i++)
            {
                var cell = UGUIKit.CreateRect(strip, $"Item{i}", Vector2.zero, Vector2.one);
                var frame = UGUIKit.CreateImage(cell, "Frame", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Color.white);
                frame.rectTransform.pivot = new Vector2(0.5f, 1f);
                frame.rectTransform.sizeDelta = new Vector2(60f, 60f);
                frame.raycastTarget = false;
                var back = UGUIKit.CreateImage(frame.transform, "Back", Vector2.zero, Vector2.one, new Color(0.1f, 0.08f, 0.07f, 1f));
                back.rectTransform.offsetMin = new Vector2(2f, 2f);
                back.rectTransform.offsetMax = new Vector2(-2f, -2f);
                back.raycastTarget = false;
                var icon = UGUIKit.CreateImage(back.transform, "Icon", Vector2.zero, Vector2.one, Color.white);
                icon.rectTransform.offsetMin = new Vector2(4f, 4f);
                icon.rectTransform.offsetMax = new Vector2(-4f, -4f);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                var name = UGUIKit.CreateText(cell, "Name", Vector2.zero, Vector2.one, "", 13, TextAnchor.UpperCenter);
                name.rectTransform.offsetMax = new Vector2(-2f, -66f);
                name.rectTransform.offsetMin = new Vector2(2f, 0f);
                name.fontStyle = FontStyle.Bold;
                name.raycastTarget = false;
                _cells.Add((cell.gameObject, frame, icon, name));
            }

            _note = UGUIKit.CreateText(fill, "Note", new Vector2(0.05f, 0.21f), new Vector2(0.95f, 0.32f), "", 12, TextAnchor.MiddleCenter);
            _note.color = TextLight;
            _note.raycastTarget = false;

            var button = UGUIKit.CreateThemedButton(fill, "Ok", new Vector2(0.36f, 0.04f), new Vector2(0.64f, 0.18f), "확인", new Color(0.32f, 0.22f, 0.1f), 14);
            button.GetComponentInChildren<Text>().color = TextLight;
            button.onClick.AddListener(Close);
            _buttonGO = button.gameObject;

            BoardHintPanel.RegisterModal(nameof(TreasurePanel), () => IsOpen);
            _root.SetActive(false);
            return this;
        }

        // 자동 플레이 점검용
        public static string DevSummary { get; private set; } = "";
        public static bool DevButtonVisible => _instance != null && _instance._buttonGO.activeInHierarchy;
        public static void DevClose() { if (_instance != null) _instance.Close(); }
    }
}
