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
    public class BattleHud : MonoBehaviour
    {
        private const int TimelineLength = 10;  // 한 화면 칸 수 (UI 솔루션 Q6)
        public const int TimelineMax = 12;      // 이번 + 다음 라운드 최대 (아군 3 + 적 3) × 2 — 넘치면 넘겨 봄
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

        public bool UseFocus => _focusToggle != null && _focusToggle.isOn;

        // 자동 플레이 점검용 — 결과 창 버튼을 누른 것과 같다.
        public void DevPressResult() => _onResultContinue?.Invoke();

        // 자동 플레이 점검용 — 스킬 버튼을 누른 것과 같다 (스킬 메뉴 스크린샷).
        public void DevOpenSkills()
        {
            if (!_skillPanel.activeSelf) ToggleSkills();
        }

        // 자동 플레이 점검용 — "추가 집중" 토글을 누른 것과 같다.
        public void DevSetFocus(bool on)
        {
            if (_focusToggle != null && _focusToggle.interactable) _focusToggle.isOn = on;
        }

        private RectTransform _canvasRect;

        private class TimelineEntry { public GameObject Root; public Image Border; public RawImage Portrait; public Text Round; public Text Mark; public Text Speed; public Text Arrow; public Image Dashed; public GameObject Divider; public RectTransform Rect; }
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
            public Text Hp; public RectTransform HpFill; public StatusChipRow Statuses; public BattleUnit Unit;
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
        private Toggle _focusToggle;

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
            _timelineEntries = entries;
            _timelineRound = currentRound;
            RenderTimeline();
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
                var border = unit.IsAlly ? AllyBorder : EnemyBorder;
                slot.Border.color = predicted ? new Color(border.r, border.g, border.b, 0.15f) : border;
                slot.Dashed.enabled = predicted;
                slot.Dashed.color = border;
                slot.Portrait.color = predicted ? new Color(0.6f, 0.6f, 0.6f, 1f) : Color.white;
                // 다음 라운드가 시작되는 칸에 "전투 R3 예상" + 구분선 (속도 변화는 이 칸부터 반영된다).
                bool newRound = entries[idx].Round != prevRound;
                slot.Round.gameObject.SetActive(newRound);
                slot.Round.text = $"전투 R{entries[idx].Round} 예상";
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
                bar.Statuses.Show(StatusChipRow.EntriesFor(bar.Unit.Statuses, bar.Unit.HasEvasionState));
                bar.Root.GetComponent<CanvasGroup>().alpha = bar.Unit.IsAlive ? 1f : 0.35f;
            }
        }

        // ───────────── ③ 행동 메뉴 ─────────────
        // canRevive: 쓰러진 아군 중 LP가 남은 사람이 있으면 "부활" 활성 (D21).
        public void ShowActions(BattleUnit actor, bool canRevive = false)
        {
            _actionPanel.SetActive(true);
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
            _focusToggle.interactable = actor.Member.CurrentFocus > 0;
            _focusToggle.isOn = false;
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
            if (_skillPanel.activeSelf) { _skillPanel.SetActive(false); return; }
            _itemPanel.SetActive(false);

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
            if (_itemPanel.activeSelf) { _itemPanel.SetActive(false); return; }
            _skillPanel.SetActive(false);
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

        public void SetHint(string text) => _hintText.text = text;

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
        public IEnumerator PlayRoll(string title, SlotCheck.Result slots, bool focusUsed, string resultText, Color resultColor)
        {
            _rollPanel.SetActive(true);
            _rollTitle.text = title;
            _rollResult.text = "";
            BuildRollSlots(slots.SlotCount);

            // 집중력 확정 슬롯은 맨 앞에서 바로 성공으로, 나머지는 결과 개수만큼 섞어서 하나씩 공개.
            int forced = focusUsed ? 1 : 0;
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
            label.color = color;
            label.fontStyle = FontStyle.Bold;
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            var rect = (RectTransform)label.transform;
            rect.sizeDelta = new Vector2(220f, 44f);
            rect.anchorMin = rect.anchorMax = Vector2.zero;

            Vector2 screen = cam.WorldToScreenPoint(worldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            rect.anchoredPosition = local + _canvasRect.rect.size * 0.5f;
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
            _resultTitle.text = title;
            _resultBody.text = body;
            _onResultContinue = onContinue;
            _resultButtonLabel.text = buttonLabel;
            _resultPanel.SetActive(true);
        }

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
            var strip = UGUIKit.CreateThemedPanel(parent, "Timeline", new Vector2(0.27f, 0.915f), new Vector2(0.73f, 0.995f), Gold, PanelFill, 1.5f, UITheme.Current.TimelineFrame);
            _roundText = Label(strip, "Round", new Vector2(0f, 0f), new Vector2(0.11f, 1f), 11, TextAnchor.MiddleCenter, TextDim, bold: true);

            // 첫 칸(지금 차례)은 크게, 나머지는 작게.
            float x = 0.12f;
            for (int i = 0; i < TimelineLength; i++)
            {
                float w = i == 0 ? 0.105f : 0.078f;
                var entry = new TimelineEntry();
                var fill = UGUIKit.CreateThemedPanel(strip, $"Turn{i}", new Vector2(x, i == 0 ? 0.02f : 0.12f), new Vector2(x + w, i == 0 ? 0.98f : 0.88f),
                    AllyBorder, new Color(0.13f, 0.12f, 0.15f), i == 0 ? 3f : 2f, UITheme.Current.PortraitFrame);
                entry.Rect = (RectTransform)fill.parent;
                entry.Root = entry.Rect.gameObject;
                entry.Border = entry.Root.GetComponent<Image>();
                entry.Portrait = UGUIKit.CreateRect(fill, "Portrait", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                // 초상 아래: 이번 라운드 정렬에 쓴 속도 (다음 라운드 칸은 지금 속도 = 예상)
                entry.Speed = Label(entry.Rect, "Speed", new Vector2(-0.2f, -0.42f), new Vector2(1.2f, 0f), 11, TextAnchor.MiddleCenter, new Color(0.9f, 0.87f, 0.8f), bold: true);
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
                x += w + 0.008f;
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
                var fill = UGUIKit.CreateThemedPanel(parent, $"EnemyBar{i}", new Vector2(x0, 0.79f), new Vector2(x0 + 0.29f, 0.895f), Gold, PanelFill, 1.5f);
                var outer = (RectTransform)fill.parent;
                bar.Root = outer.gameObject;
                bar.Border = outer.GetComponent<Image>();
                bar.Root.AddComponent<CanvasGroup>();

                var portraitFill = UGUIKit.CreateThemedPanel(fill, "Portrait", new Vector2(0.01f, 0.06f), new Vector2(0.2f, 0.94f), EnemyBorder, new Color(0.13f, 0.12f, 0.15f), 1.5f, UITheme.Current.PortraitFrame);
                bar.Portrait = UGUIKit.CreateRect(portraitFill, "Image", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();

                bar.Name = Label(fill, "Name", new Vector2(0.23f, 0.55f), new Vector2(0.72f, 0.95f), 13, TextAnchor.MiddleLeft, TextLight, bold: true);
                bar.Badge = Label(fill, "Badge", new Vector2(0.23f, 0.05f), new Vector2(0.55f, 0.32f), 10, TextAnchor.MiddleLeft, TextDim, bold: false);
                bar.Hp = Label(fill, "Hp", new Vector2(0.73f, 0.35f), new Vector2(0.99f, 1f), 26, TextAnchor.MiddleRight, HpColor, bold: true);

                var back = UGUIKit.CreateImage(fill, "HpBar", new Vector2(0.23f, 0.36f), new Vector2(0.98f, 0.52f), new Color(0.16f, 0.15f, 0.17f));
                UITheme.Apply(back, UITheme.Current.BarBack, sliceHeight: 12f);
                var hpFill = UGUIKit.CreateImage(back.transform, "Fill", Vector2.zero, Vector2.one, HpColor);
                UITheme.Apply(hpFill, UITheme.Current.BarFill, tint: true, sliceHeight: 12f);
                bar.HpFill = (RectTransform)hpFill.transform;

                // 상태 칸 — 상시 특성(회피 상태) + 걸린 상태 효과. 그림은 StatusEffectDefinition.Icon.
                bar.Statuses = new StatusChipRow(fill, "Status", new Vector2(0.57f, 0.05f), new Vector2(0.99f, 0.33f), StatusIconSlots);

                // 보스 예고 — HP 바 바로 아래, 실행·저지·사망·전투 종료까지 유지 (Boss_Abel_v1.0 예고 UI)
                var tele = UGUIKit.CreateThemedPanel(parent, $"EnemyTelegraph{i}", new Vector2(x0, 0.665f), new Vector2(x0 + 0.29f, 0.785f), CritColor, new Color(0.18f, 0.08f, 0.08f, 0.95f), 1.5f);
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

            _focusToggle = BuildFocusToggle(panel, new Vector2(0.8f, 0.08f), new Vector2(0.98f, 0.62f));
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

        // "추가 집중" — 켜면 이번 공격의 슬롯 1개를 확정 성공 (집중력 1 소모, System.md).
        private Toggle BuildFocusToggle(Transform parent, Vector2 min, Vector2 max)
        {
            var fill = UGUIKit.CreateThemedPanel(parent, "FocusToggle", min, max, Gold, new Color(0.15f, 0.13f, 0.1f), 1.5f, UITheme.Current.Button);
            var toggle = fill.parent.gameObject.AddComponent<Toggle>();
            var check = UGUIKit.CreateImage(fill, "On", new Vector2(0.06f, 0.3f), new Vector2(0.26f, 0.7f), CritColor);
            var checkSprite = UITheme.Current.FocusToggleIcon != null ? UITheme.Current.FocusToggleIcon : UITheme.Current.FocusPip;
            if (!UITheme.Apply(check, checkSprite, tint: true))
                check.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            toggle.graphic = check;
            toggle.targetGraphic = fill.GetComponent<Image>();
            Label(fill, "Label", new Vector2(0.3f, 0f), new Vector2(1f, 1f), 11, TextAnchor.MiddleLeft, TextLight, bold: true).text = "추가 집중\n(슬롯 1 확정)";
            toggle.isOn = false;
            return toggle;
        }

        private void BuildRollPanel(Transform parent)
        {
            var panel = UGUIKit.CreateThemedPanel(parent, "RollPanel", new Vector2(0.03f, 0.58f), new Vector2(0.31f, 0.77f), Gold, PanelFill, 2f);
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
            _resultTitle = Label(panel, "Title", new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.97f), 26, TextAnchor.MiddleCenter, CritColor, bold: true);
            // 보상 줄이 많아도(아이템 3칸·퀘스트 물건·레벨 업) 잘리지 않게 글자 크기 자동 축소
            _resultBody = Label(panel, "Body", new Vector2(0.06f, 0.19f), new Vector2(0.94f, 0.81f), 12, TextAnchor.MiddleCenter, TextLight, bold: false);
            _resultBody.resizeTextForBestFit = true;
            _resultBody.resizeTextMinSize = 8;
            _resultBody.resizeTextMaxSize = 13;
            var button = UGUIKit.CreateThemedButton(panel, "Continue", new Vector2(0.3f, 0.04f), new Vector2(0.7f, 0.15f), "보드로 돌아가기", ButtonFill, 13);
            button.GetComponentInChildren<Text>().color = TextLight;
            _resultButtonLabel = button.GetComponentInChildren<Text>();
            button.onClick.AddListener(() => _onResultContinue?.Invoke());
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
