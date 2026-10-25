using Fantasia.Core;
using UnityEditor;

namespace Fantasia.Editor
{
    // 플레이테스트 기록 폴더 열기 (battles.csv · economy.csv — Core/PlaytestLog)
    public static class PlaytestLogMenu
    {
        [MenuItem("Fantasia/Playtest/Open Log Folder")]
        private static void Open()
        {
            System.IO.Directory.CreateDirectory(PlaytestLog.Folder);
            EditorUtility.RevealInFinder(PlaytestLog.Folder);
        }
    }
}
