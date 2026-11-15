using System;
using System.Collections;
using System.Collections.Generic;
using Fantasia.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // ② 적 HP 바 · ③ 행동 메뉴·소모품·보스 예고·이름 배너 (11/15 긴 파일 정리: BattleHud.cs에서 옮김, 내용 그대로)
    public partial class BattleHud
    {
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
                bar.Portrait.texture = PortraitStudio.FaceFor(e);
                bar.Name.text = bar.Unit.DisplayName;
                bar.Badge.text = $"{SizeLabel(e.Size)} · {e.SlotCount}슬롯";
                bar.Trait.SetActive(e.HasEvasionState);

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
                bar.Trait.SetActive(bar.Unit.Enemy.HasEvasionState && bar.Unit.IsAlive);
                if (bar.Trait.activeSelf) bar.TraitText.text = bar.Unit.HasEvasionState ? "<b>회피</b> · 크리티컬 아니면 피해 절반" : "<b>회피 풀림</b> · 모든 공격 정상 피해";
                bar.Root.GetComponent<CanvasGroup>().alpha = bar.Unit.IsAlive ? 1f : 0.35f;
            }
        }

        // ───────────── ③ 행동 메뉴 ─────────────
        // canRevive: 쓰러진 아군 중 LP가 남은 사람이 있으면 "부활" 활성 (D21).
        public void ShowActions(BattleUnit actor, bool canRevive = false)
        {
            var swA = System.Diagnostics.Stopwatch.StartNew(); try {
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
            // 11/15 팀장 "버튼 몇 개가 눌리지 않음": 못 쓰는 버튼(부활·소모품·퇴각 등)이 쓸 수 있는 버튼과 똑같이 보였음
            // → 흐리게 + 마우스를 올리거나 누르면 아래 안내 줄에 이유
            SetActionState(_attackButton, actor.CanBasicAttack, $"MP 부족 — 기본 공격에 MP {actor.BasicAttackMPCost} 필요");
            SetActionState(_skillButton, _skillButton.interactable, "쓸 수 있는 스킬이 없습니다");
            SetActionState(_itemButton, _itemButton.interactable, "전투에서 쓸 소모품이 없습니다 (가방·빠른 칸 확인)");
            SetActionState(_defendButton, true, null);
            SetActionState(_reviveButton, canRevive, "부활: 쓰러진 아군이 없거나 LP가 남지 않았습니다");
            SetActionState(_retreatButton, !RetreatBlocked, "이 전투에서는 퇴각할 수 없습니다");
            FocusCount = 0; // 행동마다 0에서 시작
            RefreshFocusPicker();
            SetHint(actor.CanBasicAttack
                ? "행동을 고르세요. 공격은 대상 적을 클릭합니다."
                : $"MP 부족 — 기본 공격에 MP {actor.BasicAttackMPCost} 필요");
            } finally { if (swA.ElapsedMilliseconds > 30) Debug.Log($"[행동 메뉴 켜기 시간] {swA.ElapsedMilliseconds}ms"); }
        }


        private readonly Dictionary<Button, string> _disabledReason = new();
        private readonly Dictionary<Graphic, Color> _origColor = new();
        private void SetActionState(Button b, bool on, string reason)
        {
            b.interactable = on;
            var cg = b.GetComponent<CanvasGroup>(); if (cg == null) cg = b.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = on ? 1f : 0.75f;
            // 11/15 팀장 "못 쓰는 버튼은 회색빛으로": 버튼 안 그림·글자를 회색으로 (원래 색은 기억해 두고 쓸 수 있게 되면 되돌림)
            foreach (var g in b.GetComponentsInChildren<Graphic>(true))
            {
                if (!_origColor.TryGetValue(g, out var orig)) _origColor[g] = orig = g.color;
                float lum = orig.r * 0.3f + orig.g * 0.59f + orig.b * 0.11f;
                g.color = on ? orig : new Color(lum * 0.75f, lum * 0.75f, lum * 0.78f, orig.a);
            }
            cg.blocksRaycasts = true; // 못 써도 마우스는 받아서 이유를 보여 줌
            _disabledReason[b] = on ? null : reason;
            if (b.GetComponent<DisabledReasonHover>() == null)
                b.gameObject.AddComponent<DisabledReasonHover>().Init(() => _disabledReason.TryGetValue(b, out var r) ? r : null, SetHintTemp);
        }

        private string _hintBeforeTemp;
        private void SetHintTemp(string text)
        {
            if (text != null) { if (_hintBeforeTemp == null) _hintBeforeTemp = _hintText.text; SetHint(text); }
            else if (_hintBeforeTemp != null) { SetHint(_hintBeforeTemp); _hintBeforeTemp = null; }
        }

        // 11/15 자동 점검: 행동 버튼 가운데를 눌렀을 때 맨 위에 맞는 것이 그 버튼인지 (다른 UI가 덮고 있지 않은지)
        public string DevActionButtonCover()
        {
            var es = EventSystem.current; if (es == null) return "EventSystem 없음";
            var sb = new System.Text.StringBuilder();
            foreach (var b in new[] { _attackButton, _skillButton, _itemButton, _defendButton, _reviveButton, _retreatButton })
            {
                var rt = (RectTransform)b.transform; var corners = new Vector3[4]; rt.GetWorldCorners(corners);
                var canvas = b.GetComponentInParent<Canvas>().rootCanvas;
                Vector2 center = (corners[0] + corners[2]) * 0.5f;
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) center = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, center);
                var hits = new List<RaycastResult>(); es.RaycastAll(new PointerEventData(es) { position = center }, hits);
                bool ok = hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(b.transform);
                sb.Append($"{b.name}:{(ok ? "O" : "덮임(" + (hits.Count > 0 ? hits[0].gameObject.name : "없음") + ")")}{(b.interactable ? "" : "·못씀")} ");
            }
            return sb.ToString();
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
                string reason = _actionActor.CannotUseReason(skill, FocusCount);
                slot.button.interactable = reason == null;
                slot.icon.enabled = skill.Icon != null;
                slot.icon.sprite = skill.Icon;
                slot.name.text = skill.DisplayName;
                slot.sub.text = reason ?? SkillCostText(skill);
                slot.sub.color = reason == null ? TextDim : SlotFailure;
            }
            _skillPanel.SetActive(true);
            SetHint("스킬을 고르세요. 마우스를 올리면 설명이 바뀝니다.");
            if (_shownSkills.Count > 0) DescribeSkill(0); // 칸을 열면 첫 스킬 설명부터 보임
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
            SetHint("소모품을 고르세요 (개인 행동 1회). 마우스를 올리면 설명이 바뀝니다.");
            if (_shownItems.Count > 0) ShowDesc(ItemDescText(_shownItems[0]), overItems: true);
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
            string reason = _actionActor.CannotUseReason(skill, FocusCount);
            ShowDesc(SkillDescText(skill, reason), overItems: false); // 11/14 설명은 칸 위 카드로 (BattleHud.Desc)
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
            if (!string.IsNullOrEmpty(label)) { var it = _interruptButton.GetComponentInChildren<Text>(); it.text = $"{label} · 판정"; it.resizeTextForBestFit = true; it.resizeTextMinSize = 8; it.resizeTextMaxSize = 11; it.horizontalOverflow = HorizontalWrapMode.Wrap; } // 11/14 팀장 QA: 글자가 버튼 밖으로 → 짧게 ("집중 1이면 확정"은 예고 줄에 있음)
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
            string before = bar.Telegraph.activeSelf ? bar.TelegraphText.text : ""; // 저지 실패 뒤 예고 글 되돌리기용
            bar.FlashUntil = 0f;
            SetBossTelegraph(boss, text);
            bar.FlashUntil = Time.time + seconds;
            StartCoroutine(EndFlash(bar, seconds, before));
        }

        private IEnumerator EndFlash(EnemyBar bar, float seconds, string before = "")
        {
            yield return new WaitForSeconds(seconds);
            if (Time.time + 0.01f < bar.FlashUntil) yield break; // 그새 다른 표시가 덮음
            bar.FlashUntil = 0f;
            var p = bar.Unit != null && bar.Unit.Boss != null ? bar.Unit.Boss : null;
            if (p == null || !p.HasPending || !bar.Unit.IsAlive) { bar.Telegraph.SetActive(false); bar.TelegraphText.text = ""; }
            else if (!p.PendingCancelled && !string.IsNullOrEmpty(before)) { bar.Telegraph.SetActive(true); bar.TelegraphText.text = before; } // 저지 실패: 예고 그대로
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
            HideDescIfClosed();
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
    }
}
