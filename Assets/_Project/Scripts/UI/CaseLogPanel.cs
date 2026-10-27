using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 사건 기록 창 (Boss_Abel_v1.0 06 · D22): [진행 중] [완료] [증거품] 탭. Q 키·사건 기록 버튼으로 열고 Esc로 닫는다.
    // 증거품은 파티 공용 기록 — 가방 칸·무게를 쓰지 않고 판매·버리기·사용 불가. 미획득 증거는 이름·설명을 숨긴다.
    // 전투 중에도 열람만 가능 (행동 연출 중 입력은 전투 쪽에서 막음).
    public class CaseLogPanel : MonoBehaviour
    {
        public readonly struct Evidence
        {
            public readonly string Name, Description, Source, LinkedQuests;
            public Evidence(string name, string description, string source, string linked) { Name = name; Description = description; Source = source; LinkedQuests = linked; }
        }

        // 1막 증거품 (표시 문구는 기획 확정본 그대로)
        public static readonly Evidence[] AllEvidence =
        {
            new("검시 기록", "제임스의 부인을 포함한 피해자들의 반입·처리 내역이 남아 있는 장부. 수정된 검시 내용과 베르사유 귀족의 인장이 확인된다.",
                "1막 · 런던 폐검시소 — 아벨 모로 최초 처치", "제임스에게 보고 / 베르사유의 운반 기록 추적"),
            new("붉은 룬 조각", "폐검시소의 의식 장치에서 회수한 룬 파편. 영혼을 보관하고 운반하는 용기와 같은 표식이 새겨져 있다.",
                "1막 · 런던 폐검시소 — 아벨 모로 최초 처치", "영혼 운반 방식 조사"),
        };

        private enum Tab { Active, Completed, Evidence }
        private static readonly string[] TabNames = { "진행 중", "완료", "증거품" };
        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color Fill = new Color(0.08f, 0.07f, 0.09f, 0.97f);
        private static readonly Color RowFill = new Color(0.14f, 0.13f, 0.15f, 1f);
        private static readonly Color Selected = new Color(0.42f, 0.33f, 0.18f, 1f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.55f, 0.53f, 0.5f);

        public static CaseLogPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root.activeSelf;

        private GameObject _root;
        private Tab _tab;
        private Text _body;
        private readonly List<Button> _tabs = new();
        private RectTransform _openButton;

        // 씬마다 한 번: buttonOffset = 화면 오른쪽 위 기준 "사건 기록" 버튼 위치 (px, 1280×720 기준)
        public static CaseLogPanel EnsureExists(Vector2 buttonOffset)
        {
            if (Instance == null) new GameObject("CaseLogPanel").AddComponent<CaseLogPanel>().Build();
            Instance._openButton.anchoredPosition = buttonOffset;
            return Instance;
        }

        public static void Toggle()
        {
            if (Instance == null) return;
            if (IsOpen) Instance._root.SetActive(false);
            else Instance.Open(Tab.Active);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) _root.SetActive(false);
        }

        private void Open(Tab tab)
        {
            _tab = tab;
            _root.SetActive(true);
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].targetGraphic.color = (int)_tab == i ? Selected : RowFill;
            var s = BoardSession.Instance;
            if (s == null) { _body.text = ""; return; }
            switch (_tab)
            {
                case Tab.Active:
                    _body.text = s.Quests.Count == 0 ? "진행 중인 사건이 없다."
                        : string.Join("\n\n", s.Quests.Select(q => $"<b><color=#D9B45A>{q.Title}</color></b>{(q.Kind == QuestType.Main ? "  (주요)" : "")}\n{q.Objective}"));
                    break;
                case Tab.Completed:
                    _body.text = s.CompletedQuests.Count == 0 ? "아직 완료한 사건이 없다."
                        : string.Join("\n\n", s.CompletedQuests.Select(q => $"<b>{q.Title}</b>  — 완료\n<color=#8C877F>{q.Objective}</color>"));
                    break;
                case Tab.Evidence:
                    _body.text = string.Join("\n\n", AllEvidence.Select(ev => s.KeyItems.Contains(ev.Name)
                        ? $"<b><color=#D9B45A>📄 {ev.Name}</color></b>  — 확보\n{ev.Description}\n<color=#8C877F>획득: {ev.Source}\n연결: {ev.LinkedQuests}</color>"
                        : "<b>📄 ???</b>  — 미확보\n<color=#8C877F>아직 확보하지 못한 증거.</color>"));
                    break;
            }
        }

        private CaseLogPanel Build()
        {
            Instance = this;
            var canvasGO = new GameObject("CaseLogCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            // 사건 기록 버튼 (창이 닫혀 있을 때도 보임)
            var open = UGUIKit.CreateThemedButton(canvasGO.transform, "CaseLogButton", new Vector2(1f, 1f), new Vector2(1f, 1f), "사건 기록 (Q)", new Color(0.2f, 0.16f, 0.1f), 11);
            _openButton = (RectTransform)open.transform;
            _openButton.pivot = new Vector2(1f, 1f);
            _openButton.sizeDelta = new Vector2(120f, 22f);
            open.onClick.AddListener(Toggle);

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.5f));
            _root = dim.gameObject;
            var win = UGUIKit.CreateThemedPanel(UGUIKit.Safe16x9(dim.transform), "Window", new Vector2(0.2f, 0.15f), new Vector2(0.8f, 0.88f), Gold, Fill, 2f, UITheme.Current != null ? UITheme.Current.PanelFrame : null);
            win.parent.gameObject.AddComponent<Button>(); // 창 안 클릭이 뒤로 새지 않게
            var title = UGUIKit.CreateText(win, "Title", new Vector2(0.03f, 0.9f), new Vector2(0.6f, 0.98f), "사건 기록", 18, TextAnchor.MiddleLeft);
            title.color = Gold;
            title.fontStyle = FontStyle.Bold;
            UGUIKit.CreateThemedButton(win, "Close", new Vector2(0.92f, 0.91f), new Vector2(0.98f, 0.98f), "×", RowFill, 14).onClick.AddListener(() => _root.SetActive(false));
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                float x0 = 0.03f + i * 0.2f;
                var b = UGUIKit.CreateThemedButton(win, $"Tab{i}", new Vector2(x0, 0.8f), new Vector2(x0 + 0.19f, 0.88f), TabNames[i], RowFill, 12);
                b.onClick.AddListener(() => Open((Tab)index));
                // 탭 아이콘: 진행 중 = 의뢰(Z15) · 완료 = 목표 완료(Z34) · 증거품 = 검시 기록(Z16)
                var tabIcon = i == 0 ? UITheme.Current.QuestIcon : i == 1 ? UITheme.Current.ObjectiveDone : UITheme.Current.EvidenceIcon;
                UGUIKit.CreateIcon(b.transform, "Icon", new Vector2(0.04f, 0.1f), new Vector2(0.22f, 0.9f), tabIcon);
                _tabs.Add(b);
            }
            _body = UGUIKit.CreateText(win, "Body", new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.77f), "", 13, TextAnchor.UpperLeft);
            _body.color = TextLight;
            _body.supportRichText = true;
            _root.SetActive(false);
            return this;
        }

        // 자동 플레이 점검용
        public static void DevOpen(int tab) { if (Instance != null) Instance.Open((Tab)tab); }
        public static void DevClose() { if (Instance != null) Instance._root.SetActive(false); }
    }
}
