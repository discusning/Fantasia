using System;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // D20 도시 상점 화면 (Docs/Planning/City.md §4 — 보급품 / 무기 / 방어구·장신구 / 서비스 / 의뢰 탭 + 작업대·팔기).
    // 왼쪽 상품 목록(이름·가격·남은 수), 오른쪽 상세·버튼. 품절은 목록에 남기고 다음 재입고까지 라운드 표시 (City_Shop.md §13).
    // 그림은 UITheme 공용 틀 (PanelFrame·Button 등).
    public class ShopPanel : MonoBehaviour
    {
        public static ShopPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root.activeSelf;
        // 서비스·제작은 월드 행동 종료 → 보드가 아군 턴을 끝낸다
        public static event Action AllyTurnEndRequested;

        private enum Tab { Supplies, Weapons, Gear, Services, Workbench, Sell, Quests }
        private static readonly string[] TabNames = { "보급품", "무기", "방어구·장신구", "서비스", "작업대", "팔기", "의뢰" };

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color Fill = new Color(0.08f, 0.07f, 0.09f, 0.97f);
        private static readonly Color CardFill = new Color(0.12f, 0.11f, 0.13f, 1f);
        private static readonly Color RowFill = new Color(0.16f, 0.15f, 0.17f, 1f);
        private static readonly Color RowSelected = new Color(0.42f, 0.33f, 0.18f, 1f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.55f, 0.53f, 0.5f);
        private static readonly Color Bad = new Color(0.9f, 0.4f, 0.35f);
        private const int RowsPerPage = 10;

        private GameObject _root;
        private CityShopDefinition _shop;
        private Party _party;
        private Func<(bool act2, bool castle)> _stages;
        private Func<int> _completedRounds;
        private Tab _tab;
        private int _page, _selected = -1;

        private Text _title, _cent, _restock, _message, _pageLabel;
        private Text _detailName, _detailBody, _compareName, _compareBody, _subtitle;
        private Image _detailIcon;
        private GameObject _compareCard;
        private readonly List<Image> _rowIcons = new();
        private Button _actionButton;
        private Text _actionLabel;
        private readonly List<(Button button, Image fill, Text name, Text price, Text stock)> _rows = new();
        private readonly List<Button> _tabs = new();

        // 목록 한 줄이 가리키는 것
        private readonly List<object> _shown = new(); // ShopEntry · ShopService · int(레시피) · PartyInventory.Stack · GearItem

        public static ShopPanel EnsureExists()
        {
            if (Instance == null) new GameObject("ShopPanel").AddComponent<ShopPanel>().Build();
            return Instance;
        }

        public static void Open(CityShopDefinition shop, Party party, Func<(bool act2, bool castle)> stages, Func<int> completedRounds)
        {
            if (shop == null || party == null) return;
            var p = EnsureExists();
            p._shop = shop;
            p._party = party;
            p._stages = stages;
            p._completedRounds = completedRounds;
            p._tab = Tab.Supplies;
            p._page = 0;
            p._selected = -1;
            p._message.text = $"어서 오세요 — {shop.Landmark}";
            p._root.SetActive(true);
            p.Refresh();
        }

        public static void Close()
        {
            if (Instance != null) Instance._root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private ShopState State => BoardSession.Instance.Shop;

        // ───────────── 목록 ─────────────
        private void BuildList()
        {
            _shown.Clear();
            var jobs = _party.Members.Select(m => m.Job.Id).ToList();
            IEnumerable<ShopEntry> Entries(Func<ShopEntry, bool> kind) =>
                _shop.Entries.Where(e => kind(e) && !ShopRules.IsHidden(e, jobs));
            switch (_tab)
            {
                case Tab.Supplies: _shown.AddRange(Entries(e => e.Item is ConsumableDefinition)); break;
                case Tab.Weapons: _shown.AddRange(Entries(e => e.Item is WeaponDefinition)); break;
                case Tab.Gear: _shown.AddRange(Entries(e => e.Item is ArmorDefinition)); break;
                case Tab.Services: _shown.AddRange(new object[] { ShopService.Inn, ShopService.Treatment, ShopService.Meditation }); break;
                case Tab.Workbench: for (int i = 0; i < ShopRules.Recipes.Length; i++) _shown.Add(i); break;
                case Tab.Sell:
                    _shown.AddRange(_party.Inventory.Consumables.Where(s => s.Definition.Price > 0).Cast<object>());
                    _shown.AddRange(_party.Inventory.Gear.Cast<object>());
                    break;
            }
        }

        private void Refresh()
        {
            if (!IsOpen) return;
            BuildList();
            var (act2, castle) = _stages();
            int done = _completedRounds();
            _title.text = $"{_shop.DisplayName} 상점";
            _cent.text = $"{_party.Cent} Cent";
            _restock.text = $"재입고까지 {ShopRules.RoundsUntilRestock(done)}라운드";
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].targetGraphic.color = (int)_tab == i ? RowSelected : RowFill;

            int pages = Mathf.Max(1, (_shown.Count + RowsPerPage - 1) / RowsPerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.text = pages > 1 ? $"{_page + 1}/{pages}" : "";

            for (int r = 0; r < _rows.Count; r++)
            {
                int i = _page * RowsPerPage + r;
                var row = _rows[r];
                bool has = i < _shown.Count;
                row.button.gameObject.SetActive(has);
                if (!has) continue;
                var (name, price, stock, dim) = Describe(_shown[i], act2, castle, done);
                row.name.text = name;
                row.price.text = price;
                row.stock.text = stock;
                row.name.color = dim ? TextDim : TextLight;
                row.stock.color = stock.Contains("품절") || stock.Contains("완료") ? Bad : TextDim;
                row.fill.color = i == _selected ? RowSelected : RowFill;
                var icon = IconOf(_shown[i]);
                _rowIcons[r].enabled = icon != null;
                _rowIcons[r].sprite = icon;
            }
            _subtitle.text = _shop.Landmark;

            if (_tab == Tab.Quests)
            {
                _detailName.text = "의뢰";
                _detailBody.text = "도시 의뢰는 퀘스트 시스템(D22)과 함께 열립니다.\n의뢰별 최초 1회 보상 (1막 80 Cent · 경험치 40).";
                _actionButton.gameObject.SetActive(false);
                _compareCard.SetActive(false);
                _detailIcon.enabled = false;
                return;
            }
            ShowDetail(_selected >= 0 && _selected < _shown.Count ? _shown[_selected] : null, act2, castle, done);
        }

        private (string name, string price, string stock, bool dim) Describe(object o, bool act2, bool castle, int done)
        {
            switch (o)
            {
                case ShopEntry e:
                    bool unlocked = ShopRules.StageUnlocked(e.Stage, act2, castle);
                    int left = unlocked ? ShopRules.StockOf(State, _shop, e) : 0;
                    string stock = !unlocked ? "잠김" : e.Limited ? (left > 0 ? "한정 1" : "구매 완료")
                        : left > 0 ? $"{left}/{e.MaxStock}" : $"품절 · {ShopRules.RoundsUntilRestock(done)}R 후";
                    return (e.DisplayName, $"{e.Price}", stock, !unlocked || left <= 0);
                case ShopService s:
                    int n = _party.AliveMembers.Count();
                    return (ShopRules.ServiceLabel(s), $"{ShopRules.ServicePrice(s) * n}", $"{ShopRules.ServicePrice(s)} × {n}명", false);
                case int recipe:
                    var (id, reagents, fee) = ShopRules.Recipes[recipe];
                    var result = ItemCatalog.Current.Consumable(id);
                    return (result != null ? result.DisplayName : id, $"{fee}", $"시약 {reagents}", false);
                case PartyInventory.Stack st:
                    return ($"{st.Definition.DisplayName} ×{st.Count}", $"+{ShopRules.SellPrice(st.Definition)}", "소모품", false);
                case GearItem g:
                    return (g.DisplayName, $"+{g.SellPrice}", g.Slot.Label(), g.SellPrice == 0);
            }
            return ("?", "", "", true);
        }

        private static Sprite IconOf(object o) => o switch
        {
            ShopEntry { Item: WeaponDefinition w } => w.Icon,
            ShopEntry { Item: ArmorDefinition a } => a.Icon,
            ShopEntry { Item: ConsumableDefinition c } => c.Icon,
            PartyInventory.Stack st => st.Definition.Icon,
            GearItem g => g.Icon,
            _ => null,
        };

        // 장비 상품이면: 장착할 수 있는 파티원(무기는 계열 맞는 첫 사람)의 같은 부위 장비를 비교 카드에
        private void ShowCompare(object o)
        {
            GearItem candidate = o switch
            {
                ShopEntry { Item: WeaponDefinition w } => new GearItem(w),
                ShopEntry { Item: ArmorDefinition a } => new GearItem(a),
                _ => null,
            };
            _compareCard.SetActive(candidate != null);
            if (candidate == null) return;
            var member = _party.Members.FirstOrDefault(m => !m.IsPermanentlyDead && candidate.CanEquip(m, out _))
                         ?? _party.Members.FirstOrDefault(m => !m.IsPermanentlyDead && (!candidate.IsWeapon || System.Array.IndexOf(m.Job.AllowedWeaponCategories ?? new WeaponCategory[0], candidate.Weapon.Category) >= 0));
            if (member == null)
            {
                _compareName.text = "장착할 파티원 없음";
                _compareBody.text = "";
                return;
            }
            var current = member.GetEquipped(candidate.Slot);
            _compareName.text = $"{member.Job.DisplayName} · {(current != null ? current.DisplayName : "비어 있음")}";
            string can = candidate.CanEquip(member, out string why) ? "" : $"\n<color=#E0675A>지금은 착용 불가: {why}</color>";
            _compareBody.text = (current != null ? ItemText(current.IsWeapon ? (ScriptableObject)current.Weapon : current.Armor) : $"{candidate.Slot.Label()} 칸이 비어 있음")
                                + $"\n\n{InventoryPanel.DiffText(candidate, current)}{can}";
        }

        private void ShowDetail(object o, bool act2, bool castle, int done)
        {
            _actionButton.gameObject.SetActive(o != null);
            var icon = IconOf(o);
            _detailIcon.enabled = icon != null;
            _detailIcon.sprite = icon;
            // 아이콘이 없으면(아트 전) 이름·설명을 카드 위쪽부터
            var nameRt = _detailName.rectTransform;
            var bodyRt = _detailBody.rectTransform;
            nameRt.anchorMin = new Vector2(0.05f, icon != null ? 0.58f : 0.86f);
            nameRt.anchorMax = new Vector2(0.95f, icon != null ? 0.68f : 0.98f);
            bodyRt.anchorMax = new Vector2(0.95f, icon != null ? 0.57f : 0.85f);
            ShowCompare(o);
            if (o == null)
            {
                _detailName.text = "";
                _detailBody.text = _tab == Tab.Services ? "서비스는 파티 전원에게 적용되고, 이용하면 아군 턴이 끝납니다."
                    : _tab == Tab.Workbench ? "시약과 제작비로 1개를 확정 제작합니다. 제작하면 아군 턴이 끝납니다."
                    : _tab == Tab.Sell ? "되팔기는 정상 가격의 40% (최초 지급 장비는 0). 판 물건은 상점 재고에 더해지지 않습니다."
                    : "목록에서 상품을 고르세요.";
                return;
            }
            switch (o)
            {
                case ShopEntry e:
                    _detailName.text = e.DisplayName;
                    var lines = new List<string> { $"가격 {e.Price} Cent" };
                    if (!ShopRules.StageUnlocked(e.Stage, act2, castle)) lines.Add($"<color=#E0675A>{ShopRules.StageLabel(e.Stage)} 판매</color>");
                    lines.Add(ItemText(e.Item));
                    if (e.Limited) lines.Add("한정품 — 플레이 전체 1개, 재입고 없음");
                    _detailBody.text = string.Join("\n", lines);
                    _actionLabel.text = "구입";
                    break;
                case ShopService s:
                    _detailName.text = ShopRules.ServiceLabel(s);
                    _detailBody.text = $"{ShopRules.ServiceDescription(s)}\n파티 전원 · 1인 {ShopRules.ServicePrice(s)} Cent\n이용하면 아군 턴 종료";
                    _actionLabel.text = "이용";
                    break;
                case int recipe:
                    var (id, reagents, fee) = ShopRules.Recipes[recipe];
                    var result = ItemCatalog.Current.Consumable(id);
                    var reagent = ItemCatalog.Current.Consumable(ConsumableIds.Reagent);
                    _detailName.text = result != null ? result.DisplayName : id;
                    _detailBody.text = $"{(result != null ? result.EffectDescription : "")}\n재료: 연금술 시약 {reagents} (보유 {(reagent != null ? _party.Inventory.CountOf(reagent) : 0)}) · 제작비 {fee} Cent\n제작하면 아군 턴 종료";
                    _actionLabel.text = "제작";
                    break;
                case PartyInventory.Stack st:
                    _detailName.text = st.Definition.DisplayName;
                    _detailBody.text = $"{st.Definition.EffectDescription}\n보유 {st.Count} · 1개 판매가 {ShopRules.SellPrice(st.Definition)} Cent";
                    _actionLabel.text = "1개 팔기";
                    break;
                case GearItem g:
                    _detailName.text = g.DisplayName;
                    _detailBody.text = $"{ItemText(g.IsWeapon ? (ScriptableObject)g.Weapon : g.Armor)}\n판매가 {g.SellPrice} Cent{(g.SellPrice == 0 ? " (최초 지급 장비)" : "")}";
                    _actionLabel.text = "팔기";
                    break;
            }
        }

        private string ItemText(ScriptableObject item)
        {
            switch (item)
            {
                case ConsumableDefinition c:
                    return $"{c.EffectDescription}\n보유 {_party.Inventory.CountOf(c)} / {c.MaxStack}";
                case WeaponDefinition w:
                    var who = _party.Members.Where(m => w.AllowedJobs != null && w.AllowedJobs.Contains(m.Job.Id)).Select(m => m.Job.DisplayName);
                    return $"{w.Category.Label()} · {w.Grade.Label()} · 착용 Lv {w.WearLevel}\n{(w.Category.IsMagic() ? "마력" : "공격")} +{w.ComputeAttackBonus()} · 슬롯 {w.SlotCount}\n{w.WearStatBonusDescription}\n{w.CritEffectDescription}\n장착 가능: {(who.Any() ? string.Join(", ", who) : "파티에 없음")}";
                case ArmorDefinition a:
                    var parts = new List<string> { $"{a.Slot.Label()} · {a.Grade.Label()} · 착용 Lv {a.WearLevel}" };
                    if (a.PhysicalDefense > 0 || a.MagicDefense > 0) parts.Add($"방어 {a.PhysicalDefense} / {a.MagicDefense}");
                    if (a.MaxHPBonus > 0) parts.Add($"최대 HP +{a.MaxHPBonus}");
                    if (a.MaxMPBonus > 0) parts.Add($"최대 MP +{a.MaxMPBonus}");
                    if (a.MaxFocusBonus > 0) parts.Add($"최대 집중력 +{a.MaxFocusBonus}");
                    if (!string.IsNullOrEmpty(a.UniqueEffectDescription)) parts.Add(a.UniqueEffectDescription);
                    return string.Join("\n", parts);
            }
            return "";
        }

        // ───────────── 행동 ─────────────
        private void OnAction()
        {
            if (_selected < 0 || _selected >= _shown.Count) return;
            var (act2, castle) = _stages();
            ShopResult result = _shown[_selected] switch
            {
                ShopEntry e => ShopRules.Buy(State, _shop, e, _party, act2, castle),
                ShopService s => ShopRules.UseService(_party, s),
                int recipe => ShopRules.Craft(_party, ItemCatalog.Current, recipe),
                PartyInventory.Stack st => ShopRules.SellConsumable(_party, st.Definition),
                GearItem g => ShopRules.SellGear(_party, g),
                _ => new ShopResult(false, ""),
            };
            _message.text = result.Message;
            _message.color = result.Ok ? Gold : Bad;
            Debug.Log("[Shop] " + result.Message);
            if (result.Ok && _tab == Tab.Sell) _selected = Mathf.Min(_selected, _shown.Count - 2);
            if (result.Ok && result.EndsAllyTurn)
            {
                Close();
                AllyTurnEndRequested?.Invoke();
                return;
            }
            Refresh();
        }

        // ───────────── 구성 ─────────────
        private ShopPanel Build()
        {
            Instance = this;
            var canvasGO = new GameObject("ShopCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.55f));
            _root = dim.gameObject;
            var win = UGUIKit.CreateThemedPanel(dim.transform, "Window", new Vector2(0.12f, 0.24f), new Vector2(0.88f, 0.93f), Gold, Fill, 2f, UITheme.Current.PanelFrame);
            win.parent.gameObject.AddComponent<Button>(); // 창 안 클릭이 뒤로 새지 않게

            _title = Label(win, "Title", new Vector2(0.02f, 0.935f), new Vector2(0.4f, 0.995f), 18, TextAnchor.MiddleLeft, Gold, true);
            _subtitle = Label(win, "Subtitle", new Vector2(0.02f, 0.895f), new Vector2(0.4f, 0.94f), 11, TextAnchor.MiddleLeft, TextDim, false);
            _restock = Label(win, "Restock", new Vector2(0.4f, 0.9f), new Vector2(0.7f, 0.99f), 11, TextAnchor.MiddleCenter, TextDim, false);
            _cent = Label(win, "Cent", new Vector2(0.7f, 0.9f), new Vector2(0.93f, 0.99f), 15, TextAnchor.MiddleRight, Gold, true);
            UGUIKit.CreateThemedButton(win, "Close", new Vector2(0.945f, 0.91f), new Vector2(0.99f, 0.99f), "✕", RowFill, 14).onClick.AddListener(Close);

            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                float x0 = 0.015f + i * 0.138f;
                var b = UGUIKit.CreateThemedButton(win, $"Tab{i}", new Vector2(x0, 0.81f), new Vector2(x0 + 0.132f, 0.885f), TabNames[i], RowFill, 11);
                b.onClick.AddListener(() => { _tab = (Tab)index; _page = 0; _selected = -1; Refresh(); });
                _tabs.Add(b);
            }

            // 목록 — For the King 상점처럼 "재고 | 아이콘 | 아이템 | 비용" (참고: 01_포더킹1_실제_스크린샷/12_상점_상품과_가격_재고)
            Label(win, "HeadStock", new Vector2(0.015f, 0.755f), new Vector2(0.11f, 0.79f), 10, TextAnchor.MiddleCenter, TextDim, true).text = "재고";
            Label(win, "HeadName", new Vector2(0.15f, 0.755f), new Vector2(0.38f, 0.79f), 10, TextAnchor.MiddleLeft, TextDim, true).text = "아이템";
            Label(win, "HeadPrice", new Vector2(0.38f, 0.755f), new Vector2(0.465f, 0.79f), 10, TextAnchor.MiddleRight, TextDim, true).text = "비용";
            for (int r = 0; r < RowsPerPage; r++)
            {
                int index = r;
                float y1 = 0.75f - r * 0.063f;
                var bg = UGUIKit.CreateImage(win, $"Row{r}", new Vector2(0.015f, y1 - 0.058f), new Vector2(0.47f, y1), RowFill);
                var b = bg.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.onClick.AddListener(() => { _selected = _page * RowsPerPage + index; Refresh(); });
                var stock = Label(bg.transform, "Stock", new Vector2(0.01f, 0f), new Vector2(0.21f, 1f), 10, TextAnchor.MiddleCenter, TextDim, false);
                var icon = UGUIKit.CreateImage(bg.transform, "Icon", new Vector2(0.22f, 0.1f), new Vector2(0.29f, 0.9f), Color.white);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                var name = Label(bg.transform, "Name", new Vector2(0.3f, 0f), new Vector2(0.82f, 1f), 12, TextAnchor.MiddleLeft, TextLight, false);
                var price = Label(bg.transform, "Price", new Vector2(0.82f, 0f), new Vector2(0.98f, 1f), 12, TextAnchor.MiddleRight, Gold, true);
                _rows.Add((b, bg, name, price, stock));
                _rowIcons.Add(icon);
            }
            var prev = UGUIKit.CreateThemedButton(win, "Prev", new Vector2(0.015f, 0.02f), new Vector2(0.07f, 0.08f), "◀", RowFill, 11);
            prev.onClick.AddListener(() => { _page--; Refresh(); });
            var next = UGUIKit.CreateThemedButton(win, "Next", new Vector2(0.13f, 0.02f), new Vector2(0.185f, 0.08f), "▶", RowFill, 11);
            next.onClick.AddListener(() => { _page++; Refresh(); });
            _pageLabel = Label(win, "Page", new Vector2(0.07f, 0.02f), new Vector2(0.13f, 0.08f), 10, TextAnchor.MiddleCenter, TextDim, false);
            _message = Label(win, "Message", new Vector2(0.2f, 0.02f), new Vector2(0.6f, 0.08f), 11, TextAnchor.MiddleLeft, Gold, false);

            // 상세 — 고른 물건 카드 + (장비면) 지금 장착 중인 것 비교 카드 (FTK 상점의 "장착됨" 카드)
            var card = UGUIKit.CreateThemedPanel(win, "Detail", new Vector2(0.48f, 0.12f), new Vector2(0.73f, 0.79f), Gold, CardFill, 1.5f, UITheme.Current.PanelFrame);
            _detailIcon = UGUIKit.CreateImage(card, "Icon", new Vector2(0.3f, 0.68f), new Vector2(0.7f, 0.97f), Color.white);
            _detailIcon.preserveAspect = true;
            _detailIcon.enabled = false;
            _detailName = Label(card, "Name", new Vector2(0.05f, 0.58f), new Vector2(0.95f, 0.68f), 15, TextAnchor.MiddleLeft, TextLight, true);
            _detailBody = Label(card, "Body", new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.57f), 11, TextAnchor.UpperLeft, TextLight, false);
            _detailBody.supportRichText = true;
            var cmp = UGUIKit.CreateThemedPanel(win, "Compare", new Vector2(0.74f, 0.12f), new Vector2(0.985f, 0.79f), Gold, CardFill, 1.5f, UITheme.Current.PanelFrame);
            _compareCard = cmp.parent.gameObject;
            _compareName = Label(cmp, "Name", new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.98f), 14, TextAnchor.MiddleLeft, TextLight, true);
            _compareBody = Label(cmp, "Body", new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.85f), 11, TextAnchor.UpperLeft, TextLight, false);
            _compareBody.supportRichText = true;
            Label(cmp, "Footer", new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.09f), 11, TextAnchor.MiddleCenter, Gold, true).text = "장착됨";
            _compareCard.SetActive(false);
            _actionButton = UGUIKit.CreateThemedButton(win, "Action", new Vector2(0.72f, 0.02f), new Vector2(0.985f, 0.105f), "구입", new Color(0.45f, 0.18f, 0.16f), 14);
            _actionLabel = _actionButton.GetComponentInChildren<Text>();
            _actionButton.onClick.AddListener(OnAction);

            _root.SetActive(false);
            return this;
        }

        private static Text Label(Transform parent, string name, Vector2 min, Vector2 max, int size, TextAnchor align, Color color, bool bold)
        {
            var text = UGUIKit.CreateText(parent, name, min, max, "", size, align);
            text.color = color;
            if (bold) text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;
            return text;
        }

        // ── 자동 플레이 점검용 ──
        public void DevSelectTab(int tab) { _tab = (Tab)tab; _page = 0; _selected = -1; Refresh(); }
        public void DevSelectRow(int row) { _selected = row; Refresh(); }
        public void DevAct() => OnAction();
        public int DevRowCount => _shown.Count;
    }
}
