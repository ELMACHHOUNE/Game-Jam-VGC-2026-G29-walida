using System;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider2D))]
public class PhotographableCharacter : MonoBehaviour
{
    [Header("Character Identity")]
    [SerializeField] private string characterId = "placeholder_character";
    [SerializeField] private string dialogueKnotName = "ch1_apartment";

    [Header("Capture Detection")]
    [SerializeField] private Collider2D targetCollider;
    [SerializeField, Range(0.1f, 1f)] private float requiredCoverage = 0.5f;
    [SerializeField] private bool triggerOnce = true;
    [Tooltip("Optional: if assigned, this target can only be photographed while its 1960s memory is revealed.")]
    [SerializeField] private MemoryHotspot requiredMemoryHotspot;

    [Header("Events")]
    [SerializeField] private UnityEvent<PhotographableCharacter> onPhotographed;

    private bool _hasBeenPhotographed;

    public event Action<PhotographableCharacter, Texture2D> OnCharacterPhotographed;

    public string CharacterId => characterId;
    public string DialogueKnotName => dialogueKnotName;
    public float RequiredCoverage => requiredCoverage;
    public bool TriggerOnce => triggerOnce;
    public bool HasBeenPhotographed => _hasBeenPhotographed;
    public bool CanBePhotographed => (!triggerOnce || !_hasBeenPhotographed)
        && (requiredMemoryHotspot == null || requiredMemoryHotspot.IsCurrentlyRevealed);

    private void Awake()
    {
        if (targetCollider == null)
        {
            targetCollider = GetComponent<Collider2D>();
        }
    }

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        if (targetCollider != null)
        {
            bounds = targetCollider.bounds;
            return true;
        }

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            bounds = sr.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    public void NotifyPhotographed(Texture2D photo)
    {
        if (!CanBePhotographed)
        {
            return;
        }

        _hasBeenPhotographed = true;
        OnCharacterPhotographed?.Invoke(this, photo);
        onPhotographed?.Invoke(this);
    }

    public void ResetPhotographedState()
    {
        _hasBeenPhotographed = false;
    }
}
