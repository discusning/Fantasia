using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.World;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 이벤트 칸 창 — 점술마녀(제작 시안 G02: 참가비·카드 3장·행운 성공률·[선택 확정][떠나기]), 분수대, 십자가 석상(기도 6종).
    // 판정 표시는 For the King 이벤트처럼 성공률을 먼저 보여주고, 결과 칸 하나를 공개한다.
    // 결과는 판정 즉시 반영·저장한 뒤 공개 (Save.md "확정 → 저장 → 표시").
    public class EventPanel : MonoBehaviour
    {
        public static EventPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root.activeSelf;

        private static readonly Color Gold = new Color(0.82f, 0.68f, 0.4f);
        private static readonly Color Fill = new Color(0.07f, 0.05f, 0.08f, 0.97f);
        private static readonly Color CardFill = new Color(0.13f, 0.1f, 0.12f, 1f);
        private static readonly Color CardSelected = new Color(0.32f, 0.24f, 0.12f, 1f);
        private static readonly Color RedFrame = new Color(0.7f, 0.22f, 0.2f);
        private static readonly Color TextLight = new Color(0.94f, 0.9f, 0.84f);
        private static readonly Color TextDim = new Color(0.6f, 0.57f, 0.53f);
        private static readonly Color Good = new Color(0.4f, 0.75f, 0.45f);
        private static readonly Color Bad = new Color(0.88f, 0.38f, 0.33f);

        private GameObject _root, _cardsRow, _targetRow, _resultBox;
        private Image _witchArt;
        private Text _title, _chip, _odds, _note, _footer, _cent, _resultText, _confirmLabel;
        private Image _resultSlot;
        private Button _confirm, _leave;
        private readonly List<(Button button, Image fill, Image frame)> _cards = new();
        private readonly List<(Button button, Text label)> _targets = new();

        private EventKind _kind;
        private Party _party;
        private PartyMember _judge;
        private FortuneCard? _card;
        private PartyMember _target;
        private int _waitRounds;
        private bool _done;
        private Action<string> _onClosed;  // 결과 메시지
        private string _lastMessage;

        // ── 열기 ──
        public static void OpenWitch(Party party, PartyMember judge, int waitRounds, Func<FortuneCard, PartyMember, EventTiles.WitchResult> play, Action<string> onClosed)
        {
            var p = Ensure();
            p.Begin(EventKind.FortuneTeller, party, judge, onClosed);
            p._waitRounds = waitRounds;
            p._playWitch = play;
            p.Refresh();
        }

        // 우물(회복 칸): 1명 골라 집중력 +1 — 분수대와 같은 화면, 문구만 다름 (UI 솔루션 Q1)
        public static void OpenWell(Party party, Func<PartyMember, string> use, Action<string> onClosed)
        {
            var p = Ensure();
            p.Begin(EventKind.Well, party, null, onClosed);
            p._fountainUsed = false;
            p._useFountain = use;
            p.Refresh();
        }

        public static void OpenFountain(Party party, bool alreadyUsed, Func<PartyMember, string> use, Action<string> onClosed)
        {
            var p = Ensure();
            p.Begin(EventKind.Fountain, party, null, onClosed);
            p._fountainUsed = alreadyUsed;
            p._useFountain = use;
            p.Refresh();
        }

        // 십자가 석상: 캐릭터 1명 + 기도 1종, 석상당 회차 1회 (확정 때 소모, 떠나면 소모 안 함)
        public static void OpenStatue(Party party, bool alreadyUsed, Func<PartyMember, PrayerKind, string> use, Action<string> onClosed)
        {
            var p = Ensure();
            p.Begin(EventKind.Statue, party, null, onClosed);
            p._statueUsed = alreadyUsed;
            p._usePrayer = use;
            p._prayer = null;
            p.Refresh();
        }

        private Func<FortuneCard, PartyMember, EventTiles.WitchResult> _playWitch;
        private Func<PartyMember, string> _useFountain;
        private bool _fountainUsed;
        private Func<PartyMember, PrayerKind, string> _usePrayer;
        private bool _statueUsed;
        private PrayerKind? _prayer;
        private GameObject _prayerRow;
        private readonly List<(Button button, Image fill)> _prayerButtons = new();

        private static EventPanel Ensure()
        {
            if (Instance == null) new GameObject("EventPanel").AddComponent<EventPanel>().Build();
            return Instance;
        }

        private void Begin(EventKind kind, Party party, PartyMember judge, Action<string> onClosed)
        {
            _kind = kind;
            _party = party;
            _judge = judge;
            _onClosed = onClosed;
            _card = null;
            _target = null;
            _done = false;
            _lastMessage = null;
            _resultBox.SetActive(false);
            _root.SetActive(true);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ── 표시 ──
        private void Refresh()
        {
            _cent.text = $"소지금\n{_party.Cent} Cent";
            bool witch = _kind == EventKind.FortuneTeller;
            bool statue = _kind == EventKind.Statue;
            _title.text = EventTiles.Label(_kind);
            _cardsRow.SetActive(witch);
            _witchArt.sprite = UITheme.Current.PortraitForSpeaker("점술마녀");
            _witchArt.enabled = witch && _witchArt.sprite != null;
            _prayerRow.SetActive(statue);
            if (statue)
            {
                _chip.text = "캐릭터 1명 · 기도 1종 · 석상마다 1회";
                _odds.text = _prayer.HasValue ? $"{PrayerRules.Name(_prayer.Value)} — {PrayerRules.Effect(_prayer.Value)}" : "석상 앞에 무릎을 꿇고 기도할 것을 고른다.";
                _note.text = _statueUsed ? "<color=#E0675A>이미 기도를 받은 석상입니다</color>"
                    : _target != null && _target.Prayer != PrayerKind.None ? $"<color=#E0675A>{_target.Job.DisplayName}의 {PrayerRules.Name(_target.Prayer)}는 새 기도로 교체됩니다</color>"
                    : "캐릭터당 기도 1개 · 이번 회차 동안 유지";
                _footer.text = "기도와 캐릭터를 고르고 확정하세요 · 떠나면 횟수를 쓰지 않음";
                for (int i = 0; i < _prayerButtons.Count; i++)
                    _prayerButtons[i].fill.color = _prayer.HasValue && PrayerRules.Offered[i] == _prayer.Value ? CardSelected : CardFill;
                _targetRow.SetActive(!_statueUsed && !_done);
                var list = _party.Members.ToList();
                for (int i = 0; i < _targets.Count; i++)
                {
                    bool has = i < list.Count;
                    _targets[i].button.gameObject.SetActive(has);
                    if (!has) continue;
                    var m = list[i];
                    _targets[i].label.text = $"{(m == _target ? "▶ " : "")}{m.Job.DisplayName}\n<size=10>{(m.Prayer == PrayerKind.None ? "기도 없음" : PrayerRules.Name(m.Prayer))}</size>";
                    _targets[i].button.interactable = m.IsAlive;
                }
                _confirm.gameObject.SetActive(!_done);
                _confirm.interactable = !_done && !_statueUsed && _prayer.HasValue && _target != null && _target.IsAlive;
                _confirmLabel.text = "기도 받기";
                _leave.GetComponentInChildren<Text>().text = _done ? "닫기" : "떠나기";
                return;
            }
            if (witch)
            {
                int pct = EventTiles.SuccessPercent(_judge);
                _chip.text = $"참가비 {EventTiles.WitchFee} Cent · 카드 1장 선택";
                _odds.text = $"♣ 행운 {_judge.FinalStats.Luck} · 성공률 {pct}%  <color=#9A948C>({_judge.Job.DisplayName})</color>";
                _note.text = _waitRounds > 0 ? $"<color=#E0675A>점술은 {_waitRounds}라운드 뒤에 다시 볼 수 있습니다</color>" : "실패 시 보상 없음";
                _footer.text = "카드 선택 후 행운 판정 · 집중력 사용 불가";
                for (int i = 0; i < _cards.Count; i++)
                    _cards[i].fill.color = _card.HasValue && (int)_card.Value == i ? CardSelected : CardFill;
            }
            else
            {
                bool well = _kind == EventKind.Well;
                _chip.text = well ? "생존 동료 1명의 집중력 1 회복 · 이 우물에서 1회" : "선택한 캐릭터 LP +1 · 칸마다 1회";
                _odds.text = well ? "차가운 우물물이 정신을 맑게 한다." : "맑은 물에 손을 담그면 생명의 기운이 깃든다.";
                _note.text = well ? "HP·MP·LP·상태 이상은 회복되지 않습니다" : _fountainUsed ? "<color=#E0675A>이미 기운을 받은 분수대입니다</color>" : $"LP 최대 {PartyMember.MaxLP}";
                _footer.text = well ? "집중력이 가득 찬 동료는 고를 수 없습니다 · 떠나면 횟수를 쓰지 않음" : "캐릭터를 고르고 확정하세요";
            }

            // 대상 고르기: 회복·집중 카드, 분수대
            bool needTarget = witch ? _card.HasValue && EventTiles.NeedsTarget(_card.Value) : !_fountainUsed;
            _targetRow.SetActive(needTarget && !_done);
            var members = _party.Members.ToList();
            for (int i = 0; i < _targets.Count; i++)
            {
                bool has = i < members.Count;
                _targets[i].button.gameObject.SetActive(has);
                if (!has) continue;
                var m = members[i];
                bool usable = witch ? m.IsAlive : _kind == EventKind.Well ? Fantasia.World.MapTileRules.CanUseWell(m) : EventTiles.CanUseFountain(m);
                string state = witch ? (_card == FortuneCard.Recovery ? $"HP {m.CurrentHP}/{m.MaxHP}" : $"집중 {m.CurrentFocus}/{m.MaxFocus}") : _kind == EventKind.Well ? $"집중 {m.CurrentFocus}/{m.MaxFocus}" : $"LP {m.CurrentLP}/{PartyMember.MaxLP}";
                _targets[i].label.text = $"{(m == _target ? "▶ " : "")}{m.Job.DisplayName}\n<size=10>{state}</size>";
                _targets[i].button.interactable = usable;
            }

            bool ready = !_done && (witch
                ? _waitRounds == 0 && _card.HasValue && (!EventTiles.NeedsTarget(_card.Value) || _target != null) && _party.Cent >= EventTiles.WitchFee
                : !_fountainUsed && _target != null);
            _confirm.gameObject.SetActive(!_done);
            _confirm.interactable = ready;
            _confirmLabel.text = witch ? "선택 확정" : _kind == EventKind.Well ? "회복" : "기운 받기";
            _leave.GetComponentInChildren<Text>().text = _done ? "닫기" : "떠나기";
        }

        private void PickCard(int i)
        {
            if (_done || _waitRounds > 0) return;
            _card = (FortuneCard)i;
            if (!EventTiles.NeedsTarget(_card.Value)) _target = null;
            else if (_target == null) _target = _party.AliveMembers.OrderBy(m => _card == FortuneCard.Recovery ? (float)m.CurrentHP / m.MaxHP : (float)m.CurrentFocus / Mathf.Max(1, m.MaxFocus)).FirstOrDefault();
            Refresh();
        }

        private void PickPrayer(int i)
        {
            if (_done || _statueUsed) return;
            _prayer = PrayerRules.Offered[i];
            if (_target == null) _target = _party.AliveMembers.FirstOrDefault(m => m.Prayer == PrayerKind.None) ?? _party.AliveMembers.FirstOrDefault();
            Refresh();
        }

        private void PickTarget(int i)
        {
            if (_done) return;
            var members = _party.Members.ToList();
            if (i < members.Count) _target = members[i];
            Refresh();
        }

        private void OnConfirm()
        {
            if (_done) return;
            if (_kind == EventKind.Statue)
            {
                if (!_prayer.HasValue || _target == null) return;
                string message = _usePrayer(_target, _prayer.Value);
                _done = true;
                _lastMessage = $"십자가 석상 — {message}";
                StartCoroutine(Reveal(true, message));
                Refresh();
                return;
            }
            if (_kind == EventKind.FortuneTeller)
            {
                if (!_card.HasValue) return;
                var result = _playWitch(_card.Value, _target);
                if (!result.Ok) { _footer.text = result.Message; return; }
                _done = true;
                _lastMessage = $"점술마녀 [{EventTiles.CardName(_card.Value)}] — {result.Message}";
                StartCoroutine(Reveal(result.Success, result.Message));
            }
            else
            {
                if (_target == null) return;
                string message = _useFountain(_target);
                _done = true;
                _lastMessage = $"{EventTiles.Label(_kind)} — {message}";
                StartCoroutine(Reveal(true, message));
            }
            Refresh();
        }

        // 결과 공개 (FTK식 판정 칸 하나) — 상태는 이미 반영·저장됨
        private IEnumerator Reveal(bool success, string message)
        {
            _resultBox.SetActive(true);
            _resultSlot.color = TextDim;
            _resultText.text = "판정 중…";
            _resultText.color = TextLight;
            yield return new WaitForSecondsRealtime(0.6f);
            _resultSlot.color = success ? Good : Bad;
            _resultText.text = message;
            _resultText.color = success ? Good : Bad;
            Refresh();
        }

        private void OnLeave()
        {
            _root.SetActive(false);
            _onClosed?.Invoke(_lastMessage);
        }

        // ───────────── 구성 ─────────────
        private void Build()
        {
            Instance = this;
            BoardHintPanel.RegisterModal(nameof(EventPanel), () => IsOpen); // 열려 있으면 보드 안내 숨김
            var canvasGO = new GameObject("EventCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UGUIKit.ConfigureScaler(scaler);

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.45f));
            _root = dim.gameObject;
            var win = UGUIKit.CreateThemedPanel(UGUIKit.Safe16x9(dim.transform), "Window", new Vector2(0.24f, 0.2f), new Vector2(0.76f, 0.9f), Gold, Fill, 2f, UITheme.Current != null ? UITheme.Current.PanelFrame : null);
            // 점술마녀 인물 그림 (UI 솔루션 Q8, N18) — 창 왼쪽 바깥, 카드를 가리지 않게
            _witchArt = UGUIKit.CreateImage(UGUIKit.Safe16x9(dim.transform), "WitchArt", new Vector2(0.06f, 0.28f), new Vector2(0.23f, 0.84f), Color.white);
            _witchArt.preserveAspect = true;
            _witchArt.raycastTarget = false;
            _witchArt.enabled = false;
            win.parent.gameObject.AddComponent<Button>(); // 창 안 클릭이 보드로 새지 않게

            _title = Label(win, "Title", new Vector2(0.2f, 0.86f), new Vector2(0.8f, 0.97f), 26, Gold, true);
            var centBox = UGUIKit.CreateThemedPanel(win, "CentBox", new Vector2(0.79f, 0.86f), new Vector2(0.97f, 0.97f), Gold, Fill, 1.5f);
            _cent = Label(centBox, "Cent", Vector2.zero, Vector2.one, 11, TextLight, false);
            var chipBg = UGUIKit.CreateImage(win, "Chip", new Vector2(0.27f, 0.79f), new Vector2(0.73f, 0.855f), new Color(0.78f, 0.62f, 0.36f));
            _chip = Label(chipBg.transform, "Text", Vector2.zero, Vector2.one, 13, new Color(0.15f, 0.1f, 0.06f), true);
            _odds = Label(win, "Odds", new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.78f), 15, TextLight, false);
            _odds.supportRichText = true;
            _note = Label(win, "Note", new Vector2(0.1f, 0.67f), new Vector2(0.9f, 0.72f), 11, TextDim, false);
            _note.supportRichText = true;

            // 카드 3장 (부·회복·집중) — 아트는 아트 도착 후 UITheme로
            _cardsRow = UGUIKit.CreateRect(win, "Cards", new Vector2(0.12f, 0.3f), new Vector2(0.88f, 0.66f)).gameObject;
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var card = (FortuneCard)i;
                float x0 = i * 0.345f;
                var frameColor = card == FortuneCard.Recovery ? RedFrame : Gold;
                var inner = UGUIKit.CreateThemedPanel(_cardsRow.transform, $"Card{i}", new Vector2(x0, 0f), new Vector2(x0 + 0.31f, 1f), frameColor, CardFill, 2f);
                var frame = inner.parent.GetComponent<Image>();
                var fill = inner.GetComponent<Image>();
                var b = inner.parent.gameObject.AddComponent<Button>();
                b.targetGraphic = frame;
                b.onClick.AddListener(() => PickCard(index));
                Label(inner, "Symbol", new Vector2(0f, 0.45f), new Vector2(1f, 0.95f), 46, frameColor, true).text = card switch { FortuneCard.Wealth => "₵", FortuneCard.Recovery => "♥", _ => "◆" };
                var nameBg = UGUIKit.CreateImage(inner, "NameBar", new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.44f), new Color(0.05f, 0.04f, 0.05f, 0.9f));
                Label(nameBg.transform, "Name", Vector2.zero, Vector2.one, 16, Gold, true).text = EventTiles.CardName(card);
                Label(inner, "Reward", new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.28f), 11, TextLight, false).text = EventTiles.CardReward(card).Replace("성공 시 ", "성공 시\n");
                _cards.Add((b, fill, frame));
            }

            // 기도 6종 (십자가 석상) — 3칸 × 2줄
            _prayerRow = UGUIKit.CreateRect(win, "Prayers", new Vector2(0.06f, 0.3f), new Vector2(0.94f, 0.66f)).gameObject;
            for (int i = 0; i < PrayerRules.Offered.Count; i++)
            {
                int index = i;
                var kind = PrayerRules.Offered[i];
                float x0 = (i % 3) * 0.34f, y1 = i < 3 ? 1f : 0.48f;
                var inner = UGUIKit.CreateThemedPanel(_prayerRow.transform, $"Prayer{i}", new Vector2(x0, y1 - 0.46f), new Vector2(x0 + 0.32f, y1), Gold, CardFill, 1.5f);
                var b = inner.parent.gameObject.AddComponent<Button>();
                b.targetGraphic = inner.parent.GetComponent<Image>();
                b.onClick.AddListener(() => PickPrayer(index));
                Label(inner, "Name", new Vector2(0.03f, 0.6f), new Vector2(0.97f, 0.97f), 14, Gold, true).text = PrayerRules.Name(kind);
                var effect = Label(inner, "Effect", new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.6f), 10, TextLight, false);
                effect.text = PrayerRules.Effect(kind).Replace(" · ", "\n");
                _prayerButtons.Add((b, inner.GetComponent<Image>()));
            }
            _prayerRow.SetActive(false);

            // 대상 고르기 (회복·집중·분수대·석상)
            _targetRow = UGUIKit.CreateRect(win, "Targets", new Vector2(0.12f, 0.19f), new Vector2(0.88f, 0.28f)).gameObject;
            for (int i = 0; i < Party.MaxSize; i++)
            {
                int index = i;
                float x0 = i * 0.345f;
                var b = UGUIKit.CreateThemedButton(_targetRow.transform, $"Target{i}", new Vector2(x0, 0f), new Vector2(x0 + 0.31f, 1f), "", CardFill, 12);
                b.onClick.AddListener(() => PickTarget(index));
                var t = b.GetComponentInChildren<Text>();
                t.supportRichText = true;
                t.color = TextLight;
                _targets.Add((b, t));
            }

            // 결과 (판정 칸 1개)
            var result = UGUIKit.CreateThemedPanel(win, "Result", new Vector2(0.12f, 0.185f), new Vector2(0.88f, 0.285f), Gold, new Color(0.05f, 0.04f, 0.05f, 0.97f), 2f);
            _resultBox = result.parent.gameObject;
            _resultSlot = UGUIKit.CreateImage(result, "Slot", new Vector2(0.04f, 0.2f), new Vector2(0.14f, 0.8f), TextDim);
            _resultText = Label(result, "Text", new Vector2(0.17f, 0f), new Vector2(0.98f, 1f), 15, TextLight, true);
            _resultText.alignment = TextAnchor.MiddleLeft;
            _resultBox.SetActive(false);

            _confirm = UGUIKit.CreateThemedButton(win, "Confirm", new Vector2(0.2f, 0.07f), new Vector2(0.48f, 0.16f), "선택 확정", new Color(0.32f, 0.22f, 0.1f), 15);
            _confirm.onClick.AddListener(OnConfirm);
            _confirmLabel = _confirm.GetComponentInChildren<Text>();
            _leave = UGUIKit.CreateThemedButton(win, "Leave", new Vector2(0.52f, 0.07f), new Vector2(0.8f, 0.16f), "떠나기", new Color(0.16f, 0.15f, 0.17f), 15);
            _leave.onClick.AddListener(OnLeave);
            _footer = Label(win, "Footer", new Vector2(0.05f, 0.005f), new Vector2(0.95f, 0.065f), 11, TextDim, false);

            _root.SetActive(false);
        }

        private static Text Label(Transform parent, string name, Vector2 min, Vector2 max, int size, Color color, bool bold)
        {
            var t = UGUIKit.CreateText(parent, name, min, max, "", size, TextAnchor.MiddleCenter);
            t.color = color;
            if (bold) t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            return t;
        }

        // 자동 플레이 점검용
        public static void DevPickCard(int i) { if (IsOpen) Instance.PickCard(i); }
        public static void DevPickTarget(int i) { if (IsOpen) Instance.PickTarget(i); }
        public static void DevPickPrayer(int i) { if (IsOpen) Instance.PickPrayer(i); }
        public static void DevConfirm() { if (IsOpen) Instance.OnConfirm(); }
        public static void DevLeave() { if (IsOpen) Instance.OnLeave(); }
        public static bool DevDone => IsOpen && Instance._done;
    }
}
