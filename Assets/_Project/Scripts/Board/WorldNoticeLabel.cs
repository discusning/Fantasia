using Fantasia.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.Board
{
    // 11/14 팀장: 보드 위 안내 글(2막 입구 K12 "2막 베르사유 추후 공개")이 안개·앞 집에 가려 끊겨 보임
    // → 화면 UI 글자가 이 오브젝트의 화면 위치를 따라다님. 안개·건물 위에 늘 다 보이고, 창이 열리면 숨음.
    public class WorldNoticeLabel : MonoBehaviour
    {
        private Text _text;
        private RectTransform _rect;
        private Canvas _canvas;
        public static WorldNoticeLabel Last { get; private set; }
        public bool DevOnScreen => _text != null && _text.enabled;

        public void Init(string message)
        {
            Last = this;
            var canvasGO = new GameObject("WorldNoticeCanvas");
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 2; // HUD(5)·창들 아래 — 보드 그림 위
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            _text = UGUIKit.CreateText(canvasGO.transform, "Notice", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), message, 22, TextAnchor.MiddleCenter);
            _text.color = new Color(0.97f, 0.87f, 0.58f);
            _text.fontStyle = FontStyle.Bold;
            _text.raycastTarget = false;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow; _text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = _text.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(0.12f, 0.08f, 0.05f, 0.9f); outline.effectDistance = new Vector2(1.5f, -1.5f);
            _rect = _text.rectTransform;
            _rect.sizeDelta = new Vector2(260f, 70f);
        }

        private void LateUpdate()
        {
            if (_text == null) return;
            var cam = Camera.main;
            bool show = cam != null && !BoardHintPanel.AnyModalOpen;
            if (show)
            {
                var sp = cam.WorldToScreenPoint(transform.position);
                show = sp.z > 0f;
                if (show)
                {
                    var parent = (RectTransform)_rect.parent;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, sp, null, out var local);
                    _rect.anchoredPosition = local;
                    // 멀리 볼수록 조금 작게 (월드 글자처럼) — 기본 거리 16 기준
                    float d = Vector3.Distance(cam.transform.position, transform.position);
                    _rect.localScale = Vector3.one * Mathf.Clamp(16f / Mathf.Max(1f, d), 0.7f, 1.3f);
                    // 11/15 팀장 "2막 글자가 수첩 버튼에 겹침": 화면 UI(위 바·수첩 버튼·의뢰 상자·아래 카드)와 겹치면 흐리게 숨김
                    _targetAlpha = OverHud(sp) ? 0f : 1f;
                }
            }
            if (!show) { _alpha = 0f; _targetAlpha = 1f; }
            else _alpha = Mathf.MoveTowards(_alpha, _targetAlpha, Time.unscaledDeltaTime * 5f);
            var c = _text.color; c.a = _alpha; _text.color = c;
            bool on = show && _alpha > 0.01f;
            if (_text.enabled != on) _text.enabled = on;
        }

        private float _alpha, _targetAlpha = 1f;
        public bool DevHiddenByHud => _targetAlpha < 0.5f;

        // 글자 칸(화면 픽셀)이 HUD 자리와 겹치는지. 자리는 1920×1080 기준 → 화면 높이 비율로 맞춤
        private bool OverHud(Vector3 sp)
        {
            float s = Screen.height / 1080f;
            float k = _rect.localScale.x * s;
            var r = new Rect(sp.x - 130f * k, sp.y - 35f * k, 260f * k, 70f * k);
            float W = Screen.width, H = Screen.height;
            bool Hit(float x0, float y0, float x1, float y1) => r.Overlaps(Rect.MinMaxRect(x0, y0, x1, y1));
            return Hit(0f, H - 115f * s, W, H)                         // 위쪽 바
                || Hit(0f, H - 175f * s, 140f * s, H)                  // 수첩 버튼 (H)
                || Hit(W - 280f * s, H - 290f * s, W, H)               // 사건 기록 버튼 · 의뢰 상자
                || Hit(0f, 0f, W, 245f * s);                           // 아래 파티 카드
        }
    }
}
