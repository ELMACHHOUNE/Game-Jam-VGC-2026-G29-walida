using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PhonePhotoCapturer : MonoBehaviour
{
    [Header("Capture References")]
    [SerializeField] private Camera photoCamera;
    [SerializeField] private Transform viewfinderTransform;
    [SerializeField] private GameObject phoneVisualRoot;
    [SerializeField] private SpriteRenderer shutterFlashRenderer;
    [SerializeField] private PanoramaEdgeScroller edgeScroller;

    [Header("Preview UI")]
    [SerializeField] private GameObject previewContainer;
    [SerializeField] private RawImage previewImage;

    [Header("Capture Settings")]
    [SerializeField] private Vector2 viewfinderWorldSize = new Vector2(2.6f, 4.4f);
    [SerializeField] private int photoPixelWidth = 540;
    [SerializeField] private float flashDuration = 0.2f;
    [SerializeField] private float captureCooldown = 0.25f;
    [SerializeField] private bool allowSpaceKeyCapture = true;

    [Header("Storage Settings")]
    [SerializeField] private bool savePhotosToDisk = true;
    [SerializeField] private string saveSubFolder = PhotoStorage.DefaultFolderName;
    [SerializeField] private string filePrefix = "photo";

    private readonly List<Texture2D> _capturedPhotos = new List<Texture2D>();
    private readonly List<string> _savedPhotoPaths = new List<string>();
    private float _flashTimer;
    private float _nextCaptureTime;

    public event Action<Texture2D> OnPhotoCaptured;
    public event Action<Texture2D, string> OnPhotoSaved;
    public event Action<PhotographableCharacter, Texture2D> OnCharacterPhotographed;

    public bool IsCaptureEnabled { get; set; } = true;
    public IReadOnlyList<Texture2D> CapturedPhotos => _capturedPhotos;
    public IReadOnlyList<string> SavedPhotoPaths => _savedPhotoPaths;
    public Texture2D LastCapturedPhoto => _capturedPhotos.Count > 0 ? _capturedPhotos[_capturedPhotos.Count - 1] : null;
    public string LastSavedPhotoPath => _savedPhotoPaths.Count > 0 ? _savedPhotoPaths[_savedPhotoPaths.Count - 1] : null;

    private void Awake()
    {
        if (photoCamera != null)
        {
            photoCamera.enabled = false;
        }

        SetFlashAlpha(0f);

        if (previewContainer != null)
        {
            previewContainer.SetActive(false);
        }
    }

    private void Update()
    {
        UpdateShutterFlash();

        if (!IsCaptureEnabled || Time.unscaledTime < _nextCaptureTime)
        {
            return;
        }

        bool leftClicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool spacePressed = allowSpaceKeyCapture && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

        if (leftClicked || spacePressed)
        {
            CapturePhoto();
        }
    }

    public Texture2D CapturePhoto()
    {
        if (photoCamera == null || viewfinderTransform == null)
        {
            return null;
        }

        if (viewfinderWorldSize.x <= 0.01f || viewfinderWorldSize.y <= 0.01f)
        {
            return null;
        }

        _nextCaptureTime = Time.unscaledTime + captureCooldown;

        int width = Mathf.Max(32, photoPixelWidth);
        int height = Mathf.Max(32, Mathf.RoundToInt(width * (viewfinderWorldSize.y / viewfinderWorldSize.x)));

        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        rt.Create();

        Transform camTransform = photoCamera.transform;
        camTransform.position = new Vector3(viewfinderTransform.position.x, viewfinderTransform.position.y, -10f);
        camTransform.rotation = viewfinderTransform.rotation;

        photoCamera.orthographic = true;
        photoCamera.orthographicSize = viewfinderWorldSize.y * 0.5f;
        photoCamera.aspect = viewfinderWorldSize.x / viewfinderWorldSize.y;
        photoCamera.targetTexture = rt;

        bool hadVisualActive = phoneVisualRoot != null && phoneVisualRoot.activeSelf;
        if (phoneVisualRoot != null)
        {
            phoneVisualRoot.SetActive(false);
        }

        photoCamera.Render();

        if (phoneVisualRoot != null)
        {
            phoneVisualRoot.SetActive(hadVisualActive);
        }

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D photo = new Texture2D(width, height, TextureFormat.RGBA32, false);
        photo.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        photo.Apply();

        RenderTexture.active = previousActive;
        photoCamera.targetTexture = null;
        rt.Release();
        Destroy(rt);

        _capturedPhotos.Add(photo);

        if (savePhotosToDisk)
        {
            string savedPath = PhotoStorage.SavePhoto(photo, filePrefix, saveSubFolder);
            if (!string.IsNullOrEmpty(savedPath))
            {
                _savedPhotoPaths.Add(savedPath);
                OnPhotoSaved?.Invoke(photo, savedPath);
            }
        }

        if (previewImage != null)
        {
            previewImage.texture = photo;
        }

        if (previewContainer != null)
        {
            previewContainer.SetActive(true);
        }

        _flashTimer = flashDuration;
        SetFlashAlpha(0.85f);

        OnPhotoCaptured?.Invoke(photo);

        PhotographableCharacter capturedCharacter = DetectPhotographedCharacter();
        if (capturedCharacter != null)
        {
            capturedCharacter.NotifyPhotographed(photo);
            OnCharacterPhotographed?.Invoke(capturedCharacter, photo);
        }

        return photo;
    }

    private PhotographableCharacter DetectPhotographedCharacter()
    {
        PhotographableCharacter[] characters = FindObjectsByType<PhotographableCharacter>(FindObjectsInactive.Exclude);
        if (characters == null || characters.Length == 0)
        {
            return null;
        }

        float wrapWidth = edgeScroller != null && edgeScroller.LoopHorizontally ? edgeScroller.PanoramaWidth : 0f;
        PhotographableCharacter bestMatch = null;
        float bestCoverage = 0f;

        for (int i = 0; i < characters.Length; i++)
        {
            PhotographableCharacter candidate = characters[i];
            if (candidate == null || !candidate.CanBePhotographed || !candidate.TryGetWorldBounds(out Bounds bounds))
            {
                continue;
            }

            float coverage = EvaluateBoundsCoverage(bounds, 0f);
            if (wrapWidth > 0.01f)
            {
                coverage = Mathf.Max(coverage, EvaluateBoundsCoverage(bounds, -wrapWidth));
                coverage = Mathf.Max(coverage, EvaluateBoundsCoverage(bounds, wrapWidth));
            }

            if (coverage >= candidate.RequiredCoverage && coverage > bestCoverage)
            {
                bestCoverage = coverage;
                bestMatch = candidate;
            }
        }

        return bestMatch;
    }

    private float EvaluateBoundsCoverage(Bounds bounds, float offsetX)
    {
        float halfW = viewfinderWorldSize.x * 0.5f;
        float halfH = viewfinderWorldSize.y * 0.5f;
        float minX = bounds.min.x + offsetX;
        float maxX = bounds.max.x + offsetX;
        float minY = bounds.min.y;
        float maxY = bounds.max.y;

        int insideCount = 0;
        const int gridSteps = 3;
        const int totalPoints = gridSteps * gridSteps;

        for (int gx = 0; gx < gridSteps; gx++)
        {
            float tx = gx / (float)(gridSteps - 1);
            float wx = Mathf.Lerp(minX, maxX, tx);

            for (int gy = 0; gy < gridSteps; gy++)
            {
                float ty = gy / (float)(gridSteps - 1);
                float wy = Mathf.Lerp(minY, maxY, ty);

                Vector3 localPoint = viewfinderTransform.InverseTransformPoint(new Vector3(wx, wy, viewfinderTransform.position.z));
                if (Mathf.Abs(localPoint.x) <= halfW && Mathf.Abs(localPoint.y) <= halfH)
                {
                    insideCount++;
                }
            }
        }

        return insideCount / (float)totalPoints;
    }

    private void UpdateShutterFlash()
    {
        if (_flashTimer <= 0f)
        {
            return;
        }

        _flashTimer -= Time.unscaledDeltaTime;
        float normalized = flashDuration > 0f ? Mathf.Clamp01(_flashTimer / flashDuration) : 0f;
        SetFlashAlpha(normalized * 0.85f);
    }

    private void SetFlashAlpha(float alpha)
    {
        if (shutterFlashRenderer == null)
        {
            return;
        }

        Color c = shutterFlashRenderer.color;
        c.a = alpha;
        shutterFlashRenderer.color = c;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _capturedPhotos.Count; i++)
        {
            if (_capturedPhotos[i] != null)
            {
                Destroy(_capturedPhotos[i]);
            }
        }

        _capturedPhotos.Clear();
    }
}
