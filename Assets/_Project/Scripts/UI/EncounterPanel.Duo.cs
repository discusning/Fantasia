using System.Collections.Generic;
using System.Linq;
using Fantasia.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/11 팀장 요청: 한 칸에 "다른 종류" 적이 함께 나오면 모두 3D로, 화면을 나눠서 보여줌 (같은 종류만이면 지금처럼 1마리).
    // 3D 게임 조사(대결 화면·보스 등장·조우 화면 18종, Docs/Planning/Encounter_TwoEnemies_조사.md):
    //   비스듬한 선으로 화면을 나누고, 칸 크기는 같게, 칸마다 얼굴을 가운데, 이름은 칸 위쪽, 가운데 빈 공간은 만들지 않음.
    // → 왼쪽 = 만난 적(대표), 그 오른쪽으로 다른 종류. 선은 위가 오른쪽으로 12도 기운 금색.
    // 11/12 팀장 지적: ① 나눈 칸에 색 바탕이 깔림(1마리는 투명) → 바탕 없이 투명, 칸 모양으로 자르기만.
    //                  ② 3종류 이상이면 2마리만 나옴 → 종류 최대 3까지 칸을 나눔 (4종류 이상은 앞 3종류).
    public partial class EncounterPanel
    {
        private const float SingleScale = 0.75f;
        private const float DuoW = 340f, DuoH = 210f, DuoSlantDeg = 12f, DuoGap = 3f; // 창 왼쪽~오른쪽 끝 안, 위쪽 바 아래
        private const int MaxKinds = 3;
        private static readonly Color DuoGold = new Color(0.95f, 0.78f, 0.42f, 1f);

        private class SplitView
        {
            public GameObject Root;
            public RawImage[] Models;
            public Text[] Names;
        }

        private RectTransform _duoRoot;              // 나눈 칸 묶음의 부모 (2칸·3칸 둘 다 여기 아래)
        private readonly Dictionary<int, SplitView> _splits = new();

        private void BuildDuo(Transform parent, int siblingIndex)
        {
            _duoRoot = UGUIKit.CreateRect(parent, "EnemySplit", new Vector2(0.21f, 0.7535f), new Vector2(0.21f, 0.7535f));
            _duoRoot.sizeDelta = new Vector2(DuoW, DuoH);
            _duoRoot.anchoredPosition = new Vector2(0f, -(ModelSize.y - DuoH) * 0.5f); // 아래 끝은 1마리 때와 같음 (이름 띠 뒤로 숨김)
            _duoRoot.SetSiblingIndex(siblingIndex);
            for (int n = 2; n <= MaxKinds; n++) _splits[n] = BuildSplit(n);
            _duoRoot.gameObject.SetActive(false);
        }

        private SplitView BuildSplit(int n)
        {
            var root = UGUIKit.CreateRect(_duoRoot, $"Split{n}", Vector2.zero, Vector2.one);
            var view = new SplitView { Root = root.gameObject, Models = new RawImage[n], Names = new Text[n] };
            float k = Mathf.Tan(DuoSlantDeg * Mathf.Deg2Rad) * DuoH / DuoW; // 위·아래 끝에서 선이 옮겨 가는 폭 (칸 너비 비율)
            float modelScale = n == 2 ? 0.72f : 0.6f;                       // 칸이 좁을수록 얼굴을 조금 작게
            int nameSize = n == 2 ? 14 : 12;
            for (int i = 0; i < n; i++)
            {
                var maskImg = UGUIKit.CreateImage(root, $"Part{i}", Vector2.zero, Vector2.one, Color.white);
                maskImg.sprite = MakePartMask(n, i);
                maskImg.raycastTarget = false;
                maskImg.gameObject.AddComponent<Mask>().showMaskGraphic = false; // 바탕은 그리지 않음 (투명) — 칸 모양으로 자르기만
                var model = UGUIKit.CreateRect(maskImg.transform, "Model", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)).gameObject.AddComponent<RawImage>();
                model.raycastTarget = false;
                model.rectTransform.sizeDelta = ModelSize * modelScale; // 칸 밖은 잘림
                model.rectTransform.anchoredPosition = new Vector2(((i + 0.5f) / n - 0.5f) * DuoW, 8f);
                view.Models[i] = model;

                // 이름: 칸 위쪽 (선이 기울어 위쪽 칸은 오른쪽으로 0.5k만큼 밀림)
                bool first = i == 0, last = i == n - 1;
                float x0 = first ? 0.03f : (float)i / n + 0.5f * k + 0.015f;
                float x1 = last ? 0.97f : (float)(i + 1) / n + 0.5f * k - 0.015f;
                var anchor = first ? TextAnchor.UpperLeft : last ? TextAnchor.UpperRight : TextAnchor.UpperCenter;
                var name = UGUIKit.CreateText(maskImg.transform, "Name", new Vector2(x0, 0.8f), new Vector2(x1, 0.98f), "", nameSize, anchor);
                name.fontStyle = FontStyle.Bold;
                name.color = new Color(0.96f, 0.92f, 0.84f);
                name.raycastTarget = false;
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                var outline = name.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                view.Names[i] = name;
            }

            // 칸 사이 비스듬한 금색 선 (+ 그림자)
            float len = DuoH / Mathf.Cos(DuoSlantDeg * Mathf.Deg2Rad) + 6f;
            for (int i = 1; i < n; i++)
                foreach (var (w, c) in new[] { (12f, new Color(0f, 0f, 0f, 0.45f)), (4f, DuoGold) })
                {
                    var line = UGUIKit.CreateImage(root, "Divider", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), c);
                    line.raycastTarget = false;
                    line.rectTransform.sizeDelta = new Vector2(w, len);
                    line.rectTransform.anchoredPosition = new Vector2(((float)i / n - 0.5f) * DuoW, 0f);
                    line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -DuoSlantDeg); // 위가 오른쪽으로
                }
            root.gameObject.SetActive(false);
            return view;
        }

        // 칸 모양: n칸 중 i번째. 칸 경계 = 비스듬한 선 (선마다 DuoGap만큼 틈)
        private static Sprite MakePartMask(int n, int index)
        {
            const int tw = 260, th = 155;
            var tex = new Texture2D(tw, th, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            float k = Mathf.Tan(DuoSlantDeg * Mathf.Deg2Rad) * DuoH / DuoW;
            float gap = DuoGap * 0.5f / DuoW;
            var px = new Color32[tw * th];
            for (int y = 0; y < th; y++)
            {
                float v = (y + 0.5f) / th;
                float shift = (v - 0.5f) * k;
                float left = index == 0 ? -1f : (float)index / n + shift + gap;
                float right = index == n - 1 ? 2f : (float)(index + 1) / n + shift - gap;
                for (int x = 0; x < tw; x++)
                {
                    float u = (x + 0.5f) / tw;
                    float d = Mathf.Min(u - left, right - u);
                    float a = Mathf.Clamp01(d * tw + 0.5f); // 1픽셀 부드러운 끝
                    px[y * tw + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), 100f);
        }

        private void LayoutModels(IEnumerable<EnemyDefinition> all)
        {
            bool leaderModel = _enemy != null && _enemy.Model != null;
            var kinds = new List<EnemyDefinition>();
            if (leaderModel)
            {
                kinds.Add(_enemy);
                foreach (var e in all)
                    if (e != null && e.Model != null && !kinds.Contains(e) && kinds.Count < MaxKinds) kinds.Add(e);
            }
            int n = kinds.Count;
            bool split = n >= 2;
            _duoRoot.gameObject.SetActive(split);
            foreach (var pair in _splits) pair.Value.Root.SetActive(split && pair.Key == n);
            _enemyModel.enabled = leaderModel && !split;
            // 11/11 팀장: 모델 칸이 너무 큼 → 1마리도 0.75배 (아래 끝은 그대로 이름 띠 뒤)
            _enemyModel.rectTransform.sizeDelta = ModelSize * SingleScale;
            _enemyModel.rectTransform.anchoredPosition = new Vector2(0f, -ModelSize.y * (1f - SingleScale) * 0.5f);
            if (split)
            {
                var view = _splits[n];
                for (int i = 0; i < n; i++)
                {
                    view.Models[i].texture = i == 0 ? EncounterModelView.Show(kinds[0]) : EncounterModelView.ShowAlso(kinds[i]);
                    view.Names[i].text = kinds[i].DisplayName;
                }
            }
            else if (leaderModel) _enemyModel.texture = EncounterModelView.Show(_enemy);
            // 판정 칸 수 원: 나누면 대표(왼쪽) 칸 아래 왼쪽, 1마리면 모델 오른쪽 아래
            var badgeAt = !leaderModel ? new Vector2(0.21f, 0.655f) : split ? new Vector2(0.095f, 0.655f) : new Vector2(0.3f, 0.655f);
            _slotBadgeRect.anchorMin = _slotBadgeRect.anchorMax = badgeAt;
            _sizeSmall.rectTransform.anchorMin = _sizeSmall.rectTransform.anchorMax = badgeAt;
        }

        // 자동 플레이 점검용: 지금 나눈 칸 수 (1마리면 1, 모델 없으면 0)
        public static int DevModelCount => Instance == null ? 0 : Instance._duoRoot.gameObject.activeSelf ? Instance._splits.First(p => p.Value.Root.activeSelf).Key : Instance._enemyModel.enabled ? 1 : 0;
    }
}
