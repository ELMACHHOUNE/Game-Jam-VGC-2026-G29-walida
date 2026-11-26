using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Ink.Runtime;
using FMOD.Studio;
using FMODUnity;
using Walida.Audio;

public class VNStoryController : MonoBehaviour
{
    [Serializable]
    public struct NamedSprite
    {
        public string id;
        public Sprite sprite;
    }

    [Header("Dependencies")]
    [SerializeField] private VNStageView stageView;
    [SerializeField] private TextAsset inkJsonAsset;

    [Header("Feature Toggles")]
    [SerializeField] private bool enableAutoplay = false;
    [SerializeField] private bool enableSkip = false;

    [Header("Backlog")]
    [SerializeField, Min(10)] private int maxBacklogEntries = 150;

    [Header("Scene Navigation")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Stage Sprite Registries")]
    [SerializeField] private List<NamedSprite> backgrounds = new();
    [SerializeField] private List<NamedSprite> eventCGs = new();

    [Header("Playback Timings")]
    [SerializeField] private float autoAdvanceDelay = 1.6f;
    [SerializeField] private float choiceRevealDelay = 1.0f;
    [SerializeField] private bool cancelAutoOnManualClick = true;

    [Serializable]
    public struct NamedFMODEvent
    {
        public string id;
        public EventReference fmodEvent;
    }

    [Header("Audio Registries (FMOD)")]
    [Tooltip("Optional: short ids usable in Ink (#sfx:phone_ring) mapped to FMOD events. Full event:/ paths and FMODEvents aliases work without an entry.")]
    [SerializeField] private List<NamedFMODEvent> fmodSfxEvents = new();
    [Tooltip("Optional: short ids usable in Ink (#bgm:opening) mapped to FMOD events.")]
    [SerializeField] private List<NamedFMODEvent> fmodBgmEvents = new();

    [Header("Audio Settings (FMOD)")]
    [SerializeField] private bool pauseAudioOnPauseMenu = true;

    [Header("Optional Keyboard/Gamepad Input")]
    [SerializeField] private InputActionReference advanceInputAction;
    [SerializeField] private InputActionReference toggleAutoInputAction;
    [Tooltip("Advance dialogue with the Space key when no Advance Input Action is assigned.")]
    [SerializeField] private bool advanceWithSpaceKey = true;

    [Header("Denial Score")]
    [Tooltip("Ink global variable that holds the score (declare `VAR denial_score = 0` in Ink). " +
             "#denial:N tags add to it, and `~ denial_score += 1` in Ink works too. If the variable isn't declared, an internal counter is used.")]
    [SerializeField] private string denialVariableName = "denial_score";

    [Header("Exploration Integration")]
    [SerializeField] private bool startStoryOnBoot = false;
    [SerializeField] private bool deferBootToFlowController = true;

    private Story _story;
    private readonly Dictionary<string, Sprite> _bgLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sprite> _cgLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventReference> _fmodSfxLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventReference> _fmodBgmLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visitedChoicePaths = new();
    private readonly List<VNBacklogEntry> _backlog = new();

    public event Action OnDialogueEnded;

    /// <summary>Raised while building save data so other systems (player position, etc.) can add their state.</summary>
    public event Action<VNSaveData> OnCapturingSaveData;
    /// <summary>Raised after a save has been applied to the story/stage.</summary>
    public event Action<VNSaveData> OnSaveDataLoaded;

    private EventInstance _activeBgmInstance;
    private string _currentBgmPath = string.Empty;
    private EventInstance _activeLoopSfxInstance;

    private bool _isAutoMode;
    private bool _isPaused;
    private bool _hasStarted;
    private bool _areChoicesVisible;
    private bool _isStepping;
    private bool _autoAdvanceCurrentLine;

    // Denial score
    private int _denialFallback;
    private int _lastNotifiedDenial = int.MinValue;
    private bool _suppressDenialTagThisStep;

    // BGM requested by Ink (before any denial/horror substitution)
    private string _requestedBgmPath = string.Empty;

    // Standalone narration line (not driven by Ink), e.g. the first-entry world intro
    private bool _isNarrationActive;
    private Action _narrationFinished;

    private CancellationTokenSource _modeCts;
    private CancellationTokenSource _stepCts;
    private CancellationTokenSource _bgmTimerCts;
    private CancellationTokenSource _loopSfxTimerCts;

    private string _currentBgId = string.Empty;
    private string _currentCgId = string.Empty;
    private string _lastSpeaker = string.Empty;
    // Set by #speaker:<id> and kept for every following line until #speaker:none / #narrator
    private string _stickySpeaker = string.Empty;
    private string _lastDialogue = string.Empty;

    public Story CurrentStory => _story;
    public string CurrentBgmId => _currentBgmPath;
    public bool IsAutoMode => _isAutoMode;
    public bool IsPaused => _isPaused;
    public bool IsNarrationActive => _isNarrationActive;

    /// <summary>Raised whenever the denial score changes (and once after a story is started or a save is loaded).</summary>
    public event Action<int> OnDenialScoreChanged;

    /// <summary>Optional hook that swaps a requested BGM event for another (used for the high-denial horror music).</summary>
    public Func<string, string> BgmPathResolver { get; set; }

    private bool HasInkDenialVariable =>
        _story != null && !string.IsNullOrWhiteSpace(denialVariableName) &&
        _story.variablesState.GlobalVariableExistsWithName(denialVariableName);

    public int DenialScore
    {
        get
        {
            if (!HasInkDenialVariable) return _denialFallback;
            object value = _story.variablesState[denialVariableName];
            return value switch
            {
                int i => i,
                float f => Mathf.RoundToInt(f),
                _ => _denialFallback
            };
        }
    }

    public void AddDenial(int amount)
    {
        if (amount == 0) return;

        if (HasInkDenialVariable)
            _story.variablesState[denialVariableName] = DenialScore + amount;
        else
            _denialFallback += amount;

        RefreshDenialScore();
    }

    /// <summary>Sets the score directly (writes the Ink variable when declared). Used by the editor test tools.</summary>
    public void SetDenialScore(int value)
    {
        if (HasInkDenialVariable)
            _story.variablesState[denialVariableName] = value;
        else
            _denialFallback = value;

        RefreshDenialScore();
    }

    private void RefreshDenialScore(bool forceNotify = false)
    {
        int score = DenialScore;
        if (!forceNotify && score == _lastNotifiedDenial) return;
        _lastNotifiedDenial = score;
        OnDenialScoreChanged?.Invoke(score);
    }

    private static int SumDenialTags(List<string> tags)
    {
        int total = 0;
        if (tags == null) return total;
        foreach (string raw in tags)
        {
            if (TryParseDenialTag(raw, out int amount))
                total += amount;
        }
        return total;
    }

    // Accepts "denial:1", "denial:+1", "denial:-1", "denial 1"
    private static bool TryParseDenialTag(string rawTag, out int amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(rawTag)) return false;
        string tag = rawTag.Trim();
        if (!tag.StartsWith("denial", StringComparison.OrdinalIgnoreCase) || tag.Length <= 6) return false;

        char delimiter = tag[6];
        if (delimiter != ':' && !char.IsWhiteSpace(delimiter)) return false;

        return int.TryParse(tag.Substring(7).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out amount);
    }

    /// <summary>Re-applies the BGM resolver to the current track (e.g. after the denial state flips).</summary>
    public void RefreshBgm()
    {
        if (!string.IsNullOrEmpty(_requestedBgmPath) && _activeBgmInstance.isValid())
            PlayBGM(_requestedBgmPath);
    }

    private void Awake()
    {
        foreach (var entry in backgrounds)
        {
            if (!string.IsNullOrWhiteSpace(entry.id) && entry.sprite != null)
                _bgLookup[entry.id.Trim()] = entry.sprite;
        }

        foreach (var entry in eventCGs)
        {
            if (!string.IsNullOrWhiteSpace(entry.id) && entry.sprite != null)
                _cgLookup[entry.id.Trim()] = entry.sprite;
        }

        foreach (var entry in fmodSfxEvents)
        {
            if (!string.IsNullOrWhiteSpace(entry.id) && !entry.fmodEvent.IsNull)
                _fmodSfxLookup[entry.id.Trim()] = entry.fmodEvent;
        }

        foreach (var entry in fmodBgmEvents)
        {
            if (!string.IsNullOrWhiteSpace(entry.id) && !entry.fmodEvent.IsNull)
                _fmodBgmLookup[entry.id.Trim()] = entry.fmodEvent;
        }
    }

    private void OnValidate()
    {
        if (!enableAutoplay && _isAutoMode)
            SetAutoMode(false);

        if (stageView != null)
            stageView.SetTopBarFeatureVisibility(enableAutoplay, enableSkip);
    }

    private void OnEnable()
    {
        if (stageView == null)
        {
            Debug.LogError("[VNStoryController] VNStageView reference is missing.");
            return;
        }

        if (VNSettingsManager.Instance != null)
            VNSettingsManager.Instance.ApplyAllFmodBuses();

        stageView.OnViewReady += HandleViewReady;
        stageView.OnAdvanceRequested += HandleAdvanceRequested;
        stageView.OnChoiceSelected += HandleChoiceSelected;
        stageView.OnAutoClicked += ToggleAutoMode;
        stageView.OnSkipClicked += SkipCurrentScene;
        stageView.OnPauseClicked += TogglePause;
        stageView.OnResumeClicked += HandleResumeClicked;
        stageView.OnLineTypingCompleted += HandleLineTypingCompleted;
        stageView.OnSaveClicked += OpenSaveModal;
        stageView.OnLoadClicked += OpenLoadModal;
        stageView.OnSaveSlotSelected += HandleSaveSlotSelected;
        stageView.OnSaveLoadBackClicked += HandleSaveLoadBackClicked;
        stageView.OnQuitClicked += QuitToMainMenu;
        stageView.OnLogClicked += OpenBacklogModal;
        stageView.OnBacklogBackClicked += CloseBacklogModal;
        stageView.SetTopBarFeatureVisibility(enableAutoplay, enableSkip);

        if (advanceInputAction != null && advanceInputAction.action != null)
        {
            advanceInputAction.action.performed += HandleInputActionPerformed;
            advanceInputAction.action.Enable();
        }

        if (toggleAutoInputAction != null && toggleAutoInputAction.action != null)
        {
            toggleAutoInputAction.action.performed += HandleToggleAutoPerformed;
            toggleAutoInputAction.action.Enable();
        }

        if (stageView.IsReady && !_hasStarted)
            HandleViewReady();
    }

    private void OnDisable()
    {
        CancelModeTimer();
        CancelStepTask();
        Time.timeScale = 1f;

        if (stageView != null)
        {
            stageView.OnViewReady -= HandleViewReady;
            stageView.OnAdvanceRequested -= HandleAdvanceRequested;
            stageView.OnChoiceSelected -= HandleChoiceSelected;
            stageView.OnAutoClicked -= ToggleAutoMode;
            stageView.OnSkipClicked -= SkipCurrentScene;
            stageView.OnPauseClicked -= TogglePause;
            stageView.OnResumeClicked -= HandleResumeClicked;
            stageView.OnLineTypingCompleted -= HandleLineTypingCompleted;
            stageView.OnSaveClicked -= OpenSaveModal;
            stageView.OnLoadClicked -= OpenLoadModal;
            stageView.OnSaveSlotSelected -= HandleSaveSlotSelected;
            stageView.OnSaveLoadBackClicked -= HandleSaveLoadBackClicked;
            stageView.OnQuitClicked -= QuitToMainMenu;
            stageView.OnLogClicked -= OpenBacklogModal;
            stageView.OnBacklogBackClicked -= CloseBacklogModal;
        }

        if (advanceInputAction != null && advanceInputAction.action != null)
            advanceInputAction.action.performed -= HandleInputActionPerformed;

        if (toggleAutoInputAction != null && toggleAutoInputAction.action != null)
            toggleAutoInputAction.action.performed -= HandleToggleAutoPerformed;
    }

    private void OnDestroy()
    {
        CancelModeTimer();
        CancelStepTask();
        StopFmodInstance(ref _activeBgmInstance, immediate: true);
        StopFmodInstance(ref _activeLoopSfxInstance, immediate: true);
        SetWorldAmbienceDucked(false);
    }

    private void Update()
    {
        if (stageView == null || !stageView.IsDialogueVisible) return;

        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
            return;
        }

        bool hasAdvanceAction = advanceInputAction != null && advanceInputAction.action != null;
        if (advanceWithSpaceKey && !hasAdvanceAction && !stageView.IsBacklogOpen &&
            Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            HandleAdvanceRequested();
        }
    }

    /// <summary>
    /// Shows a single line in the VN dialogue box without running Ink (no background, no top bar).
    /// Click or Space completes the typing, the next click/Space closes it and invokes <paramref name="onFinished"/>.
    /// The caller is responsible for showing/hiding the stage.
    /// </summary>
    public void PlayNarrationLine(string speaker, string text, Action onFinished)
    {
        CancelStepTask();
        CancelModeTimer();
        SetAutoMode(false);
        _areChoicesVisible = false;
        stageView.HideChoices();
        stageView.SetTopBarVisible(false);

        _isNarrationActive = true;
        _narrationFinished = onFinished;
        stageView.PresentLine(speaker ?? string.Empty, text ?? string.Empty, instant: false);
    }

    private void FinishNarrationLine()
    {
        _isNarrationActive = false;
        stageView.SetTopBarVisible(true);
        stageView.PresentLine(string.Empty, string.Empty, instant: true);

        Action callback = _narrationFinished;
        _narrationFinished = null;
        callback?.Invoke();
    }

    // =========================================================================
    // RUNTIME ASSET REGISTRATION (Used by Testing/Showcase Harness)
    // =========================================================================

    public void RegisterBackground(string id, Sprite sprite)
    {
        if (!string.IsNullOrWhiteSpace(id) && sprite != null)
            _bgLookup[id.Trim()] = sprite;
    }

    public void RegisterEventCG(string id, Sprite sprite)
    {
        if (!string.IsNullOrWhiteSpace(id) && sprite != null)
            _cgLookup[id.Trim()] = sprite;
    }

    private static void CancelTokenSource(ref CancellationTokenSource cts)
    {
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
            cts = null;
        }
    }

    // =========================================================================
    // STORY LOADING, INITIALIZATION & NAVIGATION
    // =========================================================================

    public void EnsureStoryInitialized()
    {
        if (_story != null || inkJsonAsset == null)
            return;

        _story = new Story(inkJsonAsset.text);
        _visitedChoicePaths.Clear();
        _backlog.Clear();
        _denialFallback = 0;

        if (!string.IsNullOrEmpty(VNSessionBridge.CachedInkStateJson))
        {
            _story.state.LoadJson(VNSessionBridge.CachedInkStateJson);
            VNSessionBridge.CachedInkStateJson = string.Empty;
            _denialFallback = VNSessionBridge.CachedDenialScore;
        }

        RefreshDenialScore(forceNotify: true);
    }

    public void CacheInkStateForSceneTransition()
    {
        if (_story != null)
        {
            VNSessionBridge.CachedInkStateJson = _story.state.ToJson();
            VNSessionBridge.CachedDenialScore = _denialFallback;
        }
    }

    public void JumpToKnot(string knotName, bool clearStageFirst = true)
    {
        if (string.IsNullOrWhiteSpace(knotName))
            return;

        EnsureStoryInitialized();
        if (_story == null)
        {
            Debug.LogWarning("[VNStoryController] No Ink JSON asset assigned to jump.");
            return;
        }

        try
        {
            _story.ChoosePathString(knotName);
        }
        catch (StoryException)
        {
            string available = string.Join(", ", _story.mainContentContainer.namedContent.Keys);
            Debug.LogWarning($"[VNStoryController] Knot '{knotName}' not found in compiled JSON. Available knots: [{available}]");
            return;
        }

        CancelStepTask();
        CancelModeTimer();
        SetPaused(false, showPauseMenu: false);
        SetAutoMode(false);
        _areChoicesVisible = false;
        _stickySpeaker = string.Empty; // every conversation starts with the narrator

        if (clearStageFirst && stageView != null)
        {
            stageView.ClearAllCharacters();
            stageView.HideEventCG(instant: true);
            stageView.HideChoices();
        }

        StepStory(instantPresentation: false);
    }

    private void HandleViewReady()
    {
        stageView.SetTopBarFeatureVisibility(enableAutoplay, enableSkip);
        if (_hasStarted) return;
        _hasStarted = true;

        if (deferBootToFlowController || inkJsonAsset == null)
            return;

        if (VNSessionBridge.LaunchMode == VNLaunchMode.NewGame && !startStoryOnBoot)
            EnsureStoryInitialized();

        if (VNSessionBridge.LaunchMode == VNLaunchMode.ContinueLatest && VNSaveManager.Instance != null)
        {
            VNSaveData latest = VNSaveManager.Instance.GetLatestSave();
            if (latest != null)
            {
                stageView.SetDialogueVisible(true);
                LoadFromSaveData(latest);
                return;
            }
        }
        else if (VNSessionBridge.LaunchMode == VNLaunchMode.LoadSpecificSlot && VNSaveManager.Instance != null)
        {
            VNSaveData target = VNSaveManager.Instance.LoadFromSlot(VNSessionBridge.TargetSlotId);
            if (target != null)
            {
                stageView.SetDialogueVisible(true);
                LoadFromSaveData(target);
                return;
            }
        }

        if (startStoryOnBoot)
        {
            stageView.SetDialogueVisible(true);
            LoadStory(inkJsonAsset);
        }
    }

    public void LoadStory(TextAsset compiledInkJson, string startKnot = null)
    {
        if (compiledInkJson == null)
        {
            Debug.LogError("[VNStoryController] Compiled Ink JSON asset is null.");
            return;
        }

        CancelStepTask();
        CancelModeTimer();
        SetPaused(false, showPauseMenu: false);
        SetAutoMode(false);
        _areChoicesVisible = false;
        _visitedChoicePaths.Clear();
        _backlog.Clear();
        _stickySpeaker = string.Empty;
        _denialFallback = 0;
        stageView.HideChoices();

        _story = new Story(compiledInkJson.text);
        RefreshDenialScore(forceNotify: true);

        if (!string.IsNullOrWhiteSpace(startKnot))
            _story.ChoosePathString(startKnot);

        StepStory(instantPresentation: false);
    }

    // =========================================================================
    // CORE INK EXECUTION LOOP
    // =========================================================================

    private void StepStory(bool instantPresentation)
    {
        if (_story == null) return;

        CancelModeTimer();
        CancelStepTask();
        _stepCts = new CancellationTokenSource();
        _ = StepStoryAsync(instantPresentation, _stepCts.Token);
    }

    private async Awaitable StepStoryAsync(bool instantPresentation, CancellationToken ct)
    {
        _isStepping = true;

        try
        {
            while (_story.canContinue)
            {
                ct.ThrowIfCancellationRequested();

                string rawLine = _story.Continue();
                List<string> tags = new List<string>(_story.currentTags);
                _autoAdvanceCurrentLine = false;

                string trimmedLine = rawLine.Trim();
                bool isContinueToken = trimmedLine.Equals("[continue]", StringComparison.OrdinalIgnoreCase);

                // Clear the dialogue box if pre-line #wait tags or [continue] are about to execute
                if (!instantPresentation && (HasWaitTag(tags) || isContinueToken))
                    stageView.PresentLine(string.Empty, string.Empty, instant: true);

                string speakerOverride = await ProcessTagsAsync(tags, instantPresentation, ct);

                if (string.IsNullOrEmpty(trimmedLine) || isContinueToken)
                    continue;

                ExtractSpeakerAndText(trimmedLine, speakerOverride, out string speaker, out string dialogue);
                if (dialogue.Equals("[blank]", StringComparison.OrdinalIgnoreCase))
                    dialogue = string.Empty;

                // If #continue was tagged onto a [blank] line, advance immediately
                if (_autoAdvanceCurrentLine && string.IsNullOrEmpty(dialogue))
                    continue;

                _areChoicesVisible = false;
                RecordBacklogEntry(speaker, dialogue, isChoice: false);
                _lastSpeaker = speaker;
                _lastDialogue = dialogue;
                _isStepping = false;
                _suppressDenialTagThisStep = false;
                RefreshDenialScore(); // catches "~ denial += 1" written directly in Ink
                stageView.PresentLine(speaker, dialogue, instant: instantPresentation);
                return;
            }

            _isStepping = false;
            _suppressDenialTagThisStep = false;
            RefreshDenialScore();

            if (_story.currentChoices.Count > 0)
            {
                PresentCurrentChoices();
            }
            else
            {
                _areChoicesVisible = false;
                SetAutoMode(false);
                OnDialogueEnded?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            _isStepping = false;
        }
    }

    private void PresentCurrentChoices()
    {
        _areChoicesVisible = true;
        var options = new List<VNChoiceOption>(_story.currentChoices.Count);


        foreach (Choice choice in _story.currentChoices)
        {
            bool visited = _visitedChoicePaths.Contains(choice.pathStringOnChoice);
            string cleanText = System.Text.RegularExpressions.Regex.Replace(
    choice.text.Trim(), @"\s*\[[\+\-]?\d+\]\s*$", string.Empty);
            options.Add(new VNChoiceOption(cleanText, visited));
        }

        stageView.ShowChoices(options);
    }

    // =========================================================================
    // INK TAG PARSER (#bg:, #cg:, #char:, #hide:, #speaker:, #bgm:, #sfx:, #wait)
    // =========================================================================

    private async Awaitable<string> ProcessTagsAsync(List<string> tags, bool instant, CancellationToken ct)
    {
        string speakerOverride = null;
        if (tags == null || tags.Count == 0) return null;

        for (int i = 0; i < tags.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            string rawTag = tags[i]?.Trim();
            if (string.IsNullOrEmpty(rawTag))
                continue;

            // Support both "#wait 2" and "#wait:2"
            if (TryParseWaitSeconds(rawTag, out float waitSeconds))
            {
                if (!instant && waitSeconds > 0f)
                    await WaitScaledSecondsAsync(waitSeconds, ct);
                continue;
            }

            ApplyTagImmediate(rawTag, instant, ref speakerOverride);
        }

        return speakerOverride;
    }

    private void ApplyTagImmediate(string rawTag, bool instant, ref string speakerOverride)
    {
        // #denial:N — scored even when skipping (instant). Ignored once if the same tag was
        // already counted from the chosen choice's own tags, so it never counts twice.
        if (TryParseDenialTag(rawTag, out int denialAmount))
        {
            if (_suppressDenialTagThisStep)
                _suppressDenialTagThisStep = false;
            else
                AddDenial(denialAmount);
            return;
        }

        // Split ONLY on the first colon so FMOD paths like "event:/SFX/PhoneRing" stay intact
        int colonIdx = rawTag.IndexOf(':');
        string command = colonIdx >= 0
            ? rawTag.Substring(0, colonIdx).Trim().ToLowerInvariant()
            : rawTag.ToLowerInvariant();
        string payload = colonIdx >= 0
            ? rawTag.Substring(colonIdx + 1).Trim()
            : string.Empty;

        switch (command)
        {
            case "bg" when !string.IsNullOrEmpty(payload):
                {
                    SplitOptionalSuffix(payload, out string bgId, out string suffix);
                    bool forceInstant = instant || suffix.Equals("instant", StringComparison.OrdinalIgnoreCase);
                    _currentBgId = bgId.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : bgId;

                    if (bgId.Equals("none", StringComparison.OrdinalIgnoreCase))
                        stageView.SetBackground(null, forceInstant);
                    else if (_bgLookup.TryGetValue(bgId, out Sprite sprite))
                        stageView.SetBackground(sprite, forceInstant);
                    else
                        Debug.LogWarning($"[VNStoryController] Background ID not found: '{bgId}'");
                    break;
                }

            case "cg" when !string.IsNullOrEmpty(payload):
                {
                    SplitOptionalSuffix(payload, out string cgId, out string suffix);
                    bool forceInstant = instant || suffix.Equals("instant", StringComparison.OrdinalIgnoreCase);
                    bool hideCg = cgId.Equals("hide", StringComparison.OrdinalIgnoreCase) || cgId.Equals("none", StringComparison.OrdinalIgnoreCase);
                    _currentCgId = hideCg ? string.Empty : cgId;

                    if (hideCg)
                        stageView.HideEventCG(forceInstant);
                    else if (_cgLookup.TryGetValue(cgId, out Sprite sprite))
                        stageView.ShowEventCG(sprite, forceInstant);
                    else
                        Debug.LogWarning($"[VNStoryController] Event CG ID not found: '{cgId}'");
                    break;
                }

            case "char" when !string.IsNullOrEmpty(payload):
                {
                    string[] charParts = payload.Split(':');
                    string charId = charParts[0].Trim();
                    string expression = charParts.Length >= 2 && !string.IsNullOrWhiteSpace(charParts[1])
                        ? charParts[1].Trim()
                        : "neutral";

                    StageSlot slot = StageSlot.Auto;
                    if (charParts.Length >= 3)
                    {
                        slot = charParts[2].Trim().ToLowerInvariant() switch
                        {
                            "left" => StageSlot.Left,
                            "center" => StageSlot.Center,
                            "right" => StageSlot.Right,
                            _ => StageSlot.Auto
                        };
                    }

                    stageView.ShowCharacter(charId, expression, slot);
                    break;
                }

            case "hide" when !string.IsNullOrEmpty(payload):
                {
                    if (payload.Equals("all", StringComparison.OrdinalIgnoreCase))
                        stageView.ClearAllCharacters();
                    else
                        stageView.HideCharacter(payload);
                    break;
                }

            case "speaker" when !string.IsNullOrEmpty(payload):
                {
                    // Sticky: applies to this line and every following line until changed.
                    bool toNarrator = payload.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                                      payload.Equals("narrator", StringComparison.OrdinalIgnoreCase);
                    _stickySpeaker = toNarrator ? string.Empty : payload;
                    break;
                }

            case "narrator":
                {
                    _stickySpeaker = string.Empty;
                    break;
                }

            case "as" when !string.IsNullOrEmpty(payload):
                {
                    // One-line override that leaves the sticky speaker alone: #as:khadija  /  #as:none
                    speakerOverride = payload;
                    break;
                }

            case "sfx" when !string.IsNullOrEmpty(payload):
                {
                    if (!instant)
                        PlaySFX(payload);
                    break;
                }

            case "sfx_loop" when !string.IsNullOrEmpty(payload):
                {
                    if (!instant)
                    {
                        SplitOptionalDuration(payload, out string eventPath, out float duration);
                        StartLoopingSFX(eventPath, duration);
                    }
                    break;
                }

            case "sfx_param" when !string.IsNullOrEmpty(payload):
                {
                    // Format: #sfx_param:DistortionLevel:0.70
                    int paramColon = payload.IndexOf(':');
                    if (paramColon > 0 &&
                        float.TryParse(payload.Substring(paramColon + 1).Trim(),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out float paramVal))
                    {
                        SetLoopingSfxParameter(payload.Substring(0, paramColon).Trim(), paramVal);
                    }
                    break;
                }

            case "sfx_stop":
                {
                    StopLoopingSFX(instant);
                    break;
                }

            case "bgm" when !string.IsNullOrEmpty(payload):
            case "ambience" when !string.IsNullOrEmpty(payload):
                {
                    if (payload.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                        payload.Equals("stop", StringComparison.OrdinalIgnoreCase))
                    {
                        StopBGM(instant);
                    }
                    else
                    {
                        SplitOptionalDuration(payload, out string eventPath, out float duration);
                        PlayBGM(eventPath, instant, duration);
                    }
                    break;
                }

            case "bgm_stop":
                {
                    StopBGM(instant);
                    break;
                }

            case "clear":
                {
                    stageView.ClearAllCharacters();
                    stageView.HideEventCG(instant);
                    _currentCgId = string.Empty;
                    break;
                }

            case "continue":
                {
                    _autoAdvanceCurrentLine = true;
                    break;
                }

            case "glitch":
                {
                    if (!instant)
                    {
                        float dur = 0.35f;
                        if (!string.IsNullOrEmpty(payload))
                            float.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out dur);
                        stageView.TriggerGlitch(dur);
                    }
                    break;
                }
        }
    }

    private static bool TryParseWaitSeconds(string rawTag, out float seconds)
    {
        seconds = 0f;
        if (!rawTag.StartsWith("wait", StringComparison.OrdinalIgnoreCase) || rawTag.Length <= 4)
            return false;

        char delimiter = rawTag[4];
        if (delimiter != ':' && !char.IsWhiteSpace(delimiter))
            return false;

        string numPart = rawTag.Substring(5).Trim();
        return float.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
    }

    private static bool HasWaitTag(List<string> tags)
    {
        if (tags == null) return false;
        for (int i = 0; i < tags.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(tags[i]) && TryParseWaitSeconds(tags[i].Trim(), out _))
                return true;
        }
        return false;
    }

    private static void SplitOptionalSuffix(string payload, out string primary, out string suffix)
    {
        int idx = payload.LastIndexOf(':');
        if (idx > 0)
        {
            primary = payload.Substring(0, idx).Trim();
            suffix = payload.Substring(idx + 1).Trim();
        }
        else
        {
            primary = payload;
            suffix = string.Empty;
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

    // Speaker priority: #as one-line tag  >  inline "Name: text" prefix  >  sticky #speaker  >  narrator (no name)
    private void ExtractSpeakerAndText(string line, string tagOverride, out string speaker, out string text)
    {
        if (!string.IsNullOrEmpty(tagOverride))
        {
            speaker = tagOverride.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : tagOverride;
            text = line;
            return;
        }

        int colonIndex = line.IndexOf(':');
        if (colonIndex > 0 && colonIndex <= 28 && line[0] != '<')
        {
            string candidate = line.Substring(0, colonIndex).Trim();
            if (!candidate.Contains(' ') && !candidate.Contains('.') && !candidate.Contains(','))
            {
                speaker = candidate;
                text = line.Substring(colonIndex + 1).TrimStart();
                return;
            }
        }

        speaker = _stickySpeaker;
        text = line;
    }

    // =========================================================================
    // INPUT, PAUSE, AUTOPLAY & SKIP SCENE
    // =========================================================================

    private void HandleInputActionPerformed(InputAction.CallbackContext ctx) => HandleAdvanceRequested();
    private void HandleToggleAutoPerformed(InputAction.CallbackContext ctx) => ToggleAutoMode();
    private void HandleResumeClicked() => SetPaused(false);
    private void HandleSaveLoadBackClicked() => stageView.ShowPauseMenu(true);

    private void HandleAdvanceRequested()
    {
        if (_isNarrationActive)
        {
            if (_isPaused || !stageView.IsDialogueVisible) return;

            if (stageView.IsTyping)
                stageView.CompleteTyping();
            else
                FinishNarrationLine();
            return;
        }

        if (_story == null || _isPaused || _isStepping || !stageView.IsDialogueVisible) return;

        if (_isAutoMode && cancelAutoOnManualClick)
            SetAutoMode(false);

        if (stageView.IsTyping)
        {
            stageView.CompleteTyping();
            return;
        }

        if (_areChoicesVisible)
            return;

        if (_story.canContinue || _story.currentChoices.Count > 0)
            StepStory(instantPresentation: false);
    }

    private void HandleChoiceSelected(int choiceIndex)
    {
        if (_story == null || _isPaused || choiceIndex < 0 || choiceIndex >= _story.currentChoices.Count)
            return;

        Choice chosen = _story.currentChoices[choiceIndex];
        string chosenText = chosen.text.Trim();

        if (!string.IsNullOrEmpty(chosen.pathStringOnChoice))
            _visitedChoicePaths.Add(chosen.pathStringOnChoice);

        RecordBacklogEntry("CHOICE", chosenText, isChoice: true);

        // "+ [It's okay...] #denial:1" — depending on where Ink attaches the tag it shows up on the
        // choice itself and/or on the line after it; count it here and skip the duplicate.
        int choiceDenial = SumDenialTags(chosen.tags);
        _suppressDenialTagThisStep = choiceDenial != 0;
        if (choiceDenial != 0)
            AddDenial(choiceDenial);

        _areChoicesVisible = false;
        _story.ChooseChoiceIndex(choiceIndex);

        StepStory(instantPresentation: false);
        SaveCheckpoint();
    }

    public void SaveCheckpoint()
    {
        if (_story == null || VNSaveManager.Instance == null)
            return;

        VNSaveData data = CaptureSaveData("Checkpoint");
        VNSaveManager.Instance.SaveToSlot(VNSaveManager.AutosaveSlotId, data);
    }

    public VNSaveData CaptureSaveData(string label)
    {
        EnsureStoryInitialized();
        if (_story == null)
            return null;

        var data = new VNSaveData
        {
            backlog = new List<VNBacklogEntry>(_backlog),
            label = label,
            inkStateJson = _story.state.ToJson(),
            visitedChoicePaths = new List<string>(_visitedChoicePaths),
            speaker = _lastSpeaker,
            stickySpeaker = _stickySpeaker,
            dialogue = _lastDialogue,
            isWaitingForChoice = _areChoicesVisible,
            backgroundId = _currentBgId,
            eventCgId = _currentCgId,
            bgmId = _requestedBgmPath, // the Ink-requested track; the horror swap is re-applied on load
            denialScore = DenialScore,
            activeSpeakerId = stageView.GetActiveSpeakerId(),
            characters = stageView.CaptureCharacterStates(),
            // A narration line is shown over the world, so a save taken during it is an exploration save.
            inWorld = !stageView.IsDialogueVisible || _isNarrationActive
        };

        OnCapturingSaveData?.Invoke(data);
        return data;
    }

    /// <summary>
    /// Loads a slot mid-game. When a scene flow controller exists the scene is reloaded behind a fade,
    /// so the boot sequence restores VN/world state, camera and player position in one consistent path.
    /// </summary>
    public void RequestLoadSlot(string slotId)
    {
        VNSceneFlowController flow = FindFirstObjectByType<VNSceneFlowController>();
        if (flow != null)
        {
            flow.LoadSlotAndReloadScene(slotId);
            return;
        }

        VNSaveData data = VNSaveManager.Instance != null ? VNSaveManager.Instance.LoadFromSlot(slotId) : null;
        if (data == null) return;

        bool isMidVn = !data.inWorld && (data.isWaitingForChoice || !string.IsNullOrEmpty(data.dialogue));
        stageView.SetDialogueVisible(isMidVn);
        LoadFromSaveData(data);
    }

    public void LoadFromSaveData(VNSaveData data)
    {
        if (data == null || inkJsonAsset == null)
            return;

        CancelStepTask();
        CancelModeTimer();
        SetPaused(false, showPauseMenu: false);
        SetAutoMode(false);

        _story = new Story(inkJsonAsset.text);
        _story.state.LoadJson(data.inkStateJson);
        _denialFallback = data.denialScore; // ignored when Ink declares the variable (its value lives in inkStateJson)
        RefreshDenialScore(forceNotify: true);

        _visitedChoicePaths.Clear();
        if (data.visitedChoicePaths != null)
        {
            foreach (string path in data.visitedChoicePaths)
                _visitedChoicePaths.Add(path);
        }

        // 1. Restore Background & Event CG instantly
        _currentBgId = data.backgroundId ?? string.Empty;
        if (!string.IsNullOrEmpty(_currentBgId) && _bgLookup.TryGetValue(_currentBgId, out Sprite bgSprite))
            stageView.SetBackground(bgSprite, instant: true);
        else
            stageView.SetBackground(null, instant: true);

        _currentCgId = data.eventCgId ?? string.Empty;
        if (!string.IsNullOrEmpty(_currentCgId) && _cgLookup.TryGetValue(_currentCgId, out Sprite cgSprite))
            stageView.ShowEventCG(cgSprite, instant: true);
        else
            stageView.HideEventCG(instant: true);

        // 2. Restore FMOD BGM & clear any stale looping SFX
        StopLoopingSFX(instant: true);
        if (!string.IsNullOrEmpty(data.bgmId))
            PlayBGM(data.bgmId, instant: true);
        else
            StopBGM(instant: true);

        // 3. Restore Characters & Speaker Highlight
        stageView.RestoreCharacterStates(data.characters, data.activeSpeakerId);

        // 4. Restore Dialogue & Pending Choices
        _lastSpeaker = data.speaker ?? string.Empty;
        _stickySpeaker = data.stickySpeaker ?? string.Empty;
        _lastDialogue = data.dialogue ?? string.Empty;
        stageView.HideChoices();
        _backlog.Clear();
        if (data.backlog != null)
            _backlog.AddRange(data.backlog);

        if (data.inWorld)
        {
            // Saved while exploring: nothing to present, the stage stays hidden.
            _areChoicesVisible = false;
            stageView.PresentLine(string.Empty, string.Empty, instant: true);
        }
        else if (data.isWaitingForChoice && _story.currentChoices.Count > 0)
        {
            stageView.PresentLine(_lastSpeaker, _lastDialogue, instant: true);
            PresentCurrentChoices();
        }
        else
        {
            _areChoicesVisible = false;
            stageView.PresentLine(_lastSpeaker, _lastDialogue, instant: false);
        }

        OnSaveDataLoaded?.Invoke(data);
    }

    private void RecordBacklogEntry(string speaker, string text, bool isChoice)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (_backlog.Count >= maxBacklogEntries)
            _backlog.RemoveAt(0);

        _backlog.Add(new VNBacklogEntry
        {
            speaker = speaker ?? string.Empty,
            text = text,
            isChoice = isChoice
        });
    }

    private void OpenSaveModal()
    {
        if (VNSaveManager.Instance == null) return;
        var slots = VNSaveManager.Instance.GetAllSlotSummaries(includeAutosave: false);
        stageView.ShowSaveLoadModal(isSaveMode: true, slots);
    }

    private void OpenLoadModal()
    {
        if (VNSaveManager.Instance == null) return;
        var slots = VNSaveManager.Instance.GetAllSlotSummaries(includeAutosave: true);
        stageView.ShowSaveLoadModal(isSaveMode: false, slots);
    }

    private void HandleSaveSlotSelected(bool isSaveMode, string slotId)
    {
        if (VNSaveManager.Instance == null) return;

        if (isSaveMode)
        {
            VNSaveData data = CaptureSaveData(slotId.ToUpperInvariant());
            VNSaveManager.Instance.SaveToSlot(slotId, data);
            OpenSaveModal();
        }
        else
        {
            RequestLoadSlot(slotId);
        }
    }

    private async void QuitToMainMenu()
    {
        CancelModeTimer();
        CancelStepTask();
        StopFmodInstance(ref _activeBgmInstance, immediate: true);
        StopFmodInstance(ref _activeLoopSfxInstance, immediate: true);
        Time.timeScale = 1f;
        VNSessionBridge.LaunchMode = VNLaunchMode.NewGame;
        await SceneManager.LoadSceneAsync(mainMenuSceneName);
    }

    public void ToggleAutoMode()
    {
        if (!enableAutoplay) return;
        SetAutoMode(!_isAutoMode);
    }

    public void SetAutoMode(bool enabled)
    {
        if (!enableAutoplay)
            enabled = false;

        if (_isAutoMode == enabled) return;
        _isAutoMode = enabled;
        stageView.SetAutoState(_isAutoMode);

        if (_isAutoMode)
        {
            if (!_isPaused && !_isStepping && !stageView.IsTyping && _story != null && !_areChoicesVisible)
                StartDelayedAdvanceAsync(autoAdvanceDelay);
        }
        else
        {
            CancelModeTimer();
        }
    }

    public void TogglePause() => SetPaused(!_isPaused, showPauseMenu: true);

    public void SetPaused(bool paused, bool showPauseMenu = true)
    {
        if (_isPaused == paused)
        {
            if (!paused)
                stageView.ShowPauseMenu(false);
            return;
        }

        _isPaused = paused;

        if (_isPaused)
        {
            CancelModeTimer();
            Time.timeScale = 0f;
            if (showPauseMenu)
                stageView.ShowPauseMenu(true);
        }
        else
        {
            Time.timeScale = 1f;
            stageView.ShowPauseMenu(false);

            if (_isAutoMode && !_isStepping && !stageView.IsTyping && _story != null && !_areChoicesVisible)
                StartDelayedAdvanceAsync(autoAdvanceDelay);
        }

        if (pauseAudioOnPauseMenu && RuntimeManager.IsInitialized)
        {
            try
            {
                RuntimeManager.GetBus("bus:/BGM").setPaused(paused);
                RuntimeManager.GetBus("bus:/SFX").setPaused(paused);
            }
            catch (BusNotFoundException)
            {
            }
        }
    }

    private void OpenBacklogModal()
    {
        if (_story == null) return;
        SetPaused(true, showPauseMenu: false);
        stageView.ShowBacklogModal(_backlog);
    }

    private void CloseBacklogModal()
    {
        SetPaused(false);
    }

    public void SkipCurrentScene()
    {
        if (!enableSkip || _story == null || _isPaused || _areChoicesVisible)
            return;

        SetAutoMode(false);
        CancelModeTimer();
        CancelStepTask();

        if (stageView.IsTyping)
            stageView.CompleteTyping();

        string startingKnot = GetCurrentKnotName();
        string pendingSpeaker = null;
        string pendingDialogue = null;

        while (_story.canContinue)
        {
            string rawLine = _story.Continue();
            bool hasSceneBreakTag = HasSceneBreakTag(_story.currentTags);
            string speakerOverride = null;


            foreach (string rawTag in _story.currentTags)
            {
                if (!string.IsNullOrWhiteSpace(rawTag))
                    ApplyTagImmediate(rawTag.Trim(), instant: true, ref speakerOverride);
            }

            string trimmedLine = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmedLine) ||
                trimmedLine.Equals("[continue]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ExtractSpeakerAndText(trimmedLine, speakerOverride, out string speaker, out string dialogue);
            if (dialogue.Equals("[blank]", StringComparison.OrdinalIgnoreCase))
                dialogue = string.Empty;
            RecordBacklogEntry(speaker, dialogue, isChoice: false);
            _lastSpeaker = speaker;
            _lastDialogue = dialogue;
            pendingSpeaker = speaker;
            pendingDialogue = dialogue;

            string currentKnot = GetCurrentKnotName();
            bool enteredNewKnot = !string.IsNullOrEmpty(currentKnot) &&
                                  !string.Equals(currentKnot, startingKnot, StringComparison.Ordinal);

            if (enteredNewKnot || hasSceneBreakTag)
            {
                _areChoicesVisible = false;
                stageView.PresentLine(pendingSpeaker, pendingDialogue, instant: false);
                return;
            }
        }

        if (_story.currentChoices.Count > 0)
        {
            if (!string.IsNullOrEmpty(pendingDialogue))
                stageView.PresentLine(pendingSpeaker, pendingDialogue, instant: true);

            PresentCurrentChoices();
        }
        else
        {
            _areChoicesVisible = false;
            SetAutoMode(false);
            OnDialogueEnded?.Invoke();
        }
    }

    private string GetCurrentKnotName()
    {
        string path = _story?.state?.currentPathString;
        if (string.IsNullOrEmpty(path)) return string.Empty;

        int dotIndex = path.IndexOf('.');
        return dotIndex > 0 ? path.Substring(0, dotIndex) : path;
    }

    private static bool HasSceneBreakTag(List<string> tags)
    {
        if (tags == null) return false;
        foreach (string tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            int colonIdx = tag.IndexOf(':');
            string cmd = (colonIdx >= 0 ? tag.Substring(0, colonIdx) : tag).Trim().ToLowerInvariant();
            if (cmd == "scene" || cmd == "bg")
                return true;
        }
        return false;
    }

    private void HandleLineTypingCompleted()
    {
        if (_story == null || _isPaused || _isStepping || _areChoicesVisible) return;

        if (_autoAdvanceCurrentLine)
        {
            _autoAdvanceCurrentLine = false;
            if (_story.canContinue || _story.currentChoices.Count > 0)
                StepStory(instantPresentation: false);
            return;
        }

        if (!_story.canContinue && _story.currentChoices.Count > 0)
        {
            if (choiceRevealDelay <= 0f)
                StepStory(instantPresentation: false);
            else
                StartDelayedAdvanceAsync(choiceRevealDelay);

            return;
        }

        if (_isAutoMode)
            StartDelayedAdvanceAsync(autoAdvanceDelay);
    }

    private async void StartDelayedAdvanceAsync(float delay)
    {
        CancelModeTimer();
        _modeCts = new CancellationTokenSource();
        CancellationToken token = _modeCts.Token;

        try
        {
            await Awaitable.WaitForSecondsAsync(delay, token);
            if (token.IsCancellationRequested || _isPaused || _isStepping) return;

            if (_story != null && !_areChoicesVisible && (_story.canContinue || _story.currentChoices.Count > 0))
                StepStory(instantPresentation: false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelModeTimer()
    {
        if (_modeCts != null)
        {
            _modeCts.Cancel();
            _modeCts.Dispose();
            _modeCts = null;
        }
    }

    private void CancelStepTask()
    {
        _isStepping = false;
        if (_stepCts != null)
        {
            _stepCts.Cancel();
            _stepCts.Dispose();
            _stepCts = null;
        }
    }

    // =========================================================================
    // FMOD STUDIO AUDIO PLAYBACK
    // =========================================================================

    public void PlaySFX(string idOrPath)
    {
        if (string.IsNullOrWhiteSpace(idOrPath) || !RuntimeManager.IsInitialized)
            return;

        string key = idOrPath.Trim();
        bool played = _fmodSfxLookup.TryGetValue(key, out EventReference mapped)
            ? FMODAudioManager.PlayOneShot(mapped, key)
            : FMODAudioManager.PlayOneShot(key);

        if (!played)
            Debug.LogWarning($"[VNStoryController] FMOD SFX not found (registry, alias or path): '{key}'");
    }

    // Registry id (Inspector EventReference) first, then FMODEvents alias / event:/ path via FMODAudioManager.
    private static bool TryCreateVnEvent(string idOrPath, Dictionary<string, EventReference> registry, out EventInstance instance)
    {
        string key = idOrPath.Trim();
        if (registry.TryGetValue(key, out EventReference mapped) && FMODAudioManager.TryCreateInstance(mapped, out instance))
            return true;

        return FMODAudioManager.TryCreateInstance(key, out instance);
    }

    // While VN music plays, the 2.5D scene ambience managed by FMODAudioManager steps aside.
    private static void SetWorldAmbienceDucked(bool ducked)
    {
        if (Application.isPlaying)
            FMODAudioManager.Instance?.SetVNBgmOverrideActive(ducked);
    }

    private static void SplitOptionalDuration(string payload, out string eventPath, out float durationSeconds)
    {
        durationSeconds = 0f;
        int lastColon = payload.LastIndexOf(':');
        if (lastColon > 0 &&
            float.TryParse(payload.Substring(lastColon + 1).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
            parsed > 0f)
        {
            eventPath = payload.Substring(0, lastColon).Trim();
            durationSeconds = parsed;
            return;
        }

        eventPath = payload;
    }



    public void PlayBGM(string eventPath, bool instant = false, float autoStopAfterSeconds = 0f)
    {
        if (string.IsNullOrWhiteSpace(eventPath) || !RuntimeManager.IsInitialized)
            return;

        _requestedBgmPath = eventPath;
        if (BgmPathResolver != null)
        {
            string resolved = BgmPathResolver(eventPath);
            if (!string.IsNullOrWhiteSpace(resolved))
                eventPath = resolved;
        }

        if (string.Equals(_currentBgmPath, eventPath, StringComparison.OrdinalIgnoreCase) &&
            _activeBgmInstance.isValid())
        {
            return;
        }

        StopFmodInstance(ref _activeBgmInstance, immediate: instant);
        CancelTokenSource(ref _bgmTimerCts);

        if (!TryCreateVnEvent(eventPath, _fmodBgmLookup, out _activeBgmInstance))
        {
            _currentBgmPath = string.Empty;
            Debug.LogWarning($"[VNStoryController] FMOD BGM not found (registry, alias or path): '{eventPath}'");
            return;
        }

        _currentBgmPath = eventPath;
        _activeBgmInstance.start();
        SetWorldAmbienceDucked(true);

        if (autoStopAfterSeconds > 0f)
        {
            _bgmTimerCts = new CancellationTokenSource();
            _ = AutoStopBgmAfterAsync(autoStopAfterSeconds, _bgmTimerCts.Token);
        }
    }

    public void StopBGM(bool instant = false)
    {
        CancelTokenSource(ref _bgmTimerCts);
        _currentBgmPath = string.Empty;
        _requestedBgmPath = string.Empty;
        StopFmodInstance(ref _activeBgmInstance, immediate: instant);
        SetWorldAmbienceDucked(false);
    }

    public void StartLoopingSFX(string eventPath, float autoStopAfterSeconds = 0f)
    {
        if (string.IsNullOrWhiteSpace(eventPath) || !RuntimeManager.IsInitialized)
            return;

        StopFmodInstance(ref _activeLoopSfxInstance, immediate: true);
        CancelTokenSource(ref _loopSfxTimerCts);

        if (!TryCreateVnEvent(eventPath, _fmodSfxLookup, out _activeLoopSfxInstance))
        {
            Debug.LogWarning($"[VNStoryController] FMOD looping SFX not found (registry, alias or path): '{eventPath}'");
            return;
        }

        _activeLoopSfxInstance.start();

        if (autoStopAfterSeconds > 0f)
        {
            _loopSfxTimerCts = new CancellationTokenSource();
            _ = AutoStopLoopSfxAfterAsync(autoStopAfterSeconds, _loopSfxTimerCts.Token);
        }
    }


    public void SetLoopingSfxParameter(string paramName, float value)
    {
        if (_activeLoopSfxInstance.isValid())
            _activeLoopSfxInstance.setParameterByName(paramName, value);
    }

    public void StopLoopingSFX(bool instant = false)
    {
        CancelTokenSource(ref _loopSfxTimerCts);
        StopFmodInstance(ref _activeLoopSfxInstance, immediate: instant);
    }

    private async Awaitable AutoStopBgmAfterAsync(float seconds, CancellationToken ct)
    {
        try
        {
            await WaitScaledSecondsAsync(seconds, ct);
            if (!ct.IsCancellationRequested)
                StopBGM(instant: false);
        }
        catch (OperationCanceledException) { }
    }

    private async Awaitable AutoStopLoopSfxAfterAsync(float seconds, CancellationToken ct)
    {
        try
        {
            await WaitScaledSecondsAsync(seconds, ct);
            if (!ct.IsCancellationRequested)
                StopLoopingSFX(instant: false);
        }
        catch (OperationCanceledException) { }
    }

    private static void StopFmodInstance(ref EventInstance instance, bool immediate)
    {
        if (!instance.isValid())
            return;

        instance.stop(immediate ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        instance.release();
        instance.clearHandle();
    }
}