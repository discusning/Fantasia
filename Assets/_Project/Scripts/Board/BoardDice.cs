using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.Board
{
    // 보드 주사위 연출 (2026-11-06, 받은 로마 숫자 주사위 Resources/BoardArt/Dice).
    // 결과는 BoardTestController가 먼저 정하고 저장한다 → 여기서는 그 결과 면이 위로 오게 굴러 멈추는 모습만 보여 준다.
    // 보드 위에 두면 멀고 건물에 가려 숫자가 안 읽혀서, 보드 아래 떨어진 "작은 무대"에서 전용 카메라로 찍어
    // 화면 가운데 위쪽에 띄운다 (무대는 주사위 전용 층이라 보드 카메라에는 안 보임).
    // 모양용 회전·튀는 정도는 UnityEngine.Random (게임 결과 난수 GameRandom을 쓰지 않아 저장·이어하기 결과가 같음).
    // 모델이 없으면 아무것도 하지 않고 바로 끝난 것으로 친다.
    public class BoardDice : MonoBehaviour
    {
        private const float Duration = 1.0f;     // 떨어져서 멈출 때까지
        private const float DropHeight = 2.2f;
        private const int Layer = 31;            // 주사위 전용 층
        private static readonly Vector3 Stage = new Vector3(0f, -300f, 0f);
        private const float ScreenSize = 240f;   // 화면에 보이는 크기 (1920×1080 기준)

        // 모델 방향별 숫자 (Editor/DiceFaceShot로 확인): 면이 바라보는 방향 n, 숫자를 바로 읽을 때 글자 위쪽 t
        private static readonly (Vector3 n, Vector3 t)[] Faces =
        {
            (Vector3.zero, Vector3.zero),        // 0 (안 씀)
            (Vector3.forward, Vector3.up),       // I
            (Vector3.left, Vector3.up),          // II
            (Vector3.back, Vector3.up),          // III
            (Vector3.right, Vector3.up),         // IV
            (Vector3.up, Vector3.left),          // V
            (Vector3.down, Vector3.right),       // VI
        };

        private GameObject _die;
        private Camera _cam;
        private GameObject _view;
        private Coroutine _play;
        public bool Rolling { get; private set; }

        public static BoardDice Create(Transform parent)
        {
            var go = new GameObject("BoardDice");
            go.transform.SetParent(parent, false);
            var dice = go.AddComponent<BoardDice>();
            var prefab = Resources.Load<GameObject>("BoardArt/Dice");
            if (prefab != null) dice.Build(prefab);
            return dice;
        }

        private void Build(GameObject prefab)
        {
            _die = Instantiate(prefab);
            _die.name = "Dice";
            foreach (var t in _die.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            _die.transform.SetParent(transform, true);

            // 무대 카메라: 비스듬히 내려다봄 (보드 카메라와 비슷한 각도)
            var camGo = new GameObject("DiceCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.cullingMask = 1 << Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 20f;
            _cam.transform.position = Stage + new Vector3(0f, 3.4f, -2.4f);
            _cam.transform.LookAt(Stage + Vector3.up * 0.3f);
            _cam.targetTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _cam.enabled = false;
            if (Camera.main != null) Camera.main.cullingMask &= ~(1 << Layer);

            // 화면에 띄우기
            var canvasGo = new GameObject("DiceView");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5; // 보드 안내·카드 위, 창(인벤토리·상점 등) 아래
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var img = new GameObject("Dice").AddComponent<RawImage>();
            img.transform.SetParent(canvasGo.transform, false);
            img.texture = _cam.targetTexture;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.62f);
            rt.sizeDelta = new Vector2(ScreenSize, ScreenSize);
            _view = canvasGo;
            Show(false);
        }

        private void Show(bool on)
        {
            if (_die == null) return;
            _die.SetActive(on);
            _view.SetActive(on);
            _cam.enabled = on;
        }

        // 결과 value(1~6) 면이 위, 글자 위쪽이 화면 위(카메라 반대쪽)로 오게 놓는 회전
        private Quaternion RestRotation(int value)
        {
            var (n, t) = Faces[Mathf.Clamp(value, 1, 6)];
            Vector3 away = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
            return Quaternion.LookRotation(away, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(t, n));
        }

        // 굴리기 → 다 멈추면 onLanded. 모델이 없으면 바로 onLanded.
        public void Roll(int value, System.Action onLanded)
        {
            if (_play != null) StopCoroutine(_play);
            if (_die == null) { onLanded?.Invoke(); return; }
            _play = StartCoroutine(RollRoutine(value, onLanded));
        }

        public void Hide()
        {
            if (_play != null) { StopCoroutine(_play); _play = null; }
            Rolling = false;
            Show(false);
        }

        private IEnumerator RollRoutine(int value, System.Action onLanded)
        {
            Rolling = true;
            Vector3 rest = Stage + Vector3.up * 0.5f; // 한 변 1m → 중심이 0.5 위
            Quaternion target = RestRotation(value);
            Quaternion start = Random.rotationUniform;
            Vector3 axis = Random.onUnitSphere;
            float spin = Random.Range(900f, 1300f); // 도/초, 점점 느려짐
            Vector3 slide = new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(0.2f, 0.6f)); // 살짝 미끄러지며 들어옴
            Show(true);

            for (float t = 0f; t < Duration; t += Time.deltaTime)
            {
                float u = t / Duration;
                // 높이: 0~0.5 떨어짐(가속) → 0.5~0.8 한 번 튐 → 0.8~1 바닥
                float y = u < 0.5f ? DropHeight * (1f - (u / 0.5f) * (u / 0.5f))
                        : u < 0.8f ? 0.3f * Mathf.Sin(Mathf.PI * (u - 0.5f) / 0.3f)
                        : 0f;
                _die.transform.position = rest + Vector3.up * y + slide * (1f - Mathf.SmoothStep(0f, 1f, u));
                var spinning = Quaternion.AngleAxis(spin * t * (1f - u * 0.5f), axis) * start;
                float settle = Mathf.SmoothStep(0f, 1f, (u - 0.35f) / 0.6f);
                _die.transform.rotation = Quaternion.Slerp(spinning, target, settle);
                yield return null;
            }
            _die.transform.SetPositionAndRotation(rest, target);
            Rolling = false;
            _play = null;
            onLanded?.Invoke();
        }

        private void OnDestroy()
        {
            if (_cam != null && _cam.targetTexture != null) _cam.targetTexture.Release();
        }
    }
}
