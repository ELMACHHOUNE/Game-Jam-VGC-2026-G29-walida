using UnityEngine;
using UnityEngine.InputSystem;

public class MedinaInteractionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private MedinaLevelManager levelManager;

    [Header("Input Settings")]
    [SerializeField] private bool allowSpaceKey = true;

    private MedinaInteractable[] _interactables;
    private MedinaInteractable _currentTarget;

    public bool IsInteractionEnabled { get; set; } = true;
    public MedinaInteractable CurrentTarget => _currentTarget;

    private void Awake()
    {
        if (playerTransform == null)
        {
            playerTransform = transform;
        }

        if (levelManager == null)
        {
            levelManager = FindAnyObjectByType<MedinaLevelManager>();
        }

        RefreshInteractables();
    }

    public void RefreshInteractables()
    {
        _interactables = FindObjectsByType<MedinaInteractable>(FindObjectsInactive.Exclude);
    }

    private void Update()
    {
        if (!IsInteractionEnabled)
        {
            SetCurrentTarget(null);
            return;
        }

        UpdateNearestInteractable();

        if (_currentTarget == null)
        {
            return;
        }

        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        bool pressedE = kb.eKey.wasPressedThisFrame;
        bool pressedSpace = allowSpaceKey && kb.spaceKey.wasPressedThisFrame;

        if (pressedE || pressedSpace)
        {
            InteractWithCurrent();
        }
    }

    public void UpdateNearestInteractable()
    {
        if (_interactables == null || _interactables.Length == 0)
        {
            RefreshInteractables();
        }

        Vector3 origin = playerTransform.position;
        MedinaInteractable nearest = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < _interactables.Length; i++)
        {
            MedinaInteractable candidate = _interactables[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            float dist = candidate.GetPlanarDistanceTo(origin);
            if (dist <= candidate.InteractionRadius && dist < bestDistance)
            {
                bestDistance = dist;
                nearest = candidate;
            }
        }

        SetCurrentTarget(nearest);
    }

    public bool InteractWithCurrent()
    {
        if (_currentTarget == null)
        {
            return false;
        }

        MedinaInteractable target = _currentTarget;
        target.TriggerInteraction();

        if (levelManager != null)
        {
            levelManager.HandleInteractable(target);
        }

        return true;
    }

    private void SetCurrentTarget(MedinaInteractable next)
    {
        if (_currentTarget == next)
        {
            return;
        }

        if (_currentTarget != null)
        {
            _currentTarget.SetHighlighted(false);
        }

        _currentTarget = next;

        if (_currentTarget != null)
        {
            _currentTarget.SetHighlighted(true);
        }
    }
}
