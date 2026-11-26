using System.Threading;
using UnityEngine;

public class VNSceneFlowController : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private ScreenFader screenFader;
    [SerializeField] private VNStoryController storyController;
    [SerializeField] private VNStageView stageView;
    [SerializeField] private MedinaLevelManager levelManager;

    [Header("2.5D -> First-Person VN Camera Transition")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Transform playerTransform;
    [SerializeField, Min(0.05f)] private float cameraToFirstPersonDuration = 0.65f;
    [SerializeField] private float eyeHeightOffset = 1.45f;
    [SerializeField] private float forwardStepOffset = 0.25f;
    [SerializeField] private float firstPersonFov = 55f;
    [SerializeField] private bool switchOrthoToPerspectiveInVn = true;
    [Tooltip("If true, fades to black after the camera reaches first-person before opening the VN. If false, opens the VN directly over the first-person view.")]
    [SerializeField] private bool fadeToBlackBeforeVn = false;
    [SerializeField, Min(0.05f)] private float worldToVnFadeDuration = 0.3f;

    [Header("Boot VN Sequence")]
    [SerializeField] private string openingVnKnot = "opening_phone_call";
    [SerializeField] private bool playOpeningKnotOnContinue = false;

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
            storyController.OnDialogueEnded += HandleDialogueEnded;

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
            storyController.OnDialogueEnded -= HandleDialogueEnded;
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

        if (loadedSave != null)
        {
            storyController.LoadFromSaveData(loadedSave);

            if (playOpeningKnotOnContinue && !string.IsNullOrWhiteSpace(openingVnKnot))
            {
                stageView.SetDialogueVisible(true);
                if (levelManager != null) levelManager.SetExplorationActive(false);
                storyController.JumpToKnot(openingVnKnot, clearStageFirst: true);
            }
            else
            {
                bool isMidVn = loadedSave.isWaitingForChoice || !string.IsNullOrEmpty(loadedSave.dialogue);
                stageView.SetDialogueVisible(isMidVn);
                if (levelManager != null) levelManager.SetExplorationActive(!isMidVn);
            }
        }
        else
        {
            storyController.EnsureStoryInitialized();

            if (!string.IsNullOrWhiteSpace(openingVnKnot))
            {
                stageView.SetDialogueVisible(true);
                if (levelManager != null) levelManager.SetExplorationActive(false);
                storyController.JumpToKnot(openingVnKnot, clearStageFirst: true);
            }
            else
            {
                stageView.SetDialogueVisible(false);
                if (levelManager != null) levelManager.SetExplorationActive(true);
            }
        }

        if (screenFader != null)
            await screenFader.FadeInFromBlackAsync(bootFadeInDuration);
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
            if (mainCamera != null && playerTransform != null && cameraToFirstPersonDuration > 0f)
            {
                CacheCameraPose();

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
                }

                mainCamera.transform.SetPositionAndRotation(targetPos, targetRot);
                if (!mainCamera.orthographic)
                    mainCamera.fieldOfView = firstPersonFov;
            }

            if (fadeToBlackBeforeVn && screenFader != null)
                await screenFader.FadeOutToBlackAsync(worldToVnFadeDuration);

            if (stageView != null)
                stageView.SetDialogueVisible(true);

            if (storyController != null)
                storyController.JumpToKnot(knotName, clearStageFirst);

            if (fadeToBlackBeforeVn && screenFader != null)
                await screenFader.FadeInFromBlackAsync(worldToVnFadeDuration);
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

            if (levelManager != null)
                levelManager.SetExplorationActive(true);

            if (saveCheckpointOnEnteringWorld && storyController != null)
                storyController.SaveCheckpoint();

            if (blackHoldBetweenFades > 0f)
                await Awaitable.WaitForSecondsAsync(blackHoldBetweenFades);

            if (screenFader != null)
                await screenFader.FadeInFromBlackAsync(vnToWorldFadeInDuration);
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