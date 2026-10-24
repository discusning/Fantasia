using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // D20 도시 상점 데이터 — Docs/Planning/City_Shop.md 판매표 그대로 (본편 3도시: 런던·베르사유·로마).
    // 표 한 줄: "상품명|런던|베르사유|로마|단계|한정". 0이면 그 도시에서 안 팖. 프랑크푸르트·베네치아는 3차(확장)라 제외.
    public static class ShopDataSeed
    {
        private const string ShopDir = "Assets/_Project/Data/Shops";

        private static readonly string[] Table =
        {
            // §1 전 도시 공통 보급품 12종 (도시 해금 시)
            "압박 붕대|5|5|5|Base|", "생명의 영약|3|3|3|Base|", "마력의 영약|3|3|3|Base|", "집중의 향초|3|3|3|Base|",
            "해독제|3|3|3|Base|", "지혈 연고|2|2|2|Base|", "정신 안정 염|2|2|2|Base|", "성수|2|2|2|Base|",
            "휴대 수리 도구|1|1|1|Base|", "룬 기관차 탑승권|2|2|2|Base|", "탈출권|1|1|1|Base|", "연금술 시약|8|8|8|Base|",
            // §2 지역 보급품 5종
            "냉각 수은 연고|0|2|2|Base|", "안개 야영 세트|1|0|1|Base|", "텔레포트권|0|1|1|Base|", "연금술 화염병|2|2|2|Base|", "안개 연막병|1|1|0|Base|",
            // §4 2막 이후 고급 보급품 10종
            "고급 생명의 영약|2|2|2|Act2|", "고급 마력의 영약|2|2|2|Act2|", "백색 로즈마리|0|1|1|Act2|", "마녀의 뼈|0|1|1|Act2|",
            "만능 정화제|0|1|1|Act2|", "봉합 수리 도구|0|1|1|Act2|", "철심 강장제|0|0|1|Act2|", "통찰의 증류액|0|1|1|Act2|",
            "신속의 앰플|0|0|1|Act2|", "성유 은탄|0|1|1|Act2|",
            // §5 한정품
            "생명의 월계관|1|1|1|Base|limited", "마리의 집문서|0|0|1|Castle|limited",
            // §7 노말 무기 (Lv1)
            "탐정의 접이식 단검|1|1|1|Base|", "야경대 세이버|1|1|1|Base|", "교단 사냥 대검|1|1|1|Base|", "수비대 장창|1|1|1|Base|",
            "안개 사냥 활|1|1|1|Base|", "부싯돌 권총|1|1|1|Base|", "야경대 화승총|1|1|1|Base|", "검역소 지팡이|1|1|1|Base|",
            "초급 역병 처방서|1|1|1|Base|", "거리악사의 바이올린|1|1|1|Base|",
            // §8 희귀 무기 (Lv4, 2막 진입 후)
            "은도금 해부단검|1|1|1|Act2|", "백은 수사검|0|1|1|Act2|", "성유강 대검|0|0|1|Act2|", "룬각인 미늘창|0|0|1|Act2|",
            "연금술 석궁|0|0|1|Act2|", "은탄 수사권총|1|1|1|Act2|", "눈금 조준 장총|0|0|1|Act2|", "연금술 룬 스태프|1|1|1|Act2|",
            "금지된 해부학|0|1|1|Act2|", "은현 추모 바이올린|0|1|1|Act2|",
            // §9 에픽 무기 (Lv7, 고성 진입 후)
            "검은 장미 단검|0|0|1|Castle|", "월식의 룬검|0|0|1|Castle|", "종언의 처형검|0|0|1|Castle|", "가고일 파쇄창|0|0|1|Castle|",
            "은빛 송곳 석궁|0|0|1|Castle|", "룬 격발 권총|0|0|1|Castle|", "귀족 사냥 장총|0|0|1|Castle|", "백야의 성역봉|0|0|1|Castle|",
            "봉인된 연금술서|0|0|1|Castle|", "레퀴엠의 오르골|0|0|1|Castle|",
            // §10 방어구·장신구 14종
            "인부의 두꺼운 코트|2|2|2|Base|", "야경대 삼각모|2|2|2|Base|", "구리 힘의 반지|1|1|1|Base|", "작은 십자가 부적|1|1|1|Base|",
            "수사대 가죽 코트|1|1|0|Act2|", "검역소 방호 외투|0|1|1|Act2|", "역병 의사의 가면|0|1|1|Act2|", "해부학자의 반지|0|1|1|Act2|",
            "은빛 조준 반지|1|1|1|Act2|", "검역소 목걸이|0|1|1|Act2|",
            "교단 사냥 갑주|0|0|1|Castle|", "은실 장막 로브|0|0|1|Castle|", "룬 각인 모자|0|0|1|Castle|", "야경대의 휘장|0|0|1|Castle|",
        };

        private static readonly (string id, string name, string hub, string landmark)[] Cities =
        {
            ("city.london", "런던", "S05", "그리니치 전망대·수사대 상점"),
            ("city.versailles", "베르사유", "F18", "궁전 외곽 시민 상가"),
            ("city.rome", "로마", "S31", "제국 광장·성소 시장"),
        };

        [MenuItem("Fantasia/Data/Seed Shop Data")]
        public static void SeedAll()
        {
            var catalog = ItemCatalog.Current;
            if (catalog == null) { Debug.LogError("[ShopDataSeed] ItemCatalog 없음 — ItemDataSeed 먼저"); return; }
            Directory.CreateDirectory(ShopDir);

            var shops = new List<CityShopDefinition>();
            int missing = 0;
            for (int c = 0; c < Cities.Length; c++)
            {
                var (id, name, hub, landmark) = Cities[c];
                string path = $"{ShopDir}/Shop_{id.Substring(5)}.asset";
                var shop = AssetDatabase.LoadAssetAtPath<CityShopDefinition>(path);
                if (shop == null) { shop = ScriptableObject.CreateInstance<CityShopDefinition>(); AssetDatabase.CreateAsset(shop, path); }
                shop.Id = id;
                shop.DisplayName = name;
                shop.HubLabel = hub;
                shop.Landmark = landmark;

                var entries = new List<ShopEntry>();
                foreach (var row in Table)
                {
                    var p = row.Split('|');
                    int qty = int.Parse(p[1 + c]);
                    if (qty <= 0) continue;
                    ScriptableObject item = (ScriptableObject)catalog.Consumables.FirstOrDefault(x => x.DisplayName == p[0])
                                            ?? (ScriptableObject)catalog.Weapons.FirstOrDefault(x => x.DisplayName == p[0])
                                            ?? catalog.Armors.FirstOrDefault(x => x.DisplayName == p[0]);
                    if (item == null) { Debug.LogError($"[ShopDataSeed] 카탈로그에 없는 상품: {p[0]}"); missing++; continue; }
                    entries.Add(new ShopEntry
                    {
                        Item = item,
                        MaxStock = qty,
                        Stage = (ShopStage)System.Enum.Parse(typeof(ShopStage), p[4]),
                        Limited = p[5] == "limited",
                    });
                }
                shop.Entries = entries.ToArray();
                EditorUtility.SetDirty(shop);
                shops.Add(shop);
            }

            catalog.Shops = shops.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ShopDataSeed] 상점 {shops.Count}곳 — {string.Join(", ", shops.Select(s => $"{s.DisplayName} {s.Entries.Length}종 {s.Entries.Sum(e => e.MaxStock)}개"))}{(missing > 0 ? $" · 누락 {missing}" : "")}");
        }
    }
}
