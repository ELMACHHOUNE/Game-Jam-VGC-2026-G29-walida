using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Represents one of the 4 required neighborhood photo locations in L'Walida's book.
/// Supports generous proximity matching so taking a photo nearby (even slightly off-center or far)
/// still unlocks and displays the curated book photo.
/// </summary>
public class NeighborhoodPhotoTarget : MonoBehaviour
{
    [Header("Target Identity")]
    [SerializeField] private string targetId = "lwalida_home";
    [SerializeField] private string guideLabel = "Near Lwalida's old home";
    [SerializeField] private string bookTitle = "Lwalida's Old Home";
    [TextArea(2, 4)]
    [SerializeField] private string memoryCaption = "Her shadow still lingers over her old doorway.";

    [Header("Curated Book Photo")]
    [SerializeField] private Texture2D curatedBookPhoto;

    [Header("Generous Capture Detection")]
    [Tooltip("World-space radius around this spot that still counts as a valid photo (generous for nearby/far framing).")]
    [SerializeField, Min(0.5f)] private float captureRadius = 4.2f;
    [Tooltip("Optional: if enabled (e.g. for Blue Sky), aiming anywhere above minMatchWorldY also counts.")]
    [SerializeField] private bool matchAnyAboveWorldY = false;
    [SerializeField] private float minMatchWorldY = 3.2f;

    [Header("Events")]
    [SerializeField] private UnityEvent<NeighborhoodPhotoTarget> onPhotoCollected;

    public string TargetId => targetId;
    public string GuideLabel => guideLabel;
    public string BookTitle => bookTitle;
    public string MemoryCaption => memoryCaption;
    public Texture2D CuratedBookPhoto => curatedBookPhoto;
    public float CaptureRadius => captureRadius;
    public bool IsCollected { get; private set; }

    /// <summary>
    /// Evaluates whether the phone viewfinder is close enough to this photo target,
    /// accounting for horizontal 360° panorama wrapping.
    /// Returns true if within the generous capture region, plus the wrapped distance for tie-breaking.
    /// </summary>
    public bool IsWithinCaptureArea(Vector3 viewfinderWorldPos, float wrapWidth, out float wrappedDistance, out float wrappedDeltaX)
    {
        Vector3 center = transform.position;
        float dx = center.x - viewfinderWorldPos.x;

        if (wrapWidth > 0.01f)
        {
            float dxLeft = (center.x - wrapWidth) - viewfinderWorldPos.x;
            float dxRight = (center.x + wrapWidth) - viewfinderWorldPos.x;

            if (Mathf.Abs(dxLeft) < Mathf.Abs(dx)) dx = dxLeft;
            if (Mathf.Abs(dxRight) < Mathf.Abs(dx)) dx = dxRight;
        }

        wrappedDeltaX = dx;
        float dy = center.y - viewfinderWorldPos.y;
        wrappedDistance = Mathf.Sqrt(dx * dx + dy * dy);

        if (wrappedDistance <= captureRadius)
        {
            return true;
        }

        if (matchAnyAboveWorldY && viewfinderWorldPos.y >= minMatchWorldY)
        {
            // Slightly prefer exact proximity if another target is closer, while still matching any high sky shot
            wrappedDistance = Mathf.Min(wrappedDistance, captureRadius * 0.9f);
            return true;
        }

        return false;
    }

    public void MarkCollected()
    {
        if (IsCollected)
        {
            return;
        }

        IsCollected = true;
        onPhotoCollected?.Invoke(this);
    }

    public void ResetCollected()
    {
        IsCollected = false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsCollected
            ? new Color(0.4f, 0.75f, 0.45f, 0.8f)
            : new Color(0.95f, 0.78f, 0.35f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, captureRadius);
    }
}
