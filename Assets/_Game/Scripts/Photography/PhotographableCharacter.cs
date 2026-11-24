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

    [Header("Events")]
    [SerializeField] private UnityEvent<PhotographableCharacter> onPhotographed;

    private bool _hasBeenPhotographed;

    public event Action<PhotographableCharacter, Texture2D> OnCharacterPhotographed;

    public string CharacterId => characterId;
    public string DialogueKnotName => dialogueKnotName;
    public float RequiredCoverage => requiredCoverage;
    public bool TriggerOnce => triggerOnce;
    public bool HasBeenPhotographed => _hasBeenPhotographed;
    public bool CanBePhotographed => !triggerOnce || !_hasBeenPhotographed;

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
