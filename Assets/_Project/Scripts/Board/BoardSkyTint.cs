using Fantasia.Core;
using Fantasia.World;
using UnityEngine;

namespace Fantasia.Board
{
    // D25 영혼 수확 75 이상 → 하늘이 붉게 (강림 전조, Decisions.md). ☆ 몇 초에 걸쳐 서서히, 75 아래로 내려가면 복귀.
    // 아트(붉은 스카이박스)가 오기 전까지는 하늘 색·햇빛·환경광을 붉게 섞는다.
    public class BoardSkyTint : MonoBehaviour
    {
        private const float FadeSeconds = 3f;
        private static readonly Color RedSky = new Color(0.62f, 0.12f, 0.1f);
        private static readonly Color RedLight = new Color(1f, 0.55f, 0.48f);

        private Light _sun;
        private Color _sunBase, _ambientBase, _bgBase;
        private Material _sky;
        private Color _skyTintBase;
        private bool _hasSkyTint;
        private float _t;

        private void Start()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { _sun = l; break; }
            if (_sun != null) _sunBase = _sun.color;
            _ambientBase = RenderSettings.ambientLight;
            if (Camera.main != null) _bgBase = Camera.main.backgroundColor;
            if (RenderSettings.skybox != null)
            {
                _sky = new Material(RenderSettings.skybox); // 원본 자산은 건드리지 않음
                RenderSettings.skybox = _sky;
                _hasSkyTint = _sky.HasProperty("_SkyTint");
                if (_hasSkyTint) _skyTintBase = _sky.GetColor("_SkyTint");
            }
            _t = Target; // 씬을 다시 열면 바로 그 상태로
            Apply();
        }

        private static float Target => BoardSession.Instance != null && BoardSession.Instance.SoulHarvest >= BloodHuntRules.RedSkyThreshold ? 1f : 0f;

        private void Update()
        {
            float target = Target;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, Time.deltaTime / FadeSeconds);
            Apply();
        }

        private void Apply()
        {
            if (_sun != null) _sun.color = Color.Lerp(_sunBase, RedLight, _t);
            RenderSettings.ambientLight = Color.Lerp(_ambientBase, RedSky * 0.8f, _t * 0.6f);
            if (Camera.main != null) Camera.main.backgroundColor = Color.Lerp(_bgBase, RedSky, _t);
            if (_hasSkyTint) _sky.SetColor("_SkyTint", Color.Lerp(_skyTintBase, RedSky, _t));
        }
    }
}
