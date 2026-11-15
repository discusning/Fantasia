using System;
using System.Collections;
using System.Collections.Generic;
using Fantasia.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // ⑥ 결과 창 (11/15 긴 파일 정리: BattleHud.cs에서 옮김, 내용 그대로)
    public partial class BattleHud
    {
        // ───────────── ⑥ 결과 창 ─────────────
        public void ShowResult(string title, string body, Action onContinue, string buttonLabel = "보드로 돌아가기")
        {
            HideActions();
            ClearShieldMarks(); // 11/12 팀장 지적: 방어 방패가 승리·패배 창 위에 남음 → 전투가 끝나면 지움
            UseCardLayout();
            _resultTitle.text = title;
            _resultBody.text = body;
            _onResultContinue = onContinue;
            _resultButtonLabel.text = buttonLabel;
            _onResultSecond = null;
            _resultSecondButton.SetActive(false);
            _resultButtonRect.anchorMin = new Vector2(0.3f, 0.04f);
            _resultButtonRect.anchorMax = new Vector2(0.7f, 0.15f);
            _resultPanel.SetActive(true);
        }

        // 버튼 2개 결과 창 (게임 오버: [새 게임] [메인 메뉴] — 1막 기획 결정서 06)
        public void ShowResult(string title, string body, string firstLabel, Action first, string secondLabel, Action second)
        {
            ShowResult(title, body, first, firstLabel);
            _onResultSecond = second;
            _resultSecondLabel.text = secondLabel;
            _resultSecondButton.SetActive(true);
            _resultButtonRect.anchorMin = new Vector2(0.08f, 0.04f);
            _resultButtonRect.anchorMax = new Vector2(0.48f, 0.15f);
        }

        // ⑥-2 판 떨어지는 결과 (보고서 11/3 B5·B6): 승리·패배·전멸.
        // 사슬 판에 제목 + 한 줄 요약, 자세한 보상은 판 아래 칸 (판이 닿으면 나타남), 버튼은 판이 멈춘 뒤 (약 1.7초).
        // 퇴각은 예전 결과 창 그대로 (ShowResult). 판 그림이 없으면 예전 결과 창으로.
        public void ShowPanelResult(bool victory, string title, string summary, string body, Action onContinue, string buttonLabel = "보드로 돌아가기",
            string secondLabel = null, Action second = null)
        {
            if (secondLabel != null) ShowResult(title, body, buttonLabel, onContinue, secondLabel, second);
            else ShowResult(title, body, onContinue, buttonLabel);
            EnsureResultDrops();
            var drop = victory ? _victoryDrop : _defeatDrop;
            if (drop == null) return;

            // 판 아래 칸: 제목 줄 없이 자세한 내용 + 버튼
            _resultTitle.gameObject.SetActive(false);
            _resultBodyRect.anchorMin = new Vector2(0.06f, 0.19f);
            _resultBodyRect.anchorMax = new Vector2(0.94f, 0.96f);
            _resultCard.anchorMin = _resultCard.anchorMax = new Vector2(0.5f, 1f);
            _resultCard.pivot = new Vector2(0.5f, 1f);
            _resultCard.sizeDelta = new Vector2(470f, 260f);
            _resultCard.anchoredPosition = new Vector2(0f, -(drop.BoardBottom + 6f));
            ApplyResultItems(victory); // 11/12 얻은 아이템 그림·등급 색 띠
            // 11/13 팀장 지적: 아이템이 있으면 칸이 화면 아래로 넘쳐 끝이 안 보임 → 화면 안(아래 여백 18)에서 끊음
            float canvasH = _canvasRect != null && _canvasRect.rect.height > 1f ? _canvasRect.rect.height : 720f;
            float room = canvasH - (drop.BoardBottom + 6f) - 18f;
            if (_resultCard.sizeDelta.y > room) _resultCard.sizeDelta = new Vector2(_resultCard.sizeDelta.x, Mathf.Max(200f, room));
            _resultCard.gameObject.SetActive(false);
            _shownDrop = drop;
            drop.Play(title, summary);
            if (_resultReveal != null) StopCoroutine(_resultReveal);
            _resultReveal = StartCoroutine(RevealResultCard(drop));
        }

        private IEnumerator RevealResultCard(PanelDropView drop)
        {
            bool second = _resultSecondButton.activeSelf;
            _resultButtonRect.gameObject.SetActive(false);
            _resultSecondButton.SetActive(false);
            float landAt = Time.unscaledTime + drop.Drop.startDelay + drop.Drop.fallDuration;
            yield return new WaitUntil(() => Time.unscaledTime >= landAt || !drop.Drop.IsPlaying);
            _resultCard.gameObject.SetActive(true); // 판이 닿는 순간 자세한 내용
            yield return new WaitUntil(() => !drop.Drop.IsPlaying);
            _resultButtonRect.gameObject.SetActive(true); // 판이 멈춘 뒤 버튼 (입력 두 번 방지)
            _resultSecondButton.SetActive(second);
            _resultReveal = null;
        }

        private void UseCardLayout()
        {
            HideResultItems();
            if (_resultReveal != null) { StopCoroutine(_resultReveal); _resultReveal = null; }
            if (_shownDrop != null) { _shownDrop.Hide(); _shownDrop = null; }
            _resultTitle.gameObject.SetActive(true);
            _resultBodyRect.anchorMin = new Vector2(0.06f, 0.19f);
            _resultBodyRect.anchorMax = new Vector2(0.94f, 0.81f);
            _resultCard.anchorMin = new Vector2(0.32f, 0.27f);
            _resultCard.anchorMax = new Vector2(0.68f, 0.78f);
            _resultCard.pivot = new Vector2(0.5f, 0.5f);
            _resultCard.offsetMin = _resultCard.offsetMax = Vector2.zero;
            _resultCard.gameObject.SetActive(true);
            _resultButtonRect.gameObject.SetActive(true);
        }

        private void EnsureResultDrops()
        {
            if (_victoryDrop != null || _defeatDrop != null) return;
            _victoryDrop = PanelDropView.Create(_resultPanel.transform, PanelDropView.Victory, 640f, 0f, 34, 15);
            _defeatDrop = PanelDropView.Create(_resultPanel.transform, PanelDropView.Defeat, 640f, 0f, 34, 15);
        }

        // 11/11 UI 순서 규칙: 결과 판이 떠 있는 동안 알림은 기다림 (UIFlow)
        private static BattleHud _active;
        private void OnEnable() => _active = this;
        public static bool ResultOpen => _active != null && _active._resultPanel != null && _active._resultPanel.activeInHierarchy;

        public bool DevResultDropPlaying => _shownDrop != null && _shownDrop.Root.activeSelf;
        public string DevResultSummary => _shownDrop != null ? _shownDrop.Summary.text : "";
        public bool DevResultButtonsVisible => _resultCard != null && _resultCard.gameObject.activeInHierarchy && _resultButtonRect.gameObject.activeSelf;

        public void DevPressResultSecond() => _onResultSecond?.Invoke();
        public string DevResultTitle => _resultTitle != null ? _resultTitle.text : "";
        public string DevResultButtons => _resultButtonLabel == null ? "" : _resultSecondButton != null && _resultSecondButton.activeSelf ? $"{_resultButtonLabel.text} / {_resultSecondLabel.text}" : _resultButtonLabel.text;
    }
}
