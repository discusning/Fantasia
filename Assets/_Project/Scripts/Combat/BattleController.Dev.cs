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
    // 자동 플레이 점검용 Dev 함수 (11/15 긴 파일 정리: BattleController.cs에서 옮김, 내용 그대로)
    public partial class BattleController
    {
        // ───── 자동 플레이 점검(Dev/AutoPlayBot)용 — 버튼·클릭과 같은 처리를 부른다 ─────
        public bool DevAwaitingPlayer => _phase == Phase.ChoosingAction;
        public bool DevFinished => _phase == Phase.Finished;
        public void DevCheckEnd() => CheckBattleEnd(); // 자동 플레이: 강제로 바꾼 상태(전멸 등)를 바로 판정
        public BattleUnit DevCurrent => _current;
        public BattleHud DevHud => _hud;
        public IReadOnlyList<BattleUnit> DevAllies => _allies;
        public IReadOnlyList<BattleUnit> DevEnemies => _enemies;
        public int DevRound => _queue != null ? _queue.Round : 0;

        public void DevAttack(int focus)
        {
            if (_phase != Phase.ChoosingAction) return;
            _hud.DevSetFocus(focus);
            OnAttackClicked();
            OnUnitPicked(_enemies.FirstOrDefault(e => e.InBattle));
        }

        // 스킬 버튼 → 스킬 선택 → (필요하면) 대상 클릭과 같은 순서.
        public void DevUseSkill(SkillDefinition skill, BattleUnit target, int focus)
        {
            if (_phase != Phase.ChoosingAction) return;
            _hud.DevSetFocus(focus);
            OnSkillChosen(skill);
            if (_phase == Phase.ChoosingTarget) OnUnitPicked(target);
        }

        // 촬영용 (-autoplay-motions, 11/11): 아군 1명을 쓰러뜨림 / 적 HP를 1로 (다음 공격에 처치 → 승리 순간 촬영)
        public void DevKnockDownAlly(BattleUnit unit)
        {
            if (unit == null || !unit.IsAlly || !unit.IsAlive) return;
            unit.TakeDamage(unit.CurrentHP);
            KnockDown(unit);
            _hud.RefreshEnemies(null);
        }
        public void DevSetEnemyHP1(BattleUnit unit) { if (unit != null && !unit.IsAlly && unit.IsAlive) unit.TakeDamage(unit.CurrentHP - 1); }
        public GameObject DevModelOf(BattleUnit unit) => unit != null && _models.TryGetValue(unit, out var m) ? m : null;
        public Vector3 DevChest(BattleUnit unit) => ModelChest(unit); // 11/13 이펙트 QA
        public Vector3 DevFeet(BattleUnit unit) => ModelFeet(unit);
        public Vector3 DevFront(BattleUnit unit) => ModelFront(unit); // 방어력 감소·출혈 자리 (몸 앞)
        // 11/13 6차 스킬 이펙트 점검: 실제 전투와 같은 자리 규칙(PlaySkillVfx)으로 재생. 회복·정화는 대상 아군
        public void DevPlaySkillVfx(string id, BattleUnit attacker, BattleUnit target)
        {
            var s = ScriptableObject.CreateInstance<SkillDefinition>(); s.Id = id;
            string key = SkillVfxKey(s);
            if (id.EndsWith("emergency_suture")) BattleVfx.Play(key, ModelFeet(target), VfxScale(target));
            else if (id.EndsWith("purge_plague")) BattleVfx.Play(key, ModelFront(target), VfxScale(target));
            else PlaySkillVfx(s, key, attacker, target);
        }
        public Vector3 DevTop(BattleUnit unit) => ModelTop(unit);

        public void DevDefend() => OnDefendClicked();
        public void DevPressItem(ConsumableDefinition item) => OnItemChosen(item); // 11/12 아이템만 고르고 대상은 아직 (카드 클릭 점검)
        public void DevPressSkill(SkillDefinition skill) => OnSkillChosen(skill); // 11/11 스킬 칸 누르기 (대상 고르기 시작·같은 칸 다시 누르면 취소)
        public void DevDefendButton() => OnDefendClicked();
        public bool DevChoosingTarget => _phase == Phase.ChoosingTarget;
        public int DevShieldTotal => _allies.Sum(a => a.Shield);
        public int DevAbsorbed { get; private set; } // 보호막이 막은 피해 합 (자동 점검용)
        public int DevLastHpDamage => _lastDamageDealt;
        // 점검: 보호막 있는 캐릭터의 방패 표시가 "지금 캐릭터 머리 위"에서 얼마나 떨어져 있나 (m)
        public float DevShieldMarkError => _allies.Concat(_enemies).Where(u => u.Shield > 0 && _models.ContainsKey(u)).Select(u => _hud.DevShieldMarkWorld(u) is Vector3 w ? Vector3.Distance(w, ModelTop(u) - Vector3.up * 0.15f) : 99f).DefaultIfEmpty(0f).Max();
        public void DevShieldAll(int amount) { foreach (var a in _allies) if (a.IsAlive && !a.HasRetreated) { a.Shield = amount; ShieldChanged(a); } }
        public void DevRetreat() => OnRetreatClicked();

        // 소모품 메뉴 → 아이템 → (대상 클릭)과 같은 순서.
        public void DevUseItem(ConsumableDefinition item, BattleUnit target)
        {
            if (_phase != Phase.ChoosingAction) return;
            OnItemChosen(item);
            if (_phase == Phase.ChoosingTarget) OnUnitPicked(target);
        }

        // 부활 버튼 → 쓰러진 아군 클릭과 같은 순서.
        public bool DevCanRevive => CanRevive();
        public void DevRevive(BattleUnit target)
        {
            if (_phase != Phase.ChoosingAction) return;
            OnReviveClicked();
            if (_phase == Phase.ChoosingTarget) OnUnitPicked(target);
        }
        public void DevContinue() => _hud.DevPressResult(); // 결과 창 버튼 (보드로 / 처음부터 다시)

        private void ReturnToBoard()
        {
            BoardSession.Instance.PendingEncounterCoord = null;
            Fantasia.Core.SceneFader.Load(BoardSceneName);
        }

        private void OnDestroy()
        {
            PartyHudPanel.QuickSlotClicked -= OnQuickSlotClicked;
            PartyHudPanel.TargetCardClicked -= OnTargetCardClicked;
            PartyHudPanel.ClearShields(); // 전투가 끝나면 보호막 표시도 끝
            ConsumesEscape = false;
        }
    }
}
