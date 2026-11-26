using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
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

    [Header("Default Minigame Scenes")]
    [SerializeField] private string photographySceneName = "PhotographingMinigame";
    [SerializeField] private string audioRecordingSceneName = "AudioRecordingMinigame";

    [Header("Settings")]
    [SerializeField] private bool restoreSavedPositionOnStart = true;
    [SerializeField] private bool lockExplorationDuringDialogue = true;
    [SerializeField] private bool clearStageOnDialogueJump = true;

    [Header("Events")]
    [SerializeField] private UnityEvent<string> onDialogueRequested;
    [SerializeField] private UnityEvent<string> onSceneRequested;

    public bool IsExplorationPaused { get; private set; }
    public string LastTriggeredAction { get; private set; }

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
            {
                storybookCamera.SnapToTarget();
            }
        }
    }

    public void HandleInteractable(MedinaInteractable interactable)
    {
        if (interactable == null)
        {
            return;
        }

        switch (interactable.Kind)
        {
            case MedinaInteractableKind.Dialogue:
                TriggerDialogue(interactable.DialogueKnotName, interactable.DisplayTitle);
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

    public void TriggerDialogue(string knotName, string sourceTitle = "")
    {
        LastTriggeredAction = $"dialogue:{knotName}";

        if (lockExplorationDuringDialogue && storyController != null)
        {
            SetExplorationActive(false);
        }

        if (storyController != null && !string.IsNullOrWhiteSpace(knotName))
        {
            storyController.JumpToKnot(knotName, clearStageOnDialogueJump);
        }
        else
        {
            Debug.Log($"[MedinaLevelManager] Dialogue requested from '{sourceTitle}' -> knot '{knotName}'");
        }

        onDialogueRequested?.Invoke(knotName);
    }

    public void LaunchMinigameScene(string sceneName)
    {
        LastTriggeredAction = $"scene:{sceneName}";

        if (playerController != null)
        {
            _savedReturnPosition = playerController.transform.position;
            _hasSavedReturnPosition = true;
        }

        onSceneRequested?.Invoke(sceneName);

        if (!string.IsNullOrWhiteSpace(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogWarning($"[MedinaLevelManager] Scene '{sceneName}' is not in Build Settings or cannot be loaded.");
        }
    }

    public void ResumeExploration()
    {
        SetExplorationActive(true);
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

    private void SetExplorationActive(bool active)
    {
        IsExplorationPaused = !active;

        if (playerController != null)
        {
            playerController.IsMovementEnabled = active;
        }

        if (interactionController != null)
        {
            interactionController.IsInteractionEnabled = active;
        }
    }
}
