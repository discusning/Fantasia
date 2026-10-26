using System;
using System.Linq;
using Fantasia.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 1막 클리어 결과 화면 (세이브 추가 수정안 1): 보스 처치 → 보고 → 클리어 저장 → 이 화면 → 메인 메뉴.
    // 클리어 뒤 이어하기도 이 화면으로 (보상 재지급 없음 — 저장된 기록만 보여준다).
    public class ActClearPanel : MonoBehaviour
    {
        private static ActClearPanel _instance;
        public static bool IsOpen => _instance != null && _instance._root.activeSelf;

        private GameObject _root;
        private Text _body, _buttonLabel;
        private Action _onClose;

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);

        public static void Show(SaveGame.Data d, string buttonLabel, Action onClose)
        {
            if (_instance == null) _instance = new GameObject("ActClearPanel").AddComponent<ActClearPanel>().Build();
            _instance._body.text = Summary(d);
            _instance._buttonLabel.text = buttonLabel;
            _instance._onClose = onClose;
            _instance._root.SetActive(true);
        }

        private static string Summary(SaveGame.Data d)
        {
            if (d == null) return "";
            var t = TimeSpan.FromSeconds(d.playSeconds);
            var party = string.Join("\n", d.members.Select(m =>
                $"  {JobName(m.job)} · Lv {m.level} · {(m.dead ? "영구 사망" : $"HP {m.hp} · LP {m.lp}")}"));
            var evidence = d.keyItems.Count > 0 ? string.Join(", ", d.keyItems) : "없음";
            return $"검시관 아벨 모로 처치 · 제임스에게 보고 완료\n\n"
                   + $"플레이 시간 {(int)t.TotalHours}:{t.Minutes:00} · 월드 {d.worldRound}라운드 · Cent {d.cent}\n"
                   + $"확보한 증거: {evidence}\n\n파티\n{party}\n\n"
                   + "<color=#D9B45A>다음 목적지: 2막 베르사유 — 추후 공개</color>";
        }

        private static string JobName(int id) =>
            Fantasia.Data.ItemCatalog.Current != null ? Fantasia.Data.ItemCatalog.Current.Job((Fantasia.Data.JobId)id)?.DisplayName ?? "?" : "?";

        private void Close()
        {
            _root.SetActive(false);
            _onClose?.Invoke();
        }

        private ActClearPanel Build()
        {
            var canvasGO = new GameObject("ActClearCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0.03f, 0.02f, 0.03f, 0.92f));
            _root = dim.gameObject;
            var title = UGUIKit.CreateText(dim.transform, "Title", new Vector2(0.2f, 0.8f), new Vector2(0.8f, 0.93f), "1막 클리어", 40, TextAnchor.MiddleCenter);
            title.color = Gold;
            title.fontStyle = FontStyle.Bold;
            var sub = UGUIKit.CreateText(dim.transform, "Sub", new Vector2(0.2f, 0.74f), new Vector2(0.8f, 0.8f), "런던 — 검시소의 장부", 16, TextAnchor.MiddleCenter);
            sub.color = TextLight;
            _body = UGUIKit.CreateText(dim.transform, "Body", new Vector2(0.28f, 0.2f), new Vector2(0.72f, 0.72f), "", 14, TextAnchor.UpperLeft);
            _body.color = TextLight;
            _body.supportRichText = true;
            var button = UGUIKit.CreateThemedButton(dim.transform, "Close", new Vector2(0.4f, 0.07f), new Vector2(0.6f, 0.15f), "", new Color(0.45f, 0.18f, 0.16f), 16);
            button.onClick.AddListener(Close);
            _buttonLabel = button.GetComponentInChildren<Text>();
            _root.SetActive(false);
            return this;
        }

        public static void DevClose() { if (IsOpen) _instance.Close(); }
    }
}
