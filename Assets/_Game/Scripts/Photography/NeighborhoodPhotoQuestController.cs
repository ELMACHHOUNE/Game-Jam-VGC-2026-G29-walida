using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Walida.Audio;

/// <summary>
/// Manages the 4-photo Neighborhood Photo Segment:
/// - Tracks the 4 curated L'Walida book photos (Walida's home, Khadija's home, Neighborhood stairs, Blue sky).
/// - Generously accepts shots taken nearby or slightly far from a target and shows the curated book photo.
/// - Plays shutter SFX (custom AudioClip or synthesized mechanical shutter click) before flash/book update.
/// - When shooting far from any remaining target, highlights the UI guide and shows:
///   "Abbass: That's not what walida wanted."
/// - Provides a clean in-scene Guide Checklist, Toast ("Photo added to the book."), Abbass subtitle bar,
///   framing prompt ("Frame the shot. Hold still."), and toggleable Memory Book viewer (B or J).
/// </summary>
public class NeighborhoodPhotoQuestController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PhonePhotoCapturer photoCapturer;
    [SerializeField] private Transform viewfinderTransform;
    [SerializeField] private PanoramaEdgeScroller edgeScroller;
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private NeighborhoodPhotoTarget[] photoTargets;

    [Header("FMOD SFX")]
    [SerializeField] private EventReference shutterEvent;
    [SerializeField] private EventReference cameraFocusBeepEvent;
    [SerializeField] private EventReference detailPickEvent;
    [SerializeField] private EventReference menuClickEvent;
    [SerializeField, Range(0f, 1f)] private float focusBeepVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float uiSfxVolume = 0.72f;

    [Header("Shutter SFX (Legacy Fallback)")]
    [SerializeField] private AudioClip shutterSfxClip;
    [SerializeField, Range(0f, 1f)] private float shutterVolume = 0.85f;
    [SerializeField] private bool synthesizeFallbackShutterSfx = true;

    [Header("Dialogue & UI Copy")]
    [SerializeField] private string wrongShotLine = "Abbass: That's not what walida wanted.";
    [SerializeField] private string addedToBookToast = "Photo added to the book.";
    [SerializeField] private string framingHintText = "Frame the shot. Hold still...  (Click or Space)";
    [SerializeField] private string segmentCompleteLine = "Abbass: All four memories are in Walida's book.";
    [SerializeField] private float subtitleDuration = 4.0f;
    [SerializeField] private float toastDuration = 2.8f;

    [Header("Events")]
    [SerializeField] private UnityEvent<NeighborhoodPhotoTarget, int> onTargetPhotoAdded;
    [SerializeField] private UnityEvent onWrongLocationPhotographed;
    [SerializeField] private UnityEvent onAllPhotosCollected;

    private FMODLoopHandle _focusBeepHandle;
    private NeighborhoodPhotoTarget _focusedTarget;
    private float _lastFocusBeepTime = -10f;
    private AudioSource _sfxSource;
    private AudioClip _generatedShutterClip;
    private Font _uiFont;

    // Runtime HUD elements
    private GameObject _hudRoot;
    private Image _guidePanelBg;
    private Text _guideHeaderText;
    private Text[] _guideItemTexts;
    private GameObject _toastObj;
    private Text _toastText;
    private GameObject _framingObj;
    private Text _framingText;
    private GameObject _subtitleObj;
    private Text _subtitleText;
    private GameObject _bookOverlayObj;
    private RawImage[] _bookSlotImages;
    private Text[] _bookSlotTitles;
    private Text[] _bookSlotCaptions;

    private float _subtitleTimer;
    private float _toastTimer;
    private float _guidePulseTimer;
    private bool _isBookOpen;
    private bool _segmentCompleted;
    private int _collectedCount;

    public int CollectedCount => _collectedCount;
    public int TotalRequired => photoTargets != null ? photoTargets.Length : 0;
    public bool IsSegmentCompleted => _segmentCompleted;

    private void Awake()
    {
        if (photoCapturer == null)
        {
            photoCapturer = FindAnyObjectByType<PhonePhotoCapturer>();
        }

        if (edgeScroller == null)
        {
            edgeScroller = FindAnyObjectByType<PanoramaEdgeScroller>();
        }

        if (uiCanvas == null)
        {
            uiCanvas = FindAnyObjectByType<Canvas>();
        }

        if (photoTargets == null || photoTargets.Length == 0)
        {
            photoTargets = FindObjectsByType<NeighborhoodPhotoTarget>(FindObjectsInactive.Exclude);
        }

        SetupAudio();
        BuildHud();
        RefreshGuideUi();
        RefreshBookUi();
    }

    private void OnEnable()
    {
        if (photoCapturer != null)
        {
            photoCapturer.OnPhotoCaptured += HandlePhotoCaptured;
        }
    }

    private void OnDisable()
    {
        if (photoCapturer != null)
        {
            photoCapturer.OnPhotoCaptured -= HandlePhotoCaptured;
        }

        _focusBeepHandle?.Stop(0.1f);
        _focusedTarget = null;
    }

    private void Update()
    {
        if (Keyboard.current != null &&
            (Keyboard.current.bKey.wasPressedThisFrame || Keyboard.current.jKey.wasPressedThisFrame))
        {
            ToggleBookOverlay(!_isBookOpen);
        }

        if (_isBookOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ToggleBookOverlay(false);
        }

        UpdateFramingHint();
        UpdateTimers();
    }

    private void HandlePhotoCaptured(Texture2D rawCapturedTexture)
    {
        _focusBeepHandle?.Stop(0.05f);
        PlayShutterSfx();

        if (viewfinderTransform == null || photoTargets == null || photoTargets.Length == 0)
        {
            return;
        }

        float wrapWidth = edgeScroller != null && edgeScroller.LoopHorizontally ? edgeScroller.PanoramaWidth : 0f;
        Vector3 vfPos = viewfinderTransform.position;

        // First look for the closest uncollected target within generous capture range
        NeighborhoodPhotoTarget matchedTarget = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < photoTargets.Length; i++)
        {
            NeighborhoodPhotoTarget candidate = photoTargets[i];
            if (candidate == null || candidate.IsCollected)
            {
                continue;
            }

            if (candidate.IsWithinCaptureArea(vfPos, wrapWidth, out float dist, out _) && dist < bestDist)
            {
                bestDist = dist;
                matchedTarget = candidate;
            }
        }

        if (matchedTarget != null)
        {
            matchedTarget.MarkCollected();
            _collectedCount = CountCollected();
            FMODAudioManager.PlayOneShot(detailPickEvent, FMODEvents.DetailPick, uiSfxVolume);

            // Display the curated team book photo in the preview card!
            if (photoCapturer != null && matchedTarget.CuratedBookPhoto != null)
            {
                photoCapturer.SetPreviewTexture(matchedTarget.CuratedBookPhoto, true);
            }

            ShowToast($"{addedToBookToast}  ({_collectedCount}/{TotalRequired})");
            ShowSubtitle(!string.IsNullOrWhiteSpace(matchedTarget.MemoryCaption)
                ? $"{matchedTarget.BookTitle} — {matchedTarget.MemoryCaption}"
                : matchedTarget.BookTitle, subtitleDuration);

            RefreshGuideUi();
            RefreshBookUi();
            onTargetPhotoAdded?.Invoke(matchedTarget, _collectedCount);

            if (!_segmentCompleted && _collectedCount >= TotalRequired && TotalRequired > 0)
            {
                _segmentCompleted = true;
                ShowToast($"Neighborhood Complete ({_collectedCount}/{TotalRequired}) — Press B to view L'Walida's Book", 5.5f);
                ShowSubtitle(segmentCompleteLine, 6.0f);
                onAllPhotosCollected?.Invoke();
            }

            return;
        }

        // Check if the player re-photographed an already collected spot
        NeighborhoodPhotoTarget alreadyCollected = null;
        float bestCollectedDist = float.MaxValue;
        for (int i = 0; i < photoTargets.Length; i++)
        {
            NeighborhoodPhotoTarget candidate = photoTargets[i];
            if (candidate == null || !candidate.IsCollected)
            {
                continue;
            }

            if (candidate.IsWithinCaptureArea(vfPos, wrapWidth, out float dist, out _) && dist < bestCollectedDist)
            {
                bestCollectedDist = dist;
                alreadyCollected = candidate;
            }
        }

        if (alreadyCollected != null)
        {
            if (photoCapturer != null && alreadyCollected.CuratedBookPhoto != null)
            {
                photoCapturer.SetPreviewTexture(alreadyCollected.CuratedBookPhoto, true);
            }

            _guidePulseTimer = 2.4f;
            ShowSubtitle($"Abbass: I already have {alreadyCollected.BookTitle} in the book.", 3.2f);
            return;
        }

        // Far away from all remaining guide locations: pop/highlight guide + show Abbass line
        _guidePulseTimer = 3.5f;
        ShowSubtitle(wrongShotLine, subtitleDuration);
        onWrongLocationPhotographed?.Invoke();
    }

    private void UpdateFramingHint()
    {
        if (_framingObj == null || viewfinderTransform == null || photoTargets == null)
        {
            return;
        }

        if (_isBookOpen || _segmentCompleted)
        {
            _framingObj.SetActive(false);
            return;
        }

        float wrapWidth = edgeScroller != null && edgeScroller.LoopHorizontally ? edgeScroller.PanoramaWidth : 0f;
        Vector3 vfPos = viewfinderTransform.position;

        NeighborhoodPhotoTarget hoveredTarget = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < photoTargets.Length; i++)
        {
            NeighborhoodPhotoTarget candidate = photoTargets[i];
            if (candidate == null || candidate.IsCollected)
            {
                continue;
            }

            if (candidate.IsWithinCaptureArea(vfPos, wrapWidth, out float dist, out _) && dist < bestDist)
            {
                bestDist = dist;
                hoveredTarget = candidate;
            }
        }

        if (hoveredTarget != null)
        {
            _framingObj.SetActive(true);
            _framingText.text = $"{hoveredTarget.GuideLabel}  —  {framingHintText}";

            if (hoveredTarget != _focusedTarget && Time.unscaledTime - _lastFocusBeepTime >= 0.7f)
            {
                _lastFocusBeepTime = Time.unscaledTime;
                if (_focusBeepHandle == null && FMODAudioManager.Instance != null)
                    _focusBeepHandle = FMODAudioManager.Instance.CreateManagedLoop();

                _focusBeepHandle?.ReleaseImmediate();
                _focusBeepHandle?.Start(cameraFocusBeepEvent, FMODEvents.CameraFocusBeep, focusBeepVolume, fadeInSeconds: 0.02f, autoRestartLoop: false);
            }
            _focusedTarget = hoveredTarget;
        }
        else
        {
            _framingObj.SetActive(false);
            if (_focusedTarget != null)
            {
                _focusedTarget = null;
                _focusBeepHandle?.Stop(0.12f);
            }
        }
    }

    private void UpdateTimers()
    {
        float dt = Time.unscaledDeltaTime;

        if (_subtitleTimer > 0f)
        {
            _subtitleTimer -= dt;
            if (_subtitleTimer <= 0f && _subtitleObj != null)
            {
                _subtitleObj.SetActive(false);
            }
        }

        if (_toastTimer > 0f)
        {
            _toastTimer -= dt;
            if (_toastTimer <= 0f && _toastObj != null)
            {
                _toastObj.SetActive(false);
            }
        }

        if (_guidePanelBg != null)
        {
            Color baseCol = new Color(0.145f, 0.157f, 0.153f, 0.84f);
            if (_guidePulseTimer > 0f)
            {
                _guidePulseTimer = Mathf.Max(0f, _guidePulseTimer - dt);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f);
                Color alertCol = new Color(0.659f, 0.486f, 0.322f, 0.94f);
                _guidePanelBg.color = Color.Lerp(baseCol, alertCol, pulse * Mathf.Clamp01(_guidePulseTimer));
            }
            else
            {
                _guidePanelBg.color = baseCol;
            }
        }
    }

    public void ToggleBookOverlay(bool open)
    {
        if (_isBookOpen != open)
        {
            FMODAudioManager.PlayOneShot(menuClickEvent, FMODEvents.MenuClick, uiSfxVolume);
        }

        _isBookOpen = open;
        if (open)
        {
            _focusBeepHandle?.Stop(0.08f);
            _focusedTarget = null;
        }
        if (_bookOverlayObj != null)
        {
            _bookOverlayObj.SetActive(open);
        }

        if (photoCapturer != null)
        {
            photoCapturer.IsCaptureEnabled = !open;
        }

        if (open)
        {
            RefreshBookUi();
        }
    }

    private int CountCollected()
    {
        int count = 0;
        if (photoTargets == null) return 0;
        for (int i = 0; i < photoTargets.Length; i++)
        {
            if (photoTargets[i] != null && photoTargets[i].IsCollected)
            {
                count++;
            }
        }
        return count;
    }

    private void ShowSubtitle(string message, float duration)
    {
        if (_subtitleObj == null || _subtitleText == null) return;
        _subtitleText.text = message;
        _subtitleObj.SetActive(true);
        _subtitleTimer = duration;
    }

    private void ShowToast(string message, float duration = -1f)
    {
        if (_toastObj == null || _toastText == null) return;
        _toastText.text = message;
        _toastObj.SetActive(true);
        _toastTimer = duration > 0f ? duration : toastDuration;
    }

    private void RefreshGuideUi()
    {
        if (_guideHeaderText == null || photoTargets == null) return;

        int total = photoTargets.Length;
        _guideHeaderText.text = $"WALIDA'S PHOTO GUIDE  ({_collectedCount}/{total})";

        if (_guideItemTexts == null) return;
        for (int i = 0; i < _guideItemTexts.Length && i < photoTargets.Length; i++)
        {
            NeighborhoodPhotoTarget t = photoTargets[i];
            if (t == null || _guideItemTexts[i] == null) continue;

            if (t.IsCollected)
            {
                _guideItemTexts[i].text = $"[✓]  {t.GuideLabel}";
                _guideItemTexts[i].color = new Color(0.68f, 0.85f, 0.62f, 1f);
            }
            else
            {
                _guideItemTexts[i].text = $"[  ]  {t.GuideLabel}";
                _guideItemTexts[i].color = new Color(0.949f, 0.929f, 0.890f, 1f);
            }
        }
    }

    private void RefreshBookUi()
    {
        if (_bookSlotImages == null || photoTargets == null) return;

        for (int i = 0; i < _bookSlotImages.Length && i < photoTargets.Length; i++)
        {
            NeighborhoodPhotoTarget t = photoTargets[i];
            if (t == null) continue;

            if (t.IsCollected && t.CuratedBookPhoto != null)
            {
                _bookSlotImages[i].texture = t.CuratedBookPhoto;
                _bookSlotImages[i].color = Color.white;
                _bookSlotTitles[i].text = t.BookTitle;
                _bookSlotCaptions[i].text = t.MemoryCaption;
            }
            else
            {
                _bookSlotImages[i].texture = null;
                _bookSlotImages[i].color = new Color(0.18f, 0.17f, 0.16f, 0.85f);
                _bookSlotTitles[i].text = "Locked Memory";
                _bookSlotCaptions[i].text = $"Hint: {t.GuideLabel}";
            }
        }
    }

    private void SetupAudio()
    {
        _sfxSource = GetComponent<AudioSource>();
        if (_sfxSource == null)
        {
            _sfxSource = gameObject.AddComponent<AudioSource>();
        }

        _sfxSource.playOnAwake = false;
        _sfxSource.loop = false;
        _sfxSource.spatialBlend = 0f;

        if (shutterSfxClip == null && synthesizeFallbackShutterSfx)
        {
            _generatedShutterClip = CreateMechanicalShutterClip();
        }
    }

    private void PlayShutterSfx()
    {
        if (FMODAudioManager.PlayOneShot(shutterEvent, FMODEvents.Shutter, shutterVolume))
            return;

        if (_sfxSource == null) return;
        AudioClip clip = shutterSfxClip != null ? shutterSfxClip : _generatedShutterClip;
        if (clip != null)
        {
            _sfxSource.PlayOneShot(clip, shutterVolume);
        }
    }

    private static AudioClip CreateMechanicalShutterClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.14f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random rand = new System.Random(1960);

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float click1 = Mathf.Exp(-t * 160f) * (float)(rand.NextDouble() * 2.0 - 1.0);
            float click2T = Mathf.Max(0f, t - 0.045f);
            float click2 = t >= 0.045f ? Mathf.Exp(-click2T * 130f) * (float)(rand.NextDouble() * 2.0 - 1.0) * 0.72f : 0f;
            float metallic = Mathf.Sin(t * 2f * Mathf.PI * 1850f) * Mathf.Exp(-t * 95f) * 0.25f;
            samples[i] = Mathf.Clamp((click1 + click2 + metallic) * 0.85f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("ProceduralShutterSFX", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void BuildHud()
    {
        if (uiCanvas == null) return;

        _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_uiFont == null)
        {
            _uiFont = Font.CreateDynamicFontFromOSFont("Arial", 20);
        }

        _hudRoot = new GameObject("NeighborhoodPhotoHUD", typeof(RectTransform));
        _hudRoot.transform.SetParent(uiCanvas.transform, false);
        RectTransform rootRect = _hudRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        // 1. Top-Left Guide Checklist Panel
        GameObject guidePanel = CreatePanel("GuideChecklistPanel", _hudRoot.transform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(28f, -28f), new Vector2(400f, 224f),
            new Color(0.145f, 0.157f, 0.153f, 0.84f));
        _guidePanelBg = guidePanel.GetComponent<Image>();

        _guideHeaderText = CreateText("GuideHeader", guidePanel.transform,
            new Vector2(18f, -14f), new Vector2(364f, 30f),
            "WALIDA'S PHOTO GUIDE  (0/4)", 19, FontStyle.Bold,
            new Color(0.784f, 0.643f, 0.467f, 1f), TextAnchor.MiddleLeft);

        int count = photoTargets != null ? photoTargets.Length : 0;
        _guideItemTexts = new Text[count];
        for (int i = 0; i < count; i++)
        {
            _guideItemTexts[i] = CreateText($"GuideItem_{i}", guidePanel.transform,
                new Vector2(20f, -52f - i * 32f), new Vector2(360f, 28f),
                "", 16, FontStyle.Normal,
                new Color(0.949f, 0.929f, 0.890f, 1f), TextAnchor.MiddleLeft);
        }

        CreateText("BookShortcutHint", guidePanel.transform,
            new Vector2(18f, -184f), new Vector2(364f, 26f),
            "Press [B] or [J] to open Memory Book", 14, FontStyle.Italic,
            new Color(0.784f, 0.643f, 0.467f, 0.9f), TextAnchor.MiddleLeft);

        // 2. Top-Center Toast ("Photo added to the book.")
        _toastObj = CreatePanel("BookToastPanel", _hudRoot.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -28f), new Vector2(540f, 52f),
            new Color(0.400f, 0.463f, 0.353f, 0.94f));
        _toastText = CreateCenteredText("ToastText", _toastObj.transform, addedToBookToast, 20, FontStyle.Bold,
            new Color(0.949f, 0.929f, 0.890f, 1f));
        _toastObj.SetActive(false);

        // 3. Framing Hint ("Frame the shot. Hold still...")
        _framingObj = CreatePanel("FramingHintPanel", _hudRoot.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 118f), new Vector2(620f, 42f),
            new Color(0.145f, 0.157f, 0.153f, 0.78f));
        _framingText = CreateCenteredText("FramingText", _framingObj.transform, framingHintText, 16, FontStyle.Bold,
            new Color(0.949f, 0.929f, 0.890f, 1f));
        _framingObj.SetActive(false);

        // 4. Bottom Subtitle Bar ("Abbass: That's not what walida wanted.")
        _subtitleObj = CreatePanel("AbbassSubtitlePanel", _hudRoot.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 34f), new Vector2(860f, 68f),
            new Color(0.10f, 0.11f, 0.11f, 0.90f));
        _subtitleText = CreateCenteredText("SubtitleText", _subtitleObj.transform, "", 22, FontStyle.Normal,
            new Color(0.949f, 0.929f, 0.890f, 1f));
        _subtitleObj.SetActive(false);

        // 5. Toggleable Memory Book Overlay (2x2 spread)
        _bookOverlayObj = CreatePanel("MemoryBookOverlay", _hudRoot.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(1060f, 760f),
            new Color(0.14f, 0.12f, 0.10f, 0.96f));

        CreateText("BookOverlayTitle", _bookOverlayObj.transform,
            new Vector2(36f, -20f), new Vector2(988f, 40f),
            "L'WALIDA'S MEMORY BOOK   (Press B / J / Esc to close)", 24, FontStyle.Bold,
            new Color(0.949f, 0.929f, 0.890f, 1f), TextAnchor.MiddleCenter);

        int slotCount = Mathf.Min(4, count);
        _bookSlotImages = new RawImage[slotCount];
        _bookSlotTitles = new Text[slotCount];
        _bookSlotCaptions = new Text[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            int col = i % 2;
            int row = i / 2;
            float x = 44f + col * 496f;
            float y = -82f - row * 324f;

            GameObject card = CreatePanel($"BookSlot_{i}", _bookOverlayObj.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(476f, 304f),
                new Color(0.949f, 0.929f, 0.890f, 0.96f));

            GameObject imgObj = new GameObject("PhotoImage", typeof(RectTransform), typeof(RawImage));
            imgObj.transform.SetParent(card.transform, false);
            RectTransform imgRect = imgObj.GetComponent<RectTransform>();
            imgRect.anchorMin = new Vector2(0f, 1f);
            imgRect.anchorMax = new Vector2(0f, 1f);
            imgRect.pivot = new Vector2(0f, 1f);
            imgRect.anchoredPosition = new Vector2(14f, -14f);
            imgRect.sizeDelta = new Vector2(210f, 276f);
            _bookSlotImages[i] = imgObj.GetComponent<RawImage>();
            _bookSlotImages[i].raycastTarget = false;

            _bookSlotTitles[i] = CreateText("SlotTitle", card.transform,
                new Vector2(238f, -24f), new Vector2(224f, 40f),
                "Locked Memory", 20, FontStyle.Bold,
                new Color(0.145f, 0.157f, 0.153f, 1f), TextAnchor.UpperLeft);

            _bookSlotCaptions[i] = CreateText("SlotCaption", card.transform,
                new Vector2(238f, -72f), new Vector2(224f, 210f),
                "", 16, FontStyle.Normal,
                new Color(0.32f, 0.27f, 0.22f, 1f), TextAnchor.UpperLeft);
        }

        _bookOverlayObj.SetActive(false);
    }

    private GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color bgColor)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        Image img = obj.GetComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = false;
        return obj;
    }

    private Text CreateText(string name, Transform parent, Vector2 anchoredPos, Vector2 size,
        string content, int fontSize, FontStyle style, Color color, TextAnchor alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Text text = obj.GetComponent<Text>();
        text.font = _uiFont;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private Text CreateCenteredText(string name, Transform parent, string content, int fontSize, FontStyle style, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(18f, 6f);
        rect.offsetMax = new Vector2(-18f, -6f);

        Text text = obj.GetComponent<Text>();
        text.font = _uiFont;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private void OnDestroy()
    {
        if (FMODAudioManager.Instance != null)
            FMODAudioManager.Instance.UnregisterLoop(_focusBeepHandle);
        else
            _focusBeepHandle?.ReleaseImmediate();

        if (_generatedShutterClip != null)
        {
            Destroy(_generatedShutterClip);
        }
    }
}
