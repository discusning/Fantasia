using UnityEditor;

namespace Fantasia.Editor
{
    // 셀프 테스트 전부 한 번에 (배치: -executeMethod Fantasia.Editor.AllSelfTests.RunAll)
    public static class AllSelfTests
    {
        [MenuItem("Fantasia/Data/Run All Self-Tests")]
        public static void RunAll()
        {
            GameDataSeed.SeedAndValidate(); // 시드 + 검증 + 파티·월드·조우·아이템
            AttackResolverSelfTest.Run();
            BattleQueueSelfTest.Run();
            StatusEffectSelfTest.Run();
            SkillSelfTest.Run();
            DefeatSelfTest.Run();
            BoardSessionSelfTest.Run();
            PathfindingSelfTest.Run();
            WorldMapSelfTest.Run();
            BossSelfTest.Run();
            BloodHuntSelfTest.Run();
            ShopSelfTest.Run();
            SaveSelfTest.Run();
        }
    }
}
