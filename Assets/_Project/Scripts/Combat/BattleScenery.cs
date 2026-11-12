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
            if (boss) { ApplyBossLighting(light, ground, cam); return; }
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
