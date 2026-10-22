using Fantasia.Data;
using UnityEngine;

namespace Fantasia.UI
{
    // 초상 칸에 3D 모델을 띄우는 작은 촬영장. 화면 밖 먼 곳(메인 카메라 시야 밖)에 모델 +
    // 전용 카메라 + 조명을 두고 RenderTexture로 찍는다 — 그 텍스처를 PortraitView가 보여준다.
    // 모델은 JobDefinition.PortraitModel 프리팹, 없으면 PlaceholderFigure로 조립한 임시 인형.
    public sealed class PortraitStage
    {
        private static readonly Vector3 StudioOrigin = new Vector3(0f, -5000f, 0f);
        private const float SlotSpacing = 20f; // 카메라 far clip(8)보다 멀리 — 옆 촬영장이 안 찍히게
        private const int TextureSize = 256;
        private static readonly Color Backdrop = new Color(0.13f, 0.12f, 0.15f);

        public RenderTexture Texture { get; }
        private readonly GameObject _root;
        private readonly Camera _camera;

        public PortraitStage(Transform owner, int slot, JobDefinition job)
        {
            _root = new GameObject($"PortraitStage{slot}");
            _root.transform.SetParent(owner, false);
            _root.transform.position = StudioOrigin + Vector3.right * (slot * SlotSpacing);

            var model = job.PortraitModel != null ? Object.Instantiate(job.PortraitModel) : PlaceholderFigure.Build(job);
            model.transform.SetParent(_root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.AddComponent<PortraitIdleSway>().Phase = slot * 1.7f;

            Texture = new RenderTexture(TextureSize, TextureSize, 16) { antiAliasing = 2, name = $"Portrait{slot}" };

            // 상반신 구도 — 키 약 2.4 기준으로 머리·어깨가 칸에 차게.
            var camGO = new GameObject("PortraitCamera");
            camGO.transform.SetParent(_root.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.75f, 3f);
            camGO.transform.LookAt(_root.transform.position + new Vector3(0f, 1.6f, 0f));
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
            lightGO.transform.localPosition = new Vector3(1.4f, 2.8f, 2.4f);
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
