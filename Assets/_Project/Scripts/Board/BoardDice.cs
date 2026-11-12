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
        // 11/12 팀장 지적: ① 위쪽 보이지 않는 벽에서 갑자기 튀어나옴(그림 틀 밖에서 떨어졌음) · 끊겨 보임(한 프레임 15~20° 회전 + 섞기)
        //   ② 멈춘 주사위가 말 위에 남아 칸을 가림 → 눈을 1.3초 보여 준 뒤 화면 오른쪽으로 옮겨 이번 턴 동안 둠.
        // ①: 그림 틀을 넓히고(화각 30→50) 처음부터 틀 안에서 낮게 던져 굴림. 회전은 "남은 각도"가 부드럽게 0으로 줄어 결과 면에서 딱 멈춤.
        private const float Duration = 1.15f;    // 던져서 멈출 때까지
        private const float DropHeight = 0.9f;
        public const float HoldSeconds = 1.3f;   // 결과 눈을 가운데에서 보여 주는 시간 (기획 요청 1~2초)
        private const float DockSeconds = 0.45f; // 오른쪽으로 옮기는 시간
        private const float DockScale = 0.8f;
        private static readonly Vector2 DockPos = new Vector2(-150f, -640f); // 오른쪽 위 기준: 의뢰 창·사건 기록 아래, 파티 HUD 위
        private const int Layer = 31;            // 주사위 전용 층
        private static readonly Vector3 Stage = new Vector3(0f, -300f, 0f);
        private const float ScreenSize = 400f;   // 그림 틀 크기 (1920×1080 기준) — 주사위는 그 안 약 110px

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
            _cam.fieldOfView = 50f;
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
            _rt = img.rectTransform;
            _group = img.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _rt.sizeDelta = new Vector2(ScreenSize, ScreenSize);

            // 오른쪽에 옮겨 둔 뒤 아래에 "이동 N" (피의 사냥 −2 등으로 눈과 이동력이 다를 수 있어서)
            _label = Fantasia.UI.UGUIKit.CreateText(_rt, "Moves", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "", 38, TextAnchor.MiddleCenter);
            _label.rectTransform.sizeDelta = new Vector2(260f, 44f);
            _label.rectTransform.anchoredPosition = new Vector2(0f, -88f);
            _label.color = new Color(0.98f, 0.93f, 0.8f, 0f);
            var outline = _label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
            _view = canvasGo;
            PlaceAt(0f);
            Show(false);
        }

        private RectTransform _rt;
        private CanvasGroup _group;
        private Text _label;
        private static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.62f);

        // k 0 = 가운데(말 위) · 1 = 오른쪽에 옮겨 둠
        private void PlaceAt(float k)
        {
            var a = Vector2.Lerp(CenterAnchor, Vector2.one, k);
            _rt.anchorMin = _rt.anchorMax = a;
            _rt.anchoredPosition = Vector2.Lerp(Vector2.zero, DockPos, k);
            _rt.localScale = Vector3.one * Mathf.Lerp(1f, DockScale, k);
            var c = _label.color; c.a = Mathf.Clamp01(k * 2f - 1f); _label.color = c;
            DevDock = k;
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

        // 점검용
        public float DevDock { get; private set; }
        public bool DevVisible => _view != null && _view.activeSelf;
        public float DevMaxStepDeg { get; private set; }   // 굴리는 동안 한 프레임 최대 회전
        public bool DevOutOfFrame { get; private set; }    // 굴리는 동안 주사위 중심이 그림 틀 가장자리(주사위 반 크기)를 넘은 적
        public string DevLabel => _label != null ? _label.text : "";
        public Vector2 DevScreenCenter => _rt != null ? (Vector2)RectTransformUtility.WorldToScreenPoint(null, _rt.position) : Vector2.zero;
        public float DevScreenSize => _rt != null ? _rt.rect.width * _rt.lossyScale.x : 0f;

        // 굴리기 → 다 멈추면 onLanded (칸 표시는 바로) → HoldSeconds 뒤 오른쪽으로. 모델이 없으면 바로 onLanded.
        public void Roll(int value, System.Action onLanded, string label = null)
        {
            if (_play != null) StopCoroutine(_play);
            if (_die == null) { onLanded?.Invoke(); return; }
            _play = StartCoroutine(RollRoutine(value, onLanded, label ?? ""));
        }

        // 칸을 골라 움직이기 시작하면: 아직 가운데면 바로 오른쪽으로 (말을 가리지 않게)
        public void Dock()
        {
            if (_die == null || !_view.activeSelf || Rolling || DevDock >= 1f) return;
            if (_play != null) StopCoroutine(_play);
            _play = StartCoroutine(DockRoutine(DevDock));
        }

        public void Hide()
        {
            if (_play != null) { StopCoroutine(_play); _play = null; }
            Rolling = false;
            Show(false);
        }

        private IEnumerator RollRoutine(int value, System.Action onLanded, string label)
        {
            Rolling = true;
            DevMaxStepDeg = 0f; DevOutOfFrame = false;
            _label.text = label;
            PlaceAt(0f);
            Vector3 rest = Stage + Vector3.up * 0.5f; // 한 변 1m → 중심이 0.5 위
            Quaternion target = RestRotation(value);
            // 옆에서 던져 들어와 구르며 멈춤: 들어오는 쪽(좌/우 무작위)과 굴러가는 축이 맞게
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 from = new Vector3(side * Random.Range(0.55f, 0.75f), 0f, Random.Range(0.1f, 0.35f));
            Vector3 travel = -from;
            Vector3 axis = Vector3.Cross(Vector3.up, travel.normalized); // 굴러가는 방향의 회전축
            axis = Quaternion.AngleAxis(Random.Range(-25f, 25f), Vector3.up) * axis;
            float turns = Random.Range(360f, 450f); // 전체 회전 (도) — 처음 최대 약 940°/초 = 60fps에서 한 프레임 16°
            Show(true);
            _group.alpha = 0f;
            Quaternion prev = Quaternion.identity; bool hasPrev = false;

            for (float t = 0f; t < Duration; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / Duration);
                // 높이: 0~0.38 떨어짐 → 0.38~0.66 한 번 튐(0.28) → 0.66~0.84 작게 튐(0.07) → 바닥
                float y = u < 0.38f ? DropHeight * (1f - (u / 0.38f) * (u / 0.38f))
                        : u < 0.66f ? 0.28f * Mathf.Sin(Mathf.PI * (u - 0.38f) / 0.28f)
                        : u < 0.84f ? 0.07f * Mathf.Sin(Mathf.PI * (u - 0.66f) / 0.18f)
                        : 0f;
                float ease = 1f - (1f - u) * (1f - u); // 빠르게 시작 → 천천히 멈춤
                _die.transform.position = rest + Vector3.up * y + from * (1f - ease);
                // 남은 회전이 0으로 줄어 결과 면에서 정확히 멈춤 (섞기 없음 → 끊김·되돌아감 없음)
                float remain = turns * (1f - u) * (1f - u) * (1f - u * 0.4f);
                var rot = Quaternion.AngleAxis(-remain, axis) * target;
                _die.transform.rotation = rot;
                _group.alpha = Mathf.Clamp01(t / 0.12f); // 처음 0.12초 살짝 나타남
                if (hasPrev && Time.deltaTime > 0f && Time.deltaTime < 0.1f) DevMaxStepDeg = Mathf.Max(DevMaxStepDeg, Quaternion.Angle(prev, rot) / Time.deltaTime / 60f); // 60fps 한 프레임 기준 (스크린샷 저장 프레임 제외)
                prev = rot; hasPrev = true;
                var vp = _cam.WorldToViewportPoint(_die.transform.position);
                if (vp.x < 0.15f || vp.x > 0.85f || vp.y < 0.15f || vp.y > 0.85f) DevOutOfFrame = true;
                yield return null;
            }
            _die.transform.SetPositionAndRotation(rest, target);
            _group.alpha = 1f;
            Rolling = false;
            onLanded?.Invoke();
            // 결과 눈을 잠깐 보여 준 뒤 오른쪽으로
            for (float t = 0f; t < HoldSeconds; t += Time.deltaTime) yield return null;
            yield return DockRoutine(0f);
        }

        private IEnumerator DockRoutine(float startK)
        {
            for (float t = startK * DockSeconds; t < DockSeconds; t += Time.deltaTime)
            {
                float k = t / DockSeconds;
                PlaceAt(k * k * (3f - 2f * k));
                yield return null;
            }
            PlaceAt(1f);
            // 옮긴 뒤엔 주사위가 안 움직이므로 한 장만 찍어 두고 무대 카메라를 끔 (프레임 절약)
            _cam.Render();
            _cam.enabled = false;
            _play = null;
        }

        private void OnDestroy()
        {
            if (_cam != null && _cam.targetTexture != null) _cam.targetTexture.Release();
        }
    }
}
