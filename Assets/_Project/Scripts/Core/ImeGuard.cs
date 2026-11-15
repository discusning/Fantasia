using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fantasia.Core
{
    // 11/15 팀장 "게임 중 몇몇 키가 잠김": 한글 입력기(IME)가 한글 상태면 Q·H·I·B·F 같은 글자 키가
    // 입력기 조합으로 들어가 Input.GetKeyDown에 잡히지 않는다 (Space·Esc·숫자는 됨).
    // 메인 메뉴의 이름 입력에서 한글을 친 뒤 그대로 게임에 들어가면 생김.
    // → 메인 메뉴(이름 입력)만 입력기 허용, 보드·전투 씬에서는 입력기를 끈다.
    public static class ImeGuard
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply(scene);

        private static void Apply(Scene scene)
        {
            Input.imeCompositionMode = scene.name == Fantasia.UI.MainMenuController.SceneName ? IMECompositionMode.Auto : IMECompositionMode.Off;
        }

        public static IMECompositionMode DevMode => Input.imeCompositionMode;
    }
}
