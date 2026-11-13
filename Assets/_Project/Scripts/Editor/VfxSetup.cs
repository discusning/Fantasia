using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/13 전투 이펙트 1차: 받은 에셋에서 고른 효과를 크기·색·길이만 맞춰 Resources/VFX/{이름}.prefab 으로 만든다 (BattleVfx·StatusAura가 읽음).
    // 원칙(조사 문서 Docs/Art/VFX_연구_1113): 평범한 동작은 작고 짧게, 크리티컬·부활만 조금 크게. 색 = 숫자 색과 같은 뜻.
    // 11/13 팀장 피드백: 일반 물리 공격 = 가벼운 흰색 (주황은 크리티컬만) · 중독 = 독 느낌으로 · 회복 = 더 짧게.
    // 다시 만들기: -executeMethod Fantasia.Editor.VfxSetup.Build
    public static class VfxSetup
    {
        private const string Out = "Assets/_Project/Resources/VFX";
        private const string Hovl = "Assets/ThirdParty/Hovl Studio/Magic effects pack/Prefabs/";
        private const string Travis = "Assets/ThirdParty/Travis Game Assets/Hit Impact Effects/Prefabs/";
        private const string WarFx = "Assets/ThirdParty/JMO Assets/WarFX/_Effects/";
        private const string Quick = "Assets/ThirdParty/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/";

        private static readonly Color White = new Color(1f, 0.97f, 0.92f);

        private class Part
        {
            public string Path; public float Scale = 1f; public Vector3 Offset; public Color? Tint; public float Alpha = 1f;
            public string[] Drop; // 빼는 자식 (이름)
        }
        private class Recipe
        {
            public string Key, Note; public Part[] Parts;
            public bool Loop; public float Rate = 1f;
            public float LifeMul = 1f, DurationCap = 0f; // 길이 줄이기 (0 = 그대로)
            public float Period; // 반복 간격 (초, 0 = 그대로) — 순간 효과를 몇 초마다 한 번씩
            public float Gravity = -1f; // 0 이상이면 아래로 떨어지게 (피 방울)
            public bool SingleFlash; // 계속 번쩍이는 효과를 시작 순간 1번만 (총구 불꽃)
            public float SizeMul = 1f; // 알갱이 하나하나 크기 (전투 카메라 거리에서 보이게)
        }

        private static Part P(string path, float scale, Color? tint = null, float alpha = 1f, Vector3 offset = default) =>
            new Part { Path = path, Scale = scale, Tint = tint, Alpha = alpha, Offset = offset };

        private static IEnumerable<Recipe> Recipes()
        {
            yield return new Recipe { Key = "Hit", Note = "일반 물리 공격(칼·발톱·물기): 가벼운 흰 섬광 1번",
                Parts = new[] { P(Travis + "Hits/Hit_01.prefab", 2.6f, White, 0.85f) } };
            yield return new Recipe { Key = "HitMagic", Note = "지팡이·마법서·마법 피해: 작은 푸른 흰 빛 번짐",
                Parts = new[] { P(Travis + "Hits/Hit_02.prefab", 1.6f) } };
            yield return new Recipe { Key = "HitShot", Note = "총알 맞음: 짧은 흰 섬광만 (약 0.2초)",
                Parts = new[] { P(Travis + "Hits/Hit_01.prefab", 1.8f, White, 0.85f) }, // 11/13 팀장: 아직 오래 남음 → 회색 연기 뺌, 짧은 흰 섬광만
                DurationCap = 0.1f, LifeMul = 0.4f };
            // 크리티컬 (팀장 11/13: 폭탄 터지는 느낌 → 타격 느낌으로) → 불덩이 폭발(vfx_Impact_01) 뺌. 아래
            // 후보 3개(가시 빛 / 불티 / 둘 다) 비교 → 불티가 옆으로 튀는 B 채택, 고리는 빨리 끝나게(퍼지며 커지는 게 폭발처럼 보임)
            yield return new Recipe { Key = "Crit", Note = "크리티컬: 주황 섬광(짧게) + 불티가 옆으로 튐 = 세게 맞은 느낌",
                Parts = new[] { P(Travis + "Hits/Hit_01.prefab", 3.2f), P(Hovl + "Sparks/Sparks explode yellow.prefab", 0.8f, CritOrange, 1f) }, LifeMul = 0.45f };
            // 11/13 팀장: 크리티컬이 총·폭탄처럼 터짐 → 총(저격수)에만 위 Crit. 근접·마법은 따로. 근접은 베기 궤적, 마법은 큰 마법 빛
            yield return new Recipe { Key = "CritMelee", Note = "근접 크리티컬: 주황 베기 궤적 한 번 + 짧은 섬광 (후보 3개 중 베기 휘두름 채택)",
                Parts = new[] { P(Hovl + "AoE effects/AoE slash orange.prefab", 0.3f), P(Travis + "Hits/Hit_01.prefab", 2.2f) }, DurationCap = 0.2f }; // 원본은 여러 번 휘두름 → 첫 번만
            yield return new Recipe { Key = "CritMagic", Note = "마법 크리티컬: 푸른 흰 빛 크게 + 주황 섬광 조금",
                Parts = new[] { P(Travis + "Hits/Hit_02.prefab", 2.4f), P(Travis + "Hits/Hit_01.prefab", 1.8f, null, 0.7f) }, LifeMul = 0.7f };
            // 총: 에셋 원본은 1초 동안 계속 번쩍임(반복용) → 한 번만 번쩍 (팀장: 총격보다 이펙트가 오래 머묾)
            yield return new Recipe { Key = "Muzzle", Note = "총구 불꽃: 한 번만 번쩍 (약 0.1초)",
                Parts = new[] { P(WarFx + "MuzzleFlashes/Sprites/WFX_MF Spr RIFLE1.prefab", 0.6f) }, DurationCap = 0.1f, SingleFlash = true };
            yield return new Recipe { Key = "Miss", Note = "빗나감: 발밑 흙먼지 살짝",
                Parts = new[] { P(WarFx + "Bullet Impacts/WFX_BImpact Dirt.prefab", 0.8f) } };
            yield return new Recipe { Key = "Defend", Note = "방어: 몸 앞 푸른 고리 1번",
                Parts = new[] { P(Travis + "Guards/Guard_01.prefab", 2.4f) } };
            // 회복: 예전 3초 넘게 → 약 1초 (팀장: 너무 길다)
            yield return new Recipe { Key = "Heal", Note = "회복: 발밑에서 초록 알갱이가 짧게 올라감 (약 1초)",
                Parts = new[] { P(Hovl + "Character auras/Healing.prefab", 0.7f) }, DurationCap = 0.35f, LifeMul = 0.5f };
            yield return new Recipe { Key = "Revive", Note = "부활: 가슴 높이 흰 금빛 + 초록 알갱이 (약 1.3초)",
                Parts = new[] { P(Hovl + "Hits and explosions/Holy hit.prefab", 0.7f, offset: new Vector3(0f, 1f, 0f)), P(Hovl + "Character auras/Healing.prefab", 0.9f) },
                DurationCap = 0.5f, LifeMul = 0.6f };

            // ── 디버프 (팀장 요청 11/13) ──
            // 걸릴 때 = "약화" 효과(안개 + 내려가는 알갱이)를 종류 색으로 1번 (Debuff_{종류})
            // 걸려 있는 동안 = 몸 둘레 작은 알갱이가 천천히, 흐리게 (Aura_{종류}, 반복 — StatusAura가 켜고 끔)
            // 색 = 뜻: 출혈 검붉은 · 혼란 보라 · 속박 잿빛 파랑 · 공격 약화 짙은 주황 · 방어 약화 강철 파랑 · 회피 해제 흰 회색
            foreach (var (type, tint) in DebuffColors)
            {
                if (type is "Poison" or "WeakBleed" or "DefenseDown" or "EvasionRevealed" or "Bind") continue; // 걸림·동안 따로 만든 것: 아래 // 따로 만든 것: 아래
                yield return new Recipe { Key = $"Debuff_{type}", Note = $"{type} 걸림",
                    Parts = new[] { P(Hovl + "Character auras/Debuff.prefab", 0.75f, Color.Lerp(tint, Color.white, 0.45f)), P(Hovl + "Sparks/Sparks explode white.prefab", 0.9f, Color.Lerp(tint, Color.white, 0.3f), 1f) } }; // 11/13 QA: 어두운 땅에 묻혀 밝게
            }

            // ── 11/13 팀장: 파티가 적에게 거는 디버프 스킬은 "이름에 맞는" 걸림 효과 ──
            // 탐정 [약점 간파] = 방어 약화 → 가슴에 조준 표시 + 훑는 고리 (약점을 찾아냄)
            // 방어 약화 (팀장 11/13: 조준 표시는 "방어력 감소"와 안 맞고 위치도 어긋남) → 아래
            // 후보 3개(잿빛 고리 깨짐 / 금 감 + 조각 / 금 감 + 화살표) 비교 → B 채택: 방어막이 깨져 조각이 떨어짐. 화살표는 어두운 배경에서 안 보임
            yield return new Recipe { Key = "Debuff_DefenseDown", Note = "방어 약화(약점 간파): 강철빛 방어막에 금이 가고 조각이 떨어짐",
                Parts = new[] { P(Travis + "Hits/Hit_02.prefab", 1.5f, Steel, 1f), P(Hovl + "Sparks/Sparks explode white.prefab", 0.9f, Steel, 1f) }, Gravity = 1.6f };
            // 탐정 [장막 걷기] = 회피 해제 → 흰 빛 + 고리가 옆으로 퍼짐 (가려 있던 장막이 걷힘)
            yield return new Recipe { Key = "Debuff_EvasionRevealed", Note = "장막 걷기: 흰 빛 + 옆으로 퍼지는 고리",
                Parts = new[] { P(Quick + "vfx_Shockwave_01.prefab", 0.5f), P(Hovl + "Sparks/Sparks explode white.prefab", 0.9f, null, 0.8f) } };
            // 저격수 [저지 사격] = 속박(느려짐) → 발밑에 총알 튀는 흙 + 짧은 잿빛 묶는 원 (발이 묶임)
            yield return new Recipe { Key = "Debuff_Bind", Note = "저지 사격: 발밑 흙 튐 + 짧은 잿빛 원",
                Parts = new[] { P(Hovl + "Hits and explosions/Stones hit.prefab", 0.5f), P(Hovl + "Magic circles/Magic circle.prefab", 0.3f, BindGrey, 0.8f) },
                DurationCap = 0.4f, LifeMul = 0.5f };
            // 11/13 팀장: 속박 동안 흰 알갱이가 튀는 건 안 맞음·위치도 어긋남 → "걸릴 때 뜨는 동그란 원을 2개 만들어 묶는 느낌" (팀장 제안)
            // 11/13 팀장: 상반신에 하나, 상~하반신 사이에 하나 → 허리(키 0.45)·가슴(키 0.68) 높이에 같은 잿빛 원 2개 (흩날리는 불꽃·조명은 뺌), 모델 발밑 중심을 따라다님
            yield return new Recipe { Key = "Aura_Bind", Note = "속박 동안: 허리·가슴 높이 잿빛 원 2개가 몸을 묶음", Loop = true,
                Parts = new[] { BindCircle(0f), BindCircle(0f) } }; // 높이는 StatusAura가 모델 키로 정함 (허리·가슴)
            // 중독: 누런 초록 독 연기 (11/13 팀장: 연기 줄인 안을 봤으나 이 안이 낫다 → 되돌림. 역병의사 "검은 연무"와도 어울림)
            yield return new Recipe { Key = "Debuff_Poison", Note = "중독 걸림: 발밑에서 누런 초록 독 연기가 올라옴",
                Parts = new[] { P(Hovl + "Smoke effects/Smoke puff.prefab", 0.22f, PoisonGas, 0.55f), P(Hovl + "Sparks/Sparks green.prefab", 2.2f, PoisonGas, 1f) },
                LifeMul = 0.5f }; // 11/13: 너무 멀리 퍼지지 않게 0.7 → 0.5
            // 11/13 팀장: 머리 위로 너무 멀리 퍼짐 → 연기 수명 짧게(몸 높이에서 사라짐)
            yield return new Recipe { Key = "Aura_Poison", Note = "중독 동안: 몸 둘레 옅은 독 김 (머리 위로 안 올라감)", Loop = true, Rate = 0.6f, LifeMul = 0.4f,
                Parts = new[] { P(Hovl + "Smoke effects/Smoke loop.prefab", 0.22f, PoisonGas, 0.4f) } };

            // 출혈: 받은 에셋 붉은 불꽃은 빛나서 피 같지 않음 (팀장 11/13: 과하고 피 같지 않음, 위치 이상) → BuildBlood에서 직접 만듦

            // ── 강화 (팀장 11/13: 영점 조정에 효과 없음) ── 걸리는 순간 1번, 둘레 효과는 없음 (짧은 강화라)
            // 저격수 [영점 조정] = 다음 사격 명중 ↑ → 머리 앞에 작은 흰 파랑 조준선이 좁혀짐
            yield return new Recipe { Key = "Buff_AccuracyUp", Note = "영점 조정: 머리 앞 작은 흰 파랑 고리가 돌며 좁혀짐 (집중)",
                Parts = new[] { P(Travis + "Hits/Hit_04.prefab", 1.6f, new Color(0.75f, 0.9f, 1f), 1f) } }; // QA: 조준선(Hit_03)은 빨간 부분이 남고 불꽃이 오래 남아 뺌
            // 신속의 앰플 = 속도 ↑ → 발밑에서 흰 빛줄기가 빠르게 올라감
            yield return new Recipe { Key = "Buff_SpeedUp", Note = "신속: 발밑에서 흰 빛줄기가 빠르게 올라감",
                Parts = new[] { P(Hovl + "Character auras/Buff.prefab", 0.6f, new Color(0.8f, 0.95f, 1f), 0.9f) }, DurationCap = 0.5f, LifeMul = 0.6f };

            // ── 11/13 팀장 6차: 캐릭터 스킬 컨셉마다 이펙트가 모두 다르게 (Skill_{스킬 id 끝}, BattleController.SkillVfxKey) ──
            // 탐정 = 금빛·정밀 / 역병 의사 = 검보라 연기·약초 초록 / 저격수 = 흰 강철·흙. 일반 공격(무기별)과 겹치지 않게 고름
            var gold = new Color(1f, 0.85f, 0.45f);
            // 탐정 [일점 돌파]: 한 점을 꿰뚫음 → 금빛 고리가 한 점으로 좁혀지며 금 불티가 튐
            yield return new Recipe { Key = "Skill_pinpoint_breach", Note = "일점 돌파: 금빛 고리가 한 점으로 좁혀지고 금 불티",
                Parts = new[] { P(Travis + "Hits/Hit_04.prefab", 1.9f, gold, 1f), P(Hovl + "Sparks/Sparks explode yellow.prefab", 0.6f, gold, 1f) }, LifeMul = 0.5f };
            // 역병 의사 [검은 연무]: 검보라 독 안개가 적을 덮음 (중독 초록과 구분되는 어두운 보라)
            yield return new Recipe { Key = "Skill_black_miasma", Note = "검은 연무: 적마다 검보라 안개가 덮침",
                Parts = new[] { P(Hovl + "Smoke effects/Smoke puff.prefab", 0.24f, new Color(0.42f, 0.32f, 0.48f), 0.55f), P(Hovl + "Sparks/Sparks explode pink.prefab", 0.7f, new Color(0.55f, 0.35f, 0.7f), 1f) },
                LifeMul = 0.55f };
            // 11/13 QA: 치료 원이 캐릭터보다 훨씬 넓게 퍼짐 → 발밑 폭만큼. 검은 연무는 까만 구멍처럼 보임 → 옅은 검보라
            // 역병 의사 [응급 봉합]: 발밑 초록 치료 원 + 짧게 올라가는 알갱이 (일반 물약 회복과 구분)
            yield return new Recipe { Key = "Skill_emergency_suture", Note = "응급 봉합: 발밑 초록 치료 원 + 알갱이",
                Parts = new[] { P(Hovl + "Magic circles/Healing circle.prefab", 0.14f), P(Hovl + "Character auras/Healing.prefab", 0.6f) }, DurationCap = 0.45f, LifeMul = 0.55f };
            // 역병 의사 [역병 정화]: 가슴에서 맑은 흰 초록 빛이 터짐
            yield return new Recipe { Key = "Skill_purge_plague", Note = "역병 정화: 가슴에서 맑은 흰 초록 빛",
                Parts = new[] { P(Hovl + "Hits and explosions/Holy hit.prefab", 0.55f, new Color(0.85f, 1f, 0.85f), 1f), P(Hovl + "Sparks/Sparks explode green.prefab", 0.7f, null, 0.9f) }, LifeMul = 0.6f };
            // 저격수 [관통탄]: 몸을 꿰뚫는 강한 흰 섬광 + 금속 불티 (일반 총알 섬광보다 크고 날카롭게)
            yield return new Recipe { Key = "Skill_piercing_round", Note = "관통탄: 강한 흰 섬광 + 금속 불티",
                Parts = new[] { P(Travis + "Hits/Hit_01.prefab", 3f, White, 1f) }, LifeMul = 0.45f };
            yield return new Recipe { Key = "Skill_piercing_round_exit", Note = "관통탄 빠져나감: 등 뒤로 금속 불티 (쏜 방향)",
                Parts = new[] { P(WarFx + "Bullet Impacts/WFX_BImpact Metal.prefab", 0.9f) }, LifeMul = 0.6f };
            // 저격수 [저지 사격]: 발밑에 총알이 박혀 흙이 튐 (발을 묶음)
            yield return new Recipe { Key = "Skill_suppressing_fire", Note = "저지 사격: 발밑 흙이 튐",
                Parts = new[] { P(WarFx + "Bullet Impacts/WFX_BImpact Dirt.prefab", 1.2f), P(Hovl + "Hits and explosions/Stones hit.prefab", 0.45f) }, DurationCap = 0.4f, LifeMul = 0.5f };
        }

        // ── 출혈 (직접 만듦): 빛나지 않는 검붉은 피 방울, 몸(가슴)에서 몇 방울 떨어짐 ──
        private static void BuildBlood()
        {
            const string matPath = Out + "/Mat_Blood.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")); // 빛 더하기 X → 어두운 색이 그대로 보임
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorUtility.SetDirty(mat);

            // 걸리는 순간: 방울 7개가 옆으로 살짝 튀고 떨어짐 (0.7초)
            SaveBlood("Debuff_WeakBleed", mat, loop: false, burst: 16, rate: 0f, speed: (0.6f, 1.4f), life: (0.5f, 0.8f)); // 11/13 팀장: 너무 조금 → 7 → 16방울
            // 걸려 있는 동안: 1초에 1방울 남짓 똑똑 떨어짐
            SaveBlood("Aura_WeakBleed", mat, loop: true, burst: 0, rate: 3.5f, speed: (0.05f, 0.3f), life: (0.5f, 0.75f)); // 1초에 3~4방울
            BuildSoftAuras(mat); // 11/13 방어 약화·공격 약화·혼란·회피 해제 동안
            // 11/13 팀장 A안: 방어 약화 동안 = 모델 테두리 강철빛 파랑 (StatusRim) — 셰이더가 빌드에 들어가게 재질을 Resources에 둠
            var rimShader = Shader.Find("Fantasia/RimPulse");
            foreach (var (rimName, rimCol) in new[] { ("Mat_Rim_DefenseDown", new Color(0.4f, 0.6f, 1f, 1f)), ("Mat_Rim_AttackDown", new Color(1f, 0.5f, 0.15f, 1f)) })
            if (rimShader != null)
            {
                string rimPath = Out + "/" + rimName + ".mat";
                var rim = AssetDatabase.LoadAssetAtPath<Material>(rimPath);
                if (rim == null) { rim = new Material(rimShader); AssetDatabase.CreateAsset(rim, rimPath); }
                rim.shader = rimShader;
                rim.SetColor("_RimColor", rimCol);
                rim.SetFloat("_Power", 3.4f); // 가장자리만 (QA: 2.6은 몸 전체가 파래짐)
                EditorUtility.SetDirty(rim);
            }
            else Debug.LogWarning("[VfxSetup] Fantasia/RimPulse 셰이더 없음");
        }

        // ── 디버프 동안 (직접 만듦, 11/13 팀장: 방어력 감소 동안 알갱이가 몬스터 위치와 어긋남) ──
        // 받은 "Sparks red"는 한쪽으로 빠르게 쏘는 불꽃이라 알갱이가 몸 옆으로 날아갔음 → 몸 둘레 고리에서 천천히 떠다니는 알갱이로.
        // 빛 더하기 X(피와 같은 재질) → 어두운 색도 보이고 튀지 않음. 모델을 따라다님(Local).
        private static void BuildSoftAuras(Material mat)
        {
            // 11/13 팀장 6차: 공격력 감소 남는 알갱이가 걸리는 순간 효과와 안 맞음 → 주황 테두리 + 부러진 칼 아이콘 (StatusRim)
//            SoftAura("Aura_AttackDown", mat, new Color(0.95f, 0.5f, 0.15f), new Color(1f, 0.7f, 0.3f), vy: -0.2f, orbit: 0f, gravity: 0f, size: (0.11f, 0.16f));   // 주황 알갱이가 가라앉음
            SoftAura("Aura_Confusion", mat, new Color(0.72f, 0.45f, 0.98f), new Color(0.88f, 0.7f, 1f), vy: 0f, orbit: 1.4f, gravity: 0f, size: (0.11f, 0.16f));      // 보라 알갱이가 몸 둘레를 돎
            SoftAura("Aura_EvasionRevealed", mat, new Color(0.72f, 0.72f, 0.76f), new Color(0.92f, 0.92f, 0.95f), vy: 0.25f, orbit: 0f, gravity: 0f, size: (0.1f, 0.15f)); // 흰 회색이 위로 흩어짐
        }

        private static void SoftAura(string key, Material mat, Color a, Color b, float vy, float orbit, float gravity, (float min, float max) size)
        {
            var root = new GameObject(key);
            var ring = new GameObject("Ring");
            ring.transform.SetParent(root.transform, false);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 원을 바닥과 나란하게
            var ps = ring.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f; main.loop = true; main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity;
            main.maxParticles = 40;
            var em = ps.emission; em.rateOverTime = 16f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f; shape.radiusThickness = 0.3f; // 몸 바로 둘레
            var vel = ps.velocityOverLifetime;
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = 0f; vel.y = vy; vel.z = 0f;
            vel.orbitalX = 0f; vel.orbitalY = orbit; vel.orbitalZ = 0f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = ring.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/{key}.prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[VfxSetup] {key} — 몸 둘레 알갱이 (직접 만듦)");
        }

        private static void SaveBlood(string key, Material mat, bool loop, int burst, float rate, (float min, float max) speed, (float min, float max) life)
        {
            var root = new GameObject(key);
            var ps = root.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = loop ? 1f : 0.2f;
            main.loop = loop;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 모델이 움직여도 떨어진 방울은 그 자리
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.min, life.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.16f);
            main.gravityModifier = 1.4f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.02f, 0.03f, 1f), new Color(0.62f, 0.04f, 0.05f, 1f));
            main.maxParticles = 30;
            var em = ps.emission;
            em.rateOverTime = rate;
            em.SetBursts(burst > 0 ? new[] { new ParticleSystem.Burst(0f, (short)burst, (short)(burst + 2)) } : new ParticleSystem.Burst[0]);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.7f, 0.7f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = root.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Stretch; // 떨어지는 방향으로 길쭉한 방울
            r.velocityScale = 0.06f;
            r.lengthScale = 1.2f;
            PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/{key}.prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[VfxSetup] {key} — 출혈 (직접 만듦)");
        }

        private static readonly Color BindGrey = new Color(0.62f, 0.68f, 0.82f); // 속박 잿빛 파랑
        private static Part BindCircle(float y) { var p = P(Hovl + "Magic circles/Magic circle.prefab", 0.3f, BindGrey, 0.75f, new Vector3(0f, y, 0f)); p.Drop = new[] { "DarkSparks", "CircleSparks", "Sparks", "Light" }; return p; }
        private static readonly Color Steel = new Color(0.6f, 0.72f, 0.9f); // 방어 약화: 강철 파랑
        private static readonly Color SteelBright = new Color(0.75f, 0.85f, 1f);
        private static readonly Color CritOrange = new Color(1f, 0.45f, 0.12f); // 크리티컬 숫자 색과 같은 붉은 주황
        private static readonly Color Sight = new Color(1f, 0.82f, 0.45f); // 약점 간파 조준: 흐린 금빛 (탐정)


        private static readonly Color Blood = new Color(0.9f, 0.05f, 0.05f); // 원래 붉은 알갱이 밝기 × 진한 피 색
        private static readonly Color PoisonDrop = new Color(0.62f, 0.95f, 0.12f); // 독 방울: 밝은 알갱이라 너무 밝으면 흰색으로 날아감
        private static readonly Color PoisonGas = new Color(1.2f, 1.6f, 0.35f); // 어두운 연기 텍스처 × 밝은 독 초록 = 탁하지 않은 독 김

        public static readonly (string type, Color tint)[] DebuffColors =
        {
            ("WeakBleed", new Color(0.62f, 0.06f, 0.06f)),
            ("Poison", new Color(0.5f, 0.62f, 0.14f)),
            ("Confusion", new Color(0.62f, 0.32f, 0.88f)),
            ("Bind", new Color(0.48f, 0.53f, 0.64f)),
            ("AttackDown", new Color(0.8f, 0.38f, 0.1f)),
            ("DefenseDown", new Color(0.32f, 0.48f, 0.8f)),
            ("EvasionRevealed", new Color(0.78f, 0.78f, 0.8f)),
        };

        public static void Build()
        {
            Directory.CreateDirectory(Out);
            foreach (var r in Recipes()) Save(r);
            BuildBlood();
            AssetDatabase.SaveAssets();
        }

        private static void Save(Recipe r)
        {
            var root = new GameObject(r.Key);
            foreach (var p in r.Parts)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(p.Path);
                if (src == null) { Debug.LogWarning($"[VfxSetup] {r.Key}: 없음 {p.Path}"); continue; }
                var part = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(part, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = p.Offset;
                if (p.Drop != null) foreach (var t in part.GetComponentsInChildren<Transform>(true)) if (t != part.transform && System.Array.IndexOf(p.Drop, t.name) >= 0) Object.DestroyImmediate(t.gameObject);
                part.transform.localScale = src.transform.localScale * p.Scale;
                foreach (var ps in part.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy; // 크기가 자식 파티클까지
                    main.loop = r.Loop;
                    main.playOnAwake = true;
                    if (r.DurationCap > 0f) main.duration = Mathf.Min(main.duration, r.DurationCap);
                    if (r.Period > 0f) main.duration = r.Period;
                    if (r.LifeMul != 1f) main.startLifetimeMultiplier *= r.LifeMul;
                    if (r.Gravity >= 0f) main.gravityModifierMultiplier = r.Gravity;
                    if (r.SizeMul != 1f) main.startSizeMultiplier *= r.SizeMul;
                    if (r.SingleFlash)
                    {
                        var em1 = ps.emission;
                        em1.rateOverTimeMultiplier = 0f;
                        em1.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                        main.startLifetimeMultiplier = Mathf.Max(main.startLifetimeMultiplier, 0.08f);
                    }
                    if (p.Tint.HasValue)
                    {
                        main.startColor = Tint(main.startColor, p.Tint.Value, p.Alpha);
                        var col = ps.colorOverLifetime;
                        if (col.enabled) col.color = Whiten(col.color);
                    }
                    else if (p.Alpha < 1f) main.startColor = Fade(main.startColor, p.Alpha);
                    if (r.Rate != 1f)
                    {
                        var em = ps.emission;
                        em.rateOverTimeMultiplier *= r.Rate;
                        em.rateOverDistanceMultiplier *= r.Rate;
                    }
                }
                if (r.SingleFlash) { foreach (var l in part.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l.gameObject); } // 총구 조명은 효과보다 오래 켜져 있어 뺌
                else foreach (var l in part.GetComponentsInChildren<Light>(true)) l.intensity *= 0.5f; // 에셋 조명은 고딕 화면에서 너무 밝음
            }
            PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/{r.Key}.prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[VfxSetup] {r.Key} — {r.Note}");
        }

        // 원래 색의 밝기 × 새 색 (원래 주황·붉은 효과를 다른 색으로)
        private static Color Mul(Color c, Color tint, float alpha)
        {
            float lum = Mathf.Max(c.r, c.g, c.b);
            return new Color(tint.r * lum, tint.g * lum, tint.b * lum, c.a * alpha);
        }

        private static ParticleSystem.MinMaxGradient Tint(ParticleSystem.MinMaxGradient g, Color tint, float alpha) => g.mode switch
        {
            ParticleSystemGradientMode.Color => new ParticleSystem.MinMaxGradient(Mul(g.color, tint, alpha)),
            ParticleSystemGradientMode.TwoColors => new ParticleSystem.MinMaxGradient(Mul(g.colorMin, tint, alpha), Mul(g.colorMax, tint, alpha)),
            _ => new ParticleSystem.MinMaxGradient(new Color(tint.r, tint.g, tint.b, alpha)),
        };

        private static ParticleSystem.MinMaxGradient Fade(ParticleSystem.MinMaxGradient g, float alpha)
        {
            Color F(Color c) => new Color(c.r, c.g, c.b, c.a * alpha);
            return g.mode switch
            {
                ParticleSystemGradientMode.Color => new ParticleSystem.MinMaxGradient(F(g.color)),
                ParticleSystemGradientMode.TwoColors => new ParticleSystem.MinMaxGradient(F(g.colorMin), F(g.colorMax)),
                _ => g,
            };
        }

        // 수명 따라 바뀌는 색은 밝기·투명도만 남기고 색은 뺌 (새 색이 그대로 보이게)
        private static ParticleSystem.MinMaxGradient Whiten(ParticleSystem.MinMaxGradient g)
        {
            Gradient W(Gradient src)
            {
                if (src == null) return null;
                var keys = src.colorKeys;
                for (int i = 0; i < keys.Length; i++) { float v = Mathf.Max(keys[i].color.r, keys[i].color.g, keys[i].color.b); keys[i].color = new Color(v, v, v); }
                var n = new Gradient(); n.SetKeys(keys, src.alphaKeys); return n;
            }
            return g.mode switch
            {
                ParticleSystemGradientMode.Gradient => new ParticleSystem.MinMaxGradient(W(g.gradient)),
                ParticleSystemGradientMode.TwoGradients => new ParticleSystem.MinMaxGradient(W(g.gradientMin), W(g.gradientMax)),
                _ => g,
            };
        }
    }
}
