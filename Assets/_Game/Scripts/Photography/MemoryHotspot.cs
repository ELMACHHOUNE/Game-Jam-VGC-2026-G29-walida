using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Marks a specific place on the 360° panorama where pointing the phone camera
/// triggers a glitch effect and reveals the 1960s memory photo behind the present day.
/// </summary>
public class MemoryHotspot : MonoBehaviour
{
    [Header("Memory Identity")]
    [SerializeField] private string memoryId = "mother_1960s_alley";
    [SerializeField] private string memoryTitle = "Mother's Alleyway — 1960";

    [Header("Detection Radii (World Units)")]
    [Tooltip("Distance from hotspot center where the 1960s photo is fully revealed.")]
    [SerializeField, Min(0.2f)] private float innerRevealRadius = 2.4f;
    [Tooltip("Distance where camera glitch interference begins as the player nears the memory.")]
    [SerializeField, Min(0.5f)] private float outerGlitchRadius = 6.0f;

    [Header("Events")]
    [SerializeField] private UnityEvent<MemoryHotspot> onMemoryRevealed;
    [SerializeField] private UnityEvent<MemoryHotspot> onMemoryHidden;

    public string MemoryId => memoryId;
    public string MemoryTitle => memoryTitle;
    public float InnerRevealRadius => innerRevealRadius;
    public float OuterGlitchRadius => Mathf.Max(outerGlitchRadius, innerRevealRadius + 0.1f);
    public bool IsCurrentlyRevealed { get; private set; }
    public bool HasEverBeenRevealed { get; private set; }

    /// <summary>
    /// Computes normalized proximity in [0, 1] from the viewfinder to this hotspot,
    /// accounting for horizontal 360° panorama wrapping.
    /// 0 = outside outerGlitchRadius, 1 = inside innerRevealRadius.
    /// </summary>
    public float EvaluateProximity(Vector3 viewfinderWorldPos, float wrapWidth, out float closestDistance)
    {
        Vector3 center = transform.position;
        float dx = viewfinderWorldPos.x - center.x;

        if (wrapWidth > 0.01f)
        {
            float dxLeft = viewfinderWorldPos.x - (center.x - wrapWidth);
            float dxRight = viewfinderWorldPos.x - (center.x + wrapWidth);

            if (Mathf.Abs(dxLeft) < Mathf.Abs(dx)) dx = dxLeft;
            if (Mathf.Abs(dxRight) < Mathf.Abs(dx)) dx = dxRight;
        }

        float dy = viewfinderWorldPos.y - center.y;
        closestDistance = Mathf.Sqrt(dx * dx + dy * dy);

        float outer = OuterGlitchRadius;
        if (closestDistance >= outer)
        {
            return 0f;
        }

        if (closestDistance <= innerRevealRadius)
        {
            return 1f;
        }

        return 1f - ((closestDistance - innerRevealRadius) / (outer - innerRevealRadius));
    }

    public void SetRevealedState(bool revealed)
    {
        if (IsCurrentlyRevealed == revealed)
        {
            return;
        }

        IsCurrentlyRevealed = revealed;
        if (revealed)
        {
            HasEverBeenRevealed = true;
            onMemoryRevealed?.Invoke(this);
        }
        else
        {
            onMemoryHidden?.Invoke(this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.25f, 0.85f, 0.95f, 0.55f);
        Gizmos.DrawWireSphere(transform.position, OuterGlitchRadius);

        Gizmos.color = new Color(0.95f, 0.72f, 0.25f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, innerRevealRadius);
    }
}
