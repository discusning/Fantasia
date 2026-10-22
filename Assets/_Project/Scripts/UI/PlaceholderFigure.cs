using Fantasia.Data;
using UnityEngine;

namespace Fantasia.UI
{
    // 3D 에셋이 들어오기 전 초상용 임시 인형 — 기본 도형(캡슐·구·원기둥·큐브)으로 몸통·머리에
    // 직업을 알아볼 소품 하나씩 (명탐정 중절모, 역병 의사 부리 가면+넓은 챙 모자, 저격수 장총).
    // 정면은 +Z (PortraitStage 카메라가 +Z 쪽에서 바라본다). 실제 모델은 JobDefinition.PortraitModel.
    public static class PlaceholderFigure
    {
        private static readonly Color Skin = new Color(0.93f, 0.8f, 0.68f);

        public static GameObject Build(JobDefinition job)
        {
            var root = new GameObject($"PlaceholderFigure_{job.Id}");

            Color coat = job.Id switch
            {
                JobId.Detective => new Color(0.48f, 0.34f, 0.22f),
                JobId.PlagueDoctor => new Color(0.14f, 0.14f, 0.16f),
                JobId.Sniper => new Color(0.24f, 0.34f, 0.24f),
                _ => new Color(0.35f, 0.3f, 0.4f),
            };

            Part(root, PrimitiveType.Capsule, new Vector3(0f, 0.85f, 0f), new Vector3(0.95f, 0.85f, 0.7f), coat);   // 몸통
            Part(root, PrimitiveType.Sphere, new Vector3(0f, 1.95f, 0f), Vector3.one * 0.6f, Skin);                 // 머리

            switch (job.Id)
            {
                case JobId.Detective:
                    var hatColor = new Color(0.28f, 0.2f, 0.14f);
                    Part(root, PrimitiveType.Cylinder, new Vector3(0f, 2.2f, 0f), new Vector3(0.8f, 0.025f, 0.8f), hatColor);  // 챙
                    Part(root, PrimitiveType.Cylinder, new Vector3(0f, 2.36f, 0f), new Vector3(0.48f, 0.15f, 0.48f), hatColor); // 크라운
                    break;

                case JobId.PlagueDoctor:
                    var bone = new Color(0.86f, 0.8f, 0.64f);
                    var beak = Part(root, PrimitiveType.Capsule, new Vector3(0f, 1.88f, 0.42f), new Vector3(0.16f, 0.3f, 0.16f), bone);
                    beak.transform.localRotation = Quaternion.Euler(75f, 0f, 0f); // 아래로 살짝 숙인 부리
                    var black = new Color(0.08f, 0.08f, 0.09f);
                    Part(root, PrimitiveType.Cylinder, new Vector3(0f, 2.2f, 0f), new Vector3(1f, 0.02f, 1f), black);
                    Part(root, PrimitiveType.Cylinder, new Vector3(0f, 2.34f, 0f), new Vector3(0.5f, 0.13f, 0.5f), black);
                    break;

                case JobId.Sniper:
                    var wood = new Color(0.36f, 0.24f, 0.14f);
                    var rifle = Part(root, PrimitiveType.Cube, new Vector3(0.42f, 1.55f, -0.2f), new Vector3(0.08f, 1.5f, 0.08f), wood);
                    rifle.transform.localRotation = Quaternion.Euler(0f, 0f, -18f); // 어깨에 비스듬히 멘 장총
                    Part(root, PrimitiveType.Sphere, new Vector3(0f, 2.05f, -0.05f), new Vector3(0.66f, 0.5f, 0.66f), coat); // 후드
                    break;
            }

            return root;
        }

        private static GameObject Part(GameObject root, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            PortraitStage.DestroySafe(go.GetComponent<Collider>()); // 보드 클릭 레이캐스트에 걸리지 않게
            go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = color };
            return go;
        }
    }
}
