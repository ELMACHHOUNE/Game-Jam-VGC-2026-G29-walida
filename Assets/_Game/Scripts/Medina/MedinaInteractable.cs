using System;
using UnityEngine;
using UnityEngine.Events;

public enum MedinaInteractableKind
{
    Dialogue,
    PhotographyMinigame,
    AudioRecordingMinigame,
    CustomEvent
}

public class MedinaInteractable : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string interactableId = "poi_01";
    [SerializeField] private string displayTitle = "Point of Interest";
    [SerializeField] private MedinaInteractableKind kind = MedinaInteractableKind.Dialogue;

    [Header("Action Targets")]
    [SerializeField] private string targetSceneName = "";
    [SerializeField] private string dialogueKnotName = "";
    [SerializeField] private float interactionRadius = 2.6f;

    [Header("Story NPC (Dialogue only)")]
    [Tooltip("Once this conversation has been played to the end, the NPC can't be talked to again and its prompt is hidden.")]
    [SerializeField] private bool oneTimeConversation = true;
    [Tooltip("Finishing this conversation counts toward the ending. When every counted NPC is done, the ending plays.")]
    [SerializeField] private bool countsTowardEnding = true;

    [Header("Prompt Visual")]
    [SerializeField] private Transform promptVisual;
    [SerializeField] private float promptBobHeight = 0.14f;
    [SerializeField] private float promptBobSpeed = 4.5f;

    [Header("Events")]
    [SerializeField] private UnityEvent<MedinaInteractable> onInteracted;

    private Vector3 _promptBaseLocalPos;
    private bool _isHighlighted;

    public event Action<MedinaInteractable> OnInteractableTriggered;

    public string InteractableId => interactableId;
    public string DisplayTitle => displayTitle;
    public MedinaInteractableKind Kind => kind;
    public string TargetSceneName => targetSceneName;
    public string DialogueKnotName => dialogueKnotName;
    public float InteractionRadius => interactionRadius;
    public bool IsHighlighted => _isHighlighted;
    public bool OneTimeConversation => oneTimeConversation;
    /// <summary>A Dialogue interactable whose conversation counts toward the ending.</summary>
    public bool IsStoryNpc => kind == MedinaInteractableKind.Dialogue && countsTowardEnding;
    public bool IsConversationDone { get; private set; }

    /// <summary>
    /// Marks the conversation as finished. A one-time NPC hides its prompt and disables itself,
    /// which removes it from MedinaInteractionController's candidates (it skips disabled interactables).
    /// </summary>
    public void MarkConversationDone()
    {
        IsConversationDone = true;
        if (!oneTimeConversation || kind != MedinaInteractableKind.Dialogue)
            return;

        SetHighlighted(false);
        enabled = false;
    }

    private void Awake()
    {
        if (promptVisual != null)
        {
            _promptBaseLocalPos = promptVisual.localPosition;
            promptVisual.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (_isHighlighted && promptVisual != null)
        {
            float bob = Mathf.Sin(Time.unscaledTime * promptBobSpeed) * promptBobHeight;
            promptVisual.localPosition = _promptBaseLocalPos + new Vector3(0f, bob, 0f);
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        _isHighlighted = highlighted;
        if (promptVisual != null)
        {
            promptVisual.gameObject.SetActive(highlighted);
            if (!highlighted)
            {
                promptVisual.localPosition = _promptBaseLocalPos;
            }
        }
    }

    public float GetPlanarDistanceTo(Vector3 worldPosition)
    {
        Vector2 a = new Vector2(transform.position.x, transform.position.z);
        Vector2 b = new Vector2(worldPosition.x, worldPosition.z);
        return Vector2.Distance(a, b);
    }

    public void TriggerInteraction()
    {
        OnInteractableTriggered?.Invoke(this);
        onInteracted?.Invoke(this);
    }
}
