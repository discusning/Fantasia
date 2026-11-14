using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Board;
using Fantasia.Combat;
using Fantasia.Data;
using Fantasia.UI;
using UnityEngine;

namespace Fantasia.Core
{
    // 11/13 이펙트 QA (-autoplay-vfx-qa): 실제 전투 화면에서 효과를 하나씩 띄워
    //  ① 보이는 시간(알갱이가 하나라도 남은 시간) ② 크기(모델 키 대비 화면 높이) ③ 정리(오브젝트가 사라짐) ④ 스크린샷 을 확인.
    // 이어서 디버프 둘레 효과: 켜짐·한 모델 최대 2개·강화는 표시 안 함·풀리면 꺼짐·쓰러지면 꺼짐·종류별 화면·프레임.
    public partial class AutoPlayBot
    {
        private struct VfxSpec
        {
            public string Key; public bool OnAlly, Chest; public float ShotAt, MaxSeconds, MaxRatio;
            public VfxSpec(string key, bool onAlly, bool chest, float shotAt, float maxSeconds, float maxRatio)
            { Key = key; OnAlly = onAlly; Chest = chest; ShotAt = shotAt; MaxSeconds = maxSeconds; MaxRatio = maxRatio; }
        }

        // 기준: 평범한 동작은 짧고 작게(모델 키보다 작게), 크리티컬·부활·디버프 걸림만 조금 크게
        // 11/14: 팀장이 OK한 지금 효과가 기준. 11/14 실행 4번(V13~V16)의 최댓값 + 20% 여유 → 이보다 커지거나 길어지면 실패 (예전 기준은 효과를 바꾸기 전 값이라 늘 실패했음)
        private static readonly VfxSpec[] VfxSpecs =
        {
            new("Hit", false, true, 0.08f, 1.4f, 4.5f),
            new("HitMagic", false, true, 0.08f, 0.7f, 3.6f),
            new("HitShot", false, true, 0.06f, 0.7f, 2.0f),
            new("Crit", false, true, 0.1f, 1.0f, 3.0f),
            new("CritMelee", false, true, 0.06f, 1.4f, 4.0f), // 11/13 근접 크리티컬 (베기)
            new("CritMagic", false, true, 0.1f, 1.2f, 4.7f),  // 11/13 마법 크리티컬
            new("Muzzle", true, true, 0.03f, 0.3f, 0.6f),
            new("Miss", false, false, 0.15f, 1.2f, 3.1f),
            new("Defend", true, true, 0.15f, 0.9f, 1.9f),
            new("Heal", true, false, 0.4f, 1.6f, 2.0f),
            new("Revive", true, false, 0.3f, 2.0f, 2.9f),
            new("Debuff_DefenseDown", false, true, 0.25f, 1.6f, 3.4f),
            new("Debuff_EvasionRevealed", false, true, 0.25f, 1.6f, 4.6f),
            new("Debuff_Bind", false, false, 0.15f, 2.4f, 3.4f),
            new("Debuff_Poison", false, false, 0.4f, 2.6f, 2.8f),
            new("Debuff_WeakBleed", false, true, 0.25f, 1.6f, 1.4f),
            new("Debuff_Confusion", false, false, 0.2f, 2.6f, 6.0f),
            new("Debuff_AttackDown", false, false, 0.2f, 2.6f, 6.3f),
            new("Buff_AccuracyUp", true, true, 0.2f, 1.6f, 2.9f),  // 영점 조정 (머리 앞)
            new("Buff_SpeedUp", true, false, 0.2f, 1.6f, 1.7f),    // 신속 (발밑)
        };

        private IEnumerator VfxQaRun()
        {
            yield return WaitFor(() => FindFirstObjectByType<MainMenuController>() != null, 10f, "메인 메뉴");
            yield return new WaitForSeconds(1f);
            var menu = FindFirstObjectByType<MainMenuController>();
            menu.DevOpenPartySelect();
            yield return new WaitForSeconds(0.3f);
            menu.OnDepart();
            if (Seed >= 0) GameRandom.Seed(Seed);
            yield return WaitFor(() => FindFirstObjectByType<BoardTestController>() != null, 10f, "보드");
            yield return new WaitForSeconds(1f);
            yield return SkipIntro();
            var board = FindFirstObjectByType<BoardTestController>();
            for (int turn = 0; turn < 20 && !EncounterPanel.IsOpen; turn++)
            {
                yield return WaitFor(() => (board.DevIsIdle && !BoardTopBar.DevTurnDropVisible) || EncounterPanel.IsOpen, 20f, "굴림 대기");
                if (EncounterPanel.IsOpen) break;
                board.DevRoll();
                yield return WaitFor(() => board.DevAwaitingSelection || EncounterPanel.IsOpen, 5f, "칸 선택");
                board.DevMoveTowardEncounter();
                yield return WaitFor(() => board.DevIsIdle || EncounterPanel.IsOpen, 15f, "이동");
                if (DialoguePanel.IsOpen) DialoguePanel.DevAdvance();
            }
            yield return new WaitForSeconds(0.4f);
            EncounterPanel.DevChoose(1);
            yield return WaitFor(() => FindFirstObjectByType<BattleController>() != null, 10f, "전투");
            var battle = FindFirstObjectByType<BattleController>();
            yield return WaitFor(() => battle.DevAwaitingPlayer || battle.DevFinished, 40f, "아군 차례");
            yield return new WaitForSeconds(1f);
            var cam = Camera.main;
            var enemy = battle.DevEnemies.First(e => e.InBattle);
            var ally = battle.DevAllies.First(a => a.IsAlive);
            Note($"이펙트 QA: 적 {enemy.DisplayName} · 아군 {ally.DisplayName}");

            // ── 1. 효과 하나씩 ──
            foreach (var spec in VfxSpecs)
            {
                var unit = spec.OnAlly ? ally : enemy;
                // 실제 전투와 같은 자리: 방어력 감소·출혈 = 몸 앞, 영점 조정 = 머리 앞, 나머지 = 가슴/발밑
                var pos = spec.Key is "Debuff_DefenseDown" or "Debuff_WeakBleed" ? battle.DevFront(unit)
                        : spec.Key == "Buff_AccuracyUp" ? battle.DevTop(unit) - new Vector3(0f, 0.25f, 0f)
                        : spec.Chest ? battle.DevChest(unit) : battle.DevFeet(unit);
                float modelPx = ModelScreenHeight(battle, unit, cam);
                var go = BattleVfx.Play(spec.Key, pos, spec.Key == BattleVfx.Muzzle ? 1f : battle.DevVfxScale(unit)); // 실제 전투와 같은 크기
                if (go == null) { Qa(false, $"{spec.Key}: 효과 파일 없음"); continue; }
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                var renderers = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
                float t = 0f, lastAlive = 0f, peak = 0f; bool shot = false;
                while (go != null && t < 5f)
                {
                    int alive = VisibleParticles(systems);
                    if (alive > 0)
                    {
                        lastAlive = t;
                        peak = Mathf.Max(peak, ParticleScreenSize(systems, cam) / Mathf.Max(1f, modelPx));
                    }
                    if (!shot && t >= spec.ShotAt) { shot = true; yield return Shot("vfx_" + spec.Key); t += Time.deltaTime; continue; }
                    yield return null;
                    t += Time.deltaTime;
                }
                bool cleaned = go == null;
                Note($"  {spec.Key}: 보이는 시간 {lastAlive:0.00}초 (기준 {spec.MaxSeconds}) · 크기 모델 키의 {peak:0.00}배 (기준 {spec.MaxRatio}) · 정리 {(cleaned ? "됨" : "안 됨")}");
                Qa(lastAlive > 0.01f, $"{spec.Key}: 화면에 나옴");
                Qa(lastAlive <= spec.MaxSeconds, $"{spec.Key}: {lastAlive:0.00}초 이내로 끝남 (기준 {spec.MaxSeconds}초)");
                Qa(peak <= spec.MaxRatio, $"{spec.Key}: 크기 모델 키의 {peak:0.00}배 (기준 {spec.MaxRatio}배 이하)");
                Qa(cleaned, $"{spec.Key}: 끝나면 지워짐");
                yield return new WaitForSeconds(0.3f);
            }

            // ── 1-2. 11/13 6차: 캐릭터 스킬별 이펙트 (자리 규칙까지 실제 전투와 같게) ──
            foreach (var id in new[] { "skill.detective.pinpoint_breach", "skill.sniper.piercing_round", "skill.sniper.suppressing_fire", "skill.plague_doctor.black_miasma", "skill.plague_doctor.emergency_suture", "skill.plague_doctor.purge_plague" })
            {
                bool toAlly = id.EndsWith("emergency_suture") || id.EndsWith("purge_plague");
                string key = "Skill_" + id.Substring(id.LastIndexOf('.') + 1);
                Qa(BattleVfx.Has(key), $"스킬 이펙트 {key}: 파일 있음");
                battle.DevPlaySkillVfx(id, ally, toAlly ? ally : enemy);
                yield return new WaitForSeconds(0.18f);
                yield return Shot("vfx_" + key);
                yield return new WaitForSeconds(1.6f);
            }

            // ── 2. 디버프 둘레 효과 ──
            StatusEffectDefinition Def(StatusEffectType type)
            {
                var d = ScriptableObject.CreateInstance<StatusEffectDefinition>();
                d.Id = "qa_" + type; d.DisplayName = type.ToString(); d.Effect = type; d.DurationTurns = -1; d.MaxStacks = 1;
                return d;
            }
            int AuraOn(BattleUnit u)
            {
                var m = battle.DevModelOf(u);
                return m == null ? 0 : m.GetComponentsInChildren<Transform>().Count(c => c.parent == m.transform && c.name.StartsWith("Aura_"));
            }
            int AuraParticles(BattleUnit u)
            {
                var m = battle.DevModelOf(u);
                return m == null ? 0 : m.GetComponentsInChildren<ParticleSystem>().Where(p => p.transform.root == m.transform.root && p.GetComponentInParent<Transform>().name != null)
                    .Where(p => { var tr = p.transform; while (tr != null && tr != m.transform) { if (tr.name.StartsWith("Aura_")) return true; tr = tr.parent; } return false; })
                    .Sum(p => p.particleCount);
            }
            void ClearDebuffs(BattleUnit u) { foreach (var t in new[] { StatusEffectType.WeakBleed, StatusEffectType.Poison, StatusEffectType.Confusion, StatusEffectType.Bind, StatusEffectType.AttackDown, StatusEffectType.DefenseDown, StatusEffectType.EvasionRevealed, StatusEffectType.SpeedUp }) u.Statuses.Remove(t); }

            ClearDebuffs(enemy); ClearDebuffs(ally);
            yield return new WaitForSeconds(2f);
            int baseCount = StatusAura.DevActiveCount;

            // 종류별 한 장씩 (적에게 하나씩)
            foreach (var type in new[] { StatusEffectType.WeakBleed, StatusEffectType.Poison, StatusEffectType.Confusion, StatusEffectType.Bind, StatusEffectType.AttackDown, StatusEffectType.DefenseDown, StatusEffectType.EvasionRevealed })
            {
                ClearDebuffs(enemy);
                yield return new WaitForSeconds(0.3f);
                var applied = enemy.Statuses.Apply(Def(type));
                if (applied == null) { Note($"  둘레 {type}: 이 적에게는 걸리지 않음 (면역) — 건너뜀"); continue; }
                yield return new WaitForSeconds(1.6f);
                int on = AuraOn(enemy), parts = AuraParticles(enemy);
                Note($"  둘레 {type}: 켜짐 {on}개 · 알갱이 {parts}개");
                bool rimOn = (type is StatusEffectType.DefenseDown or StatusEffectType.AttackDown) && battle.DevModelOf(enemy) != null && battle.DevModelOf(enemy).GetComponentInChildren<StatusRim>() != null; // 방어·공격 약화 = 테두리 빛 + 아이콘 (파티클 없음)
                Qa(on == 1 && (parts > 0 || rimOn), $"둘레 효과 {type}: 걸려 있는 동안 보임 (켜짐 {on} · 알갱이 {parts}{(rimOn ? " · 테두리 빛" : "")})");
                yield return Shot("vfx_aura_" + type);
                if (type == StatusEffectType.DefenseDown) Note("방어 감소 아이콘: " + StatusAura.DevIconInfo);
                if (type == StatusEffectType.Bind) Note($"속박 원(적) {StatusAura.DevBindInfo} · 아래 원 키의 {StatusAura.DevBindRingY:F2}");
                if (type == StatusEffectType.DefenseDown) { yield return new WaitForSeconds(0.3f); yield return Shot("vfx_aura_DefenseDown_b"); yield return new WaitForSeconds(0.3f); yield return Shot("vfx_aura_DefenseDown_c"); } // 1.5초마다라 시간 바꿔 더 찍음
            }

            // 11/13 팀장: 속박 원 위치(가슴·허리)는 사람 모델에서도 확인
            ClearDebuffs(enemy);
            foreach (var a in battle.DevAllies.Where(u => u.IsAlive).ToList()) // 11/14 팀장 QA: 캐릭터마다 위치 확인 → 아군 3명 모두 (가슴·골반 뼈)
                if (a.Statuses.Apply(Def(StatusEffectType.Bind)) != null) { yield return new WaitForSeconds(1.6f); yield return Shot("vfx_aura_Bind_ally_" + a.DisplayName); Note($"속박 원({a.DisplayName}) {StatusAura.DevBindInfo}"); ClearDebuffs(a); yield return new WaitForSeconds(0.3f); }
            if (ally.Statuses.Apply(Def(StatusEffectType.DefenseDown)) != null) { yield return new WaitForSeconds(0.8f); yield return Shot("vfx_aura_DefenseDown_ally"); yield return new WaitForSeconds(0.8f); yield return Shot("vfx_aura_DefenseDown_ally2"); ClearDebuffs(ally); yield return new WaitForSeconds(1.2f); yield return Shot("vfx_aura_DefenseDown_ally_off"); } // A안 테두리 빛 (켜짐·깜빡임·꺼짐)
            // 한 모델 최대 2개 · 강화는 표시 안 함
            ClearDebuffs(enemy);
            yield return new WaitForSeconds(0.3f);
            enemy.Statuses.Apply(Def(StatusEffectType.WeakBleed));
            enemy.Statuses.Apply(Def(StatusEffectType.Poison));
            enemy.Statuses.Apply(Def(StatusEffectType.AttackDown));
            enemy.Statuses.Apply(Def(StatusEffectType.SpeedUp));
            ally.Statuses.Apply(Def(StatusEffectType.Bind));
            ally.Statuses.Apply(Def(StatusEffectType.SpeedUp));
            yield return new WaitForSeconds(1.6f);
            Qa(AuraOn(enemy) == StatusAura.MaxShown, $"디버프 3개 걸린 적: 둘레 효과 최대 {StatusAura.MaxShown}개 (실제 {AuraOn(enemy)})");
            Qa(AuraOn(ally) == 1, $"속박 + 속도 증가 아군: 디버프만 1개 (실제 {AuraOn(ally)})");
            yield return Shot("vfx_aura_many");

            // 둘레 효과가 켜져 있을 때 프레임
            int frames = 0; float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 2f) { frames++; yield return null; }
            float fps = frames / (Time.realtimeSinceStartup - start);
            Note($"  둘레 효과 3개 켜짐 동안 평균 {fps:0} 프레임");
            Qa(fps >= 50f, $"둘레 효과 켜짐 동안 평균 {fps:0} 프레임 (50 이상)");

            // 풀리면 꺼짐
            ClearDebuffs(enemy);
            yield return new WaitForSeconds(2.2f);
            Qa(AuraOn(enemy) == 0, $"디버프가 풀리면 둘레 효과 꺼짐 (남은 {AuraOn(enemy)}개)");

            // 쓰러지면 꺼짐
            battle.DevKnockDownAlly(ally);
            yield return new WaitForSeconds(2.2f);
            Qa(AuraOn(ally) == 0, $"쓰러지면 둘레 효과 꺼짐 (남은 {AuraOn(ally)}개)");
            Qa(StatusAura.DevActiveCount == baseCount, $"켜진 둘레 효과 수가 처음과 같음 ({baseCount} → {StatusAura.DevActiveCount})");

            Note($"이펙트 QA 끝: 통과 {_qaPass} · 실패 {_qaFail}");
        }

        private static float ModelScreenHeight(BattleController battle, BattleUnit unit, Camera cam)
        {
            var m = battle.DevModelOf(unit);
            var col = m != null ? m.GetComponent<CapsuleCollider>() : null;
            if (m == null || col == null || cam == null) return 1f;
            float h = Mathf.Max(col.height, 0.85f) * m.transform.lossyScale.y; // 아주 작은 적은 0.85m로 보고 비교
            var a = cam.WorldToScreenPoint(m.transform.position);
            var b = cam.WorldToScreenPoint(m.transform.position + Vector3.up * h);
            return Mathf.Abs(b.y - a.y);
        }

        private static ParticleSystem.Particle[] _qaBuf = new ParticleSystem.Particle[4096];

        // 눈에 보이는 알갱이 수 (거의 투명한 것은 뺌)
        private static int VisibleParticles(ParticleSystem[] systems)
        {
            int n = 0;
            foreach (var ps in systems)
            {
                if (ps == null || ps.particleCount == 0) continue;
                int c = ps.GetParticles(_qaBuf);
                for (int i = 0; i < c; i++) if (_qaBuf[i].GetCurrentColor(ps).a > 12) n++;
            }
            return n;
        }

        // 보이는 알갱이들의 화면 크기 (가로·세로 중 큰 쪽, 알갱이 크기 포함)
        private static float ParticleScreenSize(ParticleSystem[] systems, Camera cam)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue; bool any = false;
            foreach (var ps in systems)
            {
                if (ps == null || ps.particleCount == 0) continue;
                bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                float scale = ps.main.scalingMode == ParticleSystemScalingMode.Hierarchy ? ps.transform.lossyScale.x : 1f;
                int c = ps.GetParticles(_qaBuf);
                for (int i = 0; i < c; i++)
                {
                    var p = _qaBuf[i];
                    if (p.GetCurrentColor(ps).a <= 12) continue;
                    var world = local ? ps.transform.TransformPoint(p.position) : p.position;
                    var s = cam.WorldToScreenPoint(world);
                    if (s.z <= 0f) continue;
                    float r = p.GetCurrentSize(ps) * scale * 0.5f;
                    float px = Mathf.Abs(cam.WorldToScreenPoint(world + cam.transform.right * r).x - s.x);
                    minX = Mathf.Min(minX, s.x - px); maxX = Mathf.Max(maxX, s.x + px); minY = Mathf.Min(minY, s.y - px); maxY = Mathf.Max(maxY, s.y + px);
                    any = true;
                }
            }
            return any ? Mathf.Max(maxX - minX, maxY - minY) : 0f;
        }

        // 살아 있는 알갱이들의 화면 높이 (가로·세로 중 큰 쪽)
        private static float ScreenHeight(IEnumerable<ParticleSystemRenderer> renderers, Camera cam)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue; bool any = false;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                var ps = r.GetComponent<ParticleSystem>();
                if (ps == null || ps.particleCount == 0) continue;
                var b = r.bounds; any = true;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var s = cam.WorldToScreenPoint(c);
                    if (s.z <= 0f) continue;
                    minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x); minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y);
                }
            }
            return any ? Mathf.Max(maxX - minX, maxY - minY) : 0f;
        }
    }
}
