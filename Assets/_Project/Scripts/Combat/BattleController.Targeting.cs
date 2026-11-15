using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using Fantasia.Data;
using Fantasia.Items;
using Fantasia.UI;
using Fantasia.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Fantasia.Combat
{
    // 대상 선택 (카드·전장 클릭) (11/15 긴 파일 정리: BattleController.cs에서 옮김, 내용 그대로)
    public partial class BattleController
    {
        // ───────────── 11/12 아군 대상: 아래 캐릭터 카드로 고르기 ─────────────
        // 가운데 행동 창이 전장의 아군 모델을 가려서, 아군 대상(회복·강화 스킬, 아이템, 부활)을 고르는 동안 카드에 "대상" 덮개를 띄움.
        private object _cardPickKey;
        private void SyncTargetCards()
        {
            bool want = _phase == Phase.ChoosingTarget && _targetAllies;
            object key = !want ? null : _reviving ? "부활" : _pendingItem != null ? _pendingItem : _pendingSkill != null ? _pendingSkill : (object)"아군";
            if (Equals(key, _cardPickKey)) return;
            _cardPickKey = key;
            PartyHudPanel.SetTargetPick(want ? m => IsValidTarget(_allies.FirstOrDefault(a => a.Member == m)) : null);
        }

        private void OnTargetCardClicked(PartyMember member)
        {
            if (_phase != Phase.ChoosingTarget || !_targetAllies) return;
            OnUnitPicked(_allies.FirstOrDefault(a => a.Member == member));
        }

        // ───────────── 대상 선택 (전장 클릭) ─────────────
        private void Update()
        {
            SyncTargetCards();
            if (PauseMenu.IsOpen) return; // 게임 중 메뉴 — 진행 일시정지
            // 사건 기록 (Q) — 전투 중엔 열람만
            if (Input.GetKeyDown(KeyCode.Q) && !DialoguePanel.IsOpen) CaseLogPanel.Toggle();
            if (CaseLogPanel.IsOpen) return;
            ConsumesEscape = _phase == Phase.ChoosingTarget || _escConsumedFrame == Time.frameCount;
            // 11/14 팀장 QA: 카드 1~4번 칸은 숫자키로도 (행동 중인 캐릭터의 칸)
            if (_phase == Phase.ChoosingAction && _current != null && _current.IsAlly && !DialoguePanel.IsOpen)
                for (int k = 0; k < PartyMember.QuickSlotCount; k++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + k) || Input.GetKeyDown(KeyCode.Keypad1 + k)) { OnQuickSlotClicked(_current.Member, k); break; }
            if (_phase != Phase.ChoosingTarget) return;

            // 11/11 취소: 우클릭 또는 Esc. 예전엔 우클릭만, 적 카드 강조가 남아 취소됐는지 헷갈렸음
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            if (Input.GetMouseButtonDown(1) || esc)
            {
                if (esc) _escConsumedFrame = Time.frameCount;
                CancelTargeting();
                ConsumesEscape = true;
                return;
            }

            BattleUnit hovered = null;
            var cardHover = PartyHudPanel.HoveredTargetCard != null ? _allies.FirstOrDefault(a => a.Member == PartyHudPanel.HoveredTargetCard) : null;
            if (cardHover != null && IsValidTarget(cardHover)) hovered = cardHover; // 11/12 아래 카드에 마우스 → 전장 표시·미리보기도 같이
            else if (!UIFlow.PointerBlockedByUI() && _camera != null)
                hovered = TargetUnderPointer(Input.mousePosition);

            if (hovered != _hovered)
            {
                _hovered = hovered;
                if (hovered != null) ShowMarker(hovered); else HideMarker();
                _hud.RefreshEnemies(hovered);
                _hud.SetHint(hovered != null ? PreviewText(_current, hovered) : TargetingHint());
            }

            if (hovered != null && Input.GetMouseButtonDown(0)) OnUnitPicked(hovered);
        }

        // 11/15 팀장 "캐릭터 차례에 마우스로 대상을 지정해도 안 됨":
        // ① 예전엔 광선이 처음 맞은 것만 봄 → 앞에 선 아군·소품(관·탁자·수레 콜라이더)에 막히면 뒤의 적이 안 잡힘.
        //    이제 광선이 지나는 모든 것 중 가장 가까운 "고를 수 있는 대상".
        // ② 모델이 작으면(사냥개·쥐) 콜라이더가 작아 빗맞음 → 화면에서 모델 가까이(60px)면 가장 가까운 대상.
        private BattleUnit TargetUnderPointer(Vector2 screen)
        {
            var hits = Physics.RaycastAll(_camera.ScreenPointToRay(screen), 200f, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits.OrderBy(h => h.distance))
                if (_targets.TryGetValue(h.collider, out var u) && IsValidTarget(u)) return u;
            BattleUnit best = null; float bestD = PickSlackPx * (Screen.height / 1080f);
            foreach (var kv in _targets)
            {
                var u = kv.Value; if (kv.Key == null || !IsValidTarget(u)) continue;
                var c = (CapsuleCollider)kv.Key;
                var center = kv.Key.transform.TransformPoint(c.center);
                var sp = _camera.WorldToScreenPoint(center);
                if (sp.z <= 0f) continue;
                // 콜라이더 화면 세로 범위 안의 가장 가까운 점까지 거리
                var top = _camera.WorldToScreenPoint(center + Vector3.up * c.height * 0.5f);
                var bottom = _camera.WorldToScreenPoint(center - Vector3.up * c.height * 0.5f);
                float y = Mathf.Clamp(screen.y, Mathf.Min(top.y, bottom.y), Mathf.Max(top.y, bottom.y));
                float d = Vector2.Distance(screen, new Vector2(sp.x, y));
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
        }
        private const float PickSlackPx = 60f;


        public string DevTargetPickReport()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _targets)
            {
                var u = kv.Value; if (kv.Key == null || !IsValidTarget(u)) continue;
                var c = (CapsuleCollider)kv.Key;
                var sp = _camera.WorldToScreenPoint(kv.Key.transform.TransformPoint(c.center));
                var es = EventSystem.current; string ui = "";
                if (es != null)
                {
                    var hits = new List<RaycastResult>(); es.RaycastAll(new PointerEventData(es) { position = sp }, hits);
                    ui = string.Join(",", hits.Take(3).Select(h => h.gameObject.name));
                }
                var first = Physics.Raycast(_camera.ScreenPointToRay(sp), out var hit, 200f) ? hit.collider.name : "-";
                sb.Append($"{u.DisplayName}@({sp.x:0},{sp.y:0}) 광선첫={first}{(hit.collider == kv.Key ? "" : "(막힘)")} UI=[{ui}] 고름={(TargetUnderPointer(sp) == u ? "O" : "X")}; ");
            }
            return sb.ToString();
        }

        // 에디터 미리보기용 — 공격 대상 선택 중 첫 번째 적에 마우스를 올린 상태를 재현.
        public void PreviewTargeting()
        {
            if (_phase != Phase.ChoosingAction) return;
            var target = _enemies.FirstOrDefault(e => e.InBattle);
            if (target == null) return;
            _pendingSkill = null;
            _targetAllies = false;
            _phase = Phase.ChoosingTarget;
            _hovered = target;
            ShowMarker(target);
            _hud.RefreshEnemies(target);
            _hud.SetHint(PreviewText(_current, target));
        }

        // 대상에 마우스를 올리면 미리보기 — 공격은 슬롯·크리티컬 확률과 피해, 회복은 회복량.
        private string PreviewText(BattleUnit actor, BattleUnit target)
        {
            var skill = _pendingSkill;
            if (skill != null && skill.Effect == SkillEffect.Heal)
            {
                int amount = skill.HealFlat + Mathf.FloorToInt(skill.HealMagicRatio * actor.Member.FinalMagicAttack + 0.0001f);
                return $"{target.DisplayName} — 회복 {Mathf.Min(amount, target.MaxHP - target.CurrentHP)} (HP {target.CurrentHP}/{target.MaxHP})";
            }
            if (skill != null && skill.Effect != SkillEffect.Attack)
                return $"{target.DisplayName} — {skill.DisplayName}: {skill.EffectDescription}";

            int focus = FocusToUse(actor, skill, skill != null ? skill.FocusCost : 0);
            var req = skill != null ? actor.SkillAttack(skill, focus) : actor.BasicAttack(focus);
            float crit = SlotCheck.AllSuccessProbability(req.SlotCount, req.SlotChance, focus);
            int normal = AttackResolver.NormalDamage(req.Coefficient, req.AttackValue, target.Defense().DefenseAgainst(req.Type), req.PierceRatio);
            int critDmg = AttackResolver.CriticalDamage(req.Coefficient, req.AttackValue);
            string evasion = target.HasEvasionState ? $" · 회피 상태: 크리티컬이 아니면 스침 {Mathf.Max(normal > 0 ? 1 : 0, normal / 2)}" : "";
            string name = skill != null ? $"[{skill.DisplayName}] " : "";
            string focusText = focus > 0 ? $" (집중 {focus}개 확정)" : "";
            return $"{name}{target.DisplayName} — 슬롯 {req.SlotCount}개 × {req.SlotChance * 100f:0.0}%{focusText} · 크리티컬 {crit * 100f:0.0}% · 피해 {normal} / 크리 {critDmg}{evasion}";
        }
    }
}
