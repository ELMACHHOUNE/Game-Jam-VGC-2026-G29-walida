using UnityEngine;
using UnityEngine.Events;

public class PhotoDialogueTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PhonePhotoCapturer photoCapturer;
    [SerializeField] private VNStoryController storyController;
    [SerializeField] private PanoramaEdgeScroller edgeScroller;
    [SerializeField] private PhoneCursorController phoneCursorController;

    [Header("Settings")]
    [SerializeField] private bool lockPhotographyDuringDialogue = true;
    [SerializeField] private bool clearStageOnJump = true;

    [Header("Events")]
    [SerializeField] private UnityEvent<string> onDialogueTriggered;

    public bool IsDialogueActive { get; private set; }
    public string LastTriggeredKnot { get; private set; }
    public PhotographableCharacter LastPhotographedCharacter { get; private set; }

    private void OnEnable()
    {
        if (photoCapturer != null)
        {
            photoCapturer.OnCharacterPhotographed += HandleCharacterPhotographed;
        }
    }

    private void OnDisable()
    {
        if (photoCapturer != null)
        {
            photoCapturer.OnCharacterPhotographed -= HandleCharacterPhotographed;
        }
    }

    private void HandleCharacterPhotographed(PhotographableCharacter character, Texture2D photo)
    {
        if (character == null)
        {
            return;
        }

        LastPhotographedCharacter = character;
        LastTriggeredKnot = character.DialogueKnotName;
        IsDialogueActive = true;

        if (lockPhotographyDuringDialogue && storyController != null)
        {
            SetPhotographyControlsActive(false);
        }

        if (storyController != null && !string.IsNullOrWhiteSpace(character.DialogueKnotName))
        {
            storyController.JumpToKnot(character.DialogueKnotName, clearStageOnJump);
        }
        else
        {
            Debug.Log($"[PhotoDialogueTrigger] Photographed '{character.CharacterId}' -> Dialogue knot '{character.DialogueKnotName}'");
        }

        onDialogueTriggered?.Invoke(character.DialogueKnotName);
    }

    public void ResumePhotography()
    {
        IsDialogueActive = false;
        SetPhotographyControlsActive(true);
    }

    private void SetPhotographyControlsActive(bool active)
    {
        if (edgeScroller != null)
        {
            edgeScroller.enabled = active;
        }

        if (phoneCursorController != null)
        {
            phoneCursorController.enabled = active;
        }

        if (photoCapturer != null)
        {
            photoCapturer.IsCaptureEnabled = active;
        }
    }
}
