using UnityEngine;

namespace Fantasia.Core
{
    // 11/15 멈춤 원인 나누기: 한 프레임을 [스크립트 Update 시작 ~ LateUpdate 끝] 과 나머지(그리기·물리·애니메이션 등)로 나눔
    [DefaultExecutionOrder(-32000)]
    public class FrameSplitStart : MonoBehaviour
    {
        public static double UpdateStart, PrevScriptMs, PrevOtherMs;
        private static double _lastLateEnd;
        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            PrevOtherMs = _lastLateEnd > 0 ? (now - _lastLateEnd) * 1000.0 : 0; // 지난 LateUpdate 끝 → 이번 Update 시작 (그리기·물리·애니메이션)
            PrevScriptMs = FrameSplitEnd.LastScriptMs;
            UpdateStart = now;
        }
        public static void MarkLateEnd(double t) => _lastLateEnd = t;
    }

}
