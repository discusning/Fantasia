using System;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/14 팀장 B안: 안내 상자의 "Space: 주사위 굴리기"·턴 시작 알림 대신 — 굴릴 수 있을 때만 보이는 주사위 버튼.
    // 아군 턴 + 아직 안 굴림 + 창이 없을 때만 보임(보이는 것 자체가 "지금 굴릴 차례" 신호). 눌러도, Space로도 굴림.
    // 그림: 게임 주사위 모델을 찍은 것 (Editor/DiceIconShot → Resources/UI/Board/DiceButton).
    public class BoardDiceButton : MonoBehaviour
    {
        public static BoardDiceButton Instance { get; private set; }
        public static bool Visible => Instance != null && Instance._root != null && Instance._root.activeSelf;
        public static int DevClicks { get; private set; }

        private GameObject _root;
        private RectTransform _dice;
        private Action _onClick;
        private float _shownAt;

        private static readonly Color Cream = new Color(0.96f, 0.9f, 0.76f);
        private static readonly Color Gold = new Color(0.86f, 0.68f, 0.36f);

        public static void EnsureExists()
        {
            if (Instance == null) new GameObject("BoardDiceButton").AddComponent<BoardDiceButton>().Build();
        }

        public static void Set(bool visible, Action onClick)
        {
            if (Instance == null) return;
            Instance._onClick = onClick;
            if (Instance._root.activeSelf == visible) return;
            Instance._root.SetActive(visible);
            if (visible) Instance._shownAt = Time.unscaledTime;
        }

        // 자동 점검: 실제 버튼 클릭과 같은 경로
        public static void DevPress() { if (Visible) Instance.Press(); }

        private void Press()
        {
            DevClicks++;
            Fantasia.Core.SfxPlayer.Click();
            _onClick?.Invoke();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Build()
        {
            Instance = this;
            var canvasGO = new GameObject("DiceButtonCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6; // 파티 카드(HUD 5) 위, 창들 아래
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            // 화면 아래 가운데, 파티 카드·집중 칸 바로 위
            var root = UGUIKit.CreateRect(canvasGO.transform, "Root", new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.2f));
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(96f, 96f);
            root.anchoredPosition = new Vector2(0f, 30f);
            _root = root.gameObject;

            // 뒤 빛 (금색 원판, 숨쉬듯)
            var glow = UGUIKit.CreateImage(root, "Glow", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(Gold.r, Gold.g, Gold.b, 0.0f));
            glow.rectTransform.sizeDelta = new Vector2(86f, 86f);
            glow.raycastTarget = false;
            var circle = Resources.Load<Sprite>("UI/Board/Glow");
            if (circle != null) glow.sprite = circle; else glow.enabled = false;

            var dice = UGUIKit.CreateImage(root, "Dice", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Color.white);
            dice.sprite = Resources.Load<Sprite>("UI/Board/DiceButton");
            dice.preserveAspect = true;
            dice.rectTransform.sizeDelta = new Vector2(86f, 86f);
            _dice = dice.rectTransform;
            var button = dice.gameObject.AddComponent<Button>();
            button.targetGraphic = dice;
            var colors = button.colors; colors.highlightedColor = new Color(1f, 0.95f, 0.8f); colors.pressedColor = new Color(0.8f, 0.75f, 0.68f); button.colors = colors;
            button.onClick.AddListener(Press);

            // 11/14 팀장: "주사위 · Space" 이름표 상자는 없앰 — 주사위만. 바닥과 구분되게 그림자만
            var shadow = dice.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(2f, -3f);

            _root.SetActive(false);
        }

        private void Update()
        {
            if (_root == null || !_root.activeSelf) return;
            // 나타날 때 살짝 튀어오르고, 그다음은 천천히 숨쉬듯 + 조금 흔들림 (굴려 달라는 느낌)
            float t = Time.unscaledTime - _shownAt;
            float pop = t < 0.25f ? Mathf.Lerp(0.6f, 1.08f, t / 0.25f) : t < 0.4f ? Mathf.Lerp(1.08f, 1f, (t - 0.25f) / 0.15f) : 1f;
            float breathe = 1f + 0.05f * Mathf.Sin(t * 3.2f);
            _dice.localScale = Vector3.one * pop * breathe;
            _dice.localRotation = Quaternion.Euler(0f, 0f, 6f * Mathf.Sin(t * 1.7f));
        }
    }
}
