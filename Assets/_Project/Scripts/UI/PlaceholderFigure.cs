using Fantasia.Data;
using UnityEngine;

namespace Fantasia.UI
{
    // 3D 에셋이 들어오기 전 초상용 임시 인형 — 기본 도형(캡슐·구·원기둥·큐브)으로 몸통·머리에
    // 직업을 알아볼 소품 하나씩 (명탐정 중절모, 역병 의사 부리 가면+넓은 챙 모자, 저격수 장총).
    // 정면은 +Z (PortraitStage 카메라가 +Z 쪽에서 바라본다). 실제 모델은 JobDefinition.Model.
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

        // 적 임시 인형 — 1막 소형 4종은 실루엣으로 구분되게, 그 외는 크기별 기본형.
        public static GameObject Build(EnemyDefinition enemy)
        {
            var root = new GameObject($"PlaceholderEnemy_{enemy.Id}");
            switch (enemy.Id)
            {
                case "enemy.small.shadow_hound": // 네 발 짐승 + 붉은 눈
                {
                    var shadow = new Color(0.16f, 0.13f, 0.24f);
                    Rotated(Part(root, PrimitiveType.Capsule, new Vector3(0f, 0.75f, 0f), new Vector3(0.55f, 0.6f, 0.5f), shadow), 90f, 0f, 0f);
                    Part(root, PrimitiveType.Sphere, new Vector3(0f, 1.05f, 0.7f), new Vector3(0.48f, 0.42f, 0.55f), shadow);
                    Part(root, PrimitiveType.Sphere, new Vector3(-0.1f, 1.12f, 0.95f), Vector3.one * 0.08f, new Color(0.95f, 0.15f, 0.15f));
                    Part(root, PrimitiveType.Sphere, new Vector3(0.1f, 1.12f, 0.95f), Vector3.one * 0.08f, new Color(0.95f, 0.15f, 0.15f));
                    foreach (var (x, z) in new[] { (-0.2f, 0.4f), (0.2f, 0.4f), (-0.2f, -0.4f), (0.2f, -0.4f) })
                        Part(root, PrimitiveType.Cube, new Vector3(x, 0.3f, z), new Vector3(0.12f, 0.6f, 0.12f), shadow);
                    break;
                }
                case "enemy.small.alchemic_spider_larva": // 납작한 몸통 + 다리 6개
                {
                    var chimera = new Color(0.36f, 0.46f, 0.2f);
                    Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.45f, -0.1f), new Vector3(0.9f, 0.5f, 1f), chimera);
                    Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.5f, 0.5f), Vector3.one * 0.42f, new Color(0.5f, 0.3f, 0.45f));
                    for (int i = 0; i < 3; i++)
                    {
                        float z = 0.25f - i * 0.3f;
                        Rotated(Part(root, PrimitiveType.Cube, new Vector3(-0.6f, 0.3f, z), new Vector3(0.6f, 0.06f, 0.06f), chimera), 0f, 0f, 30f);
                        Rotated(Part(root, PrimitiveType.Cube, new Vector3(0.6f, 0.3f, z), new Vector3(0.6f, 0.06f, 0.06f), chimera), 0f, 0f, -30f);
                    }
                    break;
                }
                case "enemy.small.plague_rat": // 작은 몸 + 긴 꼬리
                {
                    var fur = new Color(0.42f, 0.36f, 0.3f);
                    Rotated(Part(root, PrimitiveType.Capsule, new Vector3(0f, 0.4f, 0f), new Vector3(0.45f, 0.45f, 0.42f), fur), 90f, 0f, 0f);
                    Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.5f, 0.5f), new Vector3(0.36f, 0.32f, 0.42f), fur);
                    Part(root, PrimitiveType.Sphere, new Vector3(-0.12f, 0.7f, 0.42f), Vector3.one * 0.14f, new Color(0.8f, 0.6f, 0.6f));
                    Part(root, PrimitiveType.Sphere, new Vector3(0.12f, 0.7f, 0.42f), Vector3.one * 0.14f, new Color(0.8f, 0.6f, 0.6f));
                    Rotated(Part(root, PrimitiveType.Cube, new Vector3(0f, 0.3f, -0.75f), new Vector3(0.05f, 0.05f, 0.8f), new Color(0.75f, 0.55f, 0.55f)), -10f, 0f, 0f);
                    break;
                }
                case "enemy.small.gargoyle_larva": // 돌 몸통 + 날개 + 뿔
                {
                    var stone = new Color(0.5f, 0.5f, 0.53f);
                    Part(root, PrimitiveType.Cube, new Vector3(0f, 0.8f, 0f), new Vector3(0.75f, 0.95f, 0.6f), stone);
                    Part(root, PrimitiveType.Cube, new Vector3(0f, 1.5f, 0.05f), Vector3.one * 0.5f, stone);
                    Rotated(Part(root, PrimitiveType.Cube, new Vector3(-0.6f, 1.15f, -0.2f), new Vector3(0.7f, 0.5f, 0.05f), stone), 0f, 25f, 20f);
                    Rotated(Part(root, PrimitiveType.Cube, new Vector3(0.6f, 1.15f, -0.2f), new Vector3(0.7f, 0.5f, 0.05f), stone), 0f, -25f, -20f);
                    Part(root, PrimitiveType.Cube, new Vector3(-0.17f, 1.85f, 0.05f), new Vector3(0.08f, 0.25f, 0.08f), new Color(0.3f, 0.3f, 0.32f));
                    Part(root, PrimitiveType.Cube, new Vector3(0.17f, 1.85f, 0.05f), new Vector3(0.08f, 0.25f, 0.08f), new Color(0.3f, 0.3f, 0.32f));
                    break;
                }
                default: // 아직 전용 인형이 없는 적 — 크기만 반영한 붉은 인형
                {
                    float h = EnemyHeight(enemy);
                    var red = new Color(0.55f, 0.18f, 0.2f);
                    Part(root, PrimitiveType.Capsule, new Vector3(0f, h * 0.35f, 0f), new Vector3(h * 0.4f, h * 0.35f, h * 0.3f), red);
                    Part(root, PrimitiveType.Sphere, new Vector3(0f, h * 0.82f, 0f), Vector3.one * h * 0.25f, red);
                    break;
                }
            }
            return root;
        }

        // 초상 카메라 구도·전장 배치에 쓰는 대략적인 키.
        public static float EnemyHeight(EnemyDefinition enemy) => enemy.Id switch
        {
            "enemy.small.shadow_hound" => 1.3f,
            "enemy.small.alchemic_spider_larva" => 0.9f,
            "enemy.small.plague_rat" => 0.8f,
            "enemy.small.gargoyle_larva" => 2f,
            _ => enemy.Size switch
            {
                EnemySize.Small => 1.4f,
                EnemySize.Medium => 2.2f,
                EnemySize.Large => 3f,
                _ => 3.2f,
            },
        };

        private static void Rotated(GameObject go, float x, float y, float z) => go.transform.localRotation = Quaternion.Euler(x, y, z);

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
