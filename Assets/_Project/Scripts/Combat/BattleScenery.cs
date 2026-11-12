using System.Linq;
using UnityEngine;

namespace Fantasia.Combat
{
    // 전투 장소 꾸미기 (2026-11-11, 기획 회신 02).
    // - 아벨 모로전(보스): 조명만 낮춤 — 어둡고 차가운 빛, 바탕·바닥도 어둡게.
    // - 일반 전투: 런던 거리 프로토타입 — 이미 받은 집·나무·울타리·돌담을 바닥 뒤(적 뒤쪽·먼 쪽 두 줄)에 세움. 11/15 기획 검수에서 빼면 StreetEnabled = false.
    // 아군은 x −2.6, 적은 x +2.6 줄 (BattleController.LineX). 카메라는 (−9, 7.5, −12.5)에서 +x·+z 쪽을 내려다봄 → 배경은 x +10 줄과 z +11.5 줄.
    public static class BattleScenery
    {
        public static bool StreetEnabled = true;
        private static bool Plain => System.Environment.GetCommandLineArgs().Contains("-battle-plain"); // 전후 비교 사진용: 예전 그대로 (보스 조명도)

        // 이름, 위치(x, z), 바라보는 쪽 — 집 정면이 전투 쪽을 보게
        private static readonly (string prefab, float x, float z, float faceYaw)[] Street =
        {
            // 적 뒤 줄 (x +10.5, 정면이 −x)
            ("House_B", 10.5f, -9.5f, -90f),
            ("House_A", 10.5f, -3.8f, -90f),
            ("Trees_B", 11f, 1.6f, -90f),
            ("House_C", 10.5f, 7f, -90f),
            // 먼 쪽 줄 (z +11.5, 정면이 −z)
            ("House_A", -11f, 11.5f, 180f),
            ("House_C", -5f, 11.5f, 180f),
            ("Trees_A", 0.5f, 12f, 180f),
            ("House_B", 5.5f, 11.5f, 180f),
            // 모서리 뒤 돌 아치 (멀리, 비스듬히)
            ("StoneArch", 17f, 19f, -135f),
            // 낮은 울타리·돌담 (집 앞, 싸움 자리와는 떨어지게)
            ("Fence", 7.2f, -7f, -90f),
            ("Fence", 7.2f, -5.3f, -90f),
            ("Fence", 7.2f, 4.6f, -90f),
            ("Fence", 7.2f, 6.3f, -90f),
            ("StoneWall", -8f, 8.4f, 180f),
            ("StoneWall", 2.5f, 8.6f, 180f),
        };

        // 11/12 C1 런던 시내: 받은 6종 (돌길·가로등 2종·손수레·하수구·나무 울타리). 이름, 위치(x, 높이 y, z), 바라보는 쪽.
        // 돌길(4×2m)을 집 앞에 이어 깔아 인도를 만들고, 그 위에 가로등. 카메라 → 적(x +2.6) 시선 너머(x 6~7, z 3~6)는 비움.
        // 가로등 불빛은 아직 없음 (기획 OK 뒤, Docs/Planning/StreetLamp_시간대_제안.md).
        private static readonly (string prefab, float x, float y, float z, float faceYaw)[] LondonStreet =
        {
            // 먼 쪽 인도 (담장 z +8.4 앞, 길게 x 방향)
            ("Cobble", -12f, 0f, 6.9f, 0f), ("Cobble", -8.4f, 0f, 6.9f, 0f), ("Cobble", -4.8f, 0f, 6.9f, 0f), // 판 끝이 둥글어 3.6m 간격으로 겹침
            ("Cobble", -1.2f, 0f, 6.9f, 0f), ("Cobble", 2.4f, 0f, 6.9f, 0f), ("Cobble", 5.0f, 0f, 6.9f, 0f),
            // 오른쪽 인도 (울타리 x +7.2 앞, 길게 z 방향)
            ("Cobble", 6.2f, 0f, -10f, 90f), ("Cobble", 6.2f, 0f, -6.4f, 90f), ("Cobble", 6.2f, 0f, -2.8f, 90f),
            ("Cobble", 6.2f, 0f, 0.8f, 90f), ("Cobble", 6.2f, 0f, 4.0f, 90f),
            // 가로등 (인도 위, 불 없음)
            ("StreetLamp", -6.5f, 0f, 6.2f, 180f),
            ("StreetLamp", 1.5f, 0f, 6.2f, 180f),
            ("StreetLamp", 5.5f, 0f, -7.8f, -90f),
            ("StreetLamp", 5.5f, 0f, -2.0f, -90f),
            // 벽 가로등 (집 정면 벽, 높이 2.6m)
            ("WallLamp", 8.0f, 2.6f, -2.2f, -90f),
            ("WallLamp", -3.4f, 2.6f, 9.2f, 180f),
            // 길가 소품
            ("Cart", 7.6f, 0f, -4.8f, -70f),
            ("Sewer", -1.6f, 0f, 6.4f, 180f),
            ("WoodFence", -10.4f, 0f, 8.4f, 180f),
            ("WoodFence", -12.6f, 0f, 8.4f, 180f),
        };

        public static void Apply(bool boss)
        {
            if (Plain) return;
            var light = FindSun();
            var ground = GameObject.Find("Arena Ground");
            var cam = Camera.main;
            if (boss) { ApplyBossLighting(light, ground, cam); if (MorgueEnabled) BuildMorgue(); return; }
            if (!StreetEnabled) return;

            // 바닥: 흙빛 → 어두운 돌길 색
            if (ground != null) Tint(ground, new Color(0.34f, 0.32f, 0.31f));
            // 11/11 팀장 결정: 일반 전투도 보드 시간대(월드 라운드)를 따름 — 빛 색·세기·각도·먼 곳 색·안개
            var session = Fantasia.Core.BoardSession.Instance;
            int timeIndex = Fantasia.World.TimeOfDayLook.IndexFor(session != null ? session.World.WorldRound : 2);
            var look = Fantasia.World.TimeOfDayLook.All[timeIndex];
            // 먼 곳은 안개로 바탕색에 녹임 (바닥 끝이 안 보이게). 낮 = 예전 색 (0.17, 0.15, 0.17)
            Color haze = new Color(Mathf.Min(1f, look.Outside.r * 2.25f), Mathf.Min(1f, look.Outside.g * 2.3f), Mathf.Min(1f, look.Outside.b * 2.4f));
            if (cam != null) cam.backgroundColor = haze;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = haze;
            RenderSettings.fogStartDistance = Mathf.Lerp(26f, 18f, look.Fog);
            RenderSettings.fogEndDistance = Mathf.Lerp(58f, 46f, look.Fog);
            RenderSettings.ambientIntensity *= look.AmbientMul;
            if (light != null)
            {
                light.shadows = LightShadows.Soft; // 집 그림자가 바닥에 떨어지게
                light.color *= look.Sun;
                light.intensity *= look.SunMul;
                var e = light.transform.eulerAngles;
                light.transform.rotation = Quaternion.Euler(e.x + look.ElevDelta, e.y + look.YawDelta, e.z);
            }

            var parent = new GameObject("Battle Scenery (London Street)").transform;
            foreach (var (name, x, z, yaw) in Street)
            {
                var prefab = Resources.Load<GameObject>("BattleArt/" + name);
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab, new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw + 180f, 0f), parent);
                go.name = name;
            }
            foreach (var (name, x, y, z, yaw) in LondonStreet)
            {
                var prefab = Resources.Load<GameObject>("BattleArt/" + name);
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab, new Vector3(x, y, z), Quaternion.Euler(0f, yaw + 180f, 0f), parent);
                go.name = name;
            }
            // 11/12 가로등 불빛: 시간대별 세기 (아침 25 · 낮 0 · 저녁 70 하나씩 켜짐 · 새벽 100%) — StreetLampGlow
            StreetLampGlow.ApplyAll(timeIndex, cam);
        }

        // 아벨 모로전: 기획 회신 02 — 전용 배경 대신 조명만 낮춤
        private static void ApplyBossLighting(Light light, GameObject ground, Camera cam)
        {
            if (light != null)
            {
                light.intensity = 0.55f;
                light.color = new Color(0.72f, 0.76f, 0.95f);
                light.shadows = LightShadows.Soft;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.12f, 0.12f, 0.17f);
            RenderSettings.ambientEquatorColor = new Color(0.07f, 0.06f, 0.08f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.02f, 0.02f);
            if (cam != null) cam.backgroundColor = new Color(0.06f, 0.05f, 0.07f);
            if (ground != null) Tint(ground, new Color(0.24f, 0.2f, 0.2f));
            // 바닥 끝 선이 어두운 바탕에 드러나서 안개로 녹임
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.06f, 0.05f, 0.07f);
            RenderSettings.fogStartDistance = 16f;
            RenderSettings.fogEndDistance = 34f;
        }

        // 11/13 C2 폐검시소 (아벨 모로전) — 배치 연구 Docs/Planning/폐검시소_전투배경_배치연구_1112.md 3절 (팀장 OK).
        // 1차(구조물 8개만)는 팀장 지적 "너무 비어 있음" → 실제 전투 카메라 안으로 당기고 촘촘하게:
        //  · 방: 돌바닥 텍스처 + 뒤 벽(z +10)·오른쪽 벽(x +12), 벽 등 2개
        //  · 보스 뒤 검시실: 검시대 5개(시체 3) 두 줄, 각 줄 위 차가운 등
        //  · 뒤 벽 따라 약품장·기록대·세척대·문·해골, 오른쪽 벽 따라 약품장·세척대
        // 싸움 자리(x −2.6 ~ +2.6, z −2.2 ~ +2.2)와 카메라(−9, 7.5, −12.5) → 적 시선, 카메라 쪽(왼쪽 앞)은 비움.
        // 해골 조형은 받침대에 선 해부 모형이라 매달지 않고 바닥에 세움.
        public static bool MorgueEnabled = true;
        public const float MorgueTableTop = 0.77f; // 검시대 높이(길이 2.3m 맞춤 뒤) — 시체를 올리는 높이
        private const float BackWallZ = 10f, RightWallX = 12f;
        private static readonly (string prefab, float x, float y, float z, float faceYaw)[] Morgue =
        {
            // 보스 뒤 검시실: 두 줄 (x 7.2 · x 9.8), 긴 쪽이 z 방향
            ("Morgue_Table", 7.2f, 0f, 0.6f, -90f), ("Morgue_Corpse", 7.2f, MorgueTableTop, 0.6f, -90f),
            ("Morgue_Table", 7.2f, 0f, 4.4f, -90f), ("Morgue_Corpse", 7.2f, MorgueTableTop, 4.4f, -90f),
            ("Morgue_Table", 7.2f, 0f, -3.2f, -90f),
            ("Morgue_Table", 9.8f, 0f, 2.5f, -90f), ("Morgue_Corpse", 9.8f, MorgueTableTop, 2.5f, 90f),
            ("Morgue_Table", 9.8f, 0f, -1.3f, -90f),
            // 뒤 벽 따라 (정면이 −z)
            ("Morgue_Cabinet", -8.6f, 0f, 9.3f, 180f), ("Morgue_Cabinet", -7.6f, 0f, 9.3f, 180f),
            ("Morgue_Desk", -5f, 0f, 9.1f, 180f),
            ("Morgue_Basin", -1.8f, 0f, 9.2f, 180f),
            ("Morgue_Cabinet", 0.8f, 0f, 9.3f, 180f), ("Morgue_Cabinet", 1.8f, 0f, 9.3f, 180f), ("Morgue_Cabinet", 2.8f, 0f, 9.3f, 180f),
            ("Morgue_Door", 5.4f, 0f, 9.6f, 180f),
            ("Morgue_Skeleton", 8f, 0f, 8.6f, -150f),
            ("Morgue_Desk", 10.4f, 0f, 8.9f, -135f),
            // 오른쪽 벽 따라 (정면이 −x)
            ("Morgue_Cabinet", 11.3f, 0f, 6.2f, -90f),
            ("Morgue_Basin", 11.2f, 0f, -4.6f, -90f),
            ("Morgue_Cabinet", 11.3f, 0f, -7.6f, -90f), ("Morgue_Cabinet", 11.3f, 0f, -8.6f, -90f),
            // 가림막: 검시실 앞뒤 (싸움 자리 밖)
            ("Morgue_Screen", 5.2f, 0f, 6.9f, -45f),
            ("Morgue_Screen", 8.2f, 0f, -6.2f, -90f),
            // 가운데 뒤 (보스와 뒤 벽 사이 빈 곳)
            ("Morgue_Basin", 1.6f, 0f, 6.2f, 160f),
            // 왼쪽 (아군 뒤) — 낮은 것만, 카메라를 막지 않게
            ("Morgue_Basin", -8.2f, 0f, -4.6f, 90f),
            ("Morgue_Table", -9.4f, 0f, 1.6f, 0f), ("Morgue_Corpse", -9.4f, MorgueTableTop, 1.6f, 0f),
            ("Morgue_Desk", -7.2f, 0f, 5.6f, 150f),
            ("WallLamp", -3.4f, 2.6f, BackWallZ - 0.25f, 180f), ("WallLamp", 4f, 2.6f, BackWallZ - 0.25f, 180f),
        };
        // 11/13 핏자국 (팀장 "과하지 않게"): 검시대 둘레 6곳만. 종류 0 웅덩이 · 1 튄 자국 · 2 끌린 자국 · 3 방울 (Editor/BloodDecalSetup)
        // 종류, 위치(x, 높이 y, z), 크기(m), 돌림(도). 높이 0.012 = 바닥 바로 위, 0.805 = 빈 검시대 윗면
        private static readonly (int kind, float x, float y, float z, float size, float yaw)[] MorgueBlood =
        {
            (0, 6.3f, 0.012f, -0.9f, 1.3f, 30f),    // 보스 바로 뒤 검시대 앞 웅덩이 (보스에 안 가리게)
            (3, 6.3f, 0.014f, 1.5f, 0.8f, 0f),      // 그 앞쪽 떨어진 방울
            (2, 7.0f, 0.013f, 6.0f, 1.7f, 80f),     // 둘째 검시대에서 뒤쪽으로 끌린 자국
            (1, 10.6f, 0.012f, 3.0f, 1.0f, 200f),   // 안쪽 줄 검시대 옆 튄 자국
            (3, 7.2f, 0.805f, -3.2f, 0.9f, 15f),    // 빈 검시대 윗면 얼룩
            (0, -8.6f, 0.012f, 1.2f, 0.9f, -40f),   // 왼쪽 검시대 아래
        };

        // 검시대 위 차가운 등 (빛 웅덩이) — 나머지는 보스 조명 그대로 어둡게
        private static readonly Vector3[] MorgueLamps = { new(7.2f, 3.2f, 0.6f), new(8.5f, 3.2f, 4.2f), new(9.8f, 3.2f, -1.3f), new(-9.4f, 3.2f, 1.6f) };

        public static int DevMorgueCount { get; private set; }

        private static void BuildMorgue()
        {
            var parent = new GameObject("Battle Scenery (Morgue)").transform;
            DevMorgueCount = 0;
            foreach (var (name, x, y, z, yaw) in Morgue)
            {
                var prefab = Resources.Load<GameObject>("BattleArt/" + name);
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab, new Vector3(x, y, z), Quaternion.Euler(0f, yaw + 180f, 0f), parent);
                go.name = name;
                DevMorgueCount++;
            }
            foreach (var (kind, bx, by, bz, size, byaw) in MorgueBlood)
            {
                var prefab = Resources.Load<GameObject>("BattleArt/Blood_" + kind);
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab, new Vector3(bx, by, bz), Quaternion.Euler(0f, byaw, 0f), parent);
                go.transform.localScale = new Vector3(size, 1f, size);
                go.name = "Blood_" + kind;
            }
            foreach (var p in MorgueLamps)
            {
                var lamp = new GameObject("Morgue Lamp").AddComponent<Light>();
                lamp.transform.SetParent(parent, false);
                lamp.transform.position = p;
                lamp.type = LightType.Point;
                lamp.color = new Color(0.78f, 0.88f, 1f);
                lamp.intensity = 2.6f;
                lamp.range = 7f;
                lamp.shadows = LightShadows.None;
            }

            // 바닥: 돌판 (한 그림 2m, 큰 돌 1m)
            var ground = GameObject.Find("Arena Ground");
            var floorTex = Resources.Load<Texture2D>("BattleArt/MorgueFloor");
            if (ground != null && floorTex != null && ground.TryGetComponent<Renderer>(out var gr))
            {
                var m = gr.material;
                m.mainTexture = floorTex;
                m.mainTextureScale = new Vector2(15f, 15f); // 바닥 30m
                m.color = new Color(0.62f, 0.6f, 0.62f);
                m.SetFloat("_Glossiness", 0.25f);
            }
            // 벽: 뒤(z +10)·오른쪽(x +12), 높이 4.5m — 안개에 위쪽이 녹아 천장 없이도 실내처럼
            var wallTex = Resources.Load<Texture2D>("BattleArt/MorgueWall");
            Wall(parent, wallTex, new Vector3(-1.5f, 2.25f, BackWallZ + 0.2f), new Vector3(27f, 4.5f, 0.4f));
            Wall(parent, wallTex, new Vector3(RightWallX + 0.2f, 2.25f, -2.5f), new Vector3(0.4f, 4.5f, 25f));
            // 문은 벽 앞에 붙임 → 문 뒤로 벽이 보이게 위 깊이 순서는 그대로
            RenderSettings.fogEndDistance = 40f; // 뒤 벽(약 25m)이 완전히 묻히지 않게
        }

        private static void Wall(Transform parent, Texture2D tex, Vector3 pos, Vector3 size)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = "Morgue Wall";
            Object.Destroy(w.GetComponent<Collider>());
            w.transform.SetParent(parent, false);
            w.transform.position = pos;
            w.transform.localScale = size;
            var m = new Material(Shader.Find("Standard"));
            if (tex != null) m.mainTexture = tex;
            m.mainTextureScale = new Vector2(Mathf.Max(size.x, size.z) / 2f, size.y / 2f); // 한 그림 2m
            m.color = new Color(0.55f, 0.52f, 0.5f);
            m.SetFloat("_Glossiness", 0.1f);
            w.GetComponent<Renderer>().sharedMaterial = m;
        }

        private static Light FindSun()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return l;
            return null;
        }

        private static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.material.color = c;
        }
    }
}
