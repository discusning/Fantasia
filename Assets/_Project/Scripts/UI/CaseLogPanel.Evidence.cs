using Fantasia.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/12 증거품 줄: 왼쪽 그림(받은 3D 모델을 찍은 아이콘) + 오른쪽 글. 미확보는 그림을 검은 그림자로, 이름은 ???.
    // 그림 파일: Resources/EvidenceIcons/Icon_<이름 띄어쓰기 없이>.png (ItemIconStudio가 만듦). 없으면 공용 EvidenceIcon.
    public partial class CaseLogPanel
    {
        private const float EvidenceTop = 0.77f, EvidenceRowH = 0.36f;
        private Image[] _evidenceIcons;
        private Text[] _evidenceTexts;

        private void BuildEvidenceRows(Transform win)
        {
            _evidenceIcons = new Image[AllEvidence.Length];
            _evidenceTexts = new Text[AllEvidence.Length];
            for (int i = 0; i < AllEvidence.Length; i++)
            {
                float top = EvidenceTop - i * EvidenceRowH, bottom = top - EvidenceRowH + 0.02f;
                var sprite = Resources.Load<Sprite>("EvidenceIcons/Icon_" + AllEvidence[i].Name.Replace(" ", ""));
                if (sprite == null && UITheme.Current != null) sprite = UITheme.Current.EvidenceIcon;
                var box = UGUIKit.CreateImage(win, $"EvidenceBox{i}", new Vector2(0.04f, bottom + 0.02f), new Vector2(0.18f, top - 0.02f), RowFill);
                _evidenceIcons[i] = UGUIKit.CreateIcon(box.transform, "Icon", new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), sprite);
                _evidenceIcons[i].preserveAspect = true;
                var t = UGUIKit.CreateText(win, $"EvidenceText{i}", new Vector2(0.2f, bottom), new Vector2(0.96f, top), "", 13, TextAnchor.UpperLeft);
                t.color = TextLight;
                t.supportRichText = true;
                _evidenceTexts[i] = t;
            }
            HideEvidenceRows();
        }

        private void HideEvidenceRows()
        {
            if (_evidenceIcons == null) return;
            for (int i = 0; i < _evidenceIcons.Length; i++)
            {
                _evidenceIcons[i].transform.parent.gameObject.SetActive(false);
                _evidenceTexts[i].gameObject.SetActive(false);
            }
        }

        private void ShowEvidenceRows(BoardSession s)
        {
            for (int i = 0; i < AllEvidence.Length; i++)
            {
                var ev = AllEvidence[i];
                bool got = s.KeyItems.Contains(ev.Name);
                _evidenceIcons[i].transform.parent.gameObject.SetActive(true);
                _evidenceIcons[i].color = got ? Color.white : new Color(0f, 0f, 0f, 0.85f); // 미확보: 검은 그림자
                _evidenceTexts[i].gameObject.SetActive(true);
                _evidenceTexts[i].text = got
                    ? $"<b><color=#D9B45A>{ev.Name}</color></b>  — 확보\n{ev.Description}\n<color=#8C877F>획득: {ev.Source}\n연결: {ev.LinkedQuests}</color>"
                    : "<b>???</b>  — 미확보\n<color=#8C877F>아직 확보하지 못한 증거.</color>";
            }
        }
    }
}
