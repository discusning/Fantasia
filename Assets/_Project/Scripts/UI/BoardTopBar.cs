using System.Collections;
using System.Collections.Generic;
using Fantasia.Data;
using Fantasia.World;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 보드 화면 상단 바 (조사: Docs/References/BoardTurnUI_Research_2026-10-24.md — "페이즈 시계 + 위협 트랙").
    //   왼쪽: 파티장 초상·이름 / 가운데: 아군·적 턴 시계(Armello식 2단계) + 월드 라운드 / 양옆: 피의 흔적 G(10칸)·
    //   영혼 수확 S(25·50·75 경고 눈금) 위협 트랙 / 오른쪽: Cent / 바 아래: 지역명. 턴이 바뀌면 가운데 큰 전환 배너.
    // G·S 값은 BoardSession에서 읽기만 한다 — 실제 증가는 D24(피의 흔적)·D25(영혼 수확)에서.
    // 그림은 UITheme(TopBarFrame·PhaseDial*·ThreatNotch·RegionBanner 등), 비우면 단색. 씬마다 새로 만든다(보드 전용).
    public class BoardTopBar : MonoBehaviour
    {
        private static BoardTopBar _instance;

        private const int BloodNotches = 10;               // G +10/라운드 → 칸 하나 = 1라운드
        private static readonly int[] SoulWarnings = { 25, 50, 75 }; // System.md: S 25·50·75 경고

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color BarFill = new Color(0.07f, 0.06f, 0.08f, 0.94f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.62f, 0.6f, 0.56f);
        private static readonly Color AllyColor = new Color(0.95f, 0.75f, 0.35f);   // 해
        private static readonly Color EnemyColor = new Color(0.45f, 0.35f, 0.75f);  // 달
        private static readonly Color Inactive = new Color(0.2f, 0.19f, 0.22f);
        private static readonly Color Blood = new Color(0.8f, 0.15f, 0.18f);
        private static readonly Color Soul = new Color(0.4f, 0.75f, 0.85f);
        private static readonly Color NotchOff = new Color(0.18f, 0.16f, 0.19f);

        private PortraitView _leaderPortrait;
        private Text _leaderName;
        private readonly List<Image> _bloodNotches = new();
        private Text _bloodValue;
        private Image _allyHalf, _enemyHalf;
        private RectTransform _dial;
        private Text _phaseLabel, _roundLabel, _allyTag, _enemyTag;
        private RectTransform _soulFill;
        private Text _soulValue;
        private Text _cent;
        private Text _region;

        private GameObject _splash;
        private CanvasGroup _splashGroup;
        private Image _splashFill;
        private Text _splashTitle, _splashSub;
        private Coroutine _splashRoutine, _dialRoutine;
        private WorldPhase? _shownPhase;

        public static BoardTopBar Instance => _instance;

        public static BoardTopBar EnsureExists()
        {
            if (_instance == null) _instance = new GameObject("BoardTopBar").AddComponent<BoardTopBar>().Build();
            return _instance;
        }

        // 매 갱신: 파티장·G·S·Cent·지역. 페이즈가 바뀌었으면 시계 회전 + 전환 배너(announce일 때).
        public void Refresh(WorldPhase phase, int round, PartyMember leader, int bloodTrail, int soulHarvest, int cent, string region, bool announce)
        {
            if (leader != null)
            {
                _leaderPortrait.Set(leader.Job.Portrait, PortraitStudio.TextureFor(leader.Job));
                _leaderName.text = leader.Job.DisplayName;
            }

            int filled = Mathf.Clamp(bloodTrail / (100 / BloodNotches), 0, BloodNotches);
            for (int i = 0; i < _bloodNotches.Count; i++) _bloodNotches[i].color = i < filled ? Blood : NotchOff;
            _bloodValue.text = bloodTrail >= 80 ? $"{bloodTrail} / 100  사신이 가까워진다" : $"{bloodTrail} / 100";
            _bloodValue.color = bloodTrail >= 80 ? Blood : TextDim;

            _soulFill.anchorMax = new Vector2(Mathf.Clamp01(soulHarvest / 100f), 1f);
            _soulValue.text = $"{soulHarvest} / 100";
            _soulValue.color = soulHarvest >= 75 ? Soul : TextDim;

            _cent.text = $"{cent} Cent";
            _region.text = region;

            bool ally = phase == WorldPhase.AllyTurn;
            _phaseLabel.text = round.ToString();
            _phaseLabel.color = ally ? AllyColor : EnemyColor;
            _roundLabel.text = "라운드";
            _allyTag.color = ally ? AllyColor : TextDim;
            _enemyTag.color = ally ? TextDim : EnemyColor;
            _allyTag.text = ally ? "아군 턴" : "아군";
            _enemyTag.text = ally ? "적" : "적 턴";
            SetDialColors(ally);

            if (_shownPhase != phase)
            {
                if (_shownPhase.HasValue) RotateDial();
                _shownPhase = phase;
                if (announce) ShowSplash(ally, leader);
            }
        }

        private void SetDialColors(bool ally)
        {
            var theme = UITheme.Current;
            if (!UITheme.Apply(_allyHalf, ally ? theme.PhaseDialAllyActive : theme.PhaseDialAllyIdle, tint: false)) _allyHalf.color = ally ? AllyColor : Inactive;
            if (!UITheme.Apply(_enemyHalf, ally ? theme.PhaseDialEnemyIdle : theme.PhaseDialEnemyActive, tint: false)) _enemyHalf.color = ally ? Inactive : EnemyColor;
            // 테마 그림이 있어도 반원 채우기 방식은 유지
            _allyHalf.type = Image.Type.Filled;
            _enemyHalf.type = Image.Type.Filled;
        }

        private void RotateDial()
        {
            if (_dialRoutine != null) StopCoroutine(_dialRoutine);
            _dialRoutine = StartCoroutine(Spin());
        }

        private IEnumerator Spin()
        {
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                _dial.localRotation = Quaternion.Euler(0f, 0f, Mathf.SmoothStep(0f, 180f, t / 0.45f));
                yield return null;
            }
            _dial.localRotation = Quaternion.identity;
        }

        // 턴 전환 배너 — 화면 가운데 0.25초 페이드 인, 0.8초 유지, 0.35초 페이드 아웃.
        private void ShowSplash(bool ally, PartyMember leader)
        {
            var art = ally ? UITheme.Current.AllyTurnBanner : UITheme.Current.EnemyTurnBanner;
            if (!UITheme.Apply(_splashFill, art)) _splashFill.color = ally ? new Color(0.3f, 0.22f, 0.08f, 0.92f) : new Color(0.15f, 0.08f, 0.25f, 0.92f);
            _splashTitle.text = ally ? "아군 턴" : "적 턴";
            _splashTitle.color = ally ? AllyColor : EnemyColor;
            _splashSub.text = ally ? $"{(leader != null ? leader.Job.DisplayName : "파티장")}이(가) 파티를 이끈다" : "교단이 움직인다...";
            if (_splashRoutine != null) StopCoroutine(_splashRoutine);
            _splashRoutine = StartCoroutine(Splash());
        }

        private IEnumerator Splash()
        {
            _splash.SetActive(true);
            for (float t = 0f; t < 0.25f; t += Time.deltaTime) { _splashGroup.alpha = t / 0.25f; yield return null; }
            _splashGroup.alpha = 1f;
            yield return new WaitForSeconds(0.8f);
            for (float t = 0f; t < 0.35f; t += Time.deltaTime) { _splashGroup.alpha = 1f - t / 0.35f; yield return null; }
            _splash.SetActive(false);
        }

        // 에디터 미리보기용 — 코루틴 없이 전환 배너를 그대로 띄운다.
        public void PreviewSplash(bool ally, PartyMember leader)
        {
            ShowSplash(ally, leader);
            if (_splashRoutine != null) StopCoroutine(_splashRoutine);
            _splash.SetActive(true);
            _splashGroup.alpha = 1f;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ───────────── 구성 ─────────────
        private BoardTopBar Build()
        {
            var canvasGO = new GameObject("TopBarCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);
            var root = canvasGO.transform;
            var theme = UITheme.Current;

            var bar = UGUIKit.CreateThemedPanel(root, "Bar", new Vector2(0.005f, 0.895f), new Vector2(0.995f, 0.995f), Gold, BarFill, 1.5f, theme.TopBarFrame);

            // 왼쪽: 파티장
            _leaderPortrait = new PortraitView(bar, "Leader", new Vector2(0.03f, 0.5f), 58f, Gold);
            _leaderName = Label(bar, "LeaderName", new Vector2(0.06f, 0.45f), new Vector2(0.2f, 0.95f), 15, TextAnchor.MiddleLeft, TextLight, true);
            Label(bar, "LeaderRole", new Vector2(0.06f, 0.05f), new Vector2(0.2f, 0.45f), 10, TextAnchor.MiddleLeft, TextDim, false).text = "파티장";

            // 피의 흔적 G — 10칸
            Label(bar, "BloodLabel", new Vector2(0.2f, 0.55f), new Vector2(0.43f, 0.98f), 11, TextAnchor.MiddleLeft, Blood, true).text = "피의 흔적";
            for (int i = 0; i < BloodNotches; i++)
            {
                float x0 = 0.2f + i * 0.0225f;
                var notch = UGUIKit.CreateImage(bar, $"Blood{i}", new Vector2(x0, 0.32f), new Vector2(x0 + 0.019f, 0.56f), NotchOff);
                UITheme.Apply(notch, theme.ThreatNotch, tint: true);
                _bloodNotches.Add(notch);
            }
            _bloodValue = Label(bar, "BloodValue", new Vector2(0.2f, 0.02f), new Vector2(0.43f, 0.32f), 10, TextAnchor.MiddleLeft, TextDim, false);

            // 가운데: 아군·적 턴 시계 (원을 반씩 채운 두 반원 + 가운데 라운드)
            var circle = CircleSprite();
            var dialGO = new GameObject("Dial", typeof(RectTransform));
            _dial = (RectTransform)dialGO.transform;
            _dial.SetParent(root, false);
            _dial.anchorMin = _dial.anchorMax = new Vector2(0.5f, 0.945f);
            _dial.sizeDelta = new Vector2(70f, 70f);
            var ring = UGUIKit.CreateImage(_dial, "Ring", Vector2.zero, Vector2.one, Gold);
            ring.sprite = circle;
            _allyHalf = HalfDisc(_dial, "Ally", circle, clockwise: false);
            _enemyHalf = HalfDisc(_dial, "Enemy", circle, clockwise: true);
            var hub = UGUIKit.CreateImage(_dial, "Hub", new Vector2(0.15f, 0.15f), new Vector2(0.85f, 0.85f), BarFill);
            hub.sprite = circle;
            _phaseLabel = Label(root, "Phase", new Vector2(0.5f, 0.945f), new Vector2(0.5f, 0.945f), 18, TextAnchor.MiddleCenter, AllyColor, true);
            ((RectTransform)_phaseLabel.transform).sizeDelta = new Vector2(60f, 24f);
            ((RectTransform)_phaseLabel.transform).anchoredPosition = new Vector2(0f, 5f);
            _roundLabel = Label(root, "Round", new Vector2(0.5f, 0.945f), new Vector2(0.5f, 0.945f), 10, TextAnchor.MiddleCenter, TextLight, false);
            ((RectTransform)_roundLabel.transform).sizeDelta = new Vector2(60f, 14f);
            ((RectTransform)_roundLabel.transform).anchoredPosition = new Vector2(0f, -11f);
            // 시계 양옆 — 지금 페이즈 쪽이 밝게 "아군 턴" / "적 턴"
            _allyTag = Label(root, "AllyTag", new Vector2(0.44f, 0.945f), new Vector2(0.44f, 0.945f), 13, TextAnchor.MiddleRight, AllyColor, true);
            _enemyTag = Label(root, "EnemyTag", new Vector2(0.56f, 0.945f), new Vector2(0.56f, 0.945f), 13, TextAnchor.MiddleLeft, EnemyColor, true);
            _allyTag.rectTransform.sizeDelta = new Vector2(70f, 20f);
            _enemyTag.rectTransform.sizeDelta = new Vector2(70f, 20f);

            // 영혼 수확 S — 연속 바 + 25·50·75 경고 눈금
            Label(bar, "SoulLabel", new Vector2(0.57f, 0.55f), new Vector2(0.82f, 0.98f), 11, TextAnchor.MiddleLeft, Soul, true).text = "영혼 수확";
            var soulBack = UGUIKit.CreateImage(bar, "SoulBar", new Vector2(0.57f, 0.34f), new Vector2(0.8f, 0.54f), NotchOff);
            UITheme.Apply(soulBack, theme.BarBack);
            var soulFill = UGUIKit.CreateImage(soulBack.transform, "Fill", Vector2.zero, new Vector2(0f, 1f), Soul);
            UITheme.Apply(soulFill, theme.BarFill, tint: true);
            _soulFill = soulFill.rectTransform;
            foreach (int w in SoulWarnings)
                UGUIKit.CreateImage(soulBack.transform, $"Warn{w}", new Vector2(w / 100f - 0.004f, -0.25f), new Vector2(w / 100f + 0.004f, 1.25f), Gold);
            _soulValue = Label(bar, "SoulValue", new Vector2(0.57f, 0.02f), new Vector2(0.82f, 0.32f), 10, TextAnchor.MiddleLeft, TextDim, false);

            // 오른쪽: Cent
            _cent = Label(bar, "Cent", new Vector2(0.84f, 0.1f), new Vector2(0.985f, 0.9f), 16, TextAnchor.MiddleRight, Gold, true);

            // 바 아래 가운데: 지역명 배너
            var regionFill = UGUIKit.CreateThemedPanel(root, "Region", new Vector2(0.42f, 0.855f), new Vector2(0.58f, 0.895f), Gold, new Color(0.2f, 0.08f, 0.1f, 0.95f), 1.5f, theme.RegionBanner);
            _region = Label(regionFill, "Name", Vector2.zero, Vector2.one, 12, TextAnchor.MiddleCenter, TextLight, true);

            BuildSplash(root);
            return this;
        }

        private void BuildSplash(Transform root)
        {
            var fill = UGUIKit.CreateThemedPanel(root, "TurnSplash", new Vector2(0.3f, 0.5f), new Vector2(0.7f, 0.66f), Gold, BarFill, 2.5f);
            _splash = fill.parent.gameObject;
            _splashGroup = _splash.AddComponent<CanvasGroup>();
            _splashGroup.blocksRaycasts = false;
            _splashFill = fill.GetComponent<Image>();
            _splashTitle = Label(fill, "Title", new Vector2(0f, 0.4f), new Vector2(1f, 0.95f), 34, TextAnchor.MiddleCenter, AllyColor, true);
            _splashTitle.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            _splashSub = Label(fill, "Sub", new Vector2(0f, 0.08f), new Vector2(1f, 0.42f), 14, TextAnchor.MiddleCenter, TextLight, false);
            _splash.SetActive(false);
        }

        // 원의 절반: clockwise=true → 위에서 시계 방향 180°(오른쪽 반원 = 적), false → 왼쪽 반원(아군).
        private static Image HalfDisc(Transform parent, string name, Sprite circle, bool clockwise)
        {
            var img = UGUIKit.CreateImage(parent, name, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), Inactive);
            img.sprite = circle;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillAmount = 0.5f;
            img.fillClockwise = clockwise;
            return img;
        }

        // 아트 없이 원을 그리기 위한 128px 원 스프라이트 (한 번만 생성).
        private static Sprite _circle;
        private static Sprite CircleSprite()
        {
            if (_circle != null) return _circle;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                byte a = (byte)(Mathf.Clamp01(r - d) * 255);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circle;
        }

        private static Text Label(Transform parent, string name, Vector2 min, Vector2 max, int size, TextAnchor align, Color color, bool bold)
        {
            var text = UGUIKit.CreateText(parent, name, min, max, "", size, align);
            text.color = color;
            if (bold) text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
