using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using FMODUnity;
using Ink.Runtime;
using LitMotion;
using Walida.Audio;

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

    [Serializable]
    public struct NamedFMODEvent
    {
        public string id;
        public EventReference fmodEvent;
    }

    [Header("Dependencies")]
    [SerializeField] private VNStageView stageView;
    [SerializeField] private TextAsset inkJsonAsset;

    [Header("Stage Sprite Registries")]
    [SerializeField] private List<NamedSprite> backgrounds = new();
    [SerializeField] private List<NamedSprite> eventCGs = new();

    [Header("Playback Timings")]
    [SerializeField] private float autoAdvanceDelay = 1.6f;
    [SerializeField] private float choiceRevealDelay = 1.0f;
    [SerializeField] private bool cancelAutoOnManualClick = true;

    [Header("Audio Registries (FMOD)")]
    [SerializeField] private List<NamedFMODEvent> fmodSfxEvents = new();
    [SerializeField] private List<NamedFMODEvent> fmodBgmEvents = new();

    [Header("Audio Registries (Legacy Fallback)")]
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

    private Story _story;
    private readonly Dictionary<string, Sprite> _bgLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sprite> _cgLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visitedChoicePaths = new();
    private readonly Dictionary<string, EventReference> _fmodSfxLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventReference> _fmodBgmLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioClip> _sfxLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioClip> _bgmLookup = new(StringComparer.OrdinalIgnoreCase);

    private FMODLoopHandle _fmodBgmA;
    private FMODLoopHandle _fmodBgmB;
    private FMODLoopHandle _activeFmodBgm;
    private AudioSource _activeBgmSource;
    private string _currentBgmId = string.Empty;
    private MotionHandle _bgmFadeTween;
    private bool _isAutoMode;
    private bool _isPaused;
    private bool _hasStarted;
    private bool _areChoicesVisible;
    private CancellationTokenSource _modeCts;

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

        if (FMODAudioManager.Instance != null)
        {
            _fmodBgmA = FMODAudioManager.Instance.CreateManagedLoop();
            _fmodBgmB = FMODAudioManager.Instance.CreateManagedLoop();
            _activeFmodBgm = _fmodBgmA;
        }

        if (sfxAudioSource == null)
            sfxAudioSource = CreateChildAudioSource("VN_SFX_Channel", loop: false);

        if (bgmSourceA == null)
            bgmSourceA = CreateChildAudioSource("VN_BGM_Channel_A", loop: true);

        if (bgmSourceB == null)
            bgmSourceB = CreateChildAudioSource("VN_BGM_Channel_B", loop: true);

        _activeBgmSource = bgmSourceA;
    }

    private void OnEnable()
    {
        if (stageView == null)
        {
            Debug.LogError("[VNStoryController] VNStageView reference is missing.");
            return;
        }

        stageView.OnViewReady += HandleViewReady;
        stageView.OnAdvanceRequested += HandleAdvanceRequested;
        stageView.OnChoiceSelected += HandleChoiceSelected;
        stageView.OnAutoClicked += ToggleAutoMode;
        stageView.OnSkipClicked += SkipCurrentScene;
        stageView.OnPauseClicked += TogglePause;
        stageView.OnResumeClicked += HandleResumeClicked;
        stageView.OnLineTypingCompleted += HandleLineTypingCompleted;

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
        }

        if (advanceInputAction != null && advanceInputAction.action != null)
            advanceInputAction.action.performed -= HandleInputActionPerformed;

        if (toggleAutoInputAction != null && toggleAutoInputAction.action != null)
            toggleAutoInputAction.action.performed -= HandleToggleAutoPerformed;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            TogglePause();
    }

    private void HandleViewReady()
    {
        if (_hasStarted) return;
        _hasStarted = true;

        if (inkJsonAsset != null)
            LoadStory(inkJsonAsset);
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

        if (FMODAudioManager.Instance != null)
        {
            FMODAudioManager.Instance.UnregisterLoop(_fmodBgmA);
            FMODAudioManager.Instance.UnregisterLoop(_fmodBgmB);
            FMODAudioManager.Instance.SetVNBgmOverrideActive(false);
        }
        else
        {
            _fmodBgmA?.ReleaseImmediate();
            _fmodBgmB?.ReleaseImmediate();
        }
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
            stageView.PresentLine(string.Empty, "<i>[End of Script]</i>", instant: true);
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
        if (_story == null || _isPaused) return;

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
        if (!string.IsNullOrEmpty(chosen.pathStringOnChoice))
            _visitedChoicePaths.Add(chosen.pathStringOnChoice);

        _areChoicesVisible = false;
        _story.ChooseChoiceIndex(choiceIndex);
        StepStory(instantPresentation: false);
    }

    public void ToggleAutoMode() => SetAutoMode(!_isAutoMode);

    public void SetAutoMode(bool enabled)
    {
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

    public void TogglePause() => SetPaused(!_isPaused);

    public void SetPaused(bool paused)
    {
        if (_isPaused == paused) return;
        _isPaused = paused;

        if (pauseAudioOnPauseMenu)
        {
            _fmodBgmA?.SetPaused(paused);
            _fmodBgmB?.SetPaused(paused);
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

        if (_isPaused)
        {
            CancelModeTimer();
            Time.timeScale = 0f;
            stageView.ShowPauseMenu(true);
        }
        else
        {
            Time.timeScale = 1f;
            stageView.ShowPauseMenu(false);

            if (_isAutoMode && !stageView.IsTyping && _story != null && !_areChoicesVisible)
                StartDelayedAdvanceAsync(autoAdvanceDelay);
        }
    }

    public void SkipCurrentScene()
    {
        if (_story == null || _isPaused || _areChoicesVisible)
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
            stageView.PresentLine(string.Empty, "<i>[End of Script]</i>", instant: true);
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
        if (string.IsNullOrWhiteSpace(sfxId))
            return;

        float finalVolume = sfxVolume * Mathf.Clamp01(volumeMultiplier);

        if (_fmodSfxLookup.TryGetValue(sfxId.Trim(), out EventReference mappedEvent) && !mappedEvent.IsNull)
        {
            if (FMODAudioManager.PlayOneShot(mappedEvent, sfxId, finalVolume))
                return;
        }

        if (FMODAudioManager.PlayOneShot(sfxId, finalVolume))
            return;

        if (sfxAudioSource != null && _sfxLookup.TryGetValue(sfxId, out AudioClip clip))
            sfxAudioSource.PlayOneShot(clip, finalVolume);
        else
            Debug.LogWarning($"[VNStoryController] SFX ID not found in FMOD or clip registry: '{sfxId}'");
    }

    public void PlayBGM(string bgmId, bool instant = false)
    {
        if (string.IsNullOrWhiteSpace(bgmId))
            return;

        string trimmedId = bgmId.Trim();

        // Avoid restarting the track if consecutive scenes share the same BGM
        if (string.Equals(_currentBgmId, trimmedId, StringComparison.OrdinalIgnoreCase) &&
            ((_activeFmodBgm != null && _activeFmodBgm.IsActive) ||
             (_activeBgmSource != null && _activeBgmSource.isPlaying)))
        {
            return;
        }

        EnsureFmodBgmHandles();

        if (_fmodBgmA != null && _fmodBgmB != null)
        {
            float fade = instant ? 0f : Mathf.Max(0f, bgmFadeDuration);
            FMODLoopHandle outgoingFmod = _activeFmodBgm;
            FMODLoopHandle incomingFmod = (_activeFmodBgm == _fmodBgmA) ? _fmodBgmB : _fmodBgmA;

            bool startedFmod = _fmodBgmLookup.TryGetValue(trimmedId, out EventReference mappedEvent) && !mappedEvent.IsNull
                ? incomingFmod.Start(mappedEvent, trimmedId, bgmVolume, fade, autoRestartLoop: true)
                : incomingFmod.Start(trimmedId, bgmVolume, fade, autoRestartLoop: true);

            if (startedFmod)
            {
                _currentBgmId = trimmedId;
                _activeFmodBgm = incomingFmod;
                outgoingFmod?.Stop(fade);
                FMODAudioManager.Instance?.SetVNBgmOverrideActive(true);
                return;
            }
        }

        if (!_bgmLookup.TryGetValue(trimmedId, out AudioClip nextClip))
        {
            Debug.LogWarning($"[VNStoryController] BGM ID not found in FMOD or clip registry: '{trimmedId}'");
            return;
        }

        if (_bgmFadeTween.IsActive())
            _bgmFadeTween.Cancel();

        _currentBgmId = trimmedId;
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
        float fade = instant ? 0f : Mathf.Max(0f, bgmFadeDuration);

        _fmodBgmA?.Stop(fade);
        _fmodBgmB?.Stop(fade);
        FMODAudioManager.Instance?.SetVNBgmOverrideActive(false);

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

    private void EnsureFmodBgmHandles()
    {
        if (_fmodBgmA == null && FMODAudioManager.Instance != null)
        {
            _fmodBgmA = FMODAudioManager.Instance.CreateManagedLoop();
            _fmodBgmB = FMODAudioManager.Instance.CreateManagedLoop();
            _activeFmodBgm = _fmodBgmA;
        }
    }
}