using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using LitMotion;

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

    [Header("Events")]
    [SerializeField] private UnityEvent<string> onDialogueRequested;
    [SerializeField] private UnityEvent<string> onSceneRequested;

    private MedinaInteractable[] _sceneInteractables;
    private MotionHandle _ambienceTween;

    public bool IsExplorationPaused { get; private set; }
    public string LastTriggeredAction { get; private set; }

    private void Awake()
    {
        if (storyController == null)
            storyController = FindFirstObjectByType<VNStoryController>();

        if (stageView == null)
            stageView = FindFirstObjectByType<VNStageView>();
    }

    private void OnEnable()
    {
        if (storyController != null)
            storyController.OnDialogueEnded += HandleDialogueEnded;

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
            storyController.OnDialogueEnded -= HandleDialogueEnded;

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
        if (restoreSavedPositionOnStart && _hasSavedReturnPosition && playerController != null)
        {
            playerController.TeleportTo(_savedReturnPosition);
            if (storybookCamera != null)
                storybookCamera.SnapToTarget();
        }

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
                Debug.Log($"[MedinaLevelManager] Triggered custom interactable '{interactable.DisplayTitle}'");
                break;
        }
    }

    public void TriggerDialogue(string knotName, string sourceTitle = "", Transform focusTarget = null)
    {
        LastTriggeredAction = $"dialogue:{knotName}";

        if (lockExplorationDuringDialogue)
            SetExplorationActive(false);

        FadeExplorationAmbience(0f);

        if (sceneFlowController != null && !string.IsNullOrWhiteSpace(knotName))
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

    public void LaunchMinigameScene(string sceneName)
    {
        LastTriggeredAction = $"scene:{sceneName}";

        if (playerController != null)
        {
            _savedReturnPosition = playerController.transform.position;
            _hasSavedReturnPosition = true;
        }

        if (storyController != null)
            storyController.CacheInkStateForSceneTransition();

        onSceneRequested?.Invoke(sceneName);

        if (!string.IsNullOrWhiteSpace(sceneName) && SceneUtility.GetBuildIndexByScenePath(sceneName) >= 0)
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