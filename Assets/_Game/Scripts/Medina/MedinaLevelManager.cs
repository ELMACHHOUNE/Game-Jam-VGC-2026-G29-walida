using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using LitMotion;
using Walida.Audio;

public class MedinaLevelManager : MonoBehaviour
{
    private static bool _hasSavedReturnPosition;
    private static Vector3 _savedReturnPosition;

    [Header("References")]
    [SerializeField] private MedinaPlayerController playerController;
    [SerializeField] private MedinaInteractionController interactionController;
    [SerializeField] private MedinaStorybookCamera storybookCamera;
    [SerializeField] private VNStoryController storyController;
    [SerializeField] private VNStageView stageView;
    [SerializeField] private VNSceneFlowController sceneFlowController;

    [Header("Default Minigame Scenes")]
    [SerializeField] private string photographySceneName = "PhotographingMinigame";
    [SerializeField] private string audioRecordingSceneName = "AudioRecordingMinigame";

    [Header("Exploration Audio (Optional)")]
    [SerializeField] private AudioSource explorationAmbienceSource;
    [SerializeField, Range(0f, 1f)] private float explorationAmbienceVolume = 0.5f;
    [SerializeField] private float ambienceFadeDuration = 0.45f;

    [Header("Settings")]
    [SerializeField] private bool autoBindSceneInteractables = true;
    [SerializeField] private bool restoreSavedPositionOnStart = true;
    [SerializeField] private bool lockExplorationDuringDialogue = true;
    [SerializeField] private bool clearStageOnDialogueJump = true;

    [Header("Story Progress & Endings")]
    [Tooltip("Ink knot played after every story NPC has been talked to, when the denial score is BELOW the threshold.")]
    [SerializeField] private string acceptanceEndingKnot = "";
    [Tooltip("Ink knot played after every story NPC has been talked to, when the denial score is ON or ABOVE the threshold.")]
    [SerializeField] private string denialEndingKnot = "";
    [Tooltip("Denial ending when the denial score is >= this value, acceptance ending when it is lower.")]
    [SerializeField] private int denialEndingThreshold = 5;
    [Tooltip("Scene loaded once the ending has finished.")]
    [SerializeField] private string sceneAfterEnding = "MainMenu";

    [Header("Events")]
    [SerializeField] private UnityEvent<string> onDialogueRequested;
    [SerializeField] private UnityEvent<string> onSceneRequested;
    [Tooltip("Raised when the ending starts: true = denial ending, false = acceptance ending.")]
    [SerializeField] private UnityEvent<bool> onEndingStarted;

    private MedinaInteractable[] _sceneInteractables;
    private MotionHandle _ambienceTween;

    // Story NPC progress (saved): which NPC conversations are finished, and whether the ending has begun
    private readonly System.Collections.Generic.HashSet<string> _completedNpcIds = new(System.StringComparer.OrdinalIgnoreCase);
    private MedinaInteractable _activeNpc;
    private bool _endingStarted;

    public bool IsEndingInProgress => _endingStarted;
    public string SceneAfterEnding => sceneAfterEnding;

    public bool IsExplorationPaused { get; private set; }
    public string LastTriggeredAction { get; private set; }

    private void Awake()
    {
        if (storyController == null)
            storyController = FindFirstObjectByType<VNStoryController>();

        if (stageView == null)
            stageView = FindFirstObjectByType<VNStageView>();

        if (sceneFlowController == null)
            sceneFlowController = FindFirstObjectByType<VNSceneFlowController>();
    }

    private bool HasFlowController => sceneFlowController != null && sceneFlowController.isActiveAndEnabled;

    private void OnEnable()
    {
        if (storyController != null)
        {
            storyController.OnDialogueEnded += HandleDialogueEnded;
            storyController.OnCapturingSaveData += HandleCapturingSaveData;
            storyController.OnSaveDataLoaded += HandleSaveDataLoaded;
        }

        if (autoBindSceneInteractables)
        {
            _sceneInteractables = FindObjectsByType<MedinaInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < _sceneInteractables.Length; i++)
                _sceneInteractables[i].OnInteractableTriggered += HandleInteractable;
        }
    }

    private void OnDisable()
    {
        if (storyController != null)
        {
            storyController.OnDialogueEnded -= HandleDialogueEnded;
            storyController.OnCapturingSaveData -= HandleCapturingSaveData;
            storyController.OnSaveDataLoaded -= HandleSaveDataLoaded;
        }

        if (_sceneInteractables != null)
        {
            for (int i = 0; i < _sceneInteractables.Length; i++)
            {
                if (_sceneInteractables[i] != null)
                    _sceneInteractables[i].OnInteractableTriggered -= HandleInteractable;
            }
        }

        if (_ambienceTween.IsActive())
            _ambienceTween.Cancel();
    }

    private void Start()
    {
        if (FindAnyObjectByType<MedinaAmbienceController>() == null)
        {
            gameObject.AddComponent<MedinaAmbienceController>();
        }

        if (restoreSavedPositionOnStart && _hasSavedReturnPosition && playerController != null)
        {
            playerController.TeleportTo(_savedReturnPosition);
            if (storybookCamera != null)
                storybookCamera.SnapToTarget();
        }

        // The flow controller owns the boot sequence (opening VN / save restore / fade-in);
        // toggling the stage or exploration here would fight it and flash the world.
        if (HasFlowController)
            return;

        bool restoringVnSave = VNSessionBridge.LaunchMode != VNLaunchMode.NewGame &&
                               VNSessionBridge.HasAnySaveData();

        if (restoringVnSave && lockExplorationDuringDialogue)
        {
            SetExplorationActive(false);
        }
        else
        {
            if (stageView != null)
                stageView.SetDialogueVisible(false);

            SetExplorationActive(true);
            StartExplorationAmbience();
        }
    }

    public void HandleInteractable(MedinaInteractable interactable)
    {
        if (interactable == null)
            return;

        switch (interactable.Kind)
        {
            case MedinaInteractableKind.Dialogue:
                if (interactable.IsConversationDone && interactable.OneTimeConversation)
                    break; // already talked to: can't start it again
                _activeNpc = interactable;
                TriggerDialogue(interactable.DialogueKnotName, interactable.DisplayTitle, interactable.transform);
                break;

            case MedinaInteractableKind.PhotographyMinigame:
                string photoScene = string.IsNullOrWhiteSpace(interactable.TargetSceneName)
                    ? photographySceneName
                    : interactable.TargetSceneName;
                LaunchMinigameScene(photoScene);
                break;

            case MedinaInteractableKind.AudioRecordingMinigame:
                string audioScene = string.IsNullOrWhiteSpace(interactable.TargetSceneName)
                    ? audioRecordingSceneName
                    : interactable.TargetSceneName;
                LaunchMinigameScene(audioScene);
                break;

            case MedinaInteractableKind.CustomEvent:
                LastTriggeredAction = $"custom:{interactable.InteractableId}";
                MedinaExitDoor exitDoor = interactable.GetComponent<MedinaExitDoor>();
                if (exitDoor != null)
                {
                    exitDoor.HandleDoorInteraction(interactable);
                }
                else
                {
                    Debug.Log($"[MedinaLevelManager] Triggered custom interactable '{interactable.DisplayTitle}'");
                }
                break;
        }
    }

    public void TriggerDialogue(string knotName, string sourceTitle = "", Transform focusTarget = null)
    {
        LastTriggeredAction = $"dialogue:{knotName}";

        if (lockExplorationDuringDialogue)
            SetExplorationActive(false);

        FadeExplorationAmbience(0f);

        if (HasFlowController && !string.IsNullOrWhiteSpace(knotName))
        {
            sceneFlowController.StartWorldToVnTransition(knotName, focusTarget, clearStageOnDialogueJump);
        }
        else
        {
            if (stageView != null)
                stageView.SetDialogueVisible(true);

            if (storyController != null && !string.IsNullOrWhiteSpace(knotName))
                storyController.JumpToKnot(knotName, clearStageOnDialogueJump);
        }

        onDialogueRequested?.Invoke(knotName);
    }

    private void HandleDialogueEnded()
    {
        if (storyController != null)
            storyController.StopBGM(instant: false);

        // The flow controller fades to black, tears the stage down, restores the camera
        // and then calls EnterExploration(). Hiding the stage here would reveal the world early.
        // It also calls NotifyDialogueFinished() itself, before deciding whether the ending plays.
        if (HasFlowController)
            return;

        NotifyDialogueFinished();

        if (stageView != null)
        {
            stageView.ClearAllCharacters();
            stageView.HideEventCG(instant: true);
            stageView.HideChoices();
            stageView.SetDialogueVisible(false);
        }

        FadeExplorationAmbience(explorationAmbienceVolume);
        ResumeExploration();
    }

    // =========================================================================
    // STORY NPC PROGRESS & ENDINGS
    // =========================================================================

    private MedinaInteractable[] SceneInteractables =>
        _sceneInteractables ??= FindObjectsByType<MedinaInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    /// <summary>
    /// Called when a VN conversation has been played to the end. If it was started from a story NPC,
    /// that NPC is marked done (its prompt disappears and it can't be talked to again).
    /// </summary>
    public void NotifyDialogueFinished()
    {
        MedinaInteractable npc = _activeNpc;
        _activeNpc = null;
        if (npc == null || npc.Kind != MedinaInteractableKind.Dialogue || string.IsNullOrWhiteSpace(npc.InteractableId))
            return;

        _completedNpcIds.Add(npc.InteractableId);
        npc.MarkConversationDone();
    }

    /// <summary>True once every story NPC in the level has been talked to.</summary>
    public bool AllStoryNpcsDone
    {
        get
        {
            int storyNpcs = 0;
            foreach (MedinaInteractable i in SceneInteractables)
            {
                if (i == null || !i.IsStoryNpc) continue;
                storyNpcs++;
                if (!_completedNpcIds.Contains(i.InteractableId)) return false;
            }
            return storyNpcs > 0;
        }
    }

    /// <summary>
    /// When every story NPC is done and no ending has started yet, picks the ending from the denial score
    /// (on/above threshold = denial, below = acceptance), marks it started and returns its knot.
    /// </summary>
    public bool TryBeginEnding(out string endingKnot, out bool isDenialEnding)
    {
        endingKnot = null;
        isDenialEnding = false;
        if (_endingStarted || !AllStoryNpcsDone)
            return false;

        int score = storyController != null ? storyController.DenialScore : 0;
        isDenialEnding = score >= denialEndingThreshold;
        endingKnot = isDenialEnding ? denialEndingKnot : acceptanceEndingKnot;
        _endingStarted = true;

        MedinaExitDoor.SetGameCompleted(true);
        Debug.Log($"[MedinaLevelManager] All story NPCs done. Denial score {score} (threshold {denialEndingThreshold}) -> " +
                  $"{(isDenialEnding ? "DENIAL" : "ACCEPTANCE")} ending '{endingKnot}'.");
        onEndingStarted?.Invoke(isDenialEnding);
        return true;
    }

    private void ApplyNpcProgressToScene()
    {
        foreach (MedinaInteractable i in SceneInteractables)
        {
            if (i != null && i.Kind == MedinaInteractableKind.Dialogue && _completedNpcIds.Contains(i.InteractableId))
                i.MarkConversationDone();
        }
    }

    private void HandleCapturingSaveData(VNSaveData data)
    {
        if (data == null)
            return;

        data.completedNpcIds = new System.Collections.Generic.List<string>(_completedNpcIds);
        data.endingStarted = _endingStarted;

        if (playerController == null)
            return;

        data.hasPlayerPosition = true;
        data.playerPosition = playerController.transform.position;
    }

    private void HandleSaveDataLoaded(VNSaveData data)
    {
        if (data == null)
            return;

        _completedNpcIds.Clear();
        if (data.completedNpcIds != null)
        {
            foreach (string id in data.completedNpcIds)
                if (!string.IsNullOrWhiteSpace(id)) _completedNpcIds.Add(id);
        }
        _endingStarted = data.endingStarted;
        ApplyNpcProgressToScene();

        if (!data.hasPlayerPosition || playerController == null)
            return;

        // A loaded save wins over the minigame return point.
        ClearSavedReturnPosition();
        playerController.TeleportTo(data.playerPosition);
        if (storybookCamera != null)
            storybookCamera.SnapToTarget();
    }

    public void LaunchMinigameScene(string sceneName)
    {
        LastTriggeredAction = $"scene:{sceneName}";

        if (playerController != null)
        {
            _savedReturnPosition = playerController.transform.position;
            _hasSavedReturnPosition = true;
        }

        // Snapshot the medina in memory (story, player position, NPC progress, intro flag, denial score,
        // journal). The minigame scene stays isolated; on return the medina rebuilds itself from this
        // snapshot plus the minigame's results. No save file is written.
        VNSaveData snapshot = storyController != null ? storyController.CaptureSaveData("Medina (minigame)") : null;
        MinigameSession.BeginMinigame(sceneName, snapshot);

        onSceneRequested?.Invoke(sceneName);

        // CanStreamedLevelBeLoaded accepts a scene name (GetBuildIndexByScenePath expects a full path)
        if (!string.IsNullOrWhiteSpace(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogWarning($"[MedinaLevelManager] Scene '{sceneName}' is not in Build Profiles/Settings or cannot be loaded.");
        }
    }

    public void ResumeExploration()
    {
        SetExplorationActive(true);
    }

    /// <summary>Brings the exploration ambience back up and (optionally) re-enables movement/interaction.</summary>
    public void EnterExploration(bool enableControls = true)
    {
        SetExplorationActive(enableControls);

        if (explorationAmbienceSource != null)
            explorationAmbienceSource.loop = true;
        FadeExplorationAmbience(explorationAmbienceVolume);
    }

    public void UnlockMedinaExit()
    {
        MedinaExitDoor.SetGameCompleted(true);
    }

    public void SetGameCompleted(bool completed)
    {
        MedinaExitDoor.SetGameCompleted(completed);
    }

    public static void ClearSavedReturnPosition()
    {
        _hasSavedReturnPosition = false;
    }

    public void SetExplorationActive(bool active)
    {
        IsExplorationPaused = !active;

        if (playerController != null)
            playerController.IsMovementEnabled = active;

        if (interactionController != null)
            interactionController.IsInteractionEnabled = active;
    }

    private void StartExplorationAmbience()
    {
        if (explorationAmbienceSource == null || explorationAmbienceSource.clip == null)
            return;

        explorationAmbienceSource.loop = true;
        explorationAmbienceSource.volume = explorationAmbienceVolume;
        if (!explorationAmbienceSource.isPlaying)
            explorationAmbienceSource.Play();
    }

    private void FadeExplorationAmbience(float targetVolume)
    {
        if (explorationAmbienceSource == null)
            return;

        if (_ambienceTween.IsActive())
            _ambienceTween.Cancel();

        float startVol = explorationAmbienceSource.volume;
        if (targetVolume > 0f && !explorationAmbienceSource.isPlaying && explorationAmbienceSource.clip != null)
            explorationAmbienceSource.Play();

        _ambienceTween = LMotion.Create(startVol, targetVolume, ambienceFadeDuration)
            .WithEase(Ease.Linear)
            .WithOnComplete(() =>
            {
                explorationAmbienceSource.volume = targetVolume;
                if (targetVolume <= 0.001f)
                    explorationAmbienceSource.Pause();
            })
            .Bind(v => explorationAmbienceSource.volume = v);
    }
}