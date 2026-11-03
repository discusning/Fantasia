using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI.Editor
{
    [CustomEditor(typeof(HangingPanelDrop))]
    public sealed class HangingPanelDropEditor : UnityEditor.Editor
    {
        private const string PrefabPath = "Assets/_Project/Resources/PanelDrop/HangingPanel.prefab";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            HangingPanelDrop panel = (HangingPanelDrop)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Build / Refresh Artwork Preview"))
            {
                Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Build Panel Artwork");
                panel.BuildPreview();
                EditorUtility.SetDirty(panel);
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
                if (GUILayout.Button("Play Drop + Chain Rattle")) panel.Play();
            if (GUILayout.Button("Show at Rest")) panel.ShowImmediate();
            if (GUILayout.Button("Select Content (add TMP text here)"))
            {
                panel.BuildPreview();
                if (panel.ContentRoot != null) Selection.activeGameObject = panel.ContentRoot.gameObject;
            }
            EditorGUILayout.HelpBox(
                "Play On Enable replays when enabled. Use Unscaled Time also works at timeScale 0. " +
                "Add labels and result UI under Board/Content so they follow the board.", MessageType.Info);
        }

        [MenuItem("Tools/Fantasia/Panel Drop/Create Demo Canvas")]
        private static void CreateDemoCanvas()
        {
            CreateDemoForPrefab(PrefabPath, "Fantasia Simple Panel Demo");
        }

        [MenuItem("Tools/Fantasia/Panel Drop/Create Victory Demo Canvas")]
        private static void CreateVictoryDemoCanvas()
        {
            CreateDemoForPrefab("Assets/_Project/Resources/PanelDrop/VictoryPanel.prefab",
                "Fantasia Victory Panel Demo");
        }

        [MenuItem("Tools/Fantasia/Panel Drop/Create Defeat Demo Canvas")]
        private static void CreateDefeatDemoCanvas()
        {
            CreateDemoForPrefab("Assets/_Project/Resources/PanelDrop/DefeatPanel.prefab",
                "Fantasia Defeat Panel Demo");
        }

        private static void CreateDemoForPrefab(string path, string canvasName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Fantasia", "Import the complete Fantasia PanelDrop package first.", "OK");
                return;
            }
            GameObject canvasObject = new GameObject(canvasName, typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Panel Drop Demo");
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject background = new GameObject("Demo Backdrop", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasObject.transform, false);
            RectTransform bg = (RectTransform)background.transform;
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            Image bgImage = background.GetComponent<Image>();
            bgImage.color = new Color(0.055f, 0.047f, 0.043f, 1f);
            bgImage.raycastTarget = false;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasObject.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Create Hanging Panel");
            RectTransform rt = (RectTransform)instance.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            HangingPanelDrop panel = instance.GetComponent<HangingPanelDrop>();
            panel.loop = true;
            panel.BuildPreview();

            if (Object.FindObjectOfType<AudioListener>() == null)
            {
                GameObject listener = new GameObject("UI Demo Audio Listener", typeof(AudioListener));
                Undo.RegisterCreatedObjectUndo(listener, "Create Audio Listener");
                listener.transform.SetParent(canvasObject.transform, false);
            }
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            SceneView.RepaintAll();
        }
    }
}
