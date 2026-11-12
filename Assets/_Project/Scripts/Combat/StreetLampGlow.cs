using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fantasia.Combat
{
    // 11/12 팀장 결정: 가로등 불빛 (Docs/Planning/StreetLamp_시간대_제안.md).
    // 가스등처럼 저녁에 켜고 아침에 끔: 아침 25% · 낮 0 · 저녁 70% (가로등마다 0~0.6초 차이로 하나씩 켜짐) · 새벽 100%.
    // 3겹: ① 유리 발광(재질 Emission) ② 빛 번짐(등 둘레) + 바닥 빛 웅덩이 (가짜 빛, 가벼움) ③ 진짜 점광원은 카메라에 가까운 2개만.
    // 화질 설정의 픽셀 빛 수가 모자라면(Medium 이하) ③은 끔. 규칙 영향 없음 (T4).
    // 불 위치(Point)와 렌더러는 에디터 StreetLampSetup이 프리팹에 넣어 둠.
    public class StreetLampGlow : MonoBehaviour
    {
        public static readonly float[] LevelByTime = { 0.25f, 0f, 0.7f, 1f }; // 아침 · 낮 · 저녁 · 새벽
        private static readonly Color Flame = new Color(1f, 0.75f, 0.45f);
        // 11/12 팀장: 빛이 퍼지는 모양이 덜 현실적 → 더 은은하게 (발광 2.2→1.3, 번짐 0.42→0.25, 바닥 0.2→0.1, 진짜 빛 1.6→0.8·범위 6.5→5.5)
        private const float EmissionMax = 1.3f, HaloSize = 1.2f, HaloAlpha = 0.25f, PoolSize = 3.4f, PoolAlpha = 0.1f, LightRange = 5.5f, LightMax = 0.8f, WallLightOut = 0.5f, HaloDrop = 0.08f;
        private const float FlickerAmp = 0.04f, IgniteMaxDelay = 0.6f, IgnitePop = 0.15f;

        public Transform Point;      // 불 위치 (유리 가운데)
        public Renderer[] Glass;     // 발광을 바꿀 렌더러
        public bool Wall;            // 벽 가로등이면 바닥 빛을 벽 앞쪽으로

        private static readonly List<StreetLampGlow> Active = new();
        private float _target, _level, _startAt, _seed;
        private bool _ignite;
        private Transform _halo, _pool;
        private Material _haloMat, _poolMat;
        private Light _light;
        private MaterialPropertyBlock _mpb;
        private Camera _cam;

        public static float CurrentLevel { get; private set; }

        // BattleScenery가 배치 뒤 부름. timeIndex: 0 아침 ~ 3 새벽
        public static void ApplyAll(int timeIndex, Camera cam)
        {
            float level = LevelByTime[Mathf.Clamp(timeIndex, 0, 3)];
            CurrentLevel = level;
            Active.RemoveAll(l => l == null);
            bool igniteNow = timeIndex == 2; // 저녁: 전투 시작 때 하나씩 켜짐
            foreach (var lamp in Active) lamp.Begin(level, igniteNow, cam);

            // 진짜 빛: 카메라에 가까운 것부터 최대 2개 (해·달빛 1개를 빼고 남는 픽셀 빛 수 안에서)
            int real = level > 0f ? Mathf.Clamp(QualitySettings.pixelLightCount - 1, 0, 2) : 0;
            var near = cam != null ? Active.OrderBy(l => (l.Point.position - cam.transform.position).sqrMagnitude).Take(real).ToList() : new List<StreetLampGlow>();
            foreach (var lamp in Active) lamp.SetRealLight(near.Contains(lamp));
        }

        public static int DevLitCount => Active.Count(l => l != null && l._level > 0.01f);
        public static int DevRealLights => Active.Count(l => l != null && l._light != null && l._light.enabled);
        public static int DevCount => Active.Count(l => l != null);

        private void Awake()
        {
            Active.Add(this);
            _mpb = new MaterialPropertyBlock();
            _seed = Random.value * 100f;
            var glow = Resources.Load<Material>("BattleArt/LampGlow"); // 더하기(Additive) 재질 — 에디터 StreetLampSetup이 만듦
            var haloMat = Resources.Load<Material>("BattleArt/LampHalo"); // 위가 잘린 번짐 (지붕 위로 빛이 안 나가게)
            if (glow == null || Point == null) return;
            _halo = MakeQuad("LampHalo", haloMat != null ? haloMat : glow, out _haloMat, HaloSize);
            _halo.SetParent(Point, false);
            _halo.localPosition = Vector3.down * HaloDrop; // 유리 가운데보다 조금 아래 — 빛은 유리 옆·아래로 샘
            _pool = MakeQuad("LampPool", glow, out _poolMat, PoolSize);
            _pool.SetParent(transform, true);
            var p = Point.position;
            var front = Wall ? Point.position + Point.forward * 0.9f : p;
            _pool.position = new Vector3(front.x, 0.03f, front.z);
            _pool.rotation = Quaternion.Euler(90f, 0f, 0f);
            SetVisual(0f);
        }

        private void OnDestroy() { Active.Remove(this); if (_haloMat) Destroy(_haloMat); if (_poolMat) Destroy(_poolMat); }

        private static Transform MakeQuad(string name, Material src, out Material mat, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            mat = new Material(src);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.localScale = Vector3.one * size;
            return go.transform;
        }

        private void Begin(float level, bool ignite, Camera cam)
        {
            _cam = cam;
            _target = level;
            _ignite = ignite && level > 0f;
            _startAt = Time.time + (_ignite ? Random.Range(0f, IgniteMaxDelay) : 0f);
            _level = _ignite ? 0f : level;
            SetVisual(_level);
        }

        private void SetRealLight(bool on)
        {
            if (on && _light == null && Point != null)
            {
                _light = new GameObject("LampLight").AddComponent<Light>();
                _light.transform.SetParent(Point, false);
                if (Wall) _light.transform.localPosition = Vector3.forward * WallLightOut; // 벽에 붙으면 벽만 하얗게 → 앞으로 띄움
                _light.type = LightType.Point;
                _light.color = Flame;
                _light.range = LightRange;
                _light.shadows = LightShadows.None;
                _light.renderMode = LightRenderMode.ForcePixel;
            }
            if (_light != null) _light.enabled = on;
            SetVisual(_level);
        }

        private void Update()
        {
            float lvl = _target;
            if (_ignite)
            {
                float t = Time.time - _startAt;
                if (t < 0f) lvl = 0f;
                else if (t < IgnitePop) lvl = _target * (1.25f * t / IgnitePop);                       // "탁" 밝아짐
                else if (t < IgnitePop * 2f) lvl = _target * Mathf.Lerp(1.25f, 1f, (t - IgnitePop) / IgnitePop); // 자리 잡음
                else _ignite = false;
            }
            if (lvl > 0f) lvl *= 1f + FlickerAmp * (Mathf.PerlinNoise(_seed, Time.time * 3f) * 2f - 1f); // 가스불: 아주 약하게
            _level = lvl;
            SetVisual(lvl);
        }

        private void LateUpdate()
        {
            if (_halo == null) return;
            var cam = _cam != null ? _cam : Camera.main;
            if (cam != null) _halo.rotation = Quaternion.LookRotation(_halo.position - cam.transform.position); // 늘 카메라를 봄
        }

        private void SetVisual(float lvl)
        {
            if (Glass != null)
                foreach (var r in Glass)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor("_EmissionColor", Flame * (EmissionMax * lvl));
                    r.SetPropertyBlock(_mpb);
                }
            if (_haloMat != null) { _haloMat.SetColor("_TintColor", new Color(Flame.r, Flame.g, Flame.b, HaloAlpha * lvl)); _halo.gameObject.SetActive(lvl > 0.005f); }
            if (_poolMat != null) { _poolMat.SetColor("_TintColor", new Color(Flame.r, Flame.g * 0.9f, Flame.b * 0.8f, PoolAlpha * lvl)); _pool.gameObject.SetActive(lvl > 0.005f && (_light == null || !_light.enabled)); } // 진짜 빛이 바닥을 비추면 가짜 웅덩이는 끔 (두 번 밝아지지 않게)
            if (_light != null) _light.intensity = LightMax * lvl;
        }
    }
}
