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
