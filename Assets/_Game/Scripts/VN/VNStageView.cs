using LitMotion;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;
using FMODUnity;

public enum StageSlot { Auto, Left, Center, Right }

public readonly struct VNChoiceOption
{
    public readonly string Text;
    public readonly bool IsVisited;

    public VNChoiceOption(string text, bool isVisited)
    {
        Text = text;
        IsVisited = isVisited;
    }
}

public class VNStageView : MonoBehaviour
{
    [Header("UI Panel")]
    [SerializeField] private PanelRenderer panelRenderer;

    [Header("Top-Right Button Icons (Optional - Procedural Fallbacks Used If Empty)")]
    [SerializeField] private Sprite autoplayIconSprite;
    [SerializeField] private Sprite skipIconSprite;
    [SerializeField] private Sprite pauseIconSprite;
    [SerializeField] private Sprite backlogIconSprite;
    [SerializeField] private Sprite backlogCloseArrowSprite;
    [SerializeField] private Sprite worldPauseIconSprite;

    [Header("Character Registry")]
    [SerializeField] private List<VNCharacterData> characterDatabase = new();

    [Header("Timings")]
    [SerializeField, Min(1f)] private float defaultCharsPerSecond = 45f;
    [SerializeField] private float bgFadeDuration = 0.45f;
    [SerializeField] private float charMoveDuration = 0.28f;
    [SerializeField] private float charFadeDuration = 0.22f;
    [SerializeField, Min(0f)] private float ellipsisPauseDuration = 2.0f;
    [SerializeField] private bool pauseOnUnicodeEllipsis = true;

    [Header("Backlog Drawer Layout & Animation")]
    [SerializeField, Range(20f, 100f)] private float backlogWidthPercent = 100f;
    [SerializeField, Range(20f, 95f)] private float backlogHeightPercent = 78f;
    [SerializeField, Min(0.05f)] private float backlogSlideDuration = 0.28f;

    [Header("Typewriter Audio (FMOD)")]
    [SerializeField] private EventReference textBlipEvent;
    [SerializeField, Min(1)] private int charsPerClick = 2;

    [Header("Mode Defaults")]
    [SerializeField] private bool hideDialogueOnStart = true;

    [Header("Dialogue & Nameplate Textures")]
    [SerializeField] private Sprite dialogueBoxSprite;
    [SerializeField] private Sprite nameplateSprite;
    [SerializeField] private RectOffset dialogueInnerPadding;
    [SerializeField] private RectOffset nameplateInnerPadding;

    // Lifecycle & Input Events consumed by VNStoryController
    public event Action OnViewReady;
    public event Action OnAdvanceRequested;
    public event Action<int> OnChoiceSelected;
    public event Action OnLineTypingCompleted;
    public event Action OnAutoClicked;
    public event Action OnSkipClicked;
    public event Action OnPauseClicked;
    public event Action OnResumeClicked;
    public event Action OnSaveClicked;
    public event Action OnLoadClicked;
    public event Action OnQuitClicked;
    public event Action<bool, string> OnSaveSlotSelected; // (isSaveMode, slotId)
    public event Action OnSaveLoadBackClicked;
    public event Action OnLogClicked;
    public event Action OnBacklogBackClicked;

    public bool IsReady => _boundRoot != null;
    public bool IsDialogueVisible => _boundRoot != null && _boundRoot.resolvedStyle.display != DisplayStyle.None;
    public bool IsTyping => _isTyping;
    public bool IsBacklogOpen => _isBacklogOpen;

    // UXML Elements
    private VisualElement _boundRoot;
    private int _lastBoundVersion = -1;
    private VisualElement _bgBack;
    private VisualElement _bgFront;
    private VisualElement _stageLayer;
    private VisualElement _cgLayer;
    private VisualElement _advanceSurface;
    private VisualElement _dialogueBox;
    private VisualElement _nameplate;
    private Label _speakerLabel;
    private Label _dialogueLabel;
    private VisualElement _continueIndicator;
    private VisualElement _choiceOverlay;
    private VisualElement _choiceList;
    private VisualElement _modalOverlay;
    private VisualElement _pausePanel;
    private VisualElement _saveLoadPanel;
    private Label _saveLoadTitle;
    private VisualElement _saveLoadList;
    private VisualElement _settingsPanel;
    private VNSettingsView _settingsView;
    private VisualElement _backlogPanel;
    private ScrollView _backlogScroll;

    private Button _btnAutoplay;
    private VisualElement _autoplayIcon;
    private Button _btnSkip;
    private Button _btnPause;
    private Button _btnBacklog;

    // Runtime State
    private int _audibleCharCount;
    private CancellationTokenSource _typingCts;
    private string _currentFullText = string.Empty;
    private bool _isTyping;

    private readonly Dictionary<string, VNCharacterData> _charDb = new();
    private readonly Dictionary<string, VisualElement> _activeSlots = new();
    private readonly Dictionary<string, StageSlot> _assignedSlots = new();
    private readonly Dictionary<string, MotionHandle> _charMoveTweens = new();
    private readonly Dictionary<string, MotionHandle> _charFadeTweens = new();
    private readonly List<string> _entranceOrder = new();
    private readonly List<UnityEngine.Object> _proceduralIcons = new();
    private readonly Dictionary<string, string> _activeExpressions = new();
    private string _activeSpeakerId = string.Empty;

    private MotionHandle _bgTween;
    private MotionHandle _cgTween;
    private MotionHandle _indicatorTween;
    private MotionHandle _autoplaySpinTween;
    private MotionHandle _backlogSlideTween;
    private float _currentBacklogYPercent = -100f;
    private bool _isBacklogOpen;
    private bool _isBacklogClosing;

    private void Awake()
    {
        foreach (var data in characterDatabase)
        {
            if (data != null && !string.IsNullOrEmpty(data.characterId))
                _charDb[data.characterId.ToLowerInvariant()] = data;
        }
    }

    private void OnValidate()
    {
        ApplyDialogueTextures();
        ApplyBacklogLayout();
    }

    private void OnEnable()
    {
        if (panelRenderer == null)
        {
            Debug.LogError("[VNStageView] PanelRenderer reference is missing.");
            return;
        }

        if (VNSettingsManager.Instance != null)
            VNSettingsManager.Instance.OnSettingsChanged += ApplyStageSettings;
        ApplyStageSettings();

        panelRenderer.RegisterUIReloadCallback(HandleUIReload);
    }

    private void OnDisable()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(HandleUIReload);

        if (VNSettingsManager.Instance != null)
            VNSettingsManager.Instance.OnSettingsChanged -= ApplyStageSettings;

        _settingsView?.Dispose();
        _settingsView = null;
        CancelTypingTask();
    }

    private void OnDestroy()
    {
        CancelHandle(_bgTween);
        CancelHandle(_cgTween);
        CancelHandle(_indicatorTween);
        CancelHandle(_autoplaySpinTween);
        CancelHandle(_backlogSlideTween);
        CancelTypingTask();

        foreach (var handle in _charMoveTweens.Values) CancelHandle(handle);
        foreach (var handle in _charFadeTweens.Values) CancelHandle(handle);

        foreach (var obj in _proceduralIcons)
        {
            if (obj != null) Destroy(obj);
        }
        _proceduralIcons.Clear();
    }

    private void CancelTypingTask()
    {
        if (_typingCts != null)
        {
            _typingCts.Cancel();
            _typingCts.Dispose();
            _typingCts = null;
        }
    }

    private void HandleUIReload(PanelRenderer pr, VisualElement root, int version)
    {
        if (root == null) return;

        if (_boundRoot != root || _lastBoundVersion != version)
        {
            _activeSlots.Clear();
            _assignedSlots.Clear();
            _entranceOrder.Clear();

            BindUxmlElements(root);
            _boundRoot = root;
            _lastBoundVersion = version;
        }

        OnViewReady?.Invoke();

        if (hideDialogueOnStart)
            SetDialogueVisible(false);
    }

    public void SetDialogueVisible(bool visible)
    {
        if (_boundRoot != null)
            _boundRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void BindUxmlElements(VisualElement root)
    {
        _bgBack = root.Q<VisualElement>("bg-back");
        _bgFront = root.Q<VisualElement>("bg-front");
        _stageLayer = root.Q<VisualElement>("layer-stage");
        _cgLayer = root.Q<VisualElement>("layer-cg");
        _advanceSurface = root.Q<VisualElement>("advance-surface");

        _dialogueBox = root.Q<VisualElement>("dialogue-box");
        _nameplate = root.Q<VisualElement>("nameplate");
        _speakerLabel = root.Q<Label>("speaker-label");
        _dialogueLabel = root.Q<Label>("dialogue-label");
        _continueIndicator = root.Q<VisualElement>("continue-indicator");

        _choiceOverlay = root.Q<VisualElement>("layer-choices");
        _choiceList = root.Q<VisualElement>("choice-list");
        _modalOverlay = root.Q<VisualElement>("layer-modal");

        _btnAutoplay = root.Q<Button>("btn-autoplay");
        _autoplayIcon = root.Q<VisualElement>("icon-autoplay");
        _btnSkip = root.Q<Button>("btn-skip");
        _btnBacklog = root.Q<Button>("btn-backlog");
        _btnPause = root.Q<Button>("btn-pause");
        _pausePanel = root.Q<VisualElement>("pause-menu");
        _saveLoadPanel = root.Q<VisualElement>("saveload-panel");
        _saveLoadTitle = root.Q<Label>("saveload-title");
        _saveLoadList = root.Q<VisualElement>("saveload-list");

        _settingsPanel = root.Q<VisualElement>("settings-panel");
        _backlogPanel = root.Q<VisualElement>("backlog-panel");
        _backlogScroll = root.Q<ScrollView>("backlog-scroll");

        _settingsView?.Dispose();
        _settingsView = new VNSettingsView(_settingsPanel, () => ShowPauseMenu(true));

        _btnAutoplay.style.display = DisplayStyle.None;
        _btnSkip.style.display = DisplayStyle.None;

        var btnPauseSettings = root.Q<Button>("btn-pause-settings");
        if (btnPauseSettings != null)
        {
            btnPauseSettings.focusable = false;
            btnPauseSettings.clicked += ShowSettingsModal;
        }

        root.Q<Button>("btn-saveload-back").clicked += () => OnSaveLoadBackClicked?.Invoke();

        var btnResume = root.Q<Button>("btn-resume");
        var btnSave = root.Q<Button>("btn-pause-save");
        var btnLoad = root.Q<Button>("btn-pause-load");
        var btnQuit = root.Q<Button>("btn-pause-quit");

        var btnBacklogClose = root.Q<Button>("btn-backlog-close");
        if (btnBacklogClose != null)
        {
            btnBacklogClose.focusable = false;
            btnBacklogClose.clicked += RequestCloseBacklog;
        }

        var backlogArrowIcon = root.Q<VisualElement>("icon-backlog-arrow");
        if (backlogArrowIcon != null)
        {
            backlogArrowIcon.style.backgroundImage = new StyleBackground(
                backlogCloseArrowSprite != null ? backlogCloseArrowSprite : CreateUpArrowIcon());
        }

        _modalOverlay.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0 && evt.target == _modalOverlay && _isBacklogOpen)
                RequestCloseBacklog();
        });

        ApplyBacklogLayout();

        // Prevent Space/Enter from re-triggering top icons after clicking them
        _btnAutoplay.focusable = false;
        _btnSkip.focusable = false;
        _btnBacklog.focusable = false;
        _btnPause.focusable = false;

        // Assign custom Inspector icons or generate clean procedural icons
        _autoplayIcon.style.backgroundImage = new StyleBackground(
            autoplayIconSprite != null ? autoplayIconSprite : CreateAutoplayIcon());
        root.Q<VisualElement>("icon-skip").style.backgroundImage = new StyleBackground(
            skipIconSprite != null ? skipIconSprite : CreateSkipIcon());
        root.Q<VisualElement>("icon-backlog").style.backgroundImage = new StyleBackground(
            backlogIconSprite != null ? backlogIconSprite : CreateBacklogIcon());
        root.Q<VisualElement>("icon-pause").style.backgroundImage = new StyleBackground(
            pauseIconSprite != null ? pauseIconSprite : CreatePauseIcon());

        _advanceSurface.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0) OnAdvanceRequested?.Invoke();
        });
        _dialogueBox.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0) OnAdvanceRequested?.Invoke();
        });

        _btnAutoplay.clicked += () => OnAutoClicked?.Invoke();
        _btnSkip.clicked += () => OnSkipClicked?.Invoke();
        _btnBacklog.clicked += () => OnLogClicked?.Invoke();
        _btnPause.clicked += () => OnPauseClicked?.Invoke();

        btnResume.clicked += () => OnResumeClicked?.Invoke();
        btnSave.clicked += () => OnSaveClicked?.Invoke();
        btnLoad.clicked += () => OnLoadClicked?.Invoke();
        btnQuit.clicked += () => OnQuitClicked?.Invoke();

        ApplyDialogueTextures();
    }

    // =========================================================================
    // DIALOGUE & CHOICES
    // =========================================================================

    public void PresentLine(string speakerIdOrName, string text, bool instant = false)
    {
        CancelTypingTask();
        _audibleCharCount = 0;
        HideChoices();
        StopContinueIndicator();

        if (string.IsNullOrWhiteSpace(speakerIdOrName))
        {
            // Narration line: hide nameplate and dim all active portraits
            if (_nameplate != null)
                _nameplate.style.display = DisplayStyle.None;
            SetActiveSpeaker(null);
        }
        else
        {
            if (_nameplate != null)
                _nameplate.style.display = DisplayStyle.Flex;

            string lookupKey = speakerIdOrName.ToLowerInvariant();

            if (_charDb.TryGetValue(lookupKey, out var charData))
            {
                _speakerLabel.text = charData.displayName;
                _speakerLabel.style.color = charData.nameColor;
                if (_nameplate != null)
                {
                    _nameplate.style.borderTopColor = charData.nameColor;
                    _nameplate.style.borderBottomColor = charData.nameColor;
                    _nameplate.style.borderLeftColor = charData.nameColor;
                    _nameplate.style.borderRightColor = charData.nameColor;
                }
                SetActiveSpeaker(lookupKey);
            }
            else
            {
                _speakerLabel.text = speakerIdOrName;
                SetActiveSpeaker(null);
            }
        }

        _currentFullText = text ?? string.Empty;

        if (_dialogueLabel == null)
            return;

        if (instant || string.IsNullOrEmpty(_currentFullText))
        {
            _dialogueLabel.text = _currentFullText;
            _isTyping = false;
            HandleTypingCompleted();
            return;
        }

        _typingCts = new CancellationTokenSource();
        _ = TypeTextAsync(_currentFullText, _typingCts.Token);
    }

    public void CompleteTyping()
    {
        if (!_isTyping)
            return;

        CancelTypingTask();

        if (_dialogueLabel != null)
            _dialogueLabel.text = _currentFullText;

        _isTyping = false;
        HandleTypingCompleted();
    }

    private async Awaitable TypeTextAsync(string fullText, CancellationToken ct)
    {
        _isTyping = true;
        _dialogueLabel.text = string.Empty;

        int index = 0;
        int len = fullText.Length;
        int consecutiveDots = 0;
        float charCarry = 0f;

        try
        {
            while (index < len)
            {
                ct.ThrowIfCancellationRequested();

                // Skip rich-text tags (e.g., <i>, </i>, <color=#...>) atomically
                if (fullText[index] == '<')
                {
                    int closeIdx = fullText.IndexOf('>', index);
                    if (closeIdx != -1)
                    {
                        index = closeIdx + 1;
                        _dialogueLabel.text = fullText.Substring(0, index);
                        continue;
                    }
                }

                char c = fullText[index];
                index++;
                _dialogueLabel.text = fullText.Substring(0, index);

                if (!char.IsWhiteSpace(c) && _audibleCharCount++ % charsPerClick == 0)
                    PlayTypingBlip();

                // Detect "..." (or single-character '…')
                bool shouldPauseForEllipsis = false;
                if (c == '.')
                {
                    consecutiveDots++;
                    if (consecutiveDots >= 3)
                    {
                        consecutiveDots = 0;
                        shouldPauseForEllipsis = true;
                    }
                }
                else
                {
                    consecutiveDots = 0;
                    if (pauseOnUnicodeEllipsis && c == '\u2026')
                        shouldPauseForEllipsis = true;
                }

                if (shouldPauseForEllipsis && ellipsisPauseDuration > 0f)
                {
                    charCarry = 0f;
                    await WaitScaledSecondsAsync(ellipsisPauseDuration, ct);
                    continue;
                }

                // Standard character interval using scaled deltaTime (respects Pause Menu)
                float cps = Mathf.Max(1f, defaultCharsPerSecond);
                float interval = 1f / cps;
                charCarry += interval;

                while (charCarry > 0f)
                {
                    await Awaitable.NextFrameAsync(ct);
                    charCarry -= Time.deltaTime;
                }
            }

            _isTyping = false;
            HandleTypingCompleted();
        }
        catch (OperationCanceledException)
        {
            // Handled cleanly by CompleteTyping() or object teardown
        }
    }

    private static async Awaitable WaitScaledSecondsAsync(float duration, CancellationToken ct)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            await Awaitable.NextFrameAsync(ct);
            elapsed += Time.deltaTime;
        }
    }

    private void PlayTypingBlip()
    {
        if (!textBlipEvent.IsNull && RuntimeManager.IsInitialized)
            RuntimeManager.PlayOneShot(textBlipEvent);
    }

    public void ApplyDialogueTextures()
    {
        if (_dialogueBox != null)
        {
            if (dialogueBoxSprite != null)
                _dialogueBox.style.backgroundImage = new StyleBackground(dialogueBoxSprite);

            if (dialogueInnerPadding != null &&
                (dialogueInnerPadding.left > 0 || dialogueInnerPadding.right > 0 ||
                 dialogueInnerPadding.top > 0 || dialogueInnerPadding.bottom > 0))
            {
                _dialogueBox.style.paddingLeft = dialogueInnerPadding.left;
                _dialogueBox.style.paddingRight = dialogueInnerPadding.right;
                _dialogueBox.style.paddingTop = dialogueInnerPadding.top;
                _dialogueBox.style.paddingBottom = dialogueInnerPadding.bottom;
            }
        }

        if (_nameplate != null)
        {
            if (nameplateSprite != null)
                _nameplate.style.backgroundImage = new StyleBackground(nameplateSprite);

            if (nameplateInnerPadding != null &&
                (nameplateInnerPadding.left > 0 || nameplateInnerPadding.right > 0 ||
                 nameplateInnerPadding.top > 0 || nameplateInnerPadding.bottom > 0))
            {
                _nameplate.style.paddingLeft = nameplateInnerPadding.left;
                _nameplate.style.paddingRight = nameplateInnerPadding.right;
                _nameplate.style.paddingTop = nameplateInnerPadding.top;
                _nameplate.style.paddingBottom = nameplateInnerPadding.bottom;
            }
        }
    }

    public void SetTopBarFeatureVisibility(bool showAutoplay, bool showSkip)
    {
        if (_btnAutoplay != null)
            _btnAutoplay.style.display = showAutoplay ? DisplayStyle.Flex : DisplayStyle.None;

        if (_btnSkip != null)
            _btnSkip.style.display = showSkip ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private Sprite CreateBacklogIcon()
    {
        // Three horizontal lines representing a text history log
        return BuildIconSprite(64, (x, y) =>
        {
            bool inWidth = x >= 16 && x <= 48;
            bool line1 = y >= 41 && y <= 45;
            bool line2 = y >= 30 && y <= 34;
            bool line3 = y >= 19 && y <= 23;
            return inWidth && (line1 || line2 || line3);
        });
    }

    public void ShowChoices(IReadOnlyList<VNChoiceOption> choices)
    {
        StopContinueIndicator();
        _choiceList.Clear();

        for (int i = 0; i < choices.Count; i++)
        {
            int capturedIndex = i;
            VNChoiceOption option = choices[i];

            var btn = new Button(() =>
            {
                HideChoices();
                OnChoiceSelected?.Invoke(capturedIndex);
            })
            {
                text = option.Text
            };

            btn.AddToClassList("choice-btn");

            if (option.IsVisited)
            {
                btn.AddToClassList("choice-btn--visited");
                btn.SetEnabled(false);
                btn.pickingMode = PickingMode.Ignore;
            }

            _choiceList.Add(btn);
        }

        _choiceOverlay.style.display = DisplayStyle.Flex;
    }

    public void HideChoices()
    {
        _choiceOverlay.style.display = DisplayStyle.None;
        _choiceList.Clear();
    }

    public void SetAutoState(bool active)
    {
        if (_btnAutoplay == null || _autoplayIcon == null) return;

        _btnAutoplay.EnableInClassList("icon-btn--active", active);
        CancelHandle(_autoplaySpinTween);

        if (active)
        {
            _autoplaySpinTween = LMotion.Create(0f, 360f, 1.4f)
                .WithEase(Ease.Linear)
                .WithLoops(-1, LoopType.Restart)
                .Bind(deg => _autoplayIcon.style.rotate = new Rotate(deg));
        }
        else
        {
            _autoplayIcon.style.rotate = new Rotate(0f);
        }
    }

    public void SetSkipState(bool active)
    {
        if (_btnSkip == null) return;
        _btnSkip.EnableInClassList("icon-btn--active", active);
    }

    public void ShowPauseMenu(bool show)
    {
        _isBacklogOpen = false;
        if (_modalOverlay != null)
            _modalOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

        if (_pausePanel != null)
            _pausePanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

        if (_saveLoadPanel != null)
            _saveLoadPanel.style.display = DisplayStyle.None;

        if (_settingsPanel != null)
            _settingsPanel.style.display = DisplayStyle.None;

        if (_backlogPanel != null)
            _backlogPanel.style.display = DisplayStyle.None;
    }

    public void ShowSettingsModal()
    {
        _isBacklogOpen = false;
        if (_modalOverlay == null || _settingsPanel == null) return;

        _modalOverlay.style.display = DisplayStyle.Flex;
        _pausePanel.style.display = DisplayStyle.None;
        _saveLoadPanel.style.display = DisplayStyle.None;

        _settingsView?.SyncFromManager();
        _settingsPanel.style.display = DisplayStyle.Flex;

        if (_backlogPanel != null)
            _backlogPanel.style.display = DisplayStyle.None;
    }

    private void ApplyStageSettings()
    {
        if (VNSettingsManager.Instance == null) return;
        defaultCharsPerSecond = VNSettingsManager.Instance.TextSpeed;
    }

    public void ShowSaveLoadModal(bool isSaveMode, IReadOnlyList<VNSaveSlotSummary> slots)
    {
        _isBacklogOpen = false;
        if (_modalOverlay == null || _saveLoadPanel == null) return;
        if (_backlogPanel != null)
            _backlogPanel.style.display = DisplayStyle.None;

        _modalOverlay.style.display = DisplayStyle.Flex;
        _pausePanel.style.display = DisplayStyle.None;
        _saveLoadPanel.style.display = DisplayStyle.Flex;
        _saveLoadTitle.text = isSaveMode ? "SAVE GAME" : "LOAD GAME";

        _saveLoadList.Clear();

        foreach (VNSaveSlotSummary slot in slots)
        {
            string capturedId = slot.SlotId;
            bool disabledInLoad = !isSaveMode && slot.IsEmpty;

            var card = new Button(() => OnSaveSlotSelected?.Invoke(isSaveMode, capturedId));
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

    public List<SavedCharacterState> CaptureCharacterStates()
    {
        var result = new List<SavedCharacterState>(_entranceOrder.Count);
        foreach (string id in _entranceOrder)
        {
            result.Add(new SavedCharacterState
            {
                characterId = id,
                expression = _activeExpressions.TryGetValue(id, out string expr) ? expr : "neutral",
                slot = _assignedSlots.TryGetValue(id, out StageSlot s) ? s : StageSlot.Auto
            });
        }
        return result;
    }

    public string GetActiveSpeakerId() => _activeSpeakerId;

    public void RestoreCharacterStates(List<SavedCharacterState> states, string activeSpeakerId)
    {
        // Immediately remove existing slots without exit fade tweens
        foreach (var handle in _charMoveTweens.Values) CancelHandle(handle);
        foreach (var handle in _charFadeTweens.Values) CancelHandle(handle);
        _charMoveTweens.Clear();
        _charFadeTweens.Clear();

        foreach (var slot in _activeSlots.Values)
            slot.RemoveFromHierarchy();

        _activeSlots.Clear();
        _assignedSlots.Clear();
        _activeExpressions.Clear();
        _entranceOrder.Clear();

        if (states != null)
        {
            foreach (SavedCharacterState state in states)
            {
                string id = state.characterId.ToLowerInvariant();
                if (!_charDb.TryGetValue(id, out var data)) continue;

                var slot = new VisualElement();
                slot.AddToClassList("character-slot");
                slot.pickingMode = PickingMode.Ignore;
                slot.style.opacity = 1f;

                Sprite sprite = data.GetExpression(state.expression);
                if (sprite != null)
                    slot.style.backgroundImage = new StyleBackground(sprite);

                _stageLayer.Add(slot);
                _activeSlots[id] = slot;
                _assignedSlots[id] = state.slot;
                _activeExpressions[id] = state.expression;
                _entranceOrder.Add(id);
            }

            // Snap positions instantly without sliding animations
            int count = _entranceOrder.Count;
            for (int i = 0; i < count; i++)
            {
                string id = _entranceOrder[i];
                StageSlot explicitSlot = _assignedSlots[id];
                float targetX = explicitSlot != StageSlot.Auto ? SlotToX(explicitSlot) : ResolveAutoX(i, count);
                _activeSlots[id].style.translate = new Translate(targetX, 0f);
            }
        }

        SetActiveSpeaker(activeSpeakerId);
    }

    private void HandleTypingCompleted()
    {
        CancelHandle(_indicatorTween);
        if (_continueIndicator != null)
        {
            _continueIndicator.style.opacity = 0.25f;

            _indicatorTween = LMotion.Create(0.25f, 1f, 0.55f)
                .WithEase(Ease.InOutSine)
                .WithLoops(-1, LoopType.Yoyo)
                .Bind(a => _continueIndicator.style.opacity = a);
        }

        OnLineTypingCompleted?.Invoke();
    }

    private void StopContinueIndicator()
    {
        CancelHandle(_indicatorTween);
        if (_continueIndicator != null)
            _continueIndicator.style.opacity = 0f;
    }

    // =========================================================================
    // BACKGROUNDS & EVENT CGS
    // =========================================================================

    public void SetBackground(Sprite newSprite, bool instant = false)
    {
        CancelHandle(_bgTween);

        if (newSprite == null)
        {
            _bgBack.style.backgroundImage = StyleKeyword.None;
            _bgFront.style.backgroundImage = StyleKeyword.None;
            return;
        }

        var nextBg = new StyleBackground(newSprite);

        if (instant || bgFadeDuration <= 0f)
        {
            _bgBack.style.backgroundImage = nextBg;
            _bgFront.style.backgroundImage = StyleKeyword.None;
            _bgFront.style.opacity = 0f;
            return;
        }

        _bgFront.style.backgroundImage = nextBg;
        _bgFront.style.opacity = 0f;

        _bgTween = LMotion.Create(0f, 1f, bgFadeDuration)
            .WithEase(Ease.OutQuad)
            .WithOnComplete(() =>
            {
                _bgBack.style.backgroundImage = nextBg;
                _bgFront.style.backgroundImage = StyleKeyword.None;
                _bgFront.style.opacity = 0f;
            })
            .Bind(a => _bgFront.style.opacity = a);
    }

    public void ShowEventCG(Sprite cgSprite, bool instant = false)
    {
        CancelHandle(_cgTween);

        if (cgSprite == null)
        {
            HideEventCG(instant);
            return;
        }

        _cgLayer.style.backgroundImage = new StyleBackground(cgSprite);
        _cgLayer.style.display = DisplayStyle.Flex;

        if (instant)
        {
            _cgLayer.style.opacity = 1f;
            return;
        }

        _cgTween = LMotion.Create(_cgLayer.resolvedStyle.opacity, 1f, bgFadeDuration)
            .WithEase(Ease.OutQuad)
            .Bind(a => _cgLayer.style.opacity = a);
    }

    public void HideEventCG(bool instant = false)
    {
        CancelHandle(_cgTween);

        if (instant)
        {
            _cgLayer.style.opacity = 0f;
            _cgLayer.style.display = DisplayStyle.None;
            _cgLayer.style.backgroundImage = StyleKeyword.None;
            return;
        }

        _cgTween = LMotion.Create(_cgLayer.resolvedStyle.opacity, 0f, bgFadeDuration)
            .WithEase(Ease.OutQuad)
            .WithOnComplete(() =>
            {
                _cgLayer.style.display = DisplayStyle.None;
                _cgLayer.style.backgroundImage = StyleKeyword.None;
            })
            .Bind(a => _cgLayer.style.opacity = a);
    }

    // =========================================================================
    // CHARACTER STAGE
    // =========================================================================

    public void ShowCharacter(string rawId, string expression = "neutral", StageSlot requestedSlot = StageSlot.Auto)
    {
        string id = rawId.ToLowerInvariant();
        if (!_charDb.TryGetValue(id, out var data))
        {
            Debug.LogWarning($"[VNStageView] Unknown character ID: {id}");
            return;
        }

        bool isNewEntrance = !_activeSlots.TryGetValue(id, out var slot);
        if (isNewEntrance)
        {
            slot = new VisualElement();
            slot.AddToClassList("character-slot");
            slot.pickingMode = PickingMode.Ignore;
            slot.style.opacity = 0f;

            _stageLayer.Add(slot);
            _activeSlots[id] = slot;
            _entranceOrder.Add(id);
        }

        Sprite sprite = data.GetExpression(expression);
        if (sprite != null)
        {
            slot.style.backgroundImage = new StyleBackground(sprite);
            _activeExpressions[id] = expression;
        }

        _assignedSlots[id] = requestedSlot;
        RecalculateStageLayout(isNewEntrance ? id : null);

        if (isNewEntrance)
            FadeCharacter(id, slot, 0f, 1f, destroyOnComplete: false);
    }

    public void HideCharacter(string rawId)
    {
        string id = rawId.ToLowerInvariant();
        if (!_activeSlots.TryGetValue(id, out var slot))
            return;

        _activeSlots.Remove(id);
        _assignedSlots.Remove(id);
        _entranceOrder.Remove(id);
        _activeExpressions.Remove(id);

        RecalculateStageLayout(newlyEnteredId: null);
        FadeCharacter(id, slot, slot.resolvedStyle.opacity, 0f, destroyOnComplete: true);
    }

    public void ClearAllCharacters()
    {
        foreach (var id in new List<string>(_activeSlots.Keys))
            HideCharacter(id);
    }

    public void SetActiveSpeaker(string rawId)
    {
        string speakerId = string.IsNullOrEmpty(rawId) ? string.Empty : rawId.ToLowerInvariant();

        foreach (var (id, slot) in _activeSlots)
        {
            if (id == speakerId)
            {
                slot.RemoveFromClassList("character-slot--dimmed");
                slot.BringToFront();
                _activeSpeakerId = speakerId;
            }
            else
            {
                slot.AddToClassList("character-slot--dimmed");
            }
        }
    }

    private void RecalculateStageLayout(string newlyEnteredId)
    {
        int count = _entranceOrder.Count;

        for (int i = 0; i < count; i++)
        {
            string id = _entranceOrder[i];
            VisualElement slot = _activeSlots[id];
            StageSlot explicitSlot = _assignedSlots[id];

            float targetX = explicitSlot != StageSlot.Auto
                ? SlotToX(explicitSlot)
                : ResolveAutoX(i, count);

            if (id == newlyEnteredId)
            {
                slot.style.translate = new Translate(targetX, 0f);
                continue;
            }

            MoveCharacter(id, slot, targetX);
        }
    }

    private static float ResolveAutoX(int index, int totalCount)
    {
        if (totalCount <= 1) return 0f;
        if (totalCount == 2) return index == 0 ? -420f : 420f;
        return index switch
        {
            0 => -540f,
            1 => 0f,
            _ => 540f
        };
    }

    private static float SlotToX(StageSlot slot) => slot switch
    {
        StageSlot.Left => -420f,
        StageSlot.Right => 420f,
        _ => 0f
    };

    private void MoveCharacter(string id, VisualElement slot, float targetX)
    {
        if (_charMoveTweens.TryGetValue(id, out var existing))
            CancelHandle(existing);

        float startX = slot.resolvedStyle.translate.x;
        if (Mathf.Approximately(startX, targetX))
            return;

        _charMoveTweens[id] = LMotion.Create(startX, targetX, charMoveDuration)
            .WithEase(Ease.OutCubic)
            .Bind(x => slot.style.translate = new Translate(x, 0f));
    }

    private void FadeCharacter(string id, VisualElement slot, float from, float to, bool destroyOnComplete)
    {
        if (_charFadeTweens.TryGetValue(id, out var existing))
            CancelHandle(existing);

        var builder = LMotion.Create(from, to, charFadeDuration).WithEase(Ease.OutQuad);

        _charFadeTweens[id] = destroyOnComplete
            ? builder.WithOnComplete(() => slot.RemoveFromHierarchy()).Bind(a => slot.style.opacity = a)
            : builder.Bind(a => slot.style.opacity = a);
    }

    private static void CancelHandle(MotionHandle handle)
    {
        if (handle.IsActive()) handle.Cancel();
    }

    public void RegisterCharacter(VNCharacterData data)
    {
        if (data != null && !string.IsNullOrEmpty(data.characterId))
            _charDb[data.characterId.ToLowerInvariant()] = data;
    }

    // =========================================================================
    // PROCEDURAL FALLBACK ICONS (64x64 FullRect Sprites)
    // =========================================================================

    private Sprite CreateAutoplayIcon()
    {
        // Circular ring with gap + arrowhead (spins visibly when active)
        return BuildIconSprite(64, (x, y) =>
        {
            float dx = x - 32f, dy = y - 32f;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; // -180 to 180
            bool onRing = dist >= 16f && dist <= 22f && !(angle > 15f && angle < 75f);
            bool onArrow = Mathf.Abs(x - 49f) + Mathf.Abs(y - 36f) <= 7f && y <= 37f;
            return onRing || onArrow;
        });
    }

    private Sprite CreateSkipIcon()
    {
        // Double forward triangles + end bar (>>|)
        return BuildIconSprite(64, (x, y) =>
        {
            float dy = Mathf.Abs(y - 32f);
            bool tri1 = x >= 12 && x <= 28 && dy <= (28 - x) * 0.85f;
            bool tri2 = x >= 28 && x <= 44 && dy <= (44 - x) * 0.85f;
            bool bar = x >= 47 && x <= 52 && dy <= 14f;
            return tri1 || tri2 || bar;
        });
    }

    private Sprite CreatePauseIcon()
    {
        // Two vertical pause bars (||)
        return BuildIconSprite(64, (x, y) =>
        {
            bool inHeight = y >= 16 && y <= 48;
            bool leftBar = x >= 20 && x <= 27;
            bool rightBar = x >= 37 && x <= 44;
            return inHeight && (leftBar || rightBar);
        });
    }

    private Sprite BuildIconSprite(int size, Func<int, int, bool> predicate)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = predicate(x, y) ? Color.white : Color.clear;

        tex.SetPixels(pixels);
        tex.Apply();
        _proceduralIcons.Add(tex);

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _proceduralIcons.Add(sprite);
        return sprite;
    }

    // =========================================================================
    // DIALOGUE BACKLOG DRAWER
    // =========================================================================

    public void ApplyBacklogLayout()
    {
        if (_backlogPanel == null) return;
        _backlogPanel.style.width = new Length(Mathf.Clamp(backlogWidthPercent, 20f, 100f), LengthUnit.Percent);
        _backlogPanel.style.height = new Length(Mathf.Clamp(backlogHeightPercent, 20f, 95f), LengthUnit.Percent);
    }

    public void ShowBacklogModal(IReadOnlyList<VNBacklogEntry> entries)
    {
        if (_modalOverlay == null || _backlogPanel == null || _backlogScroll == null)
            return;

        CancelHandle(_backlogSlideTween);
        _isBacklogOpen = true;
        _isBacklogClosing = false;

        ApplyBacklogLayout();

        _modalOverlay.style.display = DisplayStyle.Flex;
        _pausePanel.style.display = DisplayStyle.None;
        _saveLoadPanel.style.display = DisplayStyle.None;
        _settingsPanel.style.display = DisplayStyle.None;
        _backlogPanel.style.display = DisplayStyle.Flex;

        _backlogScroll.Clear();

        if (entries == null || entries.Count == 0)
        {
            var emptyLbl = new Label("No dialogue recorded yet.");
            emptyLbl.AddToClassList("backlog-empty");
            emptyLbl.pickingMode = PickingMode.Ignore;
            _backlogScroll.Add(emptyLbl);
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                VNBacklogEntry entry = entries[i];

                var row = new VisualElement();
                row.AddToClassList("backlog-entry");
                row.pickingMode = PickingMode.Ignore;

                if (entry.isChoice)
                    row.AddToClassList("backlog-entry--choice");

                string headerText = entry.isChoice ? "CHOICE" : entry.speaker;
                if (!string.IsNullOrWhiteSpace(headerText))
                {
                    var speakerLbl = new Label(headerText);
                    speakerLbl.AddToClassList("backlog-entry__speaker");
                    speakerLbl.pickingMode = PickingMode.Ignore;
                    row.Add(speakerLbl);
                }

                string bodyText = entry.isChoice ? $"> {entry.text}" : entry.text;
                var bodyLbl = new Label(bodyText) { enableRichText = true };
                bodyLbl.AddToClassList("backlog-entry__text");
                bodyLbl.pickingMode = PickingMode.Ignore;
                row.Add(bodyLbl);

                _backlogScroll.Add(row);
            }

            _backlogScroll.schedule.Execute(() =>
                _backlogScroll.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        // Slide smoothly down from top edge (using unscaled time since game is paused)
        _currentBacklogYPercent = -100f;
        _backlogPanel.style.translate = new Translate(0f, new Length(_currentBacklogYPercent, LengthUnit.Percent));

        _backlogSlideTween = LMotion.Create(_currentBacklogYPercent, 0f, backlogSlideDuration)
            .WithEase(Ease.OutCubic)
            .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
            .WithOnComplete(() =>
            {
                _currentBacklogYPercent = 0f;
                _backlogPanel.style.translate = new Translate(0f, new Length(0f, LengthUnit.Percent));
            })
            .Bind(pct =>
            {
                _currentBacklogYPercent = pct;
                _backlogPanel.style.translate = new Translate(0f, new Length(pct, LengthUnit.Percent));
            });
    }

    public void RequestCloseBacklog()
    {
        if (!_isBacklogOpen || _isBacklogClosing || _backlogPanel == null)
            return;

        _isBacklogClosing = true;
        CancelHandle(_backlogSlideTween);

        float startPct = _currentBacklogYPercent;
        _backlogSlideTween = LMotion.Create(startPct, -100f, backlogSlideDuration)
            .WithEase(Ease.InCubic)
            .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
            .WithOnComplete(() =>
            {
                _currentBacklogYPercent = -100f;
                _backlogPanel.style.translate = new Translate(0f, new Length(-100f, LengthUnit.Percent));
                _backlogPanel.style.display = DisplayStyle.None;
                _modalOverlay.style.display = DisplayStyle.None;
                _isBacklogOpen = false;
                _isBacklogClosing = false;
                OnBacklogBackClicked?.Invoke();
            })
            .Bind(pct =>
            {
                _currentBacklogYPercent = pct;
                _backlogPanel.style.translate = new Translate(0f, new Length(pct, LengthUnit.Percent));
            });
    }

    private Sprite CreateUpArrowIcon()
    {
        // Upward-pointing chevron (^) for retracting the top backlog drawer
        return BuildIconSprite(64, (x, y) =>
        {
            float dx = Mathf.Abs(x - 32f);
            float centerY = 39f - dx * 0.75f;
            return dx <= 18f && Mathf.Abs(y - centerY) <= 2.6f;
        });
    }

    public void TriggerGlitch(float duration = 0.35f)
    {
        if (_boundRoot == null || duration <= 0f) return;

        LMotion.Create(0f, 1f, duration)
            .WithOnComplete(() => _boundRoot.style.translate = new Translate(0f, 0f))
            .Bind(t =>
            {
                float decay = 1f - t;
                float offsetX = UnityEngine.Random.Range(-18f, 18f) * decay;
                _boundRoot.style.translate = new Translate(offsetX, 0f);
            });
    }
}