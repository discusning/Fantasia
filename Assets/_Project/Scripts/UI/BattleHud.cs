using System;
using System.Collections;
using System.Collections.Generic;
using Fantasia.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 전투 화면 UI (참고: Docs/Concept_Image/Concept/Fantasia_인터페이스_사진원본/01_포더킹1_실제_스크린샷/
    // 05_턴제_전투와_공격대상_선택_한국어.jpg). 하단 파티 카드는 PartyHudPanel을 그대로 쓰고, 여기서는
    //   ① 상단 행동 순서 타임라인  ② 상단 적 HP 바  ③ 행동 메뉴 + 판정 미리보기
    //   ④ 슬롯 판정 연출  ⑤ 피해 숫자  ⑥ 승리·패배 창 을 그린다.
    // 틀·버튼·게이지·아이콘 그림은 UITheme, 초상은 PortraitStudio(데이터의 Model/Portrait) — 아트가
    // 들어오면 코드 수정 없이 바뀐다. 전투 진행은 BattleController가 이 클래스의 메서드를 부른다.
    public partial class BattleHud : MonoBehaviour
    {
        private const int TimelineLength = 10;  // 한 화면 칸 수 (UI 솔루션 Q6)
        public const int TimelineMax = TimelineLength; // 11/11: 띠를 늘 10칸 꽉 채움 (예상 라운드로 채워서 넘겨 보기 필요 없음)
        private const int MaxEnemyBars = 3;
        private const int StatusIconSlots = 4;
        private const float SecondsPerSlot = 0.3f;

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color AllyBorder = new Color(0.4f, 0.62f, 0.95f);
        private static readonly Color EnemyBorder = new Color(0.85f, 0.3f, 0.3f);
        private static readonly Color PanelFill = new Color(0.07f, 0.06f, 0.08f, 0.93f);
        private static readonly Color ButtonFill = new Color(0.36f, 0.12f, 0.14f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.62f, 0.6f, 0.56f);
        private static readonly Color HpColor = new Color(0.85f, 0.25f, 0.25f);
        private static readonly Color SlotUnknown = new Color(0.22f, 0.2f, 0.24f);
        public static readonly Color SlotSuccess = new Color(0.3f, 0.65f, 0.4f);
        public static readonly Color SlotFailure = new Color(0.7f, 0.25f, 0.25f);
        public static readonly Color CritColor = new Color(1f, 0.8f, 0.3f);
        public static readonly Color StatusColor = new Color(0.75f, 0.5f, 0.95f);

        // 행동 메뉴 버튼 이벤트 (BattleController가 구독)
        public event Action AttackClicked;
        public event Action DefendClicked;
        public event Action RetreatClicked;
        public event Action ReviveClicked;
        public event Action InterruptClicked; // 보스 예고 중 전용 행동 (봉합 방해)
        public event Action<BattleUnit> EnemyBarClicked;
        public event Action<Fantasia.Data.SkillDefinition> SkillChosen;
        public event Action<Fantasia.Data.ConsumableDefinition> ItemChosen;
        // 보스 봉쇄 — 퇴각 버튼 비활성 (BattleController가 행동 메뉴 열 때마다 갱신)
        public bool RetreatBlocked { get; set; }

        // 이번 행동에 쓸 집중 개수 (2026-11-06 변경: 켬/끔 1개 → 0~남은 집중력 중 플레이어가 고름).
        // 실제로 쓰는 개수는 BattleController가 이 공격의 슬롯 수·스킬 집중 비용에 맞춰 자른다.
        public int FocusCount { get; private set; }
        public event Action FocusChanged; // 대상 미리보기(크리티컬 확률) 갱신용

        // 자동 플레이 점검용 — 결과 창 버튼을 누른 것과 같다.
        public void DevPressResult() => _onResultContinue?.Invoke();

        // 자동 플레이 점검용 — 스킬 버튼을 누른 것과 같다 (스킬 메뉴 스크린샷).
        public void DevOpenSkills()
        {
            if (!_skillPanel.activeSelf) ToggleSkills();
        }

        // 자동 플레이 점검용·이어하기 — 집중 −/+ 버튼으로 개수를 맞춘 것과 같다.
        public void DevSetFocus(int count) => SetFocusCount(count);

        private void SetFocusCount(int count)
        {
            int max = _actionActor != null ? _actionActor.Member.CurrentFocus : 0;
            FocusCount = Mathf.Clamp(count, 0, max);
            RefreshFocusPicker();
            FocusChanged?.Invoke();
        }

        private void RefreshFocusPicker()
        {
            if (_focusLabel == null) return;
            int max = _actionActor != null ? _actionActor.Member.CurrentFocus : 0;
            _focusLabel.text = $"집중 사용\n{FocusCount} / {max}";
            _focusLabel.color = FocusCount > 0 ? CritColor : TextLight;
            _focusMinus.interactable = FocusCount > 0;
            _focusPlus.interactable = FocusCount < max;
        }

        private RectTransform _canvasRect;

        private class TimelineEntry { public GameObject Root; public Image Border; public RawImage Portrait; public Text Round; public Text Mark; public Text Speed; public Text Arrow; public Image Dashed; public Image Team; public Image FrameArt; public GameObject Divider; public RectTransform Rect; public bool IsAlly, Predicted; }
        private IReadOnlyList<BattleQueue.Entry> _timelineEntries;
        private int _timelineRound, _timelineOffset;
        private Button _timelinePrev, _timelineNext;
        private int _arrowRound;
        private Coroutine _arrowHide;
        private readonly Dictionary<BattleUnit, int> _arrows = new();
        private readonly List<TimelineEntry> _timeline = new();
        private Text _roundText;

        private class EnemyBar
        {
            public GameObject Root; public Image Border; public RawImage Portrait; public Text Name; public Text Badge;
            public Text Hp; public RectTransform HpFill; public StatusChipRow Statuses; public BattleUnit Unit; public Transform Plate;
            public GameObject Telegraph; public Text TelegraphText; public float FlashUntil;
        }
        private readonly List<EnemyBar> _enemyBars = new();

        private GameObject _actionPanel;
        private GameObject _skillPanel;
        private readonly List<(Button button, Image icon, Text name, Text sub)> _skillSlots = new();
        private readonly List<Fantasia.Data.SkillDefinition> _shownSkills = new();
        private GameObject _itemPanel;
        private readonly List<(Button button, Image icon, Text name, Text sub)> _itemSlots = new();
        private readonly List<Fantasia.Data.ConsumableDefinition> _shownItems = new();
        private BattleUnit _actionActor;
        private Text _actorText;
        private Text _hintText;
        private Button _attackButton, _skillButton, _itemButton, _defendButton, _reviveButton, _retreatButton;
        private Text _focusLabel;
        private Button _focusMinus, _focusPlus;

        private GameObject _rollPanel;
        private Text _rollTitle;
        private Text _rollResult;
        private RectTransform _rollSlotRow;
        private readonly List<Image> _rollSlots = new();
        private readonly List<Text> _rollSlotLabels = new();

        private GameObject _resultPanel;
        private Text _resultTitle;
        private Text _resultBody;
        private Text _resultButtonLabel;
        private Action _onResultContinue;
        private Action _onResultSecond;
        private GameObject _resultSecondButton;
        private RectTransform _resultButtonRect;
        private Text _resultSecondLabel;
        private RectTransform _resultCard, _resultBodyRect;
        private PanelDropView _victoryDrop, _defeatDrop, _shownDrop;
        private Coroutine _resultReveal;

        public static BattleHud Create()
        {
            var hud = new GameObject("BattleHud").AddComponent<BattleHud>();
            hud.Build();
            return hud;
        }

        // UI 위에 마우스가 있는지 — 전장 클릭(대상 선택)과 겹치지 않게.
        public static bool PointerOverUI => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // ───────────── ① 타임라인 ─────────────
        public void SetTimeline(IReadOnlyList<BattleQueue.Entry> entries, int currentRound, System.Func<BattleUnit, int> orderChange = null)
        {
            // 새 라운드가 시작될 때 ↑/↓를 캐릭터별로 정해 두고 잠깐 보여준다 (칸이 앞으로 밀려도 같은 캐릭터에 붙음)
            if (orderChange != null && currentRound != _arrowRound)
            {
                _arrowRound = currentRound;
                _timelineOffset = 0; // 새 라운드는 현재 차례부터 (UI 솔루션 Q6)
                _arrows.Clear();
                if (currentRound > 1)
                    foreach (var e in entries)
                        if (e.Round == currentRound && orderChange(e.Unit) != 0) _arrows[e.Unit] = orderChange(e.Unit);
                if (_arrowHide != null) StopCoroutine(_arrowHide);
                _arrowHide = _arrows.Count > 0 ? StartCoroutine(HideArrows(2.5f)) : null;
            }
            // 11/11 팀장 확인: 디자인 턴 순서 움직임(11/10 패키지) — 한 차례가 끝나 다음 사람이 맨 앞이 되면 칸이 왼쪽으로 한 칸 밀림
            bool advance = _timelineOffset == 0 && _shownUnits.Count > 1 && entries != null && entries.Count > 0
                           && entries[0].Unit == _shownUnits[1] && entries[0].Unit != _shownUnits[0] && _timeline[0].Root.activeSelf;
            GameObject leaving = advance ? Instantiate(_timeline[0].Root, _timeline[0].Rect.parent) : null; // 나가는 칸 (예전 내용 그대로 복사)
            // 11/11 팀장 요청: 라운드가 바뀔 때 예상 칸(흐림·점선)이 한 번에 밝은 틀로 바뀌던 것 → 왼쪽부터 한 칸씩 밝아짐
            bool newRound = _timelineRound > 0 && currentRound != _timelineRound;
            if (newRound) for (int i = 0; i < _reveal.Length; i++) _reveal[i] = 0f;
            _timelineEntries = entries;
            _timelineRound = currentRound;
            RenderTimeline();
            if (newRound)
            {
                if (_revealRoutine != null) StopCoroutine(_revealRoutine);
                _revealRoutine = StartCoroutine(RevealRound());
            }
            _shownUnits.Clear();
            if (entries != null) foreach (var e in entries) _shownUnits.Add(e.Unit);
            if (advance)
            {
                if (_slide != null) { StopCoroutine(_slide); if (_leaving != null) Destroy(_leaving); } // 앞 움직임이 안 끝났으면 정리하고 새로
                _slide = StartCoroutine(SlideTimeline(leaving));
            }
        }

        // 턴 순서 움직임 (디자인 패키지 README: 0.42초, 왼쪽으로 한 칸, 첫 칸 122% + 금 틀, 새 끝 칸은 오른쪽에서 같이 들어옴).
        // 칸 구조(10칸·점선·3D 초상)는 그대로 두고, 새 내용을 그린 뒤 각 칸을 "오른쪽 옆 칸 자리 → 제자리"로 옮겨 보이게 함.
        // 첫 칸은 작은 칸 자리에서 큰 칸 자리로 커짐 (큰 칸 0.08 / 작은 칸 0.065 = 123%).
        public const float TimelineSlideSeconds = 0.42f;
        private readonly List<BattleUnit> _shownUnits = new();
        private readonly List<Vector2> _homeMin = new(), _homeMax = new();
        private Coroutine _slide;
        private GameObject _leaving;
        public bool DevTimelineSliding => _slide != null;

        private IEnumerator SlideTimeline(GameObject leaving)
        {
            int n = _timeline.Count;
            Vector2 step = _homeMin[n - 1] - _homeMin[n - 2]; // 작은 칸 한 칸 간격
            var fromMin = new Vector2[n];
            var fromMax = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                fromMin[i] = i + 1 < n ? _homeMin[i + 1] : _homeMin[i] + step;
                fromMax[i] = i + 1 < n ? _homeMax[i + 1] : _homeMax[i] + step;
            }
            // 나가는 칸: 첫 칸 자리 → 왼쪽으로 한 칸, 작아지며 흐려짐
            RectTransform leaveRect = null;
            CanvasGroup leaveGroup = null;
            Vector2 leaveMin0 = _homeMin[0], leaveMax0 = _homeMax[0];
            Vector2 leaveSize = _homeMax[1] - _homeMin[1];
            Vector2 leaveCenter = (leaveMin0 + leaveMax0) * 0.5f - new Vector2(step.x, 0f);
            _leaving = leaving;
            if (leaving != null)
            {
                leaveRect = (RectTransform)leaving.transform;
                leaving.transform.SetAsFirstSibling(); // 다른 칸 뒤로
                leaveGroup = leaving.AddComponent<CanvasGroup>();
                leaveGroup.blocksRaycasts = false;
            }
            var lastGroup = _timeline[n - 1].Root.GetComponent<CanvasGroup>() ?? _timeline[n - 1].Root.AddComponent<CanvasGroup>();

            for (float t = 0f; t < TimelineSlideSeconds; t += Time.unscaledDeltaTime)
            {
                float k = 1f - Mathf.Pow(1f - t / TimelineSlideSeconds, 3f); // 끝에서 부드럽게 멈춤
                for (int i = 0; i < n; i++)
                {
                    _timeline[i].Rect.anchorMin = Vector2.Lerp(fromMin[i], _homeMin[i], k);
                    _timeline[i].Rect.anchorMax = Vector2.Lerp(fromMax[i], _homeMax[i], k);
                }
                lastGroup.alpha = k; // 새 끝 칸은 오른쪽에서 나타나며 들어옴
                if (leaveRect != null)
                {
                    Vector2 c = Vector2.Lerp((leaveMin0 + leaveMax0) * 0.5f, leaveCenter, k);
                    Vector2 s = Vector2.Lerp(leaveMax0 - leaveMin0, leaveSize * 0.8f, k);
                    leaveRect.anchorMin = c - s * 0.5f;
                    leaveRect.anchorMax = c + s * 0.5f;
                    leaveGroup.alpha = 1f - k;
                }
                yield return null;
            }
            for (int i = 0; i < n; i++) { _timeline[i].Rect.anchorMin = _homeMin[i]; _timeline[i].Rect.anchorMax = _homeMax[i]; }
            lastGroup.alpha = 1f;
            if (leaving != null) Destroy(leaving);
            _leaving = null;
            _slide = null;
        }

        // 타임라인 그리기 (UI 솔루션 Q6): 한 화면 최대 10칸 — 넘치면 ◀ ▶로 넘겨 본다 (자르지 않음).
        // 이번 라운드 = 밝게·실선, 다음 라운드 = 초상 60% 밝기 + 점선 테두리 + "전투 R3 예상" + 앞에 구분선.
        private void RenderTimeline()
        {
            var entries = _timelineEntries;
            if (entries == null) return;
            int currentRound = _timelineRound;
            _roundText.text = $"전투 R{currentRound}";
            int maxOffset = Mathf.Max(0, entries.Count - _timeline.Count);
            _timelineOffset = Mathf.Clamp(_timelineOffset, 0, maxOffset);
            _timelinePrev.gameObject.SetActive(maxOffset > 0);
            _timelineNext.gameObject.SetActive(maxOffset > 0);
            _timelinePrev.interactable = _timelineOffset > 0;
            _timelineNext.interactable = _timelineOffset < maxOffset;

            int prevRound = _timelineOffset > 0 ? entries[_timelineOffset - 1].Round : currentRound;
            int lastMark = -9; // 11/11 글자 겹침: 라운드가 짧아 표시가 붙으면 짧은 글자
            for (int i = 0; i < _timeline.Count; i++)
            {
                var slot = _timeline[i];
                int idx = _timelineOffset + i;
                bool has = idx < entries.Count;
                slot.Root.SetActive(has);
                if (!has) continue;

                var unit = entries[idx].Unit;
                slot.Portrait.texture = PortraitStudio.TextureFor(unit.Definition);
                bool predicted = entries[idx].Round != currentRound;
                slot.IsAlly = unit.IsAlly;
                slot.Predicted = predicted;
                ApplySlotLook(i);
                // 다음 라운드가 시작되는 칸에 "전투 R3 예상" + 구분선 (속도 변화는 이 칸부터 반영된다).
                bool newRound = entries[idx].Round != prevRound;
                slot.Round.gameObject.SetActive(newRound);
                slot.Round.text = newRound && i - lastMark <= 2 ? $"R{entries[idx].Round}" : $"전투 R{entries[idx].Round} 예상";
                if (newRound && i - lastMark <= 2 && lastMark >= 0) _timeline[lastMark].Round.text = _timeline[lastMark].Round.text.Replace("전투 ", "").Replace(" 예상", ""); // 앞 표시도 짧게
                if (newRound) lastMark = i;
                slot.Divider.SetActive(newRound);
                slot.Mark.gameObject.SetActive(unit.Boss != null && unit.Boss.HasPending && unit.IsAlive);
                slot.Speed.text = $"속도 {entries[idx].Speed}";
                bool arrow = !predicted && _arrows.ContainsKey(unit);
                slot.Arrow.gameObject.SetActive(arrow);
                if (arrow)
                {
                    slot.Arrow.text = _arrows[unit] > 0 ? "↑" : "↓";
                    slot.Arrow.color = _arrows[unit] > 0 ? SlotSuccess : EnemyBorder;
                }
                prevRound = entries[idx].Round;
            }
        }

        // 칸 모양: 예상 칸 = 초상 60%·틀 어둡게·점선 / 이번 라운드 칸 = 밝게. _reveal[i](0→1)로 그 사이를 부드럽게 바꿈.
        private readonly float[] _reveal = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        private Coroutine _revealRoutine;
        private const float RevealSeconds = 0.3f, RevealStagger = 0.07f;

        private void ApplySlotLook(int i)
        {
            var slot = _timeline[i];
            var border = slot.IsAlly ? AllyBorder : EnemyBorder;
            float t = slot.Predicted ? 0f : _reveal[i]; // 0 = 예상 모양, 1 = 이번 라운드 모양
            slot.Border.color = new Color(border.r, border.g, border.b, Mathf.Lerp(0.15f, 1f, t));
            slot.Dashed.enabled = t < 1f;
            slot.Dashed.color = new Color(border.r, border.g, border.b, 1f - t);
            if (slot.FrameArt != null) // 11/10 그림 틀: 바깥 단색 테두리 대신 아래 색 줄, 다음 라운드 칸은 틀을 어둡게
            {
                slot.Border.color = new Color(0f, 0f, 0f, 0f);
                slot.Team.color = new Color(border.r, border.g, border.b, Mathf.Lerp(0.45f, 1f, t));
                float f = Mathf.Lerp(0.55f, 1f, t);
                slot.FrameArt.color = new Color(f, f, f, 1f);
            }
            float p = Mathf.Lerp(0.6f, 1f, t);
            slot.Portrait.color = new Color(p, p, p, 1f);
        }

        private IEnumerator RevealRound()
        {
            float total = RevealSeconds + RevealStagger * (_reveal.Length - 1);
            for (float time = 0f; time < total; time += Time.unscaledDeltaTime)
            {
                for (int i = 0; i < _reveal.Length && i < _timeline.Count; i++)
                {
                    _reveal[i] = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - i * RevealStagger) / RevealSeconds));
                    if (_timeline[i].Root.activeSelf) ApplySlotLook(i);
                }
                yield return null;
            }
            for (int i = 0; i < _reveal.Length && i < _timeline.Count; i++) { _reveal[i] = 1f; if (_timeline[i].Root.activeSelf) ApplySlotLook(i); }
            _revealRoutine = null;
        }

        private void ScrollTimeline(int step)
        {
            _timelineOffset += step;
            RenderTimeline();
        }

        // 다음 라운드 칸 점선 테두리 (아트가 오기 전 코드로 그림). 9-slice라 칸 크기에 맞춰 늘어난다.
        private static Sprite _dashed;
        private static Sprite DashedFrame()
        {
            if (_dashed != null) return _dashed;
            const int size = 32, thick = 2, dash = 4, gap = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool edge = x < thick || y < thick || x >= size - thick || y >= size - thick;
                int along = (x < thick || x >= size - thick) ? y : x;
                bool on = edge && along % (dash + gap) < dash;
                px[y * size + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            tex.SetPixels32(px);
            tex.Apply();
            _dashed = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(7, 7, 7, 7));
            return _dashed;
        }

        private System.Collections.IEnumerator HideArrows(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            foreach (var t in _timeline) t.Arrow.gameObject.SetActive(false);
            _arrows.Clear();
            _arrowHide = null;
        }

        // ───────────── ② 적 HP 바 ─────────────
        public void SetEnemies(IReadOnlyList<BattleUnit> enemies)
        {
            for (int i = 0; i < _enemyBars.Count; i++)
            {
                var bar = _enemyBars[i];
                bar.Unit = i < enemies.Count ? enemies[i] : null;
                bar.Root.SetActive(bar.Unit != null);
                if (bar.Unit == null) continue;

                var e = bar.Unit.Enemy;
                bar.Portrait.texture = PortraitStudio.TextureFor(e);
                bar.Name.text = bar.Unit.DisplayName;
                bar.Badge.text = $"{SizeLabel(e.Size)} · {e.SlotCount}슬롯";

            }
            RefreshEnemies(null);
        }

        public void RefreshEnemies(BattleUnit highlighted)
        {
            foreach (var bar in _enemyBars)
            {
                if (bar.Unit == null) continue;
                bar.Hp.text = bar.Unit.CurrentHP.ToString();
                float ratio = bar.Unit.MaxHP > 0 ? Mathf.Clamp01((float)bar.Unit.CurrentHP / bar.Unit.MaxHP) : 0f;
                bar.HpFill.anchorMax = new Vector2(ratio, 1f);
                bar.Border.color = bar.Unit == highlighted ? CritColor : Gold;
                UGUIKit.SetPlateTint(bar.Plate, bar.Unit == highlighted ? new Color(1f, 0.62f, 0.55f) : UGUIKit.PlateNormal); // 11/11 U12: 판 그림이면 대상은 붉게
                bar.Statuses.Show(StatusChipRow.EntriesFor(bar.Unit.Statuses, bar.Unit.HasEvasionState));
                bar.Root.GetComponent<CanvasGroup>().alpha = bar.Unit.IsAlive ? 1f : 0.35f;
            }
        }

        // ───────────── ③ 행동 메뉴 ─────────────
        // canRevive: 쓰러진 아군 중 LP가 남은 사람이 있으면 "부활" 활성 (D21).
        public void ShowActions(BattleUnit actor, bool canRevive = false)
        {
            _actionPanel.SetActive(true);
            ClearChosen();
            _actorText.text = $"{actor.DisplayName}의 차례";
            _attackButton.interactable = actor.CanBasicAttack;
            _defendButton.interactable = true;
            _retreatButton.interactable = !RetreatBlocked;
            _reviveButton.interactable = canRevive;
            _actionActor = actor;
            _skillButton.interactable = System.Linq.Enumerable.Any(actor.ActiveSkills());
            _skillPanel.SetActive(false);
            _itemPanel.SetActive(false);
            _itemButton.interactable = BattleItems().Count > 0;
            FocusCount = 0; // 행동마다 0에서 시작
            RefreshFocusPicker();
            SetHint(actor.CanBasicAttack
                ? "행동을 고르세요. 공격은 대상 적을 클릭합니다."
                : $"MP 부족 — 기본 공격에 MP {actor.BasicAttackMPCost} 필요");
        }

        public void HideActions()
        {
            _actionPanel.SetActive(false);
            _skillPanel.SetActive(false);
            _itemPanel.SetActive(false);
        }

        // 스킬 버튼 → 이 캐릭터의 액티브 스킬 펼치기. 못 쓰는 스킬은 이유와 함께 비활성.
        private void ToggleSkills()
        {
            if (_actionActor == null) return;
            if (_skillPanel.activeSelf) { _skillPanel.SetActive(false); if (_chosen != ChosenKind.None) CancelRequested?.Invoke(); return; } // 11/11 칸을 접으면 고른 스킬도 취소
            _itemPanel.SetActive(false);
            if (_chosen == ChosenKind.Item) CancelRequested?.Invoke(); // 소모품을 고른 채 스킬 칸으로 바꾸면 소모품 취소

            _shownSkills.Clear();
            _shownSkills.AddRange(_actionActor.ActiveSkills());
            for (int i = 0; i < _skillSlots.Count; i++)
            {
                var slot = _skillSlots[i];
                bool has = i < _shownSkills.Count;
                slot.button.gameObject.SetActive(has);
                if (!has) continue;

                var skill = _shownSkills[i];
                string reason = _actionActor.CannotUseReason(skill);
                slot.button.interactable = reason == null;
                slot.icon.enabled = skill.Icon != null;
                slot.icon.sprite = skill.Icon;
                slot.name.text = skill.DisplayName;
                slot.sub.text = reason ?? SkillCostText(skill);
                slot.sub.color = reason == null ? TextDim : SlotFailure;
            }
            _skillPanel.SetActive(true);
            SetHint("스킬을 고르세요. 마우스를 올리면 설명이 나옵니다.");
        }

        // ── 소모품 (D17): 파티 인벤토리 중 전투에서 쓸 수 있는 것, 최대 8종 ──
        private static List<Fantasia.Data.PartyInventory.Stack> BattleItems()
        {
            var party = Fantasia.Core.BoardSession.Instance != null ? Fantasia.Core.BoardSession.Instance.Party : null;
            var list = new List<Fantasia.Data.PartyInventory.Stack>();
            if (party == null) return list;
            foreach (var s in party.Inventory.Consumables)
                if (s.Definition.UsableIn(Fantasia.Data.UseContext.Battle)) list.Add(s);
            return list;
        }

        private void ToggleItems()
        {
            if (_actionActor == null) return;
            if (_itemPanel.activeSelf) { _itemPanel.SetActive(false); if (_chosen != ChosenKind.None) CancelRequested?.Invoke(); return; }
            _skillPanel.SetActive(false);
            if (_chosen == ChosenKind.Skill) CancelRequested?.Invoke();
            var stacks = BattleItems();
            _shownItems.Clear();
            for (int i = 0; i < _itemSlots.Count; i++)
            {
                var slot = _itemSlots[i];
                bool has = i < stacks.Count;
                slot.button.gameObject.SetActive(has);
                if (!has) continue;
                var def = stacks[i].Definition;
                _shownItems.Add(def);
                slot.icon.enabled = def.Icon != null;
                slot.icon.sprite = def.Icon;
                slot.name.text = def.DisplayName;
                slot.sub.text = $"×{stacks[i].Count}";
            }
            _itemPanel.SetActive(true);
            SetHint("소모품을 고르세요 (개인 행동 1회). 마우스를 올리면 효과가 나옵니다.");
        }

        public static string SkillCostText(Fantasia.Data.SkillDefinition skill)
        {
            var parts = new List<string>();
            if (skill.FocusCost > 0) parts.Add($"집중 {skill.FocusCost}");
            if (skill.MPCost > 0) parts.Add($"MP {skill.MPCost}");
            if (skill.HPCost > 0) parts.Add($"HP {skill.HPCost}");
            if (skill.CooldownTurns > 0) parts.Add($"재사용 {skill.CooldownTurns}턴");
            return parts.Count > 0 ? string.Join(" · ", parts) : "비용 없음";
        }

        private void DescribeSkill(int index)
        {
            if (index >= _shownSkills.Count || _actionActor == null) return;
            var skill = _shownSkills[index];
            string reason = _actionActor.CannotUseReason(skill);
            SetHint($"{skill.DisplayName} — {skill.EffectDescription}{(reason != null ? $" ({reason})" : "")}");
        }

        public void SetHint(string text) { _hintText.text = text; ApplyHintStyle(); }

        // ── 보스 예고 (Boss_Abel_v1.0 예고 UI) ──
        private Button _interruptButton;
        private GameObject _notice;
        private Text _noticeText;
        private float _noticeUntil;

        // 예고 중 전용 행동 버튼. null이면 숨김. 집중력이 없어도 활성.
        public void SetInterrupt(string label)
        {
            _interruptButton.gameObject.SetActive(!string.IsNullOrEmpty(label));
            if (!string.IsNullOrEmpty(label)) _interruptButton.GetComponentInChildren<Text>().text = $"{label} (집중력 0)";
        }

        // HP 바 아래 예고 — null이면 해제. 시간으로 사라지지 않는다 (잠깐 띄운 "저지 성공"은 끝까지 보여준 뒤 반영).
        public void SetBossTelegraph(BattleUnit boss, string text)
        {
            var bar = _enemyBars.Find(b => b.Unit == boss);
            if (bar == null || Time.time < bar.FlashUntil) return;
            bar.Telegraph.SetActive(!string.IsNullOrEmpty(text));
            bar.TelegraphText.text = text ?? "";
        }

        public void FlashBossTelegraph(BattleUnit boss, string text, float seconds)
        {
            var bar = _enemyBars.Find(b => b.Unit == boss);
            if (bar == null) return;
            bar.FlashUntil = 0f;
            SetBossTelegraph(boss, text);
            bar.FlashUntil = Time.time + seconds;
            StartCoroutine(EndFlash(bar, seconds));
        }

        private IEnumerator EndFlash(EnemyBar bar, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (Time.time + 0.01f < bar.FlashUntil) yield break; // 그새 다른 표시가 덮음
            bar.FlashUntil = 0f;
            var p = bar.Unit != null && bar.Unit.Boss != null ? bar.Unit.Boss : null;
            if (p == null || !p.HasPending || !bar.Unit.IsAlive) { bar.Telegraph.SetActive(false); bar.TelegraphText.text = ""; }
        }

        // 하단 알림 (예고·결과 각 2초) — HP 바 아래 예고의 보조
        public void ShowNotice(string text, float seconds)
        {
            _noticeText.text = text;
            _notice.SetActive(true);
            _noticeUntil = Time.time + seconds;
        }

        private void Update()
        {
            if (_notice != null && _notice.activeSelf && Time.time >= _noticeUntil) _notice.SetActive(false);
        }

        // ── 보스 이름 배너 (등장 연출) + [연출 건너뛰기] ──
        private GameObject _banner;
        private Text _bannerAct, _bannerName;
        private Action _onBannerSkip;

        public void ShowBossBanner(string act, string name, float seconds, Action onSkip)
        {
            _bannerAct.text = act;
            _bannerName.text = name;
            _onBannerSkip = onSkip;
            _banner.SetActive(true);
        }

        public void HideBossBanner() => _banner.SetActive(false);

        private void BuildBanner(Transform parent)
        {
            var band = UGUIKit.CreateImage(parent, "BossBanner", new Vector2(0f, 0.5f), new Vector2(1f, 0.66f), new Color(0.05f, 0.02f, 0.02f, 0.88f));
            _banner = band.gameObject;
            if (UITheme.Current.Banner != null) // 11/11 U7 보스 등장 띠: 검은 띠 → 받은 나무 탭을 화면 가로로 (양 끝 구리 장식)
            {
                band.sprite = UITheme.Current.Banner;
                band.type = Image.Type.Sliced;
                band.pixelsPerUnitMultiplier = 0.9f;
                band.color = new Color(0.85f, 0.8f, 0.8f, 0.97f);
            }
            _bannerAct = Label(band.transform, "Act", new Vector2(0.2f, 0.62f), new Vector2(0.8f, 0.95f), 14, TextAnchor.MiddleCenter, CritColor, bold: true);
            _bannerName = Label(band.transform, "Name", new Vector2(0.15f, 0.08f), new Vector2(0.85f, 0.65f), 34, TextAnchor.MiddleCenter, TextLight, bold: true);
            var skip = UGUIKit.CreateThemedButton(band.transform, "Skip", new Vector2(0.84f, 0.1f), new Vector2(0.98f, 0.4f), "연출 건너뛰기", ButtonFill, 11);
            skip.onClick.AddListener(() => _onBannerSkip?.Invoke());
            _banner.SetActive(false);
        }

        // 자동 플레이 점검용
        public bool DevBannerVisible => _banner != null && _banner.activeSelf;
        public string DevTelegraphText(BattleUnit boss) { var bar = _enemyBars.Find(b => b.Unit == boss); return bar != null && bar.Telegraph.activeSelf ? bar.TelegraphText.text : null; }
        public bool DevInterruptVisible => _interruptButton != null && _interruptButton.gameObject.activeInHierarchy;
        public void DevPressInterrupt() => InterruptClicked?.Invoke();

        private void BuildNotice(Transform parent)
        {
            var fill = UGUIKit.CreateThemedPanel(parent, "Notice", new Vector2(0.3f, 0.4f), new Vector2(0.7f, 0.45f), CritColor, new Color(0.1f, 0.08f, 0.08f, 0.92f), 1.5f);
            _notice = fill.parent.gameObject;
            _noticeText = Label(fill, "Text", new Vector2(0.02f, 0f), new Vector2(0.98f, 1f), 12, TextAnchor.MiddleCenter, TextLight, bold: true);
            _notice.SetActive(false);
        }

        // ───────────── ④ 슬롯 판정 연출 ─────────────
        public IEnumerator PlayRoll(string title, SlotCheck.Result slots, int focusUsed, string resultText, Color resultColor)
        {
            _rollPanel.SetActive(true);
            _rollTitle.text = title;
            _rollResult.text = "";
            BuildRollSlots(slots.SlotCount);

            // 집중력 확정 슬롯은 맨 앞에서 바로 성공으로, 나머지는 결과 개수만큼 섞어서 하나씩 공개.
            int forced = Mathf.Clamp(focusUsed, 0, slots.SlotCount);
            var outcome = new bool[slots.SlotCount];
            for (int i = 0; i < slots.SuccessCount; i++) outcome[i] = true;
            for (int i = slots.SlotCount - 1; i > forced; i--)
            {
                int j = UnityEngine.Random.Range(forced, i + 1);
                (outcome[i], outcome[j]) = (outcome[j], outcome[i]);
            }

            for (int i = 0; i < slots.SlotCount; i++)
            {
                yield return new WaitForSeconds(i < forced ? 0.1f : SecondsPerSlot);
                _rollSlots[i].color = outcome[i] ? SlotSuccess : SlotFailure;
                Fantasia.Core.SfxPlayer.Play(outcome[i] ? Fantasia.Core.SfxIds.SlotSuccess : Fantasia.Core.SfxIds.SlotFail);
                _rollSlotLabels[i].text = i < forced ? "집중" : outcome[i] ? "성공" : "실패";
            }

            _rollResult.text = resultText;
            _rollResult.color = resultColor;
            yield return new WaitForSeconds(0.7f);
            _rollPanel.SetActive(false);
        }

        // ───────────── ⑤ 피해 숫자 ─────────────
        public void PopText(Camera cam, Vector3 worldPos, string text, Color color)
        {
            var label = UGUIKit.CreateText(_canvasRect, "Popup", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), text, 30, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Overflow; // 11/11 긴 팝업("방어 태세 · 보호막 8")이 줄바꿈되어 잘림
            label.color = color;
            label.fontStyle = FontStyle.Bold;
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            var rect = (RectTransform)label.transform;
            rect.sizeDelta = new Vector2(220f, 44f);
            rect.anchorMin = rect.anchorMax = Vector2.zero;

            Vector2 screen = cam.WorldToScreenPoint(worldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            var pos = local + _canvasRect.rect.size * 0.5f;
            // 11/11 UI 점검: 키 큰 보스 머리 위 글자("저지!" 등)가 위쪽 순서 띠와 겹침 → 떠오른 끝(+50)까지 띠(화면 높이 0.915) 아래에 머물게
            pos.y = Mathf.Min(pos.y, _canvasRect.rect.height * 0.9f - 22f - 50f);
            rect.anchoredPosition = pos;
            StartCoroutine(FloatAndFade(label, rect));
        }

        private static IEnumerator FloatAndFade(Text label, RectTransform rect)
        {
            var start = rect.anchoredPosition;
            var color = label.color;
            for (float t = 0f; t < 1.1f; t += Time.deltaTime)
            {
                rect.anchoredPosition = start + Vector2.up * (t * 50f);
                label.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(1.6f - t * 1.4f));
                yield return null;
            }
            Destroy(label.gameObject);
        }

        // ───────────── ⑥ 결과 창 ─────────────
        public void ShowResult(string title, string body, Action onContinue, string buttonLabel = "보드로 돌아가기")
        {
            HideActions();
            ClearShieldMarks(); // 11/12 팀장 지적: 방어 방패가 승리·패배 창 위에 남음 → 전투가 끝나면 지움
            UseCardLayout();
            _resultTitle.text = title;
            _resultBody.text = body;
            _onResultContinue = onContinue;
            _resultButtonLabel.text = buttonLabel;
            _onResultSecond = null;
            _resultSecondButton.SetActive(false);
            _resultButtonRect.anchorMin = new Vector2(0.3f, 0.04f);
            _resultButtonRect.anchorMax = new Vector2(0.7f, 0.15f);
            _resultPanel.SetActive(true);
        }

        // 버튼 2개 결과 창 (게임 오버: [새 게임] [메인 메뉴] — 1막 기획 결정서 06)
        public void ShowResult(string title, string body, string firstLabel, Action first, string secondLabel, Action second)
        {
            ShowResult(title, body, first, firstLabel);
            _onResultSecond = second;
            _resultSecondLabel.text = secondLabel;
            _resultSecondButton.SetActive(true);
            _resultButtonRect.anchorMin = new Vector2(0.08f, 0.04f);
            _resultButtonRect.anchorMax = new Vector2(0.48f, 0.15f);
        }

        // ⑥-2 판 떨어지는 결과 (보고서 11/3 B5·B6): 승리·패배·전멸.
        // 사슬 판에 제목 + 한 줄 요약, 자세한 보상은 판 아래 칸 (판이 닿으면 나타남), 버튼은 판이 멈춘 뒤 (약 1.7초).
        // 퇴각은 예전 결과 창 그대로 (ShowResult). 판 그림이 없으면 예전 결과 창으로.
        public void ShowPanelResult(bool victory, string title, string summary, string body, Action onContinue, string buttonLabel = "보드로 돌아가기",
            string secondLabel = null, Action second = null)
        {
            if (secondLabel != null) ShowResult(title, body, buttonLabel, onContinue, secondLabel, second);
            else ShowResult(title, body, onContinue, buttonLabel);
            EnsureResultDrops();
            var drop = victory ? _victoryDrop : _defeatDrop;
            if (drop == null) return;

            // 판 아래 칸: 제목 줄 없이 자세한 내용 + 버튼
            _resultTitle.gameObject.SetActive(false);
            _resultBodyRect.anchorMin = new Vector2(0.06f, 0.19f);
            _resultBodyRect.anchorMax = new Vector2(0.94f, 0.96f);
            _resultCard.anchorMin = _resultCard.anchorMax = new Vector2(0.5f, 1f);
            _resultCard.pivot = new Vector2(0.5f, 1f);
            _resultCard.sizeDelta = new Vector2(470f, 260f);
            _resultCard.anchoredPosition = new Vector2(0f, -(drop.BoardBottom + 6f));
            _resultCard.gameObject.SetActive(false);
            _shownDrop = drop;
            drop.Play(title, summary);
            if (_resultReveal != null) StopCoroutine(_resultReveal);
            _resultReveal = StartCoroutine(RevealResultCard(drop));
        }

        private IEnumerator RevealResultCard(PanelDropView drop)
        {
            bool second = _resultSecondButton.activeSelf;
            _resultButtonRect.gameObject.SetActive(false);
            _resultSecondButton.SetActive(false);
            float landAt = Time.unscaledTime + drop.Drop.startDelay + drop.Drop.fallDuration;
            yield return new WaitUntil(() => Time.unscaledTime >= landAt || !drop.Drop.IsPlaying);
            _resultCard.gameObject.SetActive(true); // 판이 닿는 순간 자세한 내용
            yield return new WaitUntil(() => !drop.Drop.IsPlaying);
            _resultButtonRect.gameObject.SetActive(true); // 판이 멈춘 뒤 버튼 (입력 두 번 방지)
            _resultSecondButton.SetActive(second);
            _resultReveal = null;
        }

        private void UseCardLayout()
        {
            if (_resultReveal != null) { StopCoroutine(_resultReveal); _resultReveal = null; }
            if (_shownDrop != null) { _shownDrop.Hide(); _shownDrop = null; }
            _resultTitle.gameObject.SetActive(true);
            _resultBodyRect.anchorMin = new Vector2(0.06f, 0.19f);
            _resultBodyRect.anchorMax = new Vector2(0.94f, 0.81f);
            _resultCard.anchorMin = new Vector2(0.32f, 0.27f);
            _resultCard.anchorMax = new Vector2(0.68f, 0.78f);
            _resultCard.pivot = new Vector2(0.5f, 0.5f);
            _resultCard.offsetMin = _resultCard.offsetMax = Vector2.zero;
            _resultCard.gameObject.SetActive(true);
            _resultButtonRect.gameObject.SetActive(true);
        }

        private void EnsureResultDrops()
        {
            if (_victoryDrop != null || _defeatDrop != null) return;
            _victoryDrop = PanelDropView.Create(_resultPanel.transform, PanelDropView.Victory, 640f, 0f, 34, 15);
            _defeatDrop = PanelDropView.Create(_resultPanel.transform, PanelDropView.Defeat, 640f, 0f, 34, 15);
        }

        // 11/11 UI 순서 규칙: 결과 판이 떠 있는 동안 알림은 기다림 (UIFlow)
        private static BattleHud _active;
        private void OnEnable() => _active = this;
        public static bool ResultOpen => _active != null && _active._resultPanel != null && _active._resultPanel.activeInHierarchy;

        public bool DevResultDropPlaying => _shownDrop != null && _shownDrop.Root.activeSelf;
        public string DevResultSummary => _shownDrop != null ? _shownDrop.Summary.text : "";
        public bool DevResultButtonsVisible => _resultCard != null && _resultCard.gameObject.activeInHierarchy && _resultButtonRect.gameObject.activeSelf;

        public void DevPressResultSecond() => _onResultSecond?.Invoke();
        public string DevResultTitle => _resultTitle != null ? _resultTitle.text : "";
        public string DevResultButtons => _resultButtonLabel == null ? "" : _resultSecondButton != null && _resultSecondButton.activeSelf ? $"{_resultButtonLabel.text} / {_resultSecondLabel.text}" : _resultButtonLabel.text;

        // ───────────── 구성 ─────────────
        private void Build()
        {
            var canvasGO = new GameObject("BattleCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);
            _canvasRect = (RectTransform)canvasGO.transform;

            BuildTimeline(canvasGO.transform);
            BuildEnemyBars(canvasGO.transform);
            BuildActionPanel(canvasGO.transform);
            BuildSkillPanel(canvasGO.transform);
            BuildItemPanel(canvasGO.transform);
            BuildRollPanel(canvasGO.transform);
            BuildResultPanel(canvasGO.transform);
            BuildNotice(canvasGO.transform);
            BuildBanner(canvasGO.transform);
        }

        private void BuildTimeline(Transform parent)
        {
            var strip = UGUIKit.CreateThemedPanel(parent, "Timeline", new Vector2(0.285f, 0.915f), new Vector2(0.715f, 0.995f), Gold, PanelFill, 1.5f, UITheme.Current.TimelineFrame);
            if (UITheme.Current.TimelineFrame != null) // 11/10 받은 순서 띠 바탕: 단색 테두리 숨기고 그림 높이에 맞춰 양 끝 징 크기 유지
            {
                strip.parent.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
                var stripImage = strip.GetComponent<Image>();
                ((RectTransform)strip).offsetMin = ((RectTransform)strip).offsetMax = Vector2.zero;
                stripImage.pixelsPerUnitMultiplier = 3f;
            }
            _roundText = Label(strip, "Round", new Vector2(0f, 0f), new Vector2(0.11f, 1f), 11, TextAnchor.MiddleCenter, TextDim, bold: true);

            // 첫 칸(지금 차례)은 크게, 나머지는 작게.
            float x = 0.12f;
            for (int i = 0; i < TimelineLength; i++)
            {
                // 11/11 팀장 요청: 칸 틀이 띠 위아래·옆 칸과 겹쳐 보여서 칸을 줄이고 간격을 넓힘 (틀 그림은 칸보다 10%씩 큼)
                float w = i == 0 ? 0.08f : 0.065f;
                var entry = new TimelineEntry();
                var fill = UGUIKit.CreateThemedPanel(strip, $"Turn{i}", new Vector2(x, i == 0 ? 0.14f : 0.22f), new Vector2(x + w, i == 0 ? 0.86f : 0.78f),
                    AllyBorder, new Color(0.13f, 0.12f, 0.15f), i == 0 ? 3f : 2f, UITheme.Current.PortraitFrame);
                entry.Rect = (RectTransform)fill.parent;
                entry.Root = entry.Rect.gameObject;
                entry.Border = entry.Root.GetComponent<Image>();
                entry.Portrait = UGUIKit.CreateRect(fill, "Portrait", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                // 11/10 턴 순서 그림: 초상 위에 청동(첫 칸은 금) 틀을 덮고, 아군/적 색은 칸 아래 짧은 줄로 남김
                var slotArt = i == 0 ? UITheme.Current.TimelineCurrent : UITheme.Current.TimelineSlot;
                if (slotArt != null)
                {
                    float tail = i == 0 ? 0.345f : 0f; // 금 틀 아래 뾰족 부분 (118×158 중 34px)
                    entry.FrameArt = UGUIKit.CreateImage(entry.Rect, "FrameArt", new Vector2(-0.1f, -0.1f - tail), new Vector2(1.1f, 1.1f), Color.white);
                    entry.FrameArt.sprite = slotArt;
                    entry.FrameArt.raycastTarget = false;
                    entry.Team = UGUIKit.CreateImage(entry.Rect, "Team", new Vector2(0.15f, i == 0 ? -0.5f : -0.2f), new Vector2(0.85f, i == 0 ? -0.44f : -0.12f), AllyBorder);
                    entry.Team.raycastTarget = false;
                }
                // 초상 아래: 이번 라운드 정렬에 쓴 속도 (다음 라운드 칸은 지금 속도 = 예상)
                entry.Speed = Label(entry.Rect, "Speed", new Vector2(-0.2f, -0.42f), new Vector2(1.2f, 0f), 11, TextAnchor.MiddleCenter, new Color(0.9f, 0.87f, 0.8f), bold: true);
                entry.Speed.gameObject.SetActive(false); // 11/10 팀장 요청: 순서 칸 아래 속도 글자 숨김 (속도는 로그·캐릭터 정보에 그대로)
                // 다음 라운드 시작 때 순서가 앞당겨지면 ↑, 늦춰지면 ↓ (잠깐)
                entry.Arrow = Label(entry.Rect, "Arrow", new Vector2(-0.2f, 0.5f), new Vector2(0.45f, 1.15f), 16, TextAnchor.MiddleCenter, SlotSuccess, bold: true);
                entry.Arrow.gameObject.SetActive(false);
                // 다음 라운드(예상) 칸: 점선 테두리 + 앞에 구분선 (Q6)
                entry.Dashed = UGUIKit.CreateImage(entry.Rect, "Dashed", Vector2.zero, Vector2.one, Color.white);
                entry.Dashed.sprite = DashedFrame();
                entry.Dashed.type = Image.Type.Tiled;
                entry.Dashed.raycastTarget = false;
                entry.Dashed.enabled = false;
                entry.Divider = UGUIKit.CreateImage(entry.Rect, "Divider", new Vector2(-0.09f, -0.15f), new Vector2(-0.06f, 1.15f), Gold).gameObject;
                entry.Divider.SetActive(false);
                entry.Round = Label(entry.Rect, "RoundMark", new Vector2(-0.6f, -0.85f), new Vector2(1.6f, -0.42f), 10, TextAnchor.MiddleCenter, Gold, bold: true);
                entry.Round.gameObject.SetActive(false);
                // 예고 중인 보스 표식
                entry.Mark = Label(entry.Rect, "TelegraphMark", new Vector2(0.55f, 0.55f), new Vector2(1.15f, 1.2f), 14, TextAnchor.MiddleCenter, CritColor, bold: true);
                entry.Mark.text = "!";
                entry.Mark.gameObject.SetActive(false);
                entry.Root.SetActive(false); // 행동 순서가 정해지기 전(전투 전 대화 등)에는 빈칸을 보이지 않음
                _timeline.Add(entry);
                _homeMin.Add(entry.Rect.anchorMin); // 턴 순서 움직임의 제자리
                _homeMax.Add(entry.Rect.anchorMax);
                x += w + 0.02f;
            }
            // 10칸이 넘으면 좌우로 넘겨 보기 (Q6) — 넘치지 않으면 숨김
            _timelinePrev = UGUIKit.CreateThemedButton(strip, "TimelinePrev", new Vector2(-0.05f, 0.15f), new Vector2(-0.008f, 0.85f), "◀", PanelFill, 12);
            _timelineNext = UGUIKit.CreateThemedButton(strip, "TimelineNext", new Vector2(1.008f, 0.15f), new Vector2(1.05f, 0.85f), "▶", PanelFill, 12);
            _timelinePrev.onClick.AddListener(() => ScrollTimeline(-1));
            _timelineNext.onClick.AddListener(() => ScrollTimeline(1));
            _timelinePrev.gameObject.SetActive(false);
            _timelineNext.gameObject.SetActive(false);
        }

        private void BuildEnemyBars(Transform parent)
        {
            for (int i = 0; i < MaxEnemyBars; i++)
            {
                float x0 = 0.03f + i * 0.325f;
                var bar = new EnemyBar();
                var fill = UGUIKit.CreateThemedPanel(parent, $"EnemyBar{i}", new Vector2(x0, 0.775f), new Vector2(x0 + 0.29f, 0.88f), Gold, PanelFill, 1.5f); // 11/11 겹침: 순서 띠 화살표·예상 글자와 띄움
                var outer = (RectTransform)fill.parent;
                bar.Root = outer.gameObject;
                bar.Border = outer.GetComponent<Image>();
                bar.Plate = fill;
                bar.Root.AddComponent<CanvasGroup>();

                var portraitFill = UGUIKit.CreateThemedPanel(fill, "Portrait", new Vector2(0.01f, 0.06f), new Vector2(0.2f, 0.94f), EnemyBorder, new Color(0.13f, 0.12f, 0.15f), 1.5f, UITheme.Current.PortraitFrame);
                bar.Portrait = UGUIKit.CreateRect(portraitFill, "Image", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                if (UGUIKit.AddPortraitFrame((RectTransform)portraitFill.parent, 2.5f) != null) portraitFill.parent.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // 11/11 U4 얼굴 액자

                bar.Name = Label(fill, "Name", new Vector2(0.23f, 0.55f), new Vector2(0.72f, 0.95f), 13, TextAnchor.MiddleLeft, TextLight, bold: true);
                bar.Badge = Label(fill, "Badge", new Vector2(0.23f, 0.05f), new Vector2(0.55f, 0.32f), 10, TextAnchor.MiddleLeft, TextDim, bold: false);
                bar.Hp = Label(fill, "Hp", new Vector2(0.73f, 0.5f), new Vector2(0.97f, 0.95f), 24, TextAnchor.MiddleRight, HpColor, bold: true);

                // 11/11 팀장 결정: 적 HP도 같은 바 틀(N21) — 왼쪽 칸에 하트(Z07)
                var framed = FramedBar.Create(fill, "HpBar", new Vector2(0.23f, 0.33f), new Vector2(0.98f, 0.55f), HpColor, out var framedFill);
                if (framed != null)
                {
                    if (UITheme.Current.VitalityIcon != null)
                    {
                        var icon = UGUIKit.CreateImage(framed.Slot, "Icon", new Vector2(0.12f, 0.08f), new Vector2(0.88f, 0.92f), Color.white);
                        icon.sprite = UITheme.Current.VitalityIcon;
                        icon.preserveAspect = true;
                        icon.raycastTarget = false;
                    }
                    bar.HpFill = framedFill;
                }
                else
                {
                    var back = UGUIKit.CreateImage(fill, "HpBar", new Vector2(0.23f, 0.36f), new Vector2(0.98f, 0.52f), new Color(0.16f, 0.15f, 0.17f));
                    UITheme.Apply(back, UITheme.Current.BarBack, sliceHeight: 12f);
                    var hpFill = UGUIKit.CreateImage(back.transform, "Fill", Vector2.zero, Vector2.one, HpColor);
                    UITheme.Apply(hpFill, UITheme.Current.BarFill, tint: true, sliceHeight: 12f);
                    bar.HpFill = (RectTransform)hpFill.transform;
                }

                // 상태 칸 — 상시 특성(회피 상태) + 걸린 상태 효과. 그림은 StatusEffectDefinition.Icon.
                bar.Statuses = new StatusChipRow(fill, "Status", new Vector2(0.57f, 0.05f), new Vector2(0.99f, 0.33f), StatusIconSlots);

                // 보스 예고 — HP 바 바로 아래, 실행·저지·사망·전투 종료까지 유지 (Boss_Abel_v1.0 예고 UI)
                var tele = UGUIKit.CreateThemedPanel(parent, $"EnemyTelegraph{i}", new Vector2(x0, 0.64f), new Vector2(x0 + 0.29f, 0.76f), CritColor, new Color(0.18f, 0.08f, 0.08f, 0.95f), 1.5f);
                bar.Telegraph = tele.parent.gameObject;
                bar.TelegraphText = Label(tele, "Text", new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.96f), 10, TextAnchor.MiddleLeft, TextLight, bold: false);
                bar.TelegraphText.resizeTextForBestFit = true;
                bar.TelegraphText.resizeTextMinSize = 8;
                bar.TelegraphText.resizeTextMaxSize = 11;
                bar.Telegraph.SetActive(false);

                // 적 바를 눌러도 공격 대상 선택 (전장 클릭과 같은 효과)
                var button = bar.Root.AddComponent<Button>();
                button.targetGraphic = bar.Border;
                var captured = bar;
                button.onClick.AddListener(Fantasia.Core.SfxPlayer.Click);
                button.onClick.AddListener(() => { if (captured.Unit != null) EnemyBarClicked?.Invoke(captured.Unit); });
                _enemyBars.Add(bar);
            }
        }

        private void BuildActionPanel(Transform parent)
        {
            var panel = UGUIKit.CreateThemedPanel(parent, "ActionPanel", new Vector2(0.26f, 0.255f), new Vector2(0.74f, 0.39f), Gold, PanelFill, 2f);
            _actionPanel = panel.parent.gameObject;

            _actorText = Label(panel, "Actor", new Vector2(0.02f, 0.68f), new Vector2(0.2f, 0.98f), 13, TextAnchor.MiddleLeft, Gold, bold: true);
            _hintText = Label(panel, "Hint", new Vector2(0.45f, 0.68f), new Vector2(0.99f, 0.98f), 10, TextAnchor.MiddleRight, TextLight, bold: false);
            // 보스 예고 중에만 보이는 전용 행동 (봉합 방해 — 집중력 0, 행동 1회)
            _interruptButton = UGUIKit.CreateThemedButton(panel, "InterruptButton", new Vector2(0.2f, 0.67f), new Vector2(0.44f, 0.97f), "", new Color(0.55f, 0.35f, 0.12f), 11);
            _interruptButton.onClick.AddListener(() => InterruptClicked?.Invoke());
            _interruptButton.gameObject.SetActive(false);

            var theme = UITheme.Current;
            var defs = new (string label, Sprite icon)[]
            {
                ("공격", theme.AttackIcon), ("스킬", theme.SkillIcon), ("소모품", theme.ItemIcon), ("방어", theme.DefendIcon), ("부활", theme.ReviveIcon), ("퇴각", theme.RetreatIcon),
            };
            var buttons = new Button[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                float x0 = 0.02f + i * 0.128f;
                buttons[i] = UGUIKit.CreateThemedButton(panel, $"{defs[i].label}Button", new Vector2(x0, 0.08f), new Vector2(x0 + 0.12f, 0.62f), defs[i].label, ButtonFill, 13, defs[i].icon);
                buttons[i].GetComponentInChildren<Text>().color = TextLight;
            }
            (_attackButton, _skillButton, _itemButton, _defendButton, _reviveButton, _retreatButton) = (buttons[0], buttons[1], buttons[2], buttons[3], buttons[4], buttons[5]);
            _attackButton.onClick.AddListener(() => AttackClicked?.Invoke());
            _defendButton.onClick.AddListener(() => DefendClicked?.Invoke());
            _reviveButton.onClick.AddListener(() => ReviveClicked?.Invoke());
            _retreatButton.onClick.AddListener(() => RetreatClicked?.Invoke());
            _skillButton.onClick.AddListener(ToggleSkills);
            _itemButton.onClick.AddListener(ToggleItems);

            BuildFocusPicker(panel, new Vector2(0.8f, 0.08f), new Vector2(0.98f, 0.62f));
            _actionPanel.SetActive(false);
        }

        // 스킬 고르기 칸 — 행동 메뉴 바로 위, 최대 3개 (System.md 1차 3직업은 액티브 3개씩).
        private void BuildSkillPanel(Transform parent)
        {
            var panel = UGUIKit.CreateThemedPanel(parent, "SkillPanel", new Vector2(0.26f, 0.395f), new Vector2(0.74f, 0.5f), Gold, PanelFill, 2f);
            _skillPanel = panel.parent.gameObject;
            for (int i = 0; i < 3; i++)
            {
                float x0 = 0.015f + i * 0.33f;
                var button = UGUIKit.CreateThemedButton(panel, $"Skill{i}", new Vector2(x0, 0.1f), new Vector2(x0 + 0.315f, 0.9f), "", ButtonFill, 12);
                var fill = button.targetGraphic.transform;
                var icon = UGUIKit.CreateImage(fill, "Icon", new Vector2(0.03f, 0.1f), new Vector2(0.22f, 0.9f), Color.white);
                icon.preserveAspect = true;
                icon.enabled = false;
                var name = Label(fill, "Name", new Vector2(0.24f, 0.45f), new Vector2(0.98f, 0.98f), 13, TextAnchor.MiddleLeft, TextLight, bold: true);
                var sub = Label(fill, "Sub", new Vector2(0.24f, 0.02f), new Vector2(0.98f, 0.48f), 10, TextAnchor.MiddleLeft, TextDim, bold: false);
                int index = i;
                button.onClick.AddListener(() => { if (index < _shownSkills.Count) SkillChosen?.Invoke(_shownSkills[index]); });
                var trigger = button.gameObject.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                entry.callback.AddListener(_ => DescribeSkill(index));
                trigger.triggers.Add(entry);
                _skillSlots.Add((button, icon, name, sub));
            }
            _skillPanel.SetActive(false);
        }

        // 소모품 고르기 칸 — 행동 메뉴 위, 2줄 × 4칸
        private void BuildItemPanel(Transform parent)
        {
            var panel = UGUIKit.CreateThemedPanel(parent, "ItemPanel", new Vector2(0.26f, 0.395f), new Vector2(0.74f, 0.585f), Gold, PanelFill, 2f);
            _itemPanel = panel.parent.gameObject;
            for (int i = 0; i < 8; i++)
            {
                int row = i / 4, col = i % 4;
                float x0 = 0.012f + col * 0.247f, y1 = row == 0 ? 0.95f : 0.48f;
                var button = UGUIKit.CreateThemedButton(panel, $"Item{i}", new Vector2(x0, y1 - 0.43f), new Vector2(x0 + 0.235f, y1), "", ButtonFill, 11);
                var fill = button.targetGraphic.transform;
                var icon = UGUIKit.CreateImage(fill, "Icon", new Vector2(0.03f, 0.1f), new Vector2(0.25f, 0.9f), Color.white);
                icon.preserveAspect = true;
                icon.enabled = false;
                var name = Label(fill, "Name", new Vector2(0.27f, 0.42f), new Vector2(0.98f, 0.98f), 11, TextAnchor.MiddleLeft, TextLight, bold: true);
                var sub = Label(fill, "Sub", new Vector2(0.27f, 0.02f), new Vector2(0.98f, 0.45f), 10, TextAnchor.MiddleLeft, TextDim, bold: false);
                int index = i;
                button.onClick.AddListener(() =>
                {
                    if (index >= _shownItems.Count) return;
                    _itemPanel.SetActive(false);
                    ItemChosen?.Invoke(_shownItems[index]);
                });
                var trigger = button.gameObject.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                entry.callback.AddListener(_ => { if (index < _shownItems.Count) SetHint($"{_shownItems[index].DisplayName} — {_shownItems[index].EffectDescription}"); });
                trigger.triggers.Add(entry);
                _itemSlots.Add((button, icon, name, sub));
            }
            _itemPanel.SetActive(false);
        }

        public void DevOpenItems()
        {
            if (!_itemPanel.activeSelf) ToggleItems();
        }

        // 집중 사용 개수 고르기 (2026-11-06 변경) — [−] 개수 [+]. 1개당 이번 판정 슬롯 1개를 확정 성공, 집중력 1 소모.
        // 슬롯을 전부 확정하면 크리티컬 확정. 슬롯 수보다 많이 골라도 슬롯 수만큼만 쓴다.
        private void BuildFocusPicker(Transform parent, Vector2 min, Vector2 max)
        {
            var fill = UGUIKit.CreateThemedPanel(parent, "FocusPicker", min, max, Gold, new Color(0.15f, 0.13f, 0.1f), 1.5f, UITheme.Current.Button);
            _focusMinus = UGUIKit.CreateThemedButton(fill, "FocusMinus", new Vector2(0.03f, 0.15f), new Vector2(0.27f, 0.85f), "−", ButtonFill, 18);
            _focusPlus = UGUIKit.CreateThemedButton(fill, "FocusPlus", new Vector2(0.73f, 0.15f), new Vector2(0.97f, 0.85f), "+", ButtonFill, 18);
            foreach (var b in new[] { _focusMinus, _focusPlus }) b.GetComponentInChildren<Text>().color = TextLight;
            _focusMinus.onClick.AddListener(() => SetFocusCount(FocusCount - 1));
            _focusPlus.onClick.AddListener(() => SetFocusCount(FocusCount + 1));
            _focusLabel = Label(fill, "Label", new Vector2(0.27f, 0f), new Vector2(0.73f, 1f), 11, TextAnchor.MiddleCenter, TextLight, bold: true);
            RefreshFocusPicker();
        }

        private void BuildRollPanel(Transform parent)
        {
            var panel = UGUIKit.CreateThemedPanel(parent, "RollPanel", new Vector2(0.03f, 0.42f), new Vector2(0.31f, 0.61f), Gold, PanelFill, 2f); // 11/11 겹침: 적 카드를 덮던 굴림 창을 아래로
            _rollPanel = panel.parent.gameObject;
            _rollTitle = Label(panel, "Title", new Vector2(0.03f, 0.72f), new Vector2(0.97f, 0.97f), 13, TextAnchor.MiddleCenter, TextLight, bold: true);
            _rollSlotRow = UGUIKit.CreateRect(panel, "Slots", new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.72f));
            _rollResult = Label(panel, "Result", new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.3f), 16, TextAnchor.MiddleCenter, TextLight, bold: true);
            _rollPanel.SetActive(false);
        }

        private void BuildRollSlots(int count)
        {
            foreach (Transform child in _rollSlotRow) Destroy(child.gameObject);
            _rollSlots.Clear();
            _rollSlotLabels.Clear();

            const float size = 34f, gap = 22f;
            float total = count * size + (count - 1) * gap;
            for (int i = 0; i < count; i++)
            {
                var icon = UGUIKit.CreateImage(_rollSlotRow, $"Slot{i}", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), SlotUnknown);
                var rect = icon.rectTransform;
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = new Vector2(-total / 2f + size / 2f + i * (size + gap), 0f);
                if (!UITheme.Apply(icon, UITheme.Current.SlotIcon, tint: true)) rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                icon.color = SlotUnknown;

                var label = UGUIKit.CreateText(_rollSlotRow, $"SlotLabel{i}", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "?", 11, TextAnchor.MiddleCenter);
                label.rectTransform.sizeDelta = new Vector2(size + 16f, size);
                label.rectTransform.anchoredPosition = rect.anchoredPosition;
                label.color = TextLight;
                label.fontStyle = FontStyle.Bold;
                _rollSlots.Add(icon);
                _rollSlotLabels.Add(label);
            }
        }

        private void BuildResultPanel(Transform parent)
        {
            var dim = UGUIKit.CreateImage(parent, "ResultRoot", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.5f));
            _resultPanel = dim.gameObject;
            var panel = UGUIKit.CreateThemedPanel(dim.transform, "ResultCard", new Vector2(0.32f, 0.27f), new Vector2(0.68f, 0.78f), Gold, PanelFill, 2.5f);
            _resultCard = (RectTransform)panel.parent;
            _resultTitle = Label(panel, "Title", new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.97f), 26, TextAnchor.MiddleCenter, CritColor, bold: true);
            // 보상 줄이 많아도(아이템 3칸·퀘스트 물건·레벨 업) 잘리지 않게 글자 크기 자동 축소
            _resultBody = Label(panel, "Body", new Vector2(0.06f, 0.19f), new Vector2(0.94f, 0.81f), 12, TextAnchor.MiddleCenter, TextLight, bold: false);
            _resultBody.resizeTextForBestFit = true;
            _resultBody.resizeTextMinSize = 8;
            _resultBody.resizeTextMaxSize = 13;
            _resultBodyRect = _resultBody.rectTransform;
            var button = UGUIKit.CreateThemedButton(panel, "Continue", new Vector2(0.3f, 0.04f), new Vector2(0.7f, 0.15f), "보드로 돌아가기", ButtonFill, 13);
            button.GetComponentInChildren<Text>().color = TextLight;
            _resultButtonLabel = button.GetComponentInChildren<Text>();
            button.onClick.AddListener(() => _onResultContinue?.Invoke());
            _resultButtonRect = (RectTransform)button.transform;
            var second = UGUIKit.CreateThemedButton(panel, "Second", new Vector2(0.52f, 0.04f), new Vector2(0.92f, 0.15f), "메인 메뉴", ButtonFill, 13);
            _resultSecondLabel = second.GetComponentInChildren<Text>();
            _resultSecondLabel.color = TextLight;
            second.onClick.AddListener(() => _onResultSecond?.Invoke());
            _resultSecondButton = second.gameObject;
            _resultSecondButton.SetActive(false);
            _resultPanel.SetActive(false);
        }

        private static Text Label(Transform parent, string name, Vector2 min, Vector2 max, int size, TextAnchor align, Color color, bool bold)
        {
            var text = UGUIKit.CreateText(parent, name, min, max, "", size, align);
            text.color = color;
            if (bold) text.fontStyle = FontStyle.Bold;
            return text;
        }

        private static string SizeLabel(Fantasia.Data.EnemySize size) => size switch
        {
            Fantasia.Data.EnemySize.Small => "소형",
            Fantasia.Data.EnemySize.Medium => "중형",
            Fantasia.Data.EnemySize.Large => "대형",
            _ => "보스",
        };
    }
}
