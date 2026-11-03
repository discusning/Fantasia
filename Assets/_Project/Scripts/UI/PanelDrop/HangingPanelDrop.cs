using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Fantasia.UI
{
    /// <summary>
    /// Uses the unchanged source artwork as a UV atlas. No Animator or tween package is needed.
    /// Texture rectangles use TOP-LEFT pixel coordinates, as in an image editor.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Fantasia/UI/Hanging Panel Drop")]
    public sealed class HangingPanelDrop : MonoBehaviour
    {
        [Header("Original artwork / Sound")]
        public Texture2D artwork;
        public AudioClip chainRattle;
        public AudioClip panelStop;

        [Header("Playback")]
        public bool playOnEnable = true;
        public bool useUnscaledTime = true;
        public bool loop;
        [Min(0f)] public float loopDelay = 2f;
        [Min(0f)] public float startDelay = 0.12f;
        [Min(0.1f)] public float fallDuration = 0.55f;
        [Min(0.1f)] public float settleDuration = 1.05f;

        [Header("Motion (Canvas reference units)")]
        [Min(0f)] public float dropDistance = 950f;
        [Range(1f, 4f)] public float fallExponent = 2.2f;
        [Min(0f)] public float overshoot = 20f;
        [Range(0f, 8f)] public float swingAngle = 2.2f;
        [Range(0f, 12f)] public float chainJitter = 3.5f;

        [Header("Sound volume")]
        [Range(0f, 1f)] public float chainVolume = 0.55f;
        [Range(0f, 1f)] public float stopVolume = 0.7f;

        [Header("Source rectangles: x, y from TOP LEFT, width, height")]
        public Rect boardPixels = new Rect(67f, 338f, 1404f, 550f);
        public Rect leftChainPixels = new Rect(196f, 2f, 104f, 336f);
        public Rect rightChainPixels = new Rect(1240f, 2f, 104f, 336f);

        [Header("Optional top ornament / Content safe area")]
        [Tooltip("Separate source region above the board. Zero size disables it.")]
        public Rect topDecorationPixels = new Rect(0f, 0f, 0f, 0f);
        [Tooltip("Content insets in source pixels: left, bottom, right, top.")]
        public Vector4 contentInsets = new Vector4(75f, 55f, 75f, 105f);

        [Header("Callbacks")]
        public UnityEvent onLanded = new UnityEvent();
        public UnityEvent onFinished = new UnityEvent();

        [SerializeField, HideInInspector] private RectTransform board;
        [SerializeField, HideInInspector] private RectTransform content;
        [SerializeField, HideInInspector] private RawImage boardImage;
        [SerializeField, HideInInspector] private RawImage topDecorationImage;
        [SerializeField, HideInInspector] private RawImage[] leftPieces;
        [SerializeField, HideInInspector] private RawImage[] rightPieces;

        private const int PieceCount = 8;
        private RectTransform root;
        private AudioSource audioSource;
        private float elapsed;
        private float loopElapsed;
        private float activeDrop;
        private bool running;
        private bool waitingForLoop;
        private bool rattlePlayed;
        private bool landed;
        private bool hidden;
        private int playVersion;

        public bool IsPlaying { get { return running; } }
        public RectTransform ContentRoot { get { EnsureArtwork(); return content; } }

        private void Awake()
        {
            EnsureArtwork();
        }

        private void OnEnable()
        {
            if (playOnEnable) Play();
            else ShowImmediate();
        }

        private void OnDisable()
        {
            ++playVersion;
            running = false;
            waitingForLoop = false;
            if (audioSource != null) audioSource.Stop();
        }

        private void Update()
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (running)
            {
                elapsed += dt;
                EvaluatePlayback();
            }
            else if (waitingForLoop)
            {
                if (!loop) { waitingForLoop = false; return; }
                loopElapsed += dt;
                if (loopElapsed >= Mathf.Max(0f, loopDelay)) Play();
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            if (artwork == null || board == null) return;
            LayoutArtwork();
            if (!hidden)
            {
                if (running) ApplyPlaybackPose();
                else ApplyPose(0f, 0f, 0f, 0f);
            }
        }

        [ContextMenu("Play Drop (Play Mode)")]
        public void Play()
        {
            if (!Application.isPlaying) { BuildPreview(); return; }
            if (!isActiveAndEnabled || !EnsureArtwork()) return;
            ++playVersion;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
                audioSource.ignoreListenerPause = true;
            }
            audioSource.Stop();
            activeDrop = GetOffscreenDrop();
            elapsed = 0f;
            loopElapsed = 0f;
            rattlePlayed = false;
            landed = false;
            waitingForLoop = false;
            running = true;
            hidden = false;
            board.gameObject.SetActive(true);
            ApplyPlaybackPose();
        }

        [ContextMenu("Show Immediately")]
        public void ShowImmediate()
        {
            ++playVersion;
            running = false;
            waitingForLoop = false;
            hidden = false;
            if (audioSource != null) audioSource.Stop();
            if (!EnsureArtwork()) return;
            board.gameObject.SetActive(true);
            ApplyPose(0f, 0f, 0f, 0f);
        }

        [ContextMenu("Hide Immediately")]
        public void HideImmediate()
        {
            ++playVersion;
            running = false;
            waitingForLoop = false;
            hidden = true;
            if (audioSource != null) audioSource.Stop();
            if (board != null) board.gameObject.SetActive(false);
            SetChainVisible(leftPieces, false);
            SetChainVisible(rightPieces, false);
        }

        // Called by the custom Inspector / demo menu; also safe in Edit Mode.
        public void BuildPreview()
        {
            if (EnsureArtwork()) ShowImmediate();
        }

        private void EvaluatePlayback()
        {
            int version = playVersion;
            ApplyPlaybackPose();
            float t = elapsed - Mathf.Max(0f, startDelay);
            float fall = Mathf.Max(0.1f, fallDuration);
            if (!rattlePlayed && t >= fall * 0.42f)
            {
                rattlePlayed = true;
                if (chainRattle != null && audioSource != null)
                    audioSource.PlayOneShot(chainRattle, chainVolume);
            }
            if (!landed && t >= fall)
            {
                landed = true;
                if (panelStop != null && audioSource != null)
                    audioSource.PlayOneShot(panelStop, stopVolume);
                onLanded.Invoke();
                if (!this || !isActiveAndEnabled || version != playVersion) return;
            }
            if (t >= fall + Mathf.Max(0.1f, settleDuration))
            {
                running = false;
                ApplyPose(0f, 0f, 0f, 0f);
                waitingForLoop = loop;
                loopElapsed = 0f;
                onFinished.Invoke();
            }
        }

        private void ApplyPlaybackPose()
        {
            float t = elapsed - Mathf.Max(0f, startDelay);
            float fall = Mathf.Max(0.1f, fallDuration);
            float factor = GetEffectScale();
            if (t < 0f)
            {
                ApplyPose(activeDrop, 0f, 0f, 0f);
            }
            else if (t < fall)
            {
                float u = Mathf.Clamp01(t / fall);
                float offset = activeDrop * (1f - Mathf.Pow(u, Mathf.Max(1f, fallExponent)));
                float angle = -swingAngle * 0.38f * Mathf.Sin(u * Mathf.PI);
                ApplyPose(offset, angle, t, Mathf.SmoothStep(0f, 1f, u) * 0.85f);
            }
            else
            {
                float s = t - fall;
                float end = Mathf.Max(0.1f, settleDuration);
                float fade = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(end * 0.68f, end, s));
                float decay = Mathf.Exp(-5.3f * s) * fade;
                float offset = -overshoot * factor * decay * Mathf.Sin(2f * Mathf.PI * 3.9f * s);
                float angle = swingAngle * decay * Mathf.Sin(2f * Mathf.PI * 3.15f * s);
                ApplyPose(offset, angle, t, decay);
            }
        }

        private float GetOffscreenDrop()
        {
            float distance = Mathf.Max(0f, dropDistance) * GetEffectScale();
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                RectTransform c = canvas.rootCanvas.transform as RectTransform;
                if (c != null)
                {
                    float canvasTop = root.InverseTransformPoint(
                        c.TransformPoint(new Vector3(0f, c.rect.yMax, 0f))).y;
                    float s = GetArtScale();
                    float boardBottom = (artwork.height * 0.5f - boardPixels.yMax) * s;
                    distance = Mathf.Max(distance, canvasTop - boardBottom + 36f * GetEffectScale());
                }
            }
            return distance;
        }

        private void ApplyPose(float yOffset, float angle, float shakeTime, float shake)
        {
            if (board == null || artwork == null) return;
            float scale = GetArtScale();
            Vector2 restTop = new Vector2(
                (boardPixels.center.x - artwork.width * 0.5f) * scale,
                (artwork.height * 0.5f - boardPixels.y) * scale);
            board.anchoredPosition = restTop + Vector2.up * yOffset;
            board.localRotation = Quaternion.Euler(0f, 0f, angle);
            UpdateChain(leftPieces, leftChainPixels, scale, angle, shakeTime, shake, 0f);
            UpdateChain(rightPieces, rightChainPixels, scale, angle, shakeTime, shake, 1.8f);
        }

        private void UpdateChain(RawImage[] pieces, Rect pixels, float scale,
            float boardAngle, float time, float shake, float phase)
        {
            if (pieces == null || pieces.Length != PieceCount) return;
            Vector2 anchor = new Vector2(
                (pixels.center.x - artwork.width * 0.5f) * scale,
                (artwork.height * 0.5f - pixels.y) * scale);
            Vector2 localAttach = new Vector2((pixels.center.x - boardPixels.center.x) * scale, 0f);
            float rad = boardAngle * Mathf.Deg2Rad;
            Vector2 rotated = new Vector2(localAttach.x * Mathf.Cos(rad), localAttach.x * Mathf.Sin(rad));
            Vector2 end = board.anchoredPosition + rotated;
            Vector2 delta = end - anchor;
            if (delta.y >= -0.01f) { SetChainVisible(pieces, false); return; }
            float length = delta.magnitude;
            Vector2 direction = delta / length;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float stretch = Mathf.Max(1f, length / (pixels.height * scale));
            float chainAngle = Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg;
            float chunk = pixels.height / PieceCount;
            for (int i = 0; i < PieceCount; ++i)
            {
                RawImage piece = pieces[i];
                if (piece == null) continue;
                float sourceOffset = i * chunk;
                float start = sourceOffset * scale * stretch;
                float remaining = length - start;
                bool visible = remaining > 0.02f;
                if (piece.gameObject.activeSelf != visible) piece.gameObject.SetActive(visible);
                if (!visible) continue;
                // Reveals existing links without stretching them during deployment.
                // One source-pixel overlap hides joins while the pieces rattle.
                float available = Mathf.Min(chunk + 1f, pixels.height - sourceOffset);
                float sourceHeight = Mathf.Min(available, remaining / (scale * stretch));
                Rect sample = new Rect(pixels.x, pixels.y + sourceOffset, pixels.width, sourceHeight);
                piece.uvRect = ToUV(sample);
                RectTransform rt = piece.rectTransform;
                rt.sizeDelta = new Vector2(pixels.width * scale, sourceHeight * scale * stretch);
                float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(start / length));
                float wave = Mathf.Sin(time * 77f + i * 1.47f + phase)
                    + 0.3f * Mathf.Sin(time * 113f - i * 0.8f + phase);
                float bend = wave * chainJitter * GetEffectScale() * shake * envelope;
                rt.anchoredPosition = anchor + direction * start + normal * bend;
                rt.localRotation = Quaternion.Euler(0f, 0f,
                    chainAngle + wave * 1.2f * shake * envelope);
            }
        }

        private bool EnsureArtwork()
        {
            if (artwork == null) return false;
            if (root == null) root = (RectTransform)transform;
            if (board == null) board = GetOrCreateRect(root, "Board");
            if (boardImage == null)
            {
                boardImage = board.GetComponent<RawImage>();
                if (boardImage == null) boardImage = board.gameObject.AddComponent<RawImage>();
            }
            boardImage.texture = artwork;
            boardImage.raycastTarget = false;
            boardImage.color = Color.white;
            boardImage.uvRect = ToUV(boardPixels);
            if (topDecorationPixels.width > 0f && topDecorationPixels.height > 0f)
            {
                if (topDecorationImage == null)
                {
                    RectTransform decoration = GetOrCreateRect(board, "TopDecoration");
                    topDecorationImage = decoration.GetComponent<RawImage>();
                    if (topDecorationImage == null)
                        topDecorationImage = decoration.gameObject.AddComponent<RawImage>();
                }
                topDecorationImage.gameObject.SetActive(true);
                topDecorationImage.texture = artwork;
                topDecorationImage.color = Color.white;
                topDecorationImage.raycastTarget = false;
                topDecorationImage.uvRect = ToUV(topDecorationPixels);
                topDecorationImage.rectTransform.SetAsFirstSibling();
            }
            else if (topDecorationImage != null)
                topDecorationImage.gameObject.SetActive(false);
            if (content == null) content = GetOrCreateRect(board, "Content");
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 0.5f);
            RectTransform chains = GetOrCreateRect(root, "Chains");
            chains.anchorMin = chains.anchorMax = new Vector2(0.5f, 0.5f);
            chains.anchoredPosition = Vector2.zero;
            chains.sizeDelta = Vector2.zero;
            chains.SetAsFirstSibling();
            leftPieces = EnsurePieces(chains, "Left", leftPieces);
            rightPieces = EnsurePieces(chains, "Right", rightPieces);
            LayoutArtwork();
            return true;
        }

        private RawImage[] EnsurePieces(RectTransform parent, string prefix, RawImage[] existing)
        {
            if (existing == null || existing.Length != PieceCount) existing = new RawImage[PieceCount];
            for (int i = 0; i < PieceCount; ++i)
            {
                if (existing[i] == null)
                {
                    RectTransform rt = GetOrCreateRect(parent, prefix + "_" + i.ToString("00"));
                    existing[i] = rt.GetComponent<RawImage>();
                    if (existing[i] == null) existing[i] = rt.gameObject.AddComponent<RawImage>();
                }
                existing[i].texture = artwork;
                existing[i].color = Color.white;
                existing[i].raycastTarget = false;
                RectTransform tr = existing[i].rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
                tr.pivot = new Vector2(0.5f, 1f);
            }
            return existing;
        }

        private void LayoutArtwork()
        {
            if (board == null || artwork == null) return;
            float s = GetArtScale();
            board.anchorMin = board.anchorMax = new Vector2(0.5f, 0.5f);
            board.pivot = new Vector2(0.5f, 1f);
            board.localScale = Vector3.one;
            board.sizeDelta = boardPixels.size * s;
            if (topDecorationImage != null && topDecorationPixels.width > 0f)
            {
                RectTransform decoration = topDecorationImage.rectTransform;
                decoration.anchorMin = decoration.anchorMax = new Vector2(0.5f, 1f);
                decoration.pivot = new Vector2(0.5f, 1f);
                decoration.localScale = Vector3.one;
                decoration.localRotation = Quaternion.identity;
                decoration.anchoredPosition = new Vector2(
                    topDecorationPixels.center.x - boardPixels.center.x,
                    boardPixels.y - topDecorationPixels.y) * s;
                decoration.sizeDelta = topDecorationPixels.size * s;
            }
            if (content != null)
            {
                content.offsetMin = new Vector2(contentInsets.x, contentInsets.y) * s;
                content.offsetMax = new Vector2(-contentInsets.z, -contentInsets.w) * s;
            }
        }

        private float GetArtScale()
        {
            if (root == null) root = (RectTransform)transform;
            return Mathf.Max(1f, root.rect.width) / Mathf.Max(1f, artwork.width);
        }

        private float GetEffectScale()
        {
            if (root == null) root = (RectTransform)transform;
            return Mathf.Max(1f, root.rect.width) / 1100f;
        }

        private Rect ToUV(Rect topLeftPixels)
        {
            return new Rect(topLeftPixels.x / artwork.width,
                1f - topLeftPixels.yMax / artwork.height,
                topLeftPixels.width / artwork.width,
                topLeftPixels.height / artwork.height);
        }

        private static RectTransform GetOrCreateRect(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null) return found as RectTransform;
            GameObject child = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            child.layer = parent.gameObject.layer;
            return rect;
        }

        private static void SetChainVisible(RawImage[] pieces, bool visible)
        {
            if (pieces == null) return;
            foreach (RawImage piece in pieces)
                if (piece != null && piece.gameObject.activeSelf != visible)
                    piece.gameObject.SetActive(visible);
        }
    }
}
