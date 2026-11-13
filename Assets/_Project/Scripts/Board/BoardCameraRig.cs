using UnityEngine;

namespace Fantasia.Board
{
    // 보드 카메라 (D07 1차): 파티 토큰을 부드럽게 따라가고, WASD·방향키로 둘러보기, 휠로 줌,
    // 가운데 버튼 드래그로 이동. 토큰이 움직이기 시작하면 둘러보던 위치는 토큰으로 돌아온다.
    // 각도는 씬 카메라의 기울기(BoardTestSceneSetup: 38°)를 그대로 쓴다.
    public class BoardCameraRig : MonoBehaviour
    {
        public Transform Target;
        private HexBoard _board; private bool _boardLooked;
        private HexBoard Board { get { if (!_boardLooked) { _board = FindFirstObjectByType<HexBoard>(); _boardLooked = _board != null; } return _board; } } // 11/14 안개 범위
        public const float Pitch = 52f;
        public float Distance = 16f;
        public float MinDistance = 6f, MaxDistance = 28f;
        public float PanSpeed = 14f;
        public float FollowSharpness = 6f;

        private Vector3 _lookOffset;   // 둘러보기로 옮긴 만큼 (토큰 기준)
        private Vector3 _lastTargetPos;
        private Vector3 _focus;

        public static BoardCameraRig Attach(Camera cam, Transform target)
        {
            var rig = cam.GetComponent<BoardCameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<BoardCameraRig>();
            cam.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f); // 큰 맵이라 더 내려다보게
            rig.Target = target;
            var fade = cam.GetComponent<BoardOccluderFade>();
            if (fade == null) fade = cam.gameObject.AddComponent<BoardOccluderFade>(); // 11/12 말을 가리는 구조물 반투명
            fade.Target = target;
            rig.SnapToTarget();
            return rig;
        }

        public void SnapToTarget()
        {
            if (Target == null) return;
            _lookOffset = Vector3.zero;
            _focus = Target.position;
            _lastTargetPos = Target.position;
            _pvPrev = Target.position;
            Apply();
        }

        private void LateUpdate()
        {
            if (Target == null) return;

            // 토큰이 움직이면 둘러보기 해제
            if ((Target.position - _lastTargetPos).sqrMagnitude > 0.0001f) _lookOffset = Vector3.Lerp(_lookOffset, Vector3.zero, 1f - Mathf.Exp(-8f * Time.deltaTime));
            _lastTargetPos = Target.position;

            if (!Fantasia.UI.InventoryPanel.IsOpen)
            {
                var flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                var flatRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
                var move = Vector3.zero;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += flatForward;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move -= flatForward;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += flatRight;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= flatRight;
                _lookOffset += move * (PanSpeed * Time.deltaTime * Distance / 13f);

                if (Input.GetMouseButton(2))
                {
                    float k = 0.04f * Distance / 13f;
                    _lookOffset -= flatRight * (Input.GetAxis("Mouse X") * k * 10f) + flatForward * (Input.GetAxis("Mouse Y") * k * 10f);
                }
                if (Input.GetKeyDown(KeyCode.F)) _lookOffset = Vector3.zero; // 파티로 돌아가기

                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f) Distance = Mathf.Clamp(Distance - scroll * 1.5f, MinDistance, MaxDistance);
            }

            var desired = Target.position + _lookOffset;
            // 11/14 팀장 1안: 둘러보기는 열린 지역(안개 안쪽)까지만 — 안개 너머 빈 곳을 비추지 않게
            if (Board != null && Board.HasVeil)
            {
                var vb = Board.VisibleBounds; float m = 1.5f;
                var clamped = new Vector3(Mathf.Clamp(desired.x, vb.min.x - m, vb.max.x + m), desired.y, Mathf.Clamp(desired.z, vb.min.z - m, vb.max.z + m));
                _lookOffset += clamped - desired; desired = clamped;
            }
            _focus = Vector3.Lerp(_focus, desired, 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            if (Preview > 0) PreviewPose();
            Apply();
        }

        private void Apply() => transform.position = _focus - transform.forward * (Preview > 0 ? _pvDist : Distance);

        // ── 11/12 팀장 결정: 이동 시점 A+B (Docs/Planning/플레이어시점_연구_1112.md 6절) ──
        // A: 낮추고 다가감 (52° 16m → 40° 11m, 화각 60 → 50) · B: 이동하는 쪽으로 카메라가 천천히 돌아 파티 뒤에서 봄 (초당 최대 70°)
        // C(이동 중 확대)는 프레임·구조물 깨짐 걱정으로 안 함. 비교용 -boardcam 0 = 예전 시점, 1 = A만.
        public static readonly int Preview = ReadPreview();
        private static int ReadPreview()
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-boardcam");
            return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out var n) ? Mathf.Clamp(n, 0, 2) : 2;
        }
        // 11/13 팀장 지적 "시점이 확확 바뀜": 예전엔 칸 하나하나의 방향을 따라 돌아서, 육각 길이 지그재그면 카메라도 좌우로 흔들림.
        // → 이동을 시작할 때 "출발 → 도착" 전체 방향 하나만 정하고, 그쪽으로 천천히 한 번만 돎 (가속·감속, 초당 최대 58°).
        //   방향 차이가 작으면(25° 미만) 돌지 않음 → 짧은 이동마다 화면이 움찔하지 않게.
        public const float Pitch2 = 40f, DistanceScale = 0.7f, Fov = 50f, MaxYawSpeed = 58f, YawSmoothTime = 0.55f, MinTurn = 25f; // 11/13 팀장: 45°·0.9초는 너무 느림 → 예전(70°)과 중간
        private float _pvDist = 11f, _pvYaw, _pvHeading, _pvYawVel;
        private Vector3 _pvPrev;

        // 이동 시작 때 BoardTestController가 부름 (출발·도착 위치)
        public void SetTravel(Vector3 from, Vector3 to)
        {
            var d = to - from; d.y = 0f;
            if (d.sqrMagnitude < 0.25f) return; // 반 칸도 안 되면 그대로
            float heading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(_pvHeading, heading)) >= MinTurn) _pvHeading = heading;
        }

        private void PreviewPose()
        {
            _pvPrev = Target.position;
            if (Preview >= 2) _pvYaw = Mathf.SmoothDampAngle(_pvYaw, _pvHeading, ref _pvYawVel, YawSmoothTime, MaxYawSpeed);
            _pvDist = Distance * DistanceScale;
            transform.rotation = Quaternion.Euler(Pitch2, _pvYaw, 0f);
            var cam = GetComponent<Camera>();
            if (cam != null) cam.fieldOfView = Fov;
        }
    }
}
