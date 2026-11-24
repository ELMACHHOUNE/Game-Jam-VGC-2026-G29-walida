using UnityEngine;
using UnityEngine.InputSystem;

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

    private float _currentSpeed;
    private float _speedVelocity;
    private float _panoramaWidth;
    private float _panoramaCenterX;
    private GameObject _leftLoopTile;
    private GameObject _rightLoopTile;

    public float CurrentScrollInput { get; private set; }
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
        sr.sprite = source.sprite;
        sr.color = source.color;
        sr.flipX = source.flipX;
        sr.flipY = source.flipY;
        sr.sortingLayerID = source.sortingLayerID;
        sr.sortingOrder = source.sortingOrder;
        sr.sharedMaterial = source.sharedMaterial;
    }

    private void Update()
    {
        LastWrapDeltaX = 0f;

        if (targetCamera == null || panoramaRenderer == null)
        {
            return;
        }

        float scrollInput = ReadEdgeScrollInput();
        CurrentScrollInput = scrollInput;

        float targetSpeed = scrollInput * maxScrollSpeed;
        _currentSpeed = Mathf.SmoothDamp(_currentSpeed, targetSpeed, ref _speedVelocity, smoothTime);

        Vector3 camPos = targetCamera.transform.position;
        camPos.x += _currentSpeed * Time.deltaTime;

        if (loopHorizontally && _panoramaWidth > 0.01f)
        {
            float halfWidth = _panoramaWidth * 0.5f;
            float minWrapX = _panoramaCenterX - halfWidth;
            float maxWrapX = _panoramaCenterX + halfWidth;

            if (camPos.x > maxWrapX)
            {
                camPos.x -= _panoramaWidth;
                LastWrapDeltaX = -_panoramaWidth;
            }
            else if (camPos.x < minWrapX)
            {
                camPos.x += _panoramaWidth;
                LastWrapDeltaX = _panoramaWidth;
            }
        }
        else
        {
            camPos.x = ClampCameraX(camPos.x);
        }

        targetCamera.transform.position = camPos;
    }

    private float ReadEdgeScrollInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || Screen.width <= 0)
        {
            return 0f;
        }

        Vector2 mousePos = mouse.position.ReadValue();
        if (mousePos.x < 0f || mousePos.x > Screen.width || mousePos.y < 0f || mousePos.y > Screen.height)
        {
            return 0f;
        }

        float normalizedX = mousePos.x / Screen.width;

        if (normalizedX < edgeZoneRatio)
        {
            float t = (edgeZoneRatio - normalizedX) / edgeZoneRatio;
            return -Mathf.Clamp01(t);
        }

        float rightStart = 1f - edgeZoneRatio;
        if (normalizedX > rightStart)
        {
            float t = (normalizedX - rightStart) / edgeZoneRatio;
            return Mathf.Clamp01(t);
        }

        return 0f;
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
