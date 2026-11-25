using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SpatialAudioPhoneController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform microphoneTransform;
    [SerializeField] private PanoramaEdgeScroller edgeScroller;
    [SerializeField] private AudioMixCaptureTap audioCaptureTap;

    [Header("Phone Screen Visuals")]
    [SerializeField] private SpriteRenderer[] meterBars;
    [SerializeField] private SpriteRenderer recordProgressFill;
    [SerializeField] private SpriteRenderer recDotRenderer;
    [SerializeField] private SpriteRenderer screenFlashRenderer;
    [SerializeField] private float meterMinHeight = 0.18f;
    [SerializeField] private float meterMaxHeight = 2.0f;
    [SerializeField] private float progressFullWidth = 2.1f;

    [Header("Recording Settings")]
    [SerializeField] private float minRecordDuration = 0.25f;
    [SerializeField] private float maxRecordDuration = 8.0f;
    [SerializeField] private float flashDuration = 0.25f;
    [SerializeField] private bool allowSpaceKeyRecord = true;
    [SerializeField] private bool allowRKeyRecord = true;

    [Header("Storage Settings")]
    [SerializeField] private bool saveRecordingsToDisk = true;
    [SerializeField] private string saveSubFolder = AudioRecordingStorage.DefaultFolderName;
    [SerializeField] private string filePrefix = "recording";

    private readonly List<AudioClip> _recordedClips = new List<AudioClip>();
    private readonly List<string> _savedWavPaths = new List<string>();
    private readonly Dictionary<RecordableSoundSource, float> _clearExposureTimes = new Dictionary<RecordableSoundSource, float>();
    private RecordableSoundSource[] _cachedSources;
    private float _elapsedRecordTime;
    private float _flashTimer;
    private bool _maxDurationStopPending;
    private bool _recordingStartedByInput;
    private Color _recDotBaseColor = new Color(0.85f, 0.22f, 0.22f, 1f);

    public event Action<AudioClip, string> OnRecordingSaved;
    public event Action<RecordableSoundSource> OnSoundRecorded;

    public bool IsRecordingEnabled { get; set; } = true;
    public bool IsRecording { get; private set; }
    public float RecordingProgress => maxRecordDuration > 0.01f ? Mathf.Clamp01(_elapsedRecordTime / maxRecordDuration) : 0f;
    public float StrongestSignal { get; private set; }
    public RecordableSoundSource StrongestSource { get; private set; }
    public IReadOnlyList<AudioClip> RecordedClips => _recordedClips;
    public IReadOnlyList<string> SavedWavPaths => _savedWavPaths;
    public AudioClip LastRecordedClip => _recordedClips.Count > 0 ? _recordedClips[_recordedClips.Count - 1] : null;
    public string LastSavedWavPath => _savedWavPaths.Count > 0 ? _savedWavPaths[_savedWavPaths.Count - 1] : null;

    private void Awake()
    {
        if (microphoneTransform == null)
        {
            microphoneTransform = transform;
        }

        if (audioCaptureTap == null)
        {
            audioCaptureTap = FindAnyObjectByType<AudioMixCaptureTap>();
        }

        if (recDotRenderer != null)
        {
            _recDotBaseColor = recDotRenderer.color;
            SetRendererAlpha(recDotRenderer, 0.2f);
        }

        SetRendererAlpha(screenFlashRenderer, 0f);
        UpdateProgressVisual(0f);
        RefreshSoundSources();
    }

    public void RefreshSoundSources()
    {
        _cachedSources = FindObjectsByType<RecordableSoundSource>(FindObjectsInactive.Exclude);
    }

    private void Update()
    {
        UpdateFlash();
        UpdateSpatialHearing();
        UpdateRecordingInput();
        UpdatePhoneScreenMeter();
    }

    public void UpdateSpatialHearing()
    {
        if (_cachedSources == null || _cachedSources.Length == 0)
        {
            RefreshSoundSources();
        }

        float wrapWidth = edgeScroller != null && edgeScroller.LoopHorizontally ? edgeScroller.PanoramaWidth : 0f;
        Vector3 micPos = microphoneTransform.position;

        RecordableSoundSource strongest = null;
        float maxSignal = 0f;

        for (int i = 0; i < _cachedSources.Length; i++)
        {
            RecordableSoundSource src = _cachedSources[i];
            if (src == null || !src.isActiveAndEnabled)
            {
                continue;
            }

            float signal = src.UpdateSpatialAudio(micPos, wrapWidth);
            if (signal > maxSignal)
            {
                maxSignal = signal;
                strongest = src;
            }
        }

        StrongestSource = strongest;
        StrongestSignal = maxSignal;
    }

    private void UpdateRecordingInput()
    {
        if (!IsRecordingEnabled)
        {
            if (IsRecording)
            {
                StopRecordingAndSave();
            }

            UpdateRecDot(false);
            return;
        }

        bool holdingMouse = Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool holdingSpace = allowSpaceKeyRecord && Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        bool holdingR = allowRKeyRecord && Keyboard.current != null && Keyboard.current.rKey.isPressed;
        bool holdingRecord = holdingMouse || holdingSpace || holdingR;

        if (!holdingRecord)
        {
            _maxDurationStopPending = false;
        }

        if (holdingRecord && !_maxDurationStopPending)
        {
            if (!IsRecording)
            {
                _recordingStartedByInput = true;
                StartRecording();
            }

            StepActiveRecording(Time.deltaTime);
        }
        else if (IsRecording)
        {
            if (_recordingStartedByInput)
            {
                StopRecordingAndSave();
            }
            else
            {
                StepActiveRecording(Time.deltaTime);
            }
        }

        UpdateRecDot(IsRecording);
    }

    public void StartRecording()
    {
        if (IsRecording)
        {
            return;
        }

        IsRecording = true;
        _elapsedRecordTime = 0f;
        _clearExposureTimes.Clear();
        UpdateProgressVisual(0f);

        if (audioCaptureTap != null)
        {
            audioCaptureTap.StartCapture();
        }
    }

    public void StepActiveRecording(float deltaTime)
    {
        if (!IsRecording)
        {
            return;
        }

        _elapsedRecordTime += deltaTime;
        UpdateProgressVisual(RecordingProgress);

        if (_cachedSources != null)
        {
            for (int i = 0; i < _cachedSources.Length; i++)
            {
                RecordableSoundSource src = _cachedSources[i];
                if (src == null || !src.CanBeRecorded)
                {
                    continue;
                }

                if (src.CurrentSignalStrength >= src.RequiredSignalStrength)
                {
                    _clearExposureTimes.TryGetValue(src, out float currentExposure);
                    _clearExposureTimes[src] = currentExposure + deltaTime;
                }
            }
        }

        if (maxRecordDuration > 0.01f && _elapsedRecordTime >= maxRecordDuration)
        {
            _maxDurationStopPending = true;
            StopRecordingAndSave();
        }
    }

    public AudioClip StopRecordingAndSave()
    {
        if (!IsRecording)
        {
            return null;
        }

        IsRecording = false;
        _recordingStartedByInput = false;
        float recordedSeconds = _elapsedRecordTime;
        _elapsedRecordTime = 0f;
        UpdateProgressVisual(0f);

        float[] samples = null;
        int channels = 2;
        int sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;

        if (audioCaptureTap != null)
        {
            samples = audioCaptureTap.StopCapture(out channels, out sampleRate);
        }

        if (recordedSeconds < minRecordDuration || samples == null || samples.Length == 0)
        {
            _clearExposureTimes.Clear();
            return null;
        }

        int frameCount = samples.Length / Mathf.Max(1, channels);
        AudioClip clip = null;
        if (frameCount > 0)
        {
            clip = AudioClip.Create($"Recording_{_recordedClips.Count + 1}", frameCount, channels, sampleRate, false);
            clip.SetData(samples, 0);
            _recordedClips.Add(clip);
        }

        string savedPath = null;
        if (saveRecordingsToDisk)
        {
            savedPath = AudioRecordingStorage.SaveWav(samples, channels, sampleRate, filePrefix, saveSubFolder);
            if (!string.IsNullOrEmpty(savedPath))
            {
                _savedWavPaths.Add(savedPath);
            }
        }

        _flashTimer = flashDuration;
        SetRendererAlpha(screenFlashRenderer, 0.75f);
        OnRecordingSaved?.Invoke(clip, savedPath);

        RecordableSoundSource clearlyRecordedSource = FindClearlyRecordedSource();
        _clearExposureTimes.Clear();

        if (clearlyRecordedSource != null)
        {
            clearlyRecordedSource.NotifyRecorded();
            OnSoundRecorded?.Invoke(clearlyRecordedSource);
        }

        return clip;
    }

    private RecordableSoundSource FindClearlyRecordedSource()
    {
        RecordableSoundSource bestSource = null;
        float bestExposure = 0f;

        foreach (KeyValuePair<RecordableSoundSource, float> entry in _clearExposureTimes)
        {
            RecordableSoundSource src = entry.Key;
            float exposure = entry.Value;
            if (src != null && src.CanBeRecorded && exposure >= src.RecordDuration && exposure > bestExposure)
            {
                bestExposure = exposure;
                bestSource = src;
            }
        }

        return bestSource;
    }

    private void UpdatePhoneScreenMeter()
    {
        if (meterBars == null || meterBars.Length == 0)
        {
            return;
        }

        float time = Time.unscaledTime;
        float centerIndex = (meterBars.Length - 1) * 0.5f;
        float liveAudioLevel = audioCaptureTap != null ? Mathf.Clamp01(audioCaptureTap.CurrentRmsLevel * 4.5f) : 0f;
        float energy = Mathf.Max(liveAudioLevel, StrongestSignal * 0.65f);

        for (int i = 0; i < meterBars.Length; i++)
        {
            SpriteRenderer bar = meterBars[i];
            if (bar == null)
            {
                continue;
            }

            float centerWeight = 1f - Mathf.Abs(i - centerIndex) / Mathf.Max(1f, centerIndex + 1f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * (8f + i * 2.7f) + i * 1.4f);
            float baseAmbient = IsRecording ? 0.16f : 0.08f;
            float level = Mathf.Clamp01(baseAmbient + energy * (0.55f + 0.45f * pulse) * (0.65f + 0.35f * centerWeight));
            float height = Mathf.Lerp(meterMinHeight, meterMaxHeight, level);

            Vector3 scale = bar.transform.localScale;
            scale.y = height;
            bar.transform.localScale = scale;
        }
    }

    private void UpdateProgressVisual(float normalized)
    {
        if (recordProgressFill == null)
        {
            return;
        }

        Vector3 scale = recordProgressFill.transform.localScale;
        scale.x = progressFullWidth * Mathf.Clamp01(normalized);
        recordProgressFill.transform.localScale = scale;
    }

    private void UpdateRecDot(bool active)
    {
        if (recDotRenderer == null)
        {
            return;
        }

        float alpha = active ? (0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 10f)) : 0.2f;
        Color c = _recDotBaseColor;
        c.a = alpha;
        recDotRenderer.color = c;
    }

    private void UpdateFlash()
    {
        if (_flashTimer <= 0f)
        {
            return;
        }

        _flashTimer -= Time.unscaledDeltaTime;
        float normalized = flashDuration > 0f ? Mathf.Clamp01(_flashTimer / flashDuration) : 0f;
        SetRendererAlpha(screenFlashRenderer, normalized * 0.75f);
    }

    private static void SetRendererAlpha(SpriteRenderer sr, float alpha)
    {
        if (sr == null)
        {
            return;
        }

        Color c = sr.color;
        c.a = alpha;
        sr.color = c;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _recordedClips.Count; i++)
        {
            if (_recordedClips[i] != null)
            {
                Destroy(_recordedClips[i]);
            }
        }

        _recordedClips.Clear();
    }
}
