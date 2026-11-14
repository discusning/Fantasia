using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fantasia.Data;
using UnityEditor;
using UnityEngine;

namespace Fantasia.Editor
{
    // 11/14 디자인 "상점 아이템 및 무기 아이콘 34가지" (원본 ArtSource/2026-11-14_디자인_아이콘34, 512×512 투명).
    // 디자인 번호 Z01~Z34는 기존 UI 아이콘 Z01~Z44(UI/Sprites/Icons)와 겹치므로 그 폴더에 넣지 않음.
    //  - 소모품·장비: 개발이 3D로 찍은 임시 그림 UI/Sprites/Items/Icon_<이름>.png 를 같은 이름으로 덮어씀 (연결 유지) + 비어 있던 데이터 칸 연결
    //  - 증거품: Resources/EvidenceIcons/Icon_<이름>.png 덮어씀
    //  - 무기: UI/Sprites/Weapons/Icon_<종류>.png → 같은 종류 무기 전부. 버터플라이 나이프 = 명탐정 시작 무기 "탐정의 접이식 단검"
    // 실행: 메뉴 Fantasia/Art/디자인 아이콘 34 넣기 · 배치: -executeMethod Fantasia.Editor.DesignIconImport.Run
    public static class DesignIconImport
    {
        private const string Src = "ArtSource/2026-11-14_디자인_아이콘34/Icons";
        private const string ItemDir = "Assets/_Project/UI/Sprites/Items";
        private const string EvidenceDir = "Assets/_Project/Resources/EvidenceIcons";
        private const string WeaponDir = "Assets/_Project/UI/Sprites/Weapons";

        private static readonly Dictionary<string, WeaponCategory> WeaponIcons = new()
        {
            ["Z27_단검"] = WeaponCategory.Dagger,
            ["Z28_양손검"] = WeaponCategory.SwordSaber,
            ["Z29_대검"] = WeaponCategory.Greatsword,
            ["Z31_마법_스태프"] = WeaponCategory.Staff,
            ["Z32_마법서"] = WeaponCategory.Grimoire,
            ["Z33_머스킷_소총"] = WeaponCategory.Musket,
            ["Z34_머스킷풍_권총"] = WeaponCategory.Pistol,
        };
        private static readonly Dictionary<string, string> Rename = new()
        {
            ["마리의질문서"] = "마리의집문서", // 디자인 목록 "마리의 질문서" = 데이터 "마리의 집문서" (공동 전리품, 무기 등급 상승)
        };
        private static readonly HashSet<string> Evidence = new() { "검시기록", "붉은룬조각" };

        [MenuItem("Fantasia/Art/디자인 아이콘 34 넣기")]
        public static void Run()
        {
            var report = new List<string>();
            Directory.CreateDirectory(WeaponDir);
            var copied = new List<(string path, string key)>();
            foreach (var file in Directory.GetFiles(Src, "Z*.png").OrderBy(f => f))
            {
                string stem = Path.GetFileNameWithoutExtension(file); // Z05_압박_붕대
                string name = stem.Substring(4).Replace("_", "");     // 압박붕대
                if (Rename.TryGetValue(name, out var r)) name = r;
                string dest;
                if (stem == "Z30_버터플라이_나이프") dest = $"{WeaponDir}/Icon_탐정의접이식단검.png";
                else if (WeaponIcons.TryGetValue(stem, out var cat)) dest = $"{WeaponDir}/Icon_{cat}.png";
                else if (Evidence.Contains(name)) dest = $"{EvidenceDir}/Icon_{name}.png";
                else dest = $"{ItemDir}/Icon_{name}.png";
                bool existed = File.Exists(dest);
                File.Copy(file, dest, true);
                copied.Add((dest, name));
                report.Add($"{stem} → {dest}{(existed ? " (덮어씀)" : " (새로)")}");
            }
            AssetDatabase.Refresh();
            foreach (var (path, _) in copied) Configure(path);

            // 소모품·장비: Icon_<데이터 이름 뒷부분>.png 가 있으면 연결 (덮어쓴 것은 이미 연결돼 있음)
            int linked = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ConsumableDefinition"))
            {
                var c = AssetDatabase.LoadAssetAtPath<ConsumableDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{ItemDir}/Icon_{c.name.Replace("Consumable_", "")}.png");
                if (s != null && c.Icon != s) { c.Icon = s; EditorUtility.SetDirty(c); linked++; }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:ArmorDefinition"))
            {
                var a = AssetDatabase.LoadAssetAtPath<ArmorDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{ItemDir}/Icon_{a.name.Replace("Armor_", "")}.png");
                if (s != null && a.Icon != s) { a.Icon = s; EditorUtility.SetDirty(a); linked++; }
            }
            // 무기: 이름 전용 그림이 있으면 그것, 아니면 종류 그림
            int weapons = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponDefinition"))
            {
                var w = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{WeaponDir}/Icon_{w.name.Replace("Weapon_", "")}.png")
                        ?? AssetDatabase.LoadAssetAtPath<Sprite>($"{WeaponDir}/Icon_{w.Category}.png");
                if (s == null) { report.Add($"무기 그림 없음 (종류 {w.Category}): {w.name}"); continue; }
                if (w.Icon != s) { w.Icon = s; EditorUtility.SetDirty(w); }
                weapons++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[DesignIcon] 그림 {copied.Count}장 · 데이터 새로 연결 {linked} · 무기 {weapons}개\n" + string.Join("\n", report));
        }

        private static void Configure(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = 256; // UI 칸은 작음 (예전 아이콘과 같게)
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
    }
}
