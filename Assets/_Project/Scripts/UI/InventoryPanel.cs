using System;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Combat;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 인벤토리·장비 창 (D17·D18 1차). 참고: For the King 인벤토리 화면
    // (Docs/Concept_Image/Concept/Fantasia_인터페이스_사진원본/01_포더킹1_실제_스크린샷/11_인벤토리와_장비_비교_한국어.jpg)
    //   왼쪽: 비교 카드 2장 — [장착 중] / [선택한 것] + 장착하면 바뀌는 수치
    //   오른쪽: 캐릭터 탭 · 능력치 줄 · 장비 5칸 · 3D 초상 · 아래 목록(장비 / 소모품 / 보관함)
    // 파티 공용 인벤토리(Party.Inventory)를 보여주고, 장비는 보고 있는 캐릭터에게 입힌다.
    // 보드에서만 연다 (전투 중 장비 교체는 행동 1회 — 아직 미연결). 그림은 UITheme 공용 틀.
    public class InventoryPanel : MonoBehaviour
    {
        public static InventoryPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root.activeSelf;

        // 보드가 정한다: 지금 월드에서 소모품을 쓸 수 있는지 (아군 턴, 이동 중 아님) / 야영 세트로 턴 종료
        public static Func<bool> WorldUseAllowed;
        public static event Action AllyTurnEndRequested;

        private enum Tab { Gear, Consumables, Pending }

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color Fill = new Color(0.08f, 0.07f, 0.09f, 0.97f);
        private static readonly Color CardFill = new Color(0.12f, 0.11f, 0.13f, 1f);
        private static readonly Color RowFill = new Color(0.16f, 0.15f, 0.17f, 1f);
        private static readonly Color RowSelected = new Color(0.42f, 0.33f, 0.18f, 1f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.62f, 0.6f, 0.56f);
        private static readonly Color Good = new Color(0.45f, 0.85f, 0.45f);
        private static readonly Color Bad = new Color(0.9f, 0.4f, 0.35f);
        private const int RowsPerPage = 9;

        private GameObject _root;
        private Party _party;
        private PartyMember _member;
        private Tab _tab = Tab.Gear;
        private int _page;

        // 선택: 인벤토리 장비 / 착용 슬롯 / 소모품 / 보관함 칸 중 하나
        private GearItem _selectedGear;
        private GearSlot? _selectedSlot;
        private ConsumableDefinition _selectedConsumable;
        private int _selectedPending = -1;

        private readonly List<Button> _memberTabs = new();
        private Text _stats, _levelLine, _cent, _message;
        private PortraitView _portrait;
        private readonly List<(Button button, Text label)> _slotRows = new();
        private readonly List<(Button button, Image fill, Text name, Text right)> _rows = new();
        private readonly List<(Button button, Text label)> _tabButtons = new();
        private Text _pageLabel;
        private CardView _equippedCard, _selectedCard;
        private Button _equip, _unequip, _discard, _use, _take;
        private readonly List<Button> _quickButtons = new();

        private class CardView
        {
            public Text Header, Name, Main, Body, Footer;
        }

        public static InventoryPanel EnsureExists()
        {
            if (Instance == null) new GameObject("InventoryPanel").AddComponent<InventoryPanel>().Build();
            return Instance;
        }

        public static void Open(Party party, PartyMember member)
        {
            if (party == null || party.Members.Count == 0) return;
            var p = EnsureExists();
            p._party = party;
            p._member = member ?? party.Members[0];
            p._root.SetActive(true);
            p.ClearSelection();
            p.Refresh();
        }

        public static void Close()
        {
            if (Instance != null) Instance._root.SetActive(false);
        }

        public static void Toggle(Party party, PartyMember member)
        {
            if (IsOpen) Close();
            else Open(party, member);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_party != null) _party.Inventory.Changed -= Refresh;
        }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        // ───────────── 갱신 ─────────────
        private void ClearSelection()
        {
            _selectedGear = null;
            _selectedSlot = null;
            _selectedConsumable = null;
            _selectedPending = -1;
        }

        private void Refresh()
        {
            if (_party == null || _member == null || !_root.activeSelf) return;
            var inv = _party.Inventory;
            var pending = BoardSession.Instance != null ? BoardSession.Instance.PendingLoot : new List<LootDrop>();

            for (int i = 0; i < _memberTabs.Count; i++)
            {
                bool has = i < _party.Members.Count;
                _memberTabs[i].gameObject.SetActive(has);
                if (!has) continue;
                var m = _party.Members[i];
                _memberTabs[i].GetComponentInChildren<Text>().text = m.Job.DisplayName;
                _memberTabs[i].targetGraphic.color = m == _member ? RowSelected : RowFill;
            }

            var s = _member.FinalStats;
            _stats.text = $"힘 {s.Strength}  속도 {s.Speed}  활력 {s.Vitality}  지력 {s.Intelligence}  행운 {s.Luck}  인지 {s.Perception}\n"
                          + $"공격 {_member.FinalPhysicalAttack}  마력 {_member.FinalMagicAttack}  물리 방어 {_member.FinalPhysicalDefense}  마법 방어 {_member.FinalMagicDefense}  "
                          + $"HP {_member.CurrentHP}/{_member.MaxHP}  MP {(_member.MaxMP > 0 ? $"{_member.CurrentMP}/{_member.MaxMP}" : "없음")}  집중 {_member.CurrentFocus}/{_member.MaxFocus}";
            _levelLine.text = _member.Level >= PartyMember.MaxLevel ? $"Lv {_member.Level} (최대)" : $"Lv {_member.Level}  ·  EXP {_member.Experience}/{_member.ExperienceToNextLevel}";
            _cent.text = $"{_party.Cent} Cent";
            _portrait.Set(_member.Job.Portrait, PortraitStudio.TextureFor(_member.Job));

            foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
            {
                var row = _slotRows[(int)slot];
                var item = _member.GetEquipped(slot);
                row.label.text = $"{slot.Label()}   {(item != null ? item.DisplayName : "— 비어 있음 —")}";
                row.label.color = item != null ? TextLight : TextDim;
                row.button.targetGraphic.color = _selectedSlot == slot ? RowSelected : RowFill;
            }

            _tabButtons[0].label.text = $"장비 {inv.Gear.Count}/{PartyInventory.MaxGear}";
            _tabButtons[1].label.text = $"소모품 {inv.Consumables.Count}종";
            _tabButtons[2].label.text = pending.Count > 0 ? $"보관함 {pending.Count} !" : "보관함";
            _tabButtons[2].label.color = pending.Count > 0 ? Bad : TextLight;
            for (int i = 0; i < _tabButtons.Count; i++) _tabButtons[i].button.targetGraphic.color = (int)_tab == i ? RowSelected : RowFill;

            FillRows(inv, pending);
            FillCards();
            FillActions(inv);
        }

        private void FillRows(PartyInventory inv, List<LootDrop> pending)
        {
            int total = _tab switch { Tab.Gear => inv.Gear.Count, Tab.Consumables => inv.Consumables.Count, _ => pending.Count };
            int pages = Mathf.Max(1, (total + RowsPerPage - 1) / RowsPerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.text = pages > 1 ? $"{_page + 1}/{pages}" : "";

            for (int r = 0; r < _rows.Count; r++)
            {
                int i = _page * RowsPerPage + r;
                var row = _rows[r];
                bool has = i < total;
                row.button.gameObject.SetActive(has);
                if (!has) continue;
                bool selected = false;
                switch (_tab)
                {
                    case Tab.Gear:
                        var g = inv.Gear[i];
                        row.name.text = g.DisplayName;
                        row.right.text = $"{g.Slot.Label()} · Lv{g.WearLevel}";
                        row.name.color = g.CanEquip(_member, out _) ? TextLight : TextDim;
                        selected = g == _selectedGear;
                        break;
                    case Tab.Consumables:
                        var c = inv.Consumables[i];
                        row.name.text = c.Definition.DisplayName;
                        row.right.text = $"×{c.Count}";
                        row.name.color = c.Definition.IsImplemented ? TextLight : TextDim;
                        selected = c.Definition == _selectedConsumable;
                        break;
                    default:
                        row.name.text = pending[i].Name;
                        row.right.text = pending[i].Gear != null ? "장비" : "소모품";
                        row.name.color = TextLight;
                        selected = i == _selectedPending;
                        break;
                }
                row.fill.color = selected ? RowSelected : RowFill;
            }
        }

        private void OnRowClicked(int r)
        {
            int i = _page * RowsPerPage + r;
            var inv = _party.Inventory;
            ClearSelection();
            switch (_tab)
            {
                case Tab.Gear: if (i < inv.Gear.Count) _selectedGear = inv.Gear[i]; break;
                case Tab.Consumables: if (i < inv.Consumables.Count) _selectedConsumable = inv.Consumables[i].Definition; break;
                default: _selectedPending = i; break;
            }
            _message.text = "";
            Refresh();
        }

        // ───────────── 비교 카드 ─────────────
        private void FillCards()
        {
            GearItem selected = _selectedGear;
            if (_selectedPending >= 0 && _selectedPending < Pending.Count) selected = Pending[_selectedPending].Gear;
            if (_selectedSlot.HasValue) selected = null;

            GearItem equipped = _selectedSlot.HasValue ? _member.GetEquipped(_selectedSlot.Value)
                : selected != null ? _member.GetEquipped(selected.Slot) : null;

            ShowGear(_equippedCard, equipped, equipped != null ? "장착 중" : "", null);

            var consumable = _selectedConsumable ?? (_selectedPending >= 0 && _selectedPending < Pending.Count ? Pending[_selectedPending].Consumable : null);
            if (consumable != null) ShowConsumable(_selectedCard, consumable);
            else ShowGear(_selectedCard, selected, selected != null ? "선택" : "", equipped);
        }

        private List<LootDrop> Pending => BoardSession.Instance != null ? BoardSession.Instance.PendingLoot : new List<LootDrop>();

        private void ShowGear(CardView card, GearItem item, string footer, GearItem compareTo)
        {
            if (item == null)
            {
                card.Header.text = "";
                card.Name.text = "";
                card.Main.text = "";
                card.Body.text = card == _equippedCard ? "장착한 장비 없음" : "목록에서 장비나 소모품을 고르세요";
                card.Footer.text = "";
                return;
            }
            card.Header.text = $"{item.Slot.Label()} · {item.Grade.Label()} · 착용 Lv {item.WearLevel}";
            card.Name.text = item.BaseName;
            var lines = new List<string>();
            if (item.IsWeapon)
            {
                card.Main.text = $"{item.AttackBonus} {(item.IsMagicWeapon ? "마력" : "물리 공격")}";
                lines.Add($"{item.Weapon.Category.Label()} · 슬롯 {item.Weapon.SlotCount}{(item.Weapon.BaseAttackMPCost > 0 ? $" · 기본 공격 MP {item.Weapon.BaseAttackMPCost}" : "")}");
                if (!string.IsNullOrEmpty(item.Weapon.WearStatBonusDescription)) lines.Add(item.Weapon.WearStatBonusDescription);
                if (!string.IsNullOrEmpty(item.Weapon.CritEffectDescription)) lines.Add(item.Weapon.CritEffectDescription);
            }
            else
            {
                card.Main.text = item.PhysicalDefense > 0 || item.MagicDefense > 0 ? $"방어 {item.PhysicalDefense} / {item.MagicDefense}" : StatText(item.StatBonus);
                if (item.MaxHPBonus > 0) lines.Add($"최대 HP +{item.MaxHPBonus}");
                if (item.MaxMPBonus > 0) lines.Add($"최대 MP +{item.MaxMPBonus}");
                string stats = StatText(item.StatBonus);
                if (stats.Length > 0 && card.Main.text != stats) lines.Add(stats);
                if (item.MaxFocusBonus > 0) lines.Add($"최대 집중력 +{item.MaxFocusBonus}");
                if (!string.IsNullOrEmpty(item.Armor.UniqueEffectDescription)) lines.Add(item.Armor.UniqueEffectDescription);
            }
            lines.Add($"가격 {item.Price} Cent{(item.SellPrice > 0 ? $" · 되팔기 {item.SellPrice}" : "")}");
            if (!item.CanEquip(_member, out string why)) lines.Add($"<color=#E0675A>{why}</color>");
            if (compareTo != null || card == _selectedCard) lines.Add(DiffText(item, compareTo));
            card.Body.text = string.Join("\n", lines.Where(l => !string.IsNullOrEmpty(l)));
            card.Footer.text = footer;
        }

        private void ShowConsumable(CardView card, ConsumableDefinition def)
        {
            card.Header.text = "소모품";
            card.Name.text = def.DisplayName;
            card.Main.text = $"보유 {_party.Inventory.CountOf(def)} / {def.MaxStack}";
            string where = (def.Usable & UseContext.Battle) != 0 && (def.Usable & UseContext.World) != 0 ? "전투·월드"
                : (def.Usable & UseContext.Battle) != 0 ? "전투" : (def.Usable & UseContext.World) != 0 ? "월드" : "사용 불가 (재료 등)";
            card.Body.text = $"{def.EffectDescription}\n사용: {where}{(def.IsImplemented ? "" : "\n<color=#E0675A>아직 연결 안 됨</color>")}\n가격 {def.Price} Cent";
            card.Footer.text = "선택";
        }

        // 장착하면 바뀌는 수치 (지금 슬롯 장비 대비)
        public static string DiffText(GearItem next, GearItem current) // 상점 비교 카드도 사용
        {
            var parts = new List<string>();
            void Add(string label, int delta) { if (delta != 0) parts.Add($"<color={(delta > 0 ? "#73D973" : "#E0675A")}>{label} {(delta > 0 ? "+" : "")}{delta}</color>"); }
            int Atk(GearItem g, bool magic) => g != null && g.IsWeapon && g.IsMagicWeapon == magic ? g.AttackBonus : 0;
            Add("공격", Atk(next, false) - Atk(current, false));
            Add("마력", Atk(next, true) - Atk(current, true));
            Add("물리 방어", next.PhysicalDefense - (current?.PhysicalDefense ?? 0));
            Add("마법 방어", next.MagicDefense - (current?.MagicDefense ?? 0));
            Add("최대 HP", next.MaxHPBonus - (current?.MaxHPBonus ?? 0));
            Add("최대 MP", next.MaxMPBonus - (current?.MaxMPBonus ?? 0));
            Add("집중", next.MaxFocusBonus - (current?.MaxFocusBonus ?? 0));
            var a = next.StatBonus;
            var b = current?.StatBonus ?? default;
            foreach (StatKind k in Enum.GetValues(typeof(StatKind))) Add(StatLabel(k), a.Get(k) - b.Get(k));
            return parts.Count > 0 ? "장착하면: " + string.Join("  ", parts) : "장착하면: 변화 없음";
        }

        private static string StatText(StatBlock s)
        {
            var parts = new List<string>();
            foreach (StatKind k in Enum.GetValues(typeof(StatKind)))
            {
                int v = s.Get(k);
                if (v != 0) parts.Add($"{StatLabel(k)} {(v > 0 ? "+" : "")}{v}");
            }
            return string.Join(", ", parts);
        }

        private static string StatLabel(StatKind k) => k switch
        {
            StatKind.Strength => "힘",
            StatKind.Speed => "속도",
            StatKind.Vitality => "활력",
            StatKind.Intelligence => "지력",
            StatKind.Luck => "행운",
            StatKind.Perception => "인지",
            _ => k.ToString(),
        };

        // ───────────── 버튼 ─────────────
        private void FillActions(PartyInventory inv)
        {
            bool gearSel = _selectedGear != null;
            bool slotSel = _selectedSlot.HasValue && _member.GetEquipped(_selectedSlot.Value) != null;
            bool consSel = _selectedConsumable != null;
            bool pendSel = _selectedPending >= 0 && _selectedPending < Pending.Count;

            _equip.gameObject.SetActive(gearSel);
            _equip.interactable = gearSel && _selectedGear.CanEquip(_member, out _);
            _unequip.gameObject.SetActive(slotSel);
            _discard.gameObject.SetActive(gearSel || consSel || pendSel);
            _use.gameObject.SetActive(consSel);
            _use.interactable = consSel && _selectedConsumable.UsableIn(UseContext.World);
            _take.gameObject.SetActive(pendSel);
            foreach (var q in _quickButtons) q.gameObject.SetActive(consSel && _selectedConsumable.UsableIn(UseContext.Battle) || consSel && _selectedConsumable.UsableIn(UseContext.World));
        }

        private void OnEquip()
        {
            if (_selectedGear == null) return;
            var item = _selectedGear;
            if (_member.TryEquip(item, _party.Inventory, out string why)) Say($"{_member.Job.DisplayName} — {item.DisplayName} 장착");
            else Say(why);
            ClearSelection();
            SaveGame.Checkpoint("인벤토리 변경");
            Refresh();
        }

        private void OnUnequip()
        {
            if (!_selectedSlot.HasValue) return;
            var item = _member.GetEquipped(_selectedSlot.Value);
            if (_member.TryUnequip(_selectedSlot.Value, _party.Inventory, out string why)) Say($"{item.DisplayName} 해제 → 인벤토리");
            else Say(why);
            ClearSelection();
            SaveGame.Checkpoint("인벤토리 변경");
            Refresh();
        }

        private void OnDiscard()
        {
            var inv = _party.Inventory;
            if (_selectedGear != null) { Say($"{_selectedGear.DisplayName} 버림"); inv.RemoveGear(_selectedGear); }
            else if (_selectedConsumable != null) { Say($"{_selectedConsumable.DisplayName} 1개 버림"); inv.RemoveConsumable(_selectedConsumable); }
            else if (_selectedPending >= 0 && _selectedPending < Pending.Count) { Say($"{Pending[_selectedPending].Name} 버림"); Pending.RemoveAt(_selectedPending); }
            ClearSelection();
            SaveGame.Checkpoint("인벤토리 변경");
            Refresh();
        }

        private void OnTake()
        {
            if (_selectedPending < 0 || _selectedPending >= Pending.Count) return;
            var drop = Pending[_selectedPending];
            if (LootRules.TryAdd(_party.Inventory, drop)) { Pending.RemoveAt(_selectedPending); Say($"{drop.Name} 받음"); ClearSelection(); }
            else Say("인벤토리가 가득 참 — 먼저 장비를 버리거나 정리하세요");
            SaveGame.Checkpoint("인벤토리 변경");
            Refresh();
        }

        private void OnUse()
        {
            if (_selectedConsumable == null) return;
            if (WorldUseAllowed == null || !WorldUseAllowed()) { Say("보드의 아군 턴(이동 전·후, 이동 중 아님)에만 쓸 수 있음"); return; }
            UseWorldItem(_selectedConsumable, _member);
        }

        // 보드 퀵 슬롯 클릭도 여기로 (보드 컨트롤러가 호출)
        public static string UseWorldItem(ConsumableDefinition def, PartyMember target)
        {
            var session = BoardSession.Instance;
            int bonus = session.PendingAmbushBonus;
            var result = ConsumableRules.UseInWorld(def, target, session.Party, session.UsedWorldItemThisTurn, ref bonus);
            session.PendingAmbushBonus = bonus;
            SaveGame.Checkpoint("아이템 사용");
            if (Instance != null) { Instance.Say(result.Message); Instance.Refresh(); }
            Debug.Log("[Inventory] " + result.Message);
            if (result.EndsAllyTurn) { Close(); AllyTurnEndRequested?.Invoke(); }
            return result.Message;
        }

        private void OnQuickAssign(int slot)
        {
            if (_selectedConsumable == null) return;
            _member.QuickSlots[slot] = _member.QuickSlots[slot] == _selectedConsumable ? null : _selectedConsumable;
            Say(_member.QuickSlots[slot] != null ? $"{_member.Job.DisplayName} 퀵 슬롯 {slot + 1} ← {_selectedConsumable.DisplayName}" : $"퀵 슬롯 {slot + 1} 비움");
            Refresh();
        }

        private void Say(string text) => _message.text = text;

        // ───────────── 구성 ─────────────
        private InventoryPanel Build()
        {
            Instance = this;
            var canvasGO = new GameObject("InventoryCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            // 뒤 화면 클릭 막는 어두운 판
            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.55f));
            _root = dim.gameObject;
            dim.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            var win = UGUIKit.CreateThemedPanel(dim.transform, "Window", new Vector2(0.1f, 0.24f), new Vector2(0.9f, 0.93f), Gold, Fill, 2f, UITheme.Current.PanelFrame);
            win.parent.gameObject.AddComponent<Button>(); // 창 안 클릭은 닫기로 안 넘어가게
            var close = UGUIKit.CreateThemedButton(win, "Close", new Vector2(0.955f, 0.92f), new Vector2(0.995f, 0.995f), "✕", RowFill, 14);
            close.onClick.AddListener(Close);

            BuildCards(win);
            BuildCharacter(win);
            _message = Label(win, "Message", new Vector2(0.01f, 0.005f), new Vector2(0.42f, 0.07f), 11, TextAnchor.MiddleLeft, Gold, false);
            _root.SetActive(false);
            return this;
        }

        private void BuildCards(Transform win)
        {
            _equippedCard = BuildCard(win, "Equipped", new Vector2(0.01f, 0.2f), new Vector2(0.205f, 0.98f));
            _selectedCard = BuildCard(win, "Selected", new Vector2(0.215f, 0.2f), new Vector2(0.42f, 0.98f));

            _equip = ActionButton(win, "장착", 0, OnEquip);
            _unequip = ActionButton(win, "해제", 0, OnUnequip);
            _use = ActionButton(win, "사용", 0, OnUse);
            _take = ActionButton(win, "받기", 0, OnTake);
            _discard = ActionButton(win, "버리기", 1, OnDiscard);
            for (int q = 0; q < PartyMember.QuickSlotCount; q++)
            {
                int slot = q;
                float x0 = 0.215f + q * 0.052f;
                var b = UGUIKit.CreateThemedButton(win, $"Quick{q}", new Vector2(x0, 0.08f), new Vector2(x0 + 0.048f, 0.18f), $"퀵{q + 1}", RowFill, 10);
                b.onClick.AddListener(() => OnQuickAssign(slot));
                _quickButtons.Add(b);
            }
        }

        private static Button ActionButton(Transform win, string label, int column, UnityEngine.Events.UnityAction onClick)
        {
            float x0 = 0.01f + column * 0.1f;
            var b = UGUIKit.CreateThemedButton(win, label, new Vector2(x0, 0.08f), new Vector2(x0 + 0.095f, 0.18f), label, RowFill, 12);
            b.onClick.AddListener(onClick);
            return b;
        }

        private CardView BuildCard(Transform win, string name, Vector2 min, Vector2 max)
        {
            var fill = UGUIKit.CreateThemedPanel(win, name + "Card", min, max, Gold, CardFill, 1.5f, UITheme.Current.PanelFrame);
            var card = new CardView
            {
                Header = Label(fill, "Header", new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.99f), 10, TextAnchor.MiddleLeft, TextDim, false),
                Name = Label(fill, "Name", new Vector2(0.04f, 0.8f), new Vector2(0.96f, 0.9f), 15, TextAnchor.MiddleLeft, TextLight, true),
                Main = Label(fill, "Main", new Vector2(0.04f, 0.69f), new Vector2(0.96f, 0.8f), 17, TextAnchor.MiddleLeft, Gold, true),
                Body = Label(fill, "Body", new Vector2(0.04f, 0.1f), new Vector2(0.96f, 0.68f), 11, TextAnchor.UpperLeft, TextLight, false),
                Footer = Label(fill, "Footer", new Vector2(0.04f, 0.01f), new Vector2(0.96f, 0.09f), 11, TextAnchor.MiddleCenter, Gold, true),
            };
            card.Body.supportRichText = true;
            card.Body.horizontalOverflow = HorizontalWrapMode.Wrap;
            return card;
        }

        private void BuildCharacter(Transform win)
        {
            var fill = UGUIKit.CreateThemedPanel(win, "Character", new Vector2(0.43f, 0.01f), new Vector2(0.95f, 0.98f), Gold, CardFill, 1.5f, UITheme.Current.PanelFrame);

            // 캐릭터 탭 3개
            for (int i = 0; i < Party.MaxSize; i++)
            {
                int index = i;
                float x0 = 0.02f + i * 0.2f;
                var b = UGUIKit.CreateThemedButton(fill, $"Member{i}", new Vector2(x0, 0.925f), new Vector2(x0 + 0.19f, 0.99f), "", RowFill, 12);
                b.onClick.AddListener(() =>
                {
                    if (_party == null || index >= _party.Members.Count) return;
                    _member = _party.Members[index];
                    ClearSelection();
                    _message.text = "";
                    Refresh();
                });
                _memberTabs.Add(b);
            }
            _cent = Label(fill, "Cent", new Vector2(0.65f, 0.925f), new Vector2(0.98f, 0.99f), 14, TextAnchor.MiddleRight, Gold, true);

            _levelLine = Label(fill, "Level", new Vector2(0.02f, 0.86f), new Vector2(0.98f, 0.92f), 11, TextAnchor.MiddleLeft, Gold, true);
            _stats = Label(fill, "Stats", new Vector2(0.02f, 0.76f), new Vector2(0.98f, 0.86f), 10, TextAnchor.UpperLeft, TextLight, false);

            // 장비 5칸 (왼쪽) + 3D 초상 (오른쪽)
            foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
            {
                int i = (int)slot;
                float y1 = 0.755f - i * 0.064f;
                var b = UGUIKit.CreateThemedButton(fill, $"Slot{slot}", new Vector2(0.02f, y1 - 0.058f), new Vector2(0.66f, y1), "", RowFill, 11);
                var label = b.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                ((RectTransform)label.transform).offsetMin = new Vector2(8f, 0f);
                var s = slot;
                b.onClick.AddListener(() =>
                {
                    ClearSelection();
                    _selectedSlot = s;
                    _message.text = "";
                    Refresh();
                });
                _slotRows.Add((b, label));
            }
            _portrait = new PortraitView(fill, "Portrait", new Vector2(0.835f, 0.6f), 120f, Gold);

            // 목록 탭 + 줄
            string[] tabs = { "장비", "소모품", "보관함" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                float x0 = 0.02f + i * 0.2f;
                var b = UGUIKit.CreateThemedButton(fill, $"Tab{i}", new Vector2(x0, 0.355f), new Vector2(x0 + 0.19f, 0.41f), tabs[i], RowFill, 11);
                b.onClick.AddListener(() => { _tab = (Tab)index; _page = 0; ClearSelection(); Refresh(); });
                _tabButtons.Add((b, b.GetComponentInChildren<Text>()));
            }
            var prev = UGUIKit.CreateThemedButton(fill, "Prev", new Vector2(0.78f, 0.355f), new Vector2(0.84f, 0.41f), "◀", RowFill, 11);
            prev.onClick.AddListener(() => { _page--; Refresh(); });
            var next = UGUIKit.CreateThemedButton(fill, "Next", new Vector2(0.92f, 0.355f), new Vector2(0.98f, 0.41f), "▶", RowFill, 11);
            next.onClick.AddListener(() => { _page++; Refresh(); });
            _pageLabel = Label(fill, "Page", new Vector2(0.84f, 0.355f), new Vector2(0.92f, 0.41f), 10, TextAnchor.MiddleCenter, TextDim, false);

            for (int r = 0; r < RowsPerPage; r++)
            {
                int index = r;
                float y1 = 0.345f - r * 0.037f;
                var bg = UGUIKit.CreateImage(fill, $"Row{r}", new Vector2(0.02f, y1 - 0.034f), new Vector2(0.98f, y1), RowFill);
                var b = bg.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.onClick.AddListener(() => OnRowClicked(index));
                var name = Label(bg.transform, "Name", new Vector2(0.02f, 0f), new Vector2(0.7f, 1f), 11, TextAnchor.MiddleLeft, TextLight, false);
                var right = Label(bg.transform, "Right", new Vector2(0.7f, 0f), new Vector2(0.98f, 1f), 10, TextAnchor.MiddleRight, TextDim, false);
                _rows.Add((b, bg, name, right));
            }
        }

        private static Text Label(Transform parent, string name, Vector2 min, Vector2 max, int size, TextAnchor align, Color color, bool bold)
        {
            var text = UGUIKit.CreateText(parent, name, min, max, "", size, align);
            text.color = color;
            if (bold) text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;
            return text;
        }

        public void DevEquipSelected() => OnEquip();

        // 에디터 미리보기용
        public void DevSelectFirst(int tab)
        {
            _tab = (Tab)tab;
            _page = 0;
            OnRowClicked(0);
        }
    }
}
