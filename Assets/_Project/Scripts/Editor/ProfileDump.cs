using System.IO;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/15 멈춤 찾기: 개발용 빌드가 남긴 프로파일러 기록(.raw)에서 120ms 넘는 프레임마다 시간을 많이 쓴 항목을 나무 모양으로 뽑음.
    // 환경변수 PROF_RAW = 기록 파일, PROF_OUT = 결과 텍스트.
    public static class ProfileDump
    {
        public static void Run()
        {
            string raw = System.Environment.GetEnvironmentVariable("PROF_RAW"), outPath = System.Environment.GetEnvironmentVariable("PROF_OUT");
            var sb = new StringBuilder();
            if (!ProfilerDriver.LoadProfile(raw, false)) { File.WriteAllText(outPath, "불러오기 실패 " + raw); return; }
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex, n = 0;
            sb.AppendLine($"프레임 {first}~{last}");
            for (int f = first; f <= last && n < 40; f++)
            {
                using var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false);
                if (view == null || !view.valid || view.frameTimeMs < 120f) continue;
                n++;
                sb.AppendLine($"== 프레임 {f} · {view.frameTimeMs:0}ms");
                Walk(view, view.GetRootItemID(), 0, sb);
            }
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[ProfileDump] " + n + "개 프레임");
        }

        private static readonly System.Collections.Generic.List<int> Kids = new();
        private static void Walk(HierarchyFrameDataView v, int id, int depth, StringBuilder sb)
        {
            if (depth > 12) return;
            var children = new System.Collections.Generic.List<int>();
            v.GetItemChildren(id, children);
            foreach (var c in children)
            {
                float ms = v.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
                if (ms < 15f) continue;
                sb.AppendLine($"{new string(' ', depth * 2)}{v.GetItemName(c)} {ms:0}ms");
                Walk(v, c, depth + 1, sb);
            }
        }
    }
}
