using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/11 받은 아군 동작(N1~N5 + 추스르기)을 디자인 컨트롤러에 잇는다. 여러 번 돌려도 같은 결과.
    // 동작마다: 같은 이름의 신호(Trigger) + 상태. 어느 상태에서든 신호 → 그 동작 → 끝나면 Idle.
    // 부활(Revive)은 쓰러짐(Death) 끝 자세에서 시작하는 동작이라 같은 방식으로 Death → Revive → Idle.
    // 클립이 없는 캐릭터(적 등)는 건드리지 않음. CharacterMotion이 신호가 있는지 보고 부름.
    public static class MotionSetup
    {
        private const string Root = "Assets/_Project/Art/Models/Characters/Animations";
        // 11/12 N6~N8 추가: 보스 예고(Announce)·금지된 봉합(SelfHeal) · 회피(Dodge) · 승리(Victory, 끝 자세에서 멈춤 → Idle로 안 돌아감)
        public static readonly string[] Motions = { "Defend", "Skill", "Item", "Heal", "Recover", "Revive", "Announce", "SelfHeal", "Dodge", "Victory" };

        [MenuItem("Fantasia/동작 연결 (MotionSetup)")]
        public static void Run()
        {
            int added = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ctrl == null) continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                string dir = System.IO.Path.GetDirectoryName(path).Replace(System.IO.Path.DirectorySeparatorChar, (char)47);
                var sm = ctrl.layers[0].stateMachine;
                var idle = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle");
                if (idle == null) continue;
                var line = new System.Collections.Generic.List<string>();
                int row = 0;
                foreach (var motion in Motions)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{dir}/{name}@{motion}.anim");
                    if (clip == null) continue;
                    row++;
                    if (!ctrl.parameters.Any(p => p.name == motion)) ctrl.AddParameter(motion, AnimatorControllerParameterType.Trigger);
                    var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == motion);
                    if (state == null)
                    {
                        state = sm.AddState(motion, new Vector3(600f, row * 70f, 0f));
                        added++;
                    }
                    state.motion = clip;
                    foreach (var t in sm.anyStateTransitions.Where(t => t.destinationState == state).ToArray()) sm.RemoveAnyStateTransition(t);
                    foreach (var t in state.transitions.ToArray()) state.RemoveTransition(t);

                    var into = sm.AddAnyStateTransition(state);
                    into.AddCondition(AnimatorConditionMode.If, 0f, motion);
                    into.hasExitTime = false;
                    into.duration = motion == "Revive" ? 0.05f : 0.1f; // 부활은 누운 자세에서 바로 이어짐
                    into.canTransitionToSelf = false;

                    if (motion != "Victory") // 승리는 끝 자세 그대로 (디자인: "끝에서 멈춤")
                    {
                        var back = state.AddTransition(idle);
                        back.hasExitTime = true;
                        back.exitTime = 0.95f;
                        back.duration = 0.15f;
                    }
                    line.Add($"{motion} {clip.length:0.00}초");
                }
                if (line.Count > 0)
                {
                    EditorUtility.SetDirty(ctrl);
                    Debug.Log($"[MotionSetup] {name}: {string.Join(" · ", line)}");
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[MotionSetup] 완료 · 새 상태 {added}개");
        }
    }
}
