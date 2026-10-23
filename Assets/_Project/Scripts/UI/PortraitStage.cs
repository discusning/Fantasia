using System.Collections.Generic;
using Fantasia.Data;
using UnityEngine;

namespace Fantasia.UI
{
    // 초상 칸에 3D 모델을 띄우는 작은 촬영장. 화면 밖 먼 곳(메인 카메라 시야 밖)에 모델 +
    // 전용 카메라 + 조명을 두고 RenderTexture로 찍는다 — 그 텍스처를 PortraitView 등이 보여준다.
    // 모델은 CharacterModels가 고른다 (데이터의 Model 프리팹, 없으면 임시 인형).
    public sealed class PortraitStage
    {
        private static readonly Vector3 StudioOrigin = new Vector3(0f, -5000f, 0f);
        private const float SlotSpacing = 20f; // 카메라 far clip(8)보다 멀리 — 옆 촬영장이 안 찍히게
        private const int TextureSize = 256;
        private static readonly Color Backdrop = new Color(0.13f, 0.12f, 0.15f);

        public RenderTexture Texture { get; }
        private readonly GameObject _root;
        private readonly Camera _camera;

        public PortraitStage(Transform owner, int slot, GameObject model, float modelHeight)
        {
            _root = new GameObject($"PortraitStage{slot}");
            _root.transform.SetParent(owner, false);
            _root.transform.position = StudioOrigin + Vector3.right * (slot * SlotSpacing);

            model.transform.SetParent(_root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.AddComponent<PortraitIdleSway>().Phase = slot * 1.7f;

            Texture = new RenderTexture(TextureSize, TextureSize, 16) { antiAliasing = 2, name = $"Portrait{slot}" };

            // 상반신 구도 — 모델 키의 위쪽 1/3(머리·어깨)이 칸에 차게. 키가 작은 적(쥐 등)은 더 가까이.
            float lookY = modelHeight * 0.68f;
            float distance = Mathf.Max(1.6f, modelHeight * 1.25f);
            var camGO = new GameObject("PortraitCamera");
            camGO.transform.SetParent(_root.transform, false);
            camGO.transform.localPosition = new Vector3(0f, lookY + modelHeight * 0.06f, distance);
            camGO.transform.LookAt(_root.transform.position + new Vector3(0f, lookY, 0f));
            _camera = camGO.AddComponent<Camera>();
            _camera.fieldOfView = 30f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 8f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Backdrop;
            _camera.targetTexture = Texture;
            _camera.depth = -10f;

            var lightGO = new GameObject("PortraitLight");
            lightGO.transform.SetParent(_root.transform, false);
            lightGO.transform.localPosition = new Vector3(1.4f, lookY + 1.2f, distance * 0.8f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 2.2f;
        }

        // 에디터 미리보기처럼 카메라가 자동으로 안 돌 때 강제로 한 번 찍는다.
        public void RenderNow() => _camera.Render();

        public void Dispose()
        {
            _camera.targetTexture = null;
            Texture.Release();
            DestroySafe(Texture);
            DestroySafe(_root);
        }

        internal static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }

    // 씬마다 하나 — 직업/적 종류별 초상 텍스처를 한 번만 찍어 HUD·타임라인·적 HP 바가 같이 쓴다.
    // 씬이 바뀌면 이 오브젝트와 함께 촬영장도 사라진다.
    public class PortraitStudio : MonoBehaviour
    {
        private static PortraitStudio _instance;
        private readonly Dictionary<ScriptableObject, PortraitStage> _stages = new();

        public static Texture TextureFor(ScriptableObject definition)
        {
            if (definition == null) return null;
            if (_instance == null) _instance = new GameObject("PortraitStudio").AddComponent<PortraitStudio>();

            if (!_instance._stages.TryGetValue(definition, out var stage))
            {
                var model = CharacterModels.Create(definition, out float height);
                stage = new PortraitStage(_instance.transform, _instance._stages.Count, model, height);
                _instance._stages[definition] = stage;
            }
            return stage.Texture;
        }

        public static void RenderAllNow()
        {
            if (_instance == null) return;
            foreach (var stage in _instance._stages.Values) stage.RenderNow();
        }

        private void OnDestroy()
        {
            foreach (var stage in _stages.Values) stage.Dispose();
            _stages.Clear();
            if (_instance == this) _instance = null;
        }
    }

    // 직업·적 데이터 → 3D 모델. 데이터에 Model 프리팹이 있으면 그걸, 없으면 임시 인형.
    // 전투 필드와 초상이 같은 함수를 써서 항상 같은 모습이 나온다.
    public static class CharacterModels
    {
        public const float HumanHeight = 2.4f;

        public static GameObject Create(ScriptableObject definition, out float height)
        {
            switch (definition)
            {
                case JobDefinition job:
                    height = HumanHeight;
                    return job.Model != null ? Object.Instantiate(job.Model) : PlaceholderFigure.Build(job);
                case EnemyDefinition enemy:
                    height = PlaceholderFigure.EnemyHeight(enemy);
                    return enemy.Model != null ? Object.Instantiate(enemy.Model) : PlaceholderFigure.Build(enemy);
                default:
                    height = HumanHeight;
                    return new GameObject("EmptyModel");
            }
        }
    }

    // 초상 속 모델이 살짝 좌우로 몸을 트는 대기 동작 — 정지 그림이 아니라 3D라는 느낌만.
    public class PortraitIdleSway : MonoBehaviour
    {
        public float Phase;
        private const float Degrees = 18f;
        private const float Speed = 0.6f;

        private void Update()
        {
            float yaw = Mathf.Sin(Time.time * Speed + Phase) * Degrees;
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
