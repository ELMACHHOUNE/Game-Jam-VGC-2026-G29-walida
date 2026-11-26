using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds and animates a polished dark-mode smartphone Voice Memo / Signal Recorder UI
/// directly inside the phone's screen area (World-Space Canvas parented under PhoneVisual,
/// layered cleanly behind the PhoneAndHandsCollage bezel).
/// Uses procedurally generated anti-aliased SDF rounded rectangles, circles, and rings.
/// </summary>
public class PhoneRecorderScreenUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpatialAudioPhoneController audioController;
    [SerializeField] private RecordableSoundSource primaryTargetSource;
    [SerializeField] private Transform phoneVisualParent;

    [Header("Screen Layout (World Units)")]
    [SerializeField] private Vector3 screenLocalPosition = new Vector3(0f, 0.28f, -0.01f);
    [SerializeField] private Vector2 screenWorldSize = new Vector2(1.94f, 4.44f);
    [SerializeField] private int canvasSortingOrder = 12;
    [SerializeField] private int waveformBarCount = 21;

    [Header("Palette (L'Walida art direction)")]
    [Tooltip("Midnight Blue #183744")]
    [SerializeField] private Color screenBgColor = new Color(0.094f, 0.216f, 0.267f, 0.98f);
    [Tooltip("Deep Oudaya Blue")]
    [SerializeField] private Color cardBgColor = new Color(0.122f, 0.290f, 0.369f, 0.95f);
    [Tooltip("Chalk White #F2EDE3")]
    [SerializeField] private Color chalkWhite = new Color(0.949f, 0.929f, 0.890f, 1f);
    [Tooltip("Hassan Sand #C8A477")]
    [SerializeField] private Color hassanSand = new Color(0.784f, 0.643f, 0.467f, 1f);
    [Tooltip("Terracotta #AB6048")]
    [SerializeField] private Color terracotta = new Color(0.671f, 0.376f, 0.282f, 1f);
    [Tooltip("Signal locked: Olive Green, lightened to read on the dark screen")]
    [SerializeField] private Color lockedEmerald = new Color(0.639f, 0.706f, 0.525f, 1f);
    [Tooltip("Recording: warm terracotta red")]
    [SerializeField] private Color recCrimson = new Color(0.761f, 0.333f, 0.235f, 1f);

    private readonly List<Texture2D> _generatedTextures = new List<Texture2D>();
    private readonly List<Sprite> _generatedSprites = new List<Sprite>();

    private Sprite _roundedRectSprite;
    private Sprite _circleSprite;
    private Sprite _ringSprite;
    private Font _uiFont;
    private Font _headerFont;

    private Image[] _statusSignalBars;
    private Image _statusPillBg;
    private Text _statusPillText;
    private Text _sourceTitleText;
    private Image[] _waveformBars;
    private float[] _waveformHeights;
    private Text _timerText;
    private Text _subHintText;
    private RectTransform _progressFillRect;
    private Image _progressFillImg;
    private Image _recOuterGlow;
    private Image _recInnerShape;
    private RectTransform _recInnerRect;
    private Image _recDotImg;
    private Image _screenFlashImg;

    private float _flashAlpha;
    private float _savedBannerTimer;
    private float _progressTrackWidth;

    private void Awake()
    {
        if (audioController == null)
        {
            audioController = FindAnyObjectByType<SpatialAudioPhoneController>();
        }

        if (phoneVisualParent == null)
        {
            phoneVisualParent = transform;
        }

        _roundedRectSprite = CreateRoundedRectSprite(96, 96, 22);
        _circleSprite = CreateCircleSprite(96);
        _ringSprite = CreateRingSprite(128, 0.78f, 0.96f);

        // Same typography as the rest of the game: Lora for readouts, Cinzel for headers
        _uiFont = MedinaUIKit.BodyStrongFont;
        _headerFont = MedinaUIKit.HeaderFont;

        BuildPhoneScreenCanvas();
    }

    private void OnEnable()
    {
        if (audioController != null)
        {
            audioController.OnRecordingSaved += HandleRecordingSaved;
            audioController.OnSoundRecorded += HandleSoundRecorded;
        }
    }

    private void OnDisable()
    {
        if (audioController != null)
        {
            audioController.OnRecordingSaved -= HandleRecordingSaved;
            audioController.OnSoundRecorded -= HandleSoundRecorded;
        }
    }

    private void HandleRecordingSaved(AudioClip clip, string path)
    {
        _flashAlpha = 0.72f;
    }

    private void HandleSoundRecorded(RecordableSoundSource src)
    {
        _flashAlpha = 0.85f;
        _savedBannerTimer = 4.5f;
    }

    private void Update()
    {
        if (audioController == null) return;

        float dt = Time.unscaledDeltaTime;
        float t = Time.unscaledTime;

        RecordableSoundSource activeSource = audioController.StrongestSource != null
            ? audioController.StrongestSource
            : primaryTargetSource;
        float signal = audioController.StrongestSource != null
            ? audioController.StrongestSignal
            : (activeSource != null ? activeSource.CurrentSignalStrength : 0f);
        float requiredSignal = activeSource != null ? activeSource.RequiredSignalStrength : 0.65f;
        bool isLocked = signal >= requiredSignal;
        bool isRecording = audioController.IsRecording;

        if (_savedBannerTimer > 0f)
        {
            _savedBannerTimer -= dt;
        }

        // 1. Update top-right signal bars (4 bars)
        if (_statusSignalBars != null)
        {
            for (int i = 0; i < _statusSignalBars.Length; i++)
            {
                float threshold = (i + 1) * 0.22f;
                bool lit = signal >= threshold;
                if (lit)
                {
                    _statusSignalBars[i].color = isLocked ? lockedEmerald : hassanSand;
                }
                else
                {
                    _statusSignalBars[i].color = new Color(1f, 1f, 1f, 0.20f);
                }
            }
        }

        // 2. Update status pill & title
        if (isRecording)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 10f);
            _statusPillBg.color = new Color(recCrimson.r, recCrimson.g, recCrimson.b, 0.28f * pulse + 0.15f);
            _statusPillText.text = "●  RECORDING";
            _statusPillText.color = recCrimson;
            _sourceTitleText.text = isLocked && activeSource != null ? activeSource.DisplayName.ToUpperInvariant() : "CAPTURING AUDIO";
            _subHintText.text = isLocked ? "HOLD STEADY..." : "MOVE CLOSER FOR CLEAR SIGNAL";
        }
        else if (_savedBannerTimer > 0f)
        {
            _statusPillBg.color = new Color(lockedEmerald.r, lockedEmerald.g, lockedEmerald.b, 0.24f);
            _statusPillText.text = "✓  SAVED TO JOURNAL";
            _statusPillText.color = lockedEmerald;
            _sourceTitleText.text = activeSource != null ? activeSource.DisplayName.ToUpperInvariant() : "SOUK MERCHANTS";
            _subHintText.text = "MEMORY CAPTURED";
        }
        else if (isLocked)
        {
            _statusPillBg.color = new Color(lockedEmerald.r, lockedEmerald.g, lockedEmerald.b, 0.22f);
            _statusPillText.text = $"LOCKED  ·  {Mathf.RoundToInt(signal * 100f)}%";
            _statusPillText.color = lockedEmerald;
            _sourceTitleText.text = activeSource != null ? activeSource.DisplayName.ToUpperInvariant() : "SIGNAL CLEAR";
            _subHintText.text = "HOLD CLICK / SPACE / R";
        }
        else if (signal > 0.22f)
        {
            _statusPillBg.color = new Color(hassanSand.r, hassanSand.g, hassanSand.b, 0.22f);
            _statusPillText.text = $"TUNING  ·  {Mathf.RoundToInt(signal * 100f)}%";
            _statusPillText.color = hassanSand;
            _sourceTitleText.text = "PICKING UP VOICES...";
            _subHintText.text = "PAN PHONE TO FOCUS";
        }
        else
        {
            _statusPillBg.color = new Color(1f, 1f, 1f, 0.08f);
            _statusPillText.text = "STATIC  ·  SEARCHING";
            _statusPillText.color = new Color(chalkWhite.r, chalkWhite.g, chalkWhite.b, 0.72f);
            _sourceTitleText.text = "SOUK FREQUENCY";
            _subHintText.text = "PAN AROUND THE SOUK";
        }

        // 3. Animate 21-bar audio waveform visualizer
        float liveRms = audioController.AudioCaptureTap != null
            ? Mathf.Clamp01(audioController.AudioCaptureTap.CurrentRmsLevel * 5.2f)
            : 0f;
        float energy = Mathf.Max(liveRms, signal * 0.78f);
        Color barBaseCol = isRecording
            ? recCrimson
            : (isLocked ? lockedEmerald : Color.Lerp(new Color(0.55f, 0.68f, 0.76f, 0.85f), hassanSand, signal));

        float midIdx = (_waveformBars.Length - 1) * 0.5f;
        for (int i = 0; i < _waveformBars.Length; i++)
        {
            float distFromCenter = Mathf.Abs(i - midIdx) / Mathf.Max(1f, midIdx);
            float envelope = Mathf.Lerp(1f, 0.28f, distFromCenter * distFromCenter);

            // Static jitter when weak, rich rhythmic audio waves when locked/recording
            float staticJitter = (1f - signal) * 0.14f * (0.5f + 0.5f * Mathf.Sin(t * (26f + i * 5.3f) + i * 2.1f));
            float audioWave = energy * (0.45f + 0.55f * Mathf.Sin(t * (7.5f + (i % 5) * 2.3f) + i * 0.85f));
            float recBoost = isRecording ? 0.16f : 0f;
            float targetNorm = Mathf.Clamp01(0.08f + (staticJitter + audioWave + recBoost) * envelope);

            _waveformHeights[i] = Mathf.Lerp(_waveformHeights[i], targetNorm, 1f - Mathf.Exp(-18f * dt));
            RectTransform rt = _waveformBars[i].rectTransform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, Mathf.Lerp(10f, 178f, _waveformHeights[i]));

            Color c = barBaseCol;
            c.a = Mathf.Lerp(0.45f, 1f, _waveformHeights[i]);
            _waveformBars[i].color = c;
        }

        // 4. Update recording timer & progress bar
        float elapsed = audioController.ElapsedRecordTime;
        int mins = Mathf.FloorToInt(elapsed / 60f);
        float secs = elapsed - mins * 60f;
        _timerText.text = isRecording ? $"{mins:00}:{secs:00.0}" : "00:00.0";
        _timerText.color = isRecording ? chalkWhite : new Color(chalkWhite.r, chalkWhite.g, chalkWhite.b, 0.58f);

        float progress = audioController.RecordingProgress;
        if (_progressFillRect != null)
        {
            _progressFillRect.sizeDelta = new Vector2(_progressTrackWidth * progress, _progressFillRect.sizeDelta.y);
            _progressFillImg.color = isLocked ? lockedEmerald : recCrimson;
        }

        // 5. Update REC indicator & circular Record button morph
        if (_recDotImg != null)
        {
            float recAlpha = isRecording ? (0.45f + 0.55f * Mathf.Sin(t * 11f)) : 0.18f;
            _recDotImg.color = new Color(recCrimson.r, recCrimson.g, recCrimson.b, recAlpha);
        }

        if (_recInnerRect != null && _recInnerShape != null)
        {
            float targetSize = isRecording ? 52f : 96f;
            float cur = Mathf.Lerp(_recInnerRect.sizeDelta.x, targetSize, 1f - Mathf.Exp(-14f * dt));
            _recInnerRect.sizeDelta = new Vector2(cur, cur);
            _recInnerShape.sprite = isRecording ? _roundedRectSprite : _circleSprite;
        }

        if (_recOuterGlow != null)
        {
            float glowA = isRecording
                ? (0.28f + 0.16f * Mathf.Sin(t * 9f))
                : (isLocked ? (0.14f + 0.08f * Mathf.Sin(t * 4f)) : 0f);
            Color glowCol = isRecording ? recCrimson : lockedEmerald;
            _recOuterGlow.color = new Color(glowCol.r, glowCol.g, glowCol.b, glowA);
        }

        // 6. Screen save flash fade
        if (_screenFlashImg != null && _flashAlpha > 0.001f)
        {
            _flashAlpha = Mathf.MoveTowards(_flashAlpha, 0f, dt * 2.8f);
            _screenFlashImg.color = new Color(chalkWhite.r, chalkWhite.g, chalkWhite.b, _flashAlpha);
        }
    }

    private void BuildPhoneScreenCanvas()
    {
        // High-DPI canvas inside the smartphone screen: 388 x 888 px scaled cleanly to screenWorldSize
        const float canvasW = 388f;
        float canvasH = canvasW * (screenWorldSize.y / Mathf.Max(0.1f, screenWorldSize.x));
        float worldScale = screenWorldSize.x / canvasW;

        GameObject canvasObj = new GameObject("PhoneRecorderScreenCanvas", typeof(RectTransform), typeof(Canvas));
        canvasObj.transform.SetParent(phoneVisualParent, false);
        canvasObj.transform.localPosition = screenLocalPosition;
        canvasObj.transform.localRotation = Quaternion.identity;
        canvasObj.transform.localScale = new Vector3(worldScale, worldScale, 1f);

        Canvas canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = canvasSortingOrder;

        RectTransform root = canvasObj.GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(canvasW, canvasH);

        // Background panel
        Image bg = CreateSpriteImage("ScreenBackdrop", root, _roundedRectSprite, screenBgColor,
            Vector2.zero, new Vector2(canvasW, canvasH));
        bg.type = Image.Type.Sliced;

        // Top subtle header gradient tint band
        CreateRect("TopTint", root, MedinaUIKit.WithAlpha(MedinaUIKit.OudayaBlue, 0.22f),
            new Vector2(0f, canvasH * 0.5f - 85f), new Vector2(canvasW - 12f, 170f));

        // 1. Top Status Bar (time + 4 signal bars + battery)
        float topY = canvasH * 0.5f - 44f;
        CreateText("StatusTime", root, "9:41", 22, FontStyle.Bold, chalkWhite, TextAnchor.MiddleLeft,
            new Vector2(-112f, topY), new Vector2(100f, 34f));

        _statusSignalBars = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            float barH = 10f + i * 5f;
            _statusSignalBars[i] = CreateRect($"SigBar_{i}", root, new Color(1f, 1f, 1f, 0.25f),
                new Vector2(68f + i * 11f, topY - 12f + barH * 0.5f), new Vector2(6f, barH));
        }

        // Battery outline + fill
        CreateSpriteImage("BatteryBody", root, _roundedRectSprite, new Color(1f, 1f, 1f, 0.35f),
            new Vector2(138f, topY), new Vector2(36f, 18f));
        CreateRect("BatteryFill", root, chalkWhite,
            new Vector2(136f, topY), new Vector2(26f, 11f));

        // 2. App Label + Status Pill + Active Source Title
        Text appHeader = CreateText("AppHeader", root, "VOICE MEMOS", 17, FontStyle.Normal,
            new Color(hassanSand.r, hassanSand.g, hassanSand.b, 0.9f), TextAnchor.MiddleCenter,
            new Vector2(0f, canvasH * 0.5f - 108f), new Vector2(320f, 28f));
        appHeader.font = _headerFont;

        // Zellige stars framing the app title
        for (int side = -1; side <= 1; side += 2)
        {
            Image star = CreateSpriteImage(side < 0 ? "HeaderStarLeft" : "HeaderStarRight", root, MedinaUIKit.StarSprite,
                new Color(terracotta.r, terracotta.g, terracotta.b, 0.9f),
                new Vector2(side * 96f, canvasH * 0.5f - 108f), new Vector2(14f, 14f));
            star.type = Image.Type.Simple;
        }

        _statusPillBg = CreateSpriteImage("StatusPillBg", root, _roundedRectSprite, new Color(1f, 1f, 1f, 0.08f),
            new Vector2(0f, canvasH * 0.5f - 156f), new Vector2(286f, 44f));
        _statusPillBg.type = Image.Type.Sliced;
        _statusPillText = CreateText("StatusPillText", _statusPillBg.rectTransform, "STATIC  ·  SEARCHING",
            16, FontStyle.Normal, chalkWhite, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(276f, 40f));
        _statusPillText.font = _headerFont;

        _sourceTitleText = CreateText("SourceTitle", root, "SOUK FREQUENCY", 23, FontStyle.Normal,
            chalkWhite, TextAnchor.MiddleCenter,
            new Vector2(0f, canvasH * 0.5f - 218f), new Vector2(340f, 40f));
        _sourceTitleText.font = _headerFont;

        // 3. Waveform Card (21 rounded bars + center line)
        Image waveCard = CreateSpriteImage("WaveformCard", root, _roundedRectSprite, cardBgColor,
            new Vector2(0f, 38f), new Vector2(336f, 232f));
        waveCard.type = Image.Type.Sliced;

        // Subtle center baseline
        CreateRect("WaveCenterLine", waveCard.rectTransform, new Color(1f, 1f, 1f, 0.12f),
            Vector2.zero, new Vector2(304f, 2f));

        int bars = Mathf.Max(9, waveformBarCount);
        _waveformBars = new Image[bars];
        _waveformHeights = new float[bars];
        float totalSpan = 292f;
        float step = totalSpan / Mathf.Max(1, bars - 1);
        float startX = -totalSpan * 0.5f;

        for (int i = 0; i < bars; i++)
        {
            _waveformBars[i] = CreateSpriteImage($"WaveBar_{i}", waveCard.rectTransform, _roundedRectSprite,
                hassanSand, new Vector2(startX + i * step, 0f), new Vector2(7.5f, 24f));
            _waveformHeights[i] = 0.1f;
        }

        // 4. Digital Timer + REC dot + Sub-hint
        float timerY = -126f;
        _recDotImg = CreateSpriteImage("RecDot", root, _circleSprite, new Color(recCrimson.r, recCrimson.g, recCrimson.b, 0.2f),
            new Vector2(-92f, timerY), new Vector2(18f, 18f));
        _timerText = CreateText("TimerText", root, "00:00.0", 44, FontStyle.Bold, chalkWhite,
            TextAnchor.MiddleCenter, new Vector2(10f, timerY), new Vector2(210f, 56f));

        _subHintText = CreateText("SubHintText", root, "PAN AROUND THE SOUK", 16, FontStyle.Bold,
            new Color(hassanSand.r, hassanSand.g, hassanSand.b, 0.92f), TextAnchor.MiddleCenter,
            new Vector2(0f, -172f), new Vector2(330f, 30f));

        // 5. Recording Progress Bar
        _progressTrackWidth = 304f;
        Image progTrack = CreateSpriteImage("ProgressTrack", root, _roundedRectSprite, new Color(1f, 1f, 1f, 0.12f),
            new Vector2(0f, -214f), new Vector2(_progressTrackWidth, 12f));
        progTrack.type = Image.Type.Sliced;

        GameObject fillObj = new GameObject("ProgressFill", typeof(RectTransform), typeof(Image));
        fillObj.transform.SetParent(progTrack.transform, false);
        _progressFillRect = fillObj.GetComponent<RectTransform>();
        _progressFillRect.anchorMin = new Vector2(0f, 0.5f);
        _progressFillRect.anchorMax = new Vector2(0f, 0.5f);
        _progressFillRect.pivot = new Vector2(0f, 0.5f);
        _progressFillRect.anchoredPosition = Vector2.zero;
        _progressFillRect.sizeDelta = new Vector2(0f, 12f);
        _progressFillImg = fillObj.GetComponent<Image>();
        _progressFillImg.sprite = _roundedRectSprite;
        _progressFillImg.type = Image.Type.Sliced;
        _progressFillImg.color = recCrimson;
        _progressFillImg.raycastTarget = false;

        // 6. Bottom Modern Circular Record Button
        float btnY = -318f;
        _recOuterGlow = CreateSpriteImage("RecOuterGlow", root, _circleSprite, new Color(recCrimson.r, recCrimson.g, recCrimson.b, 0f),
            new Vector2(0f, btnY), new Vector2(168f, 168f));
        CreateSpriteImage("RecOuterRing", root, _ringSprite, chalkWhite,
            new Vector2(0f, btnY), new Vector2(126f, 126f));
        _recInnerShape = CreateSpriteImage("RecInnerCircle", root, _circleSprite, recCrimson,
            new Vector2(0f, btnY), new Vector2(96f, 96f));
        _recInnerRect = _recInnerShape.rectTransform;

        // 7. Save Flash Overlay
        _screenFlashImg = CreateSpriteImage("ScreenFlashOverlay", root, _roundedRectSprite, new Color(1f, 1f, 1f, 0f),
            Vector2.zero, new Vector2(canvasW, canvasH));
    }

    private Image CreateRect(string name, RectTransform parent, Color color, Vector2 pos, Vector2 size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = obj.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Image CreateSpriteImage(string name, RectTransform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size)
    {
        Image img = CreateRect(name, parent, color, pos, size);
        img.sprite = sprite;
        return img;
    }

    private Text CreateText(string name, RectTransform parent, string content, int fontSize, FontStyle style,
        Color color, TextAnchor alignment, Vector2 pos, Vector2 size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Text txt = obj.GetComponent<Text>();
        txt.font = _uiFont;
        txt.text = content;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.color = color;
        txt.alignment = alignment;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.raycastTarget = false;
        return txt;
    }

    private Sprite CreateCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        float radius = half - 1.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        _generatedTextures.Add(tex);
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _generatedSprites.Add(sp);
        return sp;
    }

    private Sprite CreateRingSprite(int size, float innerRatio, float outerRatio)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        float innerR = half * innerRatio;
        float outerR = half * outerRatio;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half));
                float aOuter = Mathf.Clamp01(outerR - d + 0.75f);
                float aInner = Mathf.Clamp01(d - innerR + 0.75f);
                float a = aOuter * aInner;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        _generatedTextures.Add(tex);
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        _generatedSprites.Add(sp);
        return sp;
    }

    private Sprite CreateRoundedRectSprite(int w, int h, int radius)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[w * h];
        float r = Mathf.Min(radius, Mathf.Min(w, h) * 0.5f - 1f);
        float hx = (w - 1) * 0.5f;
        float hy = (h - 1) * 0.5f;
        float bx = hx - r - 1f;
        float by = hy - r - 1f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float qx = Mathf.Abs(x - hx) - bx;
                float qy = Mathf.Abs(y - hy) - by;
                float dist = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                           + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                float a = Mathf.Clamp01(0.5f - dist);
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        _generatedTextures.Add(tex);
        Vector4 border = new Vector4(radius + 2, radius + 2, radius + 2, radius + 2);
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        _generatedSprites.Add(sp);
        return sp;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _generatedSprites.Count; i++)
        {
            if (_generatedSprites[i] != null) Destroy(_generatedSprites[i]);
        }
        for (int i = 0; i < _generatedTextures.Count; i++)
        {
            if (_generatedTextures[i] != null) Destroy(_generatedTextures[i]);
        }
    }
}
