using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/14 ThirdParty 정리: 게임(_Project 에셋·빌드 씬)과 이펙트 만드는 편집기 도구(VfxSetup·CampTentSetup)가 쓰는 파일만 남기고,
    // 나머지(데모 씬·데모 스크립트·안 쓰는 이펙트·다른 렌더 파이프라인 unitypackage)를 찾는다.
    // Audit = 목록만 (tmp 파일), Delete = 그 목록을 지움. 지운 것은 git 기록으로 되살릴 수 있음.
    public static class ThirdPartyAudit
    {
        private const string Root = "Assets/ThirdParty";

        public static void Audit() => Run(delete: false);
        public static void Delete() => Run(delete: true);

        private static void Run(bool delete)
        {
            var roots = new List<string>();
            roots.AddRange(AssetDatabase.FindAssets("", new[] { "Assets/_Project" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !AssetDatabase.IsValidFolder(p)));
            roots.AddRange(EditorBuildSettings.scenes.Select(s => s.path));

            // 편집기 도구가 이름으로 집어 쓰는 원본 프리팹 (다시 만들 때 필요)
            var toolText = string.Join("\n", new[] { "VfxSetup.cs", "CampTentSetup.cs" }
                .Select(f => Directory.GetFiles("Assets/_Project/Scripts/Editor", f, SearchOption.AllDirectories).FirstOrDefault())
                .Where(f => f != null).Select(File.ReadAllText));
            var all = AssetDatabase.FindAssets("", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !AssetDatabase.IsValidFolder(p)).Distinct().ToList();
            foreach (var p in all.Where(p => p.EndsWith(".prefab")))
                if (toolText.Contains(Path.GetFileNameWithoutExtension(p) + "\"") || toolText.Contains(Path.GetFileName(p))) roots.Add(p);

            var used = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
            // 쓰는 셰이더가 #include 하는 cginc 등은 의존성에 안 잡힘 → 셰이더가 쓰이는 폴더의 cginc/hlsl 은 남김
            var shaderDirs = new HashSet<string>(used.Where(p => p.StartsWith(Root) && p.EndsWith(".shader")).Select(Path.GetDirectoryName));
            var keepExt = new[] { ".cginc", ".hlsl" };

            var unused = all.Where(p => !used.Contains(p))
                .Where(p => !(keepExt.Contains(Path.GetExtension(p)) && shaderDirs.Contains(Path.GetDirectoryName(p))))
                .ToList();
            long bytes = unused.Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0);
            long total = all.Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0);
            var lines = new List<string> { $"[ThirdPartyAudit] 전체 {all.Count}개 {total / 1048576f:0.0}MB · 씀 {all.Count - unused.Count}개 · 안 씀 {unused.Count}개 {bytes / 1048576f:0.0}MB" };
            lines.AddRange(unused.OrderBy(p => p));
            var outPath = System.Environment.GetEnvironmentVariable("TPA_OUT");
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllLines(outPath, lines);
            Debug.Log(lines[0]);
            foreach (var g in unused.GroupBy(p => string.Join("/", p.Split('/').Take(4))).OrderByDescending(g => g.Count()))
                Debug.Log($"[ThirdPartyAudit]   {g.Key}: {g.Count()}개");

            if (!delete) return;
            var failed = new List<string>();
            AssetDatabase.DeleteAssets(unused.ToArray(), failed);
            // 비어 버린 폴더 지움
            foreach (var dir in Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                if (!Directory.EnumerateFileSystemEntries(dir).Any(e => !e.EndsWith(".meta"))) AssetDatabase.DeleteAsset(dir.Replace('\\', '/'));
            AssetDatabase.Refresh();
            Debug.Log($"[ThirdPartyAudit] 지움 {unused.Count - failed.Count}개 · 실패 {failed.Count}");
        }
    }
}
