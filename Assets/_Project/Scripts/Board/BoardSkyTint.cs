using Fantasia.Core;
using Fantasia.World;
using UnityEngine;

namespace Fantasia.Board
{
    // 보드 하늘·빛.
    // ① 시간대 (2026-11-11 팀장 결정): 월드 라운드마다 아침 → 낮 → 저녁 → 새벽, 빛 색·세기·각도·판 바깥 색·안개를 2초에 걸쳐 바꿈 (TimeOfDayLook).
    // ② D25 영혼 수확 75 이상 → 하늘이 붉게 (강림 전조, Decisions.md). ☆ 몇 초에 걸쳐 서서히, 75 아래로 내려가면 복귀. 시간대 위에 덧입힘.
    // 아트(붉은 스카이박스)가 오기 전까지는 하늘 색·햇빛·환경광을 붉게 섞는다.
    public class BoardSkyTint : MonoBehaviour
    {
        private const float FadeSeconds = 3f;
        private const float TimeFadeSeconds = 2f;
        private static readonly Color RedSky = new Color(0.62f, 0.12f, 0.1f);
        private static readonly Color RedLight = new Color(1f, 0.55f, 0.48f);
        private static readonly Color OutsideRed = new Color(0.3f, 0.06f, 0.05f);       // 붉은 하늘 때 판 바깥

        private Light _sun;
        private Color _sunBase, _ambientBase;
        private float _sunIntensityBase, _ambientIntensityBase;
        private Vector3 _sunEulerBase;
        private Material _sky;
        private Color _skyTintBase;
        private bool _hasSkyTint;
        private float _t;

        // 시간대: 지금 보이는 모양(_look)이 목표(시간대 표)로 2초에 걸쳐 옮겨감
        private int _timeIndex = -1;
        private TimeOfDayLook _from, _look;
        private float _timeT = 1f;

        private void Start()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { _sun = l; break; }
            if (_sun != null)
            {
                _sunBase = _sun.color;
                _sunIntensityBase = _sun.intensity;
                _sunEulerBase = _sun.transform.eulerAngles;
            }
            _ambientBase = RenderSettings.ambientLight;
            _ambientIntensityBase = RenderSettings.ambientIntensity;
            if (Camera.main != null)
            {
                // 11/10 기획 회신 03 (1막 C안): 판 바깥은 타일·말이 잘 보이는 어두운 단색 (템스 강·안개는 1막 이후)
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
            }
            if (RenderSettings.skybox != null)
            {
                _sky = new Material(RenderSettings.skybox); // 원본 자산은 건드리지 않음
                RenderSettings.skybox = _sky;
                _hasSkyTint = _sky.HasProperty("_SkyTint");
                if (_hasSkyTint) _skyTintBase = _sky.GetColor("_SkyTint");
            }
            _t = Target; // 씬을 다시 열면 바로 그 상태로
            _timeIndex = CurrentTimeIndex;
            _look = _from = TimeOfDayLook.All[_timeIndex];
            Apply();
        }

        private static float Target => BoardSession.Instance != null && BoardSession.Instance.SoulHarvest >= BloodHuntRules.RedSkyThreshold ? 1f : 0f;
        private static int CurrentTimeIndex => TimeOfDayLook.IndexFor(BoardSession.Instance != null ? BoardSession.Instance.World.WorldRound : 1);
        public static int DevShownTimeIndex { get; private set; } = -1;
        private int _lampIndex = -1;

        private void Update()
        {
            int index = CurrentTimeIndex;
            if (index != _timeIndex) // 적 턴이 끝나 다음 월드 라운드 → 새 시간대로 서서히
            {
                _timeIndex = index;
                _from = _look;
                _timeT = 0f;
            }
            if (_timeT < 1f)
            {
                _timeT = Mathf.MoveTowards(_timeT, 1f, Time.deltaTime / TimeFadeSeconds);
                _look = TimeOfDayLook.Lerp(_from, TimeOfDayLook.All[_timeIndex], Mathf.SmoothStep(0f, 1f, _timeT));
            }
            float target = Target;
            if (!Mathf.Approximately(_t, target)) _t = Mathf.MoveTowards(_t, target, Time.deltaTime / FadeSeconds);
            if (_lampIndex != _timeIndex) { _lampIndex = _timeIndex; Fantasia.Combat.StreetLampGlow.ApplyAll(_timeIndex, Camera.main); } // 11/13 보드 가로등도 시간대마다 (저녁·새벽 밝게)
            Apply(); // 카메라가 움직이면 안개 거리도 따라가야 해서 매 프레임 (가벼움)
        }

        private void Apply()
        {
            DevShownTimeIndex = _timeIndex;
            var look = _look;
            if (_sun != null)
            {
                _sun.color = Color.Lerp(_sunBase * look.Sun, RedLight, _t);
                _sun.intensity = _sunIntensityBase * look.SunMul;
                _sun.transform.rotation = Quaternion.Euler(_sunEulerBase.x + look.ElevDelta, _sunEulerBase.y + look.YawDelta, _sunEulerBase.z);
            }
            RenderSettings.ambientIntensity = _ambientIntensityBase * look.AmbientMul;
            RenderSettings.ambientLight = Color.Lerp(_ambientBase, RedSky * 0.8f, _t * 0.6f);
            Color outside = Color.Lerp(look.Outside, OutsideRed, _t);
            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = outside;
            if (_hasSkyTint) _sky.SetColor("_SkyTint", Color.Lerp(_skyTintBase, RedSky, _t));

            // 안개: 판 바깥 색으로 먼 칸을 살짝 녹임. 화면 가운데 칸까지의 거리 기준 — 가까운 칸·말은 안 가림
            if (cam != null && look.Fog > 0.01f)
            {
                float d = GroundDistance(cam);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = outside;
                RenderSettings.fogStartDistance = d * (1.6f - 0.7f * look.Fog);
                RenderSettings.fogEndDistance = d * (3.2f - 1.2f * look.Fog);
            }
            else RenderSettings.fog = false;
        }

        // 카메라에서 화면 가운데가 닿는 바닥(y=0)까지 거리
        private static float GroundDistance(Camera cam)
        {
            var f = cam.transform.forward;
            if (f.y < -0.05f) return cam.transform.position.y / -f.y;
            return 30f;
        }
    }
}
