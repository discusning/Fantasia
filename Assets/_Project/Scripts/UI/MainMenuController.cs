using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 메인 메뉴 씬 (Scenes/MainMenu/MainMenu.unity, 빌드 첫 씬): 새 게임 → 파티 구성 → 보드 / 이어하기 / 종료.
    // 파티 구성: 3칸, 1번 = 파티장. 칸마다 ◀ ▶로 직업을 바꾸고, 다른 칸에 있는 직업을 고르면 서로 자리를 바꾼다 (☆ 중복 직업 없음).
    // 저장 규칙(기획 요청 15~23)이 정해지기 전이라 이어하기는 지금 저장(아군 턴 시작 자동 저장)을 그대로 불러온다.
    public class MainMenuController : MonoBehaviour
    {
        public const string SceneName = "MainMenu";
        private const string BoardSceneName = "BoardTest";

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color Fill = new Color(0.08f, 0.07f, 0.09f, 0.97f);
        private static readonly Color CardFill = new Color(0.12f, 0.11f, 0.13f, 1f);
        private static readonly Color ButtonFill = new Color(0.16f, 0.15f, 0.17f, 1f);
        private static readonly Color StartFill = new Color(0.45f, 0.18f, 0.16f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);
        private static readonly Color TextDim = new Color(0.55f, 0.53f, 0.5f);

        private GameObject _menu, _partySelect, _confirm;
        private Button _continueButton;
        private Text _continueInfo, _message;
        private JobDefinition[] _jobs;
        private readonly JobDefinition[] _chosen = new JobDefinition[Party.MaxSize];
        private readonly List<(PortraitView portrait, Text name, Text body)> _cards = new();

        private void Start()
        {
            BoardSession.EnsureExists();
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
            var catalog = ItemCatalog.Current;
            _jobs = catalog != null ? catalog.Jobs.Where(j => j != null).OrderBy(j => j.Id).ToArray() : new JobDefinition[0];
            for (int i = 0; i < _chosen.Length && i < _jobs.Length; i++) _chosen[i] = _jobs[i];
            Build();
            ShowMenu();
        }

        // ───────────── 메뉴 ─────────────
        private void ShowMenu()
        {
            _menu.SetActive(true);
            _partySelect.SetActive(false);
            _confirm.SetActive(false);
            var save = SaveGame.ReadFile();
            bool continuable = SaveGame.IsContinuable(save);
            _continueButton.interactable = continuable;
            // 진행 중 회차 우선 → 최근 전멸 기록(이어하기 불가) → 진행·종료 기록 모두 없을 때만 "저장된 게임 없음" (세이브 추가 수정안 3)
            var ended = SaveGame.ReadLastEnded();
            _continueInfo.text = continuable ? ContinueInfo(save)
                : ended != null ? $"이전 회차 — {ended.act} {ended.region} · {ended.worldRound}라운드 전멸"
                : SaveGame.HasSave ? "저장 파일을 불러올 수 없습니다"             // 손상·버전 불일치 (파일은 보관)
                : "저장된 게임 없음";
        }

        // Save.md 7: 막·지역·타일 좌표 / 라운드 / 파티 생존 상태 / 누적 플레이 시간 / 마지막 저장 시각 (+ 전투 진행 중)
        private string ContinueInfo(SaveGame.Data d)
        {
            var (row, col) = Fantasia.Board.WorldMapData.AxialToOffset(new Fantasia.Board.HexCoord(d.player.q, d.player.r));
            string tile = Fantasia.Board.WorldMapData.Label(row, col);
            int alive = d.members.Count(m => !m.dead && m.hp > 0);
            var t = System.TimeSpan.FromSeconds(d.playSeconds);
            string act = d.storyFlags.Contains(ActOneStory.FlagReported) ? "1막 클리어" : "1막";
            return $"{act} · {d.regionName} {tile} · {d.worldRound}라운드 · 생존 {alive}/{d.members.Count}\n"
                   + $"플레이 {(int)t.TotalHours}:{t.Minutes:00} · 저장 {d.savedAt}{(d.inBattle ? " · 전투 진행 중" : "")}";
        }

        private void OnNewGame()
        {
            // 기존 진행이 있으면 덮어쓰기 확인 (Save.md 6)
            if (SaveGame.IsContinuable(SaveGame.ReadFile())) { _confirm.SetActive(true); return; }
            ShowPartySelect();
        }

        private void ShowPartySelect()
        {
            _menu.SetActive(false);
            _confirm.SetActive(false);
            _partySelect.SetActive(true);
            RefreshCards();
        }

        public void OnContinue()
        {
            var save = SaveGame.ReadFile();
            // 1막 클리어 회차: 이어하기 = 클리어 결과 화면 (보상 재지급 없음)
            if (save != null && save.runState == (int)BoardSession.RunState.ActCleared)
            {
                ActClearPanel.Show(save, "닫기", ShowMenu);
                return;
            }
            string error = SaveGame.Apply(save, BoardSession.Instance, ItemCatalog.Current);
            if (error != null)
            {
                _continueInfo.text = "불러오기 실패: " + error;
                return;
            }
            // 전투 진행 중 저장이면 전투 씬에서 마지막 확정 상태로 재개 (Save.md 5)
            SceneManager.LoadScene(BoardSession.Instance.BattleSnapshotJson != null ? "CombatTest" : BoardSceneName);
        }

        // ───────────── 파티 구성 ─────────────
        private void Cycle(int slot, int step)
        {
            if (_jobs.Length == 0) return;
            int index = System.Array.IndexOf(_jobs, _chosen[slot]);
            var next = _jobs[(index + step + _jobs.Length) % _jobs.Length];
            int other = System.Array.IndexOf(_chosen, next);
            if (other >= 0 && other != slot) _chosen[other] = _chosen[slot]; // 이미 다른 칸에 있으면 자리 교환
            _chosen[slot] = next;
            RefreshCards();
        }

        private void RefreshCards()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var job = _chosen[i];
                var (portrait, name, body) = _cards[i];
                portrait.Set(job != null ? job.Portrait : null, job != null ? PortraitStudio.TextureFor(job) : null);
                name.text = job != null ? job.DisplayName : "—";
                body.text = job == null ? "" : string.Join("\n",
                    job.Role,
                    "",
                    $"HP {job.StartingHP}{(job.UsesMP ? $" · MP {job.StartingMP}" : "")} · LP {job.StartingLP}",
                    $"힘 {job.Stats.Strength} · 속도 {job.Stats.Speed} · 활력 {job.Stats.Vitality}",
                    $"지력 {job.Stats.Intelligence} · 행운 {job.Stats.Luck} · 인지 {job.Stats.Perception}",
                    $"시작 무기: {(job.StartingWeapon != null ? job.StartingWeapon.DisplayName : "없음")}",
                    $"기술: {string.Join(", ", (job.Skills ?? new SkillDefinition[0]).Where(s => s != null).Select(s => s.DisplayName))}");
            }
            _message.text = _chosen.All(j => j != null) ? "1번 캐릭터가 파티장 — 보드 이동·기습·몰래 지나가기 판정을 맡습니다." : "직업 데이터가 부족합니다.";
        }

        public void OnDepart()
        {
            if (_chosen.Any(j => j == null)) return;
            var session = BoardSession.Instance;
            session.ResetForNewGame();
            var party = new Party();
            foreach (var job in _chosen) party.AddMember(new PartyMember(job));
            party.GiveStartingItems(ItemCatalog.Current);
            session.Party = party;
            // 새 회차를 바로 저장 — 성공해야 기존 회차 파일이 교체된다 (임시 파일에 쓰고 바꿔치기, Save.md 6)
            if (!SaveGame.Checkpoint("새 게임")) Debug.LogWarning("[SaveGame] 새 게임 저장 실패 — 기존 파일 유지");
            SceneManager.LoadScene(BoardSceneName);
        }

        // ───────────── 구성 ─────────────
        private void Build()
        {
            var canvasGO = new GameObject("MainMenuCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            var bg = UGUIKit.CreateImage(canvasGO.transform, "Background", Vector2.zero, Vector2.one, new Color(0.05f, 0.045f, 0.06f));

            // 메뉴
            _menu = UGUIKit.CreateRect(bg.transform, "Menu", Vector2.zero, Vector2.one).gameObject;
            Label(_menu.transform, "Title", new Vector2(0.2f, 0.66f), new Vector2(0.8f, 0.86f), 64, TextAnchor.MiddleCenter, Gold, true).text = "판타지아";
            Label(_menu.transform, "Subtitle", new Vector2(0.2f, 0.6f), new Vector2(0.8f, 0.67f), 16, TextAnchor.MiddleCenter, TextDim, false).text = "1막 — 런던";
            UGUIKit.CreateThemedButton(_menu.transform, "NewGame", new Vector2(0.4f, 0.46f), new Vector2(0.6f, 0.53f), "새 게임", StartFill, 18).onClick.AddListener(OnNewGame);
            _continueButton = UGUIKit.CreateThemedButton(_menu.transform, "Continue", new Vector2(0.4f, 0.36f), new Vector2(0.6f, 0.43f), "이어하기", ButtonFill, 18);
            _continueButton.onClick.AddListener(OnContinue);
            _continueInfo = Label(_menu.transform, "ContinueInfo", new Vector2(0.2f, 0.285f), new Vector2(0.8f, 0.36f), 11, TextAnchor.MiddleCenter, TextDim, false);
            UGUIKit.CreateThemedButton(_menu.transform, "Settings", new Vector2(0.4f, 0.2f), new Vector2(0.6f, 0.27f), "설정", ButtonFill, 16).onClick.AddListener(() => SettingsPanel.Open());
            UGUIKit.CreateThemedButton(_menu.transform, "Quit", new Vector2(0.4f, 0.11f), new Vector2(0.6f, 0.18f), "종료", ButtonFill, 16).onClick.AddListener(Application.Quit);

            // 새 게임 덮어쓰기 확인
            var dim = UGUIKit.CreateImage(bg.transform, "Confirm", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.6f));
            _confirm = dim.gameObject;
            var box = UGUIKit.CreateThemedPanel(dim.transform, "Box", new Vector2(0.32f, 0.38f), new Vector2(0.68f, 0.62f), Gold, Fill, 2f, UITheme.Current != null ? UITheme.Current.PanelFrame : null);
            Label(box, "Text", new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.92f), 14, TextAnchor.MiddleCenter, TextLight, false).text = "기존 진행 상황이 삭제됩니다.\n새 게임을 시작할까요?";
            UGUIKit.CreateThemedButton(box, "No", new Vector2(0.1f, 0.1f), new Vector2(0.45f, 0.35f), "취소", ButtonFill, 14).onClick.AddListener(ShowMenu);
            UGUIKit.CreateThemedButton(box, "Yes", new Vector2(0.55f, 0.1f), new Vector2(0.9f, 0.35f), "새 게임 시작", StartFill, 14).onClick.AddListener(ShowPartySelect);

            // 파티 구성
            _partySelect = UGUIKit.CreateRect(bg.transform, "PartySelect", Vector2.zero, Vector2.one).gameObject;
            Label(_partySelect.transform, "Title", new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.97f), 26, TextAnchor.MiddleCenter, Gold, true).text = "파티 구성";
            for (int i = 0; i < Party.MaxSize; i++)
            {
                int slot = i;
                float x0 = 0.05f + i * 0.31f;
                var card = UGUIKit.CreateThemedPanel(_partySelect.transform, $"Card{i}", new Vector2(x0, 0.17f), new Vector2(x0 + 0.28f, 0.86f), Gold, CardFill, 1.5f, UITheme.Current != null ? UITheme.Current.PanelFrame : null);
                Label(card, "Slot", new Vector2(0.05f, 0.92f), new Vector2(0.95f, 0.99f), 12, TextAnchor.MiddleCenter, i == 0 ? Gold : TextDim, true).text = i == 0 ? "1번 · 파티장" : $"{i + 1}번";
                var portrait = new PortraitView(card, "Portrait", new Vector2(0.5f, 0.74f), 130f, Gold);
                UGUIKit.CreateThemedButton(card, "Prev", new Vector2(0.04f, 0.52f), new Vector2(0.18f, 0.6f), "◀", ButtonFill, 14).onClick.AddListener(() => Cycle(slot, -1));
                UGUIKit.CreateThemedButton(card, "Next", new Vector2(0.82f, 0.52f), new Vector2(0.96f, 0.6f), "▶", ButtonFill, 14).onClick.AddListener(() => Cycle(slot, +1));
                var name = Label(card, "Name", new Vector2(0.2f, 0.51f), new Vector2(0.8f, 0.61f), 18, TextAnchor.MiddleCenter, TextLight, true);
                var body = Label(card, "Body", new Vector2(0.07f, 0.03f), new Vector2(0.93f, 0.5f), 11, TextAnchor.UpperLeft, TextLight, false);
                _cards.Add((portrait, name, body));
            }
            _message = Label(_partySelect.transform, "Message", new Vector2(0.05f, 0.1f), new Vector2(0.6f, 0.16f), 12, TextAnchor.MiddleLeft, TextDim, false);
            UGUIKit.CreateThemedButton(_partySelect.transform, "Back", new Vector2(0.05f, 0.03f), new Vector2(0.2f, 0.1f), "뒤로", ButtonFill, 14).onClick.AddListener(ShowMenu);
            UGUIKit.CreateThemedButton(_partySelect.transform, "Depart", new Vector2(0.75f, 0.03f), new Vector2(0.95f, 0.1f), "출발", StartFill, 18).onClick.AddListener(OnDepart);
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
        public void DevOpenPartySelect() => ShowPartySelect();
        public void DevCycle(int slot, int step) => Cycle(slot, step);
        public string DevPartyNames => string.Join(", ", _chosen.Select(j => j != null ? j.DisplayName : "-"));
        public string DevContinueInfo => _continueInfo != null ? _continueInfo.text : "";
    }
}
