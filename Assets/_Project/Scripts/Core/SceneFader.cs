using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Fantasia.Core
{
    // 11/15 팀장 남은 일 9: 전투 → 보드로 돌아올 때 씬 불러오기 첫 프레임(약 0.2~0.4초)이 그대로 보여
    // 결과 창이 멈춘 듯 보임 → 짧게 어두워진 뒤 씬을 바꾸고, 무거운 첫 프레임들이 지나간 다음 밝아짐.
    // 보드 ↔ 전투 ↔ 메인 메뉴 전환에 씀. 시간 정지(일시정지 메뉴) 중에도 움직이게 실제 시간 기준.
    public class SceneFader : MonoBehaviour
    {
        public const float OutSeconds = 0.18f;
        public const float InSeconds = 0.3f;
        private const int SettleFrames = 3; // 씬이 바뀐 뒤 무거운 프레임 수만큼 검은 화면 유지

        private static SceneFader _instance;
        private Image _cover;
        private bool _busy;
        private int _loadedFrame = -1;

        public static bool Busy => _instance != null && _instance._busy;

        public static void Load(string sceneName)
        {
            Ensure();
            if (_instance._busy) return; // 두 번 눌러도 한 번만
            _instance.StartCoroutine(_instance.Run(sceneName));
        }

        private static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("SceneFader");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SceneFader>();
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000; // 모든 화면 위
            go.AddComponent<GraphicRaycaster>();
            var img = new GameObject("Cover").AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;
            _instance._cover = img;
            SceneManager.sceneLoaded += (_, _) => _instance._loadedFrame = Time.frameCount;
        }

        private IEnumerator Run(string sceneName)
        {
            _busy = true;
            _cover.raycastTarget = true; // 전환 중 클릭 막기
            for (float t = 0f; t < OutSeconds; t += Time.unscaledDeltaTime)
            {
                SetAlpha(t / OutSeconds);
                yield return null;
            }
            SetAlpha(1f);
            _loadedFrame = -1;
            SceneManager.LoadScene(sceneName);
            while (_loadedFrame < 0) yield return null;
            while (Time.frameCount - _loadedFrame < SettleFrames) yield return null;
            _cover.raycastTarget = false;
            for (float t = 0f; t < InSeconds; t += Time.unscaledDeltaTime)
            {
                SetAlpha(1f - t / InSeconds);
                yield return null;
            }
            SetAlpha(0f);
            _busy = false;
        }

        private void SetAlpha(float a) => _cover.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a));
    }
}
