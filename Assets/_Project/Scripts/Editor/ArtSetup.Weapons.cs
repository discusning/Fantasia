using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/13 무기 소품 8종 (팀원 WhiteMango 11/12 푸시, 모션 요청 W1) — 캐릭터 손뼈에 붙임.
    // 받은 무기는 미터 단위, 기준점 = 손잡이. 손뼈 배율을 되돌려 실제 크기 그대로.
    // 방향: 모델마다 손뼈 축이 달라서, 서 있는 자세(Idle)를 한 번 재생해 그때 손 방향을 읽고
    //       "그 자세에서 무기가 이 방향을 보게" 회전을 계산함 (모델 정면 +Z, 오른쪽 +X, 위 +Y).
    // 아군은 직업 시작 무기를 켜 두고, 직업이 쓸 수 있는 다른 종류는 꺼 둔 채 붙임 → 장착 무기 종류로 바꿔 보임 (UI.HeldWeapon).
    // 녹슨 단검(RustDagger)은 아직 쓸 곳 없음 (적 소품 후보).
    public static partial class ArtSetup
    {
        private const string WeaponRoot = "Assets/_Project/Art/Models/Weapons";

        private enum Axis { X, Y, Z }
        private static readonly Vector3 Fwd = Vector3.forward, Up = Vector3.up, Right = Vector3.right;
        // 무기 로컬 축 along이 alongDir을, second 축이 secondDir을 보게 (서 있는 자세 기준)
        private static readonly Dictionary<string, (Axis along, Vector3 alongDir, Axis second, Vector3 secondDir)> WeaponPose = new()
        {
            ["Dagger"] = (Axis.Z, (Fwd - Up * 0.35f).normalized, Axis.X, Up),        // 칼날 앞(살짝 아래), 날 폭은 세로
            ["Scalpel"] = (Axis.Z, (Fwd - Up * 0.35f).normalized, Axis.X, Up),
            ["RustDagger"] = (Axis.Z, (Fwd - Up * 0.35f).normalized, Axis.X, Up),
            ["Staff"] = (Axis.Z, Up, Axis.Y, Fwd),                                    // 지팡이 머리가 위, 아래로 바닥 쪽
            ["Pistol"] = (Axis.Z, Fwd, Axis.Y, Up),                                   // 총구 앞, 손잡이 아래
            ["Musket"] = (Axis.X, Fwd, Axis.Y, Up),
            ["MercuryFlask"] = (Axis.Y, Up, Axis.Z, Fwd),                             // 병 똑바로
            ["Grimoire"] = (Axis.Y, Up, Axis.Z, Right),                               // 책을 세워 옆구리에
        };

        // 무기 이름, 오른손?, 처음에 켤지
        private static readonly Dictionary<string, (string weapon, bool right, bool on)[]> Weapons = new()
        {
            ["Model_Detective"] = new[] { ("Dagger", true, true) },                       // 명탐정 허용: 단검·검·대검 (검·대검 소품 없음 → 빈손)
            ["Model_PlagueDoctor"] = new[] { ("Staff", true, true), ("Grimoire", true, false) },
            ["Model_Sniper"] = new[] { ("Pistol", true, true), ("Musket", true, false) },
            ["Model_AbelMoreau"] = new[] { ("Scalpel", true, true), ("MercuryFlask", false, true) },
        };

        private static void AttachWeapons(GameObject model, string name, List<string> report)
        {
            if (!Weapons.TryGetValue(name, out var list)) return;
            var animator = model.GetComponent<Animator>();
            var avatar = animator != null ? animator.avatar : null;
            if (avatar == null || !avatar.isHuman) { report.Add("  무기: 사람형 뼈대 아님"); return; }
            Transform Bone(string human)
            {
                string bone = avatar.humanDescription.human.FirstOrDefault(h => h.humanName == human).boneName;
                return string.IsNullOrEmpty(bone) ? null : model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == bone);
            }
            var rightHand = Bone("RightHand");
            var leftHand = Bone("LeftHand");
            var rightMid = Bone("Right Middle Proximal");
            var leftMid = Bone("Left Middle Proximal");

            // 서 있는 자세에서 손 회전·쥐는 자리 읽기
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Root}/Characters/Animations/{name}/{name}@Idle.anim");
            var root = model.transform.parent != null ? model.transform.parent : model.transform;
            Quaternion rq = Quaternion.identity, lq = Quaternion.identity;
            Vector3 rGrip = Vector3.zero, lGrip = Vector3.zero;
            if (idle != null)
            {
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(model, idle, Mathf.Min(1f, idle.length * 0.5f));
                AnimationMode.EndSampling();
            }
            Quaternion inv = Quaternion.Inverse(root.rotation);
            if (rightHand != null) { rq = inv * rightHand.rotation; rGrip = GripLocal(rightHand, rightMid); }
            if (leftHand != null) { lq = inv * leftHand.rotation; lGrip = GripLocal(leftHand, leftMid); }
            if (idle != null) AnimationMode.StopAnimationMode();

            foreach (var (weapon, right, on) in list)
            {
                var hand = right ? rightHand : leftHand;
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{WeaponRoot}/Prop_Weapon_{weapon}/Prop_Weapon_{weapon}.fbx");
                if (hand == null || src == null) { report.Add($"  무기 {weapon}: 못 붙임 (손 {(hand != null)} · 파일 {(src != null)})"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                go.name = "Weapon_" + weapon;
                go.transform.SetParent(hand, false);
                go.transform.localScale = Vector3.one / Mathf.Max(0.0001f, hand.lossyScale.x); // 손뼈 배율을 되돌려 받은 크기(m) 그대로
                go.transform.localPosition = right ? rGrip : lGrip;
                var want = WeaponPose.TryGetValue(weapon, out var p) ? Desired(p.along, p.alongDir, p.second, p.secondDir) : Quaternion.identity;
                go.transform.localRotation = Quaternion.Inverse(right ? rq : lq) * want;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                go.SetActive(on);
                report.Add($"  무기 {weapon}: {hand.name}에 붙임{(on ? "" : " (꺼 둠)")}");
            }
        }

        // 쥐는 자리: 손목(손뼈)과 가운뎃손가락 뿌리 사이 70% (손뼈 로컬 좌표)
        private static Vector3 GripLocal(Transform hand, Transform mid)
            => mid == null ? Vector3.zero : hand.InverseTransformPoint(Vector3.Lerp(hand.position, mid.position, 0.7f));

        // 무기의 along 축 → alongDir, second 축 → secondDir 이 되는 회전 (모델 기준)
        private static Quaternion Desired(Axis along, Vector3 alongDir, Axis second, Vector3 secondDir)
        {
            Vector3 A(Axis a) => a == Axis.X ? Vector3.right : a == Axis.Y ? Vector3.up : Vector3.forward;
            var toZY = Quaternion.Inverse(Quaternion.LookRotation(A(along), A(second))); // along → Z, second → Y
            return Quaternion.LookRotation(alongDir, secondDir) * toZY;                   // Z → alongDir, Y → secondDir
        }
    }
}
