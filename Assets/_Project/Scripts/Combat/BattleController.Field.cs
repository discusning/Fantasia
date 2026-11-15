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
    // 전장 구성 (모델·카메라·배치) (11/15 긴 파일 정리: BattleController.cs에서 옮김, 내용 그대로)
    public partial class BattleController
    {
        // ───────────── 전장 구성 ─────────────
        private void SpawnUnits(Party party)
        {
            // 영구 사망한 파티원은 전투에 나오지 않는다.
            var living = party.LivingMembers.ToList();
            var allyPositions = BattleFormation.Line(living.Count, LineSpacing);
            for (int i = 0; i < living.Count; i++)
            {
                var unit = new BattleUnit(living[i], i);
                _allies.Add(unit);
                // 쓰러진 채로 들어온 아군(HP 0)은 행동 큐에서 빠지고, 다른 아군이 부활시킬 수 있다.
                SpawnModel(unit, new Vector3(-LineX, 0f, -allyPositions[i].z), Vector3.right); // 11/14 팀장: 카드 순서(왼→오)와 화면 자리를 맞춤 — 카메라에서 z가 클수록 왼쪽 뒤라 뒤집음
                if (!unit.IsAlive) KnockDown(unit, instant: true);
            }

            var enemyPositions = BattleFormation.Line(_setup.Enemies.Count, LineSpacing);
            for (int i = 0; i < _setup.Enemies.Count; i++)
            {
                var unit = new BattleUnit(_setup.Enemies[i], i);
                if (_setup.Enemies.Count(e => e == _setup.Enemies[i]) > 1) unit.DisplayName += $" {(char)('A' + i)}";
                _enemies.Add(unit);
                SpawnModel(unit, new Vector3(LineX, 0f, -enemyPositions[i].z), Vector3.left); // 몬스터 A(왼쪽 카드)가 화면 왼쪽
            }
        }

        // 모델은 초상과 같은 CharacterModels (데이터의 Model 프리팹 또는 임시 인형). 클릭용 콜라이더를 붙인다.
        private void SpawnModel(BattleUnit unit, Vector3 position, Vector3 facing)
        {
            var model = CharacterModels.Create(unit.Definition, out float height);
            model.name = unit.DisplayName;
            model.transform.position = position;
            model.transform.rotation = Quaternion.LookRotation(facing);
            BlobShadow.Attach(model, height); // 발밑 흐린 그림자 (11/10 기획 1A)
            HeldWeapon.Show(model, unit.Member); // 11/13 아군: 장착 무기 종류에 맞는 소품만 손에
            Fantasia.UI.HandGrip.Attach(model); // 11/14 손가락으로 무기 쥐기 (손가락 뼈 있는 모델만)
            StatusAura.Attach(model, unit, height); // 11/13 디버프가 걸려 있는 동안 은은한 둘레 효과
            var col = model.AddComponent<CapsuleCollider>();
            col.height = height;
            col.radius = Mathf.Max(0.4f, height * 0.3f);
            col.center = new Vector3(0f, height * 0.5f, 0f);
            _models[unit] = model;
            _targets[col] = unit;
        }

        // 11/13 이펙트 1차 — 동작이 무엇인지에 따라 (조사 문서 Docs/Art/VFX_연구_1113):
        // 총 = 총구 불꽃 + 맞은 자리 연기 · 지팡이·마법서·마법 피해 = 푸른 흰 빛 · 칼·적 공격 = 작은 주황 섬광 · 크리티컬만 조금 크게 · 빗나감 = 발밑 흙먼지
        // 11/13 팀장 6차: 캐릭터 스킬 컨셉마다 이펙트가 모두 다르게 → Resources/VFX/Skill_{스킬 id 끝}.prefab 이 있으면 맞는 자리에 그걸 씀
        public static string SkillVfxKey(SkillDefinition skill) => skill == null || string.IsNullOrEmpty(skill.Id) ? null : "Skill_" + skill.Id.Substring(skill.Id.LastIndexOf('.') + 1);

        private void PlayHitVfx(BattleUnit attacker, BattleUnit target, AttackRequest request, AttackOutcome outcome, SkillDefinition skill = null)
        {
            var category = attacker != null && attacker.Member != null && attacker.Member.EquippedWeapon != null
                ? attacker.Member.EquippedWeapon.Category : (WeaponCategory?)null;
            bool shot = category is WeaponCategory.Pistol or WeaponCategory.Musket or WeaponCategory.Bow;
            if (shot && category != WeaponCategory.Bow && attacker != null)
            {
                var from = ModelChest(attacker);
                var dir = ModelChest(target) - from; dir.y = 0f;
                BattleVfx.Play(BattleVfx.Muzzle, from + (dir.sqrMagnitude > 0.01f ? dir.normalized * 0.6f : Vector3.zero));
            }
            if (outcome.Evaded) { BattleVfx.Play(BattleVfx.Miss, ModelFeet(target), VfxScale(target)); return; }
            string skillKey = SkillVfxKey(skill);
            if (skillKey != null && BattleVfx.Has(skillKey))
            {
                PlaySkillVfx(skill, skillKey, attacker, target);
                if (outcome.IsCritical) BattleVfx.Play(BattleVfx.Crit, ModelFront(target), VfxScale(target) * 0.7f);
                return;
            }
            bool magic = request.Type == DamageType.Magic || (category.HasValue && category.Value.IsMagic());
            // 11/13 팀장: 크리티컬 불티 터짐은 총에만 어울림 → 근접(칼·발톱·물기) = 베기 궤적, 마법 = 큰 마법 빛
            if (outcome.IsCritical) { BattleVfx.Play(shot ? BattleVfx.Crit : magic ? BattleVfx.CritMagic : BattleVfx.CritMelee, ModelChest(target), VfxScale(target)); return; }
            BattleVfx.Play(shot ? BattleVfx.HitShot : magic ? BattleVfx.HitMagic : BattleVfx.Hit, ModelChest(target), VfxScale(target));
        }

        // 11/13 팀장 6차 "스킬 이펙트 위치도 충분히 고려": 스킬 뜻 + 카메라에서 보이는 자리
        //  · 몸 가운데(ModelChest)에 두면 몸에 반쯤 묻힘 → 맞는 자리 효과는 몸 앞(카메라 쪽 겉면, ModelFront)
        //  · 일점 돌파 = 몸 앞 가슴 한 점 · 관통탄 = 앞에 들어간 섬광 + 등 뒤(쏜 쪽 반대)로 빠져나가는 금속 불티
        //  · 검은 연무 = 발밑에서 안개가 몸을 덮으며 올라옴 · 저지 사격 = 발 바로 앞 땅(쏜 쪽)에 흙이 튐 — 몸에 가리지 않게
        //  · 크기는 모델 키에 맞춤(VfxScale) — 쥐 같은 작은 적에 사람 크기 효과가 나가지 않게
        private void PlaySkillVfx(SkillDefinition skill, string key, BattleUnit attacker, BattleUnit target)
        {
            float scale = VfxScale(target);
            float radius = _models.TryGetValue(target, out var tm) && tm.GetComponent<CapsuleCollider>() is { } c ? c.radius : 0.4f;
            var fromAttacker = attacker != null ? ModelChest(target) - ModelChest(attacker) : Vector3.right; fromAttacker.y = 0f;
            fromAttacker = fromAttacker.sqrMagnitude > 0.01f ? fromAttacker.normalized : Vector3.right;
            string id = skill.Id;
            if (id.EndsWith("black_miasma")) BattleVfx.Play(key, ModelFeet(target), scale);
            else if (id.EndsWith("suppressing_fire")) BattleVfx.Play(key, ModelFeet(target) - fromAttacker * (radius + 0.25f), scale);
            else if (id.EndsWith("piercing_round"))
            {
                BattleVfx.Play(key, ModelFront(target), scale);
                var exit = BattleVfx.Play(key + "_exit", ModelChest(target) + fromAttacker * (radius + 0.15f), scale);
                if (exit != null) exit.transform.rotation = Quaternion.LookRotation(fromAttacker); // 불티가 쏜 방향으로 빠져나감
            }
            else BattleVfx.Play(key, ModelFront(target), scale);
        }

        // 11/13 이펙트 위치: 몸 가운데(맞음·방어) · 발밑(회복·부활)
        private Vector3 ModelChest(BattleUnit unit)
        {
            if (unit == null || !_models.TryGetValue(unit, out var model)) return Vector3.zero;
            var col = model.GetComponent<CapsuleCollider>();
            return model.transform.position + Vector3.up * ((col != null ? col.height : 1.8f) * 0.55f);
        }
        // 11/13 이펙트 QA: 작은 적(쥐 등)에 사람 크기 효과가 나가 땅에 잘림 → 모델 키에 맞춰 줄임 (사람 1.7m = 1배, 0.5~1.4배)
        // 11/13 다른 아군에게 쓰는 동작: 그 아군 쪽으로 몸을 돌림 (돌기 전 방향을 돌려줌 — 끝나면 되돌리기용)
        private const float TurnTime = 0.22f;
        private Quaternion? TurnToward(BattleUnit actor, BattleUnit target)
        {
            if (actor == null || target == null || !_models.TryGetValue(actor, out var model) || !_models.TryGetValue(target, out var other)) return null;
            var dir = other.transform.position - model.transform.position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return null;
            var from = model.transform.rotation;
            StartCoroutine(TurnModel(model, Quaternion.LookRotation(dir)));
            return from;
        }

        private IEnumerator TurnModel(GameObject model, Quaternion to)
        {
            if (model == null) yield break;
            var from = model.transform.rotation;
            for (float t = 0f; t < TurnTime && model != null; t += Time.deltaTime)
            {
                model.transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / TurnTime));
                yield return null;
            }
            if (model != null) model.transform.rotation = to;
        }

        // 11/13 방어 약화: 몸 한가운데면 몸에 묻힘 → 가슴 높이에서 카메라 쪽으로 몸 반지름만큼 앞
        private Vector3 ModelFront(BattleUnit unit)
        {
            var chest = ModelChest(unit);
            if (_camera == null || !_models.TryGetValue(unit, out var model)) return chest;
            var col = model.GetComponent<CapsuleCollider>();
            var toCam = _camera.transform.position - chest; toCam.y = 0f;
            return chest + toCam.normalized * ((col != null ? col.radius : 0.4f) + 0.1f);
        }
        private float VfxScale(BattleUnit unit) => unit != null && _models.TryGetValue(unit, out var model) && model.GetComponent<CapsuleCollider>() is { } c ? Mathf.Clamp(c.height / 1.7f, 0.5f, 1.4f) : 1f;
        public float DevVfxScale(BattleUnit unit) => VfxScale(unit);
        private Vector3 ModelFeet(BattleUnit unit) => unit != null && _models.TryGetValue(unit, out var model) ? model.transform.position + Vector3.up * 0.05f : Vector3.zero;

        private Vector3 ModelTop(BattleUnit unit)
        {
            if (unit == null || !_models.TryGetValue(unit, out var model)) return Vector3.zero;
            var col = model.GetComponent<CapsuleCollider>();
            return model.transform.position + Vector3.up * (col != null ? col.height + 0.2f : 2f);
        }

        // 대상 발밑 표시 — UITheme.TargetMarkerPrefab이 있으면 그걸, 없으면 붉은 육각형 링.
        private Transform CreateTargetMarker()
        {
            var prefab = UITheme.Current.TargetMarkerPrefab;
            GameObject marker = prefab != null ? Instantiate(prefab) : BuildHexRing();
            marker.SetActive(false);
            return marker.transform;
        }

        private static GameObject BuildHexRing()
        {
            var go = new GameObject("TargetMarker");
            var line = go.AddComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = false;
            line.positionCount = 6;
            line.widthMultiplier = 0.07f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = new Color(1f, 0.3f, 0.25f);
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * (60f * i);
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.9f, 0f, Mathf.Sin(a) * 0.9f));
            }
            go.AddComponent<TargetMarkerSpin>();
            return go;
        }

        private void ShowMarker(BattleUnit unit)
        {
            if (!_models.TryGetValue(unit, out var model)) return;
            _targetMarker.position = model.transform.position + Vector3.up * 0.03f;
            _targetMarker.gameObject.SetActive(true);
        }

        private void HideMarker()
        {
            _hovered = null;
            if (_targetMarker != null) _targetMarker.gameObject.SetActive(false);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        // 보드를 거치지 않고 전투 씬을 바로 열었을 때(DevSceneNav 등)의 기본값 — 에디터에서만 시드 자산을 읽는다.
        private static Party DefaultParty()
        {
            var party = new Party();
#if UNITY_EDITOR
            foreach (var name in new[] { "Job_명탐정", "Job_역병의사", "Job_저격수" })
            {
                var job = UnityEditor.AssetDatabase.LoadAssetAtPath<JobDefinition>($"Assets/_Project/Data/Jobs/{name}.asset");
                if (job != null) party.AddMember(new PartyMember(job));
            }
#endif
            party.GiveStartingItems(ItemCatalog.Current);
            return party;
        }

        private static BattleSetup DefaultSetup()
        {
            var setup = new BattleSetup();
#if UNITY_EDITOR
            foreach (var name in new[] { "Enemy_그림자사냥개", "Enemy_역병쥐" })
            {
                var enemy = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"Assets/_Project/Data/Enemies/{name}.asset");
                if (enemy != null) setup.Enemies.Add(enemy);
            }
#endif
            return setup;
        }
    }
}
