using UnityEngine;
using UnityEngine.InputSystem;

public class PhoneCursorController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private PanoramaEdgeScroller edgeScroller;

    [Header("Attachment Settings")]
    [SerializeField] private float followSpeed = 0f;
    [SerializeField] private Vector2 viewportPadding = new Vector2(0.05f, 0.05f);
    [SerializeField] private float worldZ = -1f;
    [SerializeField] private bool hideSystemCursor = false;

    [Header("Edge Tilt Settings")]
    [SerializeField] private float maxEdgeTiltAngle = 10f;
    [SerializeField, Range(0.05f, 0.5f)] private float tiltStartEdgeZone = 0.25f;
    [SerializeField] private float tiltSmoothSpeed = 12f;

    private float _currentTiltAngle;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void OnEnable()
    {
        if (hideSystemCursor)
        {
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        if (hideSystemCursor)
        {
            Cursor.visible = true;
        }
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            return;
        }

        if (edgeScroller != null && Mathf.Abs(edgeScroller.LastWrapDeltaX) > 0.001f)
        {
            Vector3 wrappedPos = transform.position;
            wrappedPos.x += edgeScroller.LastWrapDeltaX;
            transform.position = wrappedPos;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }

        Vector2 mousePos = mouse.position.ReadValue();
        float rawVx = Mathf.Clamp01(mousePos.x / Screen.width);
        float vx = Mathf.Clamp(rawVx, viewportPadding.x, 1f - viewportPadding.x);
        float vy = Mathf.Clamp(mousePos.y / Screen.height, viewportPadding.y, 1f - viewportPadding.y);

        float depth = Mathf.Abs(worldZ - targetCamera.transform.position.z);
        Vector3 targetWorldPos = targetCamera.ViewportToWorldPoint(new Vector3(vx, vy, depth));
        targetWorldPos.z = worldZ;

        if (followSpeed <= 0f)
        {
            transform.position = targetWorldPos;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, targetWorldPos, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        }

        float targetTilt = ComputeEdgeTilt(rawVx);
        if (tiltSmoothSpeed <= 0f)
        {
            _currentTiltAngle = targetTilt;
        }
        else
        {
            _currentTiltAngle = Mathf.Lerp(_currentTiltAngle, targetTilt, 1f - Mathf.Exp(-tiltSmoothSpeed * Time.deltaTime));
        }

        transform.rotation = Quaternion.Euler(0f, 0f, _currentTiltAngle);
    }

    public float ComputeEdgeTilt(float normalizedScreenX)
    {
        if (tiltStartEdgeZone <= 0.001f)
        {
            return 0f;
        }

        if (normalizedScreenX < tiltStartEdgeZone)
        {
            float t = Mathf.Clamp01((tiltStartEdgeZone - normalizedScreenX) / tiltStartEdgeZone);
            return Mathf.SmoothStep(0f, 1f, t) * maxEdgeTiltAngle;
        }

        float rightStart = 1f - tiltStartEdgeZone;
        if (normalizedScreenX > rightStart)
        {
            float t = Mathf.Clamp01((normalizedScreenX - rightStart) / tiltStartEdgeZone);
            return -Mathf.SmoothStep(0f, 1f, t) * maxEdgeTiltAngle;
        }

        return 0f;
    }
}
