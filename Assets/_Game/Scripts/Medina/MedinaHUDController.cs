using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using LitMotion;
using FMOD.Studio;
using FMODUnity;

[Serializable]
public class JournalPhotoSlotData
{
    [Tooltip("Journal entry that unlocks this slot (see JournalProgress). Must match the minigame's id, e.g. a " +
             "NeighborhoodPhotoTarget's Journal Entry Id / Target Id. Leave empty for a slot that is always shown.")]
    public string entryId = "";
    public string caption = "Unexplored";
    [Tooltip("Caption shown while the entry is still locked.")]
    public string lockedCaption = "Unexplored";
    [Tooltip("Photo as a Sprite (used if set).")]
    public Sprite photo;
    [Tooltip("Photo as a plain texture, e.g. the same curated photo the minigame uses (used when no Sprite is set).")]
    public Texture2D photoTexture;

    public bool IsUnlocked => string.IsNullOrWhiteSpace(entryId) || JournalProgress.IsUnlocked(entryId);
    public bool HasPhoto => photo != null || photoTexture != null;
}

public class MedinaHUDController : MonoBehaviour
{
    [Header("UI Panel")]
    [SerializeField] private PanelRenderer panelRenderer;

    [Header("Scene Dependencies")]
    [SerializeField] private MedinaLevelManager levelManager;
    [SerializeField] private VNStoryController storyController;
    [SerializeField] private VNStageView vnStageView;
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Custom Sprites")]
    [SerializeField] private Sprite journalIconSprite;
    [SerializeField] private Sprite pauseIconSprite;
    [SerializeField] private Sprite journalBookSprite;

    [Header("Journal Photographs (0 & 1 = Left Page, 2 & 3 = Right Page)")]
    [SerializeField]
    private JournalPhotoSlotData[] photoSlots = new JournalPhotoSlotData[4]
    {
        new JournalPhotoSlotData { caption = "Bab Oudaya Gate" },
        new JournalPhotoSlotData { caption = "The Blue Alley" },
        new JournalPhotoSlotData { caption = "Lwalida's House" },
        new JournalPhotoSlotData { caption = "Oudaya Overlook" }
    };

    [Header("Journal Bottom-to-Up Transition")]
    [SerializeField, Min(0.08f)] private float bookSlideDuration = 0.42f;
    [SerializeField, Min(200f)] private float bookSlideOffsetPx = 900f;

    [Header("HUD Sizing & Scale (Live Inspector Preview)")]
    [SerializeField, Range(0.6f, 2.5f)] private float hudScale = 1.25f;
    [SerializeField, Min(32f)] private float actionButtonSize = 62f;
    [SerializeField, Min(12f)] private float journalGlyphSize = 32f;
    [SerializeField, Min(12f)] private float pauseGlyphSize = 24f;
    [SerializeField, Min(8f)] private float keyBadgeFontSize = 11f;
    [SerializeField, Min(10f)] private float locationTitleFontSize = 16f;
    [SerializeField, Min(9f)] private float locationSubtitleFontSize = 12f;
    [SerializeField, Min(10f)] private float interactKeyFontSize = 14f;
    [SerializeField, Min(10f)] private float interactTextFontSize = 16f;

    [Header("Modal Dimensions")]
    [SerializeField, Range(0.5f, 2.5f)] private float journalScale = 1.15f;
    [SerializeField] private Vector2 journalBookSize = new Vector2(1140f, 760f);
    [SerializeField, Min(260f)] private float pauseModalWidth = 440f;
    [SerializeField, Min(320f)] private float saveLoadModalWidth = 640f;
    [SerializeField, Min(320f)] private float settingsModalWidth = 620f;

    [Header("Audio")]
    [SerializeField] private bool pauseFmodOnPauseMenu = true;

    private VisualElement _boundRoot;
    private int _lastBoundVersion = -1;

    private VisualElement _locationPlate;
    private VisualElement _locationAccentBar;
    private Label _locationTitle;
    private Label _locationSubtitle;

    private VisualElement _hudTopBar;
    private Button _btnJournal;
    private Button _btnPause;
    private VisualElement _journalGlyph;
    private VisualElement _pauseGlyph;
    private List<Label> _keyBadges = new();

    private VisualElement _interactPrompt;
    private Label _interactKeyLabel;
    private Label _interactTextLabel;

    private VisualElement _modalOverlay;
    private VisualElement _journalPanel;
    private List<VisualElement> _bookPages = new();
    private List<Label> _bookHeaders = new();
    private readonly VisualElement[] _photoContainers = new VisualElement[4];
    private readonly VisualElement[] _photoImages = new VisualElement[4];
    private readonly Label[] _photoCaptions = new Label[4];

    private VisualElement _pausePanel;
    private VisualElement _saveLoadPanel;
    private Label _saveLoadTitle;
    private ScrollView _saveLoadList;
    private VisualElement _settingsPanel;
    private VisualElement _settingsContent;
    private VNSettingsView _settingsView;

    private MotionHandle _bookSlideTween;
    private MotionHandle _overlayFadeTween;
    private float _currentBookOffsetY;
    private bool _isJournalOpen;
    private bool _isJournalAnimating;
    private bool _isPaused;

    public bool IsJournalOpen => _isJournalOpen;
    public bool IsPaused => _isPaused;

    private bool IsVnDialogueActive => vnStageView != null && vnStageView.IsDialogueVisible;

    private void OnValidate()
    {
        if (photoSlots == null || photoSlots.Length != 4)
            Array.Resize(ref photoSlots, 4);

        ApplyHudDimensions();
        RefreshJournalPhotos();
    }

    private void OnEnable()
    {
        if (panelRenderer == null)
        {
            Debug.LogError("[MedinaHUDController] PanelRenderer reference is missing.");
            return;
        }

        panelRenderer.RegisterUIReloadCallback(HandleUIReload);

        if (storyController != null)
            storyController.OnDialogueEnded += HandleVnDialogueEnded;
    }

    private void OnDisable()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(HandleUIReload);

        if (storyController != null)
            storyController.OnDialogueEnded -= HandleVnDialogueEnded;

        CancelHandle(_bookSlideTween);
        CancelHandle(_overlayFadeTween);
        _settingsView?.Dispose();
        _settingsView = null;
        Time.timeScale = 1f;
    }

    private void Update()
    {
        if (_boundRoot == null || Keyboard.current == null)
            return;

        if (IsVnDialogueActive)
        {
            SetHudElementsVisible(false);
            return;
        }

        SetHudElementsVisible(true);

        if (Keyboard.current.jKey.wasPressedThisFrame && !_isPaused)
        {
            ToggleJournal();
            return;
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (_isJournalOpen)
                SetJournalOpen(false);
            else
                TogglePause();
        }
    }

    private void HandleVnDialogueEnded()
    {
        if (!IsVnDialogueActive)
            SetHudElementsVisible(true);
    }

    private void SetHudElementsVisible(bool visible)
    {
        DisplayStyle target = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (_hudTopBar != null && _hudTopBar.resolvedStyle.display != target)
            _hudTopBar.style.display = target;

        if (_locationPlate != null && _locationPlate.resolvedStyle.display != target)
            _locationPlate.style.display = target;

        if (!visible && _interactPrompt != null)
            _interactPrompt.style.display = DisplayStyle.None;
    }

    private void HandleUIReload(PanelRenderer pr, VisualElement root, int version)
    {
        if (root == null) return;

        if (_boundRoot != root || _lastBoundVersion != version)
        {
            BindElements(root);
            _boundRoot = root;
            _lastBoundVersion = version;
        }
    }

    private void BindElements(VisualElement root)
    {
        _locationPlate = root.Q<VisualElement>("hud-location-plate");
        _locationAccentBar = root.Q<VisualElement>(className: "location-accent-bar");
        _locationTitle = root.Q<Label>("hud-location-title");
        _locationSubtitle = root.Q<Label>("hud-location-subtitle");

        _hudTopBar = root.Q<VisualElement>("hud-top-bar");
        _btnJournal = root.Q<Button>("btn-world-journal");
        _btnPause = root.Q<Button>("btn-world-pause");
        _journalGlyph = root.Q<VisualElement>("icon-world-journal");
        _pauseGlyph = root.Q<VisualElement>("icon-world-pause");
        _keyBadges = root.Query<Label>(className: "hud-key-badge").ToList();

        _interactPrompt = root.Q<VisualElement>("hud-interact-prompt");
        _interactKeyLabel = root.Q<Label>("hud-interact-key");
        _interactTextLabel = root.Q<Label>("hud-interact-label");

        _modalOverlay = root.Q<VisualElement>("medina-modal-overlay");
        _journalPanel = root.Q<VisualElement>("journal-panel");
        _bookPages = root.Query<VisualElement>(className: "book-page").ToList();
        _bookHeaders = root.Query<Label>(className: "book-page__header").ToList();

        for (int i = 0; i < 4; i++)
        {
            _photoContainers[i] = root.Q<VisualElement>($"photo-slot-{i}");
            _photoImages[i] = root.Q<VisualElement>($"photo-img-{i}");
            _photoCaptions[i] = root.Q<Label>($"photo-cap-{i}");
        }

        _pausePanel = root.Q<VisualElement>("world-pause-menu");
        _saveLoadPanel = root.Q<VisualElement>("world-saveload-panel");
        _saveLoadTitle = root.Q<Label>("world-saveload-title");
        _saveLoadList = root.Q<ScrollView>("world-saveload-list");
        _settingsPanel = root.Q<VisualElement>("world-settings-panel");
        _settingsContent = root.Q<VisualElement>("world-settings-content");

        _settingsView?.Dispose();
        _settingsView = new VNSettingsView(_settingsContent ?? _settingsPanel, () => ShowPauseMenu(true));

        _btnJournal.focusable = false;
        _btnPause.focusable = false;

        if (_journalGlyph != null && journalIconSprite != null)
        {
            _journalGlyph.style.backgroundImage = new StyleBackground(journalIconSprite);
            _journalGlyph.style.unityBackgroundImageTintColor = Color.white;
        }

        if (_pauseGlyph != null && pauseIconSprite != null)
        {
            _pauseGlyph.style.backgroundImage = new StyleBackground(pauseIconSprite);
            _pauseGlyph.style.unityBackgroundImageTintColor = Color.white;
        }

        if (_journalPanel != null && journalBookSprite != null)
            _journalPanel.style.backgroundImage = new StyleBackground(journalBookSprite);

        _btnJournal.clicked += ToggleJournal;
        _btnPause.clicked += TogglePause;

        root.Q<Button>("btn-world-resume").clicked += () => SetPaused(false);
        root.Q<Button>("btn-world-save").clicked += OpenSaveModal;
        root.Q<Button>("btn-world-load").clicked += OpenLoadModal;
        root.Q<Button>("btn-world-settings").clicked += OpenSettingsModal;
        root.Q<Button>("btn-world-saveload-back").clicked += () => ShowPauseMenu(true);
        root.Q<Button>("btn-world-quit").clicked += QuitToMainMenu;

        _modalOverlay.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0 && evt.target == _modalOverlay && _isJournalOpen)
                SetJournalOpen(false);
        });

        ApplyHudDimensions();
        RefreshJournalPhotos();
    }

    public void ApplyHudDimensions()
    {
        float s = Mathf.Max(0.1f, hudScale);
        float js = Mathf.Max(0.1f, journalScale);

        float btnDim = actionButtonSize * s;
        if (_btnJournal != null)
        {
            _btnJournal.style.width = btnDim;
            _btnJournal.style.height = btnDim;
        }

        if (_btnPause != null)
        {
            _btnPause.style.width = btnDim;
            _btnPause.style.height = btnDim;
        }

        if (_journalGlyph != null)
        {
            float g = journalGlyphSize * s;
            _journalGlyph.style.width = g;
            _journalGlyph.style.height = g;
        }

        if (_pauseGlyph != null)
        {
            float g = pauseGlyphSize * s;
            _pauseGlyph.style.width = g;
            _pauseGlyph.style.height = g;
        }

        if (_keyBadges != null)
        {
            float badgeFont = keyBadgeFontSize * s;
            foreach (Label badge in _keyBadges)
            {
                if (badge != null)
                    badge.style.fontSize = badgeFont;
            }
        }

        if (_locationTitle != null)
            _locationTitle.style.fontSize = locationTitleFontSize * s;

        if (_locationSubtitle != null)
            _locationSubtitle.style.fontSize = locationSubtitleFontSize * s;

        if (_locationAccentBar != null)
            _locationAccentBar.style.height = (locationTitleFontSize + locationSubtitleFontSize + 4f) * s;

        if (_interactKeyLabel != null)
            _interactKeyLabel.style.fontSize = interactKeyFontSize * s;

        if (_interactTextLabel != null)
            _interactTextLabel.style.fontSize = interactTextFontSize * s;

        // Journal book + proportional photo mounts & page headers
        if (_journalPanel != null)
        {
            _journalPanel.style.width = journalBookSize.x * js;
            _journalPanel.style.height = journalBookSize.y * js;
            if (journalBookSprite != null)
                _journalPanel.style.backgroundImage = new StyleBackground(journalBookSprite);
        }

        if (_bookPages != null)
        {
            foreach (VisualElement page in _bookPages)
            {
                if (page == null) continue;
                page.style.paddingTop = 8f * js;
                page.style.paddingBottom = 8f * js;
                page.style.paddingLeft = 14f * js;
                page.style.paddingRight = 14f * js;
            }
        }

        if (_bookHeaders != null)
        {
            foreach (Label header in _bookHeaders)
            {
                if (header != null)
                    header.style.fontSize = 12f * js;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (_photoContainers[i] != null)
            {
                float sidePad = 8f * js;
                float bottomPad = 10f * js;
                _photoContainers[i].style.paddingTop = sidePad;
                _photoContainers[i].style.paddingLeft = sidePad;
                _photoContainers[i].style.paddingRight = sidePad;
                _photoContainers[i].style.paddingBottom = bottomPad;
            }

            if (_photoCaptions[i] != null)
            {
                _photoCaptions[i].style.fontSize = 12f * js;
                _photoCaptions[i].style.marginTop = 6f * js;
            }
        }

        if (_pausePanel != null)
            _pausePanel.style.width = pauseModalWidth;

        if (_saveLoadPanel != null)
            _saveLoadPanel.style.width = saveLoadModalWidth;

        if (_settingsPanel != null)
            _settingsPanel.style.width = settingsModalWidth;
    }

    private float GetEffectiveBookSlideOffset()
    {
        float scaledHeight = journalBookSize.y * Mathf.Max(0.1f, journalScale);
        return Mathf.Max(bookSlideOffsetPx, scaledHeight + 140f);
    }

    // =========================================================================
    // PUBLIC 2.5D HUD API (Locations, Prompts & 4-Photo Book Slots)
    // =========================================================================

    public void SetLocationHeader(string title, string subtitle)
    {
        if (_locationTitle != null) _locationTitle.text = title;
        if (_locationSubtitle != null) _locationSubtitle.text = subtitle;
    }

    public void SetInteractionPrompt(bool visible, string actionText = "Speak", string keyHint = "E")
    {
        if (_interactPrompt == null) return;
        _interactPrompt.style.display = visible && !IsVnDialogueActive ? DisplayStyle.Flex : DisplayStyle.None;
        if (_interactKeyLabel != null) _interactKeyLabel.text = keyHint;
        if (_interactTextLabel != null) _interactTextLabel.text = actionText;
    }

    /// <summary>
    /// Unlocks or updates one of the 4 photographs in the book (0 & 1 = Left Page, 2 & 3 = Right Page).
    /// </summary>
    public void SetJournalPhoto(int slotIndex, Sprite photoSprite, string caption)
    {
        if (slotIndex < 0 || slotIndex >= 4) return;

        if (photoSlots[slotIndex] == null)
            photoSlots[slotIndex] = new JournalPhotoSlotData();

        photoSlots[slotIndex].photo = photoSprite;
        photoSlots[slotIndex].caption = caption;

        RefreshJournalPhotos();
    }

    private void RefreshJournalPhotos()
    {
        for (int i = 0; i < 4; i++)
        {
            if (_photoContainers[i] == null || _photoImages[i] == null || _photoCaptions[i] == null)
                continue;

            JournalPhotoSlotData data = (photoSlots != null && i < photoSlots.Length) ? photoSlots[i] : null;
            bool unlocked = data != null && data.IsUnlocked;
            bool hasPhoto = unlocked && data.HasPhoto;

            _photoContainers[i].EnableInClassList("photo-mount--empty", !hasPhoto);
            if (!hasPhoto)
                _photoImages[i].style.backgroundImage = StyleKeyword.None;
            else if (data.photo != null)
                _photoImages[i].style.backgroundImage = new StyleBackground(data.photo);
            else
                _photoImages[i].style.backgroundImage = new StyleBackground(data.photoTexture);

            if (data != null && !unlocked)
            {
                _photoCaptions[i].text = string.IsNullOrWhiteSpace(data.lockedCaption) ? "Unexplored" : data.lockedCaption;
                continue;
            }

            string defaultLabel = i switch
            {
                0 => "Photograph I",
                1 => "Photograph II",
                2 => "Photograph III",
                _ => "Photograph IV"
            };
            _photoCaptions[i].text = (data != null && !string.IsNullOrWhiteSpace(data.caption))
                ? data.caption
                : defaultLabel;
        }
    }

    // =========================================================================
    // JOURNAL MODAL (BOTTOM-TO-UP TRANSITION)
    // =========================================================================

    public void ToggleJournal()
    {
        if (IsVnDialogueActive || _isPaused || _isJournalAnimating) return;
        SetJournalOpen(!_isJournalOpen);
    }

    public void SetJournalOpen(bool open)
    {
        if (_isJournalOpen == open && !_isJournalAnimating) return;
        if (_modalOverlay == null || _journalPanel == null) return;

        CancelHandle(_bookSlideTween);
        CancelHandle(_overlayFadeTween);

        float slideOffset = GetEffectiveBookSlideOffset();

        if (open)
        {
            _isJournalOpen = true;
            _isJournalAnimating = true;
            RefreshJournalPhotos();

            if (levelManager != null)
                levelManager.SetExplorationActive(false);

            _pausePanel.style.display = DisplayStyle.None;
            _saveLoadPanel.style.display = DisplayStyle.None;
            _settingsPanel.style.display = DisplayStyle.None;

            _modalOverlay.style.opacity = 0f;
            _modalOverlay.style.display = DisplayStyle.Flex;

            _currentBookOffsetY = slideOffset;
            _journalPanel.style.translate = new Translate(0f, _currentBookOffsetY);
            _journalPanel.style.display = DisplayStyle.Flex;

            _overlayFadeTween = LMotion.Create(0f, 1f, bookSlideDuration * 0.75f)
                .WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(a => _modalOverlay.style.opacity = a);

            _bookSlideTween = LMotion.Create(_currentBookOffsetY, 0f, bookSlideDuration)
                .WithEase(Ease.OutCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() =>
                {
                    _currentBookOffsetY = 0f;
                    _journalPanel.style.translate = new Translate(0f, 0f);
                    _isJournalAnimating = false;
                })
                .Bind(y =>
                {
                    _currentBookOffsetY = y;
                    _journalPanel.style.translate = new Translate(0f, y);
                });
        }
        else
        {
            _isJournalAnimating = true;
            float closeDuration = bookSlideDuration * 0.72f;

            _overlayFadeTween = LMotion.Create(_modalOverlay.resolvedStyle.opacity, 0f, closeDuration)
                .WithEase(Ease.InQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(a => _modalOverlay.style.opacity = a);

            _bookSlideTween = LMotion.Create(_currentBookOffsetY, slideOffset, closeDuration)
                .WithEase(Ease.InCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() =>
                {
                    _currentBookOffsetY = slideOffset;
                    _journalPanel.style.display = DisplayStyle.None;
                    _modalOverlay.style.display = DisplayStyle.None;
                    _modalOverlay.style.opacity = 1f;
                    _isJournalOpen = false;
                    _isJournalAnimating = false;

                    if (levelManager != null && !_isPaused && !IsVnDialogueActive)
                        levelManager.SetExplorationActive(true);
                })
                .Bind(y =>
                {
                    _currentBookOffsetY = y;
                    _journalPanel.style.translate = new Translate(0f, y);
                });
        }
    }

    // =========================================================================
    // 2.5D PAUSE, SAVE/LOAD & SETTINGS
    // =========================================================================

    public void TogglePause()
    {
        if (IsVnDialogueActive) return;
        SetPaused(!_isPaused);
    }

    public void SetPaused(bool paused)
    {
        if (_isPaused == paused) return;
        _isPaused = paused;
        _isJournalOpen = false;
        _isJournalAnimating = false;
        CancelHandle(_bookSlideTween);
        CancelHandle(_overlayFadeTween);

        Time.timeScale = paused ? 0f : 1f;

        if (levelManager != null && !IsVnDialogueActive)
            levelManager.SetExplorationActive(!paused);

        if (pauseFmodOnPauseMenu && RuntimeManager.IsInitialized)
        {
            try
            {
                RuntimeManager.GetBus("bus:/BGM").setPaused(paused);
                RuntimeManager.GetBus("bus:/SFX").setPaused(paused);
            }
            catch (BusNotFoundException) { }
        }

        ShowPauseMenu(paused);
    }

    private void ShowPauseMenu(bool show)
    {
        _modalOverlay.style.opacity = 1f;
        _modalOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        _pausePanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        _journalPanel.style.display = DisplayStyle.None;
        _saveLoadPanel.style.display = DisplayStyle.None;
        _settingsPanel.style.display = DisplayStyle.None;
    }

    private void OpenSettingsModal()
    {
        _pausePanel.style.display = DisplayStyle.None;
        _saveLoadPanel.style.display = DisplayStyle.None;
        _settingsView?.SyncFromManager();
        _settingsPanel.style.display = DisplayStyle.Flex;
    }

    private void OpenSaveModal()
    {
        if (VNSaveManager.Instance == null || storyController == null) return;
        PopulateSaveLoadList(isSaveMode: true, VNSaveManager.Instance.GetAllSlotSummaries(includeAutosave: false));
    }

    private void OpenLoadModal()
    {
        if (VNSaveManager.Instance == null || storyController == null) return;
        PopulateSaveLoadList(isSaveMode: false, VNSaveManager.Instance.GetAllSlotSummaries(includeAutosave: true));
    }

    private void PopulateSaveLoadList(bool isSaveMode, IReadOnlyList<VNSaveSlotSummary> slots)
    {
        _pausePanel.style.display = DisplayStyle.None;
        _settingsPanel.style.display = DisplayStyle.None;
        _saveLoadPanel.style.display = DisplayStyle.Flex;
        _saveLoadTitle.text = isSaveMode ? "SAVE GAME" : "LOAD GAME";
        _saveLoadList.Clear();

        foreach (VNSaveSlotSummary slot in slots)
        {
            string capturedId = slot.SlotId;
            bool disabledInLoad = !isSaveMode && slot.IsEmpty;

            var card = new Button(() =>
            {
                if (isSaveMode)
                {
                    storyController.EnsureStoryInitialized();
                    VNSaveData data = storyController.CaptureSaveData(capturedId.ToUpperInvariant());
                    VNSaveManager.Instance.SaveToSlot(capturedId, data);
                    OpenSaveModal();
                }
                else
                {
                    SetPaused(false);
                    storyController.RequestLoadSlot(capturedId);
                }
            });

            card.focusable = false;
            card.AddToClassList("slot-card");
            if (disabledInLoad)
            {
                card.AddToClassList("slot-card--empty");
                card.SetEnabled(false);
                card.pickingMode = PickingMode.Ignore;
            }

            var header = new VisualElement();
            header.AddToClassList("slot-card__header");
            header.pickingMode = PickingMode.Ignore;

            var titleLbl = new Label(slot.Title);
            titleLbl.AddToClassList("slot-card__title");
            titleLbl.pickingMode = PickingMode.Ignore;

            var timeLbl = new Label(slot.Timestamp);
            timeLbl.AddToClassList("slot-card__time");
            timeLbl.pickingMode = PickingMode.Ignore;

            var previewLbl = new Label(slot.Preview);
            previewLbl.AddToClassList("slot-card__preview");
            previewLbl.pickingMode = PickingMode.Ignore;

            header.Add(titleLbl);
            header.Add(timeLbl);
            card.Add(header);
            card.Add(previewLbl);
            _saveLoadList.Add(card);
        }
    }

    private async void QuitToMainMenu()
    {
        Time.timeScale = 1f;
        VNSessionBridge.LaunchMode = VNLaunchMode.NewGame;
        await SceneManager.LoadSceneAsync(mainMenuSceneName);
    }

    private static void CancelHandle(MotionHandle handle)
    {
        if (handle.IsActive()) handle.Cancel();
    }
}