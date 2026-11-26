using UnityEngine;

[DefaultExecutionOrder(10)]
public class PhoneArmStretch : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform wristAnchor;
    [SerializeField] private Transform armTransform;

    [Header("Shoulder Settings")]
    [SerializeField] private Vector2 shoulderViewportPosition = new Vector2(0.82f, -0.3f);
    [SerializeField] private float armThickness = 1.1f;
    [SerializeField] private float shoulderOvershoot = 2f;

    private SpriteRenderer _armSpriteRenderer;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (armTransform == null)
        {
            armTransform = transform;
        }

        if (armTransform != null)
        {
            _armSpriteRenderer = armTransform.GetComponent<SpriteRenderer>();
        }
    }

    private void LateUpdate()
    {
        UpdateArmPose();
    }

    public void UpdateArmPose()
    {
        if (targetCamera == null || wristAnchor == null || armTransform == null)
        {
            return;
        }

        float armZ = armTransform.position.z;
        float depth = Mathf.Abs(armZ - targetCamera.transform.position.z);

        Vector3 shoulderWorld = targetCamera.ViewportToWorldPoint(
            new Vector3(shoulderViewportPosition.x, shoulderViewportPosition.y, depth));
        shoulderWorld.z = armZ;

        Vector3 wristWorld = wristAnchor.position;
        wristWorld.z = armZ;

        Vector3 delta = wristWorld - shoulderWorld;
        float distance = delta.magnitude;
        if (distance < 0.001f)
        {
            return;
        }

        Vector3 dir = delta / distance;
        Vector3 shoulderEnd = shoulderWorld - dir * shoulderOvershoot;
        float totalLength = distance + shoulderOvershoot;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        float spriteWidth = 1f;
        float spriteHeight = 1f;
        if (_armSpriteRenderer != null && _armSpriteRenderer.sprite != null)
        {
            Vector3 spriteSize = _armSpriteRenderer.sprite.bounds.size;
            if (spriteSize.x > 0.001f) spriteWidth = spriteSize.x;
            if (spriteSize.y > 0.001f) spriteHeight = spriteSize.y;
        }

        armTransform.position = (shoulderEnd + wristWorld) * 0.5f;
        armTransform.rotation = Quaternion.Euler(0f, 0f, angle);
        armTransform.localScale = new Vector3(totalLength / spriteWidth, armThickness / spriteHeight, 1f);
    }
}
