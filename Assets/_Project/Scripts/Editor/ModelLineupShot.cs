using Fantasia.Data;
using Fantasia.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fantasia.Editor
{
    // 3D 에셋 요청서용 사진: 지금 임시 인형 9종을 게임 속 크기 그대로 한 줄로 세워 찍는다.
    // 왼쪽 끝 눈금 기둥은 1m마다 흰·회색이 바뀐다. 결과: Logs/model_lineup.png, model_lineup_front.png
    public static class ModelLineupShot
    {
        private static readonly string[] Jobs = { "Job_명탐정", "Job_역병의사", "Job_저격수" };
        private static readonly string[] Enemies =
            { "Enemy_역병쥐", "Enemy_연성독거미유체", "Enemy_가고일유체", "Enemy_그림자사냥개", "Enemy_인조그림자사신", "Enemy_검시관아벨모로" };

        public static void Capture()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            const float spacing = 2.6f;
            float x = 0f;

            // 눈금 기둥 (1m 단위, 4m)
            for (int m = 0; m < 4; m++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.transform.position = new Vector3(x, m + 0.5f, 0f);
                c.transform.localScale = new Vector3(0.15f, 1f, 0.15f);
                c.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = m % 2 == 0 ? Color.white : Color.gray };
            }
            x += spacing * 0.7f;

            foreach (var name in Jobs)
            {
                var job = AssetDatabase.LoadAssetAtPath<JobDefinition>($"Assets/_Project/Data/Jobs/{name}.asset");
                Place(CharacterModels.Create(job, out _), ref x, spacing);
            }
            foreach (var name in Enemies)
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"Assets/_Project/Data/Enemies/{name}.asset");
                Place(CharacterModels.Create(enemy, out _), ref x, spacing);
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = new Vector3(x * 0.5f, 0f, 0f);
            ground.transform.localScale = new Vector3(x / 8f + 1f, 1f, 1f);
            ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.5f, 0.48f) };

            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.15f, 0.18f);
            cam.orthographic = true;
            cam.orthographicSize = 2.3f;
            cam.transform.position = new Vector3((x - spacing) * 0.5f, 2.0f, -20f);
            cam.transform.rotation = Quaternion.identity;
            EditorScreenshotUtility.Capture(cam, "model_lineup.png", 2400, 440);

            // 비스듬히 본 모습
            cam.orthographic = false;
            cam.fieldOfView = 22f;
            cam.transform.position = new Vector3(x * 0.5f, 6f, -22f);
            cam.transform.LookAt(new Vector3(x * 0.5f, 1.2f, 0f));
            EditorScreenshotUtility.Capture(cam, "model_lineup_angle.png", 2400, 800);
        }

        private static void Place(GameObject model, ref float x, float spacing)
        {
            model.transform.position = new Vector3(x, 0f, 0f);
            model.transform.rotation = Quaternion.LookRotation(Vector3.back); // 정면(+Z)이 카메라 쪽
            x += spacing;
        }
    }
}
