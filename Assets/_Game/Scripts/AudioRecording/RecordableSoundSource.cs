using System;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(AudioSource))]
public class RecordableSoundSource : MonoBehaviour
{
    [Header("Sound Identity")]
    [SerializeField] private string soundId = "sound_01";
    [SerializeField] private string displayName = "Medina Sound";
    [SerializeField] private string dialogueKnotName = "";

    [Header("Audio Components")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioLowPassFilter lowPassFilter;

    [Header("Spatial Hearing Settings")]
    [SerializeField] private float innerFocusRadius = 2.0f;
    [SerializeField] private float outerFalloffRadius = 11.0f;
    [SerializeField] private float panSpreadDistance = 8.0f;
    [SerializeField, Range(0f, 1f)] private float backgroundNoiseVolume = 0.12f;
    [SerializeField, Range(0f, 1f)] private float focusVolume = 1.0f;
    [SerializeField] private float backgroundLowPassCutoff = 900f;
    [SerializeField] private float focusLowPassCutoff = 22000f;

    [Header("Recording Settings")]
    [SerializeField, Range(0.1f, 1f)] private float requiredSignalStrength = 0.65f;
    [SerializeField] private float recordDuration = 2.0f;
    [SerializeField] private bool recordOnce = true;

    [Header("Placeholder Indicator")]
    [SerializeField] private SpriteRenderer indicatorRenderer;
    [SerializeField] private Color idleColor = new Color(0.949f, 0.929f, 0.890f, 0.45f);
    [SerializeField] private Color focusedColor = new Color(0.671f, 0.376f, 0.282f, 0.95f);
    [SerializeField] private Color recordedColor = new Color(0.4f, 0.463f, 0.353f, 0.85f);

    [Header("Events")]
    [SerializeField] private UnityEvent<RecordableSoundSource> onRecorded;

    private bool _hasBeenRecorded;

    public event Action<RecordableSoundSource> OnSoundSourceRecorded;

    public string SoundId => soundId;
    public string DisplayName => displayName;
    public string DialogueKnotName => dialogueKnotName;
    public float RequiredSignalStrength => requiredSignalStrength;
    public float RecordDuration => Mathf.Max(0.1f, recordDuration);
    public bool RecordOnce => recordOnce;
    public bool HasBeenRecorded => _hasBeenRecorded;
    public bool CanBeRecorded => !recordOnce || !_hasBeenRecorded;
    public float CurrentSignalStrength { get; private set; }
    public float CurrentWrappedDistance { get; private set; }

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (lowPassFilter == null)
        {
            lowPassFilter = GetComponent<AudioLowPassFilter>();
        }

        if (audioSource != null)
        {
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.volume = backgroundNoiseVolume;
            if (!audioSource.isPlaying && audioSource.clip != null)
            {
                audioSource.Play();
            }
        }

        if (lowPassFilter != null)
        {
            lowPassFilter.cutoffFrequency = backgroundLowPassCutoff;
        }
    }

    public float UpdateSpatialAudio(Vector3 listenerWorldPosition, float panoramaWrapWidth)
    {
        Vector3 sourcePos = transform.position;
        float dx = sourcePos.x - listenerWorldPosition.x;

        if (panoramaWrapWidth > 0.01f)
        {
            float dxLeft = (sourcePos.x - panoramaWrapWidth) - listenerWorldPosition.x;
            float dxRight = (sourcePos.x + panoramaWrapWidth) - listenerWorldPosition.x;

            if (Mathf.Abs(dxLeft) < Mathf.Abs(dx))
            {
                dx = dxLeft;
            }

            if (Mathf.Abs(dxRight) < Mathf.Abs(dx))
            {
                dx = dxRight;
            }
        }

        float dy = sourcePos.y - listenerWorldPosition.y;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);
        CurrentWrappedDistance = distance;

        float safeOuter = Mathf.Max(innerFocusRadius + 0.01f, outerFalloffRadius);
        float normalizedDist = Mathf.InverseLerp(innerFocusRadius, safeOuter, distance);
        float signal = 1f - Mathf.SmoothStep(0f, 1f, normalizedDist);
        CurrentSignalStrength = signal;

        if (audioSource != null)
        {
            audioSource.volume = Mathf.Lerp(backgroundNoiseVolume, focusVolume, signal);
            audioSource.panStereo = panSpreadDistance > 0.01f
                ? Mathf.Clamp(dx / panSpreadDistance, -1f, 1f)
                : 0f;
        }

        if (lowPassFilter != null)
        {
            lowPassFilter.cutoffFrequency = Mathf.Lerp(backgroundLowPassCutoff, focusLowPassCutoff, signal * signal);
        }

        if (indicatorRenderer != null)
        {
            indicatorRenderer.color = _hasBeenRecorded
                ? recordedColor
                : Color.Lerp(idleColor, focusedColor, signal);
        }

        return signal;
    }

    public void NotifyRecorded()
    {
        if (!CanBeRecorded)
        {
            return;
        }

        _hasBeenRecorded = true;
        if (indicatorRenderer != null)
        {
            indicatorRenderer.color = recordedColor;
        }

        OnSoundSourceRecorded?.Invoke(this);
        onRecorded?.Invoke(this);
    }

    public void ResetRecordedState()
    {
        _hasBeenRecorded = false;
    }
}
