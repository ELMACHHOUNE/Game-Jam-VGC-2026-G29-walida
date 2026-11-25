using UnityEngine;

[RequireComponent(typeof(Camera))]
public class MedinaStorybookCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform target;
    [SerializeField] private Camera targetCamera;

    [Header("Diorama Framing")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 6.2f, -11.5f);
    [SerializeField] private float pitchAngle = 26f;
    [SerializeField] private float fieldOfView = 36f;
    [SerializeField] private float smoothTime = 0.18f;

    [Header("Bounds")]
    [SerializeField] private bool clampFollowTarget = true;
    [SerializeField] private Vector2 minTargetXZ = new Vector2(-10f, -6f);
    [SerializeField] private Vector2 maxTargetXZ = new Vector2(10f, 6f);

    private Vector3 _velocity;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        ApplyStorybookCameraSettings();
    }

    private void Start()
    {
        SnapToTarget();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desiredPos = ComputeDesiredPosition();
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _velocity, smoothTime);
        transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);
    }

    public void ApplyStorybookCameraSettings()
    {
        if (targetCamera == null)
        {
            return;
        }

        targetCamera.orthographic = false;
        targetCamera.fieldOfView = fieldOfView;
        targetCamera.transparencySortMode = TransparencySortMode.CustomAxis;
        targetCamera.transparencySortAxis = new Vector3(0f, 0f, 1f);
        transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);
    }

    public void SnapToTarget()
    {
        if (target == null)
        {
            return;
        }

        _velocity = Vector3.zero;
        transform.position = ComputeDesiredPosition();
        transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);
    }

    private Vector3 ComputeDesiredPosition()
    {
        Vector3 focus = target.position;
        if (clampFollowTarget)
        {
            focus.x = Mathf.Clamp(focus.x, minTargetXZ.x, maxTargetXZ.x);
            focus.z = Mathf.Clamp(focus.z, minTargetXZ.y, maxTargetXZ.y);
        }

        return new Vector3(focus.x, 0f, focus.z) + followOffset;
    }
}
