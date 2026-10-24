using UnityEngine;

namespace Fantasia.Board
{
    // 보드 카메라 (D07 1차): 파티 토큰을 부드럽게 따라가고, WASD·방향키로 둘러보기, 휠로 줌,
    // 가운데 버튼 드래그로 이동. 토큰이 움직이기 시작하면 둘러보던 위치는 토큰으로 돌아온다.
    // 각도는 씬 카메라의 기울기(BoardTestSceneSetup: 38°)를 그대로 쓴다.
    public class BoardCameraRig : MonoBehaviour
    {
        public Transform Target;
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
            rig.SnapToTarget();
            return rig;
        }

        public void SnapToTarget()
        {
            if (Target == null) return;
            _lookOffset = Vector3.zero;
            _focus = Target.position;
            _lastTargetPos = Target.position;
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
            _focus = Vector3.Lerp(_focus, desired, 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            Apply();
        }

        private void Apply() => transform.position = _focus - transform.forward * Distance;
    }
}
