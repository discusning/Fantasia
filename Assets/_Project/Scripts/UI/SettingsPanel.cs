using System;
using System.Collections.Generic;
using System.Linq;
using Fantasia.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 설정 화면 (세이브 추가 수정안 2) — 5개: 전체·음악·효과음 볼륨, 해상도, 전체 화면 + [기본값으로 초기화].
    // 볼륨: 바꾸는 즉시 반영·저장. 해상도·전체 화면: [적용] → 15초 유지 확인, [유지]면 저장, [되돌리기]·시간 초과면 이전 설정 복구.
    public class SettingsPanel : MonoBehaviour
    {
        public const float KeepSeconds = 15f;

        private static SettingsPanel _instance;
        public static bool IsOpen => _instance != null && _instance._root.activeSelf;

        private static readonly Color Gold = new Color(0.72f, 0.6f, 0.38f);
        private static readonly Color Fill = new Color(0.08f, 0.07f, 0.09f, 0.97f);
        private static readonly Color ButtonFill = new Color(0.16f, 0.15f, 0.17f, 1f);
        private static readonly Color AccentFill = new Color(0.45f, 0.18f, 0.16f);
        private static readonly Color TextLight = new Color(0.92f, 0.9f, 0.86f);

        private GameObject _root, _confirm;
        private Text _master, _music, _sfx, _resolution, _fullscreen, _confirmText;
        private List<(int w, int h)> _resolutions = new();
        private int _resIndex;
        private bool _pendingFullscreen;
        private (int w, int h, bool full) _before;
        private float _confirmUntil = -1f;
        private Action _onClose;

        public static void Open(Action onClose = null)
        {
            if (_instance == null) _instance = new GameObject("SettingsPanel").AddComponent<SettingsPanel>().Build();
            _instance._onClose = onClose;
            _instance.LoadDisplayChoices();
            _instance._root.SetActive(true);
            _instance.Refresh();
        }

        private static GameSettings S => GameSettings.Current;

        private void LoadDisplayChoices()
        {
            _resolutions = Screen.resolutions.Select(r => (r.width, r.height)).Distinct().OrderBy(r => r.width * r.height).ToList();
            int w = S.width > 0 ? S.width : Screen.width, h = S.height > 0 ? S.height : Screen.height;
            if (!_resolutions.Contains((w, h))) _resolutions.Add((w, h));
            _resIndex = _resolutions.IndexOf((w, h));
            _pendingFullscreen = S.width > 0 ? S.fullscreen : Screen.fullScreen;
        }

        private void Refresh()
        {
            _master.text = $"{Mathf.RoundToInt(S.master * 100)}%";
            _music.text = $"{Mathf.RoundToInt(S.music * 100)}%";
            _sfx.text = $"{Mathf.RoundToInt(S.sfx * 100)}%";
            var (w, h) = _resolutions.Count > 0 ? _resolutions[_resIndex] : (Screen.width, Screen.height);
            _resolution.text = $"{w} × {h}";
            _fullscreen.text = _pendingFullscreen ? "켬" : "끔";
        }

        // ── 볼륨: 즉시 반영·저장 (10% 단위) ──
        private void StepVolume(int which, int dir)
        {
            float Step(float v) => Mathf.Clamp01(Mathf.Round(v * 10f + dir) / 10f);
            switch (which)
            {
                case 0: S.master = Step(S.master); break;
                case 1: S.music = Step(S.music); break;
                default: S.sfx = Step(S.sfx); break;
            }
            S.ApplyAudio();
            S.Save();
            Refresh();
        }

        // ── 화면: 적용 → 15초 유지 확인 ──
        private void ApplyDisplay(int w, int h, bool full)
        {
            _before = (S.width > 0 ? S.width : Screen.width, S.height > 0 ? S.height : Screen.height, S.width > 0 ? S.fullscreen : Screen.fullScreen);
            Screen.SetResolution(w, h, full ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            _pending = (w, h, full);
            _confirmUntil = Time.unscaledTime + KeepSeconds;
            _confirm.SetActive(true);
        }

        private (int w, int h, bool full) _pending;

        private void KeepDisplay()
        {
            S.width = _pending.w;
            S.height = _pending.h;
            S.fullscreen = _pending.full;
            S.Save();
            _confirm.SetActive(false);
            _confirmUntil = -1f;
            Debug.Log($"[Settings] 화면 유지 — {_pending.w}×{_pending.h} 전체 화면 {_pending.full}");
        }

        private void RevertDisplay()
        {
            Screen.SetResolution(_before.w, _before.h, _before.full ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            _confirm.SetActive(false);
            _confirmUntil = -1f;
            LoadDisplayChoices();
            _resIndex = Mathf.Max(0, _resolutions.IndexOf((_before.w, _before.h)));
            _pendingFullscreen = _before.full;
            Refresh();
            Debug.Log("[Settings] 화면 설정 되돌림");
        }

        private void Update()
        {
            if (_confirmUntil < 0f) return;
            float left = _confirmUntil - Time.unscaledTime;
            if (left <= 0f) { RevertDisplay(); return; }
            _confirmText.text = $"이 화면 설정을 유지할까요?\n{Mathf.CeilToInt(left)}초 뒤 이전 설정으로 돌아갑니다.";
        }

        private void ResetDefaults()
        {
            var d = GameSettings.Defaults();
            S.master = d.master;
            S.music = d.music;
            S.sfx = d.sfx;
            S.ApplyAudio();
            S.Save();
            _resIndex = Mathf.Max(0, _resolutions.IndexOf((d.width, d.height)));
            if (_resIndex < 0 || !_resolutions.Contains((d.width, d.height))) { _resolutions.Add((d.width, d.height)); _resIndex = _resolutions.Count - 1; }
            _pendingFullscreen = d.fullscreen;
            Refresh();
            ApplyDisplay(d.width, d.height, d.fullscreen); // 화면은 같은 유지 확인
        }

        private void Close()
        {
            if (_confirmUntil >= 0f) RevertDisplay();
            _root.SetActive(false);
            _onClose?.Invoke();
        }

        // ───────────── 구성 ─────────────
        private SettingsPanel Build()
        {
            var canvasGO = new GameObject("SettingsCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvasGO.AddComponent<GraphicRaycaster>();
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var dim = UGUIKit.CreateImage(canvasGO.transform, "Dim", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.6f));
            _root = dim.gameObject;
            var win = UGUIKit.CreateThemedPanel(dim.transform, "Window", new Vector2(0.3f, 0.15f), new Vector2(0.7f, 0.85f), Gold, Fill, 2f, UITheme.Current != null ? UITheme.Current.PanelFrame : null);
            Label(win, "Title", new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.97f), "설정", 20, Gold, true);

            _master = Row(win, 0, "전체 볼륨", () => StepVolume(0, -1), () => StepVolume(0, +1));
            _music = Row(win, 1, "음악", () => StepVolume(1, -1), () => StepVolume(1, +1));
            _sfx = Row(win, 2, "효과음", () => StepVolume(2, -1), () => StepVolume(2, +1));
            _resolution = Row(win, 3, "해상도", () => { _resIndex = (_resIndex - 1 + _resolutions.Count) % Math.Max(1, _resolutions.Count); Refresh(); },
                () => { _resIndex = (_resIndex + 1) % Math.Max(1, _resolutions.Count); Refresh(); });
            _fullscreen = Row(win, 4, "전체 화면", () => { _pendingFullscreen = !_pendingFullscreen; Refresh(); }, () => { _pendingFullscreen = !_pendingFullscreen; Refresh(); });

            UGUIKit.CreateThemedButton(win, "ApplyDisplay", new Vector2(0.55f, 0.2f), new Vector2(0.93f, 0.28f), "화면 설정 적용", AccentFill, 13)
                .onClick.AddListener(() => { var (w, h) = _resolutions[_resIndex]; ApplyDisplay(w, h, _pendingFullscreen); });
            UGUIKit.CreateThemedButton(win, "Reset", new Vector2(0.07f, 0.06f), new Vector2(0.45f, 0.14f), "기본값으로 초기화", ButtonFill, 13).onClick.AddListener(ResetDefaults);
            UGUIKit.CreateThemedButton(win, "Close", new Vector2(0.55f, 0.06f), new Vector2(0.93f, 0.14f), "닫기", ButtonFill, 13).onClick.AddListener(Close);

            // 화면 유지 확인
            var cdim = UGUIKit.CreateImage(dim.transform, "Confirm", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.5f));
            _confirm = cdim.gameObject;
            var box = UGUIKit.CreateThemedPanel(cdim.transform, "Box", new Vector2(0.33f, 0.38f), new Vector2(0.67f, 0.62f), Gold, Fill, 2f);
            _confirmText = Label(box, "Text", new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.92f), "", 14, TextLight, false);
            UGUIKit.CreateThemedButton(box, "Revert", new Vector2(0.08f, 0.1f), new Vector2(0.46f, 0.34f), "되돌리기", ButtonFill, 14).onClick.AddListener(RevertDisplay);
            UGUIKit.CreateThemedButton(box, "Keep", new Vector2(0.54f, 0.1f), new Vector2(0.92f, 0.34f), "유지", AccentFill, 14).onClick.AddListener(KeepDisplay);
            _confirm.SetActive(false);

            _root.SetActive(false);
            return this;
        }

        private Text Row(Transform win, int index, string label, Action left, Action right)
        {
            float y1 = 0.82f - index * 0.11f;
            Label(win, $"Label{index}", new Vector2(0.07f, y1 - 0.08f), new Vector2(0.4f, y1), label, 14, TextLight, false).alignment = TextAnchor.MiddleLeft;
            UGUIKit.CreateThemedButton(win, $"Prev{index}", new Vector2(0.45f, y1 - 0.08f), new Vector2(0.53f, y1), "◀", ButtonFill, 12).onClick.AddListener(() => left());
            var value = Label(win, $"Value{index}", new Vector2(0.53f, y1 - 0.08f), new Vector2(0.85f, y1), "", 14, Gold, true);
            UGUIKit.CreateThemedButton(win, $"Next{index}", new Vector2(0.85f, y1 - 0.08f), new Vector2(0.93f, y1), "▶", ButtonFill, 12).onClick.AddListener(() => right());
            return value;
        }

        private static Text Label(Transform parent, string name, Vector2 min, Vector2 max, string content, int size, Color color, bool bold)
        {
            var t = UGUIKit.CreateText(parent, name, min, max, content, size, TextAnchor.MiddleCenter);
            t.color = color;
            if (bold) t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            return t;
        }

        // 자동 플레이 점검용
        public static void DevStepVolume(int which, int dir) { if (IsOpen) _instance.StepVolume(which, dir); }
        public static void DevClose() { if (IsOpen) _instance.Close(); }
    }
}
