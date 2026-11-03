using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 판 떨어지는 연출을 화면에 붙이는 공통 도우미 (보고서 11/3 B3~B7).
    // Resources/PanelDrop 프리팹을 화면 위쪽 가운데에 붙인다 — 사슬 윗끝이 화면(또는 상단 바) 위끝에서 시작하게.
    // 판 위 글자는 제목 + 한 줄 요약 (ZEN SERIF, UGUIKit).
    public sealed class PanelDropView
    {
        public const string Victory = "VictoryPanel", Defeat = "DefeatPanel", Turn = "HangingPanel";

        public readonly GameObject Root;
        public readonly HangingPanelDrop Drop;
        public readonly Text Title, Summary;

        private PanelDropView(GameObject root, HangingPanelDrop drop, Text title, Text summary)
        {
            Root = root; Drop = drop; Title = title; Summary = summary;
        }

        // width: 기준 해상도(1280×720) 단위 가로. topOffset: 화면 위끝에서 내릴 거리 (음수 = 사슬 일부를 위로 숨김)
        public static PanelDropView Create(Transform parent, string prefabName, float width, float topOffset, int titleSize, int summarySize)
        {
            var prefab = Resources.Load<GameObject>("PanelDrop/" + prefabName);
            if (prefab == null) { Debug.LogWarning($"[PanelDrop] {prefabName} 프리팹 없음"); return null; }
            var go = Object.Instantiate(prefab, parent, false);
            go.name = prefabName;
            var drop = go.GetComponent<HangingPanelDrop>();
            drop.playOnEnable = false;
            var rt = (RectTransform)go.transform;
            float height = drop.artwork != null ? width * drop.artwork.height / drop.artwork.width : width * 2f / 3f;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0f, -topOffset);

            var title = UGUIKit.CreateText(drop.ContentRoot, "Title", new Vector2(0f, 0.42f), new Vector2(1f, 1f), "", titleSize, TextAnchor.MiddleCenter);
            title.color = new Color(0.96f, 0.86f, 0.56f);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.75f);
            var summary = UGUIKit.CreateText(drop.ContentRoot, "Summary", new Vector2(0f, 0f), new Vector2(1f, 0.42f), "", summarySize, TextAnchor.MiddleCenter);
            summary.color = new Color(0.96f, 0.93f, 0.86f);
            summary.resizeTextForBestFit = true;
            summary.resizeTextMinSize = Mathf.Max(8, summarySize - 5);
            summary.resizeTextMaxSize = summarySize;
            summary.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
            go.SetActive(false);
            return new PanelDropView(go, drop, title, summary);
        }

        // 판 위끝 기준 아래쪽 경계 (판 밑에 다른 칸을 붙일 때 쓰는 거리, 기준 해상도 단위)
        public float BoardBottom
        {
            get
            {
                var rt = (RectTransform)Root.transform;
                if (Drop.artwork == null) return rt.sizeDelta.y;
                float scale = rt.sizeDelta.x / Drop.artwork.width;
                return -rt.anchoredPosition.y + Drop.boardPixels.yMax * scale;
            }
        }

        public void Play(string title, string summary)
        {
            Title.text = title;
            Summary.text = summary;
            Root.SetActive(true);
            Drop.Play();
        }

        public void Hide() => Root.SetActive(false);
    }
}
