using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/14 팀장 결정 Q3 "사건 수첩 형식 가이드 북" — 보드 왼쪽에 떠 있던 긴 안내(14줄)를 가죽 수첩 창으로 옮김.
    // 디자인 T1~T6 (원본 ArtSource/2026-11-14_디자인_묶음, Resources/UI/Notebook). 배치는 디자인 적용 예시 그대로:
    //  왼쪽 가장자리 책갈피 탭 6개 · 왼쪽 쪽 = 제목 + 게임 사진(액자) + 한 줄 · 오른쪽 쪽 = 제목 + 단문 3~6줄 + ◀ 쪽 ▶ · 오른쪽 위 밀랍 닫기.
    // 화면 약 70%, 뒤는 어둡게. H·Esc·닫기로 닫음. 첫 월드 라운드에 한 번 자동으로 열림. 보드 왼쪽 위 수첩 버튼(T6)·H로 다시 엶.
    public class NotebookPanel : MonoBehaviour
    {
        private const string Res = "UI/Notebook/";
        private static readonly string[] TabNames = { "조작", "보드", "전투", "스킬·상태", "상점", "피의 사냥·낮밤" };
        private static readonly Color Ink = new Color(0.22f, 0.15f, 0.1f);
        private static readonly Color InkDim = new Color(0.38f, 0.29f, 0.2f);
        private static readonly Color Cream = new Color(0.95f, 0.88f, 0.72f);
        private static readonly Color Accent = new Color(0.55f, 0.18f, 0.12f);

        private readonly struct Page
        {
            public readonly int Tab, Photo; public readonly string LeftTitle, Caption, RightTitle; public readonly string[] Lines;
            public Page(int tab, int photo, string leftTitle, string caption, string rightTitle, params string[] lines)
            { Tab = tab; Photo = photo; LeftTitle = leftTitle; Caption = caption; RightTitle = rightTitle; Lines = lines; }
        }

        // 글은 기획 확정 규칙(현행기획서_1막·System.md)을 단문으로. <b>강조</b>는 붉은 잉크.
        private static readonly Page[] Pages =
        {
            new(0, 0, "조작", "키보드와 마우스로 움직여요.", "키 안내",
                "<b>Space</b>  주사위 굴리기", "<b>밝은 칸 클릭</b>  그 칸으로 이동", "<b>I</b> 또는 초상 클릭  가방", "<b>Q</b>  사건 기록", "<b>B</b>  거점 칸에서 상점", "<b>H</b>  이 수첩 · <b>Esc</b>  메뉴"),
            new(0, 0, "조작", "보드를 둘러볼 때 써요.", "카메라",
                "<b>WASD · 방향키</b>  카메라 이동", "<b>마우스 휠</b>  가까이 · 멀리", "<b>F</b>  파티 쪽으로 돌아오기", "<b>Ctrl + 오른쪽 클릭</b>  건물 정보 보기"),
            new(1, 1, "보드", "칸 색으로 무엇이 있는지 알 수 있어요.", "칸 색",
                "<b>주황</b>  적", "<b>자주</b>  보스", "<b>파랑</b>  거점 (상점)", "<b>보라</b>  이벤트", "<b>초록</b>  회복", "<b>노랑</b>  보물"),
            new(1, 1, "보드", "주사위를 굴리면 갈 수 있는 칸이 밝아져요.", "이동",
                "주사위 눈만큼 움직일 수 있습니다.", "밝은 칸을 누르면 그 칸으로 갑니다.", "적 칸에 들어가면 전투가 시작됩니다.", "이벤트 칸은 이번 턴 한 번 더 움직이게 해 줍니다."),
            new(2, 2, "전투", "아군 행동과 전투 판정은 여기서 확인해요.", "판정 칸",
                "공격하면 판정 칸이 굴러갑니다.", "판정 칸이 <b>모두 성공</b>하면 치명타!", "<b>집중</b>을 쓰면 판정 칸 하나가 확정 성공합니다.", "방어는 다음 내 차례까지 유지됩니다.", "퇴각에 성공하면 파티 전원이 전투에서 빠집니다."),
            new(2, 2, "전투", "보스가 큰 기술을 예고하면 막을 수 있어요.", "보스 예고와 저지",
                "예고는 보스 HP 바 아래에 남아 있습니다.", "<b>봉합 방해</b>: 인지 판정 2칸 중 1칸 성공이면 저지.", "집중 1을 쓰면 반드시 저지합니다.", "실패해도 다른 아군이 다시 막을 수 있습니다.", "1.2배 이상 공격 기술로 맞혀도 끊깁니다."),
            new(3, 3, "스킬·상태", "명탐정 — 적을 꿰뚫어 보는 사람.", "명탐정 스킬",
                "<b>약점 간파</b>  적 물리·마법 방어 −4 (적 2번 행동).", "<b>일점 돌파</b>  강한 찌르기. 치명타면 방해.", "<b>장막 걷기</b>  적의 회피 상태를 없앰."),
            new(3, 3, "스킬·상태", "역병 의사 — 치료와 독기를 다루는 사람.", "역병 의사 스킬",
                "<b>응급 봉합</b>  HP 회복. 2칸 모두 성공이면 출혈도 제거.", "<b>역병 정화</b>  중독·저주·혼란 제거.", "<b>검은 연무</b>  모든 적에게 마법 피해. 3칸 성공이면 중독 1.", "<b>조제 숙련</b>  내가 쓰는 HP 회복약 +25%."),
            new(3, 3, "스킬·상태", "저격수 — 멀리서 한 발로 끝내는 사람.", "저격수 스킬",
                "<b>첫 탄의 침묵</b>  전투 첫 총·활 공격 피해 +15%.", "<b>영점 조정</b>  다음 공격 판정 성공률 +15%p.", "<b>관통탄</b>  적 방어를 절반만 받음.", "<b>저지 사격</b>  치명타면 적 속도 −15 (2턴)."),
            new(3, 3, "스킬·상태", "몸 둘레 빛·고리로 걸린 상태를 알 수 있어요.", "상태 효과",
                "<b>출혈</b>  내 차례마다 2 피해 (3턴).", "<b>중독</b>  차례마다 스택×3 피해, 능력 −5. <b>정화 전까지</b> 남음.", "<b>속박</b>  속도 −15 (2턴).", "<b>혼란</b>  다음 차례 행동이 멋대로 (1턴).", "<b>방어·공격 감소</b>  각각 −4·−3 (2턴)."),
            new(4, 4, "상점", "거점 칸에서 B를 눌러 열어요.", "상점",
                "탭: 보급품 · 무기 · 방어구 · 서비스 · 작업대 · 팔기 · 의뢰", "<b>3월드 라운드</b>마다 재고가 채워집니다.", "되팔면 산 값의 <b>40%</b>를 받습니다.", "중독은 해독제나 서비스 '정밀 치료'로 풉니다."),
            new(5, 5, "피의 사냥", "화면 위 피의 흔적이 차오르면 시작돼요.", "피의 사냥",
                "피의 흔적은 월드 라운드마다 +10.", "<b>100</b>이 되면 그림자 사신 3마리가 나옵니다.", "<b>3월드 라운드</b> 안에 모두 쓰러뜨리세요.", "사냥 동안 주사위 −2.", "못 잡으면 한 명이 HP 1이 됩니다."),
            new(5, 5, "낮과 밤", "가운데 원의 해와 달이 시간을 알려 줘요.", "시간대",
                "아침 → 낮 → 저녁 → 새벽 순서로 바뀝니다.", "월드 라운드마다 한 단계씩 넘어갑니다.", "시간대는 분위기만 바꿉니다.", "적 세기·보상에는 영향이 없습니다."),
        };

        public static NotebookPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;
        public static bool AutoOpened; // 한 번 자동으로 열었음 (게임 실행 중)
        // 자동 점검은 수첩이 입력을 막지 않게 자동 열기 안 함 (-autoplay-notebook은 점검이 직접 엶)
        public static bool SuppressAutoOpen => Array.IndexOf(Environment.GetCommandLineArgs(), "-autoplay") >= 0;
        private static int _closedFrame = -1;
        public static bool CanOpenThisFrame => Time.frameCount != _closedFrame; // H로 닫은 같은 프레임에 보드가 다시 열지 않게
        public static int DevPage => Instance != null ? Instance._page : -1;

        private GameObject _root, _button;
        private int _page;
        private Text _leftTitle, _caption, _rightTitle, _body, _pageNo;
        private Image _photo;
        private readonly List<(Image tab, RectTransform rect, Text label, Image icon)> _tabs = new();
        private Button _prev, _next;

        public static NotebookPanel EnsureExists()
        {
            if (Instance == null) new GameObject("NotebookPanel").AddComponent<NotebookPanel>().Build();
            BoardHintPanel.RegisterModal("Notebook", () => IsOpen);
            return Instance;
        }

        public static void Toggle() { if (Instance == null) return; if (IsOpen) Instance.Close(); else Instance.Open(Instance._page); }
        public static void OpenTab(int tab) { if (Instance != null) Instance.Open(FirstPageOf(tab)); }
        public static void SetButtonVisible(bool on) { if (Instance != null && Instance._button != null && Instance._button.activeSelf != on) Instance._button.SetActive(on); }
        public static void DevOpenPage(int page) { if (Instance != null) Instance.Open(Mathf.Clamp(page, 0, Pages.Length - 1)); }
        public static int PageCount => Pages.Length;

        private static int FirstPageOf(int tab) { for (int i = 0; i < Pages.Length; i++) if (Pages[i].Tab == tab) return i; return 0; }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.H)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) Flip(-1);
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Flip(1);
        }

        private void Open(int page)
        {
            _page = page;
            if (!_root.activeSelf) Fantasia.Core.SfxPlayer.Play(Fantasia.Core.SfxIds.UiOpen);
            _root.SetActive(true);
            Refresh();
        }

        private void Close()
        {
            _root.SetActive(false);
            _closedFrame = Time.frameCount;
            Fantasia.Core.SfxPlayer.Play(Fantasia.Core.SfxIds.UiClose);
        }

        // 같은 탭 안에서만 넘김 (탭이 바뀌는 건 책갈피로)
        private void Flip(int dir)
        {
            int n = _page + dir;
            if (n < 0 || n >= Pages.Length || Pages[n].Tab != Pages[_page].Tab) return;
            _page = n;
            Refresh();
        }

        private void Refresh()
        {
            var p = Pages[_page];
            _leftTitle.text = p.LeftTitle;
            _caption.text = p.Caption;
            _rightTitle.text = p.RightTitle;
            string accent = ColorUtility.ToHtmlStringRGB(Accent);
            _body.text = string.Join("\n", p.Lines.Select(l => "· " + l.Replace("<b>", $"<color=#{accent}><b>").Replace("</b>", "</b></color>")));
            var photo = Resources.Load<Sprite>(Res + "Photo" + p.Photo);
            _photo.sprite = photo; _photo.enabled = photo != null;
            var inTab = Enumerable.Range(0, Pages.Length).Where(i => Pages[i].Tab == p.Tab).ToList();
            int idx = inTab.IndexOf(_page);
            _pageNo.text = $"{idx + 1} / {inTab.Count}";
            _prev.gameObject.SetActive(idx > 0);
            _next.gameObject.SetActive(idx < inTab.Count - 1);
            var tabSprite = Resources.Load<Sprite>(Res + "Tab"); var tabSel = Resources.Load<Sprite>(Res + "TabSelected");
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool sel = i == p.Tab;
                _tabs[i].tab.sprite = sel ? tabSel : tabSprite;
                _tabs[i].rect.anchoredPosition = new Vector2(sel ? -14f : 0f, 0f); // 선택 탭은 왼쪽으로 더 나옴
                _tabs[i].label.color = sel ? Color.white : Cream;
            }
        }

        private static Image Img(Transform parent, string name, string sprite, Vector2 min, Vector2 max)
        {
            var img = UGUIKit.CreateImage(parent, name, min, max, Color.white);
            img.sprite = Resources.Load<Sprite>(Res + sprite);
            if (img.sprite == null) img.color = new Color(0.3f, 0.2f, 0.12f, 0.9f); // 그림이 없을 때 임시 판
            img.raycastTarget = false;
            return img;
        }

        private static Text Txt(Transform parent, string name, Vector2 min, Vector2 max, int size, TextAnchor align, Color color, bool bold = false)
        {
            var t = UGUIKit.CreateText(parent, name, min, max, "", size, align);
            t.color = color; t.supportRichText = true; t.raycastTarget = false;
            if (bold) t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        private static Button ImgButton(Transform parent, string name, string sprite, Vector2 min, Vector2 max, Action onClick)
        {
            var img = Img(parent, name, sprite, min, max);
            img.raycastTarget = true;
            img.preserveAspect = true;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var c = b.colors; c.highlightedColor = new Color(1f, 0.92f, 0.8f); c.pressedColor = new Color(0.8f, 0.72f, 0.6f); b.colors = c;
            b.onClick.AddListener(() => onClick());
            return b;
        }

        private NotebookPanel Build()
        {
            Instance = this;
            var canvasGO = new GameObject("NotebookCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11; // 사건 기록과 같은 층 (상단 바 12 아래)
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            // 보드 왼쪽 위 수첩 버튼 (T6) — 안내 상자 왼쪽 옆
            var open = ImgButton(canvasGO.transform, "NotebookButton", "Book", new Vector2(0f, 1f), new Vector2(0f, 1f), Toggle);
            var orect = (RectTransform)open.transform;
            orect.pivot = new Vector2(0f, 1f);
            orect.sizeDelta = new Vector2(46f, 46f);
            orect.anchoredPosition = new Vector2(8f, -92f);
            var key = Txt(open.transform, "Key", new Vector2(0.55f, -0.05f), new Vector2(1.15f, 0.3f), 10, TextAnchor.MiddleCenter, Cream, bold: true);
            key.text = "H";
            _button = open.gameObject;

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.6f));
            dim.gameObject.AddComponent<Button>(); // 뒤로 클릭이 새지 않게
            _root = dim.gameObject;
            var safe = UGUIKit.Safe16x9(dim.transform);
            // 펼친 수첩: 화면 약 70% (16:10) — 가죽 표지 위에 같은 크기 종이 (디자인 T1 안내)
            var book = Img(safe, "Cover", "Cover", new Vector2(0.19f, 0.05f), new Vector2(0.87f, 0.86f)).transform; // 위 상단 바(약 0.12)에 닫기 버튼이 걸리지 않게 조금 아래로
            Img(book, "Paper", "Paper", Vector2.zero, Vector2.one);
            book.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // 책 안 클릭이 어두운 판(닫기 아님)으로 가도 문제없게

            // 책갈피 탭 6개 — 책 왼쪽 가장자리 밖으로 (220×90 비율)
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                float y1 = 0.9f - i * 0.135f;
                var holder = new GameObject($"TabHolder{i}", typeof(RectTransform));
                holder.transform.SetParent(book, false);
                var hr = (RectTransform)holder.transform;
                hr.anchorMin = new Vector2(-0.185f, y1 - 0.105f); hr.anchorMax = new Vector2(0.015f, y1);
                hr.offsetMin = hr.offsetMax = Vector2.zero;
                holder.transform.SetAsFirstSibling(); // 탭은 책 뒤에서 나온 것처럼
                var tabBtn = ImgButton(holder.transform, "Tab", "Tab", Vector2.zero, Vector2.one, () => OpenTab(index));
                var tabImg = (Image)tabBtn.targetGraphic; tabImg.preserveAspect = false;
                var icon = Img(tabBtn.transform, "Icon", $"Icon{i}", new Vector2(0.08f, 0.2f), new Vector2(0.3f, 0.8f));
                icon.preserveAspect = true; icon.color = Cream;
                var label = Txt(tabBtn.transform, "Label", new Vector2(0.32f, 0.05f), new Vector2(0.98f, 0.95f), 13, TextAnchor.MiddleLeft, Cream, bold: true);
                label.text = TabNames[i];
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 9; label.resizeTextMaxSize = 13;
                _tabs.Add((tabImg, (RectTransform)tabBtn.transform, label, icon));
            }

            // 왼쪽 쪽: 제목 / 선 / 사진 + 액자 / 한 줄
            _leftTitle = Txt(book, "LeftTitle", new Vector2(0.075f, 0.8f), new Vector2(0.46f, 0.9f), 22, TextAnchor.MiddleLeft, Ink, bold: true);
            Rule(book, 0.075f, 0.46f, 0.795f);
            var frameArea = new Vector2[] { new Vector2(0.08f, 0.35f), new Vector2(0.455f, 0.77f) };
            _photo = Img(book, "Photo", "Photo0", frameArea[0] + new Vector2(0.012f, 0.018f), frameArea[1] - new Vector2(0.012f, 0.018f));
            Img(book, "Frame", "Frame", frameArea[0], frameArea[1]);
            _caption = Txt(book, "Caption", new Vector2(0.08f, 0.2f), new Vector2(0.46f, 0.32f), 14, TextAnchor.UpperLeft, InkDim);

            // 오른쪽 쪽: 제목 / 선 / 단문 / 아래 ◀ 쪽 ▶
            _rightTitle = Txt(book, "RightTitle", new Vector2(0.54f, 0.8f), new Vector2(0.925f, 0.9f), 22, TextAnchor.MiddleLeft, Ink, bold: true);
            Rule(book, 0.54f, 0.925f, 0.795f);
            _body = Txt(book, "Body", new Vector2(0.54f, 0.2f), new Vector2(0.93f, 0.77f), 15, TextAnchor.UpperLeft, Ink);
            _body.lineSpacing = 1.55f;
            _body.resizeTextForBestFit = true; _body.resizeTextMinSize = 11; _body.resizeTextMaxSize = 15;
            Rule(book, 0.54f, 0.925f, 0.185f);
            _prev = ImgButton(book, "Prev", "Prev", new Vector2(0.64f, 0.085f), new Vector2(0.69f, 0.165f), () => Flip(-1));
            _pageNo = Txt(book, "PageNo", new Vector2(0.69f, 0.085f), new Vector2(0.775f, 0.165f), 14, TextAnchor.MiddleCenter, Ink, bold: true);
            _next = ImgButton(book, "Next", "Next", new Vector2(0.775f, 0.085f), new Vector2(0.825f, 0.165f), () => Flip(1));
            var hint = Txt(book, "Hint", new Vector2(0.08f, 0.085f), new Vector2(0.46f, 0.165f), 11, TextAnchor.MiddleLeft, InkDim);
            hint.text = "H · Esc 닫기   ← → 쪽 넘기기";

            // 닫기 (밀랍 도장) — 오른쪽 위 모서리
            ImgButton(book, "Close", "Close", new Vector2(0.94f, 0.9f), new Vector2(1.02f, 1.03f), Close);

            _root.SetActive(false);
            return this;
        }

        private static void Rule(Transform book, float x0, float x1, float y)
        {
            var line = UGUIKit.CreateImage(book, "Rule", new Vector2(x0, y - 0.002f), new Vector2(x1, y + 0.002f), new Color(InkDim.r, InkDim.g, InkDim.b, 0.55f));
            line.raycastTarget = false;
        }
    }
}
