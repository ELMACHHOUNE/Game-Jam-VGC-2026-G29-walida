using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using Ink.Runtime;
using LitMotion;

public class VNStoryController : MonoBehaviour
{
    [Serializable]
    public struct NamedSprite
    {
        public string id;
        public Sprite sprite;
    }

    [Serializable]
    public struct NamedAudio
    {
        public string id;
        public AudioClip clip;
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

    [Header("Audio Registries")]
    [SerializeField] private List<NamedAudio> sfxClips = new();
    [SerializeField] private List<NamedAudio> bgmTracks = new();

    [Header("Audio Channels & Timings")]
    [SerializeField] private AudioSource sfxAudioSource;
    [SerializeField] private AudioSource bgmSourceA;
    [SerializeField] private AudioSource bgmSourceB;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] private float bgmVolume = 0.55f;
    [SerializeField] private float bgmFadeDuration = 0.8f;
    [SerializeField] private bool pauseAudioOnPauseMenu = true;
    public string CurrentBgmId => _currentBgmId;

    [Header("Optional Keyboard/Gamepad Input")]
    [SerializeField] private InputActionReference advanceInputAction;
    [SerializeField] private InputActionReference toggleAutoInputAction;

    [Header("Exploration Integration")]
    [SerializeField] private bool startStoryOnBoot = false;

    private Story _story;
    private readonly Dictionary<string, Sprite> _bgLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sprite> _cgLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visitedChoicePaths = new();
    private readonly Dictionary<string, AudioClip> _sfxLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioClip> _bgmLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<VNBacklogEntry> _backlog = new();
    public event Action OnDialogueEnded;


    private AudioSource _activeBgmSource;
    private string _currentBgmId = string.Empty;
    private MotionHandle _bgmFadeTween;
    private bool _isAutoMode;
    private bool _isPaused;
    private bool _hasStarted;
    private bool _areChoicesVisible;
    private CancellationTokenSource _modeCts;
    private string _currentBgId = string.Empty;
    private string _currentCgId = string.Empty;
    private string _lastSpeaker = string.Empty;
    private string _lastDialogue = string.Empty;

    public Story CurrentStory => _story;
    public bool IsAutoMode => _isAutoMode;
    public bool IsPaused => _isPaused;

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
        foreach (var entry in sfxClips)
        {
            if (!string.IsNullOrWhiteSpace(entry.id) && entry.clip != null)
                _sfxLookup[entry.id.Trim()] = entry.clip;
        }

        foreach (var entry in bgmTracks)
        {
            if (!string.IsNullOrWhiteSpace(entry.id) && entry.clip != null)
                _bgmLookup[entry.id.Trim()] = entry.clip;
        }

        if (sfxAudioSource == null)
            sfxAudioSource = CreateChildAudioSource("VN_SFX_Channel", loop: false);

        if (bgmSourceA == null)
            bgmSourceA = CreateChildAudioSource("VN_BGM_Channel_A", loop: true);

        if (bgmSourceB == null)
            bgmSourceB = CreateChildAudioSource("VN_BGM_Channel_B", loop: true);

        _activeBgmSource = bgmSourceA;
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
            VNSettingsManager.Instance.OnSettingsChanged += ApplyAudioSettings;
        ApplyAudioSettings();

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
        stageView.OnSaveLoadBackClicked += () => stageView.ShowPauseMenu(true);
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
        Time.timeScale = 1f;

        // Inside OnDisable():
        if (VNSettingsManager.Instance != null)
            VNSettingsManager.Instance.OnSettingsChanged -= ApplyAudioSettings;

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
            stageView.OnQuitClicked -= QuitToMainMenu;
            stageView.OnLogClicked -= OpenBacklogModal;
            stageView.OnBacklogBackClicked -= CloseBacklogModal;
        }

        if (advanceInputAction != null && advanceInputAction.action != null)
            advanceInputAction.action.performed -= HandleInputActionPerformed;

        if (toggleAutoInputAction != null && toggleAutoInputAction.action != null)
            toggleAutoInputAction.action.performed -= HandleToggleAutoPerformed;
    }

    private void Update()
    {
        if (!stageView.IsDialogueVisible) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            TogglePause();
    }

    private void ApplyAudioSettings()
    {
        if (VNSettingsManager.Instance == null) return;

        bgmVolume = VNSettingsManager.Instance.BgmVolume;
        sfxVolume = VNSettingsManager.Instance.SfxVolume;

        if (_activeBgmSource != null && _activeBgmSource.isPlaying && !_bgmFadeTween.IsActive())
            _activeBgmSource.volume = bgmVolume;
    }

    private void HandleViewReady()
    {
        stageView.SetTopBarFeatureVisibility(enableAutoplay, enableSkip);
        if (_hasStarted) return;
        _hasStarted = true;

        if (inkJsonAsset == null) return;

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

    private AudioSource CreateChildAudioSource(string childName, bool loop)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.volume = loop ? 0f : 1f;
        return src;
    }

    private void OnDestroy()
    {
        if (_bgmFadeTween.IsActive())
            _bgmFadeTween.Cancel();
    }

    public void RegisterSFX(string id, AudioClip clip)
    {
        if (!string.IsNullOrWhiteSpace(id) && clip != null)
            _sfxLookup[id.Trim()] = clip;
    }

    public void RegisterBGM(string id, AudioClip clip)
    {
        if (!string.IsNullOrWhiteSpace(id) && clip != null)
            _bgmLookup[id.Trim()] = clip;
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

    // =========================================================================
    // STORY LOADING & NAVIGATION
    // =========================================================================

    public void LoadStory(TextAsset compiledInkJson, string startKnot = null)
    {
        if (compiledInkJson == null)
        {
            Debug.LogError("[VNStoryController] Compiled Ink JSON asset is null.");
            return;
        }

        SetPaused(false);
        SetAutoMode(false);
        _areChoicesVisible = false;
        _visitedChoicePaths.Clear();
        _backlog.Clear();
        stageView.HideChoices();

        _story = new Story(compiledInkJson.text);

        if (!string.IsNullOrWhiteSpace(startKnot))
            _story.ChoosePathString(startKnot);

        StepStory(instantPresentation: false);
    }

    public void JumpToKnot(string knotName, bool clearStageFirst = true)
    {
        if (_story == null)
        {
            if (inkJsonAsset != null)
            {
                LoadStory(inkJsonAsset, knotName);
                return;
            }

            Debug.LogWarning("[VNStoryController] No story loaded to jump.");
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

        SetPaused(false);
        SetAutoMode(false);
        _areChoicesVisible = false;

        if (clearStageFirst)
        {
            stageView.ClearAllCharacters();
            stageView.HideEventCG(instant: true);
            stageView.HideChoices();
        }

        StepStory(instantPresentation: false);
    }

    // =========================================================================
    // CORE INK EXECUTION LOOP
    // =========================================================================

    private void StepStory(bool instantPresentation)
    {
        if (_story == null) return;
        CancelModeTimer();

        while (_story.canContinue)
        {
            string rawLine = _story.Continue();
            string speakerOverride = ProcessTags(_story.currentTags, instantPresentation);
            string trimmedLine = rawLine.Trim();

            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            _areChoicesVisible = false;
            ExtractSpeakerAndText(trimmedLine, speakerOverride, out string speaker, out string dialogue);
            RecordBacklogEntry(speaker, dialogue, isChoice: false);
            _lastSpeaker = speaker;
            _lastDialogue = dialogue;
            stageView.PresentLine(speaker, dialogue, instant: instantPresentation);
            return;
        }

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

    private void PresentCurrentChoices()
    {
        _areChoicesVisible = true;
        var options = new List<VNChoiceOption>(_story.currentChoices.Count);

        foreach (Choice choice in _story.currentChoices)
        {
            bool visited = _visitedChoicePaths.Contains(choice.pathStringOnChoice);
            options.Add(new VNChoiceOption(choice.text.Trim(), visited));
        }

        stageView.ShowChoices(options);
    }

    // =========================================================================
    // INK TAG PARSER (#bg:, #cg:, #char:, #hide:, #speaker:, #clear)
    // =========================================================================

    private string ProcessTags(List<string> tags, bool instant)
    {
        string speakerOverride = null;
        if (tags == null || tags.Count == 0) return null;

        foreach (string rawTag in tags)
        {
            if (string.IsNullOrWhiteSpace(rawTag)) continue;

            string[] parts = rawTag.Trim().Split(':');
            string command = parts[0].Trim().ToLowerInvariant();

            switch (command)
            {
                case "bg" when parts.Length >= 2:
                    {
                        string bgId = parts[1].Trim();
                        bool forceInstant = instant || (parts.Length >= 3 && parts[2].Trim().Equals("instant", StringComparison.OrdinalIgnoreCase));
                        _currentBgId = bgId.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : bgId;

                        if (bgId.Equals("none", StringComparison.OrdinalIgnoreCase))
                            stageView.SetBackground(null, forceInstant);
                        else if (_bgLookup.TryGetValue(bgId, out Sprite sprite))
                            stageView.SetBackground(sprite, forceInstant);
                        else
                            Debug.LogWarning($"[VNStoryController] Background ID not found: '{bgId}'");
                        break;
                    }

                case "cg" when parts.Length >= 2:
                    {
                        string cgId = parts[1].Trim();
                        bool forceInstant = instant || (parts.Length >= 3 && parts[2].Trim().Equals("instant", StringComparison.OrdinalIgnoreCase));
                        _currentCgId = (cgId.Equals("hide", StringComparison.OrdinalIgnoreCase) || cgId.Equals("none", StringComparison.OrdinalIgnoreCase)) ? string.Empty : cgId;

                        if (cgId.Equals("hide", StringComparison.OrdinalIgnoreCase) || cgId.Equals("none", StringComparison.OrdinalIgnoreCase))
                            stageView.HideEventCG(forceInstant);
                        else if (_cgLookup.TryGetValue(cgId, out Sprite sprite))
                            stageView.ShowEventCG(sprite, forceInstant);
                        else
                            Debug.LogWarning($"[VNStoryController] Event CG ID not found: '{cgId}'");
                        break;
                    }

                case "char" when parts.Length >= 2:
                    {
                        string charId = parts[1].Trim();
                        string expression = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])
                            ? parts[2].Trim()
                            : "neutral";

                        StageSlot slot = StageSlot.Auto;
                        if (parts.Length >= 4)
                        {
                            slot = parts[3].Trim().ToLowerInvariant() switch
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

                case "hide" when parts.Length >= 2:
                    {
                        string target = parts[1].Trim();
                        if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
                            stageView.ClearAllCharacters();
                        else
                            stageView.HideCharacter(target);
                        break;
                    }

                case "speaker" when parts.Length >= 2:
                    {
                        speakerOverride = parts[1].Trim();
                        break;
                    }

                case "sfx" when parts.Length >= 2:
                    {
                        // Suppress one-shot SFX when fast-forwarding skipped lines
                        if (instant) break;

                        string sfxId = parts[1].Trim();
                        float volumeScale = 1f;
                        if (parts.Length >= 3 && float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float parsedVol))
                        {
                            volumeScale = Mathf.Clamp01(parsedVol);
                        }

                        PlaySFX(sfxId, volumeScale);
                        break;
                    }

                case "bgm" when parts.Length >= 2:
                case "ambience" when parts.Length >= 2:
                    {
                        string bgmId = parts[1].Trim();
                        bool forceInstant = instant || (parts.Length >= 3 && parts[2].Trim().Equals("instant", StringComparison.OrdinalIgnoreCase));

                        if (bgmId.Equals("none", StringComparison.OrdinalIgnoreCase) || bgmId.Equals("stop", StringComparison.OrdinalIgnoreCase))
                            StopBGM(forceInstant);
                        else
                            PlayBGM(bgmId, forceInstant);
                        break;
                    }

                case "clear":
                    {
                        stageView.ClearAllCharacters();
                        stageView.HideEventCG(instant);
                        _currentCgId = string.Empty;
                        break;
                    }
            }
        }

        return speakerOverride;
    }

    private static void ExtractSpeakerAndText(string line, string tagOverride, out string speaker, out string text)
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

        speaker = string.Empty;
        text = line;
    }

    // =========================================================================
    // INPUT, PAUSE, AUTOPLAY & SKIP SCENE
    // =========================================================================

    private void HandleInputActionPerformed(InputAction.CallbackContext ctx) => HandleAdvanceRequested();
    private void HandleToggleAutoPerformed(InputAction.CallbackContext ctx) => ToggleAutoMode();
    private void HandleResumeClicked() => SetPaused(false);

    private void HandleAdvanceRequested()
    {
        if (_story == null || _isPaused || !stageView.IsDialogueVisible) return;

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
        return new VNSaveData
        {
            backlog = new List<VNBacklogEntry>(_backlog),
            label = label,
            inkStateJson = _story.state.ToJson(),
            visitedChoicePaths = new List<string>(_visitedChoicePaths),
            speaker = _lastSpeaker,
            dialogue = _lastDialogue,
            isWaitingForChoice = _areChoicesVisible,
            backgroundId = _currentBgId,
            eventCgId = _currentCgId,
            bgmId = _currentBgmId,
            activeSpeakerId = stageView.GetActiveSpeakerId(),
            characters = stageView.CaptureCharacterStates()
        };
    }

    public void LoadFromSaveData(VNSaveData data)
    {
        if (data == null || inkJsonAsset == null)
            return;

        SetPaused(false);
        SetAutoMode(false);
        CancelModeTimer();

        _story = new Story(inkJsonAsset.text);
        _story.state.LoadJson(data.inkStateJson);

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

        // 2. Restore BGM
        if (!string.IsNullOrEmpty(data.bgmId))
            PlayBGM(data.bgmId, instant: true);
        else
            StopBGM(instant: true);

        // 3. Restore Characters & Speaker Highlight
        stageView.RestoreCharacterStates(data.characters, data.activeSpeakerId);

        // 4. Restore Dialogue & Pending Choices
        _lastSpeaker = data.speaker ?? string.Empty;
        _lastDialogue = data.dialogue ?? string.Empty;
        stageView.HideChoices();
        _backlog.Clear();
        if (data.backlog != null)
            _backlog.AddRange(data.backlog);

        if (data.isWaitingForChoice && _story.currentChoices.Count > 0)
        {
            stageView.PresentLine(_lastSpeaker, _lastDialogue, instant: true);
            PresentCurrentChoices();
        }
        else
        {
            _areChoicesVisible = false;
            stageView.PresentLine(_lastSpeaker, _lastDialogue, instant: false);
        }
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
        // Manual Save shows Slots 1-3 (Autosave is reserved for automatic choice checkpoints)
        var slots = VNSaveManager.Instance.GetAllSlotSummaries(includeAutosave: false);
        stageView.ShowSaveLoadModal(isSaveMode: true, slots);
    }

    private void OpenLoadModal()
    {
        if (VNSaveManager.Instance == null) return;
        // Load shows Autosave Checkpoint + Slots 1-3
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
            OpenSaveModal(); // Refresh slot timestamps and previews immediately
        }
        else
        {
            VNSaveData data = VNSaveManager.Instance.LoadFromSlot(slotId);
            if (data != null)
                LoadFromSaveData(data);
        }
    }

    private async void QuitToMainMenu()
    {
        CancelModeTimer();
        Time.timeScale = 1f;
        VNSessionBridge.LaunchMode = VNLaunchMode.NewGame;
        await UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(mainMenuSceneName);
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
            if (!_isPaused && !stageView.IsTyping && _story != null && !_areChoicesVisible)
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

            if (_isAutoMode && !stageView.IsTyping && _story != null && !_areChoicesVisible)
                StartDelayedAdvanceAsync(autoAdvanceDelay);
        }

        if (pauseAudioOnPauseMenu)
        {
            if (paused)
            {
                sfxAudioSource?.Pause();
                bgmSourceA?.Pause();
                bgmSourceB?.Pause();
            }
            else
            {
                sfxAudioSource?.UnPause();
                bgmSourceA?.UnPause();
                bgmSourceB?.UnPause();
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

        if (stageView.IsTyping)
            stageView.CompleteTyping();

        string startingKnot = GetCurrentKnotName();
        string pendingSpeaker = null;
        string pendingDialogue = null;

        while (_story.canContinue)
        {
            string rawLine = _story.Continue();
            bool hasSceneBreakTag = HasSceneBreakTag(_story.currentTags);
            string speakerOverride = ProcessTags(_story.currentTags, instant: true);
            string trimmedLine = rawLine.Trim();

            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            ExtractSpeakerAndText(trimmedLine, speakerOverride, out string speaker, out string dialogue);
            RecordBacklogEntry(speaker, dialogue, isChoice: false);
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
            string cmd = tag.Trim().Split(':')[0].Trim().ToLowerInvariant();
            if (cmd == "scene" || cmd == "bg")
                return true;
        }
        return false;
    }

    private void HandleLineTypingCompleted()
    {
        if (_story == null || _isPaused || _areChoicesVisible) return;

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
            if (token.IsCancellationRequested || _isPaused) return;

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

    public void PlaySFX(string sfxId, float volumeMultiplier = 1f)
    {
        if (string.IsNullOrWhiteSpace(sfxId) || sfxAudioSource == null)
            return;

        if (_sfxLookup.TryGetValue(sfxId, out AudioClip clip))
            sfxAudioSource.PlayOneShot(clip, sfxVolume * Mathf.Clamp01(volumeMultiplier));
        else
            Debug.LogWarning($"[VNStoryController] SFX ID not found: '{sfxId}'");
    }

    public void PlayBGM(string bgmId, bool instant = false)
    {
        if (string.IsNullOrWhiteSpace(bgmId))
            return;

        // Avoid restarting the track if consecutive scenes share the same BGM
        if (string.Equals(_currentBgmId, bgmId, StringComparison.OrdinalIgnoreCase) &&
            _activeBgmSource != null && _activeBgmSource.isPlaying)
        {
            return;
        }

        if (!_bgmLookup.TryGetValue(bgmId, out AudioClip nextClip))
        {
            Debug.LogWarning($"[VNStoryController] BGM ID not found: '{bgmId}'");
            return;
        }

        if (_bgmFadeTween.IsActive())
            _bgmFadeTween.Cancel();

        _currentBgmId = bgmId;
        AudioSource outgoing = _activeBgmSource;
        AudioSource incoming = (_activeBgmSource == bgmSourceA) ? bgmSourceB : bgmSourceA;
        _activeBgmSource = incoming;

        incoming.clip = nextClip;
        incoming.loop = true;

        if (instant || bgmFadeDuration <= 0f)
        {
            outgoing.Stop();
            outgoing.volume = 0f;
            incoming.volume = bgmVolume;
            incoming.Play();
            return;
        }

        float outgoingStartVol = outgoing.isPlaying ? outgoing.volume : 0f;
        incoming.volume = 0f;
        incoming.Play();

        _bgmFadeTween = LMotion.Create(0f, 1f, bgmFadeDuration)
            .WithEase(Ease.Linear)
            .WithOnComplete(() =>
            {
                outgoing.Stop();
                outgoing.volume = 0f;
                incoming.volume = bgmVolume;
            })
            .Bind(t =>
            {
                outgoing.volume = Mathf.Lerp(outgoingStartVol, 0f, t);
                incoming.volume = Mathf.Lerp(0f, bgmVolume, t);
            });
    }

    public void StopBGM(bool instant = false)
    {
        if (_bgmFadeTween.IsActive())
            _bgmFadeTween.Cancel();

        _currentBgmId = string.Empty;

        if (_activeBgmSource == null || !_activeBgmSource.isPlaying)
            return;

        AudioSource target = _activeBgmSource;

        if (instant || bgmFadeDuration <= 0f)
        {
            target.Stop();
            target.volume = 0f;
            return;
        }

        float startVol = target.volume;
        _bgmFadeTween = LMotion.Create(startVol, 0f, bgmFadeDuration)
            .WithEase(Ease.Linear)
            .WithOnComplete(() =>
            {
                target.Stop();
                target.volume = 0f;
            })
            .Bind(v => target.volume = v);
    }
}