using System;
using System.Collections;
using System.Collections.Generic;
using Fantasia.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // HUD 구성 (처음 한 번 만들기) (11/15 긴 파일 정리: BattleHud.cs에서 옮김, 내용 그대로)
    public partial class BattleHud
    {
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
            BuildDescCard(canvasGO.transform);
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

                // 11/15 팀장 "그런 특성이면 몬스터 카드에 간단히 적어 줘" — 카드 바로 아래 한 줄 (회피 상태 적만)
                var traitPanel = UGUIKit.CreateThemedPanel(parent, $"EnemyTrait{i}", new Vector2(x0, 0.742f), new Vector2(x0 + 0.29f, 0.772f), new Color(0.62f, 0.8f, 0.95f, 0.8f), new Color(0.08f, 0.1f, 0.14f, 0.92f), 1f);
                bar.Trait = traitPanel.parent.gameObject;
                bar.TraitText = Label(traitPanel, "Text", new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 11, TextAnchor.MiddleLeft, new Color(0.75f, 0.88f, 1f), bold: false);
                bar.TraitText.supportRichText = true;
                bar.Trait.SetActive(false);

                // 보스 예고 — HP 바 바로 아래, 실행·저지·사망·전투 종료까지 유지 (Boss_Abel_v1.0 예고 UI)
                var tele = UGUIKit.CreateThemedPanel(parent, $"EnemyTelegraph{i}", new Vector2(x0, 0.615f), new Vector2(x0 + 0.29f, 0.735f), CritColor, new Color(0.18f, 0.08f, 0.08f, 0.95f), 1.5f);
                bar.Telegraph = tele.parent.gameObject;
                bar.TelegraphText = Label(tele, "Text", new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.96f), 10, TextAnchor.MiddleLeft, TextLight, bold: false);
                bar.TelegraphText.resizeTextForBestFit = true;
                bar.TelegraphText.resizeTextMinSize = 9;
                bar.TelegraphText.resizeTextMaxSize = 12;
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

            _actorText = Label(panel, "Actor", new Vector2(0.025f, 0.68f), new Vector2(0.2f, 0.96f), 13, TextAnchor.MiddleLeft, Gold, bold: true);
            _hintText = Label(panel, "Hint", new Vector2(0.45f, 0.68f), new Vector2(0.975f, 0.96f), 10, TextAnchor.MiddleRight, TextLight, bold: false);
            _hintText.resizeTextForBestFit = true; _hintText.resizeTextMinSize = 8; _hintText.resizeTextMaxSize = 10; // 11/14 팀장 QA: 긴 안내가 판 테두리 밖으로 → 판 안쪽에서 끝내고 길면 줄임
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
                var u13 = Resources.Load<Sprite>("UI/ActionIcons/" + defs[i].label); // 11/14 디자인 U13 행동 아이콘 (없으면 예전 테마 아이콘)
                buttons[i] = UGUIKit.CreateThemedButton(panel, $"{defs[i].label}Button", new Vector2(x0, 0.08f), new Vector2(x0 + 0.12f, 0.62f), defs[i].label, ButtonFill, 13, u13 != null ? u13 : defs[i].icon);
                var btnLabel = buttons[i].GetComponentInChildren<Text>();
                btnLabel.color = TextLight;
                // 11/14 팀장 QA "선택지 글자가 튀어나옴": 글자가 버튼 아래 틀에 걸림 → 아이콘·글자를 틀 안쪽으로
                var iconT = btnLabel.transform.parent.Find("Icon") as RectTransform;
                if (iconT != null) { iconT.anchorMin = new Vector2(0.22f, 0.4f); iconT.anchorMax = new Vector2(0.78f, 0.9f); }
                var lr = btnLabel.rectTransform;
                lr.anchorMin = new Vector2(0.05f, 0.1f); lr.anchorMax = new Vector2(0.95f, 0.4f);
                lr.offsetMin = lr.offsetMax = Vector2.zero;
                btnLabel.fontSize = 12;
                btnLabel.alignment = TextAnchor.MiddleCenter;
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
                entry.callback.AddListener(_ => { if (index < _shownItems.Count) ShowDesc(ItemDescText(_shownItems[index]), overItems: true); });
                trigger.triggers.Add(entry);
                _itemSlots.Add((button, icon, name, sub));
            }
            _itemPanel.SetActive(false);
        }

        public void DevOpenItems()
        {
            if (!_itemPanel.activeSelf) ToggleItems();
        }

        // 집중 사용 개수 고르기 (2026-11-06 변경) — 1개당 이번 판정 슬롯 1개를 확정 성공, 집중력 1 소모.
        // 슬롯을 전부 확정하면 크리티컬 확정. 슬롯 수보다 많이 골라도 슬롯 수만큼만 쓴다.
        // 11/13 팀장: 집중 그림(활성화=붉은 보석·비활성화=금 테두리)을 눌러 고름 — n번째 보석을 누르면 n개, 같은 보석을 다시 누르면 하나 줄임.
        // 11/13 팀장 2차: 버튼 그림 틀 안에 끼어 답답함 → 얇은 금 테두리 칸, 위 줄 "집중 · 개수", 아래 줄 큰 보석.
        private void BuildFocusPicker(Transform parent, Vector2 min, Vector2 max)
        {
            // 11/13 팀장 3차: 구조는 좋음, 우리 UI(나무) 느낌으로 → 바깥은 다른 판과 같은 나무 테두리 판, 위 줄은 지역 이름 칸의 나무 탭
            var fill = UGUIKit.CreateThemedPanel(parent, "FocusPicker", min, max, Gold, new Color(0.15f, 0.13f, 0.1f), 1.5f);
            // 11/13 팀장: 나무 탭 그림(위 장식·주황 테두리 빛)이 글자 뒤를 가려 읽기 어려움 → 어두운 나무색 띠 + 아래 얇은 금선, 글자에 그림자
            var header = UGUIKit.CreateImage(fill, "Header", new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.94f), new Color(0.11f, 0.07f, 0.04f, 0.92f));
            header.raycastTarget = false;
            var underline = UGUIKit.CreateImage(header.transform, "Underline", new Vector2(0.04f, 0f), new Vector2(0.96f, 0.06f), new Color(Gold.r, Gold.g, Gold.b, 0.6f));
            underline.raycastTarget = false;
            _focusLabel = Label(header.transform, "Label", new Vector2(0f, 0f), new Vector2(1f, 1f), 13, TextAnchor.MiddleCenter, Gold, bold: true); // 11/13 팀장: 가운데로
            _focusLabel.text = "집중";
            _focusLabel.verticalOverflow = VerticalWrapMode.Overflow; _focusLabel.horizontalOverflow = HorizontalWrapMode.Overflow; // 11/13 팀장: 작은 창에서 칸이 낮아지면 글자가 통째로 숨던 문제
            _focusCountText = Label(header.transform, "Count", new Vector2(0.5f, 0f), new Vector2(0.92f, 1f), 13, TextAnchor.MiddleRight, TextLight, bold: true);
            _focusCountText.gameObject.SetActive(false); // 11/13 팀장: "0/3" 숫자는 필요 없음 (보석으로 보임)
            foreach (var t in new[] { _focusLabel, _focusCountText }) { var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.9f); sh.effectDistance = new Vector2(1f, -1f); }
            var row = UGUIKit.CreateRect(fill, "Gems", new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.58f));
            _focusRow = row;
            _focusGems.Clear();
            for (int i = 0; i < Fantasia.Data.PartyMember.AbsoluteMaxFocus; i++)
            {
                int index = i;
                var gem = UGUIKit.CreateImage(row, $"Gem{i}", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Color.white);
                gem.preserveAspect = true;
                gem.rectTransform.sizeDelta = new Vector2(FocusGemW, FocusGemH);
                var btn = gem.gameObject.AddComponent<Button>();
                btn.targetGraphic = gem;
                var colors = btn.colors; colors.highlightedColor = new Color(1f, 0.92f, 0.75f); colors.pressedColor = new Color(0.8f, 0.75f, 0.7f); btn.colors = colors;
                btn.onClick.AddListener(() => SetFocusCount(FocusCount == index + 1 ? index : index + 1));
                _focusGems.Add(gem);
            }
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
            _resultBody.resizeTextMinSize = 10;
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
