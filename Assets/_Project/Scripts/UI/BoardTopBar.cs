using System.Collections;
using Fantasia.Data;
using Fantasia.World;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 보드 화면 상단 바 — UI 솔루션 Q2·Q3·Q4 (Docs/Planning/UI_Solutions_Q1-Q9_2026-10-27.pdf) 배치:
    //   왼쪽: 파티장 초상 + 플레이어 이름 / "직업 · 파티장"
    //   피의 흔적 G: 아이콘 + "70 / 100" + 주의(50)·위험(80)·피의 사냥 진행 중 (10칸은 없앰)
    //   가운데 원: 해·달 (월드 라운드마다 아침→낮→저녁→새벽, 효과 없는 연출), 양옆: 현재 아군 턴 / 적 턴
    //   영혼 수확 S: 막대 + 25·50·75 눈금 / 오른쪽: 월드 R + Cent / 바 아래: 지역명. 턴이 바뀌면 가운데 큰 전환 배너.
    // G·S 값은 BoardSession에서 읽기만 한다. 그림은 UITheme(TopBarFrame·DaySun·NightMoon·RegionBanner 등), 비우면 단색.
    public class BoardTopBar : MonoBehaviour
    {
        private static BoardTopBar _instance;
        public static string DevBloodText => _instance != null && _instance._bloodValue != null ? _instance._bloodValue.text : "";

        private static readonly int[] SoulWarnings = { 25, 50, 75 }; // System.md: S 25·50·75 경고

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color BarFill = new Color(0.07f, 0.06f, 0.08f, 0.94f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.62f, 0.6f, 0.56f);
        private static readonly Color AllyColor = new Color(0.95f, 0.75f, 0.35f);
        private static readonly Color EnemyColor = new Color(0.55f, 0.45f, 0.85f);
        private static readonly Color Blood = new Color(0.85f, 0.2f, 0.22f);
        private static readonly Color Caution = new Color(0.95f, 0.65f, 0.25f);
        private static readonly Color Soul = new Color(0.4f, 0.75f, 0.85f);
        private static readonly Color NotchOff = new Color(0.18f, 0.16f, 0.19f);
        private static readonly Color DayColor = new Color(0.95f, 0.78f, 0.4f);   // 해 그림이 오기 전 임시 색
        private static readonly Color NightColor = new Color(0.25f, 0.27f, 0.45f); // 달 그림이 오기 전 임시 색

        private PortraitView _leaderPortrait;
        private Text _leaderName, _leaderRole;
        private Text _bloodValue;
        private int _lastBlood = -1; // 처음 갱신(불러오기 포함)에서는 알림을 띄우지 않는다
        private Image _skyFill, _skyArt;
        private Text _phaseLabel, _roundLabel, _allyTag, _enemyTag, _worldRound;
        private RectTransform _soulFill;
        private Image _soulFillImage; // 그림 게이지면 채움 양으로
        private bool _art;           // 상단 바 그림(N07 조각)을 쓰는지
        private Text _soulValue;
        private Text _cent;
        private Text _region;

        private GameObject _splash;
        private CanvasGroup _splashGroup;
        private Image _splashFill;
        private Text _splashTitle, _splashSub;
        private Coroutine _splashRoutine;
        private WorldPhase? _shownPhase;

        public static BoardTopBar Instance => _instance;

        public static BoardTopBar EnsureExists()
        {
            if (_instance == null) _instance = new GameObject("BoardTopBar").AddComponent<BoardTopBar>().Build();
            return _instance;
        }

        // 매 갱신: 이름·G·S·Cent·지역·해달·턴. 페이즈가 바뀌었으면 전환 배너(announce일 때).
        public void Refresh(WorldPhase phase, int round, PartyMember leader, int bloodTrail, int soulHarvest, int cent, string region, bool announce)
        {
            // 왼쪽: 플레이어 이름 / "직업 · 파티장" (Q4)
            var session = Fantasia.Core.BoardSession.Instance;
            _leaderName.text = session != null && !string.IsNullOrEmpty(session.PlayerName) ? session.PlayerName : Fantasia.Core.BoardSession.DefaultPlayerName;
            if (leader != null)
            {
                _leaderPortrait.Set(leader.Job.Portrait, PortraitStudio.TextureFor(leader.Job));
                _leaderRole.text = $"{leader.Job.DisplayName} · 파티장";
            }

            // 피의 흔적: 숫자 + 상태 (Q2·결정서 A5 "진행 중 / 유예 n라운드"). 50·80을 아래에서 위로 넘을 때만 알림 1번 —
            // 다시 열기·불러오기로는 반복 안 함. 사냥이 끝나 0이 되면 다음 주기에 다시 알림 (결정서 T3).
            bool hunting = session != null && session.Hunt.Active;
            int grace = session != null ? session.Hunt.GraceRounds : 0;
            string state = hunting ? "피의 사냥 진행 중" : grace > 0 ? $"유예 {grace}라운드" : bloodTrail >= 80 ? "위험" : bloodTrail >= 50 ? "주의" : "";
            _bloodValue.text = $"{bloodTrail} / 100{(state.Length > 0 ? "   " + state : "")}";
            _bloodValue.color = hunting || bloodTrail >= 80 ? Blood : bloodTrail >= 50 ? Caution : TextLight;
            if (_lastBlood >= 0 && !hunting)
            {
                if (_lastBlood < 80 && bloodTrail >= 80) ItemAcquiredToast.Show("피의 흔적 80 — 위험: 사신이 가까워진다");
                else if (_lastBlood < 50 && bloodTrail >= 50) ItemAcquiredToast.Show("피의 흔적 50 — 주의");
            }
            _lastBlood = bloodTrail;

            if (_soulFillImage != null) _soulFillImage.fillAmount = Mathf.Clamp01(soulHarvest / 100f);
            else _soulFill.anchorMax = new Vector2(Mathf.Clamp01(soulHarvest / 100f), 1f);
            _soulValue.text = $"{soulHarvest} / 100";
            _soulValue.color = soulHarvest >= 75 ? Soul : _art ? new Color(0.85f, 0.8f, 0.72f) : TextDim;

            _cent.text = $"{cent} Cent";
            // 해·달 그림이 있으면 시간대 이름은 지역명 칸에 함께 (원 안에는 그림만)
            _region.text = _art && UITheme.Current.DaySun != null ? $"{region} · {TimeOfDayNames[TimeOfDayIndex(round)]}" : region;
            _worldRound.text = $"월드 R{round}";

            // 가운데 원 = 해·달 (Q3) — 그림(N02 해·N01 달)이 없으면 색 원 + 글자
            int timeIndex = TimeOfDayIndex(round);
            bool day = timeIndex < 2;
            var theme = UITheme.Current;
            var sky = day ? theme.DaySun : theme.NightMoon;
            _skyArt.enabled = sky != null;
            _skyArt.sprite = sky;
            _skyFill.color = day ? DayColor : NightColor;
            _skyFill.enabled = sky == null;
            _phaseLabel.text = sky == null ? TimeOfDayNames[timeIndex] : "";
            _phaseLabel.color = day ? new Color(0.25f, 0.17f, 0.05f) : TextLight;
            _roundLabel.text = "";

            // 아군·적 턴은 원 양옆 따로 된 글자
            bool ally = phase == WorldPhase.AllyTurn;
            _allyTag.color = ally ? AllyColor : TextDim;
            _enemyTag.color = ally ? TextDim : EnemyColor;
            _allyTag.text = ally ? "현재: 아군 턴" : "아군";
            _enemyTag.text = ally ? "적" : "현재: 적 턴";

            if (_shownPhase != phase)
            {
                _shownPhase = phase;
                if (announce) ShowSplash(ally, leader);
            }
        }

        // 아침·낮·저녁·새벽 = (진행 중 월드 라운드 − 1) % 4. 전투 라운드·개인 턴으로는 바뀌지 않는다.
        public static readonly string[] TimeOfDayNames = { "아침", "낮", "저녁", "새벽" };
        public static int TimeOfDayIndex(int worldRound) => (Mathf.Max(1, worldRound) - 1) % 4;

        // 턴 전환 배너 — 화면 가운데 0.25초 페이드 인, 0.8초 유지, 0.35초 페이드 아웃.
        private void ShowSplash(bool ally, PartyMember leader)
        {
            Fantasia.Core.SfxPlayer.Play(ally ? Fantasia.Core.SfxIds.TurnAlly : Fantasia.Core.SfxIds.TurnEnemy);
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

            // 나무판 그림(N07에서 잘라 씀)이 있으면 그 그림, 없으면 단색 틀. 바 높이 = 720 기준 72 단위.
            _art = theme.TopBarFrame != null;
            Transform bar;
            if (_art)
            {
                var plank = UGUIKit.CreateImage(root, "Bar", new Vector2(0.005f, 0.895f), new Vector2(0.995f, 0.995f), Color.white);
                UITheme.Apply(plank, theme.TopBarFrame, sliceHeight: 72f);
                bar = plank.transform;
            }
            else bar = UGUIKit.CreateThemedPanel(root, "Bar", new Vector2(0.005f, 0.895f), new Vector2(0.995f, 0.995f), Gold, BarFill, 1.5f, theme.TopBarFrame);

            // 왼쪽: 파티장 초상 + 플레이어 이름 / 직업 · 파티장 — 그림이 있으면 나무판 왼쪽 네모 칸 안에 맞춤
            _leaderPortrait = _art ? new PortraitView(bar, "Leader", new Vector2(0.035f, 0.537f), 42f, Gold) : new PortraitView(bar, "Leader", new Vector2(0.03f, 0.5f), 58f, Gold);
            _leaderName = Label(bar, "PlayerName", new Vector2(0.06f, 0.45f), new Vector2(0.19f, 0.95f), 15, TextAnchor.MiddleLeft, TextLight, true);
            _leaderName.horizontalOverflow = HorizontalWrapMode.Wrap; // 긴 이름이 옆 칸을 넘지 않게 (10글자 제한)
            _leaderName.supportRichText = false; // 이름을 서식 태그로 해석하지 않음 (Q4)
            _leaderRole = Label(bar, "LeaderRole", new Vector2(0.06f, 0.05f), new Vector2(0.19f, 0.45f), 10, TextAnchor.MiddleLeft, TextDim, false);

            // 피의 흔적 G — 아이콘 + 이름 + 숫자·상태 (10칸 없음)
            bool bloodIcon = UGUIKit.CreateIcon(bar, "BloodIcon", new Vector2(0.198f, 0.5f), new Vector2(0.214f, 1f), theme.BloodTrailIcon) != null; // Z01
            Label(bar, "BloodLabel", new Vector2(bloodIcon ? 0.216f : 0.2f, 0.55f), new Vector2(0.43f, 0.98f), 11, TextAnchor.MiddleLeft, Blood, true).text = "피의 흔적";
            _bloodValue = Label(bar, "BloodValue", new Vector2(0.2f, 0.05f), new Vector2(0.43f, 0.55f), 15, TextAnchor.MiddleLeft, TextLight, true);

            // 가운데: 해·달 원 (테두리 + 색 원 또는 그림)
            var circle = CircleSprite();
            var dialGO = new GameObject("Sky", typeof(RectTransform));
            var dial = (RectTransform)dialGO.transform;
            dial.SetParent(root, false);
            dial.anchorMin = dial.anchorMax = new Vector2(0.5f, 0.945f);
            if (_art && theme.TopBarCircle != null)
            {
                // 디자인처럼 큰 원이 바 아래로 조금 내려와 지역명 칸 위에 걸침 (위쪽은 화면 밖으로 안 나가게)
                dial.sizeDelta = new Vector2(92f, 92f);
                dial.anchoredPosition = new Vector2(0f, -10f);
                UGUIKit.CreateImage(dial, "Ring", Vector2.zero, Vector2.one, Color.white).sprite = theme.TopBarCircle;
            }
            else
            {
                dial.sizeDelta = new Vector2(70f, 70f);
                UGUIKit.CreateImage(dial, "Ring", Vector2.zero, Vector2.one, Gold).sprite = circle;
            }
            _skyFill = UGUIKit.CreateImage(dial, "SkyFill", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f), DayColor);
            _skyFill.sprite = circle;
            _skyArt = UGUIKit.CreateImage(dial, "SkyArt", _art ? new Vector2(0.16f, 0.16f) : new Vector2(0.04f, 0.04f), _art ? new Vector2(0.84f, 0.84f) : new Vector2(0.96f, 0.96f), Color.white);
            _skyArt.preserveAspect = true;
            _skyArt.enabled = false;
            _phaseLabel = Label(root, "TimeOfDay", new Vector2(0.5f, 0.945f), new Vector2(0.5f, 0.945f), 15, TextAnchor.MiddleCenter, TextLight, true);
            ((RectTransform)_phaseLabel.transform).sizeDelta = new Vector2(60f, 24f);
            _roundLabel = Label(root, "TimeOfDaySmall", new Vector2(0.5f, 0.945f), new Vector2(0.5f, 0.945f), 10, TextAnchor.MiddleCenter, TextLight, false);
            ((RectTransform)_roundLabel.transform).sizeDelta = new Vector2(60f, 14f);
            ((RectTransform)_roundLabel.transform).anchoredPosition = new Vector2(0f, -28f);
            // 원 양옆 — 지금 차례 쪽이 밝게 "현재: 아군 턴" / "현재: 적 턴"
            float tagX = _art ? 0.042f : 0.036f; // 원이 커지면 글자를 조금 더 바깥으로
            _allyTag = Label(root, "AllyTag", new Vector2(0.5f - tagX, 0.945f), new Vector2(0.5f - tagX, 0.945f), 13, TextAnchor.MiddleRight, AllyColor, true);
            _enemyTag = Label(root, "EnemyTag", new Vector2(0.5f + tagX, 0.945f), new Vector2(0.5f + tagX, 0.945f), 13, TextAnchor.MiddleLeft, EnemyColor, true);
            _allyTag.rectTransform.pivot = new Vector2(1f, 0.5f);
            _enemyTag.rectTransform.pivot = new Vector2(0f, 0.5f);
            _allyTag.rectTransform.sizeDelta = new Vector2(110f, 20f);
            _enemyTag.rectTransform.sizeDelta = new Vector2(110f, 20f);

            // 영혼 수확 S — 연속 바 + 25·50·75 경고 눈금
            bool soulIcon = UGUIKit.CreateIcon(bar, "SoulIcon", new Vector2(0.608f, 0.5f), new Vector2(0.624f, 1f), theme.SoulIcon) != null; // Z02
            Label(bar, "SoulLabel", new Vector2(soulIcon ? 0.626f : 0.61f, 0.55f), new Vector2(0.83f, 0.98f), 11, TextAnchor.MiddleLeft, Soul, true).text = "영혼 수확";
            if (theme.SoulFrame != null && theme.SoulFill != null)
            {
                // 게이지 틀(N05, 25·50·75 칸막이 포함)을 원래 비율 그대로 + 채움(N06)은 틀 안쪽에 왼쪽부터 채움
                var area = UGUIKit.CreateRect(bar, "SoulBar", new Vector2(0.61f, 0.26f), new Vector2(0.82f, 0.6f));
                var holder = UGUIKit.CreateRect(area, "Holder", Vector2.zero, Vector2.one); // 틀 원래 비율로 맞춘 칸
                var fit = holder.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = theme.SoulFrame.rect.width / theme.SoulFrame.rect.height;
                // 틀 안쪽이 불투명해서 틀 먼저(뒤), 채움 나중(앞) — 채움 그림(N06)에 칸 나눔이 들어 있음. 위치는 원본 픽셀 기준 (좌우 1.6%, 아래 25.5%·위 22.4%)
                UGUIKit.CreateImage(holder, "Frame", Vector2.zero, Vector2.one, Color.white).sprite = theme.SoulFrame;
                var fill = UGUIKit.CreateImage(holder, "Fill", new Vector2(0.016f, 0.255f), new Vector2(0.984f, 0.776f), Soul);
                fill.sprite = theme.SoulFill;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillAmount = 0f;
                _soulFillImage = fill;
                _soulFill = fill.rectTransform;
            }
            else
            {
                var soulBack = UGUIKit.CreateImage(bar, "SoulBar", new Vector2(0.61f, 0.34f), new Vector2(0.82f, 0.54f), NotchOff);
                UITheme.Apply(soulBack, theme.BarBack, sliceHeight: 12f);
                var soulFill = UGUIKit.CreateImage(soulBack.transform, "Fill", Vector2.zero, new Vector2(0f, 1f), Soul);
                UITheme.Apply(soulFill, theme.BarFill, tint: true, sliceHeight: 12f);
                _soulFill = soulFill.rectTransform;
                foreach (int w in SoulWarnings)
                    UGUIKit.CreateImage(soulBack.transform, $"Warn{w}", new Vector2(w / 100f - 0.004f, -0.25f), new Vector2(w / 100f + 0.004f, 1.25f), Gold);
            }
            _soulValue = Label(bar, "SoulValue", new Vector2(0.61f, 0.02f), new Vector2(0.83f, 0.32f), 10, TextAnchor.MiddleLeft, TextDim, false);

            // 오른쪽: 월드 라운드(위) + Cent(아래)
            _worldRound = Label(bar, "WorldRound", new Vector2(0.87f, 0.52f), new Vector2(0.985f, 0.98f), 12, TextAnchor.MiddleRight, TextLight, true);
            UGUIKit.CreateIcon(bar, "CentIcon", new Vector2(0.84f, 0.06f), new Vector2(0.87f, 0.54f), theme.CentIcon); // Z03
            _cent = Label(bar, "Cent", new Vector2(0.87f, 0.04f), new Vector2(0.985f, 0.54f), 15, TextAnchor.MiddleRight, Gold, true);

            // 바 아래 가운데: 지역명 배너
            Transform regionFill;
            if (_art && theme.RegionBanner != null)
            {
                // 지역명 칸(N07 아래 칸) — 큰 원 아래에 붙여 디자인처럼 원이 위에 걸치게
                var tab = UGUIKit.CreateImage(root, "Region", new Vector2(0.43f, 0.845f), new Vector2(0.57f, 0.89f), Color.white);
                UITheme.Apply(tab, theme.RegionBanner, sliceHeight: 32f);
                tab.transform.SetSiblingIndex(dial.GetSiblingIndex()); // 원보다 뒤에 그림
                regionFill = tab.transform;
            }
            else regionFill = UGUIKit.CreateThemedPanel(root, "Region", new Vector2(0.42f, 0.855f), new Vector2(0.58f, 0.895f), Gold, new Color(0.2f, 0.08f, 0.1f, 0.95f), 1.5f, theme.RegionBanner);
            _region = Label(regionFill, "Name", Vector2.zero, Vector2.one, 12, TextAnchor.MiddleCenter, TextLight, true);
            if (_art) _region.rectTransform.offsetMin = new Vector2(0f, -6f); // 원이 위를 조금 가리니 글자는 아래쪽으로

            if (_art)
            {
                // 나무 바탕 위 글자가 잘 읽히게: 그림자 + 흐린 글자는 조금 밝게
                foreach (var t in bar.GetComponentsInChildren<Text>(true)) t.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);
                foreach (var t in new[] { _allyTag, _enemyTag, _region }) t.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);
                _leaderRole.color = new Color(0.85f, 0.8f, 0.72f);
            }
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
