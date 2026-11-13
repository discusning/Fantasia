using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/13 이펙트 에셋 점검: ThirdParty 아래 모든 프리팹의 머티리얼·셰이더·빠진 스크립트 확인.
    // 분홍(셰이더 깨짐)·빈 머티리얼·빠진 스크립트가 있으면 목록으로. 배치: -executeMethod Fantasia.Editor.VfxAssetCheck.Run
    public static class VfxAssetCheck
    {
        public static void Run()
        {
            var roots = new[] { "Assets/ThirdParty/Hovl Studio", "Assets/ThirdParty/JMO Assets", "Assets/ThirdParty/Travis Game Assets", "Assets/ThirdParty/GabrielAguiarProductions" };
            int prefabs = 0, systems = 0, bad = 0;
            var problems = new List<string>();
            foreach (var root in roots)
            {
                int okHere = 0, badHere = 0;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    prefabs++;
                    var issues = new List<string>();
                    if (go == null) { issues.Add("열리지 않음"); }
                    else
                    {
                        int missing = go.GetComponentsInChildren<Component>(true).Count(c => c == null);
                        if (missing > 0) issues.Add($"빠진 스크립트 {missing}");
                        systems += go.GetComponentsInChildren<ParticleSystem>(true).Length;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        {
                            var mats = r.sharedMaterials;
                            var ps = r.GetComponent<ParticleSystem>();
                            for (int k = 0; k < mats.Length; k++)
                            {
                                var m = mats[k];
                                // 파티클 둘째 칸은 꼬리(trail) 전용 — 꼬리를 안 쓰면 비어 있는 게 정상
                                bool needed = r.enabled && (!(r is ParticleSystemRenderer) || (k == 0 ? ((ParticleSystemRenderer)r).renderMode != ParticleSystemRenderMode.None : ps != null && ps.trails.enabled));
                                if (m == null) { if (needed) issues.Add($"{r.name}: 머티리얼 없음"); continue; }
                                if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader") issues.Add($"{r.name}: 셰이더 깨짐 ({m.name})");
                                else if (!m.shader.isSupported) issues.Add($"{r.name}: 셰이더 지원 안 됨 {m.shader.name}");
                            }
                        }
                    }
                    if (issues.Count > 0) { badHere++; bad++; problems.Add($"  {path}: {string.Join(" · ", issues.Distinct())}"); }
                    else okHere++;
                }
                Debug.Log($"[VfxCheck] {root}: 정상 {okHere} · 문제 {badHere}");
            }
            var shaders = AssetDatabase.FindAssets("t:Shader", new[] { "Assets/ThirdParty" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null && (!s.isSupported || ShaderUtil.ShaderHasError(s))).Select(s => s.name).ToList();
            Debug.Log($"[VfxCheck] 프리팹 {prefabs} · 파티클 {systems} · 문제 프리팹 {bad} · 에러 셰이더 {shaders.Count} {string.Join(", ", shaders)}");
            foreach (var p in problems) Debug.Log("[VfxCheck]" + p);
        }
    }
}
