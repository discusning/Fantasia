using UnityEngine;

namespace Fantasia.Core
{
    [DefaultExecutionOrder(32000)]
    public class FrameSplitEnd : MonoBehaviour
    {
        public static double LastScriptMs;
        private void LateUpdate()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            LastScriptMs = (now - FrameSplitStart.UpdateStart) * 1000.0;
            FrameSplitStart.MarkLateEnd(now);
        }
    }
}
