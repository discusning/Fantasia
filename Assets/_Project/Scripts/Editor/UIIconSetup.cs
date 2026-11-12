using System.Linq;
using Fantasia.Data;
using Fantasia.UI;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 받은 아이콘 Z01~Z44(Assets/_Project/UI/Sprites/Icons)를 UI 테마·직업·소모품 칸에 연결한다.
    // 그림을 같은 파일 이름으로 바꿔 넣으면 다시 실행할 필요 없이 그대로 바뀐다 (연결은 파일 기준).
    public static class UIIconSetup
    {
        private const string Dir = "Assets/_Project/UI/Sprites/Icons/";

        [MenuItem("Fantasia/UI/Apply Icon Set (Z01-Z44)")]
        public static void Apply()
        {
            // 게이지는 좌우 둥근 끝이 찌그러지지 않게 9-slice 경계 (1024×128 그림 기준)
            SetBorder("Z38_공통_게이지_바탕.png", new Vector4(48, 40, 48, 40));
            SetBorder("Z39_공통_게이지_채움.png", new Vector4(48, 40, 48, 40));

            // 상단 바 조각 (N07 배너를 잘라 만든 것 — HUD/Cut): 나무판은 좌우만 늘어나게 (왼쪽 초상 칸 190px·오른쪽 끝 40px 고정)
            const string Cut = "Assets/_Project/UI/Sprites/HUD/Cut/";
            SetBorderAt(Cut + "TopBar_Plank.png", new Vector4(190, 0, 40, 0));
            SetBorderAt(Cut + "TopBar_RegionTab.png", new Vector4(40, 0, 40, 0));
            // 11/10 받은 UI 수정본 (Docs/Art/UI_Revision_1110) — 투명 여백을 잘라 낸 것. 테두리: 왼·아래·오른·위
            SetBorderAt(Cut + "TopBar2_Plank.png", new Vector4(200, 0, 50, 0));     // 왼쪽 초상 칸까지 고정
            SetBorderAt(Cut + "TopBar2_RegionTab.png", new Vector4(40, 0, 40, 0));
            SetBorderAt("Assets/_Project/UI/Sprites/HUD/N21_체력마나_바.png", new Vector4(200, 0, 50, 0)); // 왼쪽 네모 칸까지 고정, 좌우로만 늘어남
            SetBorderAt(Cut + "InnerPlate.png", new Vector4(40, 40, 40, 40)); // 11/11 U3 안쪽 판: 구리 테두리 약 33px
            SetBorderAt(Cut + "Timeline2_Slot.png", new Vector4(24, 24, 24, 24)); // 11/11 U4 얼굴 액자로도 씀 (순서 띠는 Simple로 그려 영향 없음)
            SetBorderAt("Assets/_Project/UI/Sprites/Panels/Panel2_Window.png", new Vector4(180, 285, 180, 300)); // 모서리 장식 포함
            SetBorderAt("Assets/_Project/UI/Sprites/Buttons/Button2_Plain.png", new Vector4(24, 24, 24, 24));
            SetBorderAt(Cut + "Timeline2_Backing.png", new Vector4(50, 0, 50, 0));

            var theme = AssetDatabase.LoadAssetAtPath<UITheme>("Assets/_Project/Resources/UITheme.asset");
            if (theme == null) { Debug.LogError("[UIIconSetup] UITheme.asset 없음"); return; }
            theme.BloodTrailIcon = S("Z01"); theme.SoulIcon = S("Z02"); theme.CentIcon = S("Z03"); theme.LpIcon = S("Z04");
            theme.StrengthIcon = S("Z05"); theme.SpeedIcon = S("Z06"); theme.VitalityIcon = S("Z07");
            theme.IntelligenceIcon = S("Z08"); theme.LuckIcon = S("Z09"); theme.PerceptionIcon = S("Z10");
            theme.QuestIcon = S("Z15"); theme.EvidenceIcon = S("Z16"); theme.TreasureIcon = S("Z17");
            theme.HubMarker = S("Z23"); theme.HoundMarker = S("Z24"); theme.BossMarker = S("Z25");
            theme.EnemyMarker = S("Z19"); // 공용 적 표식 — 새 그림이 오면 그 파일로 바꿀 것 (UI 솔루션 Q7)
            theme.RecoveryMarker = S("Z26"); theme.EventMarker = S("Z27"); theme.ExitMarker = S("Z28");
            theme.FocusPip = S("Z29"); theme.FocusPipEmpty = S("Z30");
            theme.ObjectiveOpen = S("Z33"); theme.ObjectiveDone = S("Z34");
            theme.QuickSlotFrame = S("Z35"); theme.QuickSlotSelected = S("Z36");
            theme.LevelBadge = A(Dir + "N22_레벨_원.png") ?? S("Z37"); // 11/11 받은 청동 원 (팀장: 레벨 UI)
            theme.ShieldIcon = A(Dir + "Shield_방어막_임시.png"); // 11/11 방어 보호막 (임시 — 개발이 그림)
            // 11/12 메인 화면 (디자인): 로고는 가장자리 거의 투명한 픽셀에 빨간 색이 묻어 있어 Alpha Is Transparency 켬 (크기 줄일 때 번짐 막기)
            const string Main = "Assets/_Project/UI/Sprites/MainMenu/";
            MainMenuImport(Main + "Main_게임로고.png", transparent: true);
            MainMenuImport(Main + "Main_메인배경.png", transparent: false);
            theme.MainLogo = A(Main + "Main_게임로고.png");
            theme.MainBackground = A(Main + "Main_메인배경.png");
            theme.BarBack = S("Z38"); theme.BarFill = S("Z39");
            theme.Banner = A(Cut + "TopBar_RegionTab.png"); // 11/11 U1 이름 줄 — 받은 나무 탭 다시 씀
            theme.PanelFill = A(Cut + "InnerPlate.png");     // 11/11 U3 안쪽 판 — 받은 N09 내용패널(여백 자름)
            theme.SlotIcon = A(Cut + "Ring_Tintable.png");  // 11/11 U10 주사위 칸 표식 — 받은 레벨 원(N22)을 가운데 밝게 바꾼 칠용 (성공=초록·실패=빨강)
            theme.HpMpFrame = A("Assets/_Project/UI/Sprites/HUD/N21_체력마나_바.png"); // 11/11 받은 체력·마나 바 틀 (FramedBar)
            // 상단 바 그림 (UI 적용 ①)
            theme.TopBarFrame = A(Cut + "TopBar2_Plank.png");     // 11/10 새 그림 (예전 N07 조각: TopBar_*.png)
            theme.TopBarCircle = A(Cut + "TopBar2_Circle.png");
            theme.RegionBanner = A(Cut + "TopBar2_RegionTab.png");
            theme.PanelFrame = A("Assets/_Project/UI/Sprites/Panels/Panel2_Window.png"); // 큰 창 틀 (UGUIKit.AddWindowFrame)
            theme.Button = A("Assets/_Project/UI/Sprites/Buttons/Button2_Plain.png");   // 모든 버튼 (UGUIKit.ApplyButtonArt)
            theme.WindowHeader = A("Assets/_Project/UI/Sprites/Panels/Panel2_Header.png"); // 창 제목 이름 칸 (UGUIKit.WindowHeader)
            // 턴 순서 패키지(Fantasia_TurnOrder)의 그림만 잘라 씀 — 칸 수·점선(다음 라운드)·아군/적 색은 기존 순서 띠 그대로
            theme.TimelineFrame = A(Cut + "Timeline2_Backing.png");
            theme.TimelineSlot = A(Cut + "Timeline2_Slot.png");
            theme.TimelineCurrent = A(Cut + "Timeline2_Current.png");
            theme.DaySun = A(Cut + "Sun.png");
            theme.NightMoon = A(Cut + "Moon.png");
            theme.SoulFrame = A(Cut + "Soul_Frame.png");
            theme.SoulFill = A(Cut + "Soul_Fill.png");
            // 대화 인물 (UI 솔루션 Q8) — 받은 JPG(N18~N20)는 액자 안이라 그대로 씀
            theme.SpeakerPortraits = new[]
            {
                new SpeakerPortrait { Speaker = "점술마녀", Sprite = A("Assets/_Project/UI/Sprites/Portraits/N18_대화인물_점술마녀_3x4.png") ?? P("N18") }, // 11/10 3:4 새 그림
                new SpeakerPortrait { Speaker = "제임스", Sprite = A("Assets/_Project/UI/Sprites/Portraits/N19_대화인물_제임스_3x4.png") ?? P("N19") },
                new SpeakerPortrait { Speaker = "아벨 모로", Sprite = A("Assets/_Project/UI/Sprites/Portraits/N20_대화인물_아벨모로_3x4.png") ?? P("N20") }, // 11/11 팀장: 상처완화 대체본을 3:4로 자름
            };
            EditorUtility.SetDirty(theme);

            // 직업 (Z20~Z22)
            SetJob("Job_명탐정", "Z20"); SetJob("Job_역병의사", "Z21"); SetJob("Job_저격수", "Z22");

            // 소모품 (Z11~Z14)
            var catalog = ItemCatalog.Current;
            SetConsumable(catalog, ConsumableIds.Bandage, "Z11");
            SetConsumable(catalog, ConsumableIds.LifeElixir, "Z12");
            SetConsumable(catalog, ConsumableIds.ManaElixir, "Z13");
            SetConsumable(catalog, ConsumableIds.Antidote, "Z14");

            AssetDatabase.SaveAssets();
            Debug.Log("[UIIconSetup] 아이콘 연결 완료");
        }

        private static Sprite S(string code)
        {
            var path = AssetDatabase.FindAssets("t:Sprite", new[] { Dir.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileName(p).StartsWith(code + "_"));
            var sprite = path != null ? AssetDatabase.LoadAssetAtPath<Sprite>(path) : null;
            if (sprite == null) Debug.LogWarning($"[UIIconSetup] {code} 그림 없음 — 그 자리는 글자로 보임");
            return sprite;
        }

        private static Sprite P(string code)
        {
            var path = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/_Project/UI/Sprites/Portraits" })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileName(p).StartsWith(code + "_"));
            var sprite = path != null ? AssetDatabase.LoadAssetAtPath<Sprite>(path) : null;
            if (sprite == null) Debug.LogWarning($"[UIIconSetup] {code} 인물 그림 없음");
            return sprite;
        }

        private static Sprite A(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning($"[UIIconSetup] 그림 없음: {path}");
            return sprite;
        }

        // 큰 그림(메인 화면) — 스프라이트, 밉맵 없음, 2048까지, 압축은 고품질
        private static void MainMenuImport(string path, bool transparent)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            bool changed = importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled || importer.alphaIsTransparency != transparent || importer.maxTextureSize != 2048 || importer.textureCompression != TextureImporterCompression.CompressedHQ;
            if (!changed) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = transparent;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static void SetBorderAt(string path, Vector4 border)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.spriteBorder == border) return;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }

        private static void SetBorder(string file, Vector4 border)
        {
            var importer = AssetImporter.GetAtPath(Dir + file) as TextureImporter;
            if (importer == null || importer.spriteBorder == border) return;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }

        private static void SetJob(string asset, string code)
        {
            var job = AssetDatabase.LoadAssetAtPath<JobDefinition>($"Assets/_Project/Data/Jobs/{asset}.asset");
            if (job == null) return;
            job.Icon = S(code);
            EditorUtility.SetDirty(job);
        }

        private static void SetConsumable(ItemCatalog catalog, string id, string code)
        {
            var c = catalog != null ? catalog.Consumable(id) : null;
            if (c == null) return;
            c.Icon = S(code);
            EditorUtility.SetDirty(c);
        }
    }
}
