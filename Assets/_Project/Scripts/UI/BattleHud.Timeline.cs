using System;
using System.Collections;
using System.Collections.Generic;
using Fantasia.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // ① 행동 순서 띠 (11/15 긴 파일 정리: BattleHud.cs에서 옮김, 내용 그대로)
    public partial class BattleHud
    {
        // ───────────── ① 타임라인 ─────────────
        public void SetTimeline(IReadOnlyList<BattleQueue.Entry> entries, int currentRound, System.Func<BattleUnit, int> orderChange = null)
        {
            // 새 라운드가 시작될 때 ↑/↓를 캐릭터별로 정해 두고 잠깐 보여준다 (칸이 앞으로 밀려도 같은 캐릭터에 붙음)
            if (orderChange != null && currentRound != _arrowRound)
            {
                _arrowRound = currentRound;
                _timelineOffset = 0; // 새 라운드는 현재 차례부터 (UI 솔루션 Q6)
                _arrows.Clear();
                if (currentRound > 1)
                    foreach (var e in entries)
                        if (e.Round == currentRound && orderChange(e.Unit) != 0) _arrows[e.Unit] = orderChange(e.Unit);
                if (_arrowHide != null) StopCoroutine(_arrowHide);
                _arrowHide = _arrows.Count > 0 ? StartCoroutine(HideArrows(2.5f)) : null;
            }
            // 11/11 팀장 확인: 디자인 턴 순서 움직임(11/10 패키지) — 한 차례가 끝나 다음 사람이 맨 앞이 되면 칸이 왼쪽으로 한 칸 밀림
            bool advance = _timelineOffset == 0 && _shownUnits.Count > 1 && entries != null && entries.Count > 0
                           && entries[0].Unit == _shownUnits[1] && entries[0].Unit != _shownUnits[0] && _timeline[0].Root.activeSelf;
            GameObject leaving = advance ? Instantiate(_timeline[0].Root, _timeline[0].Rect.parent) : null; // 나가는 칸 (예전 내용 그대로 복사)
            // 11/11 팀장 요청: 라운드가 바뀔 때 예상 칸(흐림·점선)이 한 번에 밝은 틀로 바뀌던 것 → 왼쪽부터 한 칸씩 밝아짐
            bool newRound = _timelineRound > 0 && currentRound != _timelineRound;
            if (newRound) for (int i = 0; i < _reveal.Length; i++) _reveal[i] = 0f;
            _timelineEntries = entries;
            _timelineRound = currentRound;
            RenderTimeline();
            if (newRound)
            {
                if (_revealRoutine != null) StopCoroutine(_revealRoutine);
                _revealRoutine = StartCoroutine(RevealRound());
            }
            _shownUnits.Clear();
            if (entries != null) foreach (var e in entries) _shownUnits.Add(e.Unit);
            if (advance)
            {
                if (_slide != null) { StopCoroutine(_slide); if (_leaving != null) Destroy(_leaving); } // 앞 움직임이 안 끝났으면 정리하고 새로
                _slide = StartCoroutine(SlideTimeline(leaving));
            }
        }

        // 턴 순서 움직임 (디자인 패키지 README: 0.42초, 왼쪽으로 한 칸, 첫 칸 122% + 금 틀, 새 끝 칸은 오른쪽에서 같이 들어옴).
        // 칸 구조(10칸·점선·3D 초상)는 그대로 두고, 새 내용을 그린 뒤 각 칸을 "오른쪽 옆 칸 자리 → 제자리"로 옮겨 보이게 함.
        // 첫 칸은 작은 칸 자리에서 큰 칸 자리로 커짐 (큰 칸 0.08 / 작은 칸 0.065 = 123%).
        public const float TimelineSlideSeconds = 0.42f;
        private readonly List<BattleUnit> _shownUnits = new();
        private readonly List<Vector2> _homeMin = new(), _homeMax = new();
        private Coroutine _slide;
        private GameObject _leaving;
        public bool DevTimelineSliding => _slide != null;
        public BattleUnit DevTimelineFirst => _shownUnits.Count > 0 ? _shownUnits[0] : null;

        private IEnumerator SlideTimeline(GameObject leaving)
        {
            int n = _timeline.Count;
            Vector2 step = _homeMin[n - 1] - _homeMin[n - 2]; // 작은 칸 한 칸 간격
            var fromMin = new Vector2[n];
            var fromMax = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                fromMin[i] = i + 1 < n ? _homeMin[i + 1] : _homeMin[i] + step;
                fromMax[i] = i + 1 < n ? _homeMax[i + 1] : _homeMax[i] + step;
            }
            // 나가는 칸: 첫 칸 자리 → 왼쪽으로 한 칸, 작아지며 흐려짐
            RectTransform leaveRect = null;
            CanvasGroup leaveGroup = null;
            Vector2 leaveMin0 = _homeMin[0], leaveMax0 = _homeMax[0];
            Vector2 leaveSize = _homeMax[1] - _homeMin[1];
            Vector2 leaveCenter = (leaveMin0 + leaveMax0) * 0.5f - new Vector2(step.x, 0f);
            _leaving = leaving;
            if (leaving != null)
            {
                leaveRect = (RectTransform)leaving.transform;
                leaving.transform.SetAsFirstSibling(); // 다른 칸 뒤로
                leaveGroup = leaving.AddComponent<CanvasGroup>();
                leaveGroup.blocksRaycasts = false;
            }
            var lastGroup = _timeline[n - 1].Root.GetComponent<CanvasGroup>() ?? _timeline[n - 1].Root.AddComponent<CanvasGroup>();

            for (float t = 0f; t < TimelineSlideSeconds; t += Time.unscaledDeltaTime)
            {
                float k = 1f - Mathf.Pow(1f - t / TimelineSlideSeconds, 3f); // 끝에서 부드럽게 멈춤
                for (int i = 0; i < n; i++)
                {
                    _timeline[i].Rect.anchorMin = Vector2.Lerp(fromMin[i], _homeMin[i], k);
                    _timeline[i].Rect.anchorMax = Vector2.Lerp(fromMax[i], _homeMax[i], k);
                }
                lastGroup.alpha = k; // 새 끝 칸은 오른쪽에서 나타나며 들어옴
                if (leaveRect != null)
                {
                    Vector2 c = Vector2.Lerp((leaveMin0 + leaveMax0) * 0.5f, leaveCenter, k);
                    Vector2 s = Vector2.Lerp(leaveMax0 - leaveMin0, leaveSize * 0.8f, k);
                    leaveRect.anchorMin = c - s * 0.5f;
                    leaveRect.anchorMax = c + s * 0.5f;
                    leaveGroup.alpha = 1f - k;
                }
                yield return null;
            }
            for (int i = 0; i < n; i++) { _timeline[i].Rect.anchorMin = _homeMin[i]; _timeline[i].Rect.anchorMax = _homeMax[i]; }
            lastGroup.alpha = 1f;
            if (leaving != null) Destroy(leaving);
            _leaving = null;
            _slide = null;
        }

        // 타임라인 그리기 (UI 솔루션 Q6): 한 화면 최대 10칸 — 넘치면 ◀ ▶로 넘겨 본다 (자르지 않음).
        // 이번 라운드 = 밝게·실선, 다음 라운드 = 초상 60% 밝기 + 점선 테두리 + "전투 R3 예상" + 앞에 구분선.
        private void RenderTimeline()
        {
            var entries = _timelineEntries;
            if (entries == null) return;
            int currentRound = _timelineRound;
            _roundText.text = $"전투 R{currentRound}";
            int maxOffset = Mathf.Max(0, entries.Count - _timeline.Count);
            _timelineOffset = Mathf.Clamp(_timelineOffset, 0, maxOffset);
            _timelinePrev.gameObject.SetActive(maxOffset > 0);
            _timelineNext.gameObject.SetActive(maxOffset > 0);
            _timelinePrev.interactable = _timelineOffset > 0;
            _timelineNext.interactable = _timelineOffset < maxOffset;

            int prevRound = _timelineOffset > 0 ? entries[_timelineOffset - 1].Round : currentRound;
            int lastMark = -9; // 11/11 글자 겹침: 라운드가 짧아 표시가 붙으면 짧은 글자
            for (int i = 0; i < _timeline.Count; i++)
            {
                var slot = _timeline[i];
                int idx = _timelineOffset + i;
                bool has = idx < entries.Count;
                slot.Root.SetActive(has);
                if (!has) continue;

                var unit = entries[idx].Unit;
                slot.Portrait.texture = PortraitStudio.FaceFor(unit.Definition);
                bool predicted = entries[idx].Round != currentRound;
                slot.IsAlly = unit.IsAlly;
                slot.Predicted = predicted;
                ApplySlotLook(i);
                // 다음 라운드가 시작되는 칸에 "전투 R3 예상" + 구분선 (속도 변화는 이 칸부터 반영된다).
                bool newRound = entries[idx].Round != prevRound;
                slot.Round.gameObject.SetActive(newRound);
                slot.Round.text = newRound && i - lastMark <= 2 ? $"R{entries[idx].Round}" : $"전투 R{entries[idx].Round} 예상";
                if (newRound && i - lastMark <= 2 && lastMark >= 0) _timeline[lastMark].Round.text = _timeline[lastMark].Round.text.Replace("전투 ", "").Replace(" 예상", ""); // 앞 표시도 짧게
                if (newRound) lastMark = i;
                slot.Divider.SetActive(newRound);
                slot.Mark.gameObject.SetActive(unit.Boss != null && unit.Boss.HasPending && unit.IsAlive);
                slot.Speed.text = $"속도 {entries[idx].Speed}";
                bool arrow = !predicted && _arrows.ContainsKey(unit);
                slot.Arrow.gameObject.SetActive(arrow);
                if (arrow)
                {
                    slot.Arrow.text = _arrows[unit] > 0 ? "↑" : "↓";
                    slot.Arrow.color = _arrows[unit] > 0 ? SlotSuccess : EnemyBorder;
                }
                prevRound = entries[idx].Round;
            }
        }

        // 칸 모양: 예상 칸 = 초상 60%·틀 어둡게·점선 / 이번 라운드 칸 = 밝게. _reveal[i](0→1)로 그 사이를 부드럽게 바꿈.
        private readonly float[] _reveal = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        private Coroutine _revealRoutine;
        private const float RevealSeconds = 0.3f, RevealStagger = 0.07f;

        private void ApplySlotLook(int i)
        {
            var slot = _timeline[i];
            var border = slot.IsAlly ? AllyBorder : EnemyBorder;
            float t = slot.Predicted ? 0f : _reveal[i]; // 0 = 예상 모양, 1 = 이번 라운드 모양
            slot.Border.color = new Color(border.r, border.g, border.b, Mathf.Lerp(0.15f, 1f, t));
            slot.Dashed.enabled = t < 1f;
            slot.Dashed.color = new Color(border.r, border.g, border.b, 1f - t);
            if (slot.FrameArt != null) // 11/10 그림 틀: 바깥 단색 테두리 대신 아래 색 줄, 다음 라운드 칸은 틀을 어둡게
            {
                slot.Border.color = new Color(0f, 0f, 0f, 0f);
                slot.Team.color = new Color(border.r, border.g, border.b, Mathf.Lerp(0.45f, 1f, t));
                float f = Mathf.Lerp(0.55f, 1f, t);
                slot.FrameArt.color = new Color(f, f, f, 1f);
            }
            float p = Mathf.Lerp(0.6f, 1f, t);
            slot.Portrait.color = new Color(p, p, p, 1f);
        }

        private IEnumerator RevealRound()
        {
            float total = RevealSeconds + RevealStagger * (_reveal.Length - 1);
            for (float time = 0f; time < total; time += Time.unscaledDeltaTime)
            {
                for (int i = 0; i < _reveal.Length && i < _timeline.Count; i++)
                {
                    _reveal[i] = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - i * RevealStagger) / RevealSeconds));
                    if (_timeline[i].Root.activeSelf) ApplySlotLook(i);
                }
                yield return null;
            }
            for (int i = 0; i < _reveal.Length && i < _timeline.Count; i++) { _reveal[i] = 1f; if (_timeline[i].Root.activeSelf) ApplySlotLook(i); }
            _revealRoutine = null;
        }

        private void ScrollTimeline(int step)
        {
            _timelineOffset += step;
            RenderTimeline();
        }

        // 다음 라운드 칸 점선 테두리 (아트가 오기 전 코드로 그림). 9-slice라 칸 크기에 맞춰 늘어난다.
        private static Sprite _dashed;
        private static Sprite DashedFrame()
        {
            if (_dashed != null) return _dashed;
            const int size = 32, thick = 2, dash = 4, gap = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool edge = x < thick || y < thick || x >= size - thick || y >= size - thick;
                int along = (x < thick || x >= size - thick) ? y : x;
                bool on = edge && along % (dash + gap) < dash;
                px[y * size + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            tex.SetPixels32(px);
            tex.Apply();
            _dashed = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(7, 7, 7, 7));
            return _dashed;
        }

        private System.Collections.IEnumerator HideArrows(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            foreach (var t in _timeline) t.Arrow.gameObject.SetActive(false);
            _arrows.Clear();
            _arrowHide = null;
        }
    }
}
