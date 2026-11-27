using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Brings the player back from a minigame (photography / audio recording) to the 2.5D medina.
/// - Automatically after the minigame is completed (after a short delay, never while the Memory Book is open).
/// - Early, by pressing Escape (an Escape that closes the Memory Book does not count).
/// The minigame scene is isolated: it only records results in MinigameSession and swaps back. The medina then
/// rebuilds itself from MinigameSession's in-memory snapshot (no save file involved), so the player
/// reappears where they left with story state, denial score and intro flag intact.
/// Added automatically to any scene containing a minigame controller; add it by hand to tweak settings.
/// </summary>
public class MinigameReturnToMedina : MonoBehaviour
{
    [Header("Destination")]
    [SerializeField] private string medinaSceneName = "MedinaMainLevel";

    [Header("Return Triggers")]
    [Tooltip("Return automatically once the minigame is complete.")]
    [SerializeField] private bool returnOnComplete = true;
    [Tooltip("Seconds after completion before returning (lets the last line and toast play).")]
    [SerializeField, Min(0f)] private float returnDelayAfterComplete = 8f;
    [Tooltip("Escape leaves the minigame early.")]
    [SerializeField] private bool allowEscapeToLeave = true;

    [Header("Journal Unlock Popup")]
    [Tooltip("When a minigame unlocks a Journal entry, show the popup and then return to the medina.")]
    [SerializeField] private bool returnAfterJournalUnlock = true;
    [SerializeField] private string unlockTitle = "You've unlocked a Journal entry!";
    [SerializeField] private string unlockContinueHint = "Click or press Space to return to the Medina";
    [Tooltip("Pause after the photo before the popup appears (lets the shutter, toast and subtitle land).")]
    [SerializeField, Min(0f)] private float popupDelay = 1.4f;
    [Tooltip("Return automatically after this many seconds of showing the popup. 0 = wait for the player.")]
    [SerializeField, Min(0f)] private float popupAutoContinue = 0f;

    [Header("Transition")]
    [SerializeField, Min(0.05f)] private float fadeOutDuration = 0.6f;

    [Header("Hint")]
    [SerializeField] private bool showHint = true;
    [SerializeField] private string hintText = "Esc  —  Return to the Medina";
    [SerializeField] private string returningText = "Returning to the Medina...";

    private NeighborhoodPhotoQuestController _photoQuest;
    private SoukAudioSegmentController _soukSegment;

    private bool _completed;
    private float _returnAt = -1f;
    private bool _bookWasOpenLastFrame;
    private bool _isReturning;

    private Canvas _overlayCanvas;
    private Image _fadeImage;
    private Text _hintLabel;

    // Journal unlock popup
    private string _pendingUnlockEntry;
    private float _showPopupAt = -1f;
    private GameObject _popupRoot;
    private float _popupShownAt = -1f;

    // ---- Automatic setup ---------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureInActiveScene();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureInActiveScene();

    private static void EnsureInActiveScene()
    {
        if (FindAnyObjectByType<MinigameReturnToMedina>() != null)
            return;

        bool isMinigame = FindAnyObjectByType<NeighborhoodPhotoQuestController>() != null ||
                          FindAnyObjectByType<SoukAudioSegmentController>() != null;
        if (isMinigame)
            new GameObject(nameof(MinigameReturnToMedina)).AddComponent<MinigameReturnToMedina>();
    }

    // ---- Lifecycle ---------------------------------------------------------------------------

    private void Awake()
    {
        _photoQuest = FindAnyObjectByType<NeighborhoodPhotoQuestController>();
        _soukSegment = FindAnyObjectByType<SoukAudioSegmentController>();
        BuildOverlay();
    }

    private void OnEnable()
    {
        _photoQuest?.OnAllPhotosCollected?.AddListener(HandleCompleted);
        _soukSegment?.OnSoukMerchantsRecorded?.AddListener(HandleSoukRecorded);
        JournalProgress.OnEntryUnlocked += HandleJournalEntryUnlocked;
    }

    private void OnDisable()
    {
        _photoQuest?.OnAllPhotosCollected?.RemoveListener(HandleCompleted);
        _soukSegment?.OnSoukMerchantsRecorded?.RemoveListener(HandleSoukRecorded);
        JournalProgress.OnEntryUnlocked -= HandleJournalEntryUnlocked;
    }

    private void HandleJournalEntryUnlocked(string entryId)
    {
        MinigameSession.RecordUnlock(entryId);

        if (!returnAfterJournalUnlock || _isReturning || _popupRoot != null)
            return;

        _pendingUnlockEntry = entryId;
        _showPopupAt = Time.unscaledTime + popupDelay;
        _returnAt = -1f; // the popup decides when to leave

        // No more photos while the popup is pending/shown
        PhonePhotoCapturer capturer = FindAnyObjectByType<PhonePhotoCapturer>();
        if (capturer != null) capturer.IsCaptureEnabled = false;
    }

    private void HandleSoukRecorded(RecordableSoundSource _) => HandleCompleted();

    private void HandleCompleted()
    {
        if (_completed) return;
        _completed = true;
        MinigameSession.MarkCompleted();

        // A Journal popup already handles leaving
        if (returnOnComplete && _showPopupAt < 0f && _popupRoot == null)
            _returnAt = Time.unscaledTime + returnDelayAfterComplete;

        if (_hintLabel != null)
            _hintLabel.text = returnOnComplete ? returningText : hintText;
    }

    private void Update()
    {
        if (_isReturning) return;

        // ---- Journal unlock popup ----
        if (_showPopupAt > 0f && Time.unscaledTime >= _showPopupAt)
        {
            bool waitForBook = _photoQuest != null && _photoQuest.IsBookOpen;
            if (!waitForBook)
            {
                _showPopupAt = -1f;
                ShowJournalPopup(_pendingUnlockEntry);
            }
        }

        if (_popupRoot != null)
        {
            bool canContinue = Time.unscaledTime - _popupShownAt > 0.4f; // ignore the click that took the photo
            bool autoContinue = popupAutoContinue > 0f && Time.unscaledTime - _popupShownAt >= popupAutoContinue;
            if ((canContinue && ContinuePressed()) || autoContinue)
                ReturnToMedina();
            return;
        }

        if (_showPopupAt > 0f)
            return; // popup pending: don't start any other exit

        // Fallback in case completion happened before this component subscribed
        if (!_completed && ((_photoQuest != null && _photoQuest.IsComplete) || (_soukSegment != null && _soukSegment.IsComplete)))
            HandleCompleted();

        bool bookOpen = _photoQuest != null && _photoQuest.IsBookOpen;

        // An Escape that just closed the Memory Book (book was open last frame) must not also leave
        bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if (allowEscapeToLeave && escapePressed && !bookOpen && !_bookWasOpenLastFrame)
        {
            ReturnToMedina();
            return;
        }

        // Auto-return after completion, but wait while the player is looking at the Memory Book
        if (_returnAt > 0f && Time.unscaledTime >= _returnAt && !bookOpen)
            ReturnToMedina();
    }

    private void LateUpdate()
    {
        _bookWasOpenLastFrame = _photoQuest != null && _photoQuest.IsBookOpen;
    }

    // ---- Return ------------------------------------------------------------------------------

    public void ReturnToMedina()
    {
        if (_isReturning) return;
        _isReturning = true;
        _ = ReturnAsync(destroyCancellationToken);
    }

    private async Awaitable ReturnAsync(CancellationToken ct)
    {
        Time.timeScale = 1f;

        if (_hintLabel != null)
            _hintLabel.text = returningText;

        if (_fadeImage != null)
        {
            _fadeImage.gameObject.SetActive(true);
            float t = 0f;
            while (t < fadeOutDuration)
            {
                await Awaitable.NextFrameAsync(ct);
                t += Time.unscaledDeltaTime;
                _fadeImage.color = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, t / fadeOutDuration));
            }
            _fadeImage.color = Color.black;
        }

        // Swap back: the medina rebuilds itself from MinigameSession's in-memory snapshot + this visit's results.
        bool hasSnapshot = MinigameSession.MedinaSnapshot != null;
        MinigameSession.EndMinigame();

        VNSessionBridge.CachedInkStateJson = string.Empty;
        VNSessionBridge.LaunchMode = hasSnapshot
            ? VNLaunchMode.ReturnFromMinigame
            : VNLaunchMode.NewGame; // minigame opened directly (e.g. from the editor): nothing to restore

        if (!Application.CanStreamedLevelBeLoaded(medinaSceneName))
        {
            Debug.LogWarning($"[MinigameReturnToMedina] Scene '{medinaSceneName}' is not in the build's scene list.");
            _isReturning = false;
            return;
        }

        await SceneManager.LoadSceneAsync(medinaSceneName);
    }

    // ---- Journal unlock popup ----------------------------------------------------------------

    private static bool ContinuePressed()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        return (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
               (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                               kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame));
    }

    private void ShowJournalPopup(string entryId)
    {
        // Look up what was unlocked (photo + text) from the minigame's targets
        Texture photo = null;
        string title = string.Empty;
        string caption = string.Empty;
        foreach (NeighborhoodPhotoTarget t in FindObjectsByType<NeighborhoodPhotoTarget>(FindObjectsSortMode.None))
        {
            if (!string.Equals(t.JournalEntryId, entryId, System.StringComparison.OrdinalIgnoreCase)) continue;
            photo = t.CuratedBookPhoto;
            title = t.BookTitle;
            caption = t.MemoryCaption;
            break;
        }

        if (_hintLabel != null)
            _hintLabel.transform.parent.gameObject.SetActive(false);

        Transform canvas = _overlayCanvas.transform;
        Image scrim = MedinaUIKit.CreateFillImage("JournalUnlockPopup", canvas, null, MedinaUIKit.Scrim, Vector4.zero);
        _popupRoot = scrim.gameObject;
        _fadeImage.transform.SetAsLastSibling(); // keep the fade on top of the popup

        bool hasPhoto = photo != null;
        float height = hasPhoto ? 620f : 300f;
        Image card = MedinaUIKit.CreateCard("Card", scrim.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, height));
        Transform c = card.transform;

        MedinaUIKit.CreateText("Label", c, MedinaTextRole.Label, "MEMORY JOURNAL", 15, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(560f, 22f));
        MedinaUIKit.CreateText("Title", c, MedinaTextRole.Title, unlockTitle, 26, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(560f, 40f));
        for (int side = -1; side <= 1; side += 2)
        {
            MedinaUIKit.CreateImage(side < 0 ? "StarLeft" : "StarRight", c, MedinaUIKit.StarSprite, MedinaUIKit.Terracotta,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(side * 150f, -37f), new Vector2(16f, 16f), sliced: false);
        }

        float y = -110f;
        if (hasPhoto)
        {
            // Aged print, slightly tilted, like the photo pasted into the journal
            Image print = MedinaUIKit.CreateImage("Print", c, MedinaUIKit.PrintSprite, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(300f, 330f));
            print.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1.8f);
            MedinaUIKit.AddSoftShadow(print, 5f, 0.3f);

            RectTransform photoRect = MedinaUIKit.CreateRect("Photo", print.transform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            photoRect.offsetMin = new Vector2(14f, 40f);
            photoRect.offsetMax = new Vector2(-14f, -14f);
            var raw = photoRect.gameObject.AddComponent<RawImage>();
            raw.texture = photo;
            raw.raycastTarget = false;
            var fitter = photoRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = photo.height > 0 ? (float)photo.width / photo.height : 1f;

            y -= 350f;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            MedinaUIKit.CreateText("EntryTitle", c, MedinaTextRole.BodyStrong, title, 21, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(540f, 30f));
            y -= 32f;
        }

        if (!string.IsNullOrWhiteSpace(caption))
        {
            MedinaUIKit.CreateText("EntryCaption", c, MedinaTextRole.Caption, caption, 17, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(540f, 50f));
        }

        MedinaUIKit.CreateText("ContinueHint", c, MedinaTextRole.Hint, unlockContinueHint, 16, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(560f, 26f));

        _popupShownAt = Time.unscaledTime;
    }

    // ---- UI ----------------------------------------------------------------------------------

    private void BuildOverlay()
    {
        var canvasGo = new GameObject("MinigameReturnOverlay", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        _overlayCanvas = canvasGo.GetComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 500; // above the minigame HUDs
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (showHint)
        {
            Image card = MedinaUIKit.CreateCard("ReturnHint", canvasGo.transform, new Vector2(1f, 1f),
                new Vector2(-28f, -28f), new Vector2(380f, 58f));
            _hintLabel = MedinaUIKit.CreateFillText("ReturnHintText", card.transform, MedinaTextRole.Hint,
                hintText, 17, TextAnchor.MiddleCenter, new Vector4(20f, 6f, 20f, 6f));
        }

        _fadeImage = MedinaUIKit.CreateFillImage("ReturnFade", canvasGo.transform, null, new Color(0f, 0f, 0f, 0f), Vector4.zero);
        _fadeImage.gameObject.SetActive(false);
    }
}
