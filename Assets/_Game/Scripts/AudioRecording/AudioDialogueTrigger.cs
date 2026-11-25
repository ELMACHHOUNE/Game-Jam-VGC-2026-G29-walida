using UnityEngine;
using UnityEngine.Events;

public class AudioDialogueTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpatialAudioPhoneController audioRecorder;
    [SerializeField] private VNStoryController storyController;
    [SerializeField] private PanoramaEdgeScroller edgeScroller;
    [SerializeField] private PhoneCursorController phoneCursorController;

    [Header("Settings")]
    [SerializeField] private bool lockControlsDuringDialogue = true;
    [SerializeField] private bool clearStageOnJump = true;

    [Header("Events")]
    [SerializeField] private UnityEvent<string> onDialogueTriggered;

    public bool IsDialogueActive { get; private set; }
    public string LastTriggeredKnot { get; private set; }
    public RecordableSoundSource LastRecordedSource { get; private set; }

    private void OnEnable()
    {
        if (audioRecorder != null)
        {
            audioRecorder.OnSoundRecorded += HandleSoundRecorded;
        }
    }

    private void OnDisable()
    {
        if (audioRecorder != null)
        {
            audioRecorder.OnSoundRecorded -= HandleSoundRecorded;
        }
    }

    private void HandleSoundRecorded(RecordableSoundSource source)
    {
        if (source == null)
        {
            return;
        }

        LastRecordedSource = source;
        if (string.IsNullOrWhiteSpace(source.DialogueKnotName))
        {
            Debug.Log($"[AudioDialogueTrigger] Recorded '{source.SoundId}' ({source.DisplayName})");
            return;
        }

        LastTriggeredKnot = source.DialogueKnotName;
        IsDialogueActive = true;

        if (lockControlsDuringDialogue && storyController != null)
        {
            SetControlsActive(false);
        }

        if (storyController != null)
        {
            storyController.JumpToKnot(source.DialogueKnotName, clearStageOnJump);
        }
        else
        {
            Debug.Log($"[AudioDialogueTrigger] Recorded '{source.SoundId}' -> Dialogue knot '{source.DialogueKnotName}'");
        }

        onDialogueTriggered?.Invoke(source.DialogueKnotName);
    }

    public void ResumeRecording()
    {
        IsDialogueActive = false;
        SetControlsActive(true);
    }

    private void SetControlsActive(bool active)
    {
        if (edgeScroller != null)
        {
            edgeScroller.enabled = active;
        }

        if (phoneCursorController != null)
        {
            phoneCursorController.enabled = active;
        }

        if (audioRecorder != null)
        {
            audioRecorder.IsRecordingEnabled = active;
        }
    }
}
