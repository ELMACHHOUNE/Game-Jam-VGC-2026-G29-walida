using System;
using FMODUnity;
using UnityEngine;
using Walida.Audio;

/// <summary>
/// Watches where the player aims the phone viewfinder on the 360° panorama.
/// When moving near/onto a MemoryHotspot, plays a camera glitch effect and reveals
/// the aligned 1960s memory photograph behind the present-day scene inside the viewfinder.
/// </summary>
public class MemoryGlitchController : MonoBehaviour
{
    private static readonly int RevealAmountId = Shader.PropertyToID("_MemoryRevealAmount");
    private static readonly int GlitchIntensityId = Shader.PropertyToID("_MemoryGlitchIntensity");
    private static readonly int GlitchTimeId = Shader.PropertyToID("_MemoryGlitchTime");

    [Header("References")]
    [SerializeField] private Transform viewfinderTransform;
    [SerializeField] private PanoramaEdgeScroller edgeScroller;
    [SerializeField] private SpriteRenderer memoryOverlayRenderer;
    [SerializeField] private Transform phoneVisualShakeTarget;

    [Header("Reveal & Glitch Tuning")]
    [Tooltip("Proximity threshold [0..1] where the memory locks in and triggers the main glitch reveal.")]
    [SerializeField, Range(0.2f, 0.98f)] private float lockThreshold = 0.62f;
    [SerializeField] private float revealBlendSpeed = 4.5f;
    [SerializeField] private float hideBlendSpeed = 5.5f;
    [Tooltip("Duration in seconds of the dramatic glitch burst when entering or leaving the memory.")]
    [SerializeField] private float glitchBurstDuration = 0.55f;
    [Tooltip("Subtle ongoing analog shimmer while looking into the 1960s memory.")]
    [SerializeField, Range(0f, 0.3f)] private float settledMemoryGlitch = 0.07f;
    [Tooltip("Maximum intensity of glitch interference while approaching the hotspot.")]
    [SerializeField, Range(0f, 1f)] private float approachGlitchMax = 0.72f;
    [Tooltip("Subtle local micro-shake applied to the phone visual during strong glitch bursts.")]
    [SerializeField] private float phoneShakeAmplitude = 0.045f;

    [Header("FMOD Memory & Glitch Audio")]
    [SerializeField] private EventReference staticRiserEvent;
    [SerializeField] private EventReference whisperEvent;
    [SerializeField] private EventReference slowHeartbeatEvent;
    [SerializeField] private EventReference rumbleEvent;
    [SerializeField, Range(0f, 1f)] private float maxStaticRiserVolume = 0.45f;
    [SerializeField, Range(0f, 1f)] private float maxWhisperVolume = 0.32f;
    [SerializeField, Range(0f, 1f)] private float maxHeartbeatVolume = 0.34f;
    [SerializeField, Range(0f, 1f)] private float rumbleBurstVolume = 0.42f;

    private MemoryHotspot[] _hotspots;
    private MemoryHotspot _activeHotspot;
    private FMODLoopHandle _staticRiserLoop;
    private FMODLoopHandle _whisperLoop;
    private FMODLoopHandle _heartbeatLoop;
    private FMODLoopHandle _rumbleHandle;
    private float _currentReveal;
    private float _currentGlitch;
    private float _burstTimer;
    private Vector3 _basePhoneVisualLocalPos;
    private bool _capturedBaseLocalPos;

    public event Action<MemoryHotspot> OnMemoryLocked;
    public event Action<MemoryHotspot> OnMemoryUnlocked;

    public float CurrentRevealAmount => _currentReveal;
    public float CurrentGlitchIntensity => _currentGlitch;
    public MemoryHotspot ActiveHotspot => _activeHotspot;
    public bool IsViewingMemory => _activeHotspot != null && _currentReveal >= 0.5f;

    private void Awake()
    {
        if (phoneVisualShakeTarget != null)
        {
            _basePhoneVisualLocalPos = phoneVisualShakeTarget.localPosition;
            _capturedBaseLocalPos = true;
        }

        RefreshHotspots();
        ResetShaderGlobals();
    }

    private void OnEnable()
    {
        RefreshHotspots();
    }

    private void OnDisable()
    {
        StopMemoryAudio(immediate: false);
        ResetShaderGlobals();
        RestorePhoneShakeOffset();
    }

    private void OnDestroy()
    {
        ReleaseMemoryAudio();
        ResetShaderGlobals();
    }

    public void RefreshHotspots()
    {
        _hotspots = FindObjectsByType<MemoryHotspot>(FindObjectsInactive.Exclude);
    }

    private void Update()
    {
        if (viewfinderTransform == null)
        {
            return;
        }

        if (_hotspots == null || _hotspots.Length == 0)
        {
            RefreshHotspots();
        }

        float wrapWidth = edgeScroller != null && edgeScroller.LoopHorizontally ? edgeScroller.PanoramaWidth : 0f;
        Vector3 viewfinderPos = viewfinderTransform.position;

        MemoryHotspot bestHotspot = null;
        float bestProximity = 0f;

        if (_hotspots != null)
        {
            for (int i = 0; i < _hotspots.Length; i++)
            {
                MemoryHotspot hotspot = _hotspots[i];
                if (hotspot == null || !hotspot.isActiveAndEnabled)
                {
                    continue;
                }

                float proximity = hotspot.EvaluateProximity(viewfinderPos, wrapWidth, out _);
                if (proximity > bestProximity)
                {
                    bestProximity = proximity;
                    bestHotspot = hotspot;
                }
            }
        }

        MemoryHotspot lockedHotspot = bestProximity >= lockThreshold ? bestHotspot : null;
        if (lockedHotspot != _activeHotspot)
        {
            MemoryHotspot previous = _activeHotspot;
            _activeHotspot = lockedHotspot;

            if (previous != null)
            {
                previous.SetRevealedState(false);
                OnMemoryUnlocked?.Invoke(previous);
            }

            if (_activeHotspot != null)
            {
                _activeHotspot.SetRevealedState(true);
                OnMemoryLocked?.Invoke(_activeHotspot);

                EnsureMemoryAudioHandles();
                _rumbleHandle?.ReleaseImmediate();
                _rumbleHandle?.Start(rumbleEvent, FMODEvents.Rumble, rumbleBurstVolume, fadeInSeconds: 0.05f, autoRestartLoop: false);
            }
            else
            {
                _rumbleHandle?.Stop(0.35f);
            }

            // Trigger a punchy glitch transition burst on enter or exit
            _burstTimer = glitchBurstDuration;
        }

        if (_burstTimer > 0f)
        {
            _burstTimer = Mathf.Max(0f, _burstTimer - Time.unscaledDeltaTime);
        }

        float burstNormalized = glitchBurstDuration > 0.001f ? (_burstTimer / glitchBurstDuration) : 0f;
        float burstCurve = burstNormalized * burstNormalized;

        // Target reveal: ramps subtly as you get close, locks to 1.0 inside the memory zone
        float targetReveal;
        if (_activeHotspot != null)
        {
            targetReveal = 1f;
        }
        else if (bestProximity > 0f)
        {
            float approachT = Mathf.Clamp01(bestProximity / Mathf.Max(0.05f, lockThreshold));
            targetReveal = approachT * approachT * 0.28f;
        }
        else
        {
            targetReveal = 0f;
        }

        float blendSpeed = targetReveal > _currentReveal ? revealBlendSpeed : hideBlendSpeed;
        _currentReveal = Mathf.MoveTowards(_currentReveal, targetReveal, blendSpeed * Time.unscaledDeltaTime);

        // Target glitch: spikes strongly on transition & ramps during approach, settles to subtle vintage shimmer once locked
        float approachGlitch = 0f;
        if (_activeHotspot == null && bestProximity > 0f)
        {
            float approachT = Mathf.Clamp01(bestProximity / Mathf.Max(0.05f, lockThreshold));
            approachGlitch = Mathf.Pow(approachT, 1.35f) * approachGlitchMax;
        }
        else if (_activeHotspot != null)
        {
            approachGlitch = settledMemoryGlitch;
        }

        float targetGlitch = Mathf.Clamp01(Mathf.Max(approachGlitch, burstCurve));
        _currentGlitch = Mathf.Lerp(_currentGlitch, targetGlitch, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));

        ApplyShaderGlobals();
        ApplyPhoneMicroShake(burstCurve);
        UpdateMemoryAudio();
    }

    private void EnsureMemoryAudioHandles()
    {
        if (FMODAudioManager.Instance == null)
            return;

        if (_staticRiserLoop == null) _staticRiserLoop = FMODAudioManager.Instance.CreateManagedLoop();
        if (_whisperLoop == null) _whisperLoop = FMODAudioManager.Instance.CreateManagedLoop();
        if (_heartbeatLoop == null) _heartbeatLoop = FMODAudioManager.Instance.CreateManagedLoop();
        if (_rumbleHandle == null) _rumbleHandle = FMODAudioManager.Instance.CreateManagedLoop();
    }

    private void UpdateMemoryAudio()
    {
        float riserVol = _currentGlitch > 0.04f ? Mathf.Clamp01(_currentGlitch) * maxStaticRiserVolume : 0f;
        float revealFactor = Mathf.InverseLerp(0.2f, 1f, _currentReveal);
        float whisperVol = revealFactor * maxWhisperVolume;
        float heartbeatVol = revealFactor * maxHeartbeatVolume;

        if (riserVol > 0.005f || whisperVol > 0.005f || heartbeatVol > 0.005f)
            EnsureMemoryAudioHandles();

        UpdateLoopVolume(_staticRiserLoop, staticRiserEvent, FMODEvents.StaticRiser, riserVol);
        UpdateLoopVolume(_whisperLoop, whisperEvent, FMODEvents.Whisper, whisperVol);
        UpdateLoopVolume(_heartbeatLoop, slowHeartbeatEvent, FMODEvents.SlowHeartbeat, heartbeatVol);
    }

    private static void UpdateLoopVolume(FMODLoopHandle handle, EventReference eventRef, string fallbackPath, float targetVol)
    {
        if (handle == null)
            return;

        if (targetVol > 0.005f)
        {
            if (!handle.IsActive)
                handle.Start(eventRef, fallbackPath, targetVol, fadeInSeconds: 0.25f, autoRestartLoop: true);
            else
                handle.SetVolumeImmediate(targetVol);
        }
        else if (handle.IsActive)
        {
            handle.Stop(0.35f);
        }
    }

    private void StopMemoryAudio(bool immediate)
    {
        if (immediate)
        {
            _staticRiserLoop?.ReleaseImmediate();
            _whisperLoop?.ReleaseImmediate();
            _heartbeatLoop?.ReleaseImmediate();
            _rumbleHandle?.ReleaseImmediate();
        }
        else
        {
            _staticRiserLoop?.Stop(0.2f);
            _whisperLoop?.Stop(0.25f);
            _heartbeatLoop?.Stop(0.25f);
            _rumbleHandle?.Stop(0.2f);
        }
    }

    private void ReleaseMemoryAudio()
    {
        if (FMODAudioManager.Instance != null)
        {
            FMODAudioManager.Instance.UnregisterLoop(_staticRiserLoop);
            FMODAudioManager.Instance.UnregisterLoop(_whisperLoop);
            FMODAudioManager.Instance.UnregisterLoop(_heartbeatLoop);
            FMODAudioManager.Instance.UnregisterLoop(_rumbleHandle);
        }
        else
        {
            StopMemoryAudio(immediate: true);
        }
    }

    private void ApplyShaderGlobals()
    {
        Shader.SetGlobalFloat(RevealAmountId, _currentReveal);
        Shader.SetGlobalFloat(GlitchIntensityId, _currentGlitch);
        Shader.SetGlobalFloat(GlitchTimeId, Time.unscaledTime);

        if (memoryOverlayRenderer != null && !memoryOverlayRenderer.enabled && (_currentReveal > 0.001f || _currentGlitch > 0.001f))
        {
            memoryOverlayRenderer.enabled = true;
        }
    }

    private void ApplyPhoneMicroShake(float burstStrength)
    {
        if (phoneVisualShakeTarget == null || !_capturedBaseLocalPos)
        {
            return;
        }

        float shake = Mathf.Max(burstStrength, _currentGlitch > 0.35f ? (_currentGlitch - 0.35f) * 0.45f : 0f);
        if (shake <= 0.005f || phoneShakeAmplitude <= 0.0001f)
        {
            phoneVisualShakeTarget.localPosition = _basePhoneVisualLocalPos;
            return;
        }

        float t = Time.unscaledTime * 55f;
        float ox = (Mathf.PerlinNoise(t, 1.7f) - 0.5f) * 2f * phoneShakeAmplitude * shake;
        float oy = (Mathf.PerlinNoise(3.1f, t) - 0.5f) * 2f * phoneShakeAmplitude * shake;
        phoneVisualShakeTarget.localPosition = _basePhoneVisualLocalPos + new Vector3(ox, oy, 0f);
    }

    private void RestorePhoneShakeOffset()
    {
        if (phoneVisualShakeTarget != null && _capturedBaseLocalPos)
        {
            phoneVisualShakeTarget.localPosition = _basePhoneVisualLocalPos;
        }
    }

    private static void ResetShaderGlobals()
    {
        Shader.SetGlobalFloat(RevealAmountId, 0f);
        Shader.SetGlobalFloat(GlitchIntensityId, 0f);
        Shader.SetGlobalFloat(GlitchTimeId, 0f);
    }
}
