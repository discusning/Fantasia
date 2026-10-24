using System;
using System.Collections;
using System.Collections.Generic;
using Fantasia.Combat;
using Fantasia.Data;
using Fantasia.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // D08 조우 선택창 — 싸우기 / 기습 / 몰래 지나가기 / 후퇴 (System.md "전투" + 확률표).
    // 레이아웃은 Docs/Concept_Image/Concept/판타지아_싸우기기습몰래지나가기후퇴.png(참고 자료)의
    // 구성을 따른다: 왼쪽에 적 초상 + 이름 배너 + 선택지 설명 + 슬롯 아이콘 + 버튼 4개 +
    // 하단 문구, 오른쪽에 성공 개수별 확률표. 아트가 없어 초상은 마름모 틀 + 크기 배지로 대신한다.
    //
    // 기습·몰래 지나가기는 버튼을 누르면 슬롯을 하나씩 공개한 뒤 결과를 잠깐 보여주고 닫힌다.
    // 결과 처리(전투 진입, 안전 타일 복귀 등)는 Show에 넘긴 콜백 쪽 책임.
    //
    // Screen Space - Overlay + DontDestroyOnLoad, LandmarkInfoPanel과 같은 이유.
    public class EncounterPanel : MonoBehaviour
    {
        private enum Choice { Fight, Ambush, Sneak, Retreat }

        public static EncounterPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

        private const float SecondsPerSlotReveal = 0.35f;
        private const float SecondsResultHold = 1.2f;

        private static readonly Color BorderGold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color PanelFill = new Color(0.07f, 0.06f, 0.08f, 0.95f);
        private static readonly Color ButtonFill = new Color(0.36f, 0.12f, 0.14f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.7f, 0.68f, 0.64f);
        private static readonly Color TextAccent = new Color(0.95f, 0.72f, 0.35f);
        private static readonly Color SlotUnknown = new Color(0.22f, 0.2f, 0.24f);
        private static readonly Color SlotSuccess = new Color(0.3f, 0.65f, 0.4f);
        private static readonly Color SlotFailure = new Color(0.7f, 0.25f, 0.25f);

        private GameObject _root;
        private Text _sizeBadgeText;
        private Text _slotBadgeText;
        private Text _nameText;
        private Text _statsText;
        private Text _descriptionText;
        private Text _footerText;
        private Image _enemyArt;
        private RectTransform _slotRow;
        private readonly List<Image> _slotIcons = new();
        private readonly List<Text> _slotLabels = new();
        private readonly Dictionary<Choice, Button> _buttons = new();
        private Text _oddsTitleText;
        private Text _oddsBodyText;

        private EnemyDefinition _enemy;
        private PartyMember _hero;
        private Action<EncounterOutcome> _onResolved;
        private bool _rolling;
        private bool _initialized;

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("EncounterPanel").AddComponent<EncounterPanel>().Initialize();
        }

        // hero: 판정에 쓸 캐릭터 (지금은 조우를 시작한 1명 — 3인 보드 표현은 기획 확인 중).
        public static void Show(EnemyDefinition enemy, PartyMember hero, Action<EncounterOutcome> onResolved)
        {
            EnsureExists();
            Instance.ShowInternal(enemy, hero, onResolved);
        }

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);

            Build();
        }

        private void ShowInternal(EnemyDefinition enemy, PartyMember hero, Action<EncounterOutcome> onResolved)
        {
            if (enemy == null) return;
            _enemy = enemy;
            _hero = hero;
            _onResolved = onResolved;
            _rolling = false;

            _nameText.text = enemy.DisplayName;
            _sizeBadgeText.text = SizeLabel(enemy.Size);
            _slotBadgeText.text = enemy.SlotCount.ToString();

            // 적 아트가 있으면 마름모 안에 그림, 없으면 크기 배지(소형/중형…).
            bool hasEnemyArt = enemy.Portrait != null;
            _enemyArt.sprite = enemy.Portrait;
            _enemyArt.enabled = hasEnemyArt;
            _sizeBadgeText.enabled = !hasEnemyArt;

            // 명탐정 패시브 "현장 검증": 조우 화면에 적 HP·속도·양쪽 방어력 표시.
            bool fieldInvestigation = hero != null && hero.Job.Id == JobId.Detective;
            _statsText.text = fieldInvestigation
                ? $"현장 검증 — HP {enemy.MaxHP} · 속도 {enemy.Speed} · 물리 방어 {enemy.PhysicalDefense} · 마법 방어 {enemy.MagicDefense}"
                : "HP ? · 속도 ? · 방어 ?";

            _footerText.text = string.IsNullOrEmpty(enemy.TraitDescription) ? "" : enemy.TraitDescription;
            _footerText.color = TextAccent;

            bool canAvoid = EncounterRules.AllowsPreCombatAvoidance(enemy);
            foreach (var pair in _buttons)
            {
                bool avoidance = pair.Key == Choice.Sneak || pair.Key == Choice.Retreat;
                pair.Value.interactable = !avoidance || canAvoid;
            }

            BuildSlotIcons(enemy.SlotCount);
            Describe(Choice.Ambush);
            _descriptionText.text = "행동을 선택하세요. 버튼에 마우스를 올리면 설명과 확률이 표시됩니다.";
            _root.SetActive(true);
        }

        private void Describe(Choice choice)
        {
            if (_enemy == null || _rolling) return;

            bool canAvoid = EncounterRules.AllowsPreCombatAvoidance(_enemy);
            switch (choice)
            {
                case Choice.Fight:
                    _descriptionText.text = "판정 없이 바로 전투를 시작합니다. 행동 순서는 속도로 정해집니다.";
                    ShowNoRollOdds("판정 없음 — 일반 속도 순서로 전투");
                    break;
                case Choice.Ambush:
                    _descriptionText.text = $"인지 판정. 모든 슬롯이 성공하면 첫 라운드에 아군이 먼저 행동하고, 실패하면 적이 먼저 행동합니다.";
                    ShowRollOdds(AmbushBonus > 0f ? $"기습 확률 (보너스 +{AmbushBonus * 100f:0}%p)" : "기습 확률",
                        EncounterRules.AmbushSlotChance(_enemy, HeroStat(StatKind.Perception), AmbushBonus), StatKind.Perception);
                    break;
                case Choice.Sneak:
                    if (!canAvoid)
                    {
                        _descriptionText.text = "보스에게서는 몰래 지나갈 수 없습니다.";
                        ShowNoRollOdds("사용 불가");
                        break;
                    }
                    _descriptionText.text = "속도 판정. 성공하면 전투 없이 지나갑니다(경험치·전리품 없음). 실패하면 일반 전투가 시작됩니다.";
                    ShowRollOdds("몰래 지나가기 확률", EncounterRules.SneakSlotChance(_enemy, HeroStat(StatKind.Speed)), StatKind.Speed);
                    break;
                case Choice.Retreat:
                    if (!canAvoid)
                    {
                        _descriptionText.text = "보스 전투에서는 후퇴할 수 없습니다.";
                        ShowNoRollOdds("사용 불가");
                        break;
                    }
                    _descriptionText.text = "판정 없이 직전 안전 타일로 돌아갑니다.";
                    ShowNoRollOdds("판정 없음 — 항상 성공");
                    break;
            }
        }

        private int HeroStat(StatKind kind) => _hero != null ? _hero.FinalStats.Get(kind) : 0;

        // 판정 캐릭터의 기습 패시브 (명탐정 현장 검증 +10%p).
        // 현장 검증 패시브 + 안개 연막병(다음 기습 1회) — 합산, 최대 95%는 슬롯 확률 계산에서
        private float AmbushBonus => (_hero != null ? _hero.PassiveValue(PassiveType.AmbushSlotBonus) : 0f)
                                     + (Fantasia.Core.BoardSession.Instance != null ? Fantasia.Core.BoardSession.Instance.PendingAmbushBonus / 100f : 0f);

        private void ShowRollOdds(string title, float slotChance, StatKind stat)
        {
            int n = _enemy.SlotCount;
            var dist = SlotCheck.SuccessCountDistribution(n, slotChance);
            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"슬롯 {n}개 · 슬롯당 {Percent(slotChance)} ({StatName(stat)} {HeroStat(stat)})");
            for (int k = n; k >= 0; k--)
            {
                lines.AppendLine($"{k}개 성공  ({Percent(dist[k])})  {(k == n ? "성공" : "실패")}");
            }
            _oddsTitleText.text = title;
            _oddsBodyText.text = lines.ToString();
        }

        private void ShowNoRollOdds(string message)
        {
            _oddsTitleText.text = "판정 확률";
            _oddsBodyText.text = message;
        }

        // 자동 플레이 점검용 — 0 싸우기, 1 기습, 2 몰래 지나가기, 3 후퇴 (버튼과 같은 처리).
        public static void DevChoose(int index)
        {
            if (IsOpen) Instance.OnChoose((Choice)index);
        }

        private void OnChoose(Choice choice)
        {
            if (_rolling || _enemy == null) return;

            switch (choice)
            {
                case Choice.Fight:
                    Finish(EncounterRules.Fight());
                    break;
                case Choice.Retreat:
                    Finish(EncounterRules.ResolveRetreatBeforeCombat(_enemy));
                    break;
                case Choice.Ambush:
                    StartCoroutine(RevealRoll(EncounterRules.RollAmbush(_enemy, HeroStat(StatKind.Perception), 0, AmbushBonus)));
                    if (Fantasia.Core.BoardSession.Instance != null) Fantasia.Core.BoardSession.Instance.PendingAmbushBonus = 0; // 연막병 소모
                    break;
                case Choice.Sneak:
                    StartCoroutine(RevealRoll(EncounterRules.RollSneak(_enemy, HeroStat(StatKind.Speed))));
                    break;
            }
        }

        // 결과는 이미 정해져 있고, 성공 슬롯 위치만 섞어서 하나씩 보여준다.
        private IEnumerator RevealRoll(EncounterRules.Roll roll)
        {
            _rolling = true;
            foreach (var button in _buttons.Values) button.interactable = false;

            int n = roll.Slots.SlotCount;
            var successes = new bool[n];
            for (int i = 0; i < roll.Slots.SuccessCount; i++) successes[i] = true;
            for (int i = n - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (successes[i], successes[j]) = (successes[j], successes[i]);
            }

            for (int i = 0; i < n; i++)
            {
                yield return new WaitForSecondsRealtime(SecondsPerSlotReveal);
                _slotIcons[i].color = successes[i] ? SlotSuccess : SlotFailure;
                _slotLabels[i].text = successes[i] ? "성공" : "실패";
            }

            _footerText.text = OutcomeMessage(roll.Outcome);
            _footerText.color = roll.Slots.AllSucceeded ? SlotSuccess : SlotFailure;
            yield return new WaitForSecondsRealtime(SecondsResultHold);

            _rolling = false;
            Finish(roll.Outcome);
        }

        private void Finish(EncounterOutcome outcome)
        {
            _root.SetActive(false);
            var callback = _onResolved;
            _onResolved = null;
            _enemy = null;
            callback?.Invoke(outcome);
        }

        public static string OutcomeMessage(EncounterOutcome outcome) => outcome switch
        {
            EncounterOutcome.FightStarted => "전투 시작.",
            EncounterOutcome.AmbushSuccess => "기습 성공! 첫 라운드에 아군이 먼저 행동한다.",
            EncounterOutcome.AmbushFailure => "기습 실패... 첫 라운드에 적이 먼저 행동한다.",
            EncounterOutcome.SneakSuccess => "몰래 지나가기 성공! 전투 없이 지나간다.",
            EncounterOutcome.SneakFailure => "들켰다... 일반 전투가 시작된다.",
            EncounterOutcome.RetreatSuccess => "후퇴 — 직전 안전 타일로 돌아갔다.",
            _ => "",
        };

        private static string Percent(float value01) => $"{value01 * 100f:0.0}%";

        private static string SizeLabel(EnemySize size) => size switch
        {
            EnemySize.Small => "소형",
            EnemySize.Medium => "중형",
            EnemySize.Large => "대형",
            EnemySize.Boss => "보스",
            _ => "?",
        };

        private static string StatName(StatKind kind) => kind switch
        {
            StatKind.Perception => "인지",
            StatKind.Speed => "속도",
            _ => kind.ToString(),
        };

        private void BuildSlotIcons(int count)
        {
            foreach (Transform child in _slotRow) Destroy(child.gameObject);
            _slotIcons.Clear();
            _slotLabels.Clear();

            const float size = 44f, gap = 18f;
            float totalWidth = count * size + (count - 1) * gap;
            for (int i = 0; i < count; i++)
            {
                var fill = UGUIKit.CreateBorderedPanel(_slotRow, $"Slot{i}", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), BorderGold, SlotUnknown, 2f);
                var outer = (RectTransform)fill.parent;
                outer.sizeDelta = new Vector2(size, size);
                outer.anchoredPosition = new Vector2(-totalWidth / 2f + size / 2f + i * (size + gap), 0f);
                outer.localRotation = Quaternion.Euler(0f, 0f, 45f);

                var label = UGUIKit.CreateText(_slotRow, $"SlotLabel{i}", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "?", 13, TextAnchor.MiddleCenter);
                var labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = new Vector2(size + 10f, size);
                labelRect.anchoredPosition = outer.anchoredPosition;
                label.color = TextLight;
                label.fontStyle = FontStyle.Bold;

                _slotIcons.Add(fill.GetComponent<Image>());
                _slotLabels.Add(label);
            }
        }

        private void Build()
        {
            var canvasGO = new GameObject("EncounterCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // 퀘스트 창 등 상시 UI 위에 뜬다
            canvasGO.AddComponent<GraphicRaycaster>();

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var backdrop = UGUIKit.CreateImage(canvasGO.transform, "Root", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.3f));
            _root = backdrop.gameObject;

            BuildPortrait(backdrop.transform);
            BuildChoiceCard(backdrop.transform);
            BuildOddsPanel(backdrop.transform);

            _root.SetActive(false);
        }

        // 적 초상 자리 — 아트가 없어 마름모 틀 안에 크기 배지를 둔다. 아래 작은 배지는 슬롯 수.
        private void BuildPortrait(Transform parent)
        {
            var frameFill = UGUIKit.CreateBorderedPanel(parent, "PortraitFrame", new Vector2(0.21f, 0.8f), new Vector2(0.21f, 0.8f),
                BorderGold, new Color(0.18f, 0.12f, 0.22f, 0.97f), 4f);
            var frame = (RectTransform)frameFill.parent;
            frame.sizeDelta = new Vector2(150f, 150f);
            frame.localRotation = Quaternion.Euler(0f, 0f, 45f);

            _sizeBadgeText = UGUIKit.CreateText(parent, "SizeBadge", new Vector2(0.21f, 0.8f), new Vector2(0.21f, 0.8f), "", 34, TextAnchor.MiddleCenter);
            ((RectTransform)_sizeBadgeText.transform).sizeDelta = new Vector2(160f, 60f);
            _sizeBadgeText.color = TextLight;
            _sizeBadgeText.fontStyle = FontStyle.Bold;

            // 마름모(한 변 150) 안에 들어가는 정사각형 ≈ 106 — 아트는 회전 없이 그 안에 맞춘다.
            _enemyArt = UGUIKit.CreateImage(parent, "EnemyArt", new Vector2(0.21f, 0.8f), new Vector2(0.21f, 0.8f), Color.white);
            ((RectTransform)_enemyArt.transform).sizeDelta = new Vector2(104f, 104f);
            _enemyArt.preserveAspect = true;
            _enemyArt.enabled = false;

            var badgeFill = UGUIKit.CreateBorderedPanel(parent, "SlotBadge", new Vector2(0.21f, 0.655f), new Vector2(0.21f, 0.655f),
                BorderGold, new Color(0.3f, 0.1f, 0.1f), 2f);
            ((RectTransform)badgeFill.parent).sizeDelta = new Vector2(46f, 46f);
            _slotBadgeText = UGUIKit.CreateText(badgeFill, "Value", Vector2.zero, Vector2.one, "", 20, TextAnchor.MiddleCenter);
            _slotBadgeText.color = TextLight;
            _slotBadgeText.fontStyle = FontStyle.Bold;

        }

        private void BuildChoiceCard(Transform parent)
        {
            var card = UGUIKit.CreateBorderedPanel(parent, "ChoiceCard", new Vector2(0.04f, 0.24f), new Vector2(0.38f, 0.6f), BorderGold, PanelFill, 2.5f);

            // 이름 배너 — 카드 위쪽 테두리에 걸쳐 놓는다.
            var bannerFill = UGUIKit.CreateBorderedPanel(card, "NameBanner", new Vector2(0.22f, 0.9f), new Vector2(0.78f, 1.06f), BorderGold, new Color(0.2f, 0.08f, 0.1f), 2f);
            _nameText = UGUIKit.CreateText(bannerFill, "Name", Vector2.zero, Vector2.one, "", 18, TextAnchor.MiddleCenter);
            _nameText.color = TextLight;
            _nameText.fontStyle = FontStyle.Bold;

            _statsText = UGUIKit.CreateText(card, "Stats", new Vector2(0.04f, 0.76f), new Vector2(0.96f, 0.86f), "", 11, TextAnchor.MiddleCenter);
            _statsText.color = TextDim;

            _descriptionText = UGUIKit.CreateText(card, "Description", new Vector2(0.05f, 0.58f), new Vector2(0.95f, 0.76f), "", 12, TextAnchor.MiddleCenter);
            _descriptionText.color = TextLight;

            _slotRow = UGUIKit.CreateRect(card, "SlotRow", new Vector2(0.1f, 0.38f), new Vector2(0.9f, 0.56f));

            var choices = new[] { (Choice.Fight, "싸우기"), (Choice.Ambush, "기습"), (Choice.Sneak, "몰래 지나가기"), (Choice.Retreat, "후퇴") };
            for (int i = 0; i < choices.Length; i++)
            {
                var (choice, label) = choices[i];
                float x0 = 0.035f + i * 0.2375f;
                var button = UGUIKit.CreateButton(card, $"{choice}Button", new Vector2(x0, 0.2f), new Vector2(x0 + 0.215f, 0.33f), label, ButtonFill, 13);
                button.GetComponentInChildren<Text>().color = TextLight;
                button.onClick.AddListener(() => OnChoose(choice));
                AddHover(button.gameObject, () => Describe(choice));
                _buttons[choice] = button;
            }

            _footerText = UGUIKit.CreateText(card, "Footer", new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.17f), "", 11, TextAnchor.MiddleCenter);
            _footerText.color = TextAccent;

        }

        private void BuildOddsPanel(Transform parent)
        {
            var panel = UGUIKit.CreateBorderedPanel(parent, "OddsPanel", new Vector2(0.4f, 0.34f), new Vector2(0.64f, 0.54f), BorderGold, PanelFill, 2.5f);

            var bannerFill = UGUIKit.CreateBorderedPanel(panel, "OddsBanner", new Vector2(0.15f, 0.82f), new Vector2(0.85f, 1.1f), BorderGold, new Color(0.2f, 0.08f, 0.1f), 2f);
            _oddsTitleText = UGUIKit.CreateText(bannerFill, "Title", Vector2.zero, Vector2.one, "판정 확률", 14, TextAnchor.MiddleCenter);
            _oddsTitleText.color = TextLight;
            _oddsTitleText.fontStyle = FontStyle.Bold;

            _oddsBodyText = UGUIKit.CreateText(panel, "Body", new Vector2(0.08f, 0.06f), new Vector2(0.95f, 0.78f), "", 13, TextAnchor.UpperLeft);
            _oddsBodyText.color = TextLight;
            _oddsBodyText.lineSpacing = 1.15f;
        }

        private static void AddHover(GameObject target, Action onEnter)
        {
            var trigger = target.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entry.callback.AddListener(_ => onEnter());
            trigger.triggers.Add(entry);
        }
    }
}
