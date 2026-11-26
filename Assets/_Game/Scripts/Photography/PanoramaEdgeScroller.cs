using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pans the camera across a 360° (equirectangular) panorama sprite in all directions.
/// - Horizontal: loops seamlessly (the 360 image wraps around).
/// - Vertical: scrolls up/down, clamped to the top/bottom of the panorama.
/// Input: move the mouse to any screen edge/corner (all directions), use WASD / arrow keys,
/// scroll with a trackpad/mouse wheel, or drag with right/middle mouse button.
/// </summary>
public class PanoramaEdgeScroller : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private SpriteRenderer panoramaRenderer;

    [Header("Edge Scroll Settings")]
    [SerializeField, Range(0.02f, 0.4f)] private float edgeZoneRatio = 0.12f;
    [SerializeField] private float maxScrollSpeed = 8f;
    [SerializeField] private float smoothTime = 0.12f;
    [SerializeField] private bool loopHorizontally = true;

    [Header("Vertical Scroll Settings")]
    [SerializeField] private bool enableVerticalScroll = true;
    [SerializeField, Range(0.02f, 0.4f)] private float verticalEdgeZoneRatio = 0.12f;
    [SerializeField] private float maxVerticalScrollSpeed = 6f;
    [Tooltip("World units trimmed from the top of the panorama (hides stretched 360 ceiling pole if needed).")]
    [SerializeField, Min(0f)] private float topLimitPadding = 0f;
    [Tooltip("World units trimmed from the bottom of the panorama (hides stretched 360 floor pole if needed).")]
    [SerializeField, Min(0f)] private float bottomLimitPadding = 0f;

    [Header("Additional Scroll Inputs")]
    [SerializeField] private bool allowKeyboardScroll = true;
    [SerializeField] private bool allowWheelScroll = true;
    [SerializeField] private float wheelScrollSpeed = 0.08f;
    [SerializeField] private bool allowDragScroll = true;

    private Vector2 _currentVelocity;
    private Vector2 _velocitySmoothing;
    private float _panoramaWidth;
    private float _panoramaCenterX;
    private GameObject _leftLoopTile;
    private GameObject _rightLoopTile;

    /// <summary>Horizontal scroll input in [-1, 1] (kept for backward compatibility).</summary>
    public float CurrentScrollInput { get; private set; }
    /// <summary>Full 2D scroll input, each axis in [-1, 1].</summary>
    public Vector2 CurrentScrollInputVector { get; private set; }
    public float LastWrapDeltaX { get; private set; }
    public float PanoramaWidth => _panoramaWidth;
    public bool LoopHorizontally => loopHorizontally;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        SetupLoopTiles();
    }

    private void Start()
    {
        if (targetCamera != null && panoramaRenderer != null && enableVerticalScroll)
        {
            Vector3 camPos = targetCamera.transform.position;
            camPos.y = ClampCameraY(camPos.y);
            targetCamera.transform.position = camPos;
        }
    }

    private void OnDisable()
    {
        _currentVelocity = Vector2.zero;
        _velocitySmoothing = Vector2.zero;
        CurrentScrollInput = 0f;
        CurrentScrollInputVector = Vector2.zero;
    }

    private void SetupLoopTiles()
    {
        if (panoramaRenderer == null)
        {
            return;
        }

        Bounds bounds = panoramaRenderer.bounds;
        _panoramaWidth = bounds.size.x;
        _panoramaCenterX = bounds.center.x;

        if (!loopHorizontally || _panoramaWidth <= 0.01f)
        {
            return;
        }

        _leftLoopTile = CreateLoopTile("PanoramaLoopLeft", -_panoramaWidth);
        _rightLoopTile = CreateLoopTile("PanoramaLoopRight", _panoramaWidth);
    }

    private GameObject CreateLoopTile(string tileName, float offsetX)
    {
        GameObject tileRoot = new GameObject(tileName);
        tileRoot.transform.SetParent(panoramaRenderer.transform.parent, false);
        tileRoot.transform.position = panoramaRenderer.transform.position + new Vector3(offsetX, 0f, 0f);
        tileRoot.transform.rotation = panoramaRenderer.transform.rotation;
        tileRoot.transform.localScale = panoramaRenderer.transform.localScale;
        tileRoot.layer = panoramaRenderer.gameObject.layer;

        CopySpriteRenderer(panoramaRenderer, tileRoot);

        SpriteRenderer[] childSprites = panoramaRenderer.GetComponentsInChildren<SpriteRenderer>(false);
        for (int i = 0; i < childSprites.Length; i++)
        {
            SpriteRenderer source = childSprites[i];
            if (source == panoramaRenderer)
            {
                continue;
            }

            GameObject childClone = new GameObject(source.gameObject.name);
            childClone.transform.position = source.transform.position + new Vector3(offsetX, 0f, 0f);
            childClone.transform.rotation = source.transform.rotation;
            childClone.transform.localScale = source.transform.lossyScale;
            childClone.transform.SetParent(tileRoot.transform, true);
            childClone.layer = source.gameObject.layer;

            CopySpriteRenderer(source, childClone);
        }

        return tileRoot;
    }

    private static void CopySpriteRenderer(SpriteRenderer source, GameObject destination)
    {
        SpriteRenderer sr = destination.AddComponent<SpriteRenderer>();
        sr.enabled = source.enabled;
        sr.sprite = source.sprite;
        sr.color = source.color;
        sr.flipX = source.flipX;
        sr.flipY = source.flipY;
        sr.drawMode = source.drawMode;
        sr.size = source.size;
        sr.sortingLayerID = source.sortingLayerID;
        sr.sortingOrder = source.sortingOrder;
        sr.maskInteraction = source.maskInteraction;
        sr.sharedMaterial = source.sharedMaterial;
    }

    private void Update()
    {
        LastWrapDeltaX = 0f;

        if (targetCamera == null || panoramaRenderer == null)
        {
            return;
        }

        Vector3 camPos = targetCamera.transform.position;

        if (TryApplyDragScroll(ref camPos))
        {
            _currentVelocity = Vector2.zero;
            _velocitySmoothing = Vector2.zero;
            CurrentScrollInput = 0f;
            CurrentScrollInputVector = Vector2.zero;
        }
        else
        {
            Vector2 scrollInput = ReadScrollInput();
            if (!enableVerticalScroll)
            {
                scrollInput.y = 0f;
            }

            if (scrollInput.sqrMagnitude > 1f)
            {
                scrollInput.Normalize();
            }

            CurrentScrollInputVector = scrollInput;
            CurrentScrollInput = scrollInput.x;

            Vector2 targetVelocity = new Vector2(scrollInput.x * maxScrollSpeed, scrollInput.y * maxVerticalScrollSpeed);
            _currentVelocity = Vector2.SmoothDamp(_currentVelocity, targetVelocity, ref _velocitySmoothing, smoothTime);

            camPos.x += _currentVelocity.x * Time.deltaTime;
            camPos.y += _currentVelocity.y * Time.deltaTime;
        }

        ApplyWheelScroll(ref camPos);

        // Horizontal: wrap around the 360 panorama, or clamp if looping is disabled.
        if (loopHorizontally && _panoramaWidth > 0.01f)
        {
            float halfWidth = _panoramaWidth * 0.5f;
            float minWrapX = _panoramaCenterX - halfWidth;
            float maxWrapX = _panoramaCenterX + halfWidth;

            while (camPos.x > maxWrapX)
            {
                camPos.x -= _panoramaWidth;
                LastWrapDeltaX -= _panoramaWidth;
            }

            while (camPos.x < minWrapX)
            {
                camPos.x += _panoramaWidth;
                LastWrapDeltaX += _panoramaWidth;
            }
        }
        else
        {
            camPos.x = ClampCameraX(camPos.x);
        }

        // Vertical: clamp to the top/bottom of the panorama.
        if (enableVerticalScroll)
        {
            float clampedY = ClampCameraY(camPos.y);
            if (!Mathf.Approximately(clampedY, camPos.y))
            {
                _currentVelocity.y = 0f;
                _velocitySmoothing.y = 0f;
            }

            camPos.y = clampedY;
        }

        targetCamera.transform.position = camPos;
    }

    private bool TryApplyDragScroll(ref Vector3 camPos)
    {
        if (!allowDragScroll || Screen.height <= 0)
        {
            return false;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || (!mouse.rightButton.isPressed && !mouse.middleButton.isPressed))
        {
            return false;
        }

        Vector2 pixelDelta = mouse.delta.ReadValue();
        if (pixelDelta.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        float worldUnitsPerPixel = (targetCamera.orthographicSize * 2f) / Screen.height;
        camPos.x -= pixelDelta.x * worldUnitsPerPixel;
        if (enableVerticalScroll)
        {
            camPos.y -= pixelDelta.y * worldUnitsPerPixel;
        }

        return true;
    }

    private void ApplyWheelScroll(ref Vector3 camPos)
    {
        if (!allowWheelScroll || Mathf.Approximately(wheelScrollSpeed, 0f))
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Vector2 wheel = mouse.scroll.ReadValue();
        if (wheel.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        float maxStep = maxScrollSpeed * 0.25f;
        float deltaX = Mathf.Clamp(wheel.x * wheelScrollSpeed, -maxStep, maxStep);
        camPos.x += deltaX;

        if (enableVerticalScroll)
        {
            float maxStepY = maxVerticalScrollSpeed * 0.25f;
            float deltaY = Mathf.Clamp(wheel.y * wheelScrollSpeed, -maxStepY, maxStepY);
            camPos.y += deltaY;
        }
    }

    private Vector2 ReadScrollInput()
    {
        Vector2 input = ReadEdgeScrollInput();

        if (allowKeyboardScroll)
        {
            Vector2 keyInput = ReadKeyboardScrollInput();
            input.x = Mathf.Abs(keyInput.x) > Mathf.Abs(input.x) ? keyInput.x : input.x;
            input.y = Mathf.Abs(keyInput.y) > Mathf.Abs(input.y) ? keyInput.y : input.y;
        }

        return input;
    }

    private Vector2 ReadEdgeScrollInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return Vector2.zero;
        }

        Vector2 mousePos = mouse.position.ReadValue();
        if (mousePos.x < 0f || mousePos.x > Screen.width || mousePos.y < 0f || mousePos.y > Screen.height)
        {
            return Vector2.zero;
        }

        float normalizedX = mousePos.x / Screen.width;
        float normalizedY = mousePos.y / Screen.height;

        return new Vector2(
            EdgeAxis(normalizedX, edgeZoneRatio),
            EdgeAxis(normalizedY, verticalEdgeZoneRatio));
    }

    private static float EdgeAxis(float normalized, float zone)
    {
        if (zone <= 0.0001f)
        {
            return 0f;
        }

        if (normalized < zone)
        {
            return -Mathf.Clamp01((zone - normalized) / zone);
        }

        float maxStart = 1f - zone;
        if (normalized > maxStart)
        {
            return Mathf.Clamp01((normalized - maxStart) / zone);
        }

        return 0f;
    }

    private static Vector2 ReadKeyboardScrollInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float x = 0f;
        float y = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;

        return new Vector2(x, y);
    }

    private float ClampCameraX(float desiredX)
    {
        Bounds bounds = panoramaRenderer.bounds;
        float halfCameraWidth = targetCamera.orthographicSize * targetCamera.aspect;
        float minX = bounds.min.x + halfCameraWidth;
        float maxX = bounds.max.x - halfCameraWidth;

        if (minX > maxX)
        {
            return bounds.center.x;
        }

        return Mathf.Clamp(desiredX, minX, maxX);
    }

    private float ClampCameraY(float desiredY)
    {
        Bounds bounds = panoramaRenderer.bounds;
        float halfCameraHeight = targetCamera.orthographicSize;
        float minY = bounds.min.y + bottomLimitPadding + halfCameraHeight;
        float maxY = bounds.max.y - topLimitPadding - halfCameraHeight;

        if (minY > maxY)
        {
            // Panorama (after padding) is shorter than the view: nothing to scroll vertically.
            return (minY + maxY) * 0.5f;
        }

        return Mathf.Clamp(desiredY, minY, maxY);
    }

    private void OnDestroy()
    {
        if (_leftLoopTile != null)
        {
            Destroy(_leftLoopTile);
        }

        if (_rightLoopTile != null)
        {
            Destroy(_rightLoopTile);
        }
    }
}
