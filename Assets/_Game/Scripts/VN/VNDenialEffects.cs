using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

/// <summary>
/// High-denial state. Once the denial score goes above the threshold the game turns "off":
/// URP distortion on the world, random glitch bursts, tunnel vision/dimming behind VN dialogue,
/// an FMOD distortion snapshot, and the music swaps to its horror version.
/// </summary>
public class VNDenialEffects : MonoBehaviour
{
    [Serializable]
    public struct BgmSwap
    {
        public string normalEventPath;
        public string horrorEventPath;
    }

    [Header("References")]
    [SerializeField] private VNStoryController storyController;
    [SerializeField] private VNStageView stageView;
    [Tooltip("Camera whose post-processing is switched on while the effect is active. Defaults to Camera.main.")]
    [SerializeField] private Camera targetCamera;

    [Header("Trigger")]
    [Tooltip("The effect switches on when the denial score is ABOVE this value (there are 8 denial choices in total).")]
    [SerializeField] private int denialThreshold = 5;
    [Tooltip("Seconds to blend every effect in or out.")]
    [SerializeField, Min(0f)] private float blendDuration = 2.5f;
    [Tooltip("Editor testing only: force the high-denial state on. Ignored in builds.")]
    [SerializeField] private bool forceActiveInEditor;

    [Header("World Distortion (URP post-processing)")]
    [Tooltip("Optional: your own Volume. If empty, a global volume is created at runtime from the values below.")]
    [SerializeField] private Volume distortionVolume;
    [SerializeField, Range(0f, 1f)] private float chromaticAberration = 0.55f;
    [SerializeField, Range(0f, 1f)] private float filmGrain = 0.45f;
    [SerializeField, Range(-1f, 1f)] private float lensDistortion = -0.18f;
    [SerializeField, Range(-100f, 0f)] private float saturation = -35f;
    [SerializeField, Range(0f, 1f)] private float worldVignette = 0.35f;

    [Header("Glitch Bursts")]
    [SerializeField] private bool glitchBursts = true;
    [Tooltip("Random seconds between bursts (min, max).")]
    [SerializeField] private Vector2 glitchInterval = new(4f, 10f);
    [Tooltip("Random length of each burst in seconds (min, max).")]
    [SerializeField] private Vector2 glitchDuration = new(0.15f, 0.4f);
    [Tooltip("Camera shudder during a burst in the 2.5D world (world units).")]
    [SerializeField, Range(0f, 0.5f)] private float cameraShake = 0.08f;
    [Tooltip("Screen fader used to draw the tear bars over the 2.5D world. Found automatically if empty.")]
    [SerializeField] private ScreenFader screenFader;

    [Header("Dialogue Tunnel Vision")]
    [SerializeField, Range(0f, 1f)] private float tunnelStrength = 0.9f;
    [Tooltip("Distance from the screen centre (0-1) where the darkening starts.")]
    [SerializeField, Range(0f, 1f)] private float tunnelInnerRadius = 0.3f;
    [Tooltip("Distance from the screen centre where the edges are fully dark.")]
    [SerializeField, Range(0.05f, 1.5f)] private float tunnelOuterRadius = 0.95f;
    [Tooltip("Flat dimming over the VN stage, behind the dialogue box.")]
    [SerializeField, Range(0f, 1f)] private float dialogueDim = 0.25f;
    [SerializeField] private Color tunnelColor = Color.black;
    [Tooltip("Speed of the slow 'breathing' of the tunnel.")]
    [SerializeField, Min(0f)] private float pulseSpeed = 0.7f;
    [SerializeField, Range(0f, 0.5f)] private float pulseAmount = 0.12f;

    [Header("Audio Distortion (FMOD)")]
    [Tooltip("FMOD snapshot held while active, e.g. snapshot:/Denial. Put distortion/low-pass on your buses inside it.")]
    [SerializeField] private string distortionSnapshot = "snapshot:/Denial";
    [Tooltip("Optional FMOD global parameter driven 0 -> 1 with the blend, e.g. Denial.")]
    [SerializeField] private string distortionGlobalParameter = "";

    [Header("Horror Music")]
    [Tooltip("Replaces every #bgm track while active. Leave empty to only use the per-track swaps.")]
    [SerializeField] private string horrorBgmEventPath = "";
    [Tooltip("Per-track replacements, checked before the catch-all above.")]
    [SerializeField] private List<BgmSwap> bgmSwaps = new();

    public bool IsActive => _active;
    public float Blend => _blend;
    public int DenialScore => _score;
    public int DenialThreshold => denialThreshold;
    public bool HasStory => storyController != null;

    /// <summary>Editor/test hook: sets the real story score (and so every effect that depends on it).</summary>
    public void SetDenialScore(int value)
    {
        if (storyController != null)
            storyController.SetDenialScore(value);
        else
            HandleDenialScoreChanged(value);
    }

    /// <summary>Editor/test hook: fires one glitch burst now, even below the threshold.</summary>
    public void TriggerGlitchBurstNow()
    {
        float min = Mathf.Max(0.02f, Mathf.Min(glitchDuration.x, glitchDuration.y));
        float max = Mathf.Max(min, Mathf.Max(glitchDuration.x, glitchDuration.y));
        StartBurst(UnityEngine.Random.Range(min, max), forced: true);
    }

    private int _score;
    private bool _active;
    private bool _hasEvaluated;
    private float _blend;

    // Post-processing
    private Volume _volume;
    private bool _ownsVolume;
    private ChromaticAberration _chromatic;
    private LensDistortion _lens;
    private float _chromaticBase;
    private float _lensBase;
    private UniversalAdditionalCameraData _cameraData;
    private bool _cameraPostWasOn;
    private bool _cameraPostForced;

    // Glitch bursts
    private float _nextBurstAt;
    private float _burstEndsAt;
    private float _burstStartedAt;
    private bool _burstForced;
    private float _nextWorldBarStep;
    private UIGlitchBars _worldBars;
    private Vector3 _shakeOffset;
    private bool _shakeApplied;

    // Tunnel vision overlay
    private VisualElement _dimLayer;
    private VisualElement _tunnelLayer;
    private Texture2D _tunnelTexture;

    // FMOD
    private EventInstance _snapshot;
    private float _lastParamValue = -1f;
    private bool _warnedSnapshot;
    private bool _warnedParam;

    private bool ForceActive
    {
        get
        {
#if UNITY_EDITOR
            return forceActiveInEditor;
#else
            return false;
#endif
        }
    }

    private void Awake()
    {
        if (storyController == null) storyController = GetComponent<VNStoryController>();
        if (stageView == null) stageView = GetComponent<VNStageView>();
        if (targetCamera == null) targetCamera = Camera.main;
        if (screenFader == null) screenFader = FindFirstObjectByType<ScreenFader>();
    }

    private void OnEnable()
    {
        if (storyController != null)
        {
            storyController.OnDenialScoreChanged += HandleDenialScoreChanged;
            storyController.BgmPathResolver = ResolveBgm;
            _score = storyController.DenialScore;
        }

        if (stageView != null)
        {
            stageView.OnViewReady += BuildOverlay;
            if (stageView.IsReady) BuildOverlay();
        }

        if (screenFader != null)
        {
            screenFader.OnOverlayRootReady += BuildWorldBars;
            if (screenFader.OverlayRoot != null) BuildWorldBars(screenFader.OverlayRoot);
        }

        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;

        SetupVolume();
        ScheduleNextBurst();
    }

    private void OnDisable()
    {
        if (storyController != null)
        {
            storyController.OnDenialScoreChanged -= HandleDenialScoreChanged;
            if (storyController.BgmPathResolver == (Func<string, string>)ResolveBgm)
                storyController.BgmPathResolver = null;
        }

        if (stageView != null)
            stageView.OnViewReady -= BuildOverlay;

        if (screenFader != null)
            screenFader.OnOverlayRootReady -= BuildWorldBars;
        _worldBars?.Detach();
        _worldBars = null;

        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;

        StopSnapshot();
        SetGlobalParameter(0f);
        RestoreCameraPost();
    }

    private void OnDestroy()
    {
        if (_ownsVolume && _volume != null)
        {
            if (_volume.sharedProfile != null) Destroy(_volume.sharedProfile);
            Destroy(_volume.gameObject);
        }

        if (_tunnelTexture != null) Destroy(_tunnelTexture);
    }

    private void OnValidate()
    {
        if (Application.isPlaying && _tunnelLayer != null)
            ApplyTunnelTexture();
    }

    // =========================================================================
    // STATE
    // =========================================================================

    private void HandleDenialScoreChanged(int score)
    {
        _score = score;
        // The very first evaluation (story start / save load) snaps instead of blending in.
        EvaluateActive(snap: !_hasEvaluated);
        _hasEvaluated = true;
    }

    private void EvaluateActive(bool snap)
    {
        bool shouldBeActive = _score > denialThreshold || ForceActive;
        if (shouldBeActive == _active)
        {
            if (snap) _blend = _active ? 1f : 0f;
            return;
        }

        _active = shouldBeActive;
        if (snap) _blend = _active ? 1f : 0f;

        if (_active)
        {
            ForceCameraPost();
            StartSnapshot();
            ScheduleNextBurst();
        }
        else
        {
            StopSnapshot();
        }

        // Swap the current track to/from its horror version
        if (storyController != null)
            storyController.RefreshBgm();
    }

    private string ResolveBgm(string requestedPath)
    {
        if (!_active || string.IsNullOrWhiteSpace(requestedPath))
            return requestedPath;

        foreach (BgmSwap swap in bgmSwaps)
        {
            if (!string.IsNullOrWhiteSpace(swap.horrorEventPath) &&
                string.Equals(swap.normalEventPath, requestedPath, StringComparison.OrdinalIgnoreCase))
            {
                return swap.horrorEventPath;
            }
        }

        return string.IsNullOrWhiteSpace(horrorBgmEventPath) ? requestedPath : horrorBgmEventPath;
    }

    private void Update()
    {
        // Lets the editor-only force toggle work live during play mode
        bool shouldBeActive = _score > denialThreshold || ForceActive;
        if (shouldBeActive != _active)
            EvaluateActive(snap: false);

        float target = _active ? 1f : 0f;
        _blend = blendDuration <= 0f
            ? target
            : Mathf.MoveTowards(_blend, target, Time.unscaledDeltaTime / blendDuration);

        UpdateBursts();
        ApplyVolume();
        ApplyTunnel();
        SetGlobalParameter(_blend);

        if (!_active && _blend <= 0f && !IsBursting)
            RestoreCameraPost();
    }

    // =========================================================================
    // GLITCH BURSTS
    // =========================================================================

    private void ScheduleNextBurst()
    {
        float min = Mathf.Max(0.5f, Mathf.Min(glitchInterval.x, glitchInterval.y));
        float max = Mathf.Max(min, Mathf.Max(glitchInterval.x, glitchInterval.y));
        _nextBurstAt = Time.unscaledTime + UnityEngine.Random.Range(min, max);
    }

    private void UpdateBursts()
    {
        if (glitchBursts && _blend >= 0.5f && Time.unscaledTime >= _nextBurstAt)
        {
            float min = Mathf.Max(0.02f, Mathf.Min(glitchDuration.x, glitchDuration.y));
            float max = Mathf.Max(min, Mathf.Max(glitchDuration.x, glitchDuration.y));
            StartBurst(UnityEngine.Random.Range(min, max), forced: false);
            ScheduleNextBurst();
        }

        UpdateWorldBurst();
    }

    private void StartBurst(float duration, bool forced)
    {
        _burstStartedAt = Time.unscaledTime;
        _burstEndsAt = Time.unscaledTime + duration;
        _burstForced = forced;
        _nextWorldBarStep = 0f;

        // In the VN the stage view runs its own layered glitch; in the world we draw bars + shake the camera.
        if (stageView != null && stageView.IsDialogueVisible)
            stageView.TriggerGlitch(duration);
    }

    private bool IsBursting => Time.unscaledTime < _burstEndsAt && (_active || _burstForced);

    // Strength of the current burst: hits hardest at the start, eases out
    private float BurstStrength
    {
        get
        {
            if (!IsBursting) return 0f;
            float length = Mathf.Max(0.0001f, _burstEndsAt - _burstStartedAt);
            return Mathf.Lerp(1f, 0.35f, (Time.unscaledTime - _burstStartedAt) / length);
        }
    }

    private void UpdateWorldBurst()
    {
        bool inWorld = stageView == null || !stageView.IsDialogueVisible;

        if (!IsBursting || !inWorld)
        {
            _worldBars?.Hide();
            _shakeOffset = Vector3.zero;
            return;
        }

        if (Time.unscaledTime < _nextWorldBarStep)
            return;

        float k = BurstStrength;
        _worldBars?.Randomize(k);
        _shakeOffset = new Vector3(
            UnityEngine.Random.Range(-1f, 1f),
            UnityEngine.Random.Range(-0.4f, 0.4f),
            0f) * (cameraShake * k);
        _nextWorldBarStep = Time.unscaledTime + UnityEngine.Random.Range(0.03f, 0.07f);
    }

    private void BuildWorldBars(VisualElement overlayRoot)
    {
        if (overlayRoot == null) return;
        if (_worldBars != null && _worldBars.IsAttachedTo(overlayRoot)) return;

        _worldBars?.Detach();
        _worldBars = new UIGlitchBars(overlayRoot);
    }

    // Shake is applied only while the camera renders, so it never leaks into the follow camera's smoothing.
    private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != targetCamera || _shakeOffset == Vector3.zero) return;
        cam.transform.position += cam.transform.rotation * _shakeOffset;
        _shakeApplied = true;
    }

    private void HandleEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != targetCamera || !_shakeApplied) return;
        cam.transform.position -= cam.transform.rotation * _shakeOffset;
        _shakeApplied = false;
    }

    // =========================================================================
    // URP VOLUME
    // =========================================================================

    private void SetupVolume()
    {
        if (_volume != null) return;

        if (distortionVolume != null)
        {
            _volume = distortionVolume;
            _ownsVolume = false;
        }
        else
        {
            var go = new GameObject("DenialDistortionVolume");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 50f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "DenialDistortion (runtime)";
            profile.Add<ChromaticAberration>(true).intensity.Override(chromaticAberration);
            profile.Add<FilmGrain>(true).intensity.Override(filmGrain);
            profile.Add<LensDistortion>(true).intensity.Override(lensDistortion);
            profile.Add<ColorAdjustments>(true).saturation.Override(saturation);
            profile.Add<Vignette>(true).intensity.Override(worldVignette);
            _volume.sharedProfile = profile;
            _ownsVolume = true;
        }

        _volume.weight = 0f;
        VolumeProfile p = _volume.sharedProfile;
        if (p != null)
        {
            if (p.TryGet(out _chromatic)) _chromaticBase = _chromatic.intensity.value;
            if (p.TryGet(out _lens)) _lensBase = _lens.intensity.value;
        }
    }

    private void ApplyVolume()
    {
        if (_volume == null) return;

        _volume.weight = _blend;

        // Live-tweakable when using the runtime volume
        if (_ownsVolume)
        {
            _chromaticBase = chromaticAberration;
            _lensBase = lensDistortion;
        }

        // Bursts briefly push the distortion harder (a forced test burst shows it even below the threshold)
        bool burst = IsBursting;
        if (burst && _burstForced)
            _volume.weight = Mathf.Max(_volume.weight, 1f);

        if (_chromatic != null)
            _chromatic.intensity.value = burst ? 1f : _chromaticBase;
        if (_lens != null)
            _lens.intensity.value = burst ? Mathf.Clamp(_lensBase * 3f - 0.15f, -1f, 1f) : _lensBase;

        if (burst) ForceCameraPost();
    }

    private void ForceCameraPost()
    {
        if (_cameraPostForced) return;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        _cameraData = targetCamera.GetUniversalAdditionalCameraData();
        if (_cameraData == null) return;

        _cameraPostWasOn = _cameraData.renderPostProcessing;
        _cameraData.renderPostProcessing = true;
        _cameraPostForced = true;
    }

    private void RestoreCameraPost()
    {
        if (!_cameraPostForced) return;
        if (_cameraData != null)
            _cameraData.renderPostProcessing = _cameraPostWasOn;
        _cameraPostForced = false;
    }

    // =========================================================================
    // TUNNEL VISION OVERLAY (inside the VN stage, under the dialogue box & top bar)
    // =========================================================================

    private void BuildOverlay()
    {
        VisualElement root = stageView != null ? stageView.RootElement : null;
        if (root == null) return;

        root.Q<VisualElement>("denial-dim")?.RemoveFromHierarchy();
        root.Q<VisualElement>("denial-tunnel")?.RemoveFromHierarchy();

        VisualElement anchor = root.Q<VisualElement>("advance-surface") ?? root.Q<VisualElement>("layer-hud");
        VisualElement parent = anchor != null ? anchor.parent : root.Q<VisualElement>("vn-root") ?? root;
        int index = anchor != null ? parent.IndexOf(anchor) : parent.childCount;

        _dimLayer = CreateFullscreenLayer("denial-dim");
        _dimLayer.style.backgroundColor = tunnelColor;

        _tunnelLayer = CreateFullscreenLayer("denial-tunnel");
        ApplyTunnelTexture();

        parent.Insert(index, _tunnelLayer);
        parent.Insert(index, _dimLayer);
        ApplyTunnel();
    }

    private static VisualElement CreateFullscreenLayer(string name)
    {
        var ve = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
        ve.style.position = Position.Absolute;
        ve.style.left = 0;
        ve.style.top = 0;
        ve.style.right = 0;
        ve.style.bottom = 0;
        ve.style.opacity = 0f;
        return ve;
    }

    private void ApplyTunnelTexture()
    {
        const int size = 256;
        if (_tunnelTexture == null)
        {
            _tunnelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "DenialTunnelVignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
        }

        float inner = Mathf.Min(tunnelInnerRadius, tunnelOuterRadius - 0.01f);
        float outer = tunnelOuterRadius;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 0 at the centre, 1 at the corners; stretched to the screen so it follows its aspect
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v) / 1.41421356f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, d));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        _tunnelTexture.SetPixels32(pixels);
        _tunnelTexture.Apply(false, false);

        if (_tunnelLayer != null)
        {
            _tunnelLayer.style.backgroundImage = new StyleBackground(Background.FromTexture2D(_tunnelTexture));
            _tunnelLayer.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            _tunnelLayer.style.unityBackgroundImageTintColor = tunnelColor;
        }

        if (_dimLayer != null)
            _dimLayer.style.backgroundColor = tunnelColor;
    }

    private void ApplyTunnel()
    {
        if (_tunnelLayer == null || _dimLayer == null) return;

        float pulse = pulseAmount > 0f ? Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) * pulseAmount : 0f;
        float burstKick = IsBursting && _active ? 0.15f : 0f;

        _tunnelLayer.style.opacity = Mathf.Clamp01(_blend * tunnelStrength * (1f + pulse) + burstKick * _blend);
        _dimLayer.style.opacity = Mathf.Clamp01(_blend * dialogueDim);
    }

    // =========================================================================
    // FMOD
    // =========================================================================

    private void StartSnapshot()
    {
        if (_snapshot.isValid() || string.IsNullOrWhiteSpace(distortionSnapshot) || !RuntimeManager.IsInitialized)
            return;

        try
        {
            _snapshot = RuntimeManager.CreateInstance(distortionSnapshot);
            _snapshot.start();
        }
        catch (EventNotFoundException)
        {
            if (!_warnedSnapshot)
                Debug.LogWarning($"[VNDenialEffects] FMOD snapshot not found: '{distortionSnapshot}'. Create it in FMOD Studio or clear the field.");
            _warnedSnapshot = true;
        }
    }

    private void StopSnapshot()
    {
        if (!_snapshot.isValid()) return;
        _snapshot.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        _snapshot.release();
        _snapshot.clearHandle();
    }

    private void SetGlobalParameter(float value)
    {
        if (string.IsNullOrWhiteSpace(distortionGlobalParameter) || !RuntimeManager.IsInitialized)
            return;
        if (Mathf.Abs(value - _lastParamValue) < 0.005f)
            return;

        _lastParamValue = value;
        FMOD.RESULT result = RuntimeManager.StudioSystem.setParameterByName(distortionGlobalParameter, value);
        if (result != FMOD.RESULT.OK && !_warnedParam)
        {
            Debug.LogWarning($"[VNDenialEffects] FMOD global parameter '{distortionGlobalParameter}' could not be set ({result}).");
            _warnedParam = true;
        }
    }
}
