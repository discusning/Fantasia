using System;
using System.Collections;
using System.Collections.Generic;
using Fantasia.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // ④ 슬롯 판정 연출 · ⑤ 피해 숫자 (11/15 긴 파일 정리: BattleHud.cs에서 옮김, 내용 그대로)
    public partial class BattleHud
    {
        // ───────────── ④ 슬롯 판정 연출 ─────────────
        public IEnumerator PlayRoll(string title, SlotCheck.Result slots, int focusUsed, string resultText, Color resultColor)
        {
            _rollPanel.SetActive(true);
            _rollTitle.text = title;
            _rollResult.text = "";
            BuildRollSlots(slots.SlotCount);

            // 집중력 확정 슬롯은 맨 앞에서 바로 성공으로, 나머지는 결과 개수만큼 섞어서 하나씩 공개.
            int forced = Mathf.Clamp(focusUsed, 0, slots.SlotCount);
            var outcome = new bool[slots.SlotCount];
            for (int i = 0; i < slots.SuccessCount; i++) outcome[i] = true;
            for (int i = slots.SlotCount - 1; i > forced; i--)
            {
                int j = UnityEngine.Random.Range(forced, i + 1);
                (outcome[i], outcome[j]) = (outcome[j], outcome[i]);
            }

            for (int i = 0; i < slots.SlotCount; i++)
            {
                yield return new WaitForSeconds(i < forced ? 0.1f : SecondsPerSlot);
                _rollSlots[i].color = outcome[i] ? SlotSuccess : SlotFailure;
                Fantasia.Core.SfxPlayer.Play(outcome[i] ? Fantasia.Core.SfxIds.SlotSuccess : Fantasia.Core.SfxIds.SlotFail);
                _rollSlotLabels[i].text = i < forced ? "집중" : outcome[i] ? "성공" : "실패";
            }

            _rollResult.text = resultText;
            _rollResult.color = resultColor;
            yield return new WaitForSeconds(0.7f);
            _rollPanel.SetActive(false);
        }

        // ───────────── ⑤ 피해 숫자 ─────────────
        public void PopText(Camera cam, Vector3 worldPos, string text, Color color, int size = 30)
        {
            var label = UGUIKit.CreateText(_canvasRect, "Popup", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), text, size, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Overflow; // 11/11 긴 팝업("방어 태세 · 보호막 8")이 줄바꿈되어 잘림
            label.verticalOverflow = VerticalWrapMode.Overflow; // 11/15 팀장 "크리티컬 피해 숫자가 안 나옴": 크리티컬(38)은 칸 높이(44)보다 글꼴 줄 높이가 커서 Unity가 줄을 통째로 숨겼음
            label.color = color;
            label.fontStyle = FontStyle.Bold;
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            var rect = (RectTransform)label.transform;
            rect.sizeDelta = new Vector2(Mathf.Max(220f, label.preferredWidth + 12f), Mathf.Max(44f, size * 1.8f)); // 11/15: 큰 글자도 칸 안에, 긴 팝업은 칸을 글자만큼 넓힘 (통합 QA 가로 넘침)
            rect.anchorMin = rect.anchorMax = Vector2.zero;

            Vector2 screen = cam.WorldToScreenPoint(worldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            var pos = local + _canvasRect.rect.size * 0.5f;
            // 11/11 UI 점검: 키 큰 보스 머리 위 글자("저지!" 등)가 위쪽 순서 띠와 겹침 → 떠오른 끝(+50)까지 띠(화면 높이 0.915) 아래에 머물게
            pos.y = Mathf.Min(pos.y, _canvasRect.rect.height * 0.9f - 22f - 50f);
            pos.x = Mathf.Clamp(pos.x, rect.sizeDelta.x * 0.5f + 8f, _canvasRect.rect.width - rect.sizeDelta.x * 0.5f - 8f); // 화면 가장자리 적의 팝업이 밖으로 나가지 않게
            rect.anchoredPosition = pos;
            StartCoroutine(FloatAndFade(label, rect));
        }

        private static IEnumerator FloatAndFade(Text label, RectTransform rect)
        {
            var start = rect.anchoredPosition;
            var color = label.color;
            for (float t = 0f; t < 1.1f; t += Time.deltaTime)
            {
                rect.anchoredPosition = start + Vector2.up * (t * 50f);
                label.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(1.6f - t * 1.4f));
                yield return null;
            }
            Destroy(label.gameObject);
        }
    }
}
