using System;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 저장 실패 (세이브 추가 수정안 — 32번 수정): 경고 → 진행 일시정지 → 재시도 → 저장 성공 후 재개.
    // 일시정지 = 시간 정지(Time.timeScale 0) + 입력을 막는 창. 성공할 때까지 창이 닫히지 않는다.
    public class SaveErrorPanel : MonoBehaviour
    {
        private static SaveErrorPanel _instance;
        public static bool IsOpen => _instance != null && _instance._root.activeSelf;

        private GameObject _root;
        private Text _body;
        private string _reason;
        private Func<bool> _retry;
        private float _timeScaleBefore = 1f;
        private int _attempts;

        public static void Show(string reason, string error, Func<bool> retry)
        {
            if (_instance == null)
            {
                var go = new GameObject("SaveErrorPanel");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SaveErrorPanel>().Build();
            }
            var p = _instance;
            if (!p._root.activeSelf)
            {
                p._timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
                p._attempts = 0;
            }
            p._reason = reason;
            p._retry = retry;
            p.SetText(error);
            p._root.SetActive(true);
            Time.timeScale = 0f;
            Debug.LogWarning($"[SaveGame] 저장 실패 — 일시정지 ({reason}): {error}");
        }

        private void SetText(string error) =>
            _body.text = $"게임을 저장하지 못했습니다.\n저장이 끝날 때까지 진행을 멈춥니다.\n\n<color=#8C877F>저장 지점: {_reason}\n원인: {error ?? "알 수 없음"}{(_attempts > 0 ? $"\n재시도 {_attempts}회 실패" : "")}</color>";

        private void Retry()
        {
            _attempts++;
            if (_retry != null && _retry())
            {
                _root.SetActive(false);
                Time.timeScale = _timeScaleBefore;
                Debug.Log($"[SaveGame] 재시도 성공 — 진행 재개 ({_reason})");
                return;
            }
            SetText(Fantasia.Core.SaveGame.LastError);
        }

        private SaveErrorPanel Build()
        {
            var canvasGO = new GameObject("SaveErrorCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // 모든 창 위
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.7f));
            _root = dim.gameObject;
            var box = UGUIKit.CreateThemedPanel(UGUIKit.Safe16x9(dim.transform), "Box", new Vector2(0.3f, 0.33f), new Vector2(0.7f, 0.67f), new Color(0.85f, 0.35f, 0.3f), new Color(0.08f, 0.06f, 0.07f, 0.98f), 2f);
            var title = UGUIKit.CreateText(box, "Title", new Vector2(0.05f, 0.8f), new Vector2(0.95f, 0.95f), "저장 실패", 18, TextAnchor.MiddleCenter);
            title.color = new Color(0.95f, 0.45f, 0.4f);
            title.fontStyle = FontStyle.Bold;
            _body = UGUIKit.CreateText(box, "Body", new Vector2(0.06f, 0.3f), new Vector2(0.94f, 0.8f), "", 13, TextAnchor.MiddleCenter);
            _body.color = new Color(0.92f, 0.9f, 0.86f);
            _body.supportRichText = true;
            UGUIKit.CreateThemedButton(box, "Retry", new Vector2(0.3f, 0.07f), new Vector2(0.7f, 0.24f), "다시 시도", new Color(0.45f, 0.18f, 0.16f), 15).onClick.AddListener(Retry);
            _root.SetActive(false);
            return this;
        }

        public static void DevRetry() { if (IsOpen) _instance.Retry(); }
    }
}
