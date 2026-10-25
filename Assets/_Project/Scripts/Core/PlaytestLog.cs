using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Fantasia.Core
{
    // 플레이테스트 기록 (D29 · System.md "플레이테스트 기록 항목", Boss_Abel_v1.0 "개발팀에서 함께 남길 결과").
    // 실행 빌드·에디터 어디서든 자동으로 CSV 두 개에 덧붙인다 (엑셀에서 바로 열리게 UTF-8 BOM):
    //   battles.csv — 전투 1회 = 1줄: 참가자·레벨·장비·슬롯 성공률·크리티컬·소비 MP/집중력/소모품·라운드·다운/LP·남은 HP·보상·난수
    //   economy.csv — Cent 변화 1건 = 1줄: 전투 보상·상점 구입/판매/서비스/제작·약탈·피의 사냥 등
    // 위치: persistentDataPath/playtest/ (자동 플레이는 autoplay_ 접두어). 메뉴 Fantasia/Playtest/Open Log Folder.
    public static class PlaytestLog
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "playtest");
        private static string Prefix => SaveGame.FileName.StartsWith("save_autoplay") ? "autoplay_" : "";
        public static string BattlesPath => Path.Combine(Folder, Prefix + "battles.csv");
        public static string EconomyPath => Path.Combine(Folder, Prefix + "economy.csv");

        private static readonly string[] BattleHeader =
        {
            "시각", "회차", "월드 라운드", "전투 칸", "적", "개시", "이어하기", "결과", "전투 라운드",
            "파티 (직업 Lv)", "장비", "시작 HP", "아군 슬롯 성공/총", "아군 평균 슬롯 성공률", "아군 크리티컬", "아군 회피",
            "적 슬롯 성공/총", "적 크리티컬", "소비 MP", "소비 집중력", "소모품 사용", "회복·해독 횟수", "봉합 방해",
            "다운", "LP 소모", "남은 HP", "보상 Cent", "경험치", "전리품", "약탈", "난수 상태",
        };

        private static readonly string[] EconomyHeader = { "시각", "회차", "월드 라운드", "위치", "종류", "변화", "소지 Cent", "내용" };

        // ───────── 전투 ─────────
        public class Battle
        {
            public string Coord, Enemies, Opening, Party, Gear, StartHp, Seed;
            public bool Resumed;
            public int AllySlots, AllySuccess, AllyCrits, AllyEvades, EnemySlots, EnemySuccess, EnemyCrits;
            public float AllyChanceSum;
            public int AllyRolls, MpSpent, FocusSpent, Heals, Interrupts, Downs, StartLp;
            public readonly Dictionary<string, int> Items = new();

            public void Hit(bool attackerIsAlly, int slots, int successes, float slotChance, bool crit, bool evaded)
            {
                if (attackerIsAlly)
                {
                    AllySlots += slots; AllySuccess += successes; AllyRolls++; AllyChanceSum += slotChance;
                    if (crit) AllyCrits++;
                }
                else
                {
                    EnemySlots += slots; EnemySuccess += successes;
                    if (crit) EnemyCrits++;
                    if (evaded) AllyEvades++;
                }
            }

            public void Item(string name) => Items[name] = Items.TryGetValue(name, out int n) ? n + 1 : 1;
        }

        public static void EndBattle(Battle b, string result, int rounds, IEnumerable<(string name, int hp, int max, int lp)> allies, int cent, int exp, string loot, int plunder)
        {
            if (b == null) return;
            var s = BoardSession.Instance;
            var list = allies.ToList();
            Append(BattlesPath, BattleHeader, new object[]
            {
                Now, Run(s), s != null ? s.World.WorldRound : 0, b.Coord, b.Enemies, b.Opening, b.Resumed ? "예" : "", result, rounds,
                b.Party, b.Gear, b.StartHp, $"{b.AllySuccess}/{b.AllySlots}", b.AllyRolls > 0 ? $"{b.AllyChanceSum / b.AllyRolls * 100f:0.0}%" : "",
                b.AllyCrits, b.AllyEvades, $"{b.EnemySuccess}/{b.EnemySlots}", b.EnemyCrits, b.MpSpent, b.FocusSpent,
                string.Join(" ", b.Items.Select(kv => $"{kv.Key}×{kv.Value}")), b.Heals, b.Interrupts, b.Downs,
                Math.Max(0, b.StartLp - list.Sum(a => a.lp)), string.Join(" / ", list.Select(a => $"{a.name} {a.hp}/{a.max}")),
                cent, exp, loot, plunder, b.Seed,
            });
        }

        // ───────── 경제 ─────────
        public static void Cent(string kind, int delta, int centAfter, string detail)
        {
            var s = BoardSession.Instance;
            string where = "";
            if (s != null)
            {
                var (row, col) = Fantasia.Board.WorldMapData.AxialToOffset(s.PlayerPosition);
                where = Fantasia.Board.WorldMapData.Label(row, col);
            }
            Append(EconomyPath, EconomyHeader, new object[] { Now, Run(s), s != null ? s.World.WorldRound : 0, where, kind, delta, centAfter, detail });
        }

        // ───────── 파일 ─────────
        private static string Now => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        private static string Run(BoardSession s) => s != null && !string.IsNullOrEmpty(s.RunId) ? s.RunId.Substring(0, Math.Min(8, s.RunId.Length)) : "";

        private static void Append(string path, string[] header, object[] row)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder();
                bool fresh = !File.Exists(path);
                if (fresh) sb.Append('﻿').AppendLine(string.Join(",", header.Select(Csv)));
                sb.AppendLine(string.Join(",", row.Select(v => Csv(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)))));
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[PlaytestLog] 기록 실패: " + e.Message); }
        }

        private static string Csv(string v)
        {
            v ??= "";
            return v.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
    }
}
