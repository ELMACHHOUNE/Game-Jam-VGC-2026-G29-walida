using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class MedinaPlayerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private StorybookCutout depthCutout;

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5.5f;
    [SerializeField] private float acceleration = 18f;
    [SerializeField] private float gravity = 20f;
    [SerializeField] private Vector2 worldBoundsMin = new Vector2(-15.5f, -9.5f);
    [SerializeField] private Vector2 worldBoundsMax = new Vector2(15.5f, 9.5f);

    [Header("Storybook Paper Animation")]
    [SerializeField] private float flipSpeed = 14f;
    [SerializeField] private float walkHopHeight = 0.14f;
    [SerializeField] private float walkHopFrequency = 11f;
    [SerializeField] private float walkTiltAngle = 5f;

    private Vector3 _velocity;
    private float _verticalVelocity;
    private float _facingSign = 1f;
    private float _currentScaleX = 1f;
    private float _walkCycle;
    private Vector3 _visualBaseLocalPos;

    public bool IsMovementEnabled { get; set; } = true;
    public Vector2 CurrentMoveInput { get; private set; }
    public Vector3 CurrentVelocity => _velocity;

    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (visualRoot != null)
        {
            _visualBaseLocalPos = visualRoot.localPosition;
        }
    }

    private void Update()
    {
        Vector2 input = IsMovementEnabled ? ReadMoveInput() : Vector2.zero;
        CurrentMoveInput = input;

        Vector3 desiredVelocity = new Vector3(input.x, 0f, input.y) * moveSpeed;
        float blend = 1f - Mathf.Exp(-acceleration * Time.deltaTime);
        _velocity = Vector3.Lerp(_velocity, desiredVelocity, blend);

        if (characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }
        else
        {
            _verticalVelocity -= gravity * Time.deltaTime;
        }

        Vector3 motion = (_velocity + Vector3.up * _verticalVelocity) * Time.deltaTime;
        characterController.Move(motion);
        ClampToWorldBounds();

        if (Mathf.Abs(input.x) > 0.05f)
        {
            _facingSign = input.x >= 0f ? 1f : -1f;
        }

        UpdatePaperCutoutAnimation();
    }

    public void TeleportTo(Vector3 worldPosition)
    {
        if (characterController != null)
        {
            characterController.enabled = false;
            transform.position = worldPosition;
            characterController.enabled = true;
        }
        else
        {
            transform.position = worldPosition;
        }

        _velocity = Vector3.zero;
        _verticalVelocity = 0f;

        if (depthCutout != null)
        {
            depthCutout.UpdateDepthSorting();
        }
    }

    private Vector2 ReadMoveInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return Vector2.zero;
        }

        float x = 0f;
        float y = 0f;

        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;

        Vector2 raw = new Vector2(x, y);
        return raw.sqrMagnitude > 1f ? raw.normalized : raw;
    }

    private void ClampToWorldBounds()
    {
        Vector3 pos = transform.position;
        float clampedX = Mathf.Clamp(pos.x, worldBoundsMin.x, worldBoundsMax.x);
        float clampedZ = Mathf.Clamp(pos.z, worldBoundsMin.y, worldBoundsMax.y);

        if (!Mathf.Approximately(clampedX, pos.x) || !Mathf.Approximately(clampedZ, pos.z))
        {
            characterController.enabled = false;
            transform.position = new Vector3(clampedX, pos.y, clampedZ);
            characterController.enabled = true;
        }
    }

    private void UpdatePaperCutoutAnimation()
    {
        if (visualRoot == null)
        {
            return;
        }

        float flipBlend = 1f - Mathf.Exp(-flipSpeed * Time.deltaTime);
        _currentScaleX = Mathf.Lerp(_currentScaleX, _facingSign, flipBlend);

        Vector3 scale = visualRoot.localScale;
        scale.x = Mathf.Abs(_currentScaleX) < 0.05f ? 0.05f * Mathf.Sign(_facingSign) : _currentScaleX;
        visualRoot.localScale = scale;

        float speedRatio = moveSpeed > 0.01f ? Mathf.Clamp01(new Vector2(_velocity.x, _velocity.z).magnitude / moveSpeed) : 0f;
        if (speedRatio > 0.05f)
        {
            _walkCycle += Time.deltaTime * walkHopFrequency * (0.6f + 0.4f * speedRatio);
        }
        else
        {
            _walkCycle = 0f;
        }

        float hop = Mathf.Abs(Mathf.Sin(_walkCycle)) * walkHopHeight * speedRatio;
        float tilt = Mathf.Sin(_walkCycle) * walkTiltAngle * speedRatio;

        visualRoot.localPosition = _visualBaseLocalPos + new Vector3(0f, hop, 0f);
        Vector3 euler = visualRoot.localEulerAngles;
        euler.z = tilt;
        visualRoot.localEulerAngles = euler;
    }
}
