using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.UI
{
    // 11/14 팀장 A안: 디자인이 넣은 손가락 본(엄지·검지·중지 묶음 2마디, origin/3D a9c2b17)을 개발이 굽혀서 "쥔 손"을 만듦.
    //  - 손가락 본은 사람형 연결표에 없어서 동작 클립이 건드리지 않음 → 매 프레임 기본 자세로 되돌린 뒤 각도만큼 굽힘
    //  - 굽히는 축은 본 축을 짐작하지 않고 그 순간 손 모양으로 계산: 손바닥 방향 = 손가락 방향 × 엄지 쪽 (왼손은 반대)
    //    → 손가락 끝이 손바닥 쪽으로 감기는 방향 (모델·동작이 달라도 같은 규칙)
    //  - 손 모양 = 쥔 물건(무기 종류) × 지금 동작(서 있기·공격·스킬·맞음·방어·아이템·회복·쓰러짐…)
    //  - 빈손도 굳은 판자처럼 펴지지 않게 살짝 굽힘. 동작이 바뀔 때는 부드럽게 넘어감
    public class HandGrip : MonoBehaviour
    {
        public enum Hold { None, Fist, Trigger, Rod, Bottle, Book }

        // 손 모양: 엄지·검지·중지묶음 각각 (첫 마디, 둘째 마디) 굽힘 각도(도). 음수 = 살짝 젖힘(펼침)
        public struct Shape
        {
            public Vector2 Thumb, Index, Middle;
            public Shape(Vector2 t, Vector2 i, Vector2 m) { Thumb = t; Index = i; Middle = m; }
            public static Shape Lerp(Shape a, Shape b, float k) => new(Vector2.Lerp(a.Thumb, b.Thumb, k), Vector2.Lerp(a.Index, b.Index, k), Vector2.Lerp(a.Middle, b.Middle, k));
            public Shape Scaled(float k) => new(Thumb * k, Index * k, Middle * k);
        }

        // 쥔 물건별 기본 모양 (서 있을 때)
        public static readonly Dictionary<Hold, Shape> Held = new()
        {
            [Hold.Fist] = new(new(38f, 32f), new(78f, 82f), new(84f, 88f)),     // 단검·수술칼: 주먹 꽉
            [Hold.Trigger] = new(new(34f, 26f), new(6f, 4f), new(84f, 88f)),     // 권총·장총: 검지는 방아쇠 밖, 총 몸통 옆에 곧게 (11/14 사격 교본: 쏠 때만 방아쇠에)
            [Hold.Rod] = new(new(30f, 22f), new(62f, 58f), new(66f, 62f)),      // 지팡이·낫 자루: 굵은 막대를 감쌈
            [Hold.Bottle] = new(new(26f, 18f), new(52f, 46f), new(56f, 50f)),   // 수은 병: 둥글게 감쌈
            [Hold.Book] = new(new(8f, 4f), new(30f, 26f), new(34f, 30f)),       // 마법서: 책을 받쳐 듦 (엄지는 책 위)
        };
        public static readonly Shape Relaxed = new(new(10f, 8f), new(14f, 18f), new(22f, 26f)); // 빈손 쉼: 자연스럽게 살짝 굽음
        public static readonly Shape Open = new(new(-4f, 0f), new(-6f, 0f), new(-4f, 0f));      // 펼친 손 (놀람·내미는 손바닥)
        public static readonly Shape Brace = new(new(30f, 24f), new(60f, 66f), new(66f, 70f));  // 힘준 빈손 (방어·버팀)
        public static readonly Shape Point = new(new(32f, 26f), new(-2f, 0f), new(86f, 90f));  // 가리키기: 검지만 펴고 나머지 접음
        public static readonly Shape Temple = new(new(18f, 12f), new(6f, 8f), new(30f, 34f));  // 관자놀이에 댄 손: 검지·중지 끝을 댐
        public static readonly Shape Cup = new(new(24f, 16f), new(46f, 40f), new(50f, 44f));    // 빈손으로 병·약을 쥔 모양 (아이템·회복)

        private class Finger { public Transform A, B; public Quaternion RestA, RestB; public Vector3 LocalDirB; }
        private class Hand
        {
            public bool Right; public Transform Root; public Finger Thumb, Index, Middle;
            public Hold Hold; public Shape Now; public bool HasNow; public float Squeeze;
        }

        private readonly Hand[] _hands = new Hand[2];
        private Animator _anim;
        private float _nextHoldCheck;
        public static bool DevOff;            // 점검용: 끄면 기본 손 그대로
        private bool _seatedOff;
        public static int DevActive;          // 점검용: 켜진 손 수
        public string DevHolds => $"오른손 {(_hands[0] != null ? _hands[0].Hold : Hold.None)} · 왼손 {(_hands[1] != null ? _hands[1].Hold : Hold.None)}";

        // 손가락 본이 있는 모델에만 붙임. 없으면 null (예전 모델·적 짐승)
        public static HandGrip Attach(GameObject model)
        {
            if (model == null) return null;
            var g = model.GetComponentInChildren<HandGrip>(true);
            if (g != null) { g.RefreshHolds(); return g; }
            var anim = model.GetComponentInChildren<Animator>(true);
            var host = anim != null ? anim.gameObject : model;
            Transform F(string n) { foreach (var t in host.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
            if (F("Thumb1.R") == null || F("Index1.R") == null || F("Middle1.R") == null) return null;
            g = host.AddComponent<HandGrip>();
            g._anim = anim;
            g.Build(F);
            Debug.Log($"[HandGrip] {model.name}: {g.DevHolds} · 무기 자리 옮김 누적 {DevSeated} {DevBladeInfo}");
            return g;
        }

        private void Build(System.Func<string, Transform> f)
        {
            for (int h = 0; h < 2; h++)
            {
                string s = h == 0 ? ".R" : ".L";
                Finger Fg(string n)
                {
                    var a = f(n + "1" + s); var b = f(n + "2" + s);
                    if (a == null || b == null) return null;
                    return new Finger { A = a, B = b, RestA = a.localRotation, RestB = b.localRotation, LocalDirB = b.InverseTransformDirection((b.position - a.position).normalized) };
                }
                _hands[h] = new Hand { Right = h == 0, Root = f("Hand" + s), Thumb = Fg("Thumb"), Index = Fg("Index"), Middle = Fg("Middle") };
                if (_hands[h].Root == null || _hands[h].Index == null || _hands[h].Middle == null) _hands[h] = null;
            }
            SeatWeapons(f);
            RefreshHolds();
            DevActive++;
        }

        // 11/14 무기 자리: 지금까지 무기 손잡이는 손목 관절(손뼈 기준점)에 있었음 → 주먹 가운데(디자인 Socket)에서 14~22cm 떨어져 빈 주먹이 됨.
        // 방향(팀장 확인한 모습)은 그대로 두고 손잡이만 주먹 가운데로 옮김. 무기마다 손잡이 어디를 쥐는지 조금 다름 (HandleGrip, 무기 긴 축 방향 m)
        private static readonly Dictionary<string, float> HandleGrip = new()
        {
            ["Weapon_Dagger"] = 0.02f, ["Weapon_Scalpel"] = 0.03f, ["Weapon_RustDagger"] = 0.02f,
            ["Weapon_Pistol"] = 0f, ["Weapon_Musket"] = 0f, ["Weapon_Staff"] = 0f, ["Weapon_Grimoire"] = 0f, ["Weapon_MercuryFlask"] = 0f, ["Scythe"] = 0f, ["Weapon_Scythe"] = 0f,
        };
        public static int DevSeated;
        public static string DevBladeInfo = "";
        private readonly List<(Transform w, Vector3 orig, Vector3 seated, Quaternion origRot, Quaternion seatedRot)> _seats = new();
        private void Seat(bool on)
        {
            foreach (var s in _seats)
                if (s.w != null) { s.w.localPosition = on ? s.seated : s.orig; s.w.localRotation = on ? s.seatedRot : s.origRot; }
        }

        // 손 기준 방향 (손뼈 로컬): F = 손목→가운데 손가락 뿌리(손등 방향), S = 새끼 쪽→검지 쪽(주먹 위쪽, 엄지 쪽)
        private static void HandFrame(Hand h, out Vector3 F, out Vector3 S)
        {
            var r = h.Root;
            F = r.InverseTransformDirection(h.Middle.A.position - r.position).normalized;
            S = r.InverseTransformDirection(h.Index.A.position - h.Middle.A.position);
            S = (S - F * Vector3.Dot(S, F)).normalized;
        }

        // 무기 메시 꼭짓점 (무기 로컬)
        private static List<Vector3> LocalVerts(Transform w)
        {
            var list = new List<Vector3>();
            foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var m = w.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
            }
            return list;
        }

        // 11/14 팀장 2차 "총 쥔 기본 자세가 어색함": 총 방향이 모델 기준(앞·위)으로 고정돼 팔을 내리면 총구가 수평으로 떠 있었음.
        // → 손 기준으로: 손잡이는 주먹 고리를 위→아래로 지나고(총 위쪽 = 엄지·검지 쪽), 총구는 손등 방향(F)에서 15° 위로. 손잡이 아래쪽 25% 꼭짓점 평균을 주먹 가운데에
        // 11/14 팀장 3차 "총 쥔 모습이 아직 어색": 현실 사격 교본·3D 게임 조사 결과로 맞춤
        //  - 내린 자세(로우 레디): 총구는 바로 아래가 아니라 약 45° 앞아래. 게임 권총 대기 동작도 총을 옆에 낮게 들되 총구가 앞아래
        //  - 수발총 손잡이는 총신에서 크게 꺾여 있음 → 주먹이 손잡이를 쥐면 총구는 손등보다 엄지 쪽으로 들림
        //  - 조준할 때(공격·스킬 가리키기)는 손목을 꺾어 총구가 팔과 거의 나란히
        //  → 총 기울기: 내린 자세 IdleTilt, 조준 AimTilt 사이를 동작에 따라 부드럽게 오감 (주먹 가운데 기준으로 돌림)
        private const float IdleTilt = 38f, AimTilt = 12f;
        private static readonly Vector2 TriggerOn = new(40f, 34f); // 방아쇠에 건 검지
        private class GunSeat { public Transform W; public Hand Hand; public Vector3 F, S, Center, Handle; public float Tilt = IdleTilt; }
        private readonly List<GunSeat> _guns = new();

        private void SeatGun(Hand hand, Transform c, Vector3 center)
        {
            HandFrame(hand, out var F, out var S);
            var g = new GunSeat { W = c, Hand = hand, F = F, S = S, Center = hand.Root.InverseTransformPoint(center) };
            PlaceGun(g, AimTilt); // 손잡이 찾기는 총이 손 기준으로 선 상태에서
            var vs = LocalVerts(c);
            if (vs.Count > 0)
            {
                float lo = float.MaxValue, hi = float.MinValue, back = float.MaxValue, front = float.MinValue;
                foreach (var v in vs) { lo = Mathf.Min(lo, v.y); hi = Mathf.Max(hi, v.y); back = Mathf.Min(back, v.z); front = Mathf.Max(front, v.z); }
                // 손잡이 = 아래쪽 30% 이면서 뒤쪽 절반 (총구 밑 장식 제외)
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var v in vs)
                    if (v.y < lo + (hi - lo) * 0.3f && v.z < (back + front) * 0.5f) { sum += v; n++; }
                if (n > 0) g.Handle = sum / n;
                g.Handle.y += (hi - lo) * 0.12f; // 주먹 고리는 손잡이 윗부분(총 몸통 바로 아래)을 감쌈
            }
            if (c.name == "Weapon_Musket") g.Handle = Vector3.zero; // 11/14 디자인 수정본: 기준점 = 방아쇠 뒤 개머리 손목 (쥐는 곳)
            _guns.Add(g);
            PlaceGun(g, IdleTilt);
        }

        // 총구 = 손등 방향(F)에서 엄지 쪽(S)으로 tilt° 들림, 총 위 = 그에 수직. 손잡이 점이 주먹 가운데에 오게
        private static void PlaceGun(GunSeat g, float tilt)
        {
            if (g.W == null) return;
            float a = tilt * Mathf.Deg2Rad;
            var barrel = g.F * Mathf.Cos(a) + g.S * Mathf.Sin(a);
            var up = g.S * Mathf.Cos(a) - g.F * Mathf.Sin(a);
            g.W.localRotation = Quaternion.LookRotation(barrel, up); // 총 +Z = 총구, +Y = 위 (ArtSetup.WeaponPose 기준)
            g.W.localPosition = g.Center - g.W.localRotation * Vector3.Scale(g.W.localScale, g.Handle);
            g.Tilt = tilt;
        }

        // 조준 정도 0~1: 공격(쏘는 동작 내내), 스킬 뒤쪽(앞을 가리키는 때)
        private static float AimOf(string state, float t) => state switch
        {
            "Attack" => Mathf.InverseLerp(0.06f, 0.22f, t) * (1f - Mathf.InverseLerp(0.72f, 0.92f, t)),
            "Skill" => Mathf.InverseLerp(0.48f, 0.6f, t) * (1f - Mathf.InverseLerp(0.88f, 0.98f, t)),
            _ => 0f,
        };

        // 11/14 팀장 2차 "사신이 기본 자세에서 무기를 제대로 안 듦": 자루가 손가락과 나란히 붙어 있었음.
        // → 자루가 주먹 고리(S 축)를 지나되, 팔을 내린 자세에서 서 있게 손목 쪽(-F)으로 40° 기울임. 날은 지금 향하던 쪽 유지, 쥐는 자루 자리도 유지
        private static void SeatShaft(Hand hand, Transform c, Vector3 center)
        {
            HandFrame(hand, out var F, out var S);
            if (c.name == "Weapon_Scythe")
            {
                // 11/14 디자인 새 낫: 축·쥐는 자리를 디자인이 정해 줌 (자루 +Z 날끝 위, 날 +Y, 기준점 = 오른손 자루 띠) → 메시를 읽지 않고 그대로 씀
                DevBladeInfo = "디자인 축 사용";
                OrientShaft(c, F, S, center, Vector3.forward, Vector3.up, Vector3.zero);
                return;
            }
            var la = LongAxis(c); // 무기 로컬 긴 축
            var vs = LocalVerts(c);
            if (vs.Count == 0) return;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in vs) { float d = Vector3.Dot(v, la); lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
            // 날이 있는 끝 = 긴 축에 수직으로 더 넓게 퍼진 끝
            Vector3 mid = Vector3.zero, endHi = Vector3.zero, endLo = Vector3.zero; int nm = 0, nh = 0, nl = 0;
            foreach (var v in vs)
            {
                float k = (Vector3.Dot(v, la) - lo) / Mathf.Max(1e-5f, hi - lo);
                if (k > 0.4f && k < 0.6f) { mid += v; nm++; }
                else if (k > 0.85f) { endHi += v; nh++; }
                else if (k < 0.15f) { endLo += v; nl++; }
            }
            if (nm == 0 || nh == 0 || nl == 0) return;
            mid /= nm; endHi /= nh; endLo /= nl;
            Vector3 Perp(Vector3 p) { var d = p - mid; return d - la * Vector3.Dot(d, la); }
            // 양 끝 15% 구간에서 자루에서 가장 멀리 튀어나온 점 (날 끝)
            Vector3 farHi = endHi, farLo = endLo; float dHi = 0f, dLo = 0f;
            foreach (var v in vs)
            {
                float k = (Vector3.Dot(v, la) - lo) / Mathf.Max(1e-5f, hi - lo);
                float pd = Perp(v).sqrMagnitude;
                if (k > 0.85f && pd > dHi) { dHi = pd; farHi = v; }
                else if (k < 0.15f && pd > dLo) { dLo = pd; farLo = v; }
            }
            bool bladeHi = dHi >= dLo;
            var bladeAxis = bladeHi ? la : -la;
            var bladeSide = Perp(bladeHi ? farHi : farLo).normalized;
            DevBladeInfo = $"날 끝 {(bladeHi ? "+" : "-")}축 · 튀어나옴 {Mathf.Sqrt(Mathf.Max(dHi, dLo)):0.00} vs {Mathf.Sqrt(Mathf.Min(dHi, dLo)):0.00}";
            // 지금 쥔 자루 자리 (자루 선에서 주먹 가운데와 가장 가까운 점, 무기 로컬)
            var cl = c.InverseTransformPoint(center);
            var gripLocal = mid + la * Vector3.Dot(cl - mid, la);
            OrientShaft(c, F, S, center, bladeAxis, bladeSide, gripLocal);
        }

        // 자루 방향·날 옆·쥐는 자리(무기 로컬)를 받아, 자루가 주먹을 비스듬히 지나고 날은 위로 서게
        private static void OrientShaft(Transform c, Vector3 F, Vector3 S, Vector3 center, Vector3 bladeAxis, Vector3 bladeSide, Vector3 gripLocal)
        {
            // 새 방향: 날 쪽 = S를 손목 쪽으로 40° 기울임, 날이 향하는 옆은 지금과 최대한 같게
            float a = 55f * Mathf.Deg2Rad; // 팔을 내린 자세에서 자루가 더 서도록
            var D = (S * Mathf.Cos(a) - F * Mathf.Sin(a)).normalized;
            var curSide = c.localRotation * bladeSide;
            var newSide = curSide - D * Vector3.Dot(curSide, D);
            if (newSide.sqrMagnitude < 1e-4f) newSide = Vector3.Cross(D, F);
            c.localRotation = Quaternion.LookRotation(D, newSide.normalized) * Quaternion.Inverse(Quaternion.LookRotation(bladeAxis, bladeSide - bladeAxis * Vector3.Dot(bladeSide, bladeAxis)));
            c.position = center - (c.TransformPoint(gripLocal) - c.position);
        }

        private void SeatWeapons(System.Func<string, Transform> f)
        {
            foreach (var hand in _hands)
            {
                if (hand == null) continue;
                foreach (Transform c in hand.Root)
                {
                    var k = HoldOf(c.name);
                    if (k == Hold.None) continue;
                    HandleGrip.TryGetValue(c.name, out float along);
                    var orig = c.localPosition; var origRot = c.localRotation;
                    // 디자인 Socket은 손가락을 편 자세 기준(손목에서 약 17cm, 가운데 마디 높이) → 굽힌 주먹과 어긋남.
                    // 그 무기를 쥔 모양으로 손가락을 실제로 굽혀 본 뒤, 검지·중지가 만든 고리의 가운데를 손잡이 자리로
                    Pose(hand, Held[k], 0f);
                    var center = (LoopCenter(hand.Index) + LoopCenter(hand.Middle)) * 0.5f;
                    Pose(hand, null, 0f);
                    var axis = c.TransformDirection(LongAxis(c)).normalized;
                    if (c.name is "Scythe" or "Weapon_Scythe") SeatShaft(hand, c, center);
                    else if (c.name is "Weapon_Pistol" or "Weapon_Musket") SeatGun(hand, c, center);
                    else c.position = center + axis * along;
                    _seats.Add((c, orig, c.localPosition, origRot, c.localRotation));
                    DevSeated++;
                }
            }
        }

        // 긴 무기 자루 위의 한 점: 긴 축 방향 40~60% 구간(날·머리가 없는 자루 가운데) 꼭짓점 평균
        private static Vector3 ShaftPoint(Transform w, Vector3 axis)
        {
            var mf = w.GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return w.position;
            var vs = mf.sharedMesh.vertices;
            var m = mf.transform.localToWorldMatrix;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in vs) { float d = Vector3.Dot(m.MultiplyPoint3x4(v), axis); lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var v in vs)
            {
                var p = m.MultiplyPoint3x4(v); float k = (Vector3.Dot(p, axis) - lo) / Mathf.Max(1e-5f, hi - lo);
                if (k > 0.4f && k < 0.6f) { sum += p; n++; }
            }
            return n > 0 ? sum / n : w.position;
        }

        // 굽힌 손가락 고리(뿌리·가운데 마디·끝)의 가운데. 끝은 둘째 마디 방향으로 첫 마디 길이의 0.85만큼
        private static Vector3 LoopCenter(Finger f)
        {
            float len = Vector3.Distance(f.A.position, f.B.position);
            var tip = f.B.position + f.B.TransformDirection(f.LocalDirB) * len * 0.85f;
            return (f.A.position + f.B.position + tip) / 3f;
        }

        // 무기 모델의 긴 축 (로컬)
        private static Vector3 LongAxis(Transform w)
        {
            var mf = w.GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null) return Vector3.forward;
            var s = mf.sharedMesh.bounds.size;
            var local = s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
            return w.InverseTransformDirection(mf.transform.TransformDirection(local)).normalized;
        }

        // 손 밑에 켜진 무기로 쥔 물건 판단 (장착 무기가 바뀌면 HeldWeapon이 켜고 끔 → 0.5초마다 다시 봄)
        public void RefreshHolds()
        {
            foreach (var hand in _hands)
            {
                if (hand == null) continue;
                hand.Hold = Hold.None;
                foreach (Transform c in hand.Root)
                    if (c.gameObject.activeSelf) { var k = HoldOf(c.name); if (k != Hold.None) { hand.Hold = k; break; } }
            }
        }

        public static Hold HoldOf(string n)
        {
            if (n.StartsWith("Weapon_"))
            {
                string w = n.Substring(7);
                return w switch
                {
                    "Dagger" or "Scalpel" or "RustDagger" => Hold.Fist,
                    "Pistol" or "Musket" => Hold.Trigger,
                    "Staff" or "Scythe" => Hold.Rod,
                    "MercuryFlask" => Hold.Bottle,
                    "Grimoire" => Hold.Book,
                    _ => Hold.Fist,
                };
            }
            return n == "Scythe" ? Hold.Rod : Hold.None;
        }

        // ── 동작별 손 모양 ──
        // state: 지금 동작 이름, t: 그 동작의 진행 비율(0~1). 반환: 이 손이 될 모양
        private static Shape Target(Hand hand, Hand other, string state, float t, out float squeeze)
        {
            squeeze = 0f;
            bool held = hand.Hold != Hold.None;
            // 11/14 동작 측정(GripCheckShot.Analyze): 스킬·아이템·예고 손짓은 오른손이 함. 왼손은 거의 안 움직임 → 빈 왼손은 쉼 자세
            bool gestureHand = hand.Right;
            if (!held && !gestureHand && state is "Skill" or "Item" or "Heal" or "Announce") return Relaxed;
            // 두 손 공격(역병 의사 지팡이·사신 낫: 측정상 두 손이 같이 크게 움직임) → 빈손도 자루를 감싸 쥠
            if (!held && state == "Attack" && other != null && other.Hold == Hold.Rod) return Shape.Lerp(Relaxed, Held[Hold.Rod], Mathf.InverseLerp(0.05f, 0.25f, t) * (1f - Mathf.InverseLerp(0.75f, 0.95f, t)));
            Shape grip = held ? Held[hand.Hold] : Relaxed;
            // 총: 조준하는 동안만 검지가 방아쇠로 들어감 (평소엔 총 몸통 옆에 곧게)
            if (hand.Hold == Hold.Trigger) grip = new Shape(grip.Thumb, Vector2.Lerp(grip.Index, TriggerOn, AimOf(state, t)), grip.Middle);
            switch (state)
            {
                case "Attack":
                    if (held)
                    {
                        // 휘두르거나 쏘는 순간 꽉 쥠. 방아쇠는 맞는 때(약 0.35) 검지를 당김
                        float hit = Bell(t, 0.36f, 0.14f);
                        if (hand.Hold == Hold.Trigger) squeeze = hit;
                        return Tighten(grip, 0.06f + 0.1f * hit);
                    }
                    return Shape.Lerp(Relaxed, Brace, 0.45f); // 빈손도 힘이 들어감
                case "Hit":
                    return held ? Tighten(grip, 0.12f) : Shape.Lerp(Relaxed, Open, 0.8f); // 무기는 놓치지 않게 꽉, 빈손은 놀라 펴짐
                case "Defend":
                    return held ? Tighten(grip, 0.12f) : Brace;
                case "Dodge":
                    return held ? Tighten(grip, 0.08f) : Shape.Lerp(Relaxed, Open, 0.4f);
                case "Skill":
                    // 강화 스킬: 관자놀이 집중(앞) → 앞을 가리킴(0.55쯤부터). 무기 든 손은 쥔 채로
                    if (held) { if (hand.Hold == Hold.Trigger) squeeze = Bell(t, 0.62f, 0.12f); return Tighten(grip, 0.04f); } // 권총은 가리키는 때 = 조준, 방아쇠 살짝
                    return t < 0.45f ? Shape.Lerp(Relaxed, Temple, Mathf.InverseLerp(0.05f, 0.25f, t)) : Shape.Lerp(Temple, Point, Mathf.InverseLerp(0.45f, 0.6f, t));
                case "Item":
                    return held ? grip : Shape.Lerp(Relaxed, Cup, Bell(t, 0.45f, 0.35f) * 1.2f); // 병을 입에 대는 손
                case "Heal":
                    // 약을 쥐었다가(앞) 아군 쪽으로 내밀며 손을 폄(0.7쯤)
                    if (held) return grip;
                    return t < 0.6f ? Shape.Lerp(Relaxed, Cup, Mathf.InverseLerp(0.1f, 0.35f, t)) : Shape.Lerp(Cup, Open, Mathf.InverseLerp(0.6f, 0.8f, t));
                case "SelfHeal":
                    return held ? Tighten(grip, 0.06f) : Shape.Lerp(Relaxed, Cup, 0.8f);
                case "Announce":
                    return held ? Tighten(grip, 0.04f) : Shape.Lerp(Relaxed, Point, Bell(t, 0.5f, 0.3f));
                case "Victory":
                    return held ? Tighten(grip, 0.1f) : Shape.Lerp(Relaxed, Brace, 0.7f);
                case "Death":
                    // 쓰러지며 힘이 빠짐. 무기는 손에 붙어 있으므로 절반만 풂
                    return Shape.Lerp(held ? grip : Relaxed, held ? Shape.Lerp(grip, Relaxed, 0.5f) : Shape.Lerp(Relaxed, Open, 0.3f), Mathf.InverseLerp(0.2f, 0.9f, t));
                default: // Idle · Walk · Recover · Revive
                    return held ? grip.Scaled(0.94f) : Relaxed;
            }
        }

        private Hand Other(Hand h) => h == _hands[0] ? _hands[1] : _hands[0];

        private static Shape Tighten(Shape s, float k) => new(s.Thumb * (1f + k), s.Index * (1f + k), s.Middle * (1f + k));
        private static float Bell(float t, float center, float width) => Mathf.Clamp01(1f - Mathf.Abs(t - center) / width);

        private static readonly string[] States = { "Attack", "Hit", "Defend", "Dodge", "Skill", "Item", "Heal", "SelfHeal", "Announce", "Victory", "Death", "Revive", "Recover", "Walk", "Idle" };
        private static readonly int[] StateHashes = System.Array.ConvertAll(States, Animator.StringToHash);

        private string StateOf(AnimatorStateInfo info) { for (int i = 0; i < States.Length; i++) if (info.shortNameHash == StateHashes[i]) return States[i]; return "Idle"; }

        private void LateUpdate()
        {
            if (Time.time >= _nextHoldCheck) { _nextHoldCheck = Time.time + 0.5f; RefreshHolds(); }
            string state = "Idle"; float t = 0f;
            if (_anim != null && _anim.isActiveAndEnabled && _anim.runtimeAnimatorController != null)
            {
                var info = _anim.GetCurrentAnimatorStateInfo(0);
                if (_anim.IsInTransition(0))
                {
                    var tr = _anim.GetAnimatorTransitionInfo(0);
                    if (tr.normalizedTime > 0.4f) info = _anim.GetNextAnimatorStateInfo(0); // 넘어가는 중이면 다음 동작 모양으로 일찍 바꿈
                }
                state = StateOf(info);
                t = info.loop ? Mathf.Repeat(info.normalizedTime, 1f) : Mathf.Clamp01(info.normalizedTime);
            }
            if (DevOff != _seatedOff) { _seatedOff = DevOff; Seat(!DevOff); }
            float dt = Time.deltaTime;
            foreach (var hand in _hands)
            {
                if (hand == null) continue;
                var target = Target(hand, Other(hand), state, t, out float squeeze);
                // 부드럽게 따라감 (약 0.08초). 방아쇠는 더 빠르게
                hand.Now = hand.HasNow ? Shape.Lerp(hand.Now, target, 1f - Mathf.Exp(-dt * 14f)) : target; hand.HasNow = true;
                hand.Squeeze = Mathf.Lerp(hand.Squeeze, squeeze, 1f - Mathf.Exp(-dt * 30f));
                Pose(hand, DevOff ? (Shape?)null : hand.Now, hand.Squeeze);
            }
            if (!DevOff)
            {
                float want = Mathf.Lerp(IdleTilt, AimTilt, AimOf(state, t));
                foreach (var g in _guns) PlaceGun(g, Mathf.Lerp(g.Tilt, want, 1f - Mathf.Exp(-dt * 12f)));
            }
        }

        // 손 하나에 모양 적용: 기본 자세로 되돌린 뒤 손바닥 쪽으로 굽힘
        private static void Pose(Hand hand, Shape? shape, float squeeze)
        {
            foreach (var f in new[] { hand.Thumb, hand.Index, hand.Middle }) if (f != null) { f.A.localRotation = f.RestA; f.B.localRotation = f.RestB; }
            if (shape == null) return;
            var s = shape.Value;
            // 손바닥 방향: 손가락 방향(손목→중지 뿌리) × 엄지 쪽(중지 뿌리→검지 뿌리). 오른손·왼손은 반대
            var fingerDir = (hand.Middle.A.position - hand.Root.position).normalized;
            var thumbSide = (hand.Index.A.position - hand.Middle.A.position).normalized;
            var palm = (hand.Right ? Vector3.Cross(fingerDir, thumbSide) : Vector3.Cross(thumbSide, fingerDir)).normalized;
            Curl(hand.Index, s.Index + new Vector2(18f, 14f) * squeeze, palm);
            Curl(hand.Middle, s.Middle, palm);
            // 엄지: 손바닥 쪽 + 새끼 쪽으로 감아 검지·중지 위를 덮음
            if (hand.Thumb != null) Curl(hand.Thumb, s.Thumb, (palm * 0.75f - thumbSide * 0.65f).normalized);
        }

        private static void Curl(Finger f, Vector2 deg, Vector3 toward)
        {
            if (f == null) return;
            var dirA = (f.B.position - f.A.position).normalized;
            var axisA = Vector3.Cross(dirA, toward);
            if (axisA.sqrMagnitude > 1e-6f) f.A.rotation = Quaternion.AngleAxis(deg.x, axisA.normalized) * f.A.rotation;
            var dirB = f.B.TransformDirection(f.LocalDirB);
            var axisB = Vector3.Cross(dirB, toward);
            if (axisB.sqrMagnitude > 1e-6f) f.B.rotation = Quaternion.AngleAxis(deg.y, axisB.normalized) * f.B.rotation;
        }

        // ── 에디터 확인 도구용 (GripCheckShot) ──
        public void InitForEditor() { if (_hands[0] == null && _hands[1] == null) return; foreach (var h in _hands) if (h != null) h.HasNow = false; }
        public void ApplyForEditor(string state, float t)
        {
            Seat(state != null); // 전 = 원래 무기 자리, 후 = 주먹 가운데
            foreach (var hand in _hands)
            {
                if (hand == null) continue;
                if (state == null) { Pose(hand, null, 0f); continue; }
                var target = Target(hand, Other(hand), state, t, out float squeeze);
                Pose(hand, target, squeeze);
            }
            if (state != null) foreach (var g in _guns) PlaceGun(g, Mathf.Lerp(IdleTilt, AimTilt, AimOf(state, t)));
        }

        private void OnDestroy() => DevActive--;
    }
}
