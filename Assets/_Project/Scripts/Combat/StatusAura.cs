using System.Collections.Generic;
using System.Linq;
using Fantasia.Data;
using UnityEngine;

namespace Fantasia.Combat
{
    // 11/13 팀장 요청: 디버프가 걸려 있는 동안 그 모델 둘레에 은은한 효과 (Resources/VFX/Aura_{종류}, 반복).
    // 상태가 바뀌는 곳이 많아서(걸림·턴 시작·라운드 끝·정화·부활·불러오기) 여기서 0.25초마다 상태 목록을 보고 맞춤.
    // 한 모델에 최대 2개 (최근 것) — 겹치면 흐려지지 않게. 쓰러지면 끔. 강화(명중·속도)는 표시 안 함.
    public class StatusAura : MonoBehaviour
    {
        private static readonly HashSet<StatusEffectType> Debuffs = new()
        {
            StatusEffectType.WeakBleed, StatusEffectType.Poison, StatusEffectType.Confusion, StatusEffectType.Bind,
            StatusEffectType.AttackDown, StatusEffectType.DefenseDown, StatusEffectType.EvasionRevealed,
        };
        public const int MaxShown = 2;

        private BattleUnit _unit;
        private float _height = 1.8f;
        private float _next;
        private readonly Dictionary<StatusEffectType, GameObject> _shown = new();

        public static int DevActiveCount, DevPeak, DevShownTotal; // 자동 플레이 점검용: 지금 켜진 둘레 효과 수 · 가장 많았던 수 · 켜진 횟수

        public static void Attach(GameObject model, BattleUnit unit, float height)
        {
            var aura = model.GetComponent<StatusAura>(); if (aura == null) aura = model.AddComponent<StatusAura>(); // Unity 객체는 ?? 대신 == null
            aura._unit = unit;
            aura._height = height;
        }

        public static bool IsDebuff(StatusEffectType t) => Debuffs.Contains(t);

        private void Update()
        {
            if (_unit == null || Time.time < _next) return;
            _next = Time.time + 0.25f;
            var want = _unit.IsAlive
                ? _unit.Statuses.All.Select(a => a.Effect).Where(IsDebuff).Distinct().Reverse().Take(MaxShown).ToList()
                : new List<StatusEffectType>();
            foreach (var gone in _shown.Keys.Where(k => !want.Contains(k)).ToList())
            {
                Stop(_shown[gone]);
                _shown.Remove(gone);
                DevActiveCount--;
            }
            for (int i = 0; i < want.Count; i++)
            {
                if (_shown.ContainsKey(want[i])) continue;
                // 11/13 팀장 A안: 방어력 감소 동안 = 파티클 대신 모델 테두리가 강철빛 파랑으로 천천히 깜빡임
                // 11/13 팀장 6차: 공격력 감소도 남는 파티클이 걸리는 순간 효과와 안 맞음 → 같은 방식 (주황 테두리 + 부러진 칼)
                if (want[i] == StatusEffectType.DefenseDown || want[i] == StatusEffectType.AttackDown)
                {
                    bool def = want[i] == StatusEffectType.DefenseDown;
                    var holder = new GameObject("Aura_" + want[i]);
                    holder.transform.SetParent(transform, false);
                    var rim = StatusRim.Attach(holder, gameObject, def ? "VFX/Mat_Rim_DefenseDown" : "VFX/Mat_Rim_AttackDown");
                    if (rim == null) { Destroy(holder); continue; }
                    // 11/13 팀장: 머리 위는 몬스터 카드(HP 바)에 가려짐 → 어깨 높이, 몸 옆. 방패는 오른쪽, 칼은 왼쪽 (둘 다 걸려도 안 겹침)
                    var camRight = Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized : Vector3.right;
                    rim.ShowIcon(def ? "VFX/Icon_DefenseDown" : "VFX/Icon_AttackDown", BodyCenter() + Vector3.up * (_height * 0.72f) + camRight * ((def ? 1f : -1f) * (_height * 0.3f + 0.15f)), Mathf.Clamp(_height * 0.2f, 0.3f, 0.4f));
                    _shown[want[i]] = holder;
                    DevActiveCount++; DevShownTotal++; DevPeak = Mathf.Max(DevPeak, DevActiveCount);
                    continue;
                }
                var prefab = Resources.Load<GameObject>("VFX/Aura_" + want[i]);
                if (prefab == null) continue;
                var go = Instantiate(prefab, transform);
                // 둘이면 하나는 허리, 하나는 어깨 높이로 나눠 겹치지 않게. 속박은 발밑(원 2개가 발목·허리에 이미 놓여 있음)
                float up = want[i] == StatusEffectType.Bind ? 0f : want[i] == StatusEffectType.Confusion ? _height * 0.58f /* 11/14 QA: 몸 둘레 보라 알갱이가 어두운 몸에 묻힘 → 머리 위에서 도는 별 (어지러움) */ : _height * (want.Count > 1 && i == 1 ? 0.65f : 0.3f);
                // 11/13 팀장: 몬스터 위치와 어긋남 → 모델 기준점이 아니라 실제 몸(그려지는 부분) 한가운데에
                go.transform.position = BodyCenter() + Vector3.up * up;
                go.transform.localScale = prefab.transform.localScale * Mathf.Clamp(_height / 1.7f, 0.45f, 1.4f) / Mathf.Max(0.0001f, transform.lossyScale.x); // 작은 적은 작게
                // 11/14 QA: 크기를 먼저 줄인 뒤 원 위치를 정함 (반대 순서면 작은 적에서 두 원이 아래로 끌려가 겹침)
                // 11/13 팀장: 속박 원 2개 = 허리(상~하반신 사이)·가슴(상반신). 모델 키로 정함 → 작은 적도 몸에 맞음
                if (want[i] == StatusEffectType.Bind && go.transform.childCount >= 2)
                {
                    go.transform.GetChild(0).position = go.transform.position + Vector3.up * (_height * 0.34f); // 11/13 팀장: 머리·가슴에 걸림 → 가슴·허리로 (원 그림이 에셋 안에서 약 0.2 위에 떠 있음)
                    go.transform.GetChild(1).position = go.transform.position + Vector3.up * (_height * 0.48f);
                }
                if ((want[i] == StatusEffectType.WeakBleed || want[i] == StatusEffectType.DefenseDown) && Camera.main != null) // 방어 약화도 금 가는 모습이 몸에 가려지지 않게 몸 앞 // 11/13 출혈: 몸 안에서 나오면 가려짐 → 가슴 높이, 카메라 쪽 앞
                {
                    var toCam = Camera.main.transform.position - transform.position; toCam.y = 0f;
                    go.transform.position = BodyCenter() + Vector3.up * (_height * 0.6f) + toCam.normalized * Mathf.Max(0.25f, _height * 0.18f);
                }
                if (want[i] == StatusEffectType.Confusion) StatusRim.Attach(go, gameObject, "VFX/Mat_Rim_Confusion"); // 11/14 QA: 별만으로는 어두운 적에서 약함 → 보라 테두리 빛도 (방어·공격 감소와 같은 방식)
                _shown[want[i]] = go;
                DevActiveCount++; DevShownTotal++; DevPeak = Mathf.Max(DevPeak, DevActiveCount);
            }
        }

        // 몸 한가운데 (바닥 높이) — 그려지는 메시들의 가운데. 둘레 효과·파티클은 빼고 셈. 못 구하면 모델 기준점.
        private Vector3 BodyCenter()
        {
            bool any = false; var b = new Bounds();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer || r is MeshRenderer) || r.GetComponentInParent<ParticleSystem>() != null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? new Vector3(b.center.x, transform.position.y, b.center.z) : transform.position;
        }

        // 바로 지우지 않고 새 알갱이만 멈춘 뒤 사라지게
        private static void Stop(GameObject go)
        {
            if (go == null) return;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            var rim = go.GetComponent<StatusRim>(); if (rim != null) rim.FadeOut(); // 테두리 빛은 1초에 걸쳐 흐려짐
            Destroy(go, 1.5f);
        }

        private void OnDestroy()
        {
            DevActiveCount -= _shown.Count;
            _shown.Clear();
        }
    }
}
