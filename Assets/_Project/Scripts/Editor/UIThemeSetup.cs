using System.IO;
using Fantasia.UI;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 빈 UITheme 에셋을 Resources에 만들어 둔다 — 디자이너가 여기에 스프라이트·프리팹·글꼴을 넣으면
    // 전투·HUD UI가 바로 그 아트로 바뀐다 (비어 있으면 지금처럼 단색).
    public static class UIThemeSetup
    {
        private const string AssetPath = "Assets/_Project/Resources/" + UITheme.ResourcePath + ".asset";

        [MenuItem("Fantasia/Data/Create UI Theme Asset")]
        public static UITheme EnsureAsset()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UITheme>(AssetPath);
            if (theme != null) return theme;

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            theme = ScriptableObject.CreateInstance<UITheme>();
            AssetDatabase.CreateAsset(theme, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[UIThemeSetup] 빈 UI 테마 생성: " + AssetPath);
            return theme;
        }
    }
}
