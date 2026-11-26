using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

public class VNSceneFlowController : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private ScreenFader screenFader;
    [SerializeField] private VNStoryController storyController;
    [SerializeField] private VNStageView stageView;
    [SerializeField] private MedinaLevelManager levelManager;

    [Header("2.5D -> First-Person VN Camera Transition")]
    [SerializeField] private Camera mainCamera;
    [Tooltip("The diorama follow camera. It is disabled during the swoop/VN so its LateUpdate doesn't overwrite the camera pose.")]
    [SerializeField] private MedinaStorybookCamera storybookCamera;
    [SerializeField] private Transform playerTransform;
    [SerializeField, Min(0.05f)] private float cameraToFirstPersonDuration = 0.65f;
    [SerializeField] private float eyeHeightOffset = 1.45f;
    [SerializeField] private float forwardStepOffset = 0.25f;
    [SerializeField] private float firstPersonFov = 55f;
    [SerializeField] private bool switchOrthoToPerspectiveInVn = true;
    [Tooltip("If true, fades to black during the swoop before opening the VN. If false, opens the VN directly over the first-person view.")]
    [SerializeField] private bool fadeToBlackBeforeVn = false;
    [Tooltip("How far through the swoop the fade to black starts (0 = as the camera starts moving, 1 = once it reaches the player's eyes). Lower values black out further from the player.")]
    [SerializeField, Range(0f, 1f)] private float fadeOutStartPoint = 0.55f;
    [Tooltip("Length of the fade to black that runs during the swoop.")]
    [SerializeField, Min(0.05f)] private float worldToVnFadeDuration = 0.3f;
    [Tooltip("Length of the fade from black into the VN.")]
    [SerializeField, Min(0.05f)] private float worldToVnFadeInDuration = 0.5f;
    [Tooltip("Once the screen is fully black, skip the rest of the swoop instead of finishing it unseen.")]
    [SerializeField] private bool endSwoopOnceBlack = true;

    [Header("Boot VN Sequence")]
    [SerializeField] private string openingVnKnot = "opening_phone_call";
    [SerializeField] private bool playOpeningKnotOnContinue = false;
    [Tooltip("Editor testing only: when ticked, New Game skips the opening VN and drops straight into the 2.5D world. Ignored in builds.")]
    [SerializeField] private bool skipOpeningInEditor = false;

    private bool ShouldPlayOpening
    {
        get
        {
            if (string.IsNullOrWhiteSpace(openingVnKnot)) return false;
#if UNITY_EDITOR
            if (skipOpeningInEditor) return false;
#endif
            return true;
        }
    }

    [Header("First World Entry Line")]
    [Tooltip("On a new save, show a single line in the VN dialogue box (transparent background) the first time the player reaches the 2.5D world. Movement stays locked until it's dismissed.")]
    [SerializeField] private bool playWorldIntroLine = true;
    [Tooltip("Optional nameplate. Leave empty for no nameplate.")]
    [SerializeField] private string worldIntroSpeaker = "";
    [SerializeField, TextArea(2, 4)] private string worldIntroLine = "Ah... Madinat Rabat... It's been a while...";
    [Tooltip("Pause after the world fades in before the line appears.")]
    [SerializeField, Min(0f)] private float worldIntroDelay = 0.25f;

    private bool _hasSeenWorldIntro;

    private bool WillPlayWorldIntro =>
        playWorldIntroLine && !_hasSeenWorldIntro && !string.IsNullOrWhiteSpace(worldIntroLine) &&
        storyController != null && stageView != null;

    [Header("Fade Timings")]
    [SerializeField, Min(0.05f)] private float bootFadeInDuration = 0.65f;
    [SerializeField, Min(0.05f)] private float vnToWorldFadeOutDuration = 0.55f;
    [SerializeField, Min(0.05f)] private float vnToWorldFadeInDuration = 0.65f;
    [SerializeField, Min(0f)] private float blackHoldBetweenFades = 0.15f;

    [Header("Checkpoints")]
    [SerializeField] private bool saveCheckpointOnEnteringWorld = true;

    private bool _bootExecuted;
    private bool _isTransitioning;
    private CancellationTokenSource _flowCts;

    // Saved 2.5D camera state so VN -> 2.5D restores the exact diorama angle
    private bool _hasSavedCameraPose;
    private Vector3 _savedCamPos;
    private Quaternion _savedCamRot;
    private float _savedCamFov;
    private bool _savedCamOrtho;
    private float _savedCamOrthoSize;

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (storybookCamera == null && mainCamera != null)
            storybookCamera = mainCamera.GetComponent<MedinaStorybookCamera>();

        if (levelManager != null)
            levelManager.SetExplorationActive(false);

        if (screenFader != null)
            screenFader.SetBlackImmediate();
    }

    private void OnEnable()
    {
        if (stageView != null)
            stageView.OnViewReady += HandleStageReady;

        if (storyController != null)
        {
            storyController.OnDialogueEnded += HandleDialogueEnded;
            storyController.OnCapturingSaveData += HandleCapturingSaveData;
            storyController.OnSaveDataLoaded += HandleSaveDataLoaded;
        }

        if (stageView != null && stageView.IsReady && !_bootExecuted)
            HandleStageReady();
    }

    private void OnDisable()
    {
        if (_flowCts != null)
        {
            _flowCts.Cancel();
            _flowCts.Dispose();
            _flowCts = null;
        }

        if (stageView != null)
            stageView.OnViewReady -= HandleStageReady;

        if (storyController != null)
        {
            storyController.OnDialogueEnded -= HandleDialogueEnded;
            storyController.OnCapturingSaveData -= HandleCapturingSaveData;
            storyController.OnSaveDataLoaded -= HandleSaveDataLoaded;
        }
    }

    private void HandleCapturingSaveData(VNSaveData data)
    {
        if (data != null) data.hasSeenWorldIntro = _hasSeenWorldIntro;
    }

    private void HandleSaveDataLoaded(VNSaveData data)
    {
        if (data != null) _hasSeenWorldIntro = data.hasSeenWorldIntro;
    }

    // =========================================================================
    // 1. BOOT FLOW (MainMenu -> Opening VN / Saved State)
    // =========================================================================

    private void HandleStageReady()
    {
        if (_bootExecuted) return;
        _bootExecuted = true;

        _flowCts = new CancellationTokenSource();
        _ = RunBootSequenceAsync(_flowCts.Token);
    }

    private async Awaitable RunBootSequenceAsync(CancellationToken ct)
    {
        await Awaitable.NextFrameAsync(ct);

        VNLaunchMode mode = VNSessionBridge.LaunchMode;
        VNSaveData loadedSave = null;

        if (mode == VNLaunchMode.ContinueLatest && VNSaveManager.Instance != null)
            loadedSave = VNSaveManager.Instance.GetLatestSave();
        else if (mode == VNLaunchMode.LoadSpecificSlot && VNSaveManager.Instance != null)
            loadedSave = VNSaveManager.Instance.LoadFromSlot(VNSessionBridge.TargetSlotId);

        VNSessionBridge.LaunchMode = VNLaunchMode.NewGame;
        _hasSeenWorldIntro = false; // a loaded save overrides this via OnSaveDataLoaded
        bool enteringWorld = false;

        if (loadedSave != null)
        {
            storyController.LoadFromSaveData(loadedSave);

            if (playOpeningKnotOnContinue && ShouldPlayOpening)
            {
                stageView.SetDialogueVisible(true);
                if (levelManager != null) levelManager.SetExplorationActive(false);
                storyController.JumpToKnot(openingVnKnot, clearStageFirst: true);
            }
            else
            {
                bool isMidVn = !loadedSave.inWorld &&
                               (loadedSave.isWaitingForChoice || !string.IsNullOrEmpty(loadedSave.dialogue));
                stageView.SetDialogueVisible(isMidVn);
                enteringWorld = !isMidVn;
                if (levelManager != null)
                {
                    if (isMidVn) levelManager.SetExplorationActive(false);
                    else levelManager.EnterExploration(enableControls: !WillPlayWorldIntro);
                }
            }
        }
        else
        {
            storyController.EnsureStoryInitialized();

            if (ShouldPlayOpening)
            {
                stageView.SetDialogueVisible(true);
                if (levelManager != null) levelManager.SetExplorationActive(false);
                storyController.JumpToKnot(openingVnKnot, clearStageFirst: true);
            }
            else
            {
                stageView.SetDialogueVisible(false);
                enteringWorld = true;
                if (levelManager != null) levelManager.EnterExploration(enableControls: !WillPlayWorldIntro);
            }
        }

        // Give UI Toolkit one frame to lay out the VN stage (background, characters)
        // before the curtain lifts, so the 2.5D world never peeks through.
        await Awaitable.NextFrameAsync(ct);

        if (screenFader != null)
            await screenFader.FadeInFromBlackAsync(bootFadeInDuration);

        if (enteringWorld)
            await PlayWorldIntroIfNeededAsync();
    }

    // =========================================================================
    // 1a. FIRST WORLD ENTRY LINE (transparent VN box, movement locked until dismissed)
    // =========================================================================

    private async Awaitable PlayWorldIntroIfNeededAsync()
    {
        if (!WillPlayWorldIntro)
            return;

        if (levelManager != null)
            levelManager.SetExplorationActive(false);

        if (worldIntroDelay > 0f)
            await Awaitable.WaitForSecondsAsync(worldIntroDelay);

        // No background or characters: the dialogue box floats over the 2.5D world.
        stageView.ClearAllCharacters();
        stageView.HideEventCG(instant: true);
        stageView.SetBackground(null, instant: true);
        stageView.SetDialogueVisible(true);

        var finished = new AwaitableCompletionSource();
        storyController.PlayNarrationLine(worldIntroSpeaker, worldIntroLine, () => finished.TrySetResult());
        await finished.Awaitable;

        _hasSeenWorldIntro = true;
        stageView.SetDialogueVisible(false);

        if (levelManager != null)
            levelManager.SetExplorationActive(true);

        // Record that the intro was seen so Continue doesn't replay it.
        if (saveCheckpointOnEnteringWorld)
            storyController.SaveCheckpoint();
    }

    // =========================================================================
    // 1b. IN-GAME LOAD (fade out, reload scene, boot flow restores the slot)
    // =========================================================================

    public void LoadSlotAndReloadScene(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId) || _isTransitioning)
            return;

        _ = LoadSlotAndReloadSceneAsync(slotId);
    }

    private async Awaitable LoadSlotAndReloadSceneAsync(string slotId)
    {
        _isTransitioning = true;
        Time.timeScale = 1f;

        if (levelManager != null)
            levelManager.SetExplorationActive(false);

        if (screenFader != null)
            await screenFader.FadeOutToBlackAsync(vnToWorldFadeOutDuration);

        if (storyController != null)
            storyController.StopBGM(instant: true);

        MedinaLevelManager.ClearSavedReturnPosition();
        VNSessionBridge.CachedInkStateJson = string.Empty;
        VNSessionBridge.TargetSlotId = slotId;
        VNSessionBridge.LaunchMode = VNLaunchMode.LoadSpecificSlot;

        await SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().name);
    }

    // =========================================================================
    // 2. 2.5D -> FIRST-PERSON CAMERA SWOOP -> VN
    // =========================================================================

    public void StartWorldToVnTransition(string knotName, Transform focusTarget = null, bool clearStageFirst = true)
    {
        if (_isTransitioning || string.IsNullOrWhiteSpace(knotName))
            return;

        _ = TransitionWorldToVnAsync(knotName, focusTarget, clearStageFirst);
    }

    public async Awaitable TransitionWorldToVnAsync(string knotName, Transform focusTarget = null, bool clearStageFirst = true)
    {
        _isTransitioning = true;

        try
        {
            if (levelManager != null)
                levelManager.SetExplorationActive(false);

            // Cache 2.5D diorama camera state and swoop to player eye-level
            if (mainCamera == null || playerTransform == null)
                Debug.LogWarning("[VNSceneFlowController] Camera swoop skipped: mainCamera or playerTransform is not assigned.");

            bool wantsFade = fadeToBlackBeforeVn && screenFader != null;
            Awaitable fadeOut = null;

            if (mainCamera != null && playerTransform != null && cameraToFirstPersonDuration > 0f)
            {
                CacheCameraPose();

                // The follow camera re-applies its diorama pose every LateUpdate; pause it so the swoop is visible.
                SetFollowCameraEnabled(false);

                Vector3 startPos = mainCamera.transform.position;
                Quaternion startRot = mainCamera.transform.rotation;
                float startFov = mainCamera.fieldOfView;

                Vector3 lookDir = focusTarget != null
                    ? (focusTarget.position - playerTransform.position)
                    : playerTransform.forward;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude < 0.0001f)
                    lookDir = playerTransform.forward;
                lookDir.Normalize();

                Vector3 targetPos = playerTransform.position + Vector3.up * eyeHeightOffset + lookDir * forwardStepOffset;
                Vector3 aimPoint = focusTarget != null
                    ? focusTarget.position + Vector3.up * eyeHeightOffset
                    : targetPos + lookDir * 2f;
                Quaternion targetRot = Quaternion.LookRotation((aimPoint - targetPos).normalized, Vector3.up);

                if (mainCamera.orthographic && switchOrthoToPerspectiveInVn)
                    mainCamera.orthographic = false;

                float elapsed = 0f;
                while (elapsed < cameraToFirstPersonDuration)
                {
                    await Awaitable.NextFrameAsync();
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / cameraToFirstPersonDuration);
                    float eased = t * t * (3f - 2f * t);

                    mainCamera.transform.SetPositionAndRotation(
                        Vector3.LerpUnclamped(startPos, targetPos, eased),
                        Quaternion.SlerpUnclamped(startRot, targetRot, eased));

                    if (!mainCamera.orthographic)
                        mainCamera.fieldOfView = Mathf.LerpUnclamped(startFov, firstPersonFov, eased);

                    // Start blacking out part-way through the swoop, while the camera is still travelling.
                    if (wantsFade && fadeOut == null && t >= fadeOutStartPoint)
                        fadeOut = screenFader.FadeOutToBlackAsync(worldToVnFadeDuration);

                    if (endSwoopOnceBlack && fadeOut != null && screenFader.CurrentAlpha >= 0.999f)
                        break;
                }

                mainCamera.transform.SetPositionAndRotation(targetPos, targetRot);
                if (!mainCamera.orthographic)
                    mainCamera.fieldOfView = firstPersonFov;
            }

            if (wantsFade)
            {
                fadeOut ??= screenFader.FadeOutToBlackAsync(worldToVnFadeDuration);
                await fadeOut;
            }

            if (stageView != null)
                stageView.SetDialogueVisible(true);

            if (storyController != null)
                storyController.JumpToKnot(knotName, clearStageFirst);

            if (wantsFade)
            {
                await Awaitable.NextFrameAsync();
                await screenFader.FadeInFromBlackAsync(worldToVnFadeInDuration);
            }
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    // =========================================================================
    // 3. VN -> 2.5D WORLD (Fades out, restores 2.5D camera, fades in)
    // =========================================================================

    private void HandleDialogueEnded()
    {
        if (_isTransitioning) return;
        _ = TransitionFromVnToWorldAsync();
    }

    private async Awaitable TransitionFromVnToWorldAsync()
    {
        _isTransitioning = true;

        try
        {
            if (screenFader != null)
                await screenFader.FadeOutToBlackAsync(vnToWorldFadeOutDuration);

            if (stageView != null)
            {
                stageView.ClearAllCharacters();
                stageView.HideEventCG(instant: true);
                stageView.SetBackground(null, instant: true);
                stageView.SetDialogueVisible(false);
            }

            RestoreCameraPose();
            SetFollowCameraEnabled(true);

            if (levelManager != null)
                levelManager.EnterExploration(enableControls: !WillPlayWorldIntro);

            if (saveCheckpointOnEnteringWorld && storyController != null)
                storyController.SaveCheckpoint();

            if (blackHoldBetweenFades > 0f)
                await Awaitable.WaitForSecondsAsync(blackHoldBetweenFades);

            if (screenFader != null)
                await screenFader.FadeInFromBlackAsync(vnToWorldFadeInDuration);

            await PlayWorldIntroIfNeededAsync();
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    private void CacheCameraPose()
    {
        if (mainCamera == null || _hasSavedCameraPose) return;

        _hasSavedCameraPose = true;
        _savedCamPos = mainCamera.transform.position;
        _savedCamRot = mainCamera.transform.rotation;
        _savedCamFov = mainCamera.fieldOfView;
        _savedCamOrtho = mainCamera.orthographic;
        _savedCamOrthoSize = mainCamera.orthographicSize;
    }

    private void SetFollowCameraEnabled(bool enabled)
    {
        if (storybookCamera == null) return;

        storybookCamera.enabled = enabled;
        if (enabled)
            storybookCamera.SnapToTarget();
    }

    private void RestoreCameraPose()
    {
        if (mainCamera == null || !_hasSavedCameraPose) return;

        mainCamera.transform.SetPositionAndRotation(_savedCamPos, _savedCamRot);
        mainCamera.orthographic = _savedCamOrtho;
        mainCamera.orthographicSize = _savedCamOrthoSize;
        mainCamera.fieldOfView = _savedCamFov;
        _hasSavedCameraPose = false;
    }
}