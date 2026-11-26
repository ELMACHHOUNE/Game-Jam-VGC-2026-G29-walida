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

    // Runtime HUD elements
    private GameObject _hudRoot;
    private Image _guidePanelBg;
    private Text _guideHeaderText;
    private Text[] _guideItemTexts;
    private Image[] _guideItemIcons;
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
    private Image[] _bookSlotLockIcons;

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
            // The card sprite carries its own colours; tinting it warm makes the whole card blush terracotta
            Color baseCol = Color.white;
            if (_guidePulseTimer > 0f)
            {
                _guidePulseTimer = Mathf.Max(0f, _guidePulseTimer - dt);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f);
                Color alertCol = new Color(1f, 0.80f, 0.70f, 1f);
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
        _subtitleText.text = MedinaUIKit.FormatSpeakerLine(message);
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
        _guideHeaderText.text = $"Walida's Photo Guide   {_collectedCount}/{total}";

        if (_guideItemTexts == null) return;
        for (int i = 0; i < _guideItemTexts.Length && i < photoTargets.Length; i++)
        {
            NeighborhoodPhotoTarget t = photoTargets[i];
            if (t == null || _guideItemTexts[i] == null) continue;

            bool done = t.IsCollected;
            _guideItemTexts[i].text = t.GuideLabel;
            _guideItemTexts[i].color = done ? MedinaUIKit.Olive : MedinaUIKit.Ink;
            _guideItemTexts[i].fontStyle = done ? FontStyle.Italic : FontStyle.Normal;

            if (_guideItemIcons != null && i < _guideItemIcons.Length && _guideItemIcons[i] != null)
            {
                _guideItemIcons[i].sprite = done ? MedinaUIKit.StarSprite : MedinaUIKit.StarOutlineSprite;
                _guideItemIcons[i].color = done ? MedinaUIKit.Olive : MedinaUIKit.WithAlpha(MedinaUIKit.Navy, 0.6f);
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

            bool unlocked = t.IsCollected && t.CuratedBookPhoto != null;
            if (unlocked)
            {
                _bookSlotImages[i].texture = t.CuratedBookPhoto;
                _bookSlotImages[i].color = Color.white;
                _bookSlotTitles[i].text = t.BookTitle;
                _bookSlotCaptions[i].text = t.MemoryCaption;
            }
            else
            {
                // An empty, sun-faded print waiting for its photo
                _bookSlotImages[i].texture = null;
                _bookSlotImages[i].color = MedinaUIKit.WithAlpha(MedinaUIKit.Sepia, 0.28f);
                _bookSlotTitles[i].text = "A memory to find";
                _bookSlotCaptions[i].text = $"Hint: {t.GuideLabel}";
            }

            if (_bookSlotLockIcons != null && i < _bookSlotLockIcons.Length && _bookSlotLockIcons[i] != null)
                _bookSlotLockIcons[i].enabled = !unlocked;
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

        _hudRoot = new GameObject("NeighborhoodPhotoHUD", typeof(RectTransform));
        _hudRoot.transform.SetParent(uiCanvas.transform, false);
        RectTransform rootRect = _hudRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        Transform root = _hudRoot.transform;

        int count = photoTargets != null ? photoTargets.Length : 0;

        // 1. Top-left guide card (storybook die-cut card with a zellige-star checklist)
        float guideHeight = 108f + count * 36f + 40f;
        _guidePanelBg = MedinaUIKit.CreateCard("GuideChecklistPanel", root, new Vector2(0f, 1f),
            new Vector2(28f, -28f), new Vector2(440f, guideHeight));
        Transform guide = _guidePanelBg.transform;

        MedinaUIKit.CreateTopLeftText("GuideLabel", guide, MedinaTextRole.Label,
            "MEMORY HUNT", 14, TextAnchor.MiddleLeft, new Vector2(30f, -18f), new Vector2(380f, 20f));

        _guideHeaderText = MedinaUIKit.CreateTopLeftText("GuideHeader", guide, MedinaTextRole.Title,
            $"Walida's Photo Guide   0/{count}", 21, TextAnchor.MiddleLeft, new Vector2(30f, -40f), new Vector2(390f, 32f));

        _guideItemTexts = new Text[count];
        _guideItemIcons = new Image[count];
        for (int i = 0; i < count; i++)
        {
            float y = -88f - i * 36f;
            _guideItemIcons[i] = MedinaUIKit.CreateImage($"GuideStar_{i}", guide, MedinaUIKit.StarOutlineSprite,
                MedinaUIKit.WithAlpha(MedinaUIKit.Navy, 0.6f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(30f, y - 4f), new Vector2(20f, 20f), sliced: false);

            _guideItemTexts[i] = MedinaUIKit.CreateTopLeftText($"GuideItem_{i}", guide, MedinaTextRole.Body,
                "", 17, TextAnchor.MiddleLeft, new Vector2(62f, y), new Vector2(350f, 28f));
        }

        MedinaUIKit.CreateTopLeftText("BookShortcutHint", guide, MedinaTextRole.Hint,
            "Press B or J to open the Memory Book", 15, TextAnchor.MiddleLeft,
            new Vector2(30f, -guideHeight + 46f), new Vector2(390f, 24f));

        // 2. Top-centre toast ("Photo added to the book.")
        Image toast = MedinaUIKit.CreateCard("BookToastPanel", root, new Vector2(0.5f, 1f),
            new Vector2(0f, -28f), new Vector2(560f, 70f));
        _toastObj = toast.gameObject;
        MedinaUIKit.CreateImage("ToastStar", toast.transform, MedinaUIKit.StarSprite, MedinaUIKit.Olive,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(26f, 26f), sliced: false);
        _toastText = MedinaUIKit.CreateFillText("ToastText", toast.transform, MedinaTextRole.BodyStrong,
            addedToBookToast, 20, TextAnchor.MiddleCenter, new Vector4(66f, 8f, 30f, 8f));
        _toastObj.SetActive(false);

        // 3. Framing hint ("Frame the shot. Hold still...")
        Image framing = MedinaUIKit.CreateCard("FramingHintPanel", root, new Vector2(0.5f, 0f),
            new Vector2(0f, 140f), new Vector2(660f, 60f));
        _framingObj = framing.gameObject;
        _framingText = MedinaUIKit.CreateFillText("FramingText", framing.transform, MedinaTextRole.Hint,
            framingHintText, 18, TextAnchor.MiddleCenter, new Vector4(28f, 6f, 28f, 6f));
        _framingObj.SetActive(false);

        // 4. Bottom subtitle card (speaker name highlighted, like the VN dialogue)
        Image subtitle = MedinaUIKit.CreateCard("AbbassSubtitlePanel", root, new Vector2(0.5f, 0f),
            new Vector2(0f, 34f), new Vector2(900f, 92f));
        _subtitleObj = subtitle.gameObject;
        _subtitleText = MedinaUIKit.CreateFillText("SubtitleText", subtitle.transform, MedinaTextRole.Body,
            "", 22, TextAnchor.MiddleCenter, new Vector4(36f, 14f, 36f, 14f));
        _subtitleObj.SetActive(false);

        // 5. Memory Book: dimmed scrim + journal page with aged photo prints (2x2)
        Image scrim = MedinaUIKit.CreateFillImage("MemoryBookOverlay", root, null, MedinaUIKit.Scrim, Vector4.zero);
        _bookOverlayObj = scrim.gameObject;

        Image page = MedinaUIKit.CreateImage("JournalPage", scrim.transform, MedinaUIKit.PaperSprite, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1120f, 800f));
        MedinaUIKit.AddSoftShadow(page, 10f, 0.4f);
        Transform pageT = page.transform;

        MedinaUIKit.CreateText("BookOverlayTitle", pageT, MedinaTextRole.Title, "L'Walida's Memory Book", 30,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), new Vector2(700f, 44f));
        MedinaUIKit.CreateText("BookOverlayHint", pageT, MedinaTextRole.Hint, "B / J / Esc to close", 16,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -70f), new Vector2(400f, 24f));
        for (int side = -1; side <= 1; side += 2)
        {
            MedinaUIKit.CreateImage(side < 0 ? "TitleStarLeft" : "TitleStarRight", pageT, MedinaUIKit.StarSprite,
                MedinaUIKit.Terracotta, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(side * 270f, -48f), new Vector2(22f, 22f), sliced: false);
        }

        int slotCount = Mathf.Min(4, count);
        _bookSlotImages = new RawImage[slotCount];
        _bookSlotTitles = new Text[slotCount];
        _bookSlotCaptions = new Text[slotCount];
        _bookSlotLockIcons = new Image[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            int col = i % 2;
            int row = i / 2;
            Vector2 slotPos = new Vector2(48f + col * 528f, -112f - row * 336f);

            RectTransform slot = MedinaUIKit.CreateRect($"BookSlot_{i}", pageT, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), slotPos, new Vector2(500f, 316f));

            // Aged print, slightly tilted like a photo pasted into a journal
            Image print = MedinaUIKit.CreateImage("Print", slot, MedinaUIKit.PrintSprite, Color.white,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(128f, -158f), new Vector2(236f, 296f));
            print.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (col == 0 ? -2.2f : 1.8f) + (row == 1 ? 0.8f : 0f));
            MedinaUIKit.AddSoftShadow(print, 5f, 0.3f);

            RectTransform photoRect = MedinaUIKit.CreateRect("PhotoImage", print.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            photoRect.offsetMin = new Vector2(12f, 34f); // wider bottom margin, like an old print
            photoRect.offsetMax = new Vector2(-12f, -12f);
            _bookSlotImages[i] = photoRect.gameObject.AddComponent<RawImage>();
            _bookSlotImages[i].raycastTarget = false;

            _bookSlotLockIcons[i] = MedinaUIKit.CreateImage("LockedStar", photoRect, MedinaUIKit.StarOutlineSprite,
                MedinaUIKit.WithAlpha(MedinaUIKit.Sepia, 0.75f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 54f), sliced: false);

            _bookSlotTitles[i] = MedinaUIKit.CreateTopLeftText("SlotTitle", slot, MedinaTextRole.Title,
                "A memory to find", 21, TextAnchor.UpperLeft, new Vector2(268f, -22f), new Vector2(222f, 56f));

            _bookSlotCaptions[i] = MedinaUIKit.CreateTopLeftText("SlotCaption", slot, MedinaTextRole.Caption,
                "", 16, TextAnchor.UpperLeft, new Vector2(268f, -86f), new Vector2(222f, 210f));
        }

        _bookOverlayObj.SetActive(false);
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
